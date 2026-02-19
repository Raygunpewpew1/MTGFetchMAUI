using System.Windows.Input;
using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;

namespace MTGFetchMAUI.ViewModels;

/// <summary>
/// ViewModel for the collection page.
/// Loads and displays user's card collection in the grid.
/// </summary>
public class CollectionViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private MTGCardGrid? _grid;
    private int _totalCards;
    private int _uniqueCards;

    public int TotalCards
    {
        get => _totalCards;
        set => SetProperty(ref _totalCards, value);
    }

    public int UniqueCards
    {
        get => _uniqueCards;
        set => SetProperty(ref _uniqueCards, value);
    }

    public ICommand RefreshCommand { get; }

    public event Action? CollectionLoaded;

    public CollectionViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;
        RefreshCommand = new Command(async () => await LoadCollectionAsync());
    }

    public void AttachGrid(MTGCardGrid grid)
    {
        _grid = grid;
        _grid.VisibleRangeChanged += OnVisibleRangeChanged;
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

        IsBusy = true;
        StatusMessage = "Loading collection...";

        try
        {
            var items = await _cardManager.GetCollectionAsync();
            TotalCards = items.Sum(i => i.Quantity);
            UniqueCards = items.Length;

            _grid?.SetCollection(items);
            CollectionLoaded?.Invoke();

            // Load images for visible cards
            await Task.Delay(50);
            LoadVisibleImages();

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
        _grid?.SetScrollOffset(scrollY);
    }

    private void OnVisibleRangeChanged(int start, int end)
    {
        LoadVisibleImages();
        LoadVisiblePrices(start, end);
    }

    private void LoadVisiblePrices(int start, int end)
    {
        if (_grid == null) return;

        _ = Task.Run(async () =>
        {
            for (int i = start; i <= end; i++)
            {
                var card = _grid.GetCardAt(i);
                if (card == null || card.PriceData != null) continue;

                var (found, prices) = await _cardManager.GetCardPricesAsync(card.UUID);
                if (found)
                {
                    string uuid = card.UUID;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _grid?.UpdateCardPrices(uuid, prices);
                    });
                }
            }
        });
    }

    public void LoadVisibleImages()
    {
        if (_grid == null) return;

        var needed = _grid.GetCardsNeedingImages();
        foreach (var (_, card) in needed)
        {
            if (string.IsNullOrEmpty(card.ScryfallId)) continue;

            _grid.MarkLoading(card.UUID);
            string uuid = card.UUID;

            _cardManager.DownloadCardImageAsync(card.ScryfallId, (image, success) =>
            {
                if (success && image != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (_grid != null)
                            _grid.UpdateCardImage(uuid, image, ImageQuality.Small);
                        else
                            image.Dispose();
                    });
                }
            }, MTGConstants.ImageSizeSmall);
        }
    }
}
