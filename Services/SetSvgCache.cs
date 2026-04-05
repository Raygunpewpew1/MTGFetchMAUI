using SkiaSharp;

namespace AetherVault.Services;

/// <summary>
/// Loads and caches SVG set symbols from embedded resources.
/// Similar to ManaSvgCache, but for set symbols in Assets/SVGSets.
/// </summary>
public static class SetSvgCache
{
    private const string FallbackResourceKey = "fallback";

    private static readonly SvgCacheEngine Engine = new(
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
    /// Uses <c>Assets/SVGSets/fallback.svg</c> when no SVG exists for <paramref name="setCode"/>.
    /// Returns null only when <paramref name="setCode"/> is empty/whitespace or fallback load fails.
    /// </summary>
    public static SKPicture? GetSymbol(string setCode)
    {
        var picture = Engine.GetSymbol(setCode);
        if (picture != null) return picture;
        if (string.IsNullOrWhiteSpace(setCode)) return null;
        return Engine.GetSymbol(FallbackResourceKey);
    }

    /// <summary>
    /// Draws a set symbol SVG onto the canvas at the specified position and size.
    /// </summary>
    public static void DrawSymbol(SKCanvas canvas, string setCode, float x, float y, float size, SKColor? tint = null)
    {
        var picture = GetSymbol(setCode);
        if (picture == null) return;
        SvgCacheEngine.DrawPictureInRect(canvas, picture, x, y, size, tint, centerInRect: true);
    }

    /// <summary>
    /// Draws a set symbol SVG using separate width/height constraints, preserving aspect ratio.
    /// </summary>
    public static void DrawSymbol(SKCanvas canvas, string setCode, SKRect destRect, SKColor? tint = null)
    {
        var picture = GetSymbol(setCode);
        if (picture == null) return;
        SvgCacheEngine.DrawPictureInRect(canvas, picture, destRect, tint, centerInRect: true);
    }

    /// <summary>
    /// Clears all cached SVGs and failed lookups.
    /// </summary>
    public static void ClearCache() => Engine.ClearCache();
}
