using SkiaSharp;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Loads and caches SVG set symbols from embedded resources.
/// Similar to ManaSvgCache, but for set symbols in Assets/SVGSets.
/// </summary>
public static class SetSvgCache
{
    private static readonly SvgCacheEngine _engine = new(
        typeof(SetSvgCache).Assembly,
        s => s.ToLowerInvariant(),
        name => name.Contains("Assets.SVGSets", StringComparison.OrdinalIgnoreCase),
        (prefix, normalized) => $"{prefix}.{normalized}.svg",
        "SetSymbol");

    /// <summary>
    /// Normalizes a set code from card data to SVG filename format.
    /// E.g. "ZEN" -> "zen"
    /// </summary>
    public static string NormalizeSetCode(string setCode) => setCode.ToLowerInvariant();

    /// <summary>
    /// Gets the cached SKPicture for a set symbol, loading it if necessary.
    /// Returns null if the symbol SVG doesn't exist.
    /// </summary>
    public static SKPicture? GetSymbol(string setCode) => _engine.GetSymbol(setCode);

    /// <summary>
    /// Draws a set symbol SVG onto the canvas at the specified position and size.
    /// </summary>
    public static void DrawSymbol(SKCanvas canvas, string setCode, float x, float y, float size, SKColor? tint = null)
    {
        var picture = GetSymbol(setCode);
        if (picture == null) return;

        canvas.Save();
        canvas.Translate(x, y);

        float scaleX = size / picture.CullRect.Width;
        float scaleY = size / picture.CullRect.Height;
        float scale = Math.Min(scaleX, scaleY);

        float offsetX = (size - (picture.CullRect.Width * scale)) / 2f;
        float offsetY = (size - (picture.CullRect.Height * scale)) / 2f;

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale, scale);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            ColorFilter = tint.HasValue
                ? SKColorFilter.CreateBlendMode(tint.Value, SKBlendMode.SrcIn)
                : null
        };

        canvas.DrawPicture(picture, paint);
        canvas.Restore();
    }

    /// <summary>
    /// Draws a set symbol SVG using separate width/height constraints, preserving aspect ratio.
    /// </summary>
    public static void DrawSymbol(SKCanvas canvas, string setCode, SKRect destRect, SKColor? tint = null)
    {
        var picture = GetSymbol(setCode);
        if (picture == null) return;

        canvas.Save();
        canvas.Translate(destRect.Left, destRect.Top);

        float scaleX = destRect.Width / picture.CullRect.Width;
        float scaleY = destRect.Height / picture.CullRect.Height;
        float scale = Math.Min(scaleX, scaleY);

        float offsetX = (destRect.Width - (picture.CullRect.Width * scale)) / 2f;
        float offsetY = (destRect.Height - (picture.CullRect.Height * scale)) / 2f;

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale, scale);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            ColorFilter = tint.HasValue
                ? SKColorFilter.CreateBlendMode(tint.Value, SKBlendMode.SrcIn)
                : null
        };

        canvas.DrawPicture(picture, paint);
        canvas.Restore();
    }

    /// <summary>
    /// Clears all cached SVGs and failed lookups.
    /// </summary>
    public static void ClearCache() => _engine.ClearCache();
}
