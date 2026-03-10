using AetherVault.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AetherVault.Services;

/// <summary>
/// SQLite database for card price data.
/// Reads from the normalized schema (uuid, source, provider, price_type, finish, currency, price).
/// Port of TCardPriceDatabase from CardPriceDatabase.pas.
/// </summary>
public class CardPriceDatabase : IDisposable
{
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _connection?.State == System.Data.ConnectionState.Open;

    /// <summary>
    /// Ensures the price database is created and connected.
    /// Schema migration is handled by CardPriceSQLiteSync on first sync.
    /// </summary>
    public async Task EnsureDatabaseAsync()
    {
        if (IsConnected) return;

        var dbPath = AppDataManager.GetPricesDatabasePath();
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ConnectionString;

        _connection = new SqliteConnection(connStr);
        await _connection.OpenAsync();

        await ExecuteAsync("PRAGMA journal_mode=WAL");
        await ExecuteAsync("PRAGMA busy_timeout=30000");
        await ExecuteAsync("PRAGMA temp_store=MEMORY");
        await ExecuteAsync("PRAGMA synchronous=OFF");

        // Create schema (no-ops if already correct; migration handled by sync)
        await ExecuteAsync(SQLQueries.CreatePricesTable);
        await ExecuteAsync(SQLQueries.CreatePriceHistoryTable);
        await ExecuteAsync(SQLQueries.CreatePricesIndex);
        await ExecuteAsync(SQLQueries.CreatePriceHistoryIndex);
        await ExecuteAsync(SQLQueries.CreatePricesUuidSourceIndex);
        await ExecuteAsync(SQLQueries.CreatePriceHistoryUuidSourceIndex);
    }

    /// <summary>
    /// Looks up all paper price data for a single card UUID, including history.
    /// </summary>
    public async Task<(bool found, CardPriceData prices)> GetCardPricesAsync(string uuid)
    {
        await _lock.WaitAsync();
        try
        {
            if (!IsConnected) return (false, CardPriceData.Empty);

            var currentRows = (await _connection!.QueryAsync<PriceRow>(
                SQLQueries.PricesGetByUuid, new { uuid })).ToList();

            if (currentRows.Count == 0) return (false, CardPriceData.Empty);

            var historyRows = (await _connection!.QueryAsync<HistoryRow>(
                SQLQueries.PricesGetHistoryByUuid, new { uuid })).ToList();

            return (true, new CardPriceData
            {
                UUID = uuid,
                Paper = BuildPaperPlatform(currentRows, historyRows),
                LastUpdated = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"GetCardPrices failed for {uuid}: {ex.Message}", LogLevel.Error);
            return (false, CardPriceData.Empty);
        }
        finally
        {
            _lock.Release();
        }
    }

    public class BulkPriceRow : PriceRow
    {
        public string uuid { get; set; } = "";
    }

    /// <summary>
    /// Looks up paper price data for multiple card UUIDs. History is omitted for performance.
    /// Chunks are queried in parallel using short-lived read-only connections (WAL mode allows concurrent readers).
    /// </summary>
    public async Task<Dictionary<string, CardPriceData>> GetCardPricesBulkAsync(IEnumerable<string> uuids)
    {
        // Quick guard — check connectivity and grab the DB path while holding the lock.
        string dbPath;
        await _lock.WaitAsync();
        try
        {
            if (!IsConnected) return [];
            dbPath = AppDataManager.GetPricesDatabasePath();
        }
        finally
        {
            _lock.Release();
        }

        try
        {
            var uuidList = uuids.Distinct().ToList();
            if (uuidList.Count == 0) return [];

            const int chunkSize = 500;
            var chunks = Enumerable
                .Range(0, (uuidList.Count + chunkSize - 1) / chunkSize)
                .Select(i => uuidList.Skip(i * chunkSize).Take(chunkSize).ToList())
                .ToList();

            // Run all chunks in parallel; each opens its own short-lived read-only connection.
            var chunkResults = await Task.WhenAll(chunks.Select(c => QueryChunkAsync(dbPath, c)));

            var result = new Dictionary<string, CardPriceData>(uuidList.Count);
            foreach (var chunkResult in chunkResults)
                foreach (var (rowUuid, data) in chunkResult)
                    result[rowUuid] = data;

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"GetCardPricesBulk failed: {ex.Message}", LogLevel.Error);
            return [];
        }
    }

