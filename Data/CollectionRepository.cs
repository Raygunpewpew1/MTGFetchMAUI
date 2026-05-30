using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;

namespace AetherVault.Data;

/// <summary>
/// CRUD for the user's collection (my_collection table in the Collection DB). Uses the same DatabaseManager as CardRepository;
/// list loads use a slim JOIN on the MTG connection (with <c>col</c> attached). All writes go through CollectionConnection.
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

    /// <summary>Dapper row for <see cref="SqlQueries.CollectionGridLoad"/>.</summary>
    private sealed class CollectionGridJoinRow
    {
        public string CardUuid { get; set; } = "";
        public int Quantity { get; set; }
        public string DateAdded { get; set; } = "";
        public int? SortOrder { get; set; }
        public int? IsFoil { get; set; }
        public int? IsEtched { get; set; }
        public string Uuid { get; set; } = "";
        public string Name { get; set; } = "";
        public string CardType { get; set; } = "";
        public string ManaCost { get; set; } = "";
        public double? ManaValue { get; set; }
        public double? FaceManaValue { get; set; }
        public string ColorIdentity { get; set; } = "";
        public string Rarity { get; set; } = "";
        public string SetCode { get; set; } = "";
        public string Number { get; set; } = "";
        public long? IsOnlineOnly { get; set; }
        public string ScryfallId { get; set; } = "";
        public double? ReferencePriceUsd { get; set; }
        public string ReferenceCapturedAt { get; set; } = "";
    }

    private sealed class CollectionFinishRow
    {
        public int? IsFoil { get; set; }
        public int? IsEtched { get; set; }
    }

    private readonly DatabaseManager _db;

    public CollectionRepository(DatabaseManager databaseManager)
    {
        _db = databaseManager;
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

    public async Task RemoveCardAsync(string cardUuid) =>
        await WithCollectionConnectionAsync(conn =>
            conn.ExecuteAsync(SqlQueries.CollectionDeleteCard, new { uuid = cardUuid }));

    public async Task ClearCollectionAsync() =>
        await WithCollectionConnectionAsync(conn =>
            conn.ExecuteAsync(SqlQueries.CollectionDeleteAll));

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
        await _db.ConnectionLock.WaitAsync();
        try
        {
            var rows = (await _db.MtgConnection.QueryAsync<CollectionGridJoinRow>(SqlQueries.CollectionGridLoad)).AsList();
            if (rows.Count == 0)
                return [];

            var items = new CollectionItem[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                DateTime? refCaptured = null;
                if (!string.IsNullOrEmpty(row.ReferenceCapturedAt)
                    && DateTime.TryParse(
                        row.ReferenceCapturedAt,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var rc))
                {
                    refCaptured = rc;
                }

                items[i] = new CollectionItem
                {
                    CardUuid = row.CardUuid,
                    Quantity = row.Quantity,
                    IsFoil = row.IsFoil.HasValue && row.IsFoil.Value != 0,
                    IsEtched = row.IsEtched.HasValue && row.IsEtched.Value != 0,
                    ReferencePriceUsd = row.ReferencePriceUsd is > 0 ? row.ReferencePriceUsd : null,
                    ReferenceCapturedAt = refCaptured,
                    DateAdded = DateTime.TryParse(row.DateAdded, out var d) ? d : DateTime.Now,
                    SortOrder = row.SortOrder ?? 0,
                    Card = ToSlimCollectionCard(row),
                };
            }

            return items;
        }
        finally
        {
            _db.ConnectionLock.Release();
        }
    }

    /// <summary>Maps a slim SQL row to <see cref="Card"/> with only fields needed for collection grid, stats helper, and CSV export.</summary>
    private static Card ToSlimCollectionCard(CollectionGridJoinRow row)
    {
        var uuid = row.Uuid ?? "";
        var card = new Card
        {
            Uuid = uuid,
            Name = row.Name ?? "",
            CardType = row.CardType ?? "",
            ManaCost = row.ManaCost ?? "",
            Cmc = row.ManaValue ?? 0,
            FaceManaValue = row.FaceManaValue ?? 0,
            ColorIdentity = row.ColorIdentity ?? "",
            Rarity = EnumExtensions.ParseCardRarity(row.Rarity),
            SetCode = row.SetCode ?? "",
            Number = row.Number ?? "",
            IsOnlineOnly = row.IsOnlineOnly.HasValue && row.IsOnlineOnly.Value != 0,
            ScryfallId = row.ScryfallId ?? "",
        };
        card.ImageUrl = string.IsNullOrEmpty(uuid)
            ? ""
            : ScryfallCdn.GetImageUrl(uuid, ScryfallSize.Small, ScryfallFace.Front);
        return card;
    }

    public async Task<IReadOnlyList<(string Uuid, int Quantity, bool IsFoil, bool IsEtched)>> GetPricingEntriesAsync() =>
        await WithCollectionConnectionAsync(async conn =>
        {
            var rows = await conn.QueryAsync<CollectionRow>(SqlQueries.CollectionGetForPricing);
            return rows
                .Select(r => (Uuid: r.CardUuid, Quantity: r.Quantity, IsFoil: r.IsFoil.HasValue && r.IsFoil.Value != 0, IsEtched: r.IsEtched.HasValue && r.IsEtched.Value != 0))
                .ToList();
        });

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

    public async Task<bool> IsInCollectionAsync(string cardUuid) =>
        await WithCollectionConnectionAsync(async conn =>
        {
            var result = await conn.QueryFirstOrDefaultAsync<int?>(
                SqlQueries.CollectionCheckExists, new { uuid = cardUuid });
            return result.HasValue;
        });

    public async Task<int> GetQuantityAsync(string cardUuid) =>
        await WithCollectionConnectionAsync(conn => GetQuantityInternalAsync(conn, cardUuid));

    public async Task<Dictionary<string, int>> GetQuantitiesAsync(IEnumerable<string> cardUuids)
    {
        var distinct = cardUuids.Where(static u => !string.IsNullOrEmpty(u)).Distinct(StringComparer.Ordinal).ToArray();
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        if (distinct.Length == 0)
            return map;

        return await WithCollectionConnectionAsync(async conn =>
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
            var rows = await conn.QueryAsync<CollectionQtyRow>(sql.ToString(), dp);
            foreach (var row in rows)
            {
                if (!string.IsNullOrEmpty(row.CardUuid))
                    map[row.CardUuid] = row.Quantity;
            }

            return map;
        });
    }

    private sealed class CollectionQtyRow
    {
        public string CardUuid { get; set; } = "";
        public int Quantity { get; set; }
    }

    public async Task ReorderAsync(IList<string> orderedUuids)
    {
        if (orderedUuids == null || orderedUuids.Count == 0)
            return;

        // One UPDATE per chunk (CASE … END) instead of N round trips; stay under SQLite max_variable_number.
        const int chunk = 450;

        await WithCollectionTransactionAsync(async (conn, trans) =>
        {
            for (int offset = 0; offset < orderedUuids.Count; offset += chunk)
            {
                var take = Math.Min(chunk, orderedUuids.Count - offset);
                var sb = new StringBuilder(128 + take * 64);
                sb.Append("UPDATE my_collection SET sort_order = CASE card_uuid");
                var dp = new DynamicParameters();
                var inList = new List<string>(take);

                for (int i = 0; i < take; i++)
                {
                    var sortOrder = offset + i;
                    var uuid = orderedUuids[sortOrder];
                    var p = "r" + sortOrder.ToString(CultureInfo.InvariantCulture);
                    sb.Append(" WHEN @").Append(p).Append(" THEN ").Append(sortOrder.ToString(CultureInfo.InvariantCulture));
                    dp.Add(p, uuid);
                    inList.Add('@' + p);
                }

                sb.Append(" END WHERE card_uuid IN (");
                sb.Append(string.Join(",", inList));
                sb.Append(')');

                await conn.ExecuteAsync(sb.ToString(), dp, trans);
            }
        });
    }

    public async Task<(bool IsFoil, bool IsEtched)?> TryGetFinishFlagsAsync(string cardUuid)
    {
        if (string.IsNullOrEmpty(cardUuid))
            return null;

        return await WithCollectionConnectionAsync<(bool IsFoil, bool IsEtched)?>(async conn =>
        {
            var row = await conn.QueryFirstOrDefaultAsync<CollectionFinishRow>(
                SqlQueries.CollectionGetFinishFlags,
                new { uuid = cardUuid });
            if (row == null)
                return null;

            return (row.IsFoil.HasValue && row.IsFoil.Value != 0, row.IsEtched.HasValue && row.IsEtched.Value != 0);
        });
    }

    public async Task TrySetReferenceBaselineIfMissingAsync(string cardUuid, double unitPriceUsd, DateTime capturedUtc)
    {
        if (string.IsNullOrEmpty(cardUuid) || unitPriceUsd <= 0)
            return;

        await WithCollectionConnectionAsync(conn =>
            conn.ExecuteAsync(
                SqlQueries.CollectionTrySetReferenceBaselineIfMissing,
                new
                {
                    uuid = cardUuid,
                    price = unitPriceUsd,
                    captured = capturedUtc.ToString("o", CultureInfo.InvariantCulture),
                }));
    }

    public async Task SetReferenceBaselineAsync(string cardUuid, double unitPriceUsd, DateTime capturedUtc)
    {
        if (string.IsNullOrEmpty(cardUuid) || unitPriceUsd <= 0)
            return;

        await WithCollectionConnectionAsync(conn =>
            conn.ExecuteAsync(
                SqlQueries.CollectionSetReferenceBaseline,
                new
                {
                    uuid = cardUuid,
                    price = unitPriceUsd,
                    captured = capturedUtc.ToString("o", CultureInfo.InvariantCulture),
                }));
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
        await WithCollectionConnectionAsync(async conn =>
        {
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
        });
    }

    private async Task WithCollectionConnectionAsync(Func<SqliteConnection, Task> action)
    {
        await _db.ConnectionLock.WaitAsync();
        try
        {
            if (!_db.IsConnected)
                throw new InvalidOperationException("Database not connected.");
            await action(_db.CollectionConnection);
        }
        finally
        {
            _db.ConnectionLock.Release();
        }
    }

    private async Task<T> WithCollectionConnectionAsync<T>(Func<SqliteConnection, Task<T>> action)
    {
        await _db.ConnectionLock.WaitAsync();
        try
        {
            if (!_db.IsConnected)
                throw new InvalidOperationException("Database not connected.");
            return await action(_db.CollectionConnection);
        }
        finally
        {
            _db.ConnectionLock.Release();
        }
    }
}
