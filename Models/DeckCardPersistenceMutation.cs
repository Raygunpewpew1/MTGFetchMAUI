namespace AetherVault.Models;

/// <summary>
/// Low-level deck card persistence step applied inside a single DB transaction.
/// </summary>
public enum DeckCardPersistenceKind
{
    Remove,
    UpdateQuantity,
    InsertOrReplace
}

/// <summary>One mutation step for batch deck card persistence.</summary>
public sealed record DeckCardPersistenceMutation(
    DeckCardPersistenceKind Kind,
    string CardId,
    string Section,
    int Quantity = 0,
    DateTime? DateAdded = null);
