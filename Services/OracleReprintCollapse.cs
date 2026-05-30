using AetherVault.Core;
using AetherVault.Models;

namespace AetherVault.Services;

/// <summary>
/// Collapses search results to one representative printing per oracle card.
/// Skipped when browsing a specific set or when the user opts into all printings.
/// </summary>
public static class OracleReprintCollapse
{
    /// <summary>SQL window partition / group key for a card row.</summary>
    public static string GetOracleGroupKey(Card card)
    {
        if (!string.IsNullOrWhiteSpace(card.ScryfallOracleId))
            return card.ScryfallOracleId.Trim();
        return card.Uuid;
    }

    /// <summary>
    /// True for catalog search (not collection, not set-scoped) unless the user wants every printing.
    /// </summary>
    public static bool ShouldApply(SearchOptions options, bool isCollectionQuery = false) =>
        !isCollectionQuery
        && !options.ShowAllPrintings
        && string.IsNullOrWhiteSpace(options.SetFilter);

    /// <summary>Picks the default printing shown in search for an oracle group.</summary>
    public static Card SelectBestPrinting(IReadOnlyList<Card> sameOracle) =>
        sameOracle.Count switch
        {
            0 => new Card(),
            1 => sameOracle[0],
            _ => sameOracle
                .OrderBy(c => IsEnglish(c) ? 0 : 1)
                .ThenBy(c => HasPaper(c) ? 0 : 1)
                .ThenByDescending(c => c.SetReleaseDate ?? "", StringComparer.Ordinal)
                .ThenBy(c => c.EdhRecRank > 0 ? c.EdhRecRank : int.MaxValue)
                .ThenByDescending(c => c.SetCode ?? "", StringComparer.Ordinal)
                .ThenBy(c => c.Uuid ?? "", StringComparer.Ordinal)
                .First()
        };

    /// <summary>In-memory collapse for result sets that were not deduped in SQL.</summary>
    public static Card[] CollapseCards(IReadOnlyList<Card> cards)
    {
        if (cards.Count == 0)
            return [];

        var groups = new Dictionary<string, List<Card>>(StringComparer.Ordinal);
        foreach (var card in cards)
        {
            var key = GetOracleGroupKey(card);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(card);
        }

        var output = new List<Card>(groups.Count);
        foreach (var group in groups.Values)
            output.Add(SelectBestPrinting(group));

        return output.ToArray();
    }

    private static bool IsEnglish(Card c) =>
        string.IsNullOrWhiteSpace(c.Language)
        || c.Language.Equals("English", StringComparison.OrdinalIgnoreCase);

    private static bool HasPaper(Card c) =>
        c.Availability.Contains("paper", StringComparison.OrdinalIgnoreCase);
}
