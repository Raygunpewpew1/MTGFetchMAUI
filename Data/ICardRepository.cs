using MTGFetchMAUI.Core;
using MTGFetchMAUI.Models;

namespace MTGFetchMAUI.Data;

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

    Task<string> GetScryfallIdAsync(string cardUUID);
    Task<CardRuling[]> GetCardRulingsAsync(string uuid);

    Task<string[]> GetOtherFaceIdsAsync(string uuid);
    Task<Card[]> GetCardWithOtherFacesAsync(string uuid);
    Task<Card[]> GetFullCardPackageAsync(string uuid);
    Task<Dictionary<string, Card>> GetCardsByUUIDsAsync(string[] uuids);

    Task<Card[]> SearchCardsAsync(string searchText, int limit = 100);
    Task<Card[]> SearchCardsAdvancedAsync(MTGSearchHelper searchHelper);
    Task<int> GetCountAdvancedAsync(MTGSearchHelper searchHelper);
    MTGSearchHelper CreateSearchHelper();
}
