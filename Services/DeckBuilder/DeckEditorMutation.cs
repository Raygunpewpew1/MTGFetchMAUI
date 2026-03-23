namespace AetherVault.Services.DeckBuilder;

/// <summary>High-level deck edit operations validated and applied as one atomic persistence batch.</summary>
public enum DeckEditorMutationKind
{
    /// <summary>Add copies to the given section (default quantity 1).</summary>
    Add,
    /// <summary>Set absolute quantity in the section (0 removes the row).</summary>
    SetQuantity,
    /// <summary>Remove the row for the card in the section.</summary>
    Remove,
    /// <summary>Move copies from source section to target section; quantity 0 means move all from the source row.</summary>
    Move
}

/// <summary>
/// User- or UI-initiated deck change. Applied in order; all-or-nothing after validation.
/// </summary>
public sealed record DeckEditorMutation(
    DeckEditorMutationKind Kind,
    string CardId,
    string Section,
    string? TargetSection = null,
    int Quantity = 1);
