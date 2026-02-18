using MTGFetchMAUI.Core;

namespace MTGFetchMAUI.Models;

/// <summary>
/// Represents a card in a deck.
/// Port of TDeckCard from MTGCollection.pas.
/// </summary>
public class DeckCard
{
    public string CardUUID { get; set; } = "";
    public string CardName { get; set; } = "";
    public int Quantity { get; set; }
    public string Category { get; set; } = "";
    public bool IsCommander { get; set; }
    public bool IsCompanion { get; set; }
    public bool IsMainDeck { get; set; } = true;
    public bool IsSideboard { get; set; }

    public bool IsValid => !string.IsNullOrEmpty(CardUUID) && Quantity > 0;

    public string GetDisplayText()
    {
        string prefix = IsCommander ? "[Commander] "
                      : IsCompanion ? "[Companion] "
                      : "";
        return $"{prefix}{Quantity}x {CardName}";
    }
}

/// <summary>
/// Statistics about a deck.
/// Port of TDeckStats from MTGCollection.pas.
/// </summary>
public class DeckStats
{
    public int TotalCards { get; set; }
    public int Lands { get; set; }
    public int Creatures { get; set; }
    public int Instants { get; set; }
    public int Sorceries { get; set; }
    public int Artifacts { get; set; }
    public int Enchantments { get; set; }
    public int Planeswalkers { get; set; }
    public double AvgCMC { get; set; }
    public int RampCount { get; set; }
    public int CardDrawCount { get; set; }
    public int RemovalCount { get; set; }
    public int BoardWipesCount { get; set; }
    public int CounterspellsCount { get; set; }
    public int[] ManaCurve { get; set; } = new int[11]; // CMC 0-10+
    public ColorIdentity ColorIdentity { get; set; }

    public int GetManaCurvePeak()
    {
        int maxCount = 0, peakCMC = 0;
        for (int i = 0; i < ManaCurve.Length; i++)
        {
            if (ManaCurve[i] > maxCount)
            {
                maxCount = ManaCurve[i];
                peakCMC = i;
            }
        }
        return peakCMC;
    }

    public override string ToString()
    {
        var curveParts = new List<string>();
        for (int i = 0; i < ManaCurve.Length; i++)
        {
            if (ManaCurve[i] > 0)
                curveParts.Add(i == 10 ? $"10+: {ManaCurve[i]}" : $"{i}: {ManaCurve[i]}");
        }

        return $"Total Cards: {TotalCards}\n" +
               $"Lands: {Lands} | Creatures: {Creatures} | Instants: {Instants} | Sorceries: {Sorceries}\n" +
               $"Artifacts: {Artifacts} | Enchantments: {Enchantments} | Planeswalkers: {Planeswalkers}\n" +
               $"Avg CMC: {AvgCMC:F2}\n" +
               $"Colors: {ColorIdentity.AsString()}\n" +
               $"Mana Curve: {string.Join(", ", curveParts)}";
    }
}

/// <summary>
/// Represents a Commander deck.
/// Port of TCommanderDeck from MTGCollection.pas.
/// </summary>
public class CommanderDeck
{
    public int DeckID { get; set; }
    public string DeckName { get; set; } = "";
    public string CommanderName { get; set; } = "";
    public string PartnerName { get; set; } = "";
    public string Companion { get; set; } = "";
    public DeckFormat DeckFormat { get; set; } = DeckFormat.Commander;
    public CommanderArchetype Archetype { get; set; } = CommanderArchetype.Unknown;
    public ColorIdentity ColorIdentity { get; set; }
    public List<DeckCard> MainDeck { get; set; } = [];
    public List<DeckCard> Sideboard { get; set; } = [];
    public string Description { get; set; } = "";
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }
    public DeckStats Stats { get; set; } = new();

    public bool HasCommander => !string.IsNullOrEmpty(CommanderName);

    public int GetTotalCards()
    {
        int total = MainDeck.Sum(c => c.Quantity);
        if (!string.IsNullOrEmpty(CommanderName)) total++;
        if (!string.IsNullOrEmpty(PartnerName)) total++;
        return total;
    }

    public int GetCardCount(string cardName)
    {
        int count = 0;
        if (CommanderName.Equals(cardName, StringComparison.OrdinalIgnoreCase)) count++;
        if (PartnerName.Equals(cardName, StringComparison.OrdinalIgnoreCase)) count++;
        if (Companion.Equals(cardName, StringComparison.OrdinalIgnoreCase)) count++;
        count += MainDeck.Where(c => c.CardName.Equals(cardName, StringComparison.OrdinalIgnoreCase))
                         .Sum(c => c.Quantity);
        count += Sideboard.Where(c => c.CardName.Equals(cardName, StringComparison.OrdinalIgnoreCase))
                          .Sum(c => c.Quantity);
        return count;
    }

    public bool IsValid =>
        !string.IsNullOrEmpty(CommanderName) &&
        GetTotalCards() == 100 &&
        DeckFormat is DeckFormat.Commander or DeckFormat.Brawl or DeckFormat.Oathbreaker;

    public string GetDisplayInfo()
    {
        var result = $"{DeckName}\n" +
                     $"Commander: {CommanderName}\n" +
                     $"Colors: {ColorIdentity.AsString()}\n" +
                     $"Archetype: {Archetype.ToDisplayName()}\n" +
                     $"Cards: {GetTotalCards()}";
        if (!string.IsNullOrEmpty(PartnerName))
            result += $"\nPartner: {PartnerName}";
        if (!string.IsNullOrEmpty(Companion))
            result += $"\nCompanion: {Companion}";
        return result;
    }
}

/// <summary>
/// Generic deck structure (non-Commander).
/// Port of TDeck from MTGCollection.pas.
/// </summary>
public class Deck
{
    public string DeckName { get; set; } = "";
    public DeckFormat DeckFormat { get; set; } = DeckFormat.Standard;
    public List<DeckCard> MainDeck { get; set; } = [];
    public List<DeckCard> Sideboard { get; set; } = [];
    public string Description { get; set; } = "";
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }
    public DeckStats Stats { get; set; } = new();

    public int GetTotalCards() => MainDeck.Sum(c => c.Quantity);

    public int GetCardCount(string cardName) =>
        MainDeck.Where(c => c.CardName.Equals(cardName, StringComparison.OrdinalIgnoreCase))
                .Sum(c => c.Quantity) +
        Sideboard.Where(c => c.CardName.Equals(cardName, StringComparison.OrdinalIgnoreCase))
                 .Sum(c => c.Quantity);

    public bool IsValid
    {
        get
        {
            int minCards = DeckFormat switch
            {
                DeckFormat.Commander or DeckFormat.Brawl or DeckFormat.Oathbreaker => 100,
                _ => 60
            };
            return GetTotalCards() >= minCards;
        }
    }

    public string GetDisplayInfo() =>
        $"{DeckName}\n" +
        $"Format: {DeckFormat.ToDisplayName()}\n" +
        $"Main Deck: {GetTotalCards()} cards\n" +
        $"Sideboard: {Sideboard.Count} cards";
}
