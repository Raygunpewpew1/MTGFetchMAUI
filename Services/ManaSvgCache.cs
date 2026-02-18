using System.Reflection;
using SkiaSharp;
using Svg.Skia;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Loads and caches SVG mana symbols from embedded resources.
/// Port of the FSVGCache / LoadSVG pattern from MTGManaCostView.pas and MTGCardTextView.pas.
/// Symbols are loaded from embedded SVG files and cached as SKSvg objects (which own the SKPicture).
/// </summary>
public static class ManaSvgCache
{
    // Cache the full SKSvg objects to keep the SKPicture alive
    private static readonly Dictionary<string, SKSvg> _cache = new();
    private static readonly HashSet<string> _failedSymbols = [];
    private static readonly object _lock = new();
    private static readonly Assembly _assembly = typeof(ManaSvgCache).Assembly;
    private static string? _resourcePrefix;

    /// <summary>
    /// Normalizes a mana symbol from card text format to SVG filename format.
    /// E.g. "2/W" -> "2_W", "B/G/P" -> "B_G_P", "T" -> "T"
    /// Port of NormalizeSymbol from MTGCardTextView.pas.
    /// </summary>
    public static string NormalizeSymbol(string symbol)
    {
        return symbol.Replace("/", "_").ToUpperInvariant();
    }

    /// <summary>
    /// Gets the cached SKPicture for a mana symbol, loading it if necessary.
    /// Returns null if the symbol SVG doesn't exist.
    /// </summary>
    public static SKPicture? GetSymbol(string symbolName)
    {
        var normalized = NormalizeSymbol(symbolName);

        lock (_lock)
        {
            if (_cache.TryGetValue(normalized, out var cached))
                return cached.Picture;

            if (_failedSymbols.Contains(normalized))
                return null;
        }

        // Load outside the lock to avoid holding it during I/O
        var svg = LoadSvgFromResources(normalized);

        lock (_lock)
        {
            // Double-check after loading
            if (_cache.TryGetValue(normalized, out var cached))
            {
                svg?.Dispose();
                return cached.Picture;
            }

            if (svg?.Picture != null)
            {
                _cache[normalized] = svg;
                return svg.Picture;
            }
            else
            {
                svg?.Dispose();
                _failedSymbols.Add(normalized);
                return null;
            }
        }
    }

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
    public static void ClearCache()
    {
        lock (_lock)
        {
            foreach (var kvp in _cache)
                kvp.Value.Dispose();
            _cache.Clear();
            _failedSymbols.Clear();
        }
    }

    private static SKSvg? LoadSvgFromResources(string normalizedName)
    {
        try
        {
            _resourcePrefix ??= FindResourcePrefix();
            if (_resourcePrefix == null) return null;

            var resourceName = $"{_resourcePrefix}.{normalizedName}.svg";
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            using var reader = new StreamReader(stream);
            var svgContent = reader.ReadToEnd();

            var svg = new SKSvg();
            svg.FromSvg(svgContent);
            return svg;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to load SVG '{normalizedName}': {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    private static string? FindResourcePrefix()
    {
        // Find the resource prefix by looking for any SVG resource
        var names = _assembly.GetManifestResourceNames();
        foreach (var name in names)
        {
            if (name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                // Resource names are like "MTGFetchMAUI.Assets.SVGMana.W.svg"
                // We want "MTGFetchMAUI.Assets.SVGMana"
                var lastDotSvg = name.LastIndexOf(".svg", StringComparison.OrdinalIgnoreCase);
                var nameWithoutExt = name[..lastDotSvg];
                var lastDot = nameWithoutExt.LastIndexOf('.');
                if (lastDot > 0)
                    return nameWithoutExt[..lastDot];
            }
        }
        return null;
    }
}
