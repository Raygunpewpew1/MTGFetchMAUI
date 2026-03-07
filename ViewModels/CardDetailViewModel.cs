using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using System.Windows.Input;

namespace AetherVault.ViewModels;

public record PriceHistoryPoint(string Label, List<PriceEntry> Points);
public record PurchaseLink(string Label, string Url);
public record LegalityItem(string Format, LegalityStatus Status)
{
    public string StatusText => Status switch
    {
        LegalityStatus.Legal => "Legal",
        LegalityStatus.Banned => "Banned",
        LegalityStatus.Restricted => "Restricted",
        _ => "Not Legal"
    };
}

/// <summary>
/// ViewModel for the card detail page (opened from search or collection). Loads full card data, images, multiple faces,
/// legalities, rulings, and prices. CardGalleryContext provides the list of UUIDs for swipe-to-next/prev from the same result set.
/// </summary>
public partial class CardDetailViewModel : BaseViewModel, IDisposable
{
    private readonly CardManager _cardManager;
    private readonly CardGalleryContext _galleryContext;

    // ── Bindable properties (detail UI binds to these) ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleFaces))]
    [NotifyPropertyChangedFor(nameof(HasRulings))]
    [NotifyPropertyChangedFor(nameof(HasPurchaseLinks))]
    public partial Card Card { get; set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentFace))]
    [NotifyPropertyChangedFor(nameof(HasMultipleFaces))]
    public partial Card[] Faces { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentFace))]
    public partial int CurrentFaceIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImageLoading))]
    public partial SKImage? CardImage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImageLoading))]
    public partial bool CardImageLoadFailed { get; set; }

    public bool IsImageLoading => CardImage == null && !CardImageLoadFailed;

    [ObservableProperty]
    public partial bool IsInCollection { get; set; }

    [ObservableProperty]
    public partial string PriceDisplay { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPriceVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPriceHistory))]
    public partial CardPriceData PriceData { get; set; } = CardPriceData.Empty;

    [ObservableProperty]
    public partial string CardPosition { get; set; } = "";

    [ObservableProperty]
    public partial Color RarityColor { get; set; } = Colors.Transparent;

    [ObservableProperty]
    public partial string CombinedText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsTextVisible { get; set; }

    [ObservableProperty]
    public partial string PTText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPTVisible { get; set; }

    [ObservableProperty]
    public partial string SetInfoText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSetSymbolVisible { get; set; }

    [ObservableProperty]
    public partial bool IsArtistVisible { get; set; }

    [ObservableProperty]
    public partial bool IsFlavorVisible { get; set; }

    [ObservableProperty]
    public partial List<PriceHistoryPoint> DisplayPriceHistory { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPurchaseLinks))]
    public partial List<PurchaseLink> PurchaseLinks { get; set; } = [];

    [ObservableProperty]
    public partial List<LegalityItem> Legalities { get; set; } = [];

    public ICommand OpenLinkCommand => new Command<string>(async (url) =>
    {
        if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Launcher.OpenAsync(uri);
        }
    });

    public bool HasPriceHistory => DisplayPriceHistory.Count > 0;

    public bool HasRulings => Card?.Rulings != null && Card.Rulings.Count > 0;

    public bool HasPurchaseLinks => PurchaseLinks.Count > 0;

    public bool HasMultipleFaces => Faces.Length > 1;

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
                UpdateCardDetails();
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

                // FILTER: Remove logic-only faces that don't have separate physical images
                // e.g. Adventure and Split cards. But KEEP tokens!
                // Tokens usually have layout == Token or their ScryfallId differs.
                // We keep a face if it's the main face, OR if its ScryfallId is different, OR if it's explicitly a Token, OR if it's a double-faced layout (Transform, MDFC).
                var filteredFaces = package.Where(face =>
                    face.UUID == uuid ||
                    face.Layout.IsDoubleFaced() ||
                    face.Layout == CardLayout.Token ||
                    face.ScryfallId != mainCard.ScryfallId
                ).ToArray();

                // Show primary face first, then others
                Faces = filteredFaces;
                CurrentFaceIndex = 0;
                for (int i = 0; i < filteredFaces.Length; i++)
                {
                    if (filteredFaces[i].UUID == uuid) { CurrentFaceIndex = i; break; }
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

            UpdateCardDetails();

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

    private void UpdateCardDetails()
    {
        if (CurrentFace == null) return;

        // Rarity color
        RarityColor = CurrentFace.Rarity switch
        {
            CardRarity.Common => Color.FromArgb("#C0C0C0"),
            CardRarity.Uncommon => Color.FromArgb("#B0C4DE"),
            CardRarity.Rare => Color.FromArgb("#FFD700"),
            CardRarity.Mythic => Color.FromArgb("#FF8C00"),
            _ => Color.FromArgb("#A0A0A0")
        };

        // Text
        CombinedText = GetCombinedText();
        IsTextVisible = !string.IsNullOrEmpty(CombinedText);

        // P/T
        var pt = CurrentFace.GetPowerToughness();
        if (!string.IsNullOrEmpty(pt))
        {
            PTText = pt;
            IsPTVisible = true;
        }
        else if (!string.IsNullOrEmpty(CurrentFace.Loyalty))
        {
            PTText = $"Loyalty: {CurrentFace.Loyalty}";
            IsPTVisible = true;
        }
        else if (!string.IsNullOrEmpty(CurrentFace.Defense))
        {
            PTText = $"Defense: {CurrentFace.Defense}";
            IsPTVisible = true;
        }
        else
        {
            PTText = "";
            IsPTVisible = false;
        }

        // Set Info
        SetInfoText = CurrentFace.GetSetAndNumber() + "\n" + CurrentFace.SetName;
        IsSetSymbolVisible = !string.IsNullOrEmpty(CurrentFace.SetCode) && SetSvgCache.GetSymbol(CurrentFace.SetCode) != null;

        // Flavor
        IsFlavorVisible = !string.IsNullOrEmpty(CurrentFace.FlavorText);

        // Artist
        IsArtistVisible = !string.IsNullOrEmpty(CurrentFace.Artist);

        // Purchase Links
        PurchaseLinks = GetPurchaseLinks();

        // Legalities
        Legalities = GetLegalityList();

        // Price History
        PopulateHistory();
    }

    private void PopulateHistory()
    {
        var points = new List<PriceHistoryPoint>();
        if (PriceData == CardPriceData.Empty)
        {
            DisplayPriceHistory = points;
            return;
        }

        var tcgHistory = PriceData.Paper.TCGPlayer.RetailNormalHistory;
        var cmHistory = PriceData.Paper.Cardmarket.RetailNormalHistory;

        if (tcgHistory.Count > 0)
            points.Add(new PriceHistoryPoint("TCGPlayer Retail", tcgHistory.Skip(Math.Max(0, tcgHistory.Count - 5)).ToList()));

        if (cmHistory.Count > 0)
            points.Add(new PriceHistoryPoint("Cardmarket Retail", cmHistory.Skip(Math.Max(0, cmHistory.Count - 5)).ToList()));

        DisplayPriceHistory = points;
        OnPropertyChanged(nameof(HasPriceHistory));
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
        if (!found) { PriceDisplay = ""; PriceData = CardPriceData.Empty; IsPriceVisible = false; return; }

        PriceData = prices;

        VendorPrices[] vendors = [prices.Paper.TCGPlayer, prices.Paper.Cardmarket, prices.Paper.CardKingdom];
        foreach (var v in vendors)
        {
            if (v.RetailNormal.Price > 0) { PriceDisplay = $"${v.RetailNormal.Price:F2}"; IsPriceVisible = true; return; }
            if (v.RetailFoil.Price > 0) { PriceDisplay = $"${v.RetailFoil.Price:F2} (Foil)"; IsPriceVisible = true; return; }
        }
        PriceDisplay = "";
        IsPriceVisible = false;
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
        UpdateCardDetails();
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

    public List<PurchaseLink> GetPurchaseLinks()
    {
        var links = new List<PurchaseLink>();
        if (Card == null) return links;

        if (!string.IsNullOrEmpty(Card.tcgplayer)) links.Add(new PurchaseLink("TCGPlayer", Card.tcgplayer));
        if (!string.IsNullOrEmpty(Card.tcgplayerEtched)) links.Add(new PurchaseLink("TCGPlayer \u2014 Etched", Card.tcgplayerEtched));
        if (!string.IsNullOrEmpty(Card.cardmarket)) links.Add(new PurchaseLink("Cardmarket", Card.cardmarket));
        if (!string.IsNullOrEmpty(Card.cardKingdom)) links.Add(new PurchaseLink("Card Kingdom", Card.cardKingdom));
        if (!string.IsNullOrEmpty(Card.cardKingdomFoil)) links.Add(new PurchaseLink("Card Kingdom \u2014 Foil", Card.cardKingdomFoil));
        if (!string.IsNullOrEmpty(Card.cardKingdomEtched)) links.Add(new PurchaseLink("Card Kingdom \u2014 Etched", Card.cardKingdomEtched));
        return links;
    }

    public List<LegalityItem> GetLegalityList()
    {
        var list = new List<LegalityItem>();
        if (Card == null) return list;

        foreach (DeckFormat fmt in Enum.GetValues<DeckFormat>())
        {
            var status = Card.Legalities[fmt];
            list.Add(new LegalityItem(fmt.ToDisplayName(), status));
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
