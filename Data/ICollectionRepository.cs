using AetherVault.Models;

namespace AetherVault.Data;

/// <summary>
/// Async interface for user collection data access.
/// Port of ICollectionRepository from CollectionRepository.pas.
/// </summary>
public interface ICollectionRepository
{
    Task AddCardAsync(string cardUuid, int quantity = 1, bool isFoil = false, bool isEtched = false);
    Task AddCardsBulkAsync(IEnumerable<(string cardUUID, int quantity, bool isFoil, bool isEtched)> cards);
    Task RemoveCardAsync(string cardUuid);
    Task ClearCollectionAsync();
    Task UpdateQuantityAsync(string cardUuid, int quantity, bool isFoil = false, bool isEtched = false);
    Task<CollectionItem[]> GetCollectionAsync();
    /// <summary>Lightweight list of (uuid, quantity, isFoil, isEtched) for pricing total. No Card load.</summary>
    Task<IReadOnlyList<(string Uuid, int Quantity, bool IsFoil, bool IsEtched)>> GetCollectionEntriesForPricingAsync();
    Task<CollectionStats> GetCollectionStatsAsync();
    Task<bool> IsInCollectionAsync(string cardUuid);
    Task<int> GetQuantityAsync(string cardUuid);
    /// <summary>Returns owned quantity per UUID (0 for cards not in collection).</summary>
    Task<Dictionary<string, int>> GetQuantitiesByUuidsAsync(IEnumerable<string> cardUuids);
    Task ReorderAsync(IList<string> orderedUuids);
}
