using MTGFetchMAUI.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace MTGFetchMAUI.Pages;

[QueryProperty(nameof(DeckId), "deckId")]
public partial class DeckDetailPage : ContentPage
{
    private readonly DeckDetailViewModel _viewModel;

    public string DeckId
    {
        set
        {
            if (int.TryParse(value, out int id))
                _ = _viewModel.LoadAsync(id);
        }
    }

    public DeckDetailPage(DeckDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
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

    private void OnCommanderArtPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float w = e.Info.Width;
        float h = e.Info.Height;

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
            using var textPaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,
                TextSize = Math.Min(h * 0.2f, 28f),
                TextAlign = SKTextAlign.Left,
                FakeBoldText = true
            };
            float textY = h - textPaint.TextSize * 0.5f;
            canvas.DrawText(headline, 16f, textY, textPaint);
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
