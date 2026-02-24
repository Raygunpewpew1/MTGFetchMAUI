# CLAUDE.md — MTGFetchMAUI

This file provides guidance for AI assistants (Claude Code and others) working in this repository.

---

## Project Overview

**MTGFetchMAUI** is a .NET MAUI Android application for browsing, searching, and managing Magic: The Gathering card collections. It queries a local SQLite copy of the [MTGJSON](https://mtgjson.com/) database, renders card images fetched from the Scryfall CDN, and persists user collections in a separate SQLite database.

- **Target Platform**: Android (primary), Windows (partial)
- **Target Framework**: `net10.0-android`
- **Application ID**: `com.mtgfetch.mobile`
- **Architecture**: MVVM with Repository pattern and DI

---

## Repository Structure

```
MTGFetchMAUI/
├── App.xaml / App.xaml.cs          # Application root; global theme resources (dark Material theme)
├── AppShell.xaml / AppShell.xaml.cs # Tab-based navigation shell (Search / Collection / Stats)
├── MauiProgram.cs                   # DI container setup; registers all services, VMs, and pages
├── GlobalUsings.cs                  # Global using directives
│
├── Models/
│   ├── Card.cs                      # Core MTG card model (106+ properties; ~10,330 lines)
│   ├── CollectionItem.cs            # Represents a card in a user's collection
│   └── DeckTypes.cs                 # Deck-related type definitions
│
├── Data/
│   ├── DatabaseManager.cs           # Thread-safe SQLite connection manager (two DBs)
│   ├── CardRepository.cs            # Query layer for MTG master DB (cards, legalities, rulings)
│   ├── CollectionRepository.cs      # CRUD layer for user collection DB
│   ├── MTGSearchHelper.cs           # Fluent SQL query builder for card searches
│   ├── SQLQueries.cs                # Centralized SQL query string constants
│   └── CardMapper.cs                # Maps SQLite DataReader rows to Card objects
│
├── Services/
│   ├── CardManager.cs               # Facade coordinating repos, image service, and pricing
│   ├── ImageDownloadService.cs      # Async Scryfall CDN fetcher with rate limiting (120ms min)
│   ├── ImageCacheService.cs         # Orchestrates file image cache
│   ├── FileImageCache.cs            # File-based cache (500 MB limit, 90-day retention)
│   ├── CardPriceManager.cs          # Card pricing information
│   ├── CardPriceImporter.cs         # Imports price data
│   ├── SetSvgCache.cs               # SVG caching for set symbols
│   ├── ManaSvgCache.cs              # SVG caching for mana symbols
│   ├── ToastService.cs              # User-facing toast notifications
│   └── Logger.cs                    # Logging utility
│
├── ViewModels/
│   ├── BaseViewModel.cs             # ObservableObject base class
│   ├── SearchViewModel.cs           # Search logic: debounce (750ms), pagination (50/page), preload (6 items)
│   ├── CardDetailViewModel.cs       # Full card detail, legalities, rulings
│   ├── CollectionViewModel.cs       # Collection management UI logic
│   ├── StatsViewModel.cs            # Collection statistics
│   └── LoadingViewModel.cs          # App initialization / splash screen
│
├── Views/Pages/
│   ├── SearchPage.xaml/.cs          # Main card search UI (UraniumUI Material Design inputs)
│   ├── CardDetailPage.xaml/.cs      # Card detail display
│   ├── SearchFiltersPage.xaml/.cs   # Advanced search filter UI
│   ├── CollectionPage.xaml/.cs      # User collection management
│   ├── StatsPage.xaml/.cs           # Statistics and analytics
│   └── LoadingPage.xaml/.cs         # Splash/loading screen
│
├── Controls/
│   ├── CardGrid.cs                  # High-performance custom card grid (gesture + scroll)
│   ├── CardGridRenderer.cs          # SkiaSharp-based rendering engine
│   ├── CardGridGestureHandler.cs    # Tap, long-press, and scroll events
│   ├── CardTextView.cs              # Custom text view (Android handler)
│   ├── ManaCostView.cs              # Mana cost symbol strip
│   ├── ManaSymbolView.cs            # Individual mana symbol renderer
│   ├── CollectionAddSheet.xaml/.cs  # Bottom sheet for adding cards to collection
│   ├── Snackbar.xaml/.cs            # Toast/snackbar notification UI
│   └── GridCardData.cs              # Card data structure for grid rendering
│
├── Core/
│   ├── Enums.cs                     # Central enum definitions (see below)
│   ├── ColorIdentity.cs             # Card color identity analysis helpers
│   ├── CardLegalities.cs            # Format legality data per card
│   ├── CardRuling.cs                # Card ruling data
│   ├── CardTypeInfo.cs              # Card type classification helpers
│   ├── ScryfallCDN.cs               # Scryfall API URL helpers
│   ├── SearchOptions.cs             # Search filter options model
│   └── Layout/
│       ├── GridLayoutEngine.cs      # Column count and cell sizing logic
│       └── GridState.cs             # State management for grid rendering
│
├── Constants/
│   └── MTGConstants.cs              # All magic string constants: card types, lands, keywords,
│                                    #   image sizes, DB download URL, file paths
│
├── Resources/
│   ├── Fonts/                       # Bundled fonts
│   ├── Images/                      # App icon, splash screen, asset images
│   └── Raw/                         # Raw resource files
│
├── Platforms/
│   ├── Android/
│   │   └── AndroidManifest.xml      # Permissions: Network, Internet, External Storage, Camera
│   └── Windows/
│       └── Package.appxmanifest     # Windows app manifest
│
├── MTGFetchMAUI.Tests/
│   ├── MTGFetchMAUI.Tests.csproj    # xUnit test project linked to main project source files
│   ├── MTGSearchHelperTests.cs      # Tests for the fluent SQL query builder
│   └── Core/Layout/
│       └── GridLayoutEngineTests.cs # Tests for grid layout calculation
│
├── .github/workflows/
│   └── main.yml                     # GitHub Actions: weekly MTG DB update & publish to Releases
│
├── TODO.md                          # Project roadmap (see below)
└── MTGFetchMAUI.sln                 # Solution file
```

---

## Architecture and Key Patterns

### MVVM
Pages bind to ViewModels via MAUI data binding. ViewModels expose `ObservableProperty` and `RelayCommand` members. Pages have minimal code-behind.
**Use CommunityToolkit.Mvvm source generators.**
- Inherit from `ObservableObject`.
- Use `[ObservableProperty]` for backing fields to generate properties.
- Use `[RelayCommand]` on methods to generate `ICommand` properties.
- Use `[NotifyPropertyChangedFor]` to notify dependent properties.
- Mark ViewModel classes as `partial`.

### Repository Pattern
- `CardRepository` — read-only queries against the MTG master database.
- `CollectionRepository` — CRUD operations on the user's collection database.
- Both are coordinated by `CardManager` (facade).

### Dependency Injection
All services, repositories, and ViewModels are registered in `MauiProgram.cs`.
- **Singleton**: `DatabaseManager`, `CardManager`, image caches, repositories (long-lived, stateful).
- **Transient**: Modal/overlay ViewModels (e.g., `CollectionAddSheet`).

### Fluent Query Builder
`MTGSearchHelper` constructs parameterized SQLite queries. Always add new search predicates through this class rather than writing raw SQL inline.

### Custom Rendering
The card grid (`CardGrid`, `CardGridRenderer`) uses SkiaSharp for high-performance rendering. Do not replace with MAUI `CollectionView` without careful performance benchmarking.

---

## Application Startup Flow

```
App (App.xaml.cs)
  └─► LoadingPage
        └─► LoadingViewModel.OnAppearing()
              └─► CardManager.InitializeAsync()
                    ├─► DatabaseManager.ConnectAsync()
                    │     ├─► Connect to MTG master DB (read-only SQLite)
                    │     └─► Connect/create Collection DB (read-write SQLite)
                    └─► (Collection tables created if absent)
  └─► AppShell (tab navigation)
        ├─► Search Tab  → SearchPage
        ├─► Collection Tab → CollectionPage
        └─► Stats Tab → StatsPage
```

---

## Database Strategy

| Database | Access Mode | Purpose |
|---|---|---|
| MTG master (`MTG_App_DB.zip`) | Read-only | All card data from MTGJSON |
| Collection DB | Read-write | User's personal card collection |

- Both databases use SQLite via `Microsoft.Data.Sqlite` and `Dapper`.
- `DatabaseManager` uses a `SemaphoreSlim` to guard concurrent access.
- MTG master DB is downloaded automatically on first launch from the GitHub Releases URL defined in `MTGConstants.MTGDatabaseUrl`.
- The master DB is updated weekly via GitHub Actions (drops `cardForeignData`, runs `VACUUM`, zips, publishes).

---

## Key Enums (`Core/Enums.cs`)

| Enum | Values |
|---|---|
| `LegalityStatus` | Legal, NotLegal, Banned, Restricted |
| `CardRarity` | Common, Uncommon, Rare, MythicRare, Special, Bonus |
| `CardLayout` | Normal, Split, Flip, Transform, ModalDfc, Meld, Leveler, Class, Saga, Planar, Scheme, Vanguard, Token, DoubleFacedToken, Emblem, Augment, Host, ArtSeries, ReversibleCard |
| `DeckFormat` | Standard, Pioneer, Modern, Legacy, Vintage, Commander, Pauper |
| `MtgColor` | White, Blue, Black, Red, Green, Colorless, Multicolor |
| `CommanderArchetype` | (various archetypes) |

Always use these enums instead of magic strings.

---

## Image Loading Pipeline

```
Request card image
  └─► FileImageCache.TryGet()   → return cached bytes if hit
  └─► ImageDownloadService.FetchAsync()
        ├─► Rate limited (120ms minimum interval between requests)
        ├─► Cancellable via CancellationToken (generation-based)
        └─► On success: store in FileImageCache
```

- File cache: up to **500 MB**, **90-day** retention.
- Rate limiting prevents hammering Scryfall's CDN.
- Image requests in `SearchViewModel` use a **6-item preload buffer** ahead of visible items.

---

## Search and Pagination

- `SearchViewModel` debounces user input by **750ms** before querying.
- Results are paginated at **50 cards per page**.
- `MTGSearchHelper` is the only place SQL predicates should be assembled.
- Always add parameterized predicates (never string-interpolate user input into SQL).

---

## NuGet Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Maui.Controls` | 10.0.41 | MAUI framework |
| `CommunityToolkit.Maui` | 14.0.0 | MAUI community extensions |
| `CommunityToolkit.Mvvm` | 8.3.2 | MVVM Source Generators |
| `Microsoft.Data.Sqlite` | 10.0.3 | SQLite provider |
| `Dapper` | 2.1.66 | Micro-ORM for data mapping |
| `SkiaSharp` | 3.119.2 | 2D vector graphics |
| `SkiaSharp.Views.Maui.Controls` | 3.119.2 | SkiaSharp MAUI integration |
| `Svg.Skia` | 3.4.1 | SVG rendering via SkiaSharp |
| `UraniumUI.Material` | 2.14.0 | Material Design UI components |
| `UraniumUI.Icons.FontAwesome` | 2.14.0 | Font Awesome icon set |
| `Plugin.Maui.OCR` | 1.1.1 | OCR (Android only, ML Kit) |

> **Note**: Syncfusion was evaluated and rejected due to licensing costs. UraniumUI (MIT) is the chosen UI library.

---

## Testing

- **Framework**: xUnit 2.9.3 with Coverlet for coverage.
- **Location**: `MTGFetchMAUI.Tests/`
- **Approach**: The test project uses `<Compile Include>` links to pull in specific source files from the main project rather than referencing the project as a whole (avoids MAUI platform dependencies in tests).
- **Existing tests**: `MTGSearchHelperTests`, `GridLayoutEngineTests`.
- When adding new logic to `MTGSearchHelper`, `GridLayoutEngine`, or other pure C# utilities, add corresponding xUnit tests.
- Platform-specific code (MAUI controls, Android handlers) is not covered by unit tests.

### Running Tests

```bash
dotnet test MTGFetchMAUI.Tests/MTGFetchMAUI.Tests.csproj
```

---

## CI/CD — GitHub Actions

**Workflow**: `.github/workflows/main.yml`

**Trigger**: Every Tuesday at 12:00 UTC (`cron: '0 12 * * 2'`) and manual dispatch.

**Steps**:
1. Download latest MTGJSON SQLite database.
2. Drop `cardForeignData` table (size reduction).
3. Run `VACUUM` to compress.
4. Zip as `MTG_App_DB.zip`.
5. Publish to GitHub Releases with a date-based tag and mark as latest.

The app downloads this artifact from `MTGConstants.MTGDatabaseUrl` on first launch.

---

## Development Conventions

### General
- C# preview language features are enabled (`<LangVersion>preview</LangVersion>`).
- Nullable reference types are enabled (`<Nullable>enable</Nullable>`).
- Implicit usings are enabled; add project-wide usings to `GlobalUsings.cs`.
- XAML source generation (`SourceGen`) is enabled — prefer it over `x:Name` code-behind lookups.

### Naming
- ViewModels: `<Feature>ViewModel.cs` in `ViewModels/`.
- Pages: `<Feature>Page.xaml` in `Views/Pages/`.
- Services: descriptive noun + `Service` or `Manager` suffix in `Services/`.
- SQL queries: defined as `const string` in `SQLQueries.cs`.
- Application-wide string constants: defined in `MTGConstants.cs`.

### Adding a New Feature
1. Define any new data in `Models/`.
2. Add data access in `CardRepository` or `CollectionRepository` (and SQL in `SQLQueries.cs`).
3. Expose functionality through `CardManager` if it spans multiple services.
4. Create a ViewModel in `ViewModels/` extending `BaseViewModel` (use `ObservableObject` and source generators).
5. Build the page in `Views/Pages/` and bind to the ViewModel.
6. Register all new types in `MauiProgram.cs`.
7. Add tab or route to `AppShell.xaml` if navigation is needed.

### Thread Safety
- Always acquire the `SemaphoreSlim` in `DatabaseManager` before database operations.
- Use `CancellationToken` for any async image or search operation.
- Do not access UI elements from background threads; use `MainThread.BeginInvokeOnMainThread` when necessary.

### SQL Safety
- **Never interpolate user input into SQL strings.** Always use parameterized queries via `Dapper` or `MTGSearchHelper`'s parameter dictionary.

### Custom Controls
- The `CardGrid` and `CardGridRenderer` are SkiaSharp-based. Modifications require understanding the `GridLayoutEngine` and `GridState` classes.
- Do not add MAUI-heavy controls inside the SkiaSharp canvas; delegate rendering to `CardGridRenderer`.

---

## Roadmap (`TODO.md` Summary)

| Area | Status |
|---|---|
| Material Design inputs via UraniumUI | Done |
| Deck building feature | Not started |

---

## Common Gotchas

- The MTG master database is **read-only**. Never attempt write operations on it.
- `CardPriceManager` and `CardPriceImporter` are present but may not be fully wired up — verify before relying on pricing data.
- `Plugin.Maui.OCR` is Android-only; any OCR code path must be guarded with `#if ANDROID` or runtime platform checks.
- The `Card` model (`Card.cs`) is very large (~10,330 lines). Use `CardMapper.cs` to map new database columns rather than modifying raw read logic scattered across the class.
- Image caching relies on `FileImageCache`. If cache behavior seems wrong, check there.
