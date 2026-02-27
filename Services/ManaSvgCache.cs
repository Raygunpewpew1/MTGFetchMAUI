using SkiaSharp;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Loads and caches SVG mana symbols from embedded resources.
/// Port of the FSVGCache / LoadSVG pattern from MTGManaCostView.pas and MTGCardTextView.pas.
/// Symbols are loaded from embedded SVG files and cached as SKSvg objects (which own the SKPicture).
/// </summary>
public static class ManaSvgCache
{
    private static readonly SvgCacheEngine _engine = new(
        typeof(ManaSvgCache).Assembly,
        s => s.Replace("/", "_").ToUpperInvariant(),
        name => name.Contains(".mana_", StringComparison.OrdinalIgnoreCase),
        (prefix, normalized) => $"{prefix}.mana_{normalized.ToLowerInvariant()}.svg",
        "ManaSymbol");

    /// <summary>
    /// Normalizes a mana symbol from card text format to SVG filename format.
    /// E.g. "2/W" -> "2_W", "B/G/P" -> "B_G_P", "T" -> "T"
    /// Port of NormalizeSymbol from MTGCardTextView.pas.
    /// </summary>
    public static string NormalizeSymbol(string symbol) =>
        symbol.Replace("/", "_").ToUpperInvariant();

    /// <summary>
    /// Gets the cached SKPicture for a mana symbol, loading it if necessary.
    /// Returns null if the symbol SVG doesn't exist.
    /// </summary>
    public static SKPicture? GetSymbol(string symbolName) => _engine.GetSymbol(symbolName);

    /// <summary>
    /// Draws a mana symbol SVG onto the canvas at the specified position and size.
    /// Port of the RenderSymbols pattern from MTGCardTextView.pas.
    /// </summary>
    public static void DrawSymbol(SKCanvas canvas, string symbolName, float x, float y, float size)
    {
        var picture = GetSymbol(symbolName);
        if (picture == null) return;

        canvas.Save();
        canvas.Translate(x, y);

        // SVGs have a 100x100 viewBox, scale to target size
        float scaleX = size / picture.CullRect.Width;
        float scaleY = size / picture.CullRect.Height;
        canvas.Scale(scaleX, scaleY);

        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    /// <summary>
    /// Clears all cached SVGs and failed lookups.
    /// </summary>
    public static void ClearCache() => _engine.ClearCache();
}
