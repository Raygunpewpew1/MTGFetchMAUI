using MTGFetchMAUI.Models;

namespace MTGFetchMAUI.Data;

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
}
