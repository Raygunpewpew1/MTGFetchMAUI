using AetherVault.Models;
using Dapper;
using Microsoft.Data.Sqlite;
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
                    ColorIdentity = deck.ColorIdentity ?? "",
                    CommanderArchetype = string.IsNullOrEmpty(deck.CommanderArchetype) ? "Unknown" : deck.CommanderArchetype
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

        await _databaseManager.RunWithConnectionLockAsync(async () =>
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
                    CommanderArchetype = string.IsNullOrEmpty(deck.CommanderArchetype) ? "Unknown" : deck.CommanderArchetype,
                    deck.Id
                });
        });
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

        return await _databaseManager.RunWithConnectionLockAsync(() =>
            _databaseManager.CollectionConnection.QueryFirstOrDefaultAsync<DeckEntity>(
                SqlQueries.DeckGet,
                new { Id = deckId }));
    }

    public async Task<List<DeckEntity>> GetAllDecksAsync()
    {
        if (!_databaseManager.IsConnected) return new List<DeckEntity>();

        return await _databaseManager.RunWithConnectionLockAsync(async () =>
        {
            var result = await _databaseManager.CollectionConnection.QueryAsync<DeckEntity>(SqlQueries.DeckGetAll);
            return result.ToList();
        });
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

        return await _databaseManager.RunWithConnectionLockAsync(async () =>
        {
            var result = await _databaseManager.CollectionConnection.QueryAsync<DeckCardEntity>(
                SqlQueries.DeckGetCards,
                new { DeckId = deckId });
            return result.ToList();
        });
    }

    public async Task<int> GetDeckCardCountAsync(int deckId)
    {
        if (!_databaseManager.IsConnected) return 0;

        return await _databaseManager.RunWithConnectionLockAsync(() =>
            _databaseManager.CollectionConnection.ExecuteScalarAsync<int>(
                SqlQueries.DeckGetCardCount, new { DeckId = deckId }));
    }

    public async Task ApplyMutationsAsync(int deckId, IReadOnlyList<DeckCardPersistenceMutation> mutations)
    {
        if (mutations == null || mutations.Count == 0)
            return;

        if (!_databaseManager.IsConnected)
            throw new InvalidOperationException("Database not connected.");

        await WithDeckTransactionAsync(async (conn, transaction) =>
        {
            foreach (var m in mutations)
            {
                switch (m.Kind)
                {
                    case DeckCardPersistenceKind.Remove:
                        await conn.ExecuteAsync(
                            SqlQueries.DeckRemoveCard,
                            new { DeckId = deckId, CardId = m.CardId, Section = m.Section },
                            transaction);
                        break;

                    case DeckCardPersistenceKind.UpdateQuantity:
                        if (m.Quantity <= 0)
                        {
                            await conn.ExecuteAsync(
                                SqlQueries.DeckRemoveCard,
                                new { DeckId = deckId, CardId = m.CardId, Section = m.Section },
                                transaction);
                        }
                        else
                        {
                            await conn.ExecuteAsync(
                                SqlQueries.DeckUpdateCardQuantity,
                                new
                                {
                                    DeckId = deckId,
                                    CardId = m.CardId,
                                    Section = m.Section,
                                    Quantity = m.Quantity
                                },
                                transaction);
                        }
                        break;

                    case DeckCardPersistenceKind.InsertOrReplace:
                        var dateAdded = (m.DateAdded ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss");
                        await conn.ExecuteAsync(
                            SqlQueries.DeckAddCard,
                            new
                            {
                                DeckId = deckId,
                                CardId = m.CardId,
                                Quantity = m.Quantity,
                                Section = m.Section,
                                DateAdded = dateAdded
                            },
                            transaction);
                        break;
                }
            }
        });
    }

    private Task<T> WithDeckTransactionAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> action) =>
        _databaseManager.RunWithConnectionLockAsync(async () =>
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
        });

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
            ColorIdentity = reader.IsDBNull(reader.GetOrdinal("ColorIdentity")) ? "" : reader.GetString(reader.GetOrdinal("ColorIdentity")),
            CommanderArchetype = SafeCol(reader, "CommanderArchetype", "Unknown")
        };
    }

    private static string SafeCol(SqliteDataReader reader, string colName, string defaultValue)
    {
        try
        {
            int ord = reader.GetOrdinal(colName);
            return reader.IsDBNull(ord) ? defaultValue : reader.GetString(ord);
        }
        catch (Exception)
        {
            return defaultValue;
        }
    }

}
