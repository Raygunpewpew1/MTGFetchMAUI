using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.Services.ImportExport;
using AetherVault.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Text;

namespace AetherVault.Pages;

[QueryProperty(nameof(DeckId), "deckId")]
public partial class DeckDetailPage : ContentPage
{
    private readonly DeckDetailViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly DeckBuilderService _deckService;
    private readonly DeckExporter _deckExporter;
    private readonly ImageDownloadService _imageDownloadService;

    private SKImage? _commanderArtImage;

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
        DeckExporter deckExporter,
        ImageDownloadService imageDownloadService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        _deckService = deckService;
        _deckExporter = deckExporter;
        _imageDownloadService = imageDownloadService;
        BindingContext = viewModel;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DeckDetailViewModel.Deck)
                               or nameof(DeckDetailViewModel.HasNoCommander))
            {
                _ = TryLoadCommanderArtAsync();
                CommanderArtCanvas?.InvalidateSurface();
            }
        };
    }

    private async void OnExportDeckClicked(object? sender, EventArgs e)
    {
        if (_viewModel.Deck == null) return;

        try
        {
            _viewModel.IsBusy = true;
            _viewModel.StatusIsError = false;
            _viewModel.StatusMessage = UserMessages.ExportingDeck;

            var csvText = await _deckExporter.ExportDeckToCsvAsync(_viewModel.Deck.Id);
            if (string.IsNullOrWhiteSpace(csvText))
            {
                _viewModel.StatusIsError = false;
                _viewModel.StatusMessage = UserMessages.NothingToExport;
                return;
            }

            var safeName = string.Join("_",
                (_viewModel.Deck.Name ?? "deck")
                    .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
                .Trim();

            if (string.IsNullOrWhiteSpace(safeName)) safeName = "deck";

            var cacheFile = Path.Combine(FileSystem.CacheDirectory, $"{safeName}_export.csv");
            await File.WriteAllTextAsync(cacheFile, csvText, Encoding.UTF8);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Deck",
                File = new ShareFile(cacheFile)
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to export deck: {ex.Message}", LogLevel.Error);
            _viewModel.StatusIsError = true;
            _viewModel.StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async Task TryLoadCommanderArtAsync()
    {
        var first = _viewModel.CommanderCards.FirstOrDefault();
        if (first?.Card == null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _commanderArtImage?.Dispose();
                _commanderArtImage = null;
                CommanderArtCanvas?.InvalidateSurface();
            });
            return;
        }

        string imageId = first.Card.ImageId;
        if (string.IsNullOrEmpty(imageId)) return;

        try
        {
            var image = await _imageDownloadService.DownloadImageDirectAsync(imageId, "art_crop", "front");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var currentFirst = _viewModel.CommanderCards.FirstOrDefault();
                if (currentFirst?.Card?.ImageId != imageId)
                {
                    image?.Dispose();
                    return;
                }
                _commanderArtImage?.Dispose();
                _commanderArtImage = image;
                CommanderArtCanvas?.InvalidateSurface();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Commander art load failed: {ex.Message}");
        }
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
                _viewModel.StatusMessage = result.Message ?? UserMessages.CouldNotSetCommander();
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
            }
        }
        else
        {
            string section = sectionIndex == 2 ? "Sideboard" : "Main";

            var addPage = _serviceProvider.GetRequiredService<AddToDeckPage>();
            addPage.CardUuid = card.UUID;
            addPage.CardName = card.Name;
            addPage.InitialDeckId = _viewModel.Deck.Id;
            addPage.InitialSection = section;
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
                _viewModel.StatusMessage = result.Message ?? UserMessages.CouldNotAddCardToDeck();
            }
            else
            {
                _viewModel.StatusIsError = false;
                _viewModel.StatusMessage = UserMessages.CardsAddedToSection(addResult.Quantity, card.Name, addResult.Section);
                _viewModel.RegisterLastAdded(card.UUID, card.Name, addResult.Section, addResult.Quantity);
            }
        }

        await _viewModel.ReloadAsync(preserveState: true);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ReloadCompleted -= RunDeferredLayoutPass;
        _commanderArtImage?.Dispose();
        _commanderArtImage = null;
    }

    /// <summary>Delay so invalidate runs after WindowManager destroys modal surface (logcat: Destroying surface → focus change).</summary>
    private const int DeferredLayoutDelayMs = 220;

    private void RunDeferredLayoutPass()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            (Content as View)?.InvalidateMeasure();
            DeckDetailRoot.InvalidateMeasure();
            CommanderArtCanvas.InvalidateSurface();
        });
        _ = Task.Run(async () =>
        {
            await Task.Delay(DeferredLayoutDelayMs);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                (Content as View)?.InvalidateMeasure();
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
            if (_commanderArtImage != null)
            {
                float imgW = _commanderArtImage.Width;
                float imgH = _commanderArtImage.Height;
                if (imgW > 0 && imgH > 0)
                {
                    float scale = Math.Max(w / imgW, h / imgH);
                    float drawW = imgW * scale;
                    float drawH = imgH * scale;
                    float x = (w - drawW) / 2f;
                    float y = (h - drawH) / 2f;
                    canvas.DrawImage(_commanderArtImage, new SKRect(x, y, x + drawW, y + drawH));
                }
            }

            var colorIdentity = _viewModel.Deck?.ColorIdentity ?? "";
            var (topColor, bottomColor) = GetGradientColors(colorIdentity);

            using var gradPaint = new SKPaint();
            gradPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(w, h),
                [topColor.WithAlpha(102), bottomColor.WithAlpha(102)],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, w, h, gradPaint);

            using var overlayPaint = new SKPaint();
            overlayPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, h * 0.4f),
                new SKPoint(0, h),
                [SKColors.Transparent, new SKColor(0x12, 0x12, 0x12, 230)],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, w, h, overlayPaint);

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
