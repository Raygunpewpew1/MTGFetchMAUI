using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Models;

namespace MTGFetchMAUI.Data;

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

            using (var insertCmd = _databaseManager.CollectionConnection.CreateCommand())
            {
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = SQLQueries.DeckInsert;
                insertCmd.Parameters.AddWithValue("@Name", deck.Name);
                insertCmd.Parameters.AddWithValue("@Format", deck.Format);
                insertCmd.Parameters.AddWithValue("@Description", deck.Description);
                insertCmd.Parameters.AddWithValue("@CoverCardId", deck.CoverCardId ?? "");
                insertCmd.Parameters.AddWithValue("@CommanderId", deck.CommanderId ?? "");
                insertCmd.Parameters.AddWithValue("@PartnerId", deck.PartnerId ?? "");
                insertCmd.Parameters.AddWithValue("@ColorIdentity", deck.ColorIdentity ?? "");
                await insertCmd.ExecuteNonQueryAsync();
            }

            long newId;
            using (var idCmd = _databaseManager.CollectionConnection.CreateCommand())
            {
                idCmd.Transaction = transaction;
                idCmd.CommandText = SQLQueries.DeckGetLastId;
                newId = (long)(await idCmd.ExecuteScalarAsync() ?? 0);
            }

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
            using var cmd = _databaseManager.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.DeckUpdate;
            cmd.Parameters.AddWithValue("@Name", deck.Name);
            cmd.Parameters.AddWithValue("@Description", deck.Description);
            cmd.Parameters.AddWithValue("@CoverCardId", deck.CoverCardId ?? "");
            cmd.Parameters.AddWithValue("@CommanderId", deck.CommanderId ?? "");
            cmd.Parameters.AddWithValue("@PartnerId", deck.PartnerId ?? "");
            cmd.Parameters.AddWithValue("@ColorIdentity", deck.ColorIdentity ?? "");
            cmd.Parameters.AddWithValue("@Id", deck.Id);
            await cmd.ExecuteNonQueryAsync();
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

            using (var cmdCards = _databaseManager.CollectionConnection.CreateCommand())
            {
                cmdCards.Transaction = transaction;
                cmdCards.CommandText = SQLQueries.DeckDeleteCards;
                cmdCards.Parameters.AddWithValue("@Id", deckId);
                await cmdCards.ExecuteNonQueryAsync();
            }

            using (var cmdDeck = _databaseManager.CollectionConnection.CreateCommand())
            {
                cmdDeck.Transaction = transaction;
                cmdDeck.CommandText = SQLQueries.DeckDelete;
                cmdDeck.Parameters.AddWithValue("@Id", deckId);
                await cmdDeck.ExecuteNonQueryAsync();
            }

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
            using var cmd = _databaseManager.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.DeckGet;
            cmd.Parameters.AddWithValue("@Id", deckId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapDeck(reader);
            }
            return null;
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task<List<DeckEntity>> GetAllDecksAsync()
    {
        var result = new List<DeckEntity>();
        if (!_databaseManager.IsConnected) return result;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            using var cmd = _databaseManager.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.DeckGetAll;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(MapDeck(reader));
            }
            return result;
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
            using var cmd = _databaseManager.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.DeckAddCard;
            cmd.Parameters.AddWithValue("@DeckId", card.DeckId);
            cmd.Parameters.AddWithValue("@CardId", card.CardId);
            cmd.Parameters.AddWithValue("@Quantity", card.Quantity);
            cmd.Parameters.AddWithValue("@Section", card.Section);
            cmd.Parameters.AddWithValue("@DateAdded", card.DateAdded);
            await cmd.ExecuteNonQueryAsync();
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
            using var cmd = _databaseManager.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.DeckRemoveCard;
            cmd.Parameters.AddWithValue("@DeckId", deckId);
            cmd.Parameters.AddWithValue("@CardId", cardId);
            cmd.Parameters.AddWithValue("@Section", section);
            await cmd.ExecuteNonQueryAsync();
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
            using var cmd = _databaseManager.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.DeckUpdateCardQuantity;
            cmd.Parameters.AddWithValue("@DeckId", deckId);
            cmd.Parameters.AddWithValue("@CardId", cardId);
            cmd.Parameters.AddWithValue("@Section", section);
            cmd.Parameters.AddWithValue("@Quantity", quantity);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task<List<DeckCardEntity>> GetDeckCardsAsync(int deckId)
    {
        var result = new List<DeckCardEntity>();
        if (!_databaseManager.IsConnected) return result;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            using var cmd = _databaseManager.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.DeckGetCards;
            cmd.Parameters.AddWithValue("@DeckId", deckId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                DateTime.TryParse(reader.GetString(reader.GetOrdinal("DateAdded")), out var dateAdded);

                result.Add(new DeckCardEntity
                {
                    DeckId = reader.GetInt32(reader.GetOrdinal("DeckId")),
                    CardId = reader.GetString(reader.GetOrdinal("CardId")),
                    Quantity = reader.GetInt32(reader.GetOrdinal("Quantity")),
                    Section = reader.GetString(reader.GetOrdinal("Section")),
                    DateAdded = dateAdded
                });
            }
            return result;
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
            PartnerId = reader.IsDBNull(reader.GetOrdinal("PartnerId")) ? "" : reader.GetString(reader.GetOrdinal("PartnerId")),
            ColorIdentity = reader.IsDBNull(reader.GetOrdinal("ColorIdentity")) ? "" : reader.GetString(reader.GetOrdinal("ColorIdentity"))
        };
    }
}
