namespace MTGFetchMAUI.Models;

/// <summary>
/// Database entity for a Card in a Deck.
/// </summary>
public class DeckCardEntity
{
    public int DeckId { get; set; }
    public string CardId { get; set; } = "";
    public int Quantity { get; set; }
    public string Section { get; set; } = "Main"; // Main, Sideboard, Commander, Companion
    public DateTime DateAdded { get; set; }
}
