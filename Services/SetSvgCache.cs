using SkiaSharp;
using Svg.Skia;
using System.Reflection;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Loads and caches SVG set symbols from embedded resources.
/// Similar to ManaSvgCache, but for set symbols in Assets/SVGSets.
/// </summary>
public static class SetSvgCache
{
    private static readonly Dictionary<string, SKSvg> _cache = new();
    private static readonly HashSet<string> _failedSymbols = [];
    private static readonly object _lock = new();
    private static readonly Assembly _assembly = typeof(SetSvgCache).Assembly;
    private static string? _resourcePrefix;

    /// <summary>
    /// Normalizes a set code from card data to SVG filename format.
    /// E.g. "ZEN" -> "zen"
    /// </summary>
    public static string NormalizeSetCode(string setCode)
    {
        return setCode.ToLowerInvariant();
    }

    /// <summary>
    /// Gets the cached SKPicture for a set symbol, loading it if necessary.
    /// Returns null if the symbol SVG doesn't exist.
    /// </summary>
    public static SKPicture? GetSymbol(string setCode)
    {
        if (string.IsNullOrEmpty(setCode)) return null;

        var normalized = NormalizeSetCode(setCode);

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
    /// Draws a set symbol SVG onto the canvas at the specified position and size.
    /// </summary>
    public static void DrawSymbol(SKCanvas canvas, string setCode, float x, float y, float size)
    {
        var picture = GetSymbol(setCode);
        if (picture == null) return;

        canvas.Save();
        canvas.Translate(x, y);

        // SVGs have a viewBox, scale to target size based on cull rect
        float scaleX = size / picture.CullRect.Width;
        float scaleY = size / picture.CullRect.Height;

        // Maintain aspect ratio if needed, or fill square
        // Usually set symbols fit in a square or near-square area
        // We'll scale to fit within 'size' while preserving aspect ratio
        float scale = Math.Min(scaleX, scaleY);

        // Center within the box if aspect ratio differs
        float offsetX = (size - (picture.CullRect.Width * scale)) / 2f;
        float offsetY = (size - (picture.CullRect.Height * scale)) / 2f;

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale, scale);

        using var paint = new SKPaint
        {
            IsAntialias = true
        };

        canvas.DrawPicture(picture, paint);
        canvas.Restore();
    }

    /// <summary>
    /// Draws a set symbol SVG using separate width/height constraints, preserving aspect ratio.
    /// </summary>
    public static void DrawSymbol(SKCanvas canvas, string setCode, SKRect destRect)
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
            IsAntialias = true
        };

        canvas.DrawPicture(picture, paint);
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

            // Symbols are in Assets/SVGSets as {normalizedName}.svg
            // e.g. "zen" -> "zen.svg"
            // The resource name will look like "MTGFetchMAUI.Assets.SVGSets.zen.svg"
            var resourceName = $"{_resourcePrefix}.{normalizedName}.svg";

            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // Try fallback if normalized didn't work (e.g. maybe case issue, but likely handled by ToLower)
                return null;
            }

            using var reader = new StreamReader(stream);
            var svgContent = reader.ReadToEnd();

            var svg = new SKSvg();
            svg.FromSvg(svgContent);
            return svg;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to load Set SVG '{normalizedName}': {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    private static string? FindResourcePrefix()
    {
        // Find the resource prefix by looking for a set symbol SVG resource
        // e.g. "zen.svg" or "10e.svg"
        // We know "10e.svg" exists in Assets/SVGSets based on list_files
        var names = _assembly.GetManifestResourceNames();
        foreach (var name in names)
        {
            // Look for resources that end in .svg and contain "Assets.SVGSets" or similar
            if (name.Contains("Assets.SVGSets", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                // Resource name example: "MTGFetchMAUI.Assets.SVGSets.10e.svg"
                // We want "MTGFetchMAUI.Assets.SVGSets"
                var lastDotSvg = name.LastIndexOf(".svg", StringComparison.OrdinalIgnoreCase);
                var nameWithoutExt = name[..lastDotSvg];
                var lastDot = nameWithoutExt.LastIndexOf('.'); // Last dot before extension (e.g. before "10e")

                if (lastDot > 0)
                {
                   return nameWithoutExt[..lastDot];
                }
            }
        }
        return null;
    }
}
