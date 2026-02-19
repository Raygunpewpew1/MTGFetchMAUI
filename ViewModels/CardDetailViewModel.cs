using System.Windows.Input;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;
using SkiaSharp;

namespace MTGFetchMAUI.ViewModels;

/// <summary>
/// ViewModel for card detail page.
/// Loads card data, images, faces, legalities, and prices.
/// Port of TCardDetailFrame logic from CardDetailFrame.pas.
/// </summary>
public class CardDetailViewModel : BaseViewModel, IDisposable
{
    private readonly CardManager _cardManager;
    private Card _card = new();
    private Card[] _faces = [];
    private int _currentFaceIndex;
    private SKImage? _cardImage;
    private bool _isInCollection;
    private string _priceDisplay = "";
    private CardPriceData _priceData = CardPriceData.Empty;
    private bool _showLegalities;

    public Card Card
    {
        get => _card;
        set { SetProperty(ref _card, value); OnPropertyChanged(nameof(HasMultipleFaces)); }
    }

    public Card[] Faces
    {
        get => _faces;
        set => SetProperty(ref _faces, value);
    }

    public SKImage? CardImage
    {
        get => _cardImage;
        set
        {
            if (_cardImage != value)
            {
                _cardImage?.Dispose();
                SetProperty(ref _cardImage, value);
            }
        }
    }

    public bool IsInCollection
    {
        get => _isInCollection;
        set => SetProperty(ref _isInCollection, value);
    }

    public string PriceDisplay
    {
        get => _priceDisplay;
        set => SetProperty(ref _priceDisplay, value);
    }

    public CardPriceData PriceData
    {
        get => _priceData;
        set { SetProperty(ref _priceData, value); OnPropertyChanged(nameof(HasPriceHistory)); }
    }

    public bool HasPriceHistory => _priceData != CardPriceData.Empty &&
                                   (_priceData.Paper.TCGPlayer.RetailNormalHistory.Count > 0 ||
                                    _priceData.Paper.Cardmarket.RetailNormalHistory.Count > 0);

    public bool ShowLegalities
    {
        get => _showLegalities;
        set => SetProperty(ref _showLegalities, value);
    }

    public bool HasMultipleFaces => _faces.Length > 1 && Card.Layout.IsDoubleFaced();

    public Card CurrentFace => _faces.Length > 0 ? _faces[_currentFaceIndex] : _card;

    public ICommand FlipFaceCommand { get; }
    public ICommand AddToCollectionCommand { get; }
    public ICommand RemoveFromCollectionCommand { get; }
    public ICommand ToggleLegalitiesCommand { get; }

    public event Action<string>? AddedToCollection;

    public CardDetailViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;
        FlipFaceCommand = new Command(FlipFace);
        AddToCollectionCommand = new Command<int>(async qty => await AddToCollectionAsync(qty));
        RemoveFromCollectionCommand = new Command(async () => await RemoveFromCollectionAsync());
        ToggleLegalitiesCommand = new Command(() => ShowLegalities = !ShowLegalities);

