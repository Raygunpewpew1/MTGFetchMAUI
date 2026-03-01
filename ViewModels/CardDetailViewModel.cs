using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
public partial class CardDetailViewModel : BaseViewModel, IDisposable
{
    private readonly CardManager _cardManager;
    private readonly CardGalleryContext _galleryContext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleFaces))]
    [NotifyPropertyChangedFor(nameof(HasRulings))]
    [NotifyPropertyChangedFor(nameof(HasPurchaseLinks))]
    private Card _card = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentFace))]
    [NotifyPropertyChangedFor(nameof(HasMultipleFaces))]
    private Card[] _faces = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentFace))]
    private int _currentFaceIndex;

    [ObservableProperty]
    private SKImage? _cardImage;

    [ObservableProperty]
    private bool _cardImageLoadFailed;

    [ObservableProperty]
    private bool _isInCollection;

    [ObservableProperty]
    private string _priceDisplay = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPriceHistory))]
    private CardPriceData _priceData = CardPriceData.Empty;

    [ObservableProperty]
    private string _cardPosition = "";

    public bool HasPriceHistory => PriceData != CardPriceData.Empty &&
                                   (PriceData.Paper.TCGPlayer.RetailNormalHistory.Count > 0 ||
                                    PriceData.Paper.Cardmarket.RetailNormalHistory.Count > 0);

    public bool HasRulings => Card?.Rulings != null && Card.Rulings.Count > 0;

    public bool HasPurchaseLinks => Card != null &&
        (!string.IsNullOrEmpty(Card.cardKingdom) ||
         !string.IsNullOrEmpty(Card.cardKingdomFoil) ||
         !string.IsNullOrEmpty(Card.cardKingdomEtched) ||
         !string.IsNullOrEmpty(Card.cardmarket) ||
         !string.IsNullOrEmpty(Card.tcgplayer) ||
         !string.IsNullOrEmpty(Card.tcgplayerEtched));

    public bool HasMultipleFaces => Faces.Length > 1 && Card.Layout.IsDoubleFaced();

    public bool ShowGalleryNavigation => _galleryContext.HasContext;

    public Card CurrentFace => Faces.Length > 0 && CurrentFaceIndex >= 0 && CurrentFaceIndex < Faces.Length
        ? Faces[CurrentFaceIndex]
        : Card;

    public event Action<string>? AddedToCollection;

    public CardDetailViewModel(CardManager cardManager, CardGalleryContext galleryContext)
    {
        _cardManager = cardManager;
        _galleryContext = galleryContext;
        _cardManager.OnPricesUpdated += HandlePricesUpdated;
    }

    private async void HandlePricesUpdated()
    {
        if (Card != null && !string.IsNullOrEmpty(Card.UUID))
        {
            await LoadPriceAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
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
        if (!await _cardManager.EnsureInitializedAsync()) return;

        IsBusy = true;

        try
        {
            CardImage = null;
            CardImageLoadFailed = false;

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
                CurrentFaceIndex = 0;
                for (int i = 0; i < package.Length; i++)
                {
                    if (package[i].UUID == uuid) { CurrentFaceIndex = i; break; }
                }
            }
            else
            {
                Faces = [Card];
                CurrentFaceIndex = 0;
            }
            // Faces setter already notifies CurrentFace via NotifyPropertyChangedFor

            // Check collection status
            IsInCollection = await _cardManager.IsInCollectionAsync(uuid);

            // Load image
            await LoadCardImageAsync();

            // Load price
            await LoadPriceAsync();

            UpdateGalleryState();
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

    private void UpdateGalleryState()
    {
        var pos = _galleryContext.GetPositionText();
        CardPosition = string.IsNullOrEmpty(pos) ? "" : $"‹  {pos}  ›";
        OnPropertyChanged(nameof(ShowGalleryNavigation));
    }

    private async Task LoadCardImageAsync()
    {
        var currentFace = CurrentFace;
        if (string.IsNullOrEmpty(currentFace.ScryfallId))
        {
            CardImageLoadFailed = true;
            return;
        }

        // Determine face parameter for Scryfall
        string faceParam = "front";
        if (currentFace.Side is 'b' or 'c')
        {
            // If the current face has a different Scryfall ID than the main face,
            // it's a separate card record (e.g. Meld result), so we want its 'front'.
            // Otherwise it's the back of a standard Transform/MDFC.
            if (Faces.Length > 0 && currentFace.ScryfallId == Faces[0].ScryfallId)
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
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (success && image != null)
                    CardImage = image;
                else
                    CardImageLoadFailed = true;
            });
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

    partial void OnCardImageChanging(SKImage? value)
    {
        CardImage?.Dispose();
    }

    [RelayCommand]
    private async Task NavigatePreviousCard()
    {
        var uuid = _galleryContext.GetPreviousUuid();
        if (uuid == null) return;
        _galleryContext.MovePrevious();
        await LoadCardAsync(uuid);
    }

    [RelayCommand]
    private async Task NavigateNextCard()
    {
        var uuid = _galleryContext.GetNextUuid();
        if (uuid == null) return;
        _galleryContext.MoveNext();
        await LoadCardAsync(uuid);
    }

    [RelayCommand]
    private void FlipFace()
    {
        if (Faces.Length <= 1) return;
        CurrentFaceIndex = (CurrentFaceIndex + 1) % Faces.Length;
        _ = LoadCardImageAsync();
    }

    [RelayCommand]
    private async Task AddToCollection(int quantity)
    {
        await _cardManager.AddCardToCollectionAsync(Card.UUID, quantity);
        IsInCollection = true;
        AddedToCollection?.Invoke(Card.UUID);
    }

    public async Task AddToCollectionWithFinishAsync(int quantity, bool isFoil, bool isEtched)
    {
        await _cardManager.UpdateCardQuantityAsync(Card.UUID, quantity, isFoil, isEtched);
        IsInCollection = quantity > 0;
        AddedToCollection?.Invoke(Card.UUID);
    }

    [RelayCommand]
    private async Task RemoveFromCollection()
    {
        await _cardManager.RemoveCardFromCollectionAsync(Card.UUID);
        IsInCollection = false;
    }

    public List<(string label, string url)> GetPurchaseLinks()
    {
        var links = new List<(string, string)>();
        if (!string.IsNullOrEmpty(Card.tcgplayer)) links.Add(("TCGPlayer", Card.tcgplayer));
        if (!string.IsNullOrEmpty(Card.tcgplayerEtched)) links.Add(("TCGPlayer \u2014 Etched", Card.tcgplayerEtched));
        if (!string.IsNullOrEmpty(Card.cardmarket)) links.Add(("Cardmarket", Card.cardmarket));
        if (!string.IsNullOrEmpty(Card.cardKingdom)) links.Add(("Card Kingdom", Card.cardKingdom));
        if (!string.IsNullOrEmpty(Card.cardKingdomFoil)) links.Add(("Card Kingdom \u2014 Foil", Card.cardKingdomFoil));
        if (!string.IsNullOrEmpty(Card.cardKingdomEtched)) links.Add(("Card Kingdom \u2014 Etched", Card.cardKingdomEtched));
        return links;
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
        if (Faces.Length <= 1)
            return Card.Text;

        var parts = new List<string>();
        foreach (var face in Faces)
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
