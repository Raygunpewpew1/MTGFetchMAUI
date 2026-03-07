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
public partial class CollectionViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private readonly IGridPriceLoadService _gridPriceLoadService;
    private readonly CollectionImporter _importer;
    private readonly CollectionExporter _exporter;
    private CardGrid? _grid;
    private CollectionItem[] _allItems = [];

    // ── Bindable properties ──

    [ObservableProperty]
    public partial int TotalCards { get; set; }

    [ObservableProperty]
    public partial int UniqueCards { get; set; }

    [ObservableProperty]
    public partial bool IsCollectionEmpty { get; set; }

    [ObservableProperty]
    public partial CollectionSortMode SortMode { get; set; } = CollectionSortMode.Manual;

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    /// <summary>Labels for the sort-mode picker (Manual, Name, CMC, etc.).</summary>
    public List<string> SortModeOptions { get; } = ["Manual", "Name", "CMC", "Rarity", "Color"];

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

    public CollectionViewModel(CardManager cardManager, IGridPriceLoadService gridPriceLoadService, CollectionImporter importer, CollectionExporter exporter)
    {
        _cardManager = cardManager;
        _gridPriceLoadService = gridPriceLoadService;
        _importer = importer;
        _exporter = exporter;
        ImportCollectionCommand = new AsyncRelayCommand(ImportCollectionAsync);
        ExportCollectionCommand = new AsyncRelayCommand(ExportCollectionAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);

        _cardManager.OnPriceSyncProgress += (msg, pct) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsImportingPrices = pct < 100;
            });
        };
    }

    /// <summary>Called by CollectionPage when the card grid is created. Needed for visible-range updates (e.g. loading prices).</summary>
    public void AttachGrid(CardGrid grid)
    {
        _grid = grid;
        _grid.VisibleRangeChanged += OnVisibleRangeChanged;
    }

    protected override void OnViewModeUpdated(ViewMode value)
    {
        if (_grid != null) _grid.ViewMode = value;
    }

    private CancellationTokenSource? _filterCts;

    partial void OnSortModeChanged(CollectionSortMode value)
    {
        OnPropertyChanged(nameof(SortModeIndex));
        _ = ApplyFilterAndSortAsync();
    }

    partial void OnFilterTextChanged(string value) => _ = ApplyFilterAndSortAsync();

    private async Task ApplyFilterAndSortAsync()
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        if (_allItems.Length == 0)
        {
            Logger.LogStuff("[CollectionUI] ApplyFilterAndSort: empty branch, _allItems=0", LogLevel.Debug);
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
            var (filtered, displayedTotal, displayedUnique) = await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return ([], 0, 0);

                IEnumerable<CollectionItem> result = _allItems;

                // Filter by name
                var filter = FilterText?.Trim();
                if (!string.IsNullOrEmpty(filter))
                    result = result.Where(i => i.Card.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

                // Sort
                if (token.IsCancellationRequested) return ([], 0, 0);

                result = SortMode switch
                {
                    CollectionSortMode.Name => result.OrderBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.CMC => result.OrderBy(i => i.Card.FaceManaValue).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.Rarity => result.OrderByDescending(i => i.Card.Rarity).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    CollectionSortMode.Color => result.OrderBy(i => i.Card.ColorIdentity.Length).ThenBy(i => i.Card.ColorIdentity).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
                    _ => result // Manual: keep loaded order
                };

                var arr = result.ToArray();
                var total = arr.Sum(i => i.Quantity);
                var unique = arr.Length;

                return (arr, total, unique);
            }, token);

            if (token.IsCancellationRequested) return;

            if (_grid != null) await _grid.SetCollectionAsync(filtered);

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
                        _gridPriceLoadService.LoadVisiblePrices(_grid, start, end);
                    }
                }
            });
        }
        catch (OperationCanceledException) { /* Expected when operation is cancelled (e.g. new search). */ }
    }

    private async Task RefreshAsync()
    {
        await LoadCollectionAsync();
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
            _allItems = await Task.Run(() => _cardManager.GetCollectionAsync());
            Logger.LogStuff($"[CollectionUI] LoadCollectionAsync: loaded _allItems.Count={_allItems.Length}", LogLevel.Debug);

            await ApplyFilterAndSortAsync();

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
        _gridPriceLoadService.LoadVisiblePrices(_grid, start, end);
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
                FilterText = "";
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
