using AetherVault.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AetherVault.Services;

/// <summary>
/// SQLite database for current card price data.
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
        await ExecuteAsync(SqlQueries.CreatePricesTable);
        await ExecuteAsync(SqlQueries.CreatePricesIndex);
        await ExecuteAsync(SqlQueries.CreatePricesUuidSourceIndex);
    }

    /// <summary>
    /// Looks up all paper price data for a single card UUID.
    /// </summary>
    public async Task<(bool found, CardPriceData prices)> GetCardPricesAsync(string uuid)
    {
        await _lock.WaitAsync();
        try
        {
            if (!IsConnected) return (false, CardPriceData.Empty);

            var currentRows = (await _connection!.QueryAsync<PriceRow>(
                SqlQueries.PricesGetByUuid, new { uuid })).ToList();

            if (currentRows.Count == 0) return (false, CardPriceData.Empty);

            return (true, new CardPriceData
            {
                Uuid = uuid,
                Paper = BuildPaperPlatform(currentRows),
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
        public string Uuid { get; set; } = "";
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

    /// <summary>
    /// Computes collection total value directly in SQLite using provider priority and finish fallback.
    /// Expects the collection DB to be attached as alias "col" (attached on demand if needed).
    /// </summary>
    public async Task<double> GetCollectionTotalValueAsync(IReadOnlyList<PriceVendor> vendorPriority)
    {
        if (vendorPriority.Count == 0)
            return 0;

        // Ensure we always have 4 providers so SQL can bind @v1..@v4.
        var providers = new List<string>(4);
        foreach (var vendor in vendorPriority)
        {
            var provider = ToProviderName(vendor);
            if (!providers.Contains(provider, StringComparer.Ordinal))
                providers.Add(provider);
        }

        var defaults = new[] { "tcgplayer", "cardmarket", "cardkingdom", "manapool" };
        foreach (var provider in defaults)
        {
            if (providers.Count >= 4) break;
            if (!providers.Contains(provider, StringComparer.Ordinal))
                providers.Add(provider);
        }

        while (providers.Count < 4)
            providers.Add(defaults[providers.Count]);

        await _lock.WaitAsync();
        try
        {
            if (!IsConnected) return 0;

            await EnsureCollectionAttachedAsync(_connection!);

            var total = await _connection!.ExecuteScalarAsync<double>(
                SqlQueries.PricesGetCollectionTotalValue,
                new
                {
                    v1 = providers[0],
                    v2 = providers[1],
                    v3 = providers[2],
                    v4 = providers[3]
                });

            return total;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"GetCollectionTotalValue failed: {ex.Message}", LogLevel.Warning);
            return 0;
        }
        finally
        {
            _lock.Release();
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

        var sql = string.Format(SqlQueries.PricesGetBulkByUuids, string.Join(",", paramNames));
        var rows = await conn.QueryAsync<BulkPriceRow>(sql, dynamicParams);

        var result = new Dictionary<string, CardPriceData>();
        foreach (var g in rows.GroupBy(r => r.Uuid))
        {
            result[g.Key] = new CardPriceData
            {
                Uuid = g.Key,
                Paper = BuildPaperPlatform(g.Cast<PriceRow>().ToList()),
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
            var result = await _connection!.ExecuteScalarAsync<long>(SqlQueries.PricesCount);
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
        public string Provider { get; set; } = "";
        public string PriceType { get; set; } = "";
        public string Finish { get; set; } = "";
        public string Currency { get; set; } = "";
        public double Price { get; set; }
    }

    // ── Pivot Helpers ─────────────────────────────────────────────────

    private static PaperPlatform BuildPaperPlatform(List<PriceRow> rows) => new()
    {
        TcgPlayer = BuildVendorPrices(rows, "tcgplayer"),
        Cardmarket = BuildVendorPrices(rows, "cardmarket"),
        CardKingdom = BuildVendorPrices(rows, "cardkingdom"),
        ManaPool = BuildVendorPrices(rows, "manapool")
    };

    private static VendorPrices BuildVendorPrices(List<PriceRow> rows, string provider)
    {
        var vendorRows = rows.Where(r => r.Provider == provider).ToList();
        if (vendorRows.Count == 0) return VendorPrices.Empty;

        double Get(string priceType, string finish) =>
            vendorRows.FirstOrDefault(r => r.PriceType == priceType && r.Finish == finish)?.Price ?? 0;

        return new VendorPrices
        {
            RetailNormal = new PriceEntry(DateTime.Now, Get("retail", "normal")),
            RetailFoil = new PriceEntry(DateTime.Now, Get("retail", "foil")),
            RetailEtched = new PriceEntry(DateTime.Now, Get("retail", "etched")),
            BuylistNormal = new PriceEntry(DateTime.Now, Get("buylist", "normal")),
            BuylistEtched = new PriceEntry(DateTime.Now, Get("buylist", "etched")),
            Currency = ParseCurrency(vendorRows[0].Currency)
        };
    }

    private static PriceCurrency ParseCurrency(string s) =>
        s.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? PriceCurrency.Eur : PriceCurrency.Usd;

    private static string ToProviderName(PriceVendor vendor) => vendor switch
    {
        PriceVendor.TcgPlayer => "tcgplayer",
        PriceVendor.Cardmarket => "cardmarket",
        PriceVendor.CardKingdom => "cardkingdom",
        PriceVendor.ManaPool => "manapool",
        _ => "tcgplayer"
    };

    private async Task ExecuteAsync(string sql)
    {
        await _connection!.ExecuteAsync(sql);
    }

    private static async Task EnsureCollectionAttachedAsync(SqliteConnection conn)
    {
        var attached = await conn.ExecuteScalarAsync<string?>(
            "SELECT name FROM pragma_database_list WHERE name = 'col' LIMIT 1");

        if (!string.Equals(attached, "col", StringComparison.Ordinal))
        {
            var collectionDbPath = AppDataManager.GetCollectionDatabasePath().Replace("'", "''");
            await conn.ExecuteAsync($"ATTACH DATABASE '{collectionDbPath}' AS col");
        }
    }
}
