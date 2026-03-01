using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace MTGFetchMAUI.Controls;

/// <summary>
/// SKCanvasView that renders a mana curve bar chart.
/// Each slot corresponds to CMC 0–10+.
/// Bar colors loosely mirror MTG mana colors.
/// </summary>
public class ManaCurveView : SKCanvasView
{
    public static readonly BindableProperty ManaCurveProperty = BindableProperty.Create(
        nameof(ManaCurve),
        typeof(int[]),
        typeof(ManaCurveView),
        null,
        propertyChanged: (b, _, _) => ((ManaCurveView)b).InvalidateSurface());

    public int[]? ManaCurve
    {
        get => (int[]?)GetValue(ManaCurveProperty);
        set => SetValue(ManaCurveProperty, value);
    }

    // CMC 0=gray, 1=plains, 2=island, 3=swamp(lightened), 4=mountain, 5=forest, 6+=accent
    private static readonly SKColor[] BarColors =
    [
        new SKColor(0x88, 0x88, 0x88), // 0 – colorless
        new SKColor(0xF0, 0xE8, 0xD0), // 1 – white/plains
        new SKColor(0x14, 0x79, 0xCD), // 2 – blue/island
        new SKColor(0x7B, 0x6E, 0xC8), // 3 – black/swamp (lightened purple)
        new SKColor(0xD3, 0x20, 0x2A), // 4 – red/mountain
        new SKColor(0x00, 0x73, 0x33), // 5 – green/forest
        new SKColor(0x03, 0xDA, 0xC5), // 6 – multicolor accent
        new SKColor(0x03, 0xDA, 0xC5), // 7
        new SKColor(0x03, 0xDA, 0xC5), // 8
        new SKColor(0x03, 0xDA, 0xC5), // 9
        new SKColor(0x03, 0xDA, 0xC5), // 10+
    ];

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var curve = ManaCurve;
        if (curve == null || curve.Length == 0) return;

        int maxVal = 0;
        foreach (var v in curve) if (v > maxVal) maxVal = v;
        if (maxVal == 0) return;

        float w = e.Info.Width;
        float h = e.Info.Height;
        int slotCount = curve.Length; // 11 (CMC 0–10+)
        float barW = w / slotCount;
        float pad = barW * 0.12f;
        float labelH = h * 0.22f;
        float chartH = h - labelH;

        using var barPaint = new SKPaint { IsAntialias = true };
        using var labelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x99, 0x99, 0x99),
            TextSize = Math.Max(8f, h * 0.13f),
            TextAlign = SKTextAlign.Center
        };
        using var countPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            TextSize = Math.Max(7f, h * 0.11f),
            TextAlign = SKTextAlign.Center
        };

        for (int i = 0; i < slotCount; i++)
        {
            float barH = (float)curve[i] / maxVal * chartH;
            float x = i * barW + pad;
            float bw = barW - pad * 2f;
            float y = chartH - barH;

            barPaint.Color = BarColors[Math.Min(i, BarColors.Length - 1)];

            if (barH > 0)
            {
                canvas.DrawRect(x, y, bw, barH, barPaint);

                if (curve[i] > 0 && barH > countPaint.TextSize * 1.4f)
                {
                    canvas.DrawText(curve[i].ToString(), x + bw / 2f, y + countPaint.TextSize, countPaint);
                }
            }

            string label = i == 10 ? "10+" : i.ToString();
            canvas.DrawText(label, x + bw / 2f, h - 2f, labelPaint);
        }
    }
}
