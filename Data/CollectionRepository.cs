using System.Text;
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
        public double TotalCmc { get; set; }
        public int NonLandCount { get; set; }
    }
#pragma warning restore SA1401

    private class CollectionRow
    {
        public string CardUuid { get; set; } = "";
        public int Quantity { get; set; }
        public string DateAdded { get; set; } = "";
        public int? SortOrder { get; set; }
        public int? IsFoil { get; set; }
        public int? IsEtched { get; set; }
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
    public async Task AddCardAsync(string cardUuid, int quantity = 1, bool isFoil = false, bool isEtched = false)
    {
        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            await conn.ExecuteAsync(
                SqlQueries.CollectionUpsertAddCard,
                new { uuid = cardUuid, qty = quantity, isFoil = isFoil ? 1 : 0, isEtched = isEtched ? 1 : 0 },
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

            await conn.ExecuteAsync(SqlQueries.CollectionUpsertAddCard, parameters, trans);
        });
    }

    public async Task RemoveCardAsync(string cardUuid)
    {
        await _lock.WaitAsync();
        try
        {
            await _db.CollectionConnection.ExecuteAsync(SqlQueries.CollectionDeleteCard, new { uuid = cardUuid });
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
            await _db.CollectionConnection.ExecuteAsync(SqlQueries.CollectionDeleteAll);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateQuantityAsync(string cardUuid, int quantity, bool isFoil = false, bool isEtched = false)
    {
        if (quantity <= 0)
        {
            await RemoveCardAsync(cardUuid);
            return;
        }

        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            var currentQty = await GetQuantityInternalAsync(conn, cardUuid, trans);

            if (currentQty > 0)
            {
                await conn.ExecuteAsync(
                    SqlQueries.CollectionUpdateQuantity,
                    new { qty = quantity, isFoil = isFoil ? 1 : 0, isEtched = isEtched ? 1 : 0, uuid = cardUuid },
                    trans);
            }
            else
            {
                await conn.ExecuteAsync(
                    SqlQueries.CollectionInsertCard,
                    new { uuid = cardUuid, qty = quantity, isFoil = isFoil ? 1 : 0, isEtched = isEtched ? 1 : 0 },
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
            entries = await _db.CollectionConnection.QueryAsync<CollectionRow>(SqlQueries.CollectionGetAll);
        }
        finally
        {
            _lock.Release();
        }

        var entryList = entries.ToList();
        var uuids = entryList.Select(e => e.CardUuid).ToArray();

        // Batch-load card details from MTG database
        if (uuids.Length > 0)
        {
            var cardCache = await _cardRepo.GetCardsByUuiDsAsync(uuids);

            foreach (var row in entryList)
            {
                if (cardCache.TryGetValue(row.CardUuid, out var card))
                {
                    DateTime dateAdded = DateTime.TryParse(row.DateAdded, out var d) ? d : DateTime.Now;
                    items.Add(new CollectionItem
                    {
                        CardUuid = row.CardUuid,
                        Quantity = row.Quantity,
                        IsFoil = row.IsFoil.HasValue && row.IsFoil.Value != 0,
                        IsEtched = row.IsEtched.HasValue && row.IsEtched.Value != 0,
                        DateAdded = dateAdded,
                        SortOrder = row.SortOrder ?? 0,
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
            var rows = await _db.CollectionConnection.QueryAsync<CollectionRow>(SqlQueries.CollectionGetForPricing);
            return rows
                .Select(r => (Uuid: r.CardUuid, Quantity: r.Quantity, IsFoil: r.IsFoil.HasValue && r.IsFoil.Value != 0, IsEtched: r.IsEtched.HasValue && r.IsEtched.Value != 0))
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
        double totalCmc = 0;
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
                totalCmc += item.Card.Cmc * item.Quantity;
                nonLandCount += item.Quantity;
            }

            if (item.IsFoil || item.IsEtched)
                stats.FoilCount += item.Quantity;
        }

        if (nonLandCount > 0)
            stats.AvgCmc = totalCmc / nonLandCount;

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
            var row = await _db.MtgConnection.QueryFirstOrDefaultAsync<CollectionStatsAggregateRow>(SqlQueries.CollectionStatsAggregates);
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
                stats.AvgCmc = row.TotalCmc / row.NonLandCount;

            return stats;
        }
        finally
        {
            _db.ConnectionLock.Release();
        }
    }

    public async Task<bool> IsInCollectionAsync(string cardUuid)
    {
        await _lock.WaitAsync();
        try
        {
            var result = await _db.CollectionConnection.QueryFirstOrDefaultAsync<int?>(
                SqlQueries.CollectionCheckExists, new { uuid = cardUuid });
            return result.HasValue;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetQuantityAsync(string cardUuid)
    {
        await _lock.WaitAsync();
        try
        {
            return await GetQuantityInternalAsync(_db.CollectionConnection, cardUuid);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Dictionary<string, int>> GetQuantitiesByUuidsAsync(IEnumerable<string> cardUuids)
    {
        var distinct = cardUuids.Where(static u => !string.IsNullOrEmpty(u)).Distinct(StringComparer.Ordinal).ToArray();
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        if (distinct.Length == 0)
            return map;

        await _lock.WaitAsync();
        try
        {
            // Microsoft.Data.Sqlite does not accept Dapper's "IN @uuids" expansion; build explicit placeholders.
            var sql = new StringBuilder("SELECT card_uuid AS CardUuid, quantity AS Quantity FROM my_collection WHERE card_uuid IN (");
            var dp = new DynamicParameters();
            for (int i = 0; i < distinct.Length; i++)
            {
                if (i > 0) sql.Append(',');
                string p = $"p{i}";
                sql.Append('@').Append(p);
                dp.Add(p, distinct[i]);
            }

            sql.Append(')');
            var rows = await _db.CollectionConnection.QueryAsync<CollectionQtyRow>(sql.ToString(), dp);
            foreach (var row in rows)
            {
                if (!string.IsNullOrEmpty(row.CardUuid))
                    map[row.CardUuid] = row.Quantity;
            }

            return map;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed class CollectionQtyRow
    {
        public string CardUuid { get; set; } = "";
        public int Quantity { get; set; }
    }

    public async Task ReorderAsync(IList<string> orderedUuids)
    {
        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            for (int i = 0; i < orderedUuids.Count; i++)
            {
                await conn.ExecuteAsync(
                    SqlQueries.CollectionReorderItem,
                    new { sortOrder = i, uuid = orderedUuids[i] },
                    trans);
            }
        });
    }

    // ── Private helpers ─────────────────────────────────────────────

    private Task<int> GetQuantityInternalAsync(SqliteConnection conn, string cardUuid, SqliteTransaction? trans = null)
    {
        return conn.QueryFirstOrDefaultAsync<int>(
            SqlQueries.CollectionGetQuantity,
            new { uuid = cardUuid },
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
