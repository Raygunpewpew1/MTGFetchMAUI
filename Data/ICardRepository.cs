using AetherVault.Core;
using AetherVault.Models;

namespace AetherVault.Data;

/// <summary>
/// Async interface for card data access operations.
/// Port of ICardRepository from CardRepository.pas.
/// </summary>
public interface ICardRepository
{
    Task<Card> GetCardByUUIDAsync(string uuid);
    Task<Card> GetCardDetailsAsync(string uuid);
    Task<Card> GetCardWithLegalitiesAsync(string uuid);
    Task<Card> GetCardWithRulingsAsync(string uuid);
    Task<Card> GetCardByFaceNameAndSetAsync(string faceName, string setCode);
    Task<Card?> GetCardByNameAndSetAsync(string name, string setCode);
    Task<Card?> GetCardByScryfallIdAsync(string scryfallId);

    Task<string> GetScryfallIdAsync(string cardUUID);
    Task<CardRuling[]> GetCardRulingsAsync(string uuid);

    Task<string[]> GetOtherFaceIdsAsync(string uuid);
    Task<Card[]> GetCardWithOtherFacesAsync(string uuid);
    Task<Card[]> GetFullCardPackageAsync(string uuid);
    Task<Dictionary<string, Card>> GetCardsByUUIDsAsync(string[] uuids);
    Task<IReadOnlyList<ImportLookupRow>> GetImportLookupRowsAsync();

    Task<Card[]> SearchCardsAsync(string searchText, int limit = 100);
    Task<Card[]> SearchCardsAdvancedAsync(MTGSearchHelper searchHelper);
    Task<int> GetCountAdvancedAsync(MTGSearchHelper searchHelper);
    MTGSearchHelper CreateSearchHelper();

    /// <summary>Returns all sets (code + name) for filter dropdowns, ordered by name.</summary>
    Task<IReadOnlyList<SetInfo>> GetAllSetsAsync();

    /// <summary>Returns true if the av_cards_fts table exists (built by CI). When false, search falls back to LIKE.</summary>
    Task<bool> HasFtsAsync();
}
