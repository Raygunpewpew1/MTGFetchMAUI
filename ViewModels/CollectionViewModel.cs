using AetherVault.Controls;
using AetherVault.Core;
using AetherVault.Core.Layout;
using AetherVault.Models;
using AetherVault.Services;
using AetherVault.Services.ImportExport;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for the Collection tab. Loads the user's saved cards, supports sort/filter, import/export, and add/remove.
/// Binds to the same style of CardGrid as Search; data comes from CollectionRepository (user DB), not the MTG card DB.
/// </summary>
public partial class CollectionViewModel : BaseViewModel, ISearchFilterTarget
{
    private readonly CardManager _cardManager;
    private readonly ISearchFiltersOpener _filtersOpener;
    private readonly IGridPriceLoadService _gridPriceLoadService;
    private readonly CollectionImporter _importer;
    private readonly CollectionExporter _exporter;
    private CardGrid? _grid;
    private CollectionItem[] _allItems = [];
    private CollectionItem[] _cachedFilteredItems = [];
    private bool _gridApplyPending;
    private bool _hasLoadedOnce;
    private int _lastLoadedCollectionVersion = -1;

    /// <summary>Full price payloads for collection price sort — same pivot as grid bulk load.</summary>
    private readonly Dictionary<string, CardPriceData> _collectionSortPriceData = new(StringComparer.Ordinal);
    private readonly object _sortPriceLock = new();
    private readonly SemaphoreSlim _sortPriceLoadGate = new(1, 1);
    private int _sortPricesCollectionVersion = -1;
    private string _sortPricesVendorKey = "";
    private int _sortPriceInvalidationEpoch;

