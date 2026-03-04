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

    public async Task<CollectionItem[]> GetCollectionAsync(string filterText = "", CollectionSortMode sortMode = CollectionSortMode.Manual)
    {
        var items = new List<CollectionItem>();

        var helper = _cardRepo.CreateSearchHelper();
        helper.SearchMyCollection();

        if (!string.IsNullOrEmpty(filterText))
        {
            helper.WhereNameContains(filterText);
        }

        switch (sortMode)
        {
            case CollectionSortMode.Name:
                helper.OrderBy("c.name ASC");
                break;
            case CollectionSortMode.CMC:
                helper.OrderBy("c.faceManaValue ASC, c.name ASC");
                break;
            case CollectionSortMode.Rarity:
                helper.OrderBy(@"CASE c.rarity
                                 WHEN 'mythic' THEN 4
                                 WHEN 'rare' THEN 3
                                 WHEN 'uncommon' THEN 2
                                 WHEN 'common' THEN 1
                                 ELSE 0 END DESC, c.name ASC");
                break;
            case CollectionSortMode.Color:
                helper.OrderBy("LENGTH(c.colorIdentity) ASC, c.colorIdentity ASC, c.name ASC");
                break;
            default:
                helper.OrderBy("mc.sort_order ASC, mc.date_added DESC");
                break;
        }

        // We use the advanced search which returns cards, but we actually need CollectionItems.
        // The SearchCardsAdvancedAsync method just maps cards and drops the collection info (quantity etc).
        // Let's implement our own execution here to get the CollectionItem data alongside the card.

        var (sql, parameters) = helper.Build();

        await _lock.WaitAsync();
        try
        {
            var dynamicParams = new DynamicParameters();
            foreach (var (name, value) in parameters)
            {
                dynamicParams.Add(name, value);
            }

            using var reader = await _db.MTGConnection.ExecuteReaderAsync(sql, dynamicParams) as System.Data.Common.DbDataReader
                ?? throw new InvalidOperationException("Failed to create DbDataReader.");

            var cardOrdinals = new CardMapper.CardOrdinals(reader);

            // Resolve ordinals for collection specific columns manually
            int qtyOrd = reader.GetOrdinal("quantity");
            int dateOrd = reader.GetOrdinal("date_added");
            int sortOrd = reader.GetOrdinal("sort_order");
            int isFoilOrd = reader.GetOrdinal("is_foil");
            int isEtchedOrd = reader.GetOrdinal("is_etched");

            while (await reader.ReadAsync())
            {
                var card = CardMapper.MapCard(reader, cardOrdinals);

                int qty = reader.GetInt32(qtyOrd);
                string dateStr = reader.IsDBNull(dateOrd) ? "" : reader.GetString(dateOrd);
                int sortOrder = reader.IsDBNull(sortOrd) ? 0 : reader.GetInt32(sortOrd);
                bool isFoil = !reader.IsDBNull(isFoilOrd) && reader.GetInt32(isFoilOrd) != 0;
                bool isEtched = !reader.IsDBNull(isEtchedOrd) && reader.GetInt32(isEtchedOrd) != 0;

                DateTime dateAdded = DateTime.TryParse(dateStr, out var d) ? d : DateTime.Now;

                items.Add(new CollectionItem
                {
                    CardUUID = card.UUID,
                    Quantity = qty,
                    IsFoil = isFoil,
                    IsEtched = isEtched,
                    DateAdded = dateAdded,
                    SortOrder = sortOrder,
                    Card = card
                });
            }
        }
        finally
        {
            _lock.Release();
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
