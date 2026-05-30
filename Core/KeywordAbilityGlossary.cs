using System.Text.Json;

namespace AetherVault.Core;

/// <summary>
/// Short unofficial reminders for every keyword string in MTGJSON <c>Keywords.json</c>
/// (ability words, keyword abilities, keyword actions). Summaries may end with a
/// parenthetical plain-language tail (literal ordering, concrete checks); see
/// <c>tools/keyword-plain-hints.mjs</c>. Data is embedded as <c>MtgjsonKeywordSummaries.json</c>;
/// regenerate with <c>node tools/gen-keyword-summaries.mjs</c>. Lookup is case-insensitive.
/// For official rules text, use the Comprehensive Rules.
/// </summary>
public static class KeywordAbilityGlossary
{
    private static readonly object LoadLock = new();
    private static Dictionary<string, string>? _summaries;
    private static string[]? _keysLongestFirst;

    private static void EnsureLoaded()
    {
        if (_summaries != null) return;
        lock (LoadLock)
        {
            if (_summaries != null) return;

            var asm = typeof(KeywordAbilityGlossary).Assembly;
            var resName = Array.Find(
                asm.GetManifestResourceNames(),
                static n => n.EndsWith("MtgjsonKeywordSummaries.json", StringComparison.Ordinal));

            if (resName == null)
                throw new InvalidOperationException("Embedded resource MtgjsonKeywordSummaries.json was not found.");

            using var stream = asm.GetManifestResourceStream(resName)
                ?? throw new InvalidOperationException($"Could not open embedded resource {resName}.");

            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException("MtgjsonKeywordSummaries.json could not be parsed.");

            _summaries = new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
            _keysLongestFirst = [.. _summaries.Keys.OrderByDescending(k => k.Length)];
        }
    }

    /// <summary>Returns a short summary, or <c>null</c> if the string is empty or not a known MTGJSON keyword.</summary>
    public static string? TryGetSummary(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return null;

        EnsureLoaded();

        var trimmed = keyword.Trim();
        if (_summaries!.TryGetValue(trimmed, out var exact))
            return exact;

        foreach (var key in _keysLongestFirst!)
        {
            if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return _summaries[key];
        }

        return null;
    }
}