    // ── Bindable properties ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollectionStatsSummary))]
    public partial int TotalCards { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollectionStatsSummary))]
    public partial int UniqueCards { get; set; }

    [ObservableProperty]
    public partial bool IsCollectionEmpty { get; set; }

    [ObservableProperty]
    public partial CollectionSortMode SortMode { get; set; } = CollectionSortMode.Manual;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    public SearchOptions CurrentOptions { get; set; } = new();

    /// <summary>When true, only rows marked foil in the collection DB.</summary>
    [ObservableProperty]
    public partial bool CollectionFilterFoilOnly { get; set; }

    /// <summary>When true, only rows marked etched in the collection DB.</summary>
    [ObservableProperty]
    public partial bool CollectionFilterEtchedOnly { get; set; }

    /// <summary>
    /// Picker index for "minimum copies on this collection row": 0 = Any, 1 = 2+, 2 = 3+, … (threshold = index + 1 when index &gt; 0).
    /// Uses a picker instead of a stepper so two-way binding is reliable and we skip useless "≥1" (matches almost every row).
    /// </summary>
    [ObservableProperty]
    public partial int CollectionFilterMinQtyPickerIndex { get; set; }

    /// <summary>Labels for <see cref="CollectionFilterMinQtyPickerIndex"/> (Any, 2+, …, 30+).</summary>
    public IReadOnlyList<string> CollectionMinQtyPickerOptions { get; } =
        ["Any", .. Enumerable.Range(2, 29).Select(static i => $"{i}+")];

    public string FiltersButtonText
    {
        get
        {
            var merged = CurrentOptions.Clone();
            merged.NameFilter = SearchText?.Trim() ?? "";
            var count = merged.ActiveFilterCount;
            return count > 0 ? $"Filters ({count})" : "Filters";
        }
    }

    /// <summary>Labels for the sort-mode picker (must match <see cref="CollectionSortMode"/> order).</summary>
    public List<string> SortModeOptions { get; } =
    [
        "Manual",
        "Name",
        "CMC",
        "Rarity",
        "Color",
        "Price",
        "Set / #",
        "Date added",
        "Quantity",
        "Type",
        "CMC (high)",
        "Δ vs baseline",
    ];

    /// <summary>Single-line summary for the collection toolbar (card totals).</summary>
    public string CollectionStatsSummary => $"{TotalCards} cards · {UniqueCards} unique";

    public int SortModeIndex
    {
        get => (int)SortMode;
        set
        {
            if (value >= 0 && value < SortModeOptions.Count)
                SortMode = (CollectionSortMode)value;
        }
    }

    /// <summary>Raised when collection has been loaded so the UI can refresh (e.g. empty-state visibility).</summary>
    public event Action? CollectionLoaded;

    // Explicit IAsyncRelayCommand properties for XAML compiled bindings (avoids MAUIG2045 reflection fallback)
    public IAsyncRelayCommand ImportCollectionCommand { get; }

    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand ExportCollectionCommand { get; }

    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand OpenFiltersCommand { get; }

    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand ClearCollectionFiltersCommand { get; }

    /// <summary>Sets each row's stored baseline to the current retail unit price (overflow menu).</summary>
    public IAsyncRelayCommand RecapturePriceBaselinesCommand { get; }

    public CollectionViewModel(
        CardManager cardManager,
        ISearchFiltersOpener filtersOpener,
        IGridPriceLoadService gridPriceLoadService,
        CollectionImporter importer,
        CollectionExporter exporter)
    {
        _cardManager = cardManager;
        _filtersOpener = filtersOpener;
        _gridPriceLoadService = gridPriceLoadService;
        _importer = importer;
        _exporter = exporter;
        ImportCollectionCommand = new AsyncRelayCommand(ImportCollectionAsync);
        ExportCollectionCommand = new AsyncRelayCommand(ExportCollectionAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenFiltersCommand = new AsyncRelayCommand(OpenFiltersAsync);
        ClearCollectionFiltersCommand = new AsyncRelayCommand(ClearCollectionFiltersAsync);
        RecapturePriceBaselinesCommand = new AsyncRelayCommand(RecapturePriceBaselinesAsync);

        _cardManager.OnPriceSyncProgress += (msg, pct) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsImportingPrices = pct < 100;
            });
        };

        _cardManager.OnPricesUpdated += OnCardManagerPricesUpdated;
        _cardManager.MtgDatabaseReplacing += OnMtgDatabaseReplacing;
        _cardManager.MtgDatabaseReplaced += OnMtgDatabaseReplaced;
    }

    private void OnMtgDatabaseReplacing()
    {
        _filterCts?.Cancel();
        _filterDebounceCts?.Cancel();
        _allItems = [];
        _cachedFilteredItems = [];
        _gridApplyPending = false;
        _hasLoadedOnce = false;
        _lastLoadedCollectionVersion = -1;
        IsCollectionEmpty = true;
        IsBusy = false;
        InvalidateSortUnitPriceCache();
        _grid?.ResetForDatabaseReload();
    }

    private void OnMtgDatabaseReplaced()
    {
        _hasLoadedOnce = false;
    }

    private void OnCardManagerPricesUpdated()
    {
        InvalidateSortUnitPriceCache();
    }

    private void InvalidateSortUnitPriceCache()
    {
        Interlocked.Increment(ref _sortPriceInvalidationEpoch);
        lock (_sortPriceLock)
        {
            _collectionSortPriceData.Clear();
            _sortPricesCollectionVersion = -1;
            _sortPricesVendorKey = "";
        }
    }

    /// <summary>Called by CollectionPage when the card grid is created. Needed for visible-range updates (e.g. loading prices).</summary>
    public void AttachGrid(CardGrid grid)
    {
        _grid = grid;
        _grid.ViewMode = ViewMode;
        _grid.VisibleRangeChanged += OnVisibleRangeChanged;
        if (_hasLoadedOnce && _gridApplyPending)
            _ = ApplyCachedFilteredToGridAsync();
    }

    /// <summary>
    /// Applies a filter/sort result cached during background warm-up (when the grid did not exist yet)
    /// without re-running the full filter pipeline.
    /// </summary>
    private async Task ApplyCachedFilteredToGridAsync()
    {
        if (_grid == null || !_gridApplyPending)
            return;

        try
        {
            await _grid.SetCollectionAsync(_cachedFilteredItems);
            _gridApplyPending = false;

            await Task.Delay(50);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_grid == null)
                    return;

                var (start, end) = _grid.GetVisibleRange();
                if (end >= start && start >= 0)
                    _gridPriceLoadService.LoadVisiblePrices(_grid, start, end, isCollectionGrid: true);
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"ApplyCachedFilteredToGrid failed: {ex.Message}", LogLevel.Warning);
            _gridApplyPending = false;
            _ = ApplyFilterAndSortAsync(immediate: true);
        }
    }

    protected override void OnViewModeUpdated(ViewMode value)
    {
        if (_grid != null) _grid.ViewMode = value;
    }

    private const int SearchTextDebounceMs = 300;

    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _filterDebounceCts;

    partial void OnSortModeChanged(CollectionSortMode value)
    {
        OnPropertyChanged(nameof(SortModeIndex));
        _ = ApplyFilterAndSortAsync(immediate: true);
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FiltersButtonText));
        _ = ApplyFilterAndSortAfterDebounceAsync();
    }

    partial void OnCollectionFilterFoilOnlyChanged(bool value) => _ = ApplyFilterAndSortAsync(immediate: true);

    partial void OnCollectionFilterEtchedOnlyChanged(bool value) => _ = ApplyFilterAndSortAsync(immediate: true);

    partial void OnCollectionFilterMinQtyPickerIndexChanged(int value) => _ = ApplyFilterAndSortAsync(immediate: true);

    /// <summary>Opens the shared filters page (same sheet as Search).</summary>
    private async Task OpenFiltersAsync()
    {
        await _filtersOpener.OpenAsync(this, _cardManager);
    }

    /// <summary>Clears name query and all advanced filters, then reapplies the grid.</summary>
    private async Task ClearCollectionFiltersAsync()
    {
        SearchText = "";
        CurrentOptions = new SearchOptions();
        CollectionFilterFoilOnly = false;
        CollectionFilterEtchedOnly = false;
        CollectionFilterMinQtyPickerIndex = 0;
        OnPropertyChanged(nameof(FiltersButtonText));
        await ApplyFilterAndSortAsync(immediate: true);
    }

    public async Task ApplyFiltersAndSearchAsync(SearchOptions options)
    {
        CurrentOptions = options;
        OnPropertyChanged(nameof(FiltersButtonText));
        await ApplyFilterAndSortAsync(immediate: true);
    }

    /// <summary>Debounced path for filter text so each keystroke does not schedule a full filter+sort over the collection.</summary>
    private async Task ApplyFilterAndSortAfterDebounceAsync()
    {
        _filterDebounceCts?.Cancel();
        _filterDebounceCts = new CancellationTokenSource();
        var debounceToken = _filterDebounceCts.Token;
        try
        {
            await Task.Delay(SearchTextDebounceMs, debounceToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ApplyFilterAndSortAsync(immediate: false).ConfigureAwait(false);
    }

    /// <param name="immediate">When true, cancels any pending debounced filter (e.g. sort change, reload).</param>
    private async Task ApplyFilterAndSortAsync(bool immediate = true)
    {
        if (immediate)
            _filterDebounceCts?.Cancel();

        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        if (_allItems.Length == 0)
        {
            Logger.LogStuff("[CollectionUI] ApplyFilterAndSort: empty branch, _allItems=0", LogLevel.Debug);
            _cachedFilteredItems = [];
            _gridApplyPending = false;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsCollectionEmpty = true;
                TotalCards = 0;
                UniqueCards = 0;
                StatusMessage = UserMessages.StatusClear;
                Logger.LogStuff("[CollectionUI] ApplyFilterAndSort: set IsCollectionEmpty=true on main thread", LogLevel.Debug);
            });
            if (token.IsCancellationRequested) return;
            // Do not call SetCollectionAsync([]) when empty: content is already swapped to EmptyState and grid is out of the tree.
            // Updating grid state can still trigger its pipeline (e.g. on Android) and cause a black frame when grid is re-shown later.
            Logger.LogStuff("[CollectionUI] ApplyFilterAndSort: empty branch done (skipped SetCollectionAsync)", LogLevel.Debug);
            return;
        }

        try
        {
            CollectionItem[] filteredForSort;

            var merged = CurrentOptions.Clone();
            merged.NameFilter = SearchText?.Trim() ?? "";

            if (!merged.HasActiveFilters)
            {
                filteredForSort = _allItems;
            }
            else
            {
                if (token.IsCancellationRequested) return;
                var cards = await _cardManager.SearchCardsWithOptionsAsync(
                    merged,
                    nameContains: null,
                    inCollectionOnly: true,
                    limit: 0,
                    restrictToDeckLegalFormat: false,
                    DeckFormat.Standard).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                var allowed = new HashSet<string>(cards.Select(static c => c.Uuid), StringComparer.Ordinal);
                filteredForSort = CollectionFilterHelper.IntersectPreservingOrder(_allItems, allowed);
            }

            var minQty = CollectionFilterMinQtyPickerIndex <= 0 ? 0 : CollectionFilterMinQtyPickerIndex + 1;
            filteredForSort = CollectionFilterHelper.ApplyRowFilters(
                filteredForSort,
                CollectionFilterFoilOnly,
                CollectionFilterEtchedOnly,
                minQty);

            if (token.IsCancellationRequested) return;

            var usePriceSort = SortMode == CollectionSortMode.Price
                && PricePreferences.PricesDataEnabled
                && PricePreferences.CollectionPriceDisplayEnabled;

            var usePriceDeltaSort = SortMode == CollectionSortMode.PriceChangePercent
                && PricePreferences.PricesDataEnabled
                && PricePreferences.CollectionPriceDisplayEnabled;

            // Same CardPriceData + vendor priority as grid; sort key matches grid label (GetDisplayPrice uses non-finish preference).
            Dictionary<string, CardPriceData> sortPriceData = [];
            if ((usePriceSort || usePriceDeltaSort) && filteredForSort.Length > 0)
            {
                await EnsureCollectionSortPriceDataAsync(token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                lock (_sortPriceLock)
                    sortPriceData = new Dictionary<string, CardPriceData>(_collectionSortPriceData, StringComparer.Ordinal);
            }

            var (filtered, displayedTotal, displayedUnique) = await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return ([], 0, 0);

                IEnumerable<CollectionItem> result = filteredForSort;

                if (token.IsCancellationRequested) return ([], 0, 0);

                result = SortMode switch
                {
                    CollectionSortMode.Name => result.OrderBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.Cmc => result.OrderBy(i => i.Card.EffectiveManaValue).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.Rarity => result.OrderByDescending(i => i.Card.Rarity).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.Color => result.OrderBy(i => i.Card.ColorIdentity.Length).ThenBy(i => i.Card.ColorIdentity).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.Price when usePriceSort => result.OrderByDescending(i => PriceDisplayHelper.GetNumericPrice(
                        sortPriceData.GetValueOrDefault(i.Card.Uuid), false, false)).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.Price => result.OrderBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.PriceChangePercent when usePriceDeltaSort => result.OrderByDescending(i =>
                    {
                        var b = i.ReferencePriceUsd ?? 0;
                        if (b <= 0)
                            return double.MinValue;
                        var cur = PriceDisplayHelper.GetNumericPrice(
                            sortPriceData.GetValueOrDefault(i.Card.Uuid), i.IsFoil, i.IsEtched);
                        if (cur <= 0)
                            return double.MinValue;
                        return (cur - b) / b * 100.0;
                    }).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.PriceChangePercent => result.OrderBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.SetNumber => result
                        .OrderBy(i => i.Card.SetCode, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(i => i.Card.Number, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.DateAdded => result
                        .OrderByDescending(i => i.DateAdded)
                        .ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.Quantity => result
                        .OrderByDescending(i => i.Quantity)
                        .ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.Type => result
                        .OrderBy(i => i.Card.CardType, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.CmcHigh => result
                        .OrderByDescending(i => i.Card.EffectiveManaValue)
                        .ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    _ => result // Manual: keep loaded order
                };

                var arr = result.ToArray();
                var total = arr.Sum(i => i.Quantity);
                var unique = arr.Length;

                return (arr, total, unique);
            }, token);

            if (token.IsCancellationRequested) return;

            _cachedFilteredItems = filtered;
            if (_grid != null)
            {
                await _grid.SetCollectionAsync(filtered);
                _gridApplyPending = false;
            }
            else
            {
                _gridApplyPending = filtered.Length > 0;
            }

            if (token.IsCancellationRequested) return;

            // Brief delay so grid can process state and repaint before we hide the empty overlay (avoids one-frame black flash)
            await Task.Delay(50);

            if (token.IsCancellationRequested) return;

            var totalCards = displayedTotal;
            var uniqueCards = displayedUnique;
            var statusMessage = $"{displayedTotal} cards ({displayedUnique} unique)";
            Logger.LogStuff($"[CollectionUI] ApplyFilterAndSort: hasData branch, setting IsCollectionEmpty=false, count={filtered.Length}", LogLevel.Debug);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (token.IsCancellationRequested) return;
                IsCollectionEmpty = false;
                TotalCards = totalCards;
                UniqueCards = uniqueCards;
                StatusMessage = statusMessage;

                if (_grid != null)
                {
                    var (start, end) = _grid.GetVisibleRange();
                    if (end >= start && start >= 0)
                    {
                        _gridPriceLoadService.LoadVisiblePrices(_grid, start, end, isCollectionGrid: true);
                    }
                }
            });
        }
        catch (OperationCanceledException) { /* Expected when operation is cancelled (e.g. new search). */ }
    }

    /// <summary>
    /// Loads <see cref="CardPriceData"/> for the whole collection once per version + vendor order (same as grid bulk pricing).
    /// </summary>
    private async Task EnsureCollectionSortPriceDataAsync(CancellationToken cancellationToken)
    {
        static string VendorKeyString()
        {
            var p = PriceDisplayHelper.GetVendorPriority();
            return p.Length == 0 ? "" : string.Join(',', p.Select(static v => v.ToString()));
        }

        var version = _cardManager.CollectionVersion;
        var vendorKey = VendorKeyString();
        lock (_sortPriceLock)
        {
            if (_sortPricesCollectionVersion == version && string.Equals(_sortPricesVendorKey, vendorKey, StringComparison.Ordinal))
                return;
        }

        await _sortPriceLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            version = _cardManager.CollectionVersion;
            vendorKey = VendorKeyString();
            lock (_sortPriceLock)
            {
                if (_sortPricesCollectionVersion == version && string.Equals(_sortPricesVendorKey, vendorKey, StringComparison.Ordinal))
                    return;
            }

            var tempWarm = new Dictionary<string, CardPriceData>(StringComparer.Ordinal);
            if (_cardManager.TryCopyWarmCollectionPricesIfCurrent(version, vendorKey, tempWarm))
            {
                lock (_sortPriceLock)
                {
                    _collectionSortPriceData.Clear();
                    foreach (var kv in tempWarm)
                        _collectionSortPriceData[kv.Key] = kv.Value;
                    _sortPricesCollectionVersion = version;
                    _sortPricesVendorKey = vendorKey;
                }

                return;
            }

            var snapshotCommitted = false;
            for (var attempt = 0; attempt < 3 && !cancellationToken.IsCancellationRequested; attempt++)
            {
                var epoch0 = Volatile.Read(ref _sortPriceInvalidationEpoch);
                var v0 = _cardManager.CollectionVersion;
                var vk0 = VendorKeyString();
                var map = await _cardManager.GetCollectionCardPricesAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var v1 = _cardManager.CollectionVersion;
                var vk1 = VendorKeyString();
                if (v0 != v1 || vk0 != vk1) continue;
                if (epoch0 != Volatile.Read(ref _sortPriceInvalidationEpoch)) continue;

                lock (_sortPriceLock)
                {
                    _collectionSortPriceData.Clear();
                    foreach (var kv in map)
                        _collectionSortPriceData[kv.Key] = kv.Value;
                    _sortPricesCollectionVersion = v1;
                    _sortPricesVendorKey = vk1;
                }

                snapshotCommitted = true;
                break;
            }

            if (!snapshotCommitted)
            {
                lock (_sortPriceLock)
                {
                    _collectionSortPriceData.Clear();
                    _sortPricesCollectionVersion = -1;
                    _sortPricesVendorKey = "";
                }
            }
        }
        finally
        {
            _sortPriceLoadGate.Release();
        }
    }

    private async Task RefreshAsync()
    {
        await LoadCollectionAsync();
    }

    private async Task RecapturePriceBaselinesAsync()
    {
        if (!PricePreferences.PricesDataEnabled)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusIsError = true;
                StatusMessage = "Turn on price data in Settings to capture baselines.";
            });
            return;
        }

        try
        {
            var updated = await _cardManager.RecaptureAllCollectionPriceBaselinesAsync();
            await LoadCollectionAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusIsError = false;
                StatusMessage = updated > 0
                    ? $"Updated price baselines for {updated} cards."
                    : "No prices were available to store as baselines.";
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Recapture baselines failed: {ex.Message}", LogLevel.Error);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusIsError = true;
                StatusMessage = UserMessages.LoadFailed(ex.Message);
            });
        }
    }

    public async Task<Card?> GetCardDetailsAsync(string uuid)
    {
        try
        {
            return await _cardManager.GetCardDetailsAsync(uuid);
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> GetCollectionQuantityAsync(string uuid)
    {
        try
        {
            return await _cardManager.GetQuantityAsync(uuid);
        }
        catch
        {
            return 0;
        }
    }

    public async Task UpdateCollectionAsync(string uuid, int quantity, bool isFoil = false, bool isEtched = false)
    {
        try
        {
            await _cardManager.UpdateCardQuantityAsync(uuid, quantity, isFoil, isEtched);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to update collection: {ex.Message}", LogLevel.Error);
        }
    }

    public async Task ReorderCollectionAsync(int fromIndex, int toIndex)
    {
        if (_grid == null || fromIndex == toIndex) return;

        try
        {
            // The grid's in-memory state is already updated by ApplyInMemoryReorder;
            // read the current order and persist it directly.
            var uuids = _grid.GetAllUuids().ToList();
            await _cardManager.ReorderCollectionAsync(uuids);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to reorder collection: {ex.Message}", LogLevel.Error);
        }
    }

    public async Task LoadCollectionAsync()
    {
        if (IsBusy) return;

        if (!await _cardManager.EnsureInitializedAsync())
        {
            MainThread.BeginInvokeOnMainThread(() => StatusMessage = UserMessages.DatabaseNotConnected);
            return;
        }

        // Ensure prices are initialized
        await _cardManager.InitializePricesAsync();

        // Don't set IsCollectionEmpty = false here: when loading after a clear we have no data,
        // so we must keep the empty state visible until ApplyFilterAndSortAsync runs.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsBusy = true;
            StatusIsError = false;
            StatusMessage = UserMessages.LoadingCollection;
        });

        try
        {
            InvalidateSortUnitPriceCache();

            _allItems = await Task.Run(() => _cardManager.GetCollectionAsync());
            Logger.LogStuff($"[CollectionUI] LoadCollectionAsync: loaded _allItems.Count={_allItems.Length}", LogLevel.Debug);

            await ApplyFilterAndSortAsync();

            // Mark the current collection version as loaded so future tab switches can
            // skip reloading until we detect another mutation.
            _hasLoadedOnce = true;
            _lastLoadedCollectionVersion = _cardManager.CollectionVersion;

            var isEmptyNow = IsCollectionEmpty;
            Logger.LogStuff($"[CollectionUI] LoadCollectionAsync: after ApplyFilterAndSort, IsCollectionEmpty={isEmptyNow}, willInvokeCollectionLoaded={!isEmptyNow}", LogLevel.Debug);
            if (!IsCollectionEmpty)
                MainThread.BeginInvokeOnMainThread(() => CollectionLoaded?.Invoke());
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusIsError = true;
                StatusMessage = UserMessages.LoadFailed(msg);
            });
            Logger.LogStuff($"Collection load error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() => IsBusy = false);
        }
    }

    /// <summary>
    /// Ensures the collection is loaded for the Collection tab. On first call, it
    /// loads from the database. On subsequent calls it only reloads if the
    /// underlying collection version has changed (i.e., the DB was mutated).
    /// </summary>
    public async Task EnsureCollectionLoadedAsync()
    {
        var currentVersion = _cardManager.CollectionVersion;

        if (!_hasLoadedOnce)
        {
            await LoadCollectionAsync();
            return;
        }

        if (currentVersion != _lastLoadedCollectionVersion)
        {
            await LoadCollectionAsync();
        }
    }

    public async Task AddCardAsync(string uuid, int quantity = 1)
    {
        await _cardManager.AddCardToCollectionAsync(uuid, quantity);
        await LoadCollectionAsync();
    }

    public async Task RemoveCardAsync(string uuid)
    {
        await _cardManager.RemoveCardFromCollectionAsync(uuid);
        await LoadCollectionAsync();
    }

    /// <summary>
    /// Clears the entire collection. Call after user confirmation (e.g. from page code-behind).
    /// </summary>
    public async Task ClearCollectionAsync()
    {
        await _cardManager.ClearCollectionAsync();
        await LoadCollectionAsync();
    }

    public void OnScrollChanged(float scrollY)
    {
        // No-op for now unless we need to trigger infinite scroll
    }

    private void OnVisibleRangeChanged(int start, int end)
    {
        _gridPriceLoadService.LoadVisiblePrices(_grid, start, end, isCollectionGrid: true);
    }

    private async Task ImportCollectionAsync()
    {
        try
        {
            var result = await FilePickerHelper.PickCsvFileAsync("Select a CSV file to import");

            if (result != null)
            {
                IsBusy = true;
                StatusMessage = UserMessages.ImportingCollection;
                StatusIsError = false;

                void OnProgress(string message, int progress)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        StatusMessage = message;
                    });
                }

                using var stream = await result.OpenReadAsync();
                var importResult = await Task.Run(() => _importer.ImportCsvAsync(stream, OnProgress));

                if (importResult.Errors.Any())
                {
                    Logger.LogStuff($"Import completed with {importResult.Errors.Count} errors. First error: {importResult.Errors.First()}", LogLevel.Warning);
                }

                IsBusy = false;
                await LoadCollectionAsync();
                // Clear filter after reload so the list shows the full collection (avoids showing
                // empty when a previous filter matches no cards in the new dataset). Doing this
                // after the load prevents a race where a filter-triggered apply could overwrite
                // the grid with pre-import data.
                SearchText = "";
                CurrentOptions = new SearchOptions();
                CollectionFilterFoilOnly = false;
                CollectionFilterEtchedOnly = false;
                CollectionFilterMinQtyPickerIndex = 0;
                OnPropertyChanged(nameof(FiltersButtonText));
            }
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to import collection: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportCollectionAsync()
    {
        try
        {
            if (!await _cardManager.EnsureInitializedAsync())
                return;

            if (_allItems.Length == 0)
                return;

            IsBusy = true;
            StatusMessage = UserMessages.ExportingCollection;
            StatusIsError = false;

            var csvText = await _exporter.ExportToCsvAsync();

            var cacheFile = Path.Combine(FileSystem.CacheDirectory, "collection_export.csv");
            await File.WriteAllTextAsync(cacheFile, csvText, Encoding.UTF8);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Collection",
                File = new ShareFile(cacheFile)
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to export collection: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
            _ = ApplyFilterAndSortAsync();
        }
    }
}
