using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Models;

namespace MTGFetchMAUI.Data;

/// <summary>
/// Async user collection CRUD operations.
/// Port of TCollectionRepository from CollectionRepository.pas.
/// </summary>
public class CollectionRepository : ICollectionRepository
{
    private readonly DatabaseManager _db;
    private readonly ICardRepository _cardRepo;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CollectionRepository(DatabaseManager databaseManager, ICardRepository cardRepository)
    {
        _db = databaseManager;
        _cardRepo = cardRepository;
    }

    public async Task AddCardAsync(string cardUUID, int quantity = 1)
    {
        await WithCollectionTransactionAsync(async conn =>
        {
            var currentQty = await GetQuantityInternalAsync(conn, cardUUID);

            if (currentQty > 0)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SQLQueries.CollectionUpdateQuantity;
                cmd.Parameters.AddWithValue("@qty", currentQty + quantity);
                cmd.Parameters.AddWithValue("@uuid", cardUUID);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SQLQueries.CollectionInsertCard;
                cmd.Parameters.AddWithValue("@uuid", cardUUID);
                cmd.Parameters.AddWithValue("@qty", quantity);
                await cmd.ExecuteNonQueryAsync();
            }
        });
    }

    public async Task RemoveCardAsync(string cardUUID)
    {
        await WithCollectionCommandAsync(SQLQueries.CollectionDeleteCard, cmd =>
            cmd.Parameters.AddWithValue("@uuid", cardUUID));
    }

    public async Task UpdateQuantityAsync(string cardUUID, int quantity)
    {
        if (quantity <= 0)
        {
            await RemoveCardAsync(cardUUID);
            return;
        }

        await WithCollectionCommandAsync(SQLQueries.CollectionUpdateQuantity, cmd =>
        {
            cmd.Parameters.AddWithValue("@qty", quantity);
            cmd.Parameters.AddWithValue("@uuid", cardUUID);
        });
    }

    public async Task<CollectionItem[]> GetCollectionAsync()
    {
        var items = new List<CollectionItem>();
        var uuids = new List<string>();
        var entries = new List<(string uuid, int qty, DateTime dateAdded)>();

        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.CollectionGetAll;
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var uuid = reader.GetString(0);
                var qty = reader.GetInt32(1);
                var dateAdded = DateTime.TryParse(reader.GetString(2), out var d) ? d : DateTime.Now;
                uuids.Add(uuid);
                entries.Add((uuid, qty, dateAdded));
            }
        }
        finally
        {
            _lock.Release();
        }

        // Batch-load card details from MTG database
        if (uuids.Count > 0)
        {
            var cardCache = await _cardRepo.GetCardsByUUIDsAsync(uuids.ToArray());

            foreach (var (uuid, qty, dateAdded) in entries)
            {
                if (cardCache.TryGetValue(uuid, out var card))
                {
                    items.Add(new CollectionItem
                    {
                        CardUUID = uuid,
                        Quantity = qty,
                        DateAdded = dateAdded,
                        Card = card
                    });
                }
            }
        }

        return items.ToArray();
    }

    public async Task<CollectionStats> GetCollectionStatsAsync()
    {
        var stats = new CollectionStats();
        var collection = await GetCollectionAsync();

        foreach (var item in collection)
        {
            stats.UniqueCards++;
            stats.TotalCards += item.Quantity;

            if (item.Card.IsCreature)
                stats.CreatureCount += item.Quantity;
            else if (item.Card.IsLand)
                stats.LandCount += item.Quantity;
            else
                stats.SpellCount += item.Quantity;

            switch (item.Card.Rarity)
            {
                case CardRarity.Common: stats.CommonCount += item.Quantity; break;
                case CardRarity.Uncommon: stats.UncommonCount += item.Quantity; break;
                case CardRarity.Rare: stats.RareCount += item.Quantity; break;
                case CardRarity.Mythic: stats.MythicCount += item.Quantity; break;
            }
        }

        return stats;
    }

    public async Task<bool> IsInCollectionAsync(string cardUUID)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.CollectionCheckExists;
            cmd.Parameters.AddWithValue("@uuid", cardUUID);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetQuantityAsync(string cardUUID)
    {
        return await GetQuantityInternalAsync(_db.CollectionConnection, cardUUID);
    }

    // ── Private helpers ─────────────────────────────────────────────

    private async Task<int> GetQuantityInternalAsync(SqliteConnection conn, string cardUUID)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SQLQueries.CollectionGetQuantity;
        cmd.Parameters.AddWithValue("@uuid", cardUUID);
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? reader.GetInt32(0) : 0;
    }

    private async Task WithCollectionCommandAsync(string sql, Action<SqliteCommand> configureParams)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = sql;
            configureParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task WithCollectionTransactionAsync(Func<SqliteConnection, Task> action)
    {
        await _lock.WaitAsync();
        try
        {
            var conn = _db.CollectionConnection;
            using var transaction = conn.BeginTransaction();
            try
            {
                await action(conn);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
