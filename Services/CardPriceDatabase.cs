using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Data;

namespace MTGFetchMAUI.Services;

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
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

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

            var currentRows = new List<PriceRow>();
            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = SQLQueries.PricesGetByUuid;
                cmd.Parameters.AddWithValue("@uuid", uuid);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    currentRows.Add(ReadPriceRow(reader));
            }

            if (currentRows.Count == 0) return (false, CardPriceData.Empty);

            var historyRows = new List<HistoryRow>();
            using (var histCmd = _connection!.CreateCommand())
            {
                histCmd.CommandText = SQLQueries.PricesGetHistoryByUuid;
                histCmd.Parameters.AddWithValue("@uuid", uuid);
                using var reader = await histCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    historyRows.Add(ReadHistoryRow(reader));
            }

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

    /// <summary>
    /// Looks up paper price data for multiple card UUIDs. History is omitted for performance.
    /// </summary>
    public async Task<Dictionary<string, CardPriceData>> GetCardPricesBulkAsync(IEnumerable<string> uuids)
    {
        await _lock.WaitAsync();
        try
        {
            if (!IsConnected) return [];

            var result = new Dictionary<string, CardPriceData>();
            var uuidList = uuids.Distinct().ToList();
            if (uuidList.Count == 0) return result;

            const int chunkSize = 500;
            for (int i = 0; i < uuidList.Count; i += chunkSize)
            {
                var chunk = uuidList.Skip(i).Take(chunkSize).ToList();
                using var cmd = _connection!.CreateCommand();

                var paramNames = new List<string>(chunk.Count);
                for (int j = 0; j < chunk.Count; j++)
                {
                    var p = $"@p{j}";
                    cmd.Parameters.AddWithValue(p, chunk[j]);
                    paramNames.Add(p);
                }

                cmd.CommandText = string.Format(SQLQueries.PricesGetBulkByUuids, string.Join(",", paramNames));

                // Collect rows grouped by UUID, then pivot each group into CardPriceData
                var rowsByUuid = new Dictionary<string, List<PriceRow>>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var rowUuid = reader.GetString(reader.GetOrdinal("uuid"));
                    if (!rowsByUuid.TryGetValue(rowUuid, out var list))
                    {
                        list = [];
                        rowsByUuid[rowUuid] = list;
                    }
                    list.Add(ReadPriceRow(reader));
                }

                foreach (var (rowUuid, rows) in rowsByUuid)
                {
                    result[rowUuid] = new CardPriceData
                    {
                        UUID = rowUuid,
                        Paper = BuildPaperPlatform(rows, []),
                        LastUpdated = DateTime.Now
                    };
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"GetCardPricesBulk failed: {ex.Message}", LogLevel.Error);
            return [];
        }
        finally
        {
            _lock.Release();
        }
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
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = SQLQueries.PricesCount;
            var result = await cmd.ExecuteScalarAsync();
            return result is long count && count > 0;
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

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Private Row Types ─────────────────────────────────────────────

    private record PriceRow(string Provider, string PriceType, string Finish, string Currency, double Price);
    private record HistoryRow(string Provider, string PriceType, string Finish, string Date, double Price);

    // ── Read Helpers ──────────────────────────────────────────────────

    private static PriceRow ReadPriceRow(SqliteDataReader r) => new(
        r.GetString(r.GetOrdinal("provider")),
        r.GetString(r.GetOrdinal("price_type")),
        r.GetString(r.GetOrdinal("finish")),
        r.GetString(r.GetOrdinal("currency")),
        r.GetDouble(r.GetOrdinal("price")));

    private static HistoryRow ReadHistoryRow(SqliteDataReader r) => new(
        r.GetString(r.GetOrdinal("provider")),
        r.GetString(r.GetOrdinal("price_type")),
        r.GetString(r.GetOrdinal("finish")),
        r.GetString(r.GetOrdinal("date")),
        r.GetDouble(r.GetOrdinal("price")));

    // ── Pivot Helpers ─────────────────────────────────────────────────

    private static PaperPlatform BuildPaperPlatform(List<PriceRow> rows, List<HistoryRow> history) => new()
    {
        TCGPlayer  = BuildVendorPrices(rows, history, "tcgplayer"),
        Cardmarket = BuildVendorPrices(rows, history, "cardmarket"),
        CardKingdom = BuildVendorPrices(rows, history, "cardkingdom"),
        ManaPool   = BuildVendorPrices(rows, history, "manapool")
    };

    private static VendorPrices BuildVendorPrices(List<PriceRow> rows, List<HistoryRow> history, string provider)
    {
        var vendorRows = rows.Where(r => r.Provider == provider).ToList();
        if (vendorRows.Count == 0) return VendorPrices.Empty;

        double Get(string priceType, string finish) =>
            vendorRows.FirstOrDefault(r => r.PriceType == priceType && r.Finish == finish)?.Price ?? 0;

        return new VendorPrices
        {
            RetailNormal  = new PriceEntry(DateTime.Now, Get("retail", "normal")),
            RetailFoil    = new PriceEntry(DateTime.Now, Get("retail", "foil")),
            RetailEtched  = new PriceEntry(DateTime.Now, Get("retail", "etched")),
            BuylistNormal = new PriceEntry(DateTime.Now, Get("buylist", "normal")),
            BuylistEtched = new PriceEntry(DateTime.Now, Get("buylist", "etched")),
            Currency      = ParseCurrency(vendorRows[0].Currency),
            RetailNormalHistory  = BuildHistoryList(history, provider, "retail",  "normal"),
            RetailFoilHistory    = BuildHistoryList(history, provider, "retail",  "foil"),
            RetailEtchedHistory  = BuildHistoryList(history, provider, "retail",  "etched"),
            BuylistNormalHistory = BuildHistoryList(history, provider, "buylist", "normal"),
            BuylistEtchedHistory = BuildHistoryList(history, provider, "buylist", "etched"),
        };
    }

    private static List<PriceEntry> BuildHistoryList(
        List<HistoryRow> history, string provider, string priceType, string finish) =>
        history
            .Where(h => h.Provider == provider && h.PriceType == priceType && h.Finish == finish)
            .Select(h => new PriceEntry(PriceDateParser.ParseISO8601Date(h.Date), h.Price))
            .ToList();

    private static PriceCurrency ParseCurrency(string s) =>
        s.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? PriceCurrency.EUR : PriceCurrency.USD;

    private async Task ExecuteAsync(string sql)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
