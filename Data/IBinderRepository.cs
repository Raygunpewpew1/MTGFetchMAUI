using AetherVault.Models;

namespace AetherVault.Data;

/// <summary>
/// Async interface for binder (collection sub-group) data access.
/// Binders are named labels on top of the main collection; a card can belong
/// to multiple binders and is never removed from the main collection when a binder is deleted.
/// </summary>
public interface IBinderRepository
{
    Task<BinderEntity[]> GetAllBindersAsync();
    Task<int> CreateBinderAsync(string name, string description = "");
    Task DeleteBinderAsync(int binderId);
    Task RenameBinderAsync(int binderId, string newName);
    Task AddCardToBinderAsync(int binderId, string cardUuid);
    Task RemoveCardFromBinderAsync(int binderId, string cardUuid);
    Task<CollectionItem[]> GetBinderCardsAsync(int binderId);
    Task<int[]> GetCardBinderIdsAsync(string cardUuid);
}
