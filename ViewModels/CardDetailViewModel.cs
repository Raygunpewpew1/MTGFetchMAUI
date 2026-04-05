using System.Text;
using System.Text.Json;
using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using SkiaSharp;
using System.Windows.Input;

namespace AetherVault.ViewModels;

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
    private readonly ICardImageSaveService _cardImageSave;
    private readonly IToastService _toast;

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
    public partial string PtText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPtVisible { get; set; }

    [ObservableProperty]
    public partial string SetInfoText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSetSymbolVisible { get; set; }

    [ObservableProperty]
    public partial bool IsArtistVisible { get; set; }

    [ObservableProperty]
    public partial bool IsFlavorVisible { get; set; }

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

    public bool HasRulings => Card?.Rulings != null && Card.Rulings.Count > 0;

    public bool HasPurchaseLinks => PurchaseLinks.Count > 0;

    public bool HasMultipleFaces => Faces.Length > 1;

    public bool ShowGalleryNavigation => _galleryContext.HasContext;

    public Card CurrentFace => Faces.Length > 0 && CurrentFaceIndex >= 0 && CurrentFaceIndex < Faces.Length
        ? Faces[CurrentFaceIndex]
        : Card;

    public event Action<string>? AddedToCollection;

    public CardDetailViewModel(
        CardManager cardManager,
        CardGalleryContext galleryContext,
        ICardImageSaveService cardImageSave,
        IToastService toast)
    {
        _cardManager = cardManager;
        _galleryContext = galleryContext;
        _cardImageSave = cardImageSave;
        _toast = toast;
        _cardManager.OnPricesUpdated += HandlePricesUpdated;
    }

    private async void HandlePricesUpdated()
    {
        if (Card != null && !string.IsNullOrEmpty(Card.Uuid))
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
            return await _cardManager.GetQuantityAsync(Card.Uuid);
        }
        catch
        {
            return 0;
        }
    }

    public async Task LoadCardAsync(string uuid)
    {
        #region agent log
        AgentDebugLog("initial", "H3", "ViewModels/CardDetailViewModel.cs:LoadCardAsync:entry", "Card detail load entered", new
        {
            uuid
        });
        #endregion
        if (!await _cardManager.EnsureInitializedAsync()) return;

        IsBusy = true;

        try
        {
            CardImage = null;
            CardImageLoadFailed = false;

            // Load full card with rulings
            var mainCard = await _cardManager.GetCardWithRulingsAsync(uuid);
            Card = mainCard;
            #region agent log
            AgentDebugLog("initial", "H3", "ViewModels/CardDetailViewModel.cs:LoadCardAsync:main-card", "Main card loaded", new
            {
                requestedUuid = uuid,
                loadedUuid = mainCard.Uuid,
                layout = mainCard.Layout.ToString(),
                hasScryfall = !string.IsNullOrEmpty(mainCard.ScryfallId)
            });
            #endregion

            // Load faces
            var package = await _cardManager.GetFullCardPackageAsync(uuid);
            #region agent log
            AgentDebugLog("initial", "H4", "ViewModels/CardDetailViewModel.cs:LoadCardAsync:package", "Full card package loaded", new
            {
                requestedUuid = uuid,
                packageLength = package.Length
            });
            #endregion
            if (package.Length > 0)
            {
                // MELD CARD FILTER: Only show current card + meld result (not the other piece)
                // This makes meld cards behave like transform cards (front/back only)
                if (package.Length > 2 && mainCard.Layout == CardLayout.Meld)
                {
                    var filtered = new List<Card>();
                    // Add current piece
                    var current = package.FirstOrDefault(f => f.Uuid == uuid);
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
                    face.Uuid == uuid ||
                    face.Layout.IsDoubleFaced() ||
                    face.Layout == CardLayout.Token ||
                    face.ScryfallId != mainCard.ScryfallId
                ).ToArray();

                // Show primary face first, then others
                Faces = filteredFaces;
                CurrentFaceIndex = 0;
                for (int i = 0; i < filteredFaces.Length; i++)
                {
                    if (filteredFaces[i].Uuid == uuid) { CurrentFaceIndex = i; break; }
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
            #region agent log
            AgentDebugLog("initial", "H3", "ViewModels/CardDetailViewModel.cs:LoadCardAsync:error", "Card detail load exception", new
            {
                uuid,
                error = ex.Message
            });
            #endregion
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
            PtText = pt;
            IsPtVisible = true;
        }
        else if (!string.IsNullOrEmpty(CurrentFace.Loyalty))
        {
            PtText = $"Loyalty: {CurrentFace.Loyalty}";
            IsPtVisible = true;
        }
        else if (!string.IsNullOrEmpty(CurrentFace.Defense))
        {
            PtText = $"Defense: {CurrentFace.Defense}";
            IsPtVisible = true;
        }
        else
        {
            PtText = "";
            IsPtVisible = false;
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

        var faceParam = GetCurrentImageFaceParam();

        var cached = await _cardManager.GetCachedCardImageAsync(currentFace.ScryfallId, MtgConstants.ImageSizeNormal, faceParam);
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
        }, MtgConstants.ImageSizeNormal, faceParam);
    }

    private async Task LoadPriceAsync()
    {
        if (!PricePreferences.PricesDataEnabled)
        {
            PriceDisplay = "";
            PriceData = CardPriceData.Empty;
            IsPriceVisible = false;
            return;
        }

        var (found, prices) = await _cardManager.GetCardPricesAsync(Card.Uuid);
        if (!found) { PriceDisplay = ""; PriceData = CardPriceData.Empty; IsPriceVisible = false; return; }

        PriceData = prices;

        var display = PriceDisplayHelper.GetDisplayPrice(prices, preferFoilLabel: true, preferEtchedLabel: true);
        if (!string.IsNullOrEmpty(display))
        {
            PriceDisplay = display;
            IsPriceVisible = true;
        }
        else
        {
            PriceDisplay = "";
            IsPriceVisible = false;
        }
    }

    partial void OnCardImageChanging(SKImage? value)
    {
        CardImage?.Dispose();
    }

    /// <summary>Scryfall CDN face query: <c>front</c> or <c>back</c>.</summary>
    private string GetCurrentImageFaceParam()
    {
        var currentFace = CurrentFace;
        if (string.IsNullOrEmpty(currentFace.ScryfallId)) return "front";

        var faceParam = "front";
        if (currentFace.Side is 'b' or 'c')
        {
            if (Faces.Length > 0 && currentFace.ScryfallId == Faces[0].ScryfallId)
                faceParam = "back";
        }

        return faceParam;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "card";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Trim().Length);
        foreach (var c in name.Trim())
            sb.Append(invalid.Contains(c) ? '_' : c);
        var s = sb.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "card" : s;
    }

    private string BuildPngFileNameForCurrentFace()
    {
        var name = SanitizeFileName(CurrentFace.Name);
        if (name.Length > 80)
            name = name[..80];
        var id = CurrentFace.ScryfallId;
        var id8 = id.Length >= 8 ? id[..8] : id;
        var back = GetCurrentImageFaceParam() == "back" ? "_back" : "";
        return $"{name}_{id8}{back}.png";
    }

    private static string BuildScryfallPageUrl(Card face)
    {
        if (!string.IsNullOrWhiteSpace(face.SetCode) && !string.IsNullOrWhiteSpace(face.Number))
        {
            var set = face.SetCode.Trim().ToLowerInvariant();
            var num = face.Number.Trim();
            return $"https://scryfall.com/card/{Uri.EscapeDataString(set)}/{Uri.EscapeDataString(num)}";
        }

        if (!string.IsNullOrWhiteSpace(face.ScryfallId))
            return $"https://scryfall.com/card/{Uri.EscapeDataString(face.ScryfallId)}";

        return "https://scryfall.com";
    }

    private string BuildShareText()
    {
        var face = CurrentFace;
        var sb = new StringBuilder();
        sb.AppendLine(face.Name);
        if (!string.IsNullOrWhiteSpace(face.ManaCost))
            sb.AppendLine(face.ManaCost);
        sb.AppendLine(face.CardType);
        if (!string.IsNullOrWhiteSpace(face.SetCode) || !string.IsNullOrWhiteSpace(face.Number))
            sb.AppendLine(face.GetSetAndNumber());
        if (!string.IsNullOrWhiteSpace(face.SetName))
            sb.AppendLine(face.SetName);
        sb.AppendLine();
        sb.Append(BuildScryfallPageUrl(face));
        return sb.ToString().TrimEnd();
    }

    [RelayCommand]
    private async Task SaveCardImage()
    {
        var currentFace = CurrentFace;
        if (string.IsNullOrEmpty(currentFace.ScryfallId))
        {
            _toast.Show(UserMessages.SaveCardImageNoId);
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = UserMessages.StatusClear;
        try
        {
            var faceParam = GetCurrentImageFaceParam();
            var bytes = await _cardManager.DownloadCardImageBytesAsync(
                currentFace.ScryfallId,
                MtgConstants.ImageSizePng,
                faceParam);

            if (bytes == null || bytes.Length == 0)
            {
                StatusIsError = true;
                StatusMessage = UserMessages.SaveCardImageFailed;
                _toast.Show(UserMessages.SaveCardImageFailed);
                return;
            }

            var fileName = BuildPngFileNameForCurrentFace();
            var (ok, err) = await _cardImageSave.SavePngToGalleryAsync(bytes, fileName);
            if (ok)
            {
                _toast.Show(UserMessages.SaveCardImageSuccess);
            }
            else
            {
                StatusIsError = true;
                StatusMessage = string.IsNullOrEmpty(err) ? UserMessages.SaveCardImageFailed : err;
                _toast.Show(StatusMessage);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShareCardDetails()
    {
        if (string.IsNullOrEmpty(Card.Uuid)) return;

        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = CurrentFace.Name,
                Subject = CurrentFace.Name,
                Text = BuildShareText()
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogStuff($"Share card failed: {ex.Message}", LogLevel.Warning);
            _toast.Show(UserMessages.ShareCardFailed);
        }
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
        await _cardManager.AddCardToCollectionAsync(Card.Uuid, quantity);
        IsInCollection = true;
        AddedToCollection?.Invoke(Card.Uuid);
    }

    public async Task AddToCollectionWithFinishAsync(int quantity, bool isFoil, bool isEtched)
    {
        await _cardManager.UpdateCardQuantityAsync(Card.Uuid, quantity, isFoil, isEtched);
        IsInCollection = quantity > 0;
        AddedToCollection?.Invoke(Card.Uuid);
    }

    [RelayCommand]
    private async Task RemoveFromCollection()
    {
        await _cardManager.RemoveCardFromCollectionAsync(Card.Uuid);
        IsInCollection = false;
    }

    public List<PurchaseLink> GetPurchaseLinks()
    {
        var links = new List<PurchaseLink>();
        if (Card == null) return links;

        if (!string.IsNullOrEmpty(Card.Tcgplayer)) links.Add(new PurchaseLink("TCGPlayer", Card.Tcgplayer));
        if (!string.IsNullOrEmpty(Card.TcgplayerEtched)) links.Add(new PurchaseLink("TCGPlayer \u2014 Etched", Card.TcgplayerEtched));
        if (!string.IsNullOrEmpty(Card.Cardmarket)) links.Add(new PurchaseLink("Cardmarket", Card.Cardmarket));
        if (!string.IsNullOrEmpty(Card.CardKingdom)) links.Add(new PurchaseLink("Card Kingdom", Card.CardKingdom));
        if (!string.IsNullOrEmpty(Card.CardKingdomFoil)) links.Add(new PurchaseLink("Card Kingdom \u2014 Foil", Card.CardKingdomFoil));
        if (!string.IsNullOrEmpty(Card.CardKingdomEtched)) links.Add(new PurchaseLink("Card Kingdom \u2014 Etched", Card.CardKingdomEtched));
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
            CardLayout.ModalDfc => "\n\u2015\u2015\u2015 // \u2015\u2015\u2015\n",
            CardLayout.Adventure => "\n\u2015\u2015\u2015 \u2694 Adventure \u2694 \u2015\u2015\u2015\n",
            CardLayout.Split => "\n\u2015\u2015\u2015 // \u2015\u2015\u2015\n",
            _ => "\n\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\n"
        };

        return string.Join(separator, parts);
    }

    private static void AgentDebugLog(string runId, string hypothesisId, string location, string message, object data)
    {
        try
        {
            var dataJson = JsonSerializer.Serialize(data);
            Logger.LogStuff($"DBG|session=068b48|run={runId}|h={hypothesisId}|loc={location}|msg={message}|data={dataJson}", LogLevel.Info);
        }
        catch
        {
            // Never fail app flow for debug logging.
        }
    }
}
