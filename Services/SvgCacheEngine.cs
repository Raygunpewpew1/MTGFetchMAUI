using SkiaSharp;
using Svg.Skia;
using System.Reflection;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Shared SVG loading, caching, and double-check-lock infrastructure used by
/// ManaSvgCache and SetSvgCache. Keeps both static caches DRY without requiring
/// inheritance (which C# disallows for static classes).
/// </summary>
internal sealed class SvgCacheEngine
{
    private readonly Dictionary<string, SKSvg> _cache = new();
    private readonly HashSet<string> _failedSymbols = [];
    private readonly object _lock = new();
    private readonly Assembly _assembly;
    private readonly Func<string, string> _normalizer;
    private readonly Func<string, bool> _resourceMatcher;
    private readonly Func<string, string, string> _resourceNameBuilder;
    private readonly string _logTag;
    private string? _resourcePrefix;

    /// <param name="assembly">Assembly that owns the embedded SVG resources.</param>
    /// <param name="normalizer">Converts raw symbol/code input to the normalized cache key.</param>
    /// <param name="resourceMatcher">Returns true for manifest names that belong to this symbol set.</param>
    /// <param name="resourceNameBuilder">Builds the full manifest resource name from (prefix, normalizedKey).</param>
    /// <param name="logTag">Short label used in log messages (e.g. "ManaSymbol", "SetSymbol").</param>
    public SvgCacheEngine(
        Assembly assembly,
        Func<string, string> normalizer,
        Func<string, bool> resourceMatcher,
        Func<string, string, string> resourceNameBuilder,
        string logTag)
    {
        _assembly = assembly;
        _normalizer = normalizer;
        _resourceMatcher = resourceMatcher;
        _resourceNameBuilder = resourceNameBuilder;
        _logTag = logTag;
    }

    /// <summary>
    /// Returns the cached SKPicture for the given symbol/code, loading it on first access.
    /// Returns null if the SVG resource does not exist.
    /// </summary>
    public SKPicture? GetSymbol(string input)
    {
        if (string.IsNullOrEmpty(input)) return null;
        var normalized = _normalizer(input);

        lock (_lock)
        {
            if (_cache.TryGetValue(normalized, out var cached)) return cached.Picture;
            if (_failedSymbols.Contains(normalized)) return null;
        }

        var svg = LoadSvg(normalized);

        lock (_lock)
        {
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

            svg?.Dispose();
            _failedSymbols.Add(normalized);
            return null;
        }
    }

    /// <summary>Clears all cached SVGs and failed-lookup records.</summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            foreach (var kvp in _cache) kvp.Value.Dispose();
            _cache.Clear();
            _failedSymbols.Clear();
        }
    }

    private SKSvg? LoadSvg(string normalized)
    {
        try
        {
            _resourcePrefix ??= FindPrefix();
            if (_resourcePrefix == null) return null;

            var resourceName = _resourceNameBuilder(_resourcePrefix, normalized);
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            using var reader = new StreamReader(stream);
            var svg = new SKSvg();
            svg.FromSvg(reader.ReadToEnd());
            return svg;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to load SVG [{_logTag}] '{normalized}': {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    private string? FindPrefix()
    {
        var names = _assembly.GetManifestResourceNames();
        foreach (var name in names)
        {
            if (!name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
            if (!_resourceMatcher(name)) continue;

            var withoutExt = name[..name.LastIndexOf(".svg", StringComparison.OrdinalIgnoreCase)];
            var lastDot = withoutExt.LastIndexOf('.');
            if (lastDot > 0) return withoutExt[..lastDot];
        }
        return null;
    }
}
