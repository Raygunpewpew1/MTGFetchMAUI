using MTGFetchMAUI.Core;

namespace MTGFetchMAUI.Models;

/// <summary>
/// Database entity for a Deck.
/// </summary>
public class DeckEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Format { get; set; } = DeckFormat.Standard.ToDbField();
    public string Description { get; set; } = "";
    public string CoverCardId { get; set; } = "";
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }

    // Commander specific
    public string CommanderId { get; set; } = "";
    public string CommanderName { get; set; } = "";
    public string PartnerId { get; set; } = "";
    public string ColorIdentity { get; set; } = "";

    public bool HasCommander => !string.IsNullOrEmpty(CommanderName);
    public string CommanderDisplay => HasCommander ? $"☆ {CommanderName}" : "";
    public string FormatDisplay => EnumExtensions.ParseDeckFormat(Format).ToDisplayName();
}
