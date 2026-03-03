using AetherVault.Core;
using AetherVault.Models;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AetherVault.Data;

/// <summary>
/// Async user collection CRUD operations.
/// Port of TCollectionRepository from CollectionRepository.pas.
/// </summary>
public class CollectionRepository : ICollectionRepository
{
    private class CollectionRow
    {
        public string card_uuid { get; set; } = "";
        public int quantity { get; set; }
        public string date_added { get; set; } = "";
        public int? sort_order { get; set; }
        public int? is_foil { get; set; }
        public int? is_etched { get; set; }
    }

    private readonly DatabaseManager _db;
    private readonly ICardRepository _cardRepo;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CollectionRepository(DatabaseManager databaseManager, ICardRepository cardRepository)
    {
        _db = databaseManager;
        _cardRepo = cardRepository;
    }

    public async Task AddCardAsync(string cardUUID, int quantity = 1, bool isFoil = false, bool isEtched = false)
    {
        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            var currentQty = await GetQuantityInternalAsync(conn, cardUUID, trans);

            if (currentQty > 0)
            {
                await conn.ExecuteAsync(
                    SQLQueries.CollectionUpdateQuantity,
                    new { qty = currentQty + quantity, isFoil = isFoil ? 1 : 0, isEtched = isEtched ? 1 : 0, uuid = cardUUID },
                    trans);
            }
            else
            {
                await conn.ExecuteAsync(
                    SQLQueries.CollectionInsertCard,
                    new { uuid = cardUUID, qty = quantity, isFoil = isFoil ? 1 : 0, isEtched = isEtched ? 1 : 0 },
                    trans);
            }
        });
    }

    public async Task AddCardsBulkAsync(IEnumerable<(string cardUUID, int quantity, bool isFoil, bool isEtched)> cards)
    {
        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            foreach (var card in cards)
            {
                var currentQty = await GetQuantityInternalAsync(conn, card.cardUUID, trans);

                if (currentQty > 0)
                {
                    await conn.ExecuteAsync(
                        SQLQueries.CollectionUpdateQuantity,
                        new { qty = currentQty + card.quantity, isFoil = card.isFoil ? 1 : 0, isEtched = card.isEtched ? 1 : 0, uuid = card.cardUUID },
                        trans);
                }
                else
                {
                    await conn.ExecuteAsync(
                        SQLQueries.CollectionInsertCard,
                        new { uuid = card.cardUUID, qty = card.quantity, isFoil = card.isFoil ? 1 : 0, isEtched = card.isEtched ? 1 : 0 },
                        trans);
                }
            }
        });
    }

    public async Task RemoveCardAsync(string cardUUID)
    {
        await _lock.WaitAsync();
        try
        {
            await _db.CollectionConnection.ExecuteAsync(SQLQueries.CollectionDeleteCard, new { uuid = cardUUID });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateQuantityAsync(string cardUUID, int quantity, bool isFoil = false, bool isEtched = false)
    {
        if (quantity <= 0)
        {
            await RemoveCardAsync(cardUUID);
            return;
        }

        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            var currentQty = await GetQuantityInternalAsync(conn, cardUUID, trans);

            if (currentQty > 0)
            {
                await conn.ExecuteAsync(
                    SQLQueries.CollectionUpdateQuantity,
                    new { qty = quantity, isFoil = isFoil ? 1 : 0, isEtched = isEtched ? 1 : 0, uuid = cardUUID },
                    trans);
            }
            else
            {
                await conn.ExecuteAsync(
                    SQLQueries.CollectionInsertCard,
                    new { uuid = cardUUID, qty = quantity, isFoil = isFoil ? 1 : 0, isEtched = isEtched ? 1 : 0 },
                    trans);
            }
        });
    }

    public async Task<CollectionItem[]> GetCollectionAsync()
    {
        var items = new List<CollectionItem>();
        IEnumerable<CollectionRow> entries;

        await _lock.WaitAsync();
        try
        {
            entries = await _db.CollectionConnection.QueryAsync<CollectionRow>(SQLQueries.CollectionGetAll);
        }
        finally
        {
            _lock.Release();
        }

        var entryList = entries.ToList();
        var uuids = entryList.Select(e => e.card_uuid).ToArray();

        // Batch-load card details from MTG database
        if (uuids.Length > 0)
        {
            var cardCache = await _cardRepo.GetCardsByUUIDsAsync(uuids);

            foreach (var row in entryList)
            {
                if (cardCache.TryGetValue(row.card_uuid, out var card))
                {
                    DateTime dateAdded = DateTime.TryParse(row.date_added, out var d) ? d : DateTime.Now;
                    items.Add(new CollectionItem
                    {
                        CardUUID = row.card_uuid,
                        Quantity = row.quantity,
                        IsFoil = row.is_foil.HasValue && row.is_foil.Value != 0,
                        IsEtched = row.is_etched.HasValue && row.is_etched.Value != 0,
                        DateAdded = dateAdded,
                        SortOrder = row.sort_order ?? 0,
                        Card = card
                    });
                }
            }
        }

        return [.. items];
    }

    public async Task<CollectionStats> GetCollectionStatsAsync()
    {
        var collection = await GetCollectionAsync();
        return CalculateStats(collection);
    }

    /// <summary>
    /// Calculates collection statistics from a list of items.
    /// Public static for testing.
    /// </summary>
    public static CollectionStats CalculateStats(IList<CollectionItem> collection)
    {
        var stats = new CollectionStats();
        double totalCMC = 0;
        int nonLandCount = 0;

        foreach (var item in collection)
        {
            stats.UniqueCards++;
            stats.TotalCards += item.Quantity;

            // Type Breakdown
            if (item.Card.IsCreature)
                stats.CreatureCount += item.Quantity;
            else if (item.Card.IsLand)
                stats.LandCount += item.Quantity;
            else
                stats.SpellCount += item.Quantity;

            // Rarity Breakdown
            switch (item.Card.Rarity)
            {
                case CardRarity.Common: stats.CommonCount += item.Quantity; break;
                case CardRarity.Uncommon: stats.UncommonCount += item.Quantity; break;
                case CardRarity.Rare: stats.RareCount += item.Quantity; break;
                case CardRarity.Mythic: stats.MythicCount += item.Quantity; break;
            }

            // CMC Calculation (Non-Lands only)
            if (!item.Card.IsLand)
            {
                totalCMC += item.Card.CMC * item.Quantity;
                nonLandCount += item.Quantity;
            }

            if (item.IsFoil || item.IsEtched)
                stats.FoilCount += item.Quantity;
        }

        if (nonLandCount > 0)
            stats.AvgCMC = totalCMC / nonLandCount;

        return stats;
    }

    public async Task<bool> IsInCollectionAsync(string cardUUID)
    {
        await _lock.WaitAsync();
        try
        {
            var result = await _db.CollectionConnection.QueryFirstOrDefaultAsync<int?>(
                SQLQueries.CollectionCheckExists, new { uuid = cardUUID });
            return result.HasValue;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetQuantityAsync(string cardUUID)
    {
        await _lock.WaitAsync();
        try
        {
            return await GetQuantityInternalAsync(_db.CollectionConnection, cardUUID);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ReorderAsync(IList<string> orderedUuids)
    {
        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            for (int i = 0; i < orderedUuids.Count; i++)
            {
                await conn.ExecuteAsync(
                    SQLQueries.CollectionReorderItem,
                    new { sortOrder = i, uuid = orderedUuids[i] },
                    trans);
            }
        });
    }

    public async Task ClearCollectionAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await _db.CollectionConnection.ExecuteAsync(SQLQueries.CollectionClearAll);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Private helpers ─────────────────────────────────────────────

    private Task<int> GetQuantityInternalAsync(SqliteConnection conn, string cardUUID, SqliteTransaction? trans = null)
    {
        return conn.QueryFirstOrDefaultAsync<int>(
            SQLQueries.CollectionGetQuantity,
            new { uuid = cardUUID },
            trans);
    }

    private async Task WithCollectionTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> action)
    {
        await _lock.WaitAsync();
        try
        {
            var conn = _db.CollectionConnection;
            using var transaction = conn.BeginTransaction();
            try
            {
                await action(conn, transaction);
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
