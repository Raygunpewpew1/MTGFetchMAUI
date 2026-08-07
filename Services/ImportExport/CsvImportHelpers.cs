using CsvHelper.Configuration;
using System.Globalization;

namespace AetherVault.Services.ImportExport;

/// <summary>Shared CSV reader settings and header lookup for collection/deck import.</summary>
public static class CsvImportHelpers
{
    /// <summary>Invariant-culture CSV config used by collection and deck importers.</summary>
    public static CsvConfiguration CreateReaderConfiguration() => new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        MissingFieldFound = null,
        HeaderValidated = null,
        IgnoreBlankLines = true,
        TrimOptions = TrimOptions.Trim,
    };

    /// <summary>
    /// Returns the index of the first matching candidate in <paramref name="lowerHeaders"/>,
    /// or -1 when none match. Candidates should already be lower-case.
    /// </summary>
    public static int FindHeader(string[] lowerHeaders, params string[] candidates)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            int idx = Array.IndexOf(lowerHeaders, candidates[i]);
            if (idx != -1) return idx;
        }

        return -1;
    }

    /// <summary>Lower-cases and trims header names for case-insensitive column matching.</summary>
    public static string[] ToLowerHeaders(string[] headers) =>
        [.. headers.Select(h => h.ToLowerInvariant().Trim())];
}
