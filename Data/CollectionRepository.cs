using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AetherVault.Data;

/// <summary>
/// CRUD for the user's collection (my_collection table in the Collection DB). Uses the same DatabaseManager as CardRepository;
/// for queries that need card data (e.g. names, set) we use ICardRepository against the MTG DB. All writes go through CollectionConnection.
/// </summary>
public class CollectionRepository : ICollectionRepository
{
#pragma warning disable SA1401 // Fields should be private (internal helper DTO)
    private sealed class CollectionStatsAggregateRow
    {
        public int TotalCards { get; set; }
        public int UniqueCards { get; set; }
        public int CreatureCount { get; set; }
        public int SpellCount { get; set; }
        public int LandCount { get; set; }
        public int CommonCount { get; set; }
        public int UncommonCount { get; set; }
        public int RareCount { get; set; }
        public int MythicCount { get; set; }
        public int FoilCount { get; set; }
        public double TotalCMC { get; set; }
        public int NonLandCount { get; set; }
    }
#pragma warning restore SA1401

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

    /// <summary>Insert or update quantity for one card. Uses upsert so multiple adds accumulate quantity.</summary>
    public async Task AddCardAsync(string cardUUID, int quantity = 1, bool isFoil = false, bool isEtched = false)
    {
        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            await conn.ExecuteAsync(
                SQLQueries.CollectionUpsertAddCard,
                new { uuid = cardUUID, qty = quantity, isFoil = isFoil ? 1 : 0, isEtched = isEtched ? 1 : 0 },
                trans);
        });
    }

    public async Task AddCardsBulkAsync(IEnumerable<(string cardUUID, int quantity, bool isFoil, bool isEtched)> cards)
    {
        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            var parameters = new List<object>();
            foreach (var card in cards)
            {
                parameters.Add(new
                {
                    uuid = card.cardUUID,
                    qty = card.quantity,
                    isFoil = card.isFoil ? 1 : 0,
                    isEtched = card.isEtched ? 1 : 0
                });
            }

            if (parameters.Count == 0)
            {
                return;
            }

            await conn.ExecuteAsync(SQLQueries.CollectionUpsertAddCard, parameters, trans);
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

    public async Task ClearCollectionAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await _db.CollectionConnection.ExecuteAsync(SQLQueries.CollectionDeleteAll);
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

    public async Task<IReadOnlyList<(string Uuid, int Quantity, bool IsFoil, bool IsEtched)>> GetCollectionEntriesForPricingAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var rows = await _db.CollectionConnection.QueryAsync<CollectionRow>(SQLQueries.CollectionGetAll);
            return rows
                .Select(r => (Uuid: r.card_uuid, Quantity: r.quantity, IsFoil: r.is_foil.HasValue && r.is_foil.Value != 0, IsEtched: r.is_etched.HasValue && r.is_etched.Value != 0))
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CollectionStats> GetCollectionStatsAsync()
    {
        // Single aggregate query on existing MTG connection (collection already attached). No full load.
        try
        {
            var aggregated = await GetCollectionStatsFromDatabaseAsync();
            if (aggregated is not null)
                return aggregated;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Collection stats aggregate failed: {ex.Message}", LogLevel.Warning);
        }

        return new CollectionStats();
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

    /// <summary>
    /// Computes collection statistics using the existing MTG connection (collection already attached as 'col').
    /// Avoids opening a second connection and re-attaching; much faster for large collections.
    /// </summary>
    private async Task<CollectionStats?> GetCollectionStatsFromDatabaseAsync()
    {
        if (!_db.IsConnected)
            return null;

        await _db.ConnectionLock.WaitAsync();
        try
        {
            var row = await _db.MTGConnection.QueryFirstOrDefaultAsync<CollectionStatsAggregateRow>(SQLQueries.CollectionStatsAggregates);
            if (row is null)
                return new CollectionStats();

            var stats = new CollectionStats
            {
                TotalCards = row.TotalCards,
                UniqueCards = row.UniqueCards,
                CreatureCount = row.CreatureCount,
                SpellCount = row.SpellCount,
                LandCount = row.LandCount,
                CommonCount = row.CommonCount,
                UncommonCount = row.UncommonCount,
                RareCount = row.RareCount,
                MythicCount = row.MythicCount,
                FoilCount = row.FoilCount
            };

            if (row.NonLandCount > 0)
                stats.AvgCMC = row.TotalCMC / row.NonLandCount;

            return stats;
        }
        finally
        {
            _db.ConnectionLock.Release();
        }
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
