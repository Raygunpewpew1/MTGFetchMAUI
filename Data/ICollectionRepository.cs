using MTGFetchMAUI.Models;

namespace MTGFetchMAUI.Data;

/// <summary>
/// Async interface for user collection data access.
/// Port of ICollectionRepository from CollectionRepository.pas.
/// </summary>
public interface ICollectionRepository
{
    Task AddCardAsync(string cardUUID, int quantity = 1, bool isFoil = false, bool isEtched = false);
    Task RemoveCardAsync(string cardUUID);
    Task UpdateQuantityAsync(string cardUUID, int quantity, bool isFoil = false, bool isEtched = false);
    Task<CollectionItem[]> GetCollectionAsync();
    Task<CollectionStats> GetCollectionStatsAsync();
    Task<bool> IsInCollectionAsync(string cardUUID);
    Task<int> GetQuantityAsync(string cardUUID);
    Task ReorderAsync(IList<string> orderedUuids);
}
