using AetherVault.Models;
using Dapper;

namespace AetherVault.Data;

public class DeckRepository : IDeckRepository
{
    private readonly DatabaseManager _databaseManager;

    public DeckRepository(DatabaseManager databaseManager)
    {
        _databaseManager = databaseManager;
    }

    public async Task<int> CreateDeckAsync(DeckEntity deck)
    {
        if (!_databaseManager.IsConnected)
            throw new InvalidOperationException("Database not connected.");

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            using var transaction = _databaseManager.CollectionConnection.BeginTransaction();

            await _databaseManager.CollectionConnection.ExecuteAsync(
                SQLQueries.DeckInsert,
                new
                {
                    deck.Name,
                    deck.Format,
                    deck.Description,
                    CoverCardId = deck.CoverCardId ?? "",
                    CommanderId = deck.CommanderId ?? "",
                    CommanderName = deck.CommanderName ?? "",
                    PartnerId = deck.PartnerId ?? "",
                    ColorIdentity = deck.ColorIdentity ?? ""
                },
                transaction);

            var newId = await _databaseManager.CollectionConnection.QuerySingleAsync<long>(
                SQLQueries.DeckGetLastId,
                transaction: transaction);

            transaction.Commit();
            return (int)newId;
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task UpdateDeckAsync(DeckEntity deck)
    {
        if (!_databaseManager.IsConnected) return;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            await _databaseManager.CollectionConnection.ExecuteAsync(
                SQLQueries.DeckUpdate,
                new
                {
                    deck.Name,
                    deck.Description,
                    CoverCardId = deck.CoverCardId ?? "",
                    CommanderId = deck.CommanderId ?? "",
                    CommanderName = deck.CommanderName ?? "",
                    PartnerId = deck.PartnerId ?? "",
                    ColorIdentity = deck.ColorIdentity ?? "",
                    deck.Id
                });
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task DeleteDeckAsync(int deckId)
    {
        if (!_databaseManager.IsConnected) return;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            // Transaction for deletion
            using var transaction = _databaseManager.CollectionConnection.BeginTransaction();

            await _databaseManager.CollectionConnection.ExecuteAsync(
                SQLQueries.DeckDeleteCards,
                new { Id = deckId },
                transaction);

            await _databaseManager.CollectionConnection.ExecuteAsync(
                SQLQueries.DeckDelete,
                new { Id = deckId },
                transaction);

            transaction.Commit();
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task<DeckEntity?> GetDeckAsync(int deckId)
    {
        if (!_databaseManager.IsConnected) return null;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            return await _databaseManager.CollectionConnection.QueryFirstOrDefaultAsync<DeckEntity>(
                SQLQueries.DeckGet,
                new { Id = deckId });
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task<List<DeckEntity>> GetAllDecksAsync()
    {
        if (!_databaseManager.IsConnected) return new List<DeckEntity>();

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            var result = await _databaseManager.CollectionConnection.QueryAsync<DeckEntity>(SQLQueries.DeckGetAll);
            return result.ToList();
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task AddCardToDeckAsync(DeckCardEntity card)
    {
        if (!_databaseManager.IsConnected) return;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            await _databaseManager.CollectionConnection.ExecuteAsync(
                SQLQueries.DeckAddCard,
                new
                {
                    card.DeckId,
                    card.CardId,
                    card.Quantity,
                    card.Section,
                    DateAdded = card.DateAdded.ToString("yyyy-MM-dd HH:mm:ss")
                });
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task RemoveCardFromDeckAsync(int deckId, string cardId, string section)
    {
        if (!_databaseManager.IsConnected) return;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            await _databaseManager.CollectionConnection.ExecuteAsync(
                SQLQueries.DeckRemoveCard,
                new
                {
                    DeckId = deckId,
                    CardId = cardId,
                    Section = section
                });
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task UpdateCardQuantityAsync(int deckId, string cardId, string section, int quantity)
    {
        if (!_databaseManager.IsConnected) return;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            await _databaseManager.CollectionConnection.ExecuteAsync(
                SQLQueries.DeckUpdateCardQuantity,
                new
                {
                    DeckId = deckId,
                    CardId = cardId,
                    Section = section,
                    Quantity = quantity
                });
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task<List<DeckCardEntity>> GetDeckCardsAsync(int deckId)
    {
        if (!_databaseManager.IsConnected) return new List<DeckCardEntity>();

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            var result = await _databaseManager.CollectionConnection.QueryAsync<DeckCardEntity>(
                SQLQueries.DeckGetCards,
                new { DeckId = deckId });
            return result.ToList();
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }
}
