using AetherVault.Core;
using AetherVault.Models;

namespace AetherVault.Data;

/// <summary>
/// Async interface for card data access operations.
/// Port of ICardRepository from CardRepository.pas.
/// </summary>
public interface ICardRepository
{
    Task<Card> GetCardByUuidAsync(string uuid);
    Task<Card> GetCardDetailsAsync(string uuid);
    Task<Card> GetCardWithLegalitiesAsync(string uuid);
    Task<Card> GetCardWithRulingsAsync(string uuid);
    Task<Card> GetCardByFaceAndSetAsync(string faceName, string setCode);
    Task<Card?> GetCardByNameAndSetAsync(string name, string setCode);
    Task<Card?> GetCardByScryfallIdAsync(string scryfallId);

    Task<string> GetScryfallIdAsync(string cardUuid);
    Task<CardRuling[]> GetCardRulingsAsync(string uuid);

    Task<string[]> GetOtherFaceIdsAsync(string uuid);
    Task<Card[]> GetOtherFacesAsync(string uuid);
    Task<Card[]> GetFullCardPackageAsync(string uuid);
    Task<Dictionary<string, Card>> GetCardsAsync(string[] uuids);
    Task<IReadOnlyList<ImportLookupRow>> GetImportLookupRowsAsync();

    Task<Card[]> SearchCardsAsync(string searchText, int limit = 100);
    Task<Card[]> SearchAdvancedAsync(MtgSearchHelper searchHelper);
    /// <summary>Like <see cref="SearchAdvancedAsync"/> but also returns the full match count via a window column (avoids a second COUNT query for pagination).</summary>
    Task<(Card[] cards, int totalCount)> SearchAdvancedWithResultTotalAsync(MtgSearchHelper searchHelper);
    Task<int> CountAdvancedAsync(MtgSearchHelper searchHelper);
    MtgSearchHelper CreateSearchHelper();

    /// <summary>Returns all sets (code + name) for filter dropdowns, ordered by name.</summary>
    Task<IReadOnlyList<SetInfo>> GetAllSetsAsync();

    /// <summary>Set browse rows (release date, counts, preview flag) for Search → Sets.</summary>
    Task<IReadOnlyList<SetBrowseRow>> GetSetsBrowseAsync();

    /// <summary>Returns true if the av_cards_fts table exists (built by CI). When false, search falls back to LIKE.</summary>
    Task<bool> HasFtsAsync();

    /// <summary>Other primary-face printings that share a Scryfall oracle id.</summary>
    Task<IReadOnlyList<OtherPrintingSummary>> GetOtherPrintingsByOracleIdAsync(string oracleId, string currentUuid);

}
