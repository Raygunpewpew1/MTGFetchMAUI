using MTGFetchMAUI.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace MTGFetchMAUI.Controls.Legacy;

public class ManaSymbolView : SKCanvasView
{
    public static readonly BindableProperty SymbolProperty = BindableProperty.Create(
        nameof(Symbol),
        typeof(string),
        typeof(ManaSymbolView),
        defaultValue: string.Empty,
        propertyChanged: OnSymbolChanged);

    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    private static void OnSymbolChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ManaSymbolView view)
        {
            view.InvalidateSurface();
        }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (string.IsNullOrEmpty(Symbol))
            return;

        var info = e.Info;
        float size = Math.Min(info.Width, info.Height);
        float x = (info.Width - size) / 2;
        float y = (info.Height - size) / 2;

        ManaSvgCache.DrawSymbol(canvas, Symbol, x, y, size);
    }
}
