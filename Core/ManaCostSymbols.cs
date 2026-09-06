namespace AetherVault.Core;

/// <summary>
/// Shared parser for MTG mana-cost brace tokens (<c>{2}{W}{G/U}</c>).
/// </summary>
public static class ManaCostSymbols
{
    /// <summary>Yields the inner text of each <c>{…}</c> token in order (e.g. <c>2</c>, <c>W</c>, <c>G/U</c>).</summary>
    public static IEnumerable<string> Enumerate(string? manaCost)
    {
        if (string.IsNullOrEmpty(manaCost))
            yield break;

        int i = 0;
        while (i < manaCost.Length)
        {
            if (manaCost[i] == '{')
            {
                int end = manaCost.IndexOf('}', i);
                if (end > i)
                {
                    yield return manaCost.Substring(i + 1, end - i - 1);
                    i = end + 1;
                    continue;
                }
            }

            i++;
        }
    }

    /// <summary>
    /// Collects up to <paramref name="max"/> symbols. When none are found and
    /// <paramref name="fallbackWhenEmpty"/> is set, returns that single-item array.
    /// </summary>
    public static string[] Take(string? manaCost, int max, string? fallbackWhenEmpty = null)
    {
        if (max <= 0)
            return fallbackWhenEmpty is null ? [] : [fallbackWhenEmpty];

        var list = new List<string>(Math.Min(max, 8));
        foreach (var symbol in Enumerate(manaCost))
        {
            list.Add(symbol);
            if (list.Count >= max)
                break;
        }

        if (list.Count > 0)
            return list.ToArray();

        return fallbackWhenEmpty is null ? [] : [fallbackWhenEmpty];
    }
}
