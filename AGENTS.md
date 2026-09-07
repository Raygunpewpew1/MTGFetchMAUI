# AGENTS.md — AetherVault

Guidance for AI assistants (Cursor, Claude Code, Jules, Copilot, etc.) working in this repository.

**Read and follow this file before suggesting or applying changes.**

---

## Project overview

**AetherVault** is a .NET MAUI Android application for browsing, searching, and managing Magic: The Gathering card collections. It queries a local SQLite copy of the [MTGJSON](https://mtgjson.com/) database, renders card images from the Scryfall CDN, and persists user collections in a separate SQLite database.

- **Target platform**: Android
- **Target framework**: `net10.0-android`
- **Application ID**: `com.aethervault.mobile`
- **Architecture**: MVVM, repository pattern, DI

---

## Repository structure (representative)

The tree below is a high-level map; additional `Pages/`, `Views/`, `ViewModels/`, and services exist (e.g. decks, settings, import/export).

```
AetherVault/
├── App.xaml / App.xaml.cs          # Root; global theme (dark Material)
├── AppShell.xaml / AppShell.xaml.cs # Tab shell (Search / Collection / Stats, etc.)
├── MauiProgram.cs                   # DI — services, repos, VMs, pages
├── GlobalUsings.cs
│
├── Models/                          # Card, collection, deck entities
├── Data/                            # DatabaseManager, repos, MTGSearchHelper, SQLQueries, CardMapper
├── Services/                        # AppDataManager, CardManager, image cache/download, pricing, SVG caches,
│   └── DeckBuilder/                 # DeckBuilderService, DeckValidator
├── ViewModels/
├── Pages/                           # Search, CardDetail, SearchFiltersSheet, Collection, Stats, Loading,
│                                    #   CollectionAdd, decks, settings, etc.
├── Controls/                        # CardGrid + Skia renderer, mana views, SwipeGestureContainer, …
├── Core/                            # Enums, SearchOptions, layout engine, Scryfall helpers, …
├── Constants/                       # MTGConstants (partial; release URLs generated at build — AetherVault.Local.props)
├── docs/                            # e.g. MTGSQLiteCardsSchemaNotes.md
├── Assets/SVGSets/                  # Embedded set symbol SVGs
├── Platforms/Android/
├── AetherVault.Tests/               # xUnit; linked source files, not a project reference
├── .github/workflows/               # main.yml (weekly DB), prices-daily.yml (rolling prices)
├── TODO.md
└── AetherVault.sln
```

---

## Architecture and patterns

### MVVM (CommunityToolkit.Mvvm)

Pages bind to ViewModels; use `[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`; ViewModels are `partial` and inherit `ObservableObject` or **`BaseViewModel`**.

- **Status UI**: `BaseViewModel` exposes `StatusMessage` and `StatusIsError`. Prefer these over ad hoc status properties in feature VMs.

### Repository pattern

- `ICardRepository` / `CardRepository` — read-only MTG master DB.
- `ICollectionRepository` / `CollectionRepository` — user collection DB.
- `IDeckRepository` / `DeckRepository` — decks in the collection DB.
- **`CardManager`** coordinates cross-cutting work.

Register repos via **interfaces** in `MauiProgram.cs` for testability.

### Dependency injection (`MauiProgram.cs`)

- **Singleton**: `DatabaseManager`, `CardManager`, image caches, repos, `CardGalleryContext`, etc.
- **Transient**: Modal pages and their VMs (e.g. `CardDetailPage`, `LoadingPage`).

### Fluent SQL

Add search predicates only through **`MTGSearchHelper`** (parameterized). Never interpolate user input into SQL.

### Custom rendering

`CardGrid` / `CardGridRenderer` use **SkiaSharp**. Do not swap for `CollectionView` without benchmarking.

### Swipe between cards

`CardGalleryContext` holds the ordered UUID list; `SwipeGestureContainer` on `CardDetailPage` uses **AppoMobi.Maui.Gestures** for swipe navigation.

### Database strategy

| Database | Mode | Role |
|----------|------|------|
| MTG master (`MTG_App_DB.zip`) | Read-only | MTGJSON card data |
| Collection DB | Read-write | Collection + decks |

- **`cards` quirks** (`availability` comma-separated vs JSON, etc.): [`docs/MTGSQLiteCardsSchemaNotes.md`](docs/MTGSQLiteCardsSchemaNotes.md).
- **Cross-DB**: `ATTACH` the collection DB on the MTG connection (`AS col`); use `col.my_collection` (and similar) in SQL.
- **Dapper**: Prefer `ExecuteAsync` / `QueryAsync` on `SqliteConnection` over manual commands.
- **`ExecuteReaderAsync`**: Returns `DbDataReader`, not `SqliteDataReader` — use `DbDataReader` in signatures/casts (see `CardMapper`).
- **`UNION ALL` cards + tokens**: Align columns; pad with `NULL AS …`. Use **`SQLQueries.BaseCardsAndTokens`** for UUID lookups across both tables.

### Navigation and DI details

- Prefer **`Application.Current!.Windows[0].Page!`** or Shell — avoid `Application.Current.MainPage` for new code.
- Resolving transient modals from a VM: inject **`IServiceProvider`**, use `GetService<TPage>()` (not a service locator elsewhere).
- **Search filters**: `Pages/SearchFiltersSheet` + **`SearchFiltersViewModel`**, opened via **`ISearchFiltersOpener`** / **`SearchFiltersOpenerService`** (transient). There is no `SearchFiltersPage` (obsolete name).

### Release download URLs

`MtgConstants.DatabaseDownloadUrl`, `PricesBundleDownloadUrl`, and `PricesBundleMetaUrl` are **generated at build**. Override defaults with **`AetherVault.Local.props`** (gitignored); see **`AetherVault.Local.props.example`**.

---

## Application startup flow

```
App → LoadingPage → LoadingViewModel.OnAppearing()
  → CardManager.InitializeAsync()
      → AppDataManager (download/validate MTG DB)
      → DatabaseManager.ConnectAsync() (MTG RO + collection RW; create tables if needed)
  → AppShell → Search / Collection / Stats (and other tabs/routes)
```

---

## Key enums (`Core/Enums.cs`)

Use enums, not magic strings: `LegalityStatus`, `CardRarity`, `CardLayout`, `DeckFormat`, `MtgColor`, `CommanderArchetype`, etc.

---

## Image loading pipeline

`FileImageCache.TryGet` → on miss, **`ImageDownloadService.FetchAsync`** (rate-limited, **120 ms** min interval, cancellable) → write cache. Cache cap **~500 MB**, **90-day** retention. **`SearchViewModel`** preloads **6** images ahead of the visible range.

---

## Search and pagination

- **Debounce**: **750 ms** (`SearchViewModel`).
- **Page size**: **50** cards.
- All predicates via **`MTGSearchHelper`** + parameters.

---

## UI and MAUI guidelines

- **XAML namespaces**: e.g. `CardRuling` (`AetherVault.Core`), `PriceEntry` (`AetherVault.Services`) — add correct `xmlns`.
- **Dynamic lists**: `BindableLayout.ItemsSource` to VM collections, not imperative view construction in code-behind.
- **`<Frame>`** is obsolete on .NET 9+ → use **`<Border>`** (`Stroke`, `StrokeShape`).
- **UraniumUI** `TextField` / `PickerField`: global `Entry` **BackgroundColor** `Transparent` in `App.xaml` avoids stray fills/shadows.
- **CheckBox columns**: prefer **`Grid`** over `FlexLayout` for alignment.
- **DataTemplate → parent command**: `Source={RelativeSource AncestorType={x:Type viewmodels:…}}` to avoid MAUIG2045 / reflection binding.
- **Deck editor (`DeckDetailPage`)**: two tabs — **Cards** (`Views/DeckCardsTabView`) and **Insights** (`Views/DeckStatsTabView`). One grouped CollectionView shows Commander → main type groups → Sideboard (`DeckDetailViewModel.CombinedDeckGroups`); the header shows commander slot + build progress; **`DeckNextStepAdvisor`** (pure, tested) produces the actionable "next steps" chips. There is no per-tab layout mode and no `DeckMainTabView`/`DeckSideboardTabView`/`DeckCommanderTabView` (obsolete names).
- **Deck list rows (`DeckCardsTabView`)**: use **`SelectionMode="Single"`** and **`SelectionChanged`** for tap-to-detail; **`SwipeView`** was removed (it stole width from names and fought ±). Move/remove: ⋯ on the thumbnail; commander rows hide the ± stepper (`ShowQuantityControls`).
- **Add-cards modal (`DeckAddCardsPage`)**: suggestions auto-load for commander decks with a commander set; theme/curated-list/strategy chips are inline under the search bar (the browse popup was removed — obsolete name `DeckAddCardsBrowsePopup`). Target section is passed via `DeckDetailViewModel.RequestAddCardsForSection` / `ConsumePendingAddModalTargetSection`.

### SkiaSharp (`CardGridRenderer`)

- Cache `SKPaint` (etc.) on the type; dispose in `Dispose()`.
- Guard **zero** width/height in `OnPaintSurface`; try/catch render path to avoid black screens.
- **`EnableTouchEvents="True"`** + `Touch` handler; `e.Handled = true`; nullable `object? sender`.

### Threading

- UI-bound **`ObservableCollection`** updates from background work → **`MainThread.BeginInvokeOnMainThread`**.
- Heavy CPU / bulk DB → **`Task.Run`** where appropriate.

---

## Performance and data behavior

- Filter/sort large sets in **SQLite**, not large in-memory LINQ.
- Short fixed strings: prefer **`string.Create`** / `Span<char>` when it matters.
- Cache **`Enum.GetValues<T>()`** in `static readonly` in hot paths.
- Prices: **`CardManager.GetCardPricesBulkAsync`** + **`CardGrid.UpdateCardPricesBulk`** for visible rows.
- Rapid filter/search: **`CancellationTokenSource`** — cancel/recreate on new input (`CollectionViewModel`).
- **`GridLayoutEngine`**: `visibleEnd` = `lastRow * columns - 1` (spacing included in `lastRow` math).
- **`CardGrid.SetCollection`**: may drop async-loaded prices — re-query **`GetVisibleRange()`** and reload prices on MainThread.
- Large grid loads: **`SetCollectionAsync`** not `SetCollection`.
- **Related tokens** → `Card.RelatedFaces` / carousel behavior in **`CardDetailViewModel.GetFullCardPackageAsync`**.
- **Price JSON**: deep null-conditional access.
- **File read**: prefer **`File.ReadAllText`** in try/catch over `File.Exists` + read.

---

## Testing

- **xUnit** + Coverlet; **`AetherVault.Tests`** links sources via `<Compile Include="…" Link="…">` (no MAUI project reference).
- Add tests for pure logic (`MTGSearchHelper`, `GridLayoutEngine`, deck builder, import/export, etc.).
- Run: `dotnet test AetherVault.Tests/AetherVault.Tests.csproj` (avoids full solution / Android workload when not needed).

---

## CI/CD (GitHub Actions)

- **`main.yml`**: Weekly MTGJSON SQLite → trim (e.g. drop heavy tables), **FTS5** + indexes, **VACUUM**, zip **`MTG_App_DB.zip`**, dated release, **latest**.
- **`prices-daily.yml`**: Trimmed prices → rolling tag **`aethervault-prices-rolling`** (not latest).

---

## NuGet (verify versions in `AetherVault.csproj`)

| Package | Purpose |
|---------|---------|
| Microsoft.Maui.Controls | MAUI |
| CommunityToolkit.Maui / Mvvm | UI helpers, MVVM generators |
| Microsoft.Data.Sqlite, Dapper | SQLite access |
| SkiaSharp, Svg.Skia | Grid rendering, SVG |
| UraniumUI.Material, UraniumUI.Icons.FontAwesome | Material UI |
| AppoMobi.Maui.Gestures | Swipe |
| CsvHelper | Import/export CSV |
| Microcharts.Maui | Charts (e.g. Stats) |

UraniumUI (MIT) replaced Syncfusion for licensing reasons.

---

## Conventions and gotchas

- **C#**: preview language, nullable enabled, implicit usings; XAML **SourceGen** where enabled.
- **Naming**: `*ViewModel`, `*Page`, `*Service`/`*Manager`, `I*Repository`, SQL in **`SQLQueries`**, app strings in **`MtgConstants`** (where appropriate).
- **New feature**: Model → `IRepository` + impl → SQL → `CardManager` if needed → VM → Page → register in **`MauiProgram`** / **`AppShell`**.
- **DB**: always **`DatabaseManager`** semaphore; **never** write to the MTG master DB.
- **Pricing**: `CardPriceManager` / related — confirm wiring for the flow you touch.
- **`Card`**: map new columns in **`CardMapper`**.
- **SVG**: **`SvgCacheEngine`** base for **`ManaSvgCache`** / **`SetSvgCache`** — don’t duplicate loading logic.
- **Downloads**: **`AppDataManager`** owns MTG DB fetch/versioning.
- **Modal pages**: `TaskCompletionSource` with **`RunContinuationsAsynchronously`**; **`TrySetResult`** in **`OnDisappearing`** (back button).
- **Modal leaks**: unsubscribe shared events in **`OnDisappearing`**.
- **`CardGrid.IsDragEnabled`**: when false, drag may end as **long-press**.
- **Collection `AvgCMC`**: non-lands only.
- **Trade** feature removed (stability).
- **Formatting**: prefer storage sizes as **MB with one decimal** (e.g. `123.4 MB`).
- **Collection expressions** (`[.. ]`) where they clarify typed collection init.

---

## Roadmap (see `TODO.md`)

| Area | Status |
|------|--------|
| UraniumUI Material inputs | Done |
| Deck building | In progress |

---

By following this document you reduce regressions, UI threading issues, and SQL/grid performance problems in AetherVault.
