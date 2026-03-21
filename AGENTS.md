# AGENTS.md — AetherVault

This file provides crucial guidance and operational instructions for AI assistants (Jules, Claude, etc.) working in this repository.

**Always read and adhere to these guidelines before suggesting or applying changes.**

---

## Project Overview

**AetherVault** is a .NET MAUI Android application for browsing, searching, and managing Magic: The Gathering card collections. It queries a local SQLite copy of the [MTGJSON](https://mtgjson.com/) database, renders card images fetched from the Scryfall CDN, and persists user collections in a separate SQLite database.

- **Target Platform**: Android (primary), Windows (partial)
- **Target Framework**: `net10.0-android`
- **Application ID**: `com.aethervault.mobile`
- **Architecture**: MVVM with Repository pattern and DI

---

## 🏗️ Architecture and Design Patterns

### MVVM with CommunityToolkit.Mvvm
Pages bind to ViewModels via MAUI data binding. ViewModels expose `ObservableProperty` and `RelayCommand` members. Pages have minimal code-behind.
**Use CommunityToolkit.Mvvm source generators.**
- Inherit from `ObservableObject` or `BaseViewModel`.
- Use `[ObservableProperty]` for backing fields to generate properties.
- Use `[RelayCommand]` on methods to generate `ICommand` properties.
- Use `[NotifyPropertyChangedFor]` to notify dependent properties.
- Mark ViewModel classes as `partial`.
- **Status Messages**: `BaseViewModel` exposes `StatusMessage` and `StatusIsError` properties for consistent UI feedback across the app. Child ViewModels should utilize these rather than defining custom status text/color properties.

### Database Strategy
- **MTG master (`MTG_App_DB.zip`)**: Read-only SQLite copy of MTGJSON data. Downloaded automatically on first launch via `AppDataManager`.
- **Collection DB**: Read-write SQLite DB for user collections and decks.
- **Cross-DB Queries**: To query across both (e.g., joining cards with `my_collection`), the collection database must be attached to the MTG connection (e.g., `ATTACH DATABASE '...' AS col`) and collection tables must be referenced with the `col.` prefix (e.g., `col.my_collection`).
- **ORM**: The project uses Dapper. Prefer Dapper's extension methods (e.g., `ExecuteAsync`, `QuerySingleAsync`, `QueryAsync`) on `SqliteConnection` over manual `SqliteCommand` and `SqlDataReader` instantiation.
- **Dapper Reader Types**: Dapper's `ExecuteReaderAsync` with SQLite returns a wrapped `System.Data.Common.DbDataReader`, not a provider-specific `SqliteDataReader`. Use `DbDataReader` for typecasts and method signatures (like in `CardMapper`) to prevent `InvalidOperationException` cast failures.
- **Unions**: When using `UNION ALL` to combine results from the `cards` and `tokens` tables in the MTGJSON SQLite database, explicitly map the columns of the `tokens` table and pad missing fields with `NULL AS [fieldName]` to match the exact schema expected by `CardMapper`.
  - To fetch both regular cards and tokens by UUID simultaneously, use `SQLQueries.BaseCardsAndTokens`, which performs a schema-aligned `UNION ALL` across tables.

### Navigation and DI
- **DI Container**: All services, repositories, and ViewModels are registered in `MauiProgram.cs`. Register repositories via their interfaces (`ICardRepository`, etc.) to support testing.
- **Navigation Context**: `Application.Current.MainPage` is obsolete in modern multi-window MAUI apps. Use `Application.Current!.Windows[0].Page!` or Shell navigation.
- **ViewModel Navigation**: To resolve transient modal pages from a ViewModel without a Service Locator anti-pattern, inject `IServiceProvider` directly into the ViewModel's constructor and use `_serviceProvider.GetService<TPage>()`.
- **Search filters UI**: Advanced search filters live in **`Pages/SearchFiltersSheet`** (Community Toolkit popup, UraniumUI fields), bound to **`SearchFiltersViewModel`**, opened via **`ISearchFiltersOpener`** / **`SearchFiltersOpenerService`**, registered transient in **`MauiProgram.cs`**. There is no `SearchFiltersPage`—that name is obsolete.

---

## 🖥️ UI & MAUI Guidelines

### XAML and Controls
- **Namespaces**: Data types used for XAML bindings are distributed across different namespaces (e.g., `CardRuling` in `AetherVault.Core`, `PriceEntry` in `AetherVault.Services`). XAML files must include the appropriate `clr-namespace` declarations (e.g., `xmlns:core` and `xmlns:services`).
- **Collection Binding**: When building dynamic UI lists in XAML, use declarative `BindableLayout.ItemsSource` bound to ViewModel collections rather than manually constructing and appending views via imperative C# in the code-behind.
- **Frame is Obsolete**: The `<Frame>` control is obsolete in .NET 9+. Replace with `<Border>`. Map properties: `BorderColor` -> `Stroke`, `CornerRadius="X"` -> `StrokeShape="RoundRectangle X"`.
- **UraniumUI Borders**: To prevent visual artifacts (unintended shadows/solid backgrounds) in UraniumUI's `TextField` and `PickerField` controls, the global `Style` for `Entry` in `App.xaml` must have its `BackgroundColor` set to `Transparent`.
- **Aligning Checkboxes**: When arranging UI elements with varying label lengths (like CheckBoxes) in multiple columns, use a `Grid` instead of a wrapping `FlexLayout` to maintain vertical and horizontal alignment.
- **Bindings in DataTemplates**: In .NET MAUI XAML, to avoid `MAUIG2045` reflection fallback warnings when binding to a parent ViewModel's command from within a `DataTemplate` (which has its own `x:DataType`), use `Source={RelativeSource AncestorType={x:Type viewmodels:ParentViewModel}}` rather than `Source={x:Reference ...}`.
- **Touch Conflicts**: Using a `TapGestureRecognizer` on `CollectionView` item templates that contain a `SwipeView` causes touch conflicts. Use `CollectionView.SelectionMode="Single"` and handle the `SelectionChanged` event instead.

### SkiaSharp Render Loop
- **Avoid Allocations**: To prevent GC pressure and frame drops in 60fps render loops (like `CardGridRenderer.cs`), avoid inline object allocations (e.g., `new SKPaint`). Cache these resources as class-level fields, initialize them in `EnsureResources()`, and clean them up in `Dispose()`.
- **Guard Zero Dimensions**: `SKCanvasView` paint events (e.g., `OnPaintSurface`) can trigger with zero width/height before layout is complete. Always guard against `w <= 0 || h <= 0` and use `try-catch` blocks during rendering to prevent silent UI crashes/black screens.
- **Touch Events**: To enable touch events on an `SKCanvasView`, the `EnableTouchEvents="True"` property must be set in XAML alongside the `Touch` event binding. In the event handler, verify `e.ActionType` (e.g., `SKTouchAction.Released`) and set `e.Handled = true`. With nullable reference types enabled, the sender parameter must be nullable (`object? sender`).

### UI Thread Management
- **MainThread Updates**: When updating `ObservableCollection` instances bound to the UI from background threads, wrap the updates in `MainThread.BeginInvokeOnMainThread` to prevent application crashes and black screens.
- **Background Processing**: When performing heavy data processing or bulk database operations (e.g., parsing CSV files, importing collections), always wrap the work in `Task.Run()` to offload it to a background ThreadPool thread.

---

## 🚀 Performance & Optimization

- **DB Down-level Filtering**: To minimize memory overhead when handling large datasets, push filtering and primary sorting operations down to the SQLite database level rather than using in-memory C# LINQ.
- **Span String Creation**: When building short, fixed-length strings (e.g., color identity symbols like 'WUBRG'), use `string.Create` with a `Span<char>` to perform a single allocation and populate characters directly.
- **Caching Reflection**: To optimize the use of `Enum.GetValues<T>` in performance-critical code paths, cache the array in a `static readonly` field to avoid reflection overhead.
- **Batch Data Loading**: To prevent N+1 performance issues when loading prices for visible grid items, use `CardManager.GetCardPricesBulkAsync` to query the SQLite database and `CardGrid.UpdateCardPricesBulk` to batch UI updates.
- **Cancel Stale Operations**: In ViewModels handling rapid user input (like `CollectionViewModel`), manage background tasks with a `CancellationTokenSource`. Cancel and recreate the token on new input to prevent overlapping operations.

---

## 📊 Data Models and Specific Logic

- **Grid Sizing**: In `GridLayoutEngine`, the `lastRow` calculation incorporates `config.CardSpacing` to determine the end of the viewport's buffer zone. The `visibleEnd` index is calculated as `lastRow * columns - 1` without additional increments.
- **Visible Range Updates**: When updating a `CardGrid`'s collection via `SetCollection`, asynchronously loaded data like prices may be lost. ViewModels must manually query `_grid.GetVisibleRange()` on the MainThread and trigger data reloading (like `LoadVisiblePrices`) after the collection updates.
- **Card Loading**: When providing large datasets to `CardGrid`, use `SetCollectionAsync` rather than `SetCollection` to offload `CardState` mapping to a background thread.
- **Tokens and Faces**: Cards are linked to their tokens via the `relatedCards` JSON array. `CardMapper` extracts this to populate `Card.RelatedCards`. In `CardDetailViewModel`, related cards are appended as additional 'faces' within `GetFullCardPackageAsync`, which the UI carousel handles when `Faces.Length > 1`.
- **Deep JSON Paths**: When traversing deeply nested MTGJSON price data (e.g., `prices.Paper.TCGPlayer.RetailNormal.Price`), utilize null-conditional (`?.`) and null-coalescing (`??`) operators.
- **File I/O**: Optimize file reads in C# when a file is expected to exist by directly calling `File.ReadAllText` inside a `try-catch` block (handling `IOException` and `UnauthorizedAccessException`) rather than using a preceding `File.Exists` check.

---

## 🛠️ Project Specifics & Quirks

- **AetherVault.Tests Linked Files**: The `AetherVault.Tests` project links source files from the main project (`Link="..."`) rather than using a direct project reference to avoid platform target incompatibilities. When writing unit tests for files not already included, manually add them to `AetherVault.Tests.csproj` as linked compile items.
- **Running Tests**: The development sandbox is frequently offline or restricted from reaching `api.nuget.org`. Commands like `dotnet restore` or `dotnet test` may time out. Target the test project directly (`dotnet test AetherVault.Tests/AetherVault.Tests.csproj`) rather than building the entire solution to bypass Android SDK workload errors in headless environments.
- **Formatting Preferences**: The user prefers file size or storage metrics to be displayed in Megabytes with one decimal place precision (e.g., '123.4 MB').
- **Removed Features**: The 'Trade' feature (TradePage, TradeViewModel, TradeTab) was temporarily removed due to stability issues.
- **Constants**: `Constants/MTGConstants.cs` is a centralized static class for application constants, serving as a port of the legacy `MTGConstants.pas` file.
- **TaskCompletionSource Deadlocks**: When utilizing a `TaskCompletionSource<T>` to return a result from a modal page, instantiate it with `TaskCreationOptions.RunContinuationsAsynchronously` to prevent UI deadlocks. Call `_tcs.TrySetResult(default)` in the page's `OnDisappearing()` to prevent hanging if dismissed via hardware back button.
- **Modal Page Leaks**: Always unsubscribe from events (e.g., `SearchCompleted`, `CardClicked`) in a modal page's `OnDisappearing()` method to prevent memory leaks.
- **Grid Drag and Drop**: The `CardGrid` `IsDragEnabled` property toggles drag-and-drop. When disabled, the internal handler blocks the `Dragging` state, causing the gesture to resolve as a `LongPressed` event upon release, even if movement occurred.
- **App Icons**: The application icon is configured using the `<MauiIcon>` element. `Resources/AppIcon/appicon.svg` serves as the base/background layer, while `Resources/AppIcon/appiconfg.svg` is used as the foreground layer with a transparent background.
- **Collection Stats**: Collection statistics `AvgCMC` is calculated using only non-land cards to avoid skewing the average with land cards.
- **CSV Handling**: The project utilizes the `CsvHelper` NuGet package for robust parsing and formatting of CSV files in the `AetherVault.Services.ImportExport` namespace.
- **Modern C#**: Use C# 12+ collection expressions (e.g., `[.. collection]`) instead of `.ToList()` or `.ToArray()` when initializing collections with explicitly typed targets.

---

By adhering to these rules, you will minimize crashes, performance bottlenecks, and regressions in AetherVault.
