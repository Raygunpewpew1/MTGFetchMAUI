namespace AetherVault.Core;

/// <summary>
/// Builds the short human-readable filter strip shown under Search / Filters
/// (e.g. <c>Colors: White • CMC: 2-4 • Format: Commander</c>).
/// </summary>
public static class SearchFiltersSummaryBuilder
{
    private const int MaxSummaryLength = 120;

    public static string Build(SearchOptions options)
    {
        var parts = new List<string>();
        AddTextAndTypeSummary(parts, options);
        AddOracleKeywordsSummary(parts, options);
        AddColorAndRaritySummary(parts, options);
        AddCmcSummary(parts, options);
        AddPowerToughnessSummary(parts, options);
        AddFormatSetArtistSummary(parts, options);
        AddAvailabilitySummary(parts, options);
        AddLayoutSummary(parts, options);
        AddFinishesSummary(parts, options);
        AddSpecialSummary(parts, options);

        if (parts.Count == 0)
            return string.Empty;

        var summary = string.Join(" • ", parts);
        return summary.Length <= MaxSummaryLength
            ? summary
            : summary[..MaxSummaryLength] + "…";
    }

    private static void AddTextAndTypeSummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TextFilter))
            parts.Add($"Text: \"{options.TextFilter}\"");

        if (!string.IsNullOrWhiteSpace(options.TypeFilter) &&
            !options.TypeFilter.Equals("Any", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Type: {options.TypeFilter}");

        if (!string.IsNullOrWhiteSpace(options.SubtypeFilter))
            parts.Add($"Subtype: {options.SubtypeFilter}");

        if (!string.IsNullOrWhiteSpace(options.SupertypeFilter))
            parts.Add($"Supertype: {options.SupertypeFilter}");
    }

    private static void AddOracleKeywordsSummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.KeywordsFilter))
            parts.Add($"Keywords: {options.KeywordsFilter}");
    }

    private static void AddColorAndRaritySummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ColorFilter))
            parts.Add($"Colors: {ColorFilterDisplay.ToDisplayString(options.ColorFilter)}");

        if (!string.IsNullOrWhiteSpace(options.ColorIdentityFilter))
            parts.Add($"Identity: {ColorFilterDisplay.ToDisplayString(options.ColorIdentityFilter)}");

        if (options.RarityFilter.Count > 0)
            parts.Add($"Rarity: {string.Join("/", options.RarityFilter)}");
    }

    private static void AddCmcSummary(List<string> parts, SearchOptions options)
    {
        if (options.UseCmcRange)
            parts.Add($"CMC: {options.CmcMin}-{options.CmcMax}");
        else if (options.UseCmcExact)
            parts.Add($"CMC: {options.CmcExact}");
    }

    private static void AddPowerToughnessSummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PowerFilter))
            parts.Add($"Power: {options.PowerFilter}");

        if (!string.IsNullOrWhiteSpace(options.ToughnessFilter))
            parts.Add($"Toughness: {options.ToughnessFilter}");
    }

    private static void AddFormatSetArtistSummary(List<string> parts, SearchOptions options)
    {
        if (options.UseLegalFormat)
            parts.Add($"Format: {options.LegalFormat.ToDisplayName()}");

        if (!string.IsNullOrWhiteSpace(options.SetFilter))
            parts.Add($"Set: {options.SetFilter}");

        if (!string.IsNullOrWhiteSpace(options.ArtistFilter))
            parts.Add($"Artist: {options.ArtistFilter}");
    }

    private static void AddAvailabilitySummary(List<string> parts, SearchOptions options)
    {
        if (options.AvailabilityFilter.Count == 0) return;
        var labels = options.AvailabilityFilter
            .Select(static t => t.ToLowerInvariant() switch
            {
                "paper" => "Paper",
                "mtgo" => "MTGO",
                "arena" => "Arena",
                _ => t
            })
            .Distinct();
        parts.Add($"Available: {string.Join("/", labels)}");
    }

    private static void AddLayoutSummary(List<string> parts, SearchOptions options)
    {
        if (options.LayoutFilter.Count == 0) return;
        var labels = options.LayoutFilter.Select(l => l switch
        {
            CardLayout.ModalDfc => "MDFC",
            CardLayout.DoubleFacedToken => "DFC token",
            _ => l.ToString()
        });
        parts.Add($"Layout: {string.Join("/", labels)}");
    }

    private static void AddFinishesSummary(List<string> parts, SearchOptions options)
    {
        if (options.FinishesFilter.Count == 0) return;
        var labels = options.FinishesFilter
            .Select(static t => t.ToLowerInvariant() switch
            {
                "nonfoil" => "Nonfoil",
                "foil" => "Foil",
                "etched" => "Etched",
                _ => t
            })
            .Distinct();
        parts.Add($"Finish: {string.Join("/", labels)}");
    }

    private static void AddSpecialSummary(List<string> parts, SearchOptions options)
    {
        if (options.NoVariations)
            parts.Add("No variations");

        if (options.ShowAllPrintings)
            parts.Add("All printings");

        if (options.IncludeTokens)
            parts.Add("Include tokens");

        if (options.CommanderOnly)
            parts.Add("Can be commander only");
    }
}
