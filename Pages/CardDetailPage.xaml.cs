using AetherVault.Controls;
using AetherVault.Core;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace AetherVault.Pages;

[QueryProperty(nameof(CardUUID), "uuid")]
public partial class CardDetailPage : ContentPage
{
    private readonly CardDetailViewModel _viewModel;
    private readonly IToastService _toastService;
    private readonly DeckBuilderService _deckService;
    private string _cardUUID = "";
    private bool _isSwipeAnimating;

    public string CardUUID
    {
        get => _cardUUID;
        set
        {
            _cardUUID = value;
            _ = LoadCard();
        }
    }

    public CardDetailPage(CardDetailViewModel viewModel, IToastService toastService, DeckBuilderService deckService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toastService = toastService;
        _deckService = deckService;
        BindingContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Wire cross-platform swipe navigation via SwipeGestureContainer wrapper.
        // Left swipe = next card; Right swipe = previous card.
        SwipeContainer.SwipedLeft += () => _ = HandleSwipeAsync(isNext: true);
        SwipeContainer.SwipedRight += () => _ = HandleSwipeAsync(isNext: false);

        Unloaded += (s, e) => _viewModel.Dispose();
     //   _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private async Task LoadCard()
    {
        if (string.IsNullOrEmpty(_cardUUID)) return;

        await _viewModel.LoadCardAsync(_cardUUID);

        if (_viewModel.ShowGalleryNavigation)
            ShowSwipeHintIfNeededAsync();
    }

    private async void ShowSwipeHintIfNeededAsync()
    {
        if (Preferences.Default.Get("SwipeHintShown", false)) return;
        Preferences.Default.Set("SwipeHintShown", true);
        SwipeHintLabel.Opacity = 1;
        await SwipeHintLabel.FadeToAsync(0, 2500);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardDetailViewModel.CardImage))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CardImageView.InvalidateSurface();
            });
        }
        else if (e.PropertyName == nameof(CardDetailViewModel.CurrentFace))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_viewModel.IsSetSymbolVisible)
                    SetSymbolView.InvalidateSurface();
                CardImageView.InvalidateSurface();
            });
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

        if (string.IsNullOrEmpty(setCode)) return;

        var rarityColor = _viewModel.RarityColor.ToSKColor();

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

        if (result is CollectionAddResult r)
        {
            await _viewModel.AddToCollectionWithFinishAsync(r.NewQuantity, r.IsFoil, r.IsEtched);
            if (r.NewQuantity > 0)
                _toastService.Show($"{r.NewQuantity}x {_viewModel.Card.Name} in collection");
            else
                _toastService.Show($"{_viewModel.Card.Name} removed from collection");
        }
    }

    private async void OnAddToDeckClicked(object? sender, EventArgs e)
    {
        var page = new AddToDeckPage(_deckService, _viewModel.Card.UUID, _viewModel.Card.Name);
        await Navigation.PushModalAsync(page);
        var result = await page.WaitForResultAsync();

        if (result != null)
        {
            var validation = await _deckService.AddCardAsync(result.DeckId, _viewModel.Card.UUID, result.Quantity, result.Section);
            if (validation.IsError)
                _toastService.Show(validation.Message ?? "Could not add card to deck.");
            else
                _toastService.Show($"{result.Quantity}× {_viewModel.Card.Name} added to {result.DeckName}.");
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
        => _ = HandleSwipeAsync(isNext: true);

    private void OnSwipedRight(object? sender, SwipedEventArgs e)
        => _ = HandleSwipeAsync(isNext: false);

    private async Task HandleSwipeAsync(bool isNext)
    {
        if (_isSwipeAnimating)
            return;

        _isSwipeAnimating = true;

        try
        {
            // If gallery navigation is not active, just execute the command without animation.
            if (!_viewModel.ShowGalleryNavigation || CardContentRoot is null)
            {
                if (isNext)
                    _viewModel.NavigateNextCardCommand.Execute(null);
                else
                    _viewModel.NavigatePreviousCardCommand.Execute(null);

                return;
            }

            var width = DetailScreen.Width;
            if (width <= 0)
                width = Width;

            if (width <= 0)
            {
                if (isNext)
                    _viewModel.NavigateNextCardCommand.Execute(null);
                else
                    _viewModel.NavigatePreviousCardCommand.Execute(null);

                return;
            }

            var direction = isNext ? -1 : 1;
            var travel = Math.Max(48, width * 0.12);
            const uint duration = 140;

            // Slide current card slightly in swipe direction.
            await CardContentRoot.TranslateToAsync(direction * travel, 0, duration, Easing.CubicOut);

            // Trigger navigation while the content is offset.
            if (isNext)
                _viewModel.NavigateNextCardCommand.Execute(null);
            else
                _viewModel.NavigatePreviousCardCommand.Execute(null);

            // Prepare new content just off-screen on the opposite side.
            CardContentRoot.TranslationX = -direction * travel;

            // Slide new card into place.
            await CardContentRoot.TranslateToAsync(0, 0, duration, Easing.CubicIn);
        }
        finally
        {
            _isSwipeAnimating = false;
        }
    }

    private void CardImageView_Touch(object? sender, SKTouchEventArgs e)
    {
        if (e.ActionType == SKTouchAction.Released)
        {
            if (_viewModel.HasMultipleFaces)
            {
                _viewModel.FlipFaceCommand.Execute(null);
            }
        }
        e.Handled = true;
    }
}
