using AetherVault.Controls;
using AetherVault.Core;
using AetherVault.Core.Layout;
using AetherVault.Models;
using AetherVault.Services;
using AetherVault.Services.ImportExport;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for the collection page.
/// Loads and displays user's card collection in the grid.
/// </summary>
public partial class CollectionViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private readonly CollectionImporter _importer;
    private readonly CollectionExporter _exporter;
    private readonly IToastService _toastService;
    private CardGrid? _grid;
    private CollectionItem[] _allItems = [];

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

    public event Action? CollectionLoaded;

    public CollectionViewModel(CardManager cardManager, CollectionImporter importer, CollectionExporter exporter, IToastService toastService)
    {
        _cardManager = cardManager;
        _importer = importer;
        _exporter = exporter;
        _toastService = toastService;

        _cardManager.OnPriceSyncProgress += (msg, pct) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsImportingPrices = pct < 100;
            });
        };
    }

    public void AttachGrid(CardGrid grid)
    {
        _grid = grid;
        _grid.VisibleRangeChanged += OnVisibleRangeChanged;
    }

    protected override void OnViewModeUpdated(ViewMode value)
    {
        if (_grid != null) _grid.ViewMode = value;
    }

    partial void OnSortModeChanged(CollectionSortMode value)
    {
        OnPropertyChanged(nameof(SortModeIndex));
        ApplyFilterAndSort();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilterAndSort();

    private void ApplyFilterAndSort()
    {
        if (_allItems.Length == 0) return;

        IEnumerable<CollectionItem> result = _allItems;

        // Filter by name
        var filter = FilterText?.Trim();
        if (!string.IsNullOrEmpty(filter))
            result = result.Where(i => i.Card.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        // Sort
        result = SortMode switch
        {
            CollectionSortMode.Name => result.OrderBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
            CollectionSortMode.CMC => result.OrderBy(i => i.Card.FaceManaValue).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
            CollectionSortMode.Rarity => result.OrderByDescending(i => i.Card.Rarity).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
            CollectionSortMode.Color => result.OrderBy(i => i.Card.ColorIdentity.Length).ThenBy(i => i.Card.ColorIdentity).ThenBy(i => i.Card.Name, StringComparer.OrdinalIgnoreCase),
            _ => result // Manual: keep loaded order
        };

        var filtered = result.ToArray();
        _grid?.SetCollection(filtered);

        var displayedTotal = filtered.Sum(i => i.Quantity);
        var displayedUnique = filtered.Length;
        TotalCards = displayedTotal;
        UniqueCards = displayedUnique;
        IsCollectionEmpty = _allItems.Length == 0;

        StatusMessage = _allItems.Length == 0 ? "" : $"{displayedTotal} cards ({displayedUnique} unique)";

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_grid != null)
            {
                var (start, end) = _grid.GetVisibleRange();
                if (end >= start && start >= 0)
                {
                    LoadVisiblePrices(start, end);
                }
            }
        });
    }

    [RelayCommand]
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
            StatusMessage = "Database not connected.";
            return;
        }

        // Ensure prices are initialized
        await _cardManager.InitializePricesAsync();

        IsBusy = true;
        IsCollectionEmpty = false;
        StatusIsError = false;
        StatusMessage = "Loading collection...";

        try
        {
            _allItems = await _cardManager.GetCollectionAsync();

            ApplyFilterAndSort();
            CollectionLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Load failed: {ex.Message}";
            Logger.LogStuff($"Collection load error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
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

    public void OnScrollChanged(float scrollY)
    {
        // No-op for now unless we need to trigger infinite scroll
    }

    private void OnVisibleRangeChanged(int start, int end)
    {
        LoadVisiblePrices(start, end);
    }

    private void LoadVisiblePrices(int start, int end)
    {
        if (_grid == null) return;

        _ = Task.Run(async () =>
        {
            for (int i = start; i <= end; i++)
            {
                var card = _grid.GetCardStateAt(i);
                if (card == null || card.PriceData != null) continue;

                var (found, prices) = await _cardManager.GetCardPricesAsync(card.Id.Value);
                if (found)
                {
                    string uuid = card.Id.Value;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _grid?.UpdateCardPrices(uuid, prices);
                    });
                }
            }
        });
    }

    [RelayCommand]
    private async Task ImportCollectionAsync()
    {
        try
        {
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.iOS, new[] { "public.comma-separated-values-text" } },
                { DevicePlatform.Android, new[] { "text/csv" } },
                { DevicePlatform.WinUI, new[] { ".csv" } },
                { DevicePlatform.macOS, new[] { "public.comma-separated-values-text" } },
            });

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a CSV file to import",
                FileTypes = customFileType,
            });

            if (result != null)
            {
                IsBusy = true;
                StatusMessage = "Importing collection...";
                StatusIsError = false;

                void OnProgress(string message, int progress)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        StatusMessage = message;
                    });
                }

                using var stream = await result.OpenReadAsync();
                var importResult = await _importer.ImportCsvAsync(stream, OnProgress);

                if (importResult.Errors.Any())
                {
                    Logger.LogStuff($"Import completed with {importResult.Errors.Count} errors. First error: {importResult.Errors.First()}", LogLevel.Warning);
                }

                _toastService.Show($"Imported {importResult.SuccessCount} lines ({importResult.TotalCards} cards).");

                await LoadCollectionAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to import collection: {ex.Message}", LogLevel.Error);
            _toastService.Show("Failed to import collection.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportCollectionAsync()
    {
        try
        {
            if (_allItems.Length == 0)
            {
                _toastService.Show("Collection is empty.");
                return;
            }

            IsBusy = true;
            StatusMessage = "Exporting collection...";
            StatusIsError = false;

            var csvText = await _exporter.ExportToCsvAsync();

            var cacheFile = Path.Combine(FileSystem.CacheDirectory, "collection_export.csv");
            await File.WriteAllTextAsync(cacheFile, csvText, Encoding.UTF8);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Collection",
                File = new ShareFile(cacheFile)
            });

            _toastService.Show("Collection exported successfully.");
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to export collection: {ex.Message}", LogLevel.Error);
            _toastService.Show("Failed to export collection.");
        }
        finally
        {
            IsBusy = false;
            StatusMessage = "";
        }
    }
}
