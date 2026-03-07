using AetherVault.Models;

namespace AetherVault.Data;

/// <summary>
/// Async interface for user collection data access.
/// Port of ICollectionRepository from CollectionRepository.pas.
/// </summary>
public interface ICollectionRepository
{
    Task AddCardAsync(string cardUUID, int quantity = 1, bool isFoil = false, bool isEtched = false);
    Task AddCardsBulkAsync(IEnumerable<(string cardUUID, int quantity, bool isFoil, bool isEtched)> cards);
    Task RemoveCardAsync(string cardUUID);
    Task ClearCollectionAsync();
    Task UpdateQuantityAsync(string cardUUID, int quantity, bool isFoil = false, bool isEtched = false);
    Task<CollectionItem[]> GetCollectionAsync();
    /// <summary>Lightweight list of (uuid, quantity, isFoil, isEtched) for pricing total. No Card load.</summary>
    Task<IReadOnlyList<(string Uuid, int Quantity, bool IsFoil, bool IsEtched)>> GetCollectionEntriesForPricingAsync();
    Task<CollectionStats> GetCollectionStatsAsync();
    Task<bool> IsInCollectionAsync(string cardUUID);
    Task<int> GetQuantityAsync(string cardUUID);
    Task ReorderAsync(IList<string> orderedUuids);
}
