using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using TextAlignment = Microsoft.Maui.TextAlignment;

namespace MTGFetchMAUI.Pages;

[QueryProperty(nameof(CardUUID), "uuid")]
public partial class CardDetailPage : ContentPage
{
    private readonly CardDetailViewModel _viewModel;
    private readonly IToastService _toastService;
    private string _cardUUID = "";

    public string CardUUID
    {
        get => _cardUUID;
        set
        {
            _cardUUID = value;
            _ = LoadCard();
        }
    }

    public CardDetailPage(CardDetailViewModel viewModel, IToastService toastService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toastService = toastService;
        BindingContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Wire cross-platform swipe navigation via SwipeGestureContainer wrapper.
        // Left swipe = next card; Right swipe = previous card.
        SwipeContainer.SwipedLeft += () => _viewModel.NavigateNextCardCommand.Execute(null);
        SwipeContainer.SwipedRight += () => _viewModel.NavigatePreviousCardCommand.Execute(null);

        Unloaded += (s, e) => _viewModel.Dispose();
    }

    private async Task LoadCard()
    {
        if (string.IsNullOrEmpty(_cardUUID)) return;

        await _viewModel.LoadCardAsync(_cardUUID);
        UpdateUI();
    }

    private void ResetCardContent()
    {
        PTLabel.IsVisible = false;
        PriceLabel.IsVisible = false;
        TextBorder.IsVisible = false;
        FlavorBorder.IsVisible = false;
        ArtistLabel.IsVisible = false;
        PriceHistoryBorder.IsVisible = false;
    }

