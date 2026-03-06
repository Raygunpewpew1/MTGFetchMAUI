using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace AetherVault.Pages;

[QueryProperty(nameof(DeckId), "deckId")]
public partial class DeckDetailPage : ContentPage
{
    private readonly DeckDetailViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly DeckBuilderService _deckService;
    private readonly IToastService _toastService;

    public string DeckId
    {
        set
        {
            if (int.TryParse(value, out int id))
                _ = _viewModel.LoadAsync(id);
        }
    }

    public DeckDetailPage(
        DeckDetailViewModel viewModel,
        IServiceProvider serviceProvider,
        DeckBuilderService deckService,
        IToastService toastService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        _deckService = deckService;
        _toastService = toastService;
        BindingContext = viewModel;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DeckDetailViewModel.Deck)
                               or nameof(DeckDetailViewModel.HasNoCommander))
            {
                CommanderArtCanvas?.InvalidateSurface();
            }
        };

    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ReloadCompleted += RunDeferredLayoutPass;
    }

    private async void OnAddCardClicked(object? sender, EventArgs e)
    {
        if (_viewModel.Deck == null) return;

        var pickerPage = _serviceProvider.GetRequiredService<CardSearchPickerPage>();
        await Navigation.PushModalAsync(new NavigationPage(pickerPage));
        var card = await pickerPage.WaitForResultAsync();

        if (card == null) return;

        int sectionIndex = _viewModel.SelectedSectionIndex;

        if (sectionIndex == 0) // Commander tab
        {
            var result = await _deckService.SetCommanderAsync(_viewModel.Deck.Id, card.UUID);
            if (result.IsError)
            {
                _viewModel.StatusIsError = true;
                _viewModel.StatusMessage = result.Message ?? "Could not set commander.";
                _toastService.Show(result.Message ?? "Could not set commander.");
            }
            else
            {
                _viewModel.StatusIsError = false;
                string message;
                if (result.IsWarning && !string.IsNullOrWhiteSpace(result.Message))
                {
                    // Soft warning: commander is set, but deck has color-identity issues.
                    message = result.Message;
                }
                else
                {
                    message = $"{card.Name} set as commander.";
                }

                _viewModel.StatusMessage = message;
                _toastService.Show(message);
            }
        }
        else
        {
            string section = sectionIndex == 2 ? "Sideboard" : "Main";

            // Reuse the add-to-deck modal to pick quantity/section with current deck preselected.
            var addPage = new AddToDeckPage(
                _deckService,
                card.UUID,
                card.Name,
                _viewModel.Deck.Id,
                section);

            await Navigation.PushModalAsync(addPage);
            var addResult = await addPage.WaitForResultAsync();

            if (addResult is null)
                return;

            var result = await _deckService.AddCardAsync(
                addResult.DeckId,
                card.UUID,
                addResult.Quantity,
                addResult.Section);

            if (result.IsError)
            {
                _viewModel.StatusIsError = true;
                _viewModel.StatusMessage = result.Message ?? "Could not add card.";
                _toastService.Show(result.Message ?? "Could not add card.");
            }
            else
            {
                _viewModel.StatusIsError = false;
                _viewModel.StatusMessage = $"{addResult.Quantity}× {card.Name} added to {addResult.Section}.";
                _toastService.Show($"{addResult.Quantity}× {card.Name} added to {addResult.Section}.");
                _viewModel.RegisterLastAdded(card.UUID, card.Name, addResult.Section, addResult.Quantity);
            }
        }

        await _viewModel.ReloadAsync(preserveState: true);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ReloadCompleted -= RunDeferredLayoutPass;
    }

    private const int DeferredLayoutDelayMs = 120;

    private void RunDeferredLayoutPass()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DeckDetailRoot.InvalidateMeasure();
            CommanderArtCanvas.InvalidateSurface();
        });
        _ = Task.Run(async () =>
        {
            await Task.Delay(DeferredLayoutDelayMs);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DeckDetailRoot.InvalidateMeasure();
                CommanderArtCanvas.InvalidateSurface();
            });
        });
    }

    private void OnCommanderArtPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float w = e.Info.Width;
        float h = e.Info.Height;

        if (w <= 0 || h <= 0) return;

        try
        {
            // Background gradient using deck color identity colors
            var colorIdentity = _viewModel.Deck?.ColorIdentity ?? "";
        var (topColor, bottomColor) = GetGradientColors(colorIdentity);

        using var gradPaint = new SKPaint();
        gradPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(w, h),
            [topColor, bottomColor],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, gradPaint);

        // Dark overlay at bottom for legibility
        using var overlayPaint = new SKPaint();
        overlayPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, h * 0.4f),
            new SKPoint(0, h),
            [SKColors.Transparent, new SKColor(0x12, 0x12, 0x12, 230)],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, overlayPaint);

            // Commander name or deck name text
            string headline = _viewModel.Deck?.CommanderName is { Length: > 0 } cn
                ? cn
                : (_viewModel.Deck?.Name ?? "");

            if (!string.IsNullOrEmpty(headline))
            {
                using var typeface = SKTypeface.FromFamilyName("sans-serif");
                using var textFont = new SKFont(typeface) { Size = Math.Min(h * 0.2f, 28f), Embolden = true, Subpixel = true };
                using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
                float textY = h - textFont.Size * 0.5f;
                canvas.DrawText(headline, 16f, textY, SKTextAlign.Left, textFont, textPaint);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error drawing commander art: {ex}");
        }
    }

    private static (SKColor top, SKColor bottom) GetGradientColors(string colorIdentity)
    {
        // Pick colors based on first 1-2 color identity chars
        bool w = colorIdentity.Contains('W');
        bool u = colorIdentity.Contains('U');
        bool b = colorIdentity.Contains('B');
        bool r = colorIdentity.Contains('R');
        bool g = colorIdentity.Contains('G');

        int count = (w ? 1 : 0) + (u ? 1 : 0) + (b ? 1 : 0) + (r ? 1 : 0) + (g ? 1 : 0);

        if (count == 0) return (new SKColor(0x44, 0x44, 0x55), new SKColor(0x22, 0x22, 0x33));
        if (count >= 3) return (new SKColor(0x8B, 0x6C, 0x1A), new SKColor(0x3B, 0x2C, 0x0A)); // gold/multicolor

        // Single or dual color
        SKColor primary = u ? new SKColor(0x1A, 0x5C, 0x9E)
                        : g ? new SKColor(0x1A, 0x6B, 0x3A)
                        : r ? new SKColor(0x9E, 0x1A, 0x1A)
                        : b ? new SKColor(0x3A, 0x2A, 0x6B)
                        : new SKColor(0x7A, 0x6A, 0x4A); // white -> tan

        SKColor secondary = count == 1 ? Darken(primary) :
                            u ? new SKColor(0x1A, 0x5C, 0x9E)
                              : g ? new SKColor(0x1A, 0x6B, 0x3A)
                              : r ? new SKColor(0x9E, 0x1A, 0x1A)
                              : b ? new SKColor(0x3A, 0x2A, 0x6B)
                              : new SKColor(0x7A, 0x6A, 0x4A);

        return (primary, Darken(secondary));
    }

    private static SKColor Darken(SKColor c) =>
        new SKColor((byte)(c.Red / 2), (byte)(c.Green / 2), (byte)(c.Blue / 2));
}
