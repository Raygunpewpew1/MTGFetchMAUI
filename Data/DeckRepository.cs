using AetherVault.Models;
using AetherVault.Services;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AetherVault.Data;

/// <summary>
/// CRUD for decks and deck cards. Uses the Collection database (Decks and DeckCards tables).
/// DeckBuilderService coordinates deck logic; this class handles persistence only.
/// </summary>
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

        return await WithDeckTransactionAsync(async (conn, transaction) =>
        {
            await conn.ExecuteAsync(
                SqlQueries.DeckInsert,
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

            var newId = await conn.QuerySingleAsync<long>(
                SqlQueries.DeckGetLastId,
                transaction: transaction);

            return (int)newId;
        });
    }

    public async Task UpdateDeckAsync(DeckEntity deck)
    {
        if (!_databaseManager.IsConnected) return;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            await _databaseManager.CollectionConnection.ExecuteAsync(
                SqlQueries.DeckUpdate,
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

        await WithDeckTransactionAsync(async (conn, transaction) =>
        {
            await conn.ExecuteAsync(
                SqlQueries.DeckDeleteCards,
                new { Id = deckId },
                transaction);

            await conn.ExecuteAsync(
                SqlQueries.DeckDelete,
                new { Id = deckId },
                transaction);
        });
    }

    public async Task<DeckEntity?> GetDeckAsync(int deckId)
    {
        if (!_databaseManager.IsConnected) return null;

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            return await _databaseManager.CollectionConnection.QueryFirstOrDefaultAsync<DeckEntity>(
                SqlQueries.DeckGet,
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
            #region agent log
            AgentDebugLog("initial", "H2", "Data/DeckRepository.cs:GetAllDecksAsync:before-query", "Fetching all decks", new
            {
                isConnected = _databaseManager.IsConnected,
                connState = _databaseManager.CollectionConnection.State.ToString()
            });
            #endregion
            var result = await _databaseManager.CollectionConnection.QueryAsync<DeckEntity>(SqlQueries.DeckGetAll);
            var list = result.ToList();
            #region agent log
            AgentDebugLog("initial", "H2", "Data/DeckRepository.cs:GetAllDecksAsync:after-query", "Fetched all decks", new
            {
                count = list.Count
            });
            #endregion
            return list;
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task AddCardToDeckAsync(DeckCardEntity card)
    {
        if (!_databaseManager.IsConnected) return;

        await WithDeckTransactionAsync(async (conn, transaction) =>
        {
            await conn.ExecuteAsync(
                SqlQueries.DeckAddCard,
                new
                {
                    card.DeckId,
                    card.CardId,
                    card.Quantity,
                    card.Section,
                    DateAdded = card.DateAdded.ToString("yyyy-MM-dd HH:mm:ss")
                },
                transaction);
        });
    }

    public async Task RemoveCardFromDeckAsync(int deckId, string cardId, string section)
    {
        if (!_databaseManager.IsConnected) return;

        await WithDeckTransactionAsync(async (conn, transaction) =>
        {
            await conn.ExecuteAsync(
                SqlQueries.DeckRemoveCard,
                new
                {
                    DeckId = deckId,
                    CardId = cardId,
                    Section = section
                },
                transaction);
        });
    }

    public async Task UpdateCardQuantityAsync(int deckId, string cardId, string section, int quantity)
    {
        if (!_databaseManager.IsConnected) return;

        await WithDeckTransactionAsync(async (conn, transaction) =>
        {
            await conn.ExecuteAsync(
                SqlQueries.DeckUpdateCardQuantity,
                new
                {
                    DeckId = deckId,
                    CardId = cardId,
                    Section = section,
                    Quantity = quantity
                },
                transaction);
        });
    }

    public async Task<List<DeckCardEntity>> GetDeckCardsAsync(int deckId)
    {
        if (!_databaseManager.IsConnected) return new List<DeckCardEntity>();

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            var result = await _databaseManager.CollectionConnection.QueryAsync<DeckCardEntity>(
                SqlQueries.DeckGetCards,
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
            var conn = _databaseManager.CollectionConnection;
            return await conn.ExecuteScalarAsync<int>(SqlQueries.DeckGetCardCount, new { DeckId = deckId });
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    public async Task<Dictionary<int, int>> GetDeckCardCountsAsync(IEnumerable<int> deckIds)
    {
        var deckIdsList = deckIds.ToList();
        if (deckIdsList.Count == 0) return new Dictionary<int, int>();

        if (!_databaseManager.IsConnected) return deckIdsList.ToDictionary(id => id, _ => 0);

        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            var conn = _databaseManager.CollectionConnection;
            var rows = await conn.QueryAsync<(int DeckId, int Total)>(SqlQueries.DeckGetCardCountsBatch, new { DeckIds = deckIdsList });
            var dict = deckIdsList.ToDictionary(id => id, _ => 0);
            foreach (var row in rows)
                dict[row.DeckId] = row.Total;
            return dict;
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    private async Task<T> WithDeckTransactionAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> action)
    {
        await _databaseManager.ConnectionLock.WaitAsync();
        try
        {
            var conn = _databaseManager.CollectionConnection;
            using var transaction = conn.BeginTransaction();
            try
            {
                var result = await action(conn, transaction);
                transaction.Commit();
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        finally
        {
            _databaseManager.ConnectionLock.Release();
        }
    }

    private Task WithDeckTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> action) =>
        WithDeckTransactionAsync(async (conn, trans) =>
        {
            await action(conn, trans);
            return true;
        });

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

    private static void AgentDebugLog(string runId, string hypothesisId, string location, string message, object data)
    {
        try
        {
            var dataJson = JsonSerializer.Serialize(data);
            Logger.LogStuff($"DBG|session=068b48|run={runId}|h={hypothesisId}|loc={location}|msg={message}|data={dataJson}", LogLevel.Info);
        }
        catch
        {
            // Never fail app flow for debug logging.
        }
    }
}