    private static async Task<Dictionary<string, CardPriceData>> QueryChunkAsync(string dbPath, List<string> chunk)
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString;

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        var dynamicParams = new DynamicParameters();
        var paramNames = new List<string>(chunk.Count);
        for (int j = 0; j < chunk.Count; j++)
        {
            var p = $"@p{j}";
            dynamicParams.Add(p, chunk[j]);
            paramNames.Add(p);
        }

        var sql = string.Format(SQLQueries.PricesGetBulkByUuids, string.Join(",", paramNames));
        var rows = await conn.QueryAsync<BulkPriceRow>(sql, dynamicParams);

        var result = new Dictionary<string, CardPriceData>();
        foreach (var g in rows.GroupBy(r => r.uuid))
        {
            result[g.Key] = new CardPriceData
            {
                UUID = g.Key,
                Paper = BuildPaperPlatform(g.Cast<PriceRow>().ToList(), []),
                LastUpdated = DateTime.Now
            };
        }

        return result;
    }

    /// <summary>
    /// Returns true if the price database has any data.
    /// </summary>
    public async Task<bool> HasPriceDataAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!IsConnected) return false;
            var result = await _connection!.ExecuteScalarAsync<long>(SQLQueries.PricesCount);
            return result > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Closes the database connection so the sync process can take exclusive write access.
    /// Call <see cref="EnsureDatabaseAsync"/> to reconnect when sync is finished.
    /// </summary>
    public async Task CloseAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _connection?.Dispose();  // Dispose() calls Close() internally
            _connection = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Private Row Types ─────────────────────────────────────────────

    public class PriceRow
    {
        public string provider { get; set; } = "";
        public string price_type { get; set; } = "";
        public string finish { get; set; } = "";
        public string currency { get; set; } = "";
        public double price { get; set; }
    }

    public class HistoryRow
    {
        public string provider { get; set; } = "";
        public string price_type { get; set; } = "";
        public string finish { get; set; } = "";
        public string date { get; set; } = "";
        public double price { get; set; }
    }

    // ── Pivot Helpers ─────────────────────────────────────────────────

    private static PaperPlatform BuildPaperPlatform(List<PriceRow> rows, List<HistoryRow> history) => new()
    {
        TCGPlayer = BuildVendorPrices(rows, history, "tcgplayer"),
        Cardmarket = BuildVendorPrices(rows, history, "cardmarket"),
        CardKingdom = BuildVendorPrices(rows, history, "cardkingdom"),
        ManaPool = BuildVendorPrices(rows, history, "manapool")
    };

    private static VendorPrices BuildVendorPrices(List<PriceRow> rows, List<HistoryRow> history, string provider)
    {
        var vendorRows = rows.Where(r => r.provider == provider).ToList();
        if (vendorRows.Count == 0) return VendorPrices.Empty;

        double Get(string priceType, string finish) =>
            vendorRows.FirstOrDefault(r => r.price_type == priceType && r.finish == finish)?.price ?? 0;

        return new VendorPrices
        {
            RetailNormal = new PriceEntry(DateTime.Now, Get("retail", "normal")),
            RetailFoil = new PriceEntry(DateTime.Now, Get("retail", "foil")),
            RetailEtched = new PriceEntry(DateTime.Now, Get("retail", "etched")),
            BuylistNormal = new PriceEntry(DateTime.Now, Get("buylist", "normal")),
            BuylistEtched = new PriceEntry(DateTime.Now, Get("buylist", "etched")),
            Currency = ParseCurrency(vendorRows[0].currency),
            RetailNormalHistory = BuildHistoryList(history, provider, "retail", "normal"),
            RetailFoilHistory = BuildHistoryList(history, provider, "retail", "foil"),
            RetailEtchedHistory = BuildHistoryList(history, provider, "retail", "etched"),
            BuylistNormalHistory = BuildHistoryList(history, provider, "buylist", "normal"),
            BuylistEtchedHistory = BuildHistoryList(history, provider, "buylist", "etched"),
        };
    }

    private static List<PriceEntry> BuildHistoryList(
        List<HistoryRow> history, string provider, string priceType, string finish) =>
        [.. history
            .Where(h => h.provider == provider && h.price_type == priceType && h.finish == finish)
            .Select(h => new PriceEntry(PriceDateParser.ParseISO8601Date(h.date), h.price))];

    private static PriceCurrency ParseCurrency(string s) =>
        s.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? PriceCurrency.EUR : PriceCurrency.USD;

    private async Task ExecuteAsync(string sql)
    {
        await _connection!.ExecuteAsync(sql);
    }
}
