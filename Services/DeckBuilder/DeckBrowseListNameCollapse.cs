using AetherVault.Models;
using AetherVault.Services;

namespace AetherVault.Services.DeckBuilder;

/// <summary>
/// Picks a single "best" printing per English card name for fixed-name popular lists, so the grid
/// does not show dozens of duplicate-name rows (and does not load that many art assets).
/// </summary>
public static class DeckBrowseListNameCollapse
{
    /// <summary>Rows to request from SQL for name-based lists (must cover all printings that match filters).</summary>
    public const int EnglishNameListSqlOverfetch = 500;

    /// <summary>
    /// If <paramref name="catalogKey"/> is a pure English-name list and <paramref name="namePart"/> is not narrowing the set,
    /// returns one <see cref="Card"/> per expected name, in catalog order, else returns <paramref name="cards"/> as a new array copy.
    /// </summary>
    public static Card[] ApplyIfNeeded(
        string? catalogKey,
        string? namePart,
        IReadOnlyList<Card> cards)
    {
        if (string.IsNullOrEmpty(catalogKey) || !string.IsNullOrEmpty(namePart))
            return [.. cards];

        var order = DeckBrowseListCatalog.GetEnglishNameListOrderOrNull(catalogKey);
        if (order is null || order.Count == 0 || cards.Count == 0)
            return [.. cards];

        var byName = new Dictionary<string, List<Card>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cards)
        {
            var n = c.Name;
            if (string.IsNullOrEmpty(n)) continue;
            if (!byName.TryGetValue(n, out var list))
            {
                list = [];
                byName[n] = list;
            }

            list.Add(c);
        }

        var outList = new List<Card>(order.Count);
        foreach (var englishName in order)
        {
            if (!byName.TryGetValue(englishName, out var candidates) || candidates.Count == 0)
                continue;
            outList.Add(SelectBestPrinting(candidates));
        }

        return outList.ToArray();
    }

    private static Card SelectBestPrinting(List<Card> sameName) =>
        OracleReprintCollapse.SelectBestPrinting(sameName);
}
