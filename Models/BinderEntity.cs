namespace AetherVault.Models;

/// <summary>
/// Represents a named binder for organizing the user's card collection.
/// Binders are tags/labels on top of the main collection — cards can belong
/// to multiple binders and always remain in the main collection.
/// </summary>
public class BinderEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }

    /// <summary>Populated at load time from a subquery; not a persisted column.</summary>
    public int CardCount { get; set; }

    public string CardCountDisplay => CardCount == 1 ? "1 card" : $"{CardCount} cards";
}
