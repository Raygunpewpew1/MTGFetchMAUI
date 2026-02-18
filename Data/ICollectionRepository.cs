using MTGFetchMAUI.Models;

namespace MTGFetchMAUI.Data;

/// <summary>
/// Async interface for user collection data access.
/// Port of ICollectionRepository from CollectionRepository.pas.
/// </summary>
public interface ICollectionRepository
{
    Task AddCardAsync(string cardUUID, int quantity = 1);
    Task RemoveCardAsync(string cardUUID);
    Task UpdateQuantityAsync(string cardUUID, int quantity);
    Task<CollectionItem[]> GetCollectionAsync();
    Task<CollectionStats> GetCollectionStatsAsync();
    Task<bool> IsInCollectionAsync(string cardUUID);
    Task<int> GetQuantityAsync(string cardUUID);
}
