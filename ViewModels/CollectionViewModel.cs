using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Core.Layout;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;

namespace MTGFetchMAUI.ViewModels;

/// <summary>
/// ViewModel for the collection page.
/// Loads and displays user's card collection in the grid.
/// </summary>
public partial class CollectionViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private CardGrid? _grid;
    private CollectionItem[] _allItems = [];

    [ObservableProperty]
    private int _totalCards;

    [ObservableProperty]
    private int _uniqueCards;

    [ObservableProperty]
    private bool _isCollectionEmpty;

    [ObservableProperty]
    private CollectionSortMode _sortMode = CollectionSortMode.Manual;

    [ObservableProperty]
    private string _filterText = "";

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

    public CollectionViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;
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
}
