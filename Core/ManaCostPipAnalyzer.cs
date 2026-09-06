namespace AetherVault.Core;

/// <summary>
/// Counts colored mana symbols in MTG <c>{W}</c> mana cost strings for deck visuals.
/// Hybrid symbols count toward each color shown; generic / X / phyrexian tails roll into "other".
/// </summary>
public static class ManaCostPipAnalyzer
{
    /// <summary>W, U, B, R, G, other (includes C and non-single-letter tokens).</summary>
    public const int SlotCount = 6;

    public static void Accumulate(string? manaCost, int quantity, int[] counts)
    {
        if (counts.Length < SlotCount || quantity == 0 || string.IsNullOrEmpty(manaCost))
            return;

        foreach (var symbol in ManaCostSymbols.Enumerate(manaCost))
            AccumulateInner(symbol.AsSpan(), quantity, counts);
    }

    private static void AccumulateInner(ReadOnlySpan<char> inner, int qty, int[] counts)
    {
        foreach (var range in inner.ToString().Split('/'))
        {
            var s = range.Trim();
            if (s.Length == 0)
                continue;

            char f = char.ToUpperInvariant(s[0]);
            if (s.Length == 1 && IsWubrgc(f))
            {
                counts[SlotIndex(f)] += qty;
            }
            else if (char.IsAsciiDigit(f))
            {
                counts[5] += qty;
            }
            else if (IsWubrgc(f))
            {
                counts[SlotIndex(f)] += qty;
            }
            else
            {
                counts[5] += qty;
            }
        }
    }

    private static bool IsWubrgc(char c) =>
        c is 'W' or 'U' or 'B' or 'R' or 'G' or 'C';

    private static int SlotIndex(char c) =>
        char.ToUpperInvariant(c) switch
        {
            'W' => 0,
            'U' => 1,
            'B' => 2,
            'R' => 3,
            'G' => 4,
            'C' => 5,
            _ => 5
        };
}