        _cardManager.OnPricesUpdated += HandlePricesUpdated;
    }

    private async void HandlePricesUpdated()
    {
        if (Card != null && !string.IsNullOrEmpty(Card.UUID))
        {
            await LoadPriceAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(PriceDisplay));
                OnPropertyChanged(nameof(CurrentFace)); // Triggers UI update
            });
        }
    }

    public void Dispose()
    {
        _cardManager.OnPricesUpdated -= HandlePricesUpdated;
        GC.SuppressFinalize(this);
    }

    public async Task<int> GetCollectionQuantityAsync()
    {
        try
        {
            return await _cardManager.GetQuantityAsync(Card.UUID);
        }
        catch
        {
            return 0;
        }
    }

    public async Task LoadCardAsync(string uuid)
    {
        if (!_cardManager.DatabaseManager.IsConnected)
        {
            if (!await _cardManager.InitializeAsync())
            {
                return;
            }
        }

        IsBusy = true;

        try
        {
            // Load full card with rulings
            var mainCard = await _cardManager.GetCardWithRulingsAsync(uuid);
            Card = mainCard;

            // Load faces
            var package = await _cardManager.GetFullCardPackageAsync(uuid);
            if (package.Length > 0)
            {
                // MELD CARD FILTER: Only show current card + meld result (not the other piece)
                // This makes meld cards behave like transform cards (front/back only)
                if (package.Length > 2 && mainCard.Layout == CardLayout.Meld)
                {
                    var filtered = new List<Card>();
                    // Add current piece
                    var current = package.FirstOrDefault(f => f.UUID == uuid);
                    if (current != null) filtered.Add(current);
                    // Add meld result (side 'b')
                    var result = package.FirstOrDefault(f => f.Side == 'b');
                    if (result != null) filtered.Add(result);

                    package = [.. filtered];
                }

                // Filter: show primary face first, then others
                Faces = package;
                _currentFaceIndex = 0;
                for (int i = 0; i < package.Length; i++)
                {
                    if (package[i].UUID == uuid) { _currentFaceIndex = i; break; }
                }
            }
            else
            {
                Faces = [Card];
                _currentFaceIndex = 0;
            }
            OnPropertyChanged(nameof(CurrentFace));
            OnPropertyChanged(nameof(HasMultipleFaces));

            // Check collection status
            IsInCollection = await _cardManager.IsInCollectionAsync(uuid);

            // Load image
            await LoadCardImageAsync();

            // Load price
            await LoadPriceAsync();
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Card detail load error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCardImageAsync()
    {
        var currentFace = CurrentFace;
        if (string.IsNullOrEmpty(currentFace.ScryfallId)) return;

        // Determine face parameter for Scryfall
        string faceParam = "front";
        if (currentFace.Side is 'b' or 'c')
        {
            // If the current face has a different Scryfall ID than the main face,
            // it's a separate card record (e.g. Meld result), so we want its 'front'.
            // Otherwise it's the back of a standard Transform/MDFC.
            if (_faces.Length > 0 && currentFace.ScryfallId == _faces[0].ScryfallId)
                faceParam = "back";
        }

        var cached = await _cardManager.GetCachedCardImageAsync(currentFace.ScryfallId, MTGConstants.ImageSizeNormal, faceParam);
        if (cached != null)
        {
            CardImage = cached;
            return;
        }

        _cardManager.DownloadCardImageAsync(currentFace.ScryfallId, (image, success) =>
        {
            if (success && image != null)
            {
                MainThread.BeginInvokeOnMainThread(() => CardImage = image);
            }
        }, MTGConstants.ImageSizeNormal, faceParam);
    }

    private async Task LoadPriceAsync()
    {
        var (found, prices) = await _cardManager.GetCardPricesAsync(Card.UUID);
        if (!found) { PriceDisplay = ""; PriceData = CardPriceData.Empty; return; }

        PriceData = prices;

        VendorPrices[] vendors = [prices.Paper.TCGPlayer, prices.Paper.Cardmarket, prices.Paper.CardKingdom];
        foreach (var v in vendors)
        {
            if (v.RetailNormal.Price > 0) { PriceDisplay = $"${v.RetailNormal.Price:F2}"; return; }
            if (v.RetailFoil.Price > 0) { PriceDisplay = $"${v.RetailFoil.Price:F2} (Foil)"; return; }
        }
        PriceDisplay = "";
    }

    private void FlipFace()
    {
        if (_faces.Length <= 1) return;
        _currentFaceIndex = (_currentFaceIndex + 1) % _faces.Length;
        OnPropertyChanged(nameof(CurrentFace));
        _ = LoadCardImageAsync();
    }

    private async Task AddToCollectionAsync(int quantity)
    {
        await _cardManager.AddCardToCollectionAsync(Card.UUID, quantity);
        IsInCollection = true;
        AddedToCollection?.Invoke(Card.UUID);
    }

    private async Task RemoveFromCollectionAsync()
    {
        await _cardManager.RemoveCardFromCollectionAsync(Card.UUID);
        IsInCollection = false;
    }

    public List<(string format, LegalityStatus status)> GetLegalityList()
    {
        var list = new List<(string, LegalityStatus)>();
        foreach (DeckFormat fmt in Enum.GetValues<DeckFormat>())
        {
            var status = Card.Legalities[fmt];
            list.Add((fmt.ToDisplayName(), status));
        }
        return list;
    }

    public string GetCombinedText()
    {
        if (_faces.Length <= 1)
            return Card.Text;

        var parts = new List<string>();
        foreach (var face in _faces)
        {
            string header = face.Name;
            if (!string.IsNullOrEmpty(face.ManaCost))
                header += $" {face.ManaCost}";
            if (!string.IsNullOrEmpty(face.CardType))
                header += $"\n{face.CardType}";
            string body = face.Text ?? "";
            parts.Add($"{header}\n{body}");
        }

        string separator = Card.Layout switch
        {
            CardLayout.Transform => "\n\u2015\u2015\u2015 \u21C4 Transform \u21C4 \u2015\u2015\u2015\n",
            CardLayout.ModalDFC => "\n\u2015\u2015\u2015 // \u2015\u2015\u2015\n",
            CardLayout.Adventure => "\n\u2015\u2015\u2015 \u2694 Adventure \u2694 \u2015\u2015\u2015\n",
            CardLayout.Split => "\n\u2015\u2015\u2015 // \u2015\u2015\u2015\n",
            _ => "\n\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\n"
        };

        return string.Join(separator, parts);
    }
}
