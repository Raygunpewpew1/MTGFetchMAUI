using AetherVault.Models;

namespace AetherVault.Data;

public interface IDeckRepository
{
    Task<int> CreateDeckAsync(DeckEntity deck);
    Task UpdateDeckAsync(DeckEntity deck);
    Task DeleteDeckAsync(int deckId);
    Task<DeckEntity?> GetDeckAsync(int deckId);
    Task<List<DeckEntity>> GetAllDecksAsync();

    Task AddCardToDeckAsync(DeckCardEntity card);
    Task RemoveCardFromDeckAsync(int deckId, string cardId, string section);
    Task UpdateCardQuantityAsync(int deckId, string cardId, string section, int quantity);
    Task<List<DeckCardEntity>> GetDeckCardsAsync(int deckId);
    Task<int> GetDeckCardCountAsync(int deckId);

    /// <summary>Returns card count per deck for the given deck IDs. Missing decks get count 0.</summary>
    Task<Dictionary<int, int>> GetDeckCardCountsAsync(IEnumerable<int> deckIds);
}
