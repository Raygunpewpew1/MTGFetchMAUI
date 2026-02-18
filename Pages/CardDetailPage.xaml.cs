using MTGFetchMAUI.Core;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace MTGFetchMAUI.Pages;

[QueryProperty(nameof(CardUUID), "uuid")]
public partial class CardDetailPage : ContentPage
{
    private readonly CardDetailViewModel _viewModel;
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

    public CardDetailPage(CardDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Unloaded += (s, e) => _viewModel.Dispose();
    }

    private async Task LoadCard()
    {
        if (string.IsNullOrEmpty(_cardUUID)) return;

        await _viewModel.LoadCardAsync(_cardUUID);
        UpdateUI();
    }

    private void UpdateUI()
    {
        var card = _viewModel.CurrentFace;

        CardNameLabel.Text = card.Name;
        ManaCost.ManaText = card.ManaCost ?? "";
        TypeLineLabel.Text = card.CardType;
        SetInfoLabel.Text = card.GetSetAndNumber();

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
                ImageLoading.IsVisible = false;
                ImageLoading.IsRunning = false;
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

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        string result = await DisplayPromptAsync("Add to Collection", "How many copies?",
            initialValue: "1", keyboard: Keyboard.Numeric);
        if (int.TryParse(result, out int qty) && qty > 0)
        {
            _viewModel.AddToCollectionCommand.Execute(qty);
            await DisplayAlertAsync("Added", $"Added {qty}x {_viewModel.Card.Name} to collection", "OK");
        }
    }

    private async void OnRemoveClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlertAsync("Remove", $"Remove {_viewModel.Card.Name} from collection?", "Yes", "No");
        if (confirm)
            _viewModel.RemoveFromCollectionCommand.Execute(null);
    }

    private void OnLegalitiesClicked(object? sender, EventArgs e)
    {
        LegalitiesBorder.IsVisible = !LegalitiesBorder.IsVisible;
        if (LegalitiesBorder.IsVisible)
            PopulateLegalities();
    }

    private void OnFlipClicked(object? sender, EventArgs e)
    {
        _viewModel.FlipFaceCommand.Execute(null);
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
            RulingsBorder.IsVisible = true;
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
                stack.Add(new Label
                {
                    Text = ruling.Text,
                    FontSize = 13,
                    TextColor = Color.FromArgb("#E0E0E0"),
                    LineBreakMode = LineBreakMode.WordWrap
                });
                RulingsStack.Add(stack);
            }
        }
    }
}
