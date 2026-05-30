using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using System.Text;
using System.Windows.Input;

namespace AetherVault.ViewModels;

public record PurchaseLink(string Label, string Url);

/// <summary>Keyword from MTGJSON <c>keywords</c> plus a short in-app summary (or fallback text).</summary>
public record KeywordHelpRow(string Keyword, string Summary);

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
    private readonly DeckSynergyNavigationContext _deckSynergyNavigationContext;
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
    [NotifyPropertyChangedFor(nameof(HasOtherPrintings))]
    public partial List<OtherPrintingSummary> OtherPrintings { get; set; } = [];

    public bool HasOtherPrintings => OtherPrintings.Count > 0;

    [ObservableProperty]
    public partial bool IsArtistVisible { get; set; }

    [ObservableProperty]
    public partial bool IsFlavorVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPurchaseLinks))]
    public partial List<PurchaseLink> PurchaseLinks { get; set; } = [];

    [ObservableProperty]
    public partial List<LegalityItem> Legalities { get; set; } = [];

    /// <summary>Union of <see cref="Card.Keywords"/> across all loaded faces, with glossary text where available.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeywordHelp))]
    public partial List<KeywordHelpRow> KeywordHelpRows { get; set; } = [];

    public bool HasKeywordHelp => KeywordHelpRows.Count > 0;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDeckSynergyHint))]
    public partial string DeckSynergyHintLine { get; set; } = "";

    public bool ShowDeckSynergyHint => !string.IsNullOrEmpty(DeckSynergyHintLine);

    public Card CurrentFace => Faces.Length > 0 && CurrentFaceIndex >= 0 && CurrentFaceIndex < Faces.Length
        ? Faces[CurrentFaceIndex]
        : Card;

    public event Action<string>? AddedToCollection;

    public CardDetailViewModel(
        CardManager cardManager,
        CardGalleryContext galleryContext,
        DeckSynergyNavigationContext deckSynergyNavigationContext,
        ICardImageSaveService cardImageSave,
        IToastService toast)
    {
        _cardManager = cardManager;
        _galleryContext = galleryContext;
        _deckSynergyNavigationContext = deckSynergyNavigationContext;
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

            await LoadOtherPrintingsAsync(uuid);
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
        if (CurrentFace == null)
        {
            DeckSynergyHintLine = "";
            return;
        }

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

        KeywordHelpRows = BuildKeywordHelpRows(Faces);

        DeckSynergyHintLine = _deckSynergyNavigationContext.GetOverlapHint(CurrentFace) ?? "";
    }

    private async Task LoadOtherPrintingsAsync(string currentUuid)
    {
        var oracleId = Card.ScryfallOracleId;
        if (string.IsNullOrWhiteSpace(oracleId))
        {
            OtherPrintings = [];
            return;
        }

        try
        {
            var rows = await _cardManager.GetOtherPrintingsAsync(oracleId, currentUuid);
            OtherPrintings = rows.Count > 0 ? [.. rows] : [];
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Other printings load failed: {ex.Message}", LogLevel.Warning);
            OtherPrintings = [];
        }
    }

    [RelayCommand]
    private async Task OpenOtherPrinting(OtherPrintingSummary? printing)
    {
        if (printing is null || string.IsNullOrEmpty(printing.Uuid))
            return;

        await LoadCardAsync(printing.Uuid);
    }

    private static List<KeywordHelpRow> BuildKeywordHelpRows(Card[] faces)
    {
        if (faces.Length == 0)
            return [];

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var face in faces)
        {
            foreach (var k in face.GetKeywordsArray())
            {
                if (!string.IsNullOrWhiteSpace(k))
                    set.Add(k.Trim());
            }
        }

        if (set.Count == 0)
            return [];

        const string fallback =
            "No glossary entry — see reminder text on the card or the Comprehensive Rules.";

        return set
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k => new KeywordHelpRow(k, KeywordAbilityGlossary.TryGetSummary(k) ?? fallback))
            .ToList();
    }

    private void UpdateGalleryState()
    {
        var pos = _galleryContext.GetPositionText();
        CardPosition = string.IsNullOrEmpty(pos) ? "" : $"‹  {pos}  ›";
        OnPropertyChanged(nameof(ShowGalleryNavigation));
    }

    /// <param name="awaitRemoteDownload">When true (e.g. mid flip animation), wait for the CDN callback so the new face art is ready.</param>
    private async Task LoadCardImageAsync(bool awaitRemoteDownload = false)
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
            CardImageLoadFailed = false;
            return;
        }

        CardImageLoadFailed = false;
        if (!awaitRemoteDownload)
        {
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
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _cardManager.DownloadCardImageAsync(currentFace.ScryfallId, (image, success) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (success && image != null)
                        CardImage = image;
                    else
                        CardImageLoadFailed = true;
                }
                finally
                {
                    tcs.TrySetResult();
                }
            });
        }, MtgConstants.ImageSizeNormal, faceParam);
        await tcs.Task;
    }

    /// <summary>Advances to the next face and loads its image (awaiting download when needed). Used by card-detail flip animation.</summary>
    public async Task AdvanceFaceAndLoadImageAsync()
    {
        if (Faces.Length <= 1) return;
        CurrentFaceIndex = (CurrentFaceIndex + 1) % Faces.Length;
        UpdateCardDetails();
        await LoadCardImageAsync(awaitRemoteDownload: true);
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
    private async Task FlipFace()
    {
        await AdvanceFaceAndLoadImageAsync();
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

    /// <summary>When set (e.g. card detail opened as a modal from the deck editor), <see cref="BackCommand"/> uses this instead of Shell navigation.</summary>
    public Func<Task>? BackNavigationAsync { get; set; }

    [RelayCommand]
    private async Task Back()
    {
        if (BackNavigationAsync != null)
            await BackNavigationAsync.Invoke();
        else
            await Shell.Current.GoToAsync("..");
    }
}
