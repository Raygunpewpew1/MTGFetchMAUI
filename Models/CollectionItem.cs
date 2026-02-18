namespace MTGFetchMAUI.Models;

/// <summary>
/// Represents a card in a user's collection.
/// Port of TCollectionItem from MTGCollection.pas.
/// </summary>
public class CollectionItem
{
    public string CardUUID { get; set; } = "";
    public int Quantity { get; set; }
    public DateTime DateAdded { get; set; }
    public Card Card { get; set; } = new();

    public string GetDisplayInfo() => $"{Quantity}x {Card.Name} ({Card.SetCode})";
}

/// <summary>
/// Statistics about a collection.
/// Port of TCollectionStats from MTGCollection.pas.
/// </summary>
public class CollectionStats
{
    public int TotalCards { get; set; }
    public int UniqueCards { get; set; }
    public int CreatureCount { get; set; }
    public int SpellCount { get; set; }
    public int LandCount { get; set; }
    public int CommonCount { get; set; }
    public int UncommonCount { get; set; }
    public int RareCount { get; set; }
    public int MythicCount { get; set; }
    public int FoilCount { get; set; }
    public double TotalValue { get; set; }
    public double AvgCMC { get; set; }

    public override string ToString() =>
        $"Total: {TotalCards} cards ({UniqueCards} unique)\n" +
        $"Creatures: {CreatureCount} | Spells: {SpellCount} | Lands: {LandCount}\n" +
        $"Common: {CommonCount} | Uncommon: {UncommonCount} | Rare: {RareCount} | Mythic: {MythicCount}\n" +
        $"Foils: {FoilCount} | Avg CMC: {AvgCMC:F2}";
}
