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

    public async Task<int> GetDeckCardCountAsync(int deckId)
    {
        if (!_databaseManager.IsConnected) return 0;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            using var cmd = _databaseManager.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.DeckGetCardCount;
            cmd.Parameters.AddWithValue("@DeckId", deckId);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result ?? 0);
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    private static DeckEntity MapDeck(SqliteDataReader reader)
    {
        var createdStr = reader.IsDBNull(reader.GetOrdinal("DateCreated")) ? null : reader.GetString(reader.GetOrdinal("DateCreated"));
        var modifiedStr = reader.IsDBNull(reader.GetOrdinal("DateModified")) ? null : reader.GetString(reader.GetOrdinal("DateModified"));

        DateTime.TryParse(createdStr, out var created);
        DateTime.TryParse(modifiedStr, out var modified);

        return new DeckEntity
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Format = reader.GetString(reader.GetOrdinal("Format")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? "" : reader.GetString(reader.GetOrdinal("Description")),
            CoverCardId = reader.IsDBNull(reader.GetOrdinal("CoverCardId")) ? "" : reader.GetString(reader.GetOrdinal("CoverCardId")),
            DateCreated = created,
            DateModified = modified,
            CommanderId = reader.IsDBNull(reader.GetOrdinal("CommanderId")) ? "" : reader.GetString(reader.GetOrdinal("CommanderId")),
            CommanderName = reader.IsDBNull(reader.GetOrdinal("CommanderName")) ? "" : reader.GetString(reader.GetOrdinal("CommanderName")),
            PartnerId = reader.IsDBNull(reader.GetOrdinal("PartnerId")) ? "" : reader.GetString(reader.GetOrdinal("PartnerId")),
            ColorIdentity = reader.IsDBNull(reader.GetOrdinal("ColorIdentity")) ? "" : reader.GetString(reader.GetOrdinal("ColorIdentity"))
        };
    }
}
