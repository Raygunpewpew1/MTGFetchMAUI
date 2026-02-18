using MTGFetchMAUI.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace MTGFetchMAUI.Controls;

/// <summary>
/// SkiaSharp-based mana cost display using SVG mana symbols.
/// Port of TMTGManaCostView from MTGManaCostView.pas.
/// Loads SVG files via ManaSvgCache and renders them as proper vector graphics.
/// </summary>
public class ManaCostView : SKCanvasView
{
    private string _manaText = "";
    private float _symbolSize = 20f;
    private float _spacing = 2f;

    public string ManaText
    {
        get => _manaText;
        set
        {
            if (_manaText == value) return;
            _manaText = value;
            UpdateSize();
            InvalidateSurface();
        }
    }

    public float SymbolSize
    {
        get => _symbolSize;
        set { _symbolSize = value; UpdateSize(); InvalidateSurface(); }
    }

    public float Spacing
    {
        get => _spacing;
        set { _spacing = value; UpdateSize(); InvalidateSurface(); }
    }

    private List<string> ParseSymbols()
    {
        var symbols = new List<string>();
        if (string.IsNullOrEmpty(_manaText)) return symbols;

        int i = 0;
        while (i < _manaText.Length)
        {
            if (_manaText[i] == '{')
            {
                int end = _manaText.IndexOf('}', i);
                if (end > i)
                {
                    symbols.Add(_manaText[(i + 1)..end]);
                    i = end + 1;
                    continue;
                }
            }
            i++;
        }
        return symbols;
    }

    private void UpdateSize()
    {
        var symbols = ParseSymbols();
        if (symbols.Count == 0)
        {
            WidthRequest = 0;
            HeightRequest = 0;
            return;
        }
        WidthRequest = symbols.Count * (_symbolSize + _spacing) - _spacing;
        HeightRequest = _symbolSize;
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var symbols = ParseSymbols();
        if (symbols.Count == 0) return;

        float scale = e.Info.Width / (float)(Width > 0 ? Width : 100);
        canvas.Scale(scale);

        // Right-align like the original Delphi version
        float totalWidth = symbols.Count * _symbolSize + (symbols.Count - 1) * _spacing;
        float currentX = (float)(Width > 0 ? Width : totalWidth) - totalWidth;
        float currentY = ((float)(Height > 0 ? Height : _symbolSize) - _symbolSize) / 2f;

        foreach (var sym in symbols)
        {
            ManaSvgCache.DrawSymbol(canvas, sym, currentX, currentY, _symbolSize);
            currentX += _symbolSize + _spacing;
        }
    }
}