    private void UpdateUI()
    {
        ResetCardContent();
        var card = _viewModel.CurrentFace;

        CardNameLabel.Text = card.Name;
        ManaCost.ManaText = card.ManaCost ?? "";
        TypeLineLabel.Text = card.CardType;

        SetInfoLabel.Text = card.GetSetAndNumber() + "\n" + card.SetName;

        // Set Symbol
        SetSymbolView.IsVisible = !string.IsNullOrEmpty(card.SetCode) && SetSvgCache.GetSymbol(card.SetCode) != null;
        if (SetSymbolView.IsVisible)
            SetSymbolView.InvalidateSurface();

        // Rarity with color
        RarityLabel.Text = card.Rarity.ToString();
        RarityLabel.TextColor = card.Rarity switch
        {
            CardRarity.Common => Color.FromArgb("#C0C0C0"),
            CardRarity.Uncommon => Color.FromArgb("#B0C4DE"),
            CardRarity.Rare => Color.FromArgb("#FFD700"),
            CardRarity.Mythic => Color.FromArgb("#FF8C00"),
            _ => Color.FromArgb("#A0A0A0")
        };

        // Price
        if (!string.IsNullOrEmpty(_viewModel.PriceDisplay))
        {
            PriceLabel.Text = _viewModel.PriceDisplay;
            PriceLabel.IsVisible = true;
        }

        // P/T or Loyalty
        var pt = card.GetPowerToughness();
        if (!string.IsNullOrEmpty(pt))
        {
            PTLabel.Text = pt;
            PTLabel.IsVisible = true;
        }
        else if (!string.IsNullOrEmpty(card.Loyalty))
        {
            PTLabel.Text = $"Loyalty: {card.Loyalty}";
            PTLabel.IsVisible = true;
        }
        else if (!string.IsNullOrEmpty(card.Defense))
        {
            PTLabel.Text = $"Defense: {card.Defense}";
            PTLabel.IsVisible = true;
        }

        // Card text (SkiaSharp rendered with keywords + symbols)
        string text = _viewModel.GetCombinedText();
        if (!string.IsNullOrEmpty(text))
        {
            CardTextView.CardText = text;
            TextBorder.IsVisible = true;
        }

        // Flavor text
        if (!string.IsNullOrEmpty(card.FlavorText))
        {
            FlavorTextLabel.Text = card.FlavorText;
            FlavorBorder.IsVisible = true;
        }

        // Artist
        if (!string.IsNullOrEmpty(card.Artist))
        {
            ArtistLabel.Text = $"Illustrated by {card.Artist}";
            ArtistLabel.IsVisible = true;
        }

        // Multi-face controls
        FlipBtn.IsVisible = _viewModel.HasMultipleFaces;
        FlipHint.IsVisible = _viewModel.HasMultipleFaces;

        // Collection status
        RemoveBtn.IsVisible = _viewModel.IsInCollection;

        // Price History
        PopulateHistory();

        PopulatePurchaseLinks();

        PopulateLegalities();

        // Image
        ImageLoading.IsVisible = _viewModel.CardImage == null;
        ImageLoading.IsRunning = _viewModel.CardImage == null;
        CardImageView.InvalidateSurface();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardDetailViewModel.CardImage))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                bool hasImage = _viewModel.CardImage != null;
                ImageLoading.IsVisible = !hasImage;
                ImageLoading.IsRunning = !hasImage;
                CardImageView.InvalidateSurface();
            });
        }
        else if (e.PropertyName == nameof(CardDetailViewModel.CurrentFace))
        {
            MainThread.BeginInvokeOnMainThread(UpdateUI);
        }
        else if (e.PropertyName == nameof(CardDetailViewModel.IsInCollection))
        {
            MainThread.BeginInvokeOnMainThread(() => RemoveBtn.IsVisible = _viewModel.IsInCollection);
        }
        else if (e.PropertyName == nameof(CardDetailViewModel.PriceData))
        {
            MainThread.BeginInvokeOnMainThread(UpdateUI);
        }
    }

    private void OnCardImagePaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(30, 30, 30));

        var image = _viewModel.CardImage;
        if (image == null) return;

        var info = e.Info;
        float scale = Math.Min((float)info.Width / image.Width, (float)info.Height / image.Height);
        float x = (info.Width - image.Width * scale) / 2f;
        float y = (info.Height - image.Height * scale) / 2f;

        var destRect = new SKRect(x, y, x + image.Width * scale, y + image.Height * scale);
        using var paint = new SKPaint { IsAntialias = true };
        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        canvas.DrawImage(image, destRect, sampling, paint);
    }

    private void OnSetSymbolPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(); // Transparent

        var setCode = _viewModel.CurrentFace?.SetCode;
        var Rarity = _viewModel.CurrentFace?.Rarity;

        if (string.IsNullOrEmpty(setCode)) return;


        var rarityColor = Rarity switch
        {
            CardRarity.Common => SKColors.White,
            CardRarity.Uncommon => new SKColor(180, 192, 203), // silver
            CardRarity.Rare => new SKColor(200, 170, 80),      // gold
            CardRarity.Mythic => new SKColor(230, 100, 30),    // orange
            _ => SKColors.White
        };

        SetSvgCache.DrawSymbol(canvas, setCode, e.Info.Rect, rarityColor);
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        int currentQty = await _viewModel.GetCollectionQuantityAsync();

        var page = new CollectionAddPage(
            _viewModel.Card.Name,
            $"{_viewModel.Card.SetCode} #{_viewModel.Card.Number}",
            currentQty);

        await Navigation.PushModalAsync(page);
        var result = await page.WaitForResultAsync();

        if (result is int quantity)
        {
            _viewModel.AddToCollectionCommand.Execute(quantity);
            _toastService.Show($"{quantity}x {_viewModel.Card.Name} in collection");
        }
    }

    private async void OnRemoveClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlertAsync("Remove", $"Remove {_viewModel.Card.Name} from collection?", "Yes", "No");
        if (confirm)
            _viewModel.RemoveFromCollectionCommand.Execute(null);
    }

    private void OnFlipClicked(object? sender, EventArgs e)
    {
        _viewModel.FlipFaceCommand.Execute(null);
    }

    private void OnSwipedLeft(object? sender, SwipedEventArgs e)
        => _viewModel.NavigateNextCardCommand.Execute(null);

    private void OnSwipedRight(object? sender, SwipedEventArgs e)
        => _viewModel.NavigatePreviousCardCommand.Execute(null);

    private void PopulateHistory()
    {
        HistoryStack.Children.Clear();
        var data = _viewModel.PriceData;
        if (data == CardPriceData.Empty)
        {
            PriceHistoryBorder.IsVisible = false;
            return;
        }

        // We'll show a sample of history (last 5 entries) to prove it works
        var tcgHistory = data.Paper.TCGPlayer.RetailNormalHistory;
        var cmHistory = data.Paper.Cardmarket.RetailNormalHistory;

        bool added = false;
        if (tcgHistory.Count > 0)
        {
            added = true;
            AddHistoryItems("TCGPlayer Retail", tcgHistory);
        }

        if (cmHistory.Count > 0)
        {
            added = true;
            AddHistoryItems("Cardmarket Retail", cmHistory);
        }

        PriceHistoryBorder.IsVisible = added;
    }

    private void AddHistoryItems(string label, List<PriceEntry> history)
    {
        HistoryStack.Add(new Label { Text = label, FontSize = 13, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 4, 0, 2) });

        // Take last 5 points
        var points = history.Skip(Math.Max(0, history.Count - 5)).ToList();
        foreach (var entry in points)
        {
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
            row.Add(new Label { Text = entry.Date.ToShortDateString(), FontSize = 12, TextColor = Color.FromArgb("#A0A0A0") });
            row.Add(new Label { Text = $"${entry.Price:F2}", FontSize = 12, HorizontalTextAlignment = TextAlignment.End }, 1);
            HistoryStack.Add(row);
        }
    }

    private void PopulatePurchaseLinks()
    {
        PurchaseLinksStack.Children.Clear();
        foreach (var (label, url) in _viewModel.GetPurchaseLinks())
        {
            var link = new Label
            {
                Text = label,
                FontSize = 14,
                TextColor = Color.FromArgb("#6CB4E4"),
                TextDecorations = TextDecorations.Underline
            };
            link.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () =>
                {
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                        await Launcher.OpenAsync(uri);
                })
            });
            PurchaseLinksStack.Children.Add(link);
        }
    }

    private void PopulateLegalities()
    {
        LegalitiesStack.Children.Clear();

        foreach (var (format, status) in _viewModel.GetLegalityList())
        {
            var row = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                Padding = new Thickness(0, 2)
            };

            row.Add(new Label
            {
                Text = format,
                FontSize = 13,
                TextColor = Color.FromArgb("#A0A0A0")
            }, 0);

            var statusColor = status switch
            {
                LegalityStatus.Legal => Color.FromArgb("#4CAF50"),
                LegalityStatus.Banned => Color.FromArgb("#F44336"),
                LegalityStatus.Restricted => Color.FromArgb("#FFC107"),
                _ => Color.FromArgb("#666666")
            };

            row.Add(new Label
            {
                Text = status switch
                {
                    LegalityStatus.Legal => "Legal",
                    LegalityStatus.Banned => "Banned",
                    LegalityStatus.Restricted => "Restricted",
                    _ => "Not Legal"
                },
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = statusColor,
                HorizontalTextAlignment = TextAlignment.End
            }, 1);

            LegalitiesStack.Add(row);
        }

        // Rulings
        if (_viewModel.Card.Rulings.Count > 0)
        {
            //  RulingsBorder.IsVisible = true;
            RulingsStack.Children.Clear();

            foreach (var ruling in _viewModel.Card.Rulings)
            {
                var stack = new VerticalStackLayout { Spacing = 2 };
                stack.Add(new Label
                {
                    Text = ruling.GetFormattedDate(),
                    FontSize = 11,
                    TextColor = Color.FromArgb("#888888")
                });
                stack.Add(new CardTextView
                {
                    CardText = ruling.Text,
                    TextSize = 13,
                    TextColor = Color.FromRgb(224, 224, 224),
                    SymbolSize = 14,
                    BackgroundColor = Colors.Transparent,
                    HorizontalOptions = LayoutOptions.Fill
                });
                RulingsStack.Add(stack);
            }
        }
    }

    private void CardImageView_Touch(object sender, SKTouchEventArgs e)
    {

    }
}
