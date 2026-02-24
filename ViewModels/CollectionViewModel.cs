using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTGFetchMAUI.Controls;
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

    [ObservableProperty]
    private int _totalCards;

    [ObservableProperty]
    private int _uniqueCards;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeButtonText))]
    private ViewMode _viewMode = ViewMode.Grid;

    public string ViewModeButtonText => ViewMode == ViewMode.Grid ? "☰" : "⊞";

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

    partial void OnViewModeChanged(ViewMode value)
    {
        if (_grid != null) _grid.ViewMode = value;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadCollectionAsync();
    }

    [RelayCommand]
    private void ToggleViewMode()
    {
        ViewMode = ViewMode == ViewMode.Grid ? ViewMode.List : ViewMode.Grid;
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

    public async Task UpdateCollectionAsync(string uuid, int quantity)
    {
        try
        {
            await _cardManager.UpdateCardQuantityAsync(uuid, quantity);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to update collection: {ex.Message}", LogLevel.Error);
        }
    }

    public async Task LoadCollectionAsync()
    {
        if (IsBusy) return;

        if (!_cardManager.DatabaseManager.IsConnected)
        {
            StatusMessage = "Connecting...";
            if (!await _cardManager.InitializeAsync())
            {
                StatusMessage = "Database not connected.";
                return;
            }
        }

        // Ensure prices are initialized
        await _cardManager.InitializePricesAsync();

        IsBusy = true;
        StatusMessage = "Loading collection...";

        try
        {
            var items = await _cardManager.GetCollectionAsync();

            TotalCards = items.Sum(i => i.Quantity);
            UniqueCards = items.Length;

            _grid?.SetCollection(items);
            CollectionLoaded?.Invoke();

            StatusMessage = $"{TotalCards} cards ({UniqueCards} unique)";
        }
        catch (Exception ex)
        {
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
