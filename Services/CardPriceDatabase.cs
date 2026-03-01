using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Data;

namespace MTGFetchMAUI.Services;

/// <summary>
/// SQLite database for card price data.
/// Port of TCardPriceDatabase from CardPriceDatabase.pas.
/// </summary>
public class CardPriceDatabase : IDisposable
{
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// The underlying SQLite connection. Null if not connected.
    /// </summary>
    public SqliteConnection? Connection => _connection;

    public bool IsConnected => _connection?.State == System.Data.ConnectionState.Open;

    /// <summary>
    /// Ensures the price database is created and connected.
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

        // Configure for performance
        await ExecuteAsync("PRAGMA journal_mode=WAL");
        await ExecuteAsync("PRAGMA busy_timeout=30000");
        await ExecuteAsync("PRAGMA temp_store=MEMORY");
        await ExecuteAsync("PRAGMA synchronous=OFF");

        // Create schema
        await ExecuteAsync(SQLQueries.CreatePricesTable);
        await ExecuteAsync(SQLQueries.CreatePriceHistoryTable);
        await ExecuteAsync(SQLQueries.CreatePricesIndex);
        await ExecuteAsync(SQLQueries.CreatePriceHistoryIndex);
    }

    /// <summary>
    /// Looks up price data by card UUID.
    /// </summary>
    public async Task<(bool found, CardPriceData prices)> GetCardPricesAsync(string uuid)
    {
        await _lock.WaitAsync();
        try
        {
            if (!IsConnected)
                return (false, CardPriceData.Empty);

            // 1. Get current prices
            CardPriceData? prices = null;
            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = SQLQueries.PricesGetByUuid;
                cmd.Parameters.AddWithValue("@uuid", uuid);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    prices = PopulatePriceData(reader);
                }
            }

            if (prices == null)
                return (false, CardPriceData.Empty);

            // 2. Get history
            using (var historyCmd = _connection!.CreateCommand())
            {
                historyCmd.CommandText = SQLQueries.PricesGetHistoryByUuid;
                historyCmd.Parameters.AddWithValue("@uuid", uuid);
                using var reader = await historyCmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var dateInt = reader.GetInt32(reader.GetOrdinal("price_date"));
                    var vendor = reader.GetString(reader.GetOrdinal("vendor"));
                    var type = reader.GetString(reader.GetOrdinal("price_type"));
                    var val = reader.GetDouble(reader.GetOrdinal("price_value"));

                    var dt = DateTime.ParseExact(dateInt.ToString(), "yyyyMMdd", null);
                    var entry = new PriceEntry(dt, val);

                    // Add to appropriate list
                    AddToHistory(prices, vendor, type, entry);
                }
            }

            return (true, prices);
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
    /// Looks up price data for multiple cards by UUID.
    /// </summary>
    public async Task<Dictionary<string, CardPriceData>> GetCardPricesBulkAsync(IEnumerable<string> uuids)
    {
        await _lock.WaitAsync();
        try
        {
            if (!IsConnected)
                return [];

            var result = new Dictionary<string, CardPriceData>();
            var uuidList = uuids.Distinct().ToList();
            if (uuidList.Count == 0) return result;

            const int chunkSize = 500;
            for (int i = 0; i < uuidList.Count; i += chunkSize)
            {
                var chunk = uuidList.Skip(i).Take(chunkSize).ToList();
                using var cmd = _connection!.CreateCommand();

                var paramsList = new List<string>(chunk.Count);
                for (int j = 0; j < chunk.Count; j++)
                {
                    var pName = $"@p{j}";
                    cmd.Parameters.AddWithValue(pName, chunk[j]);
                    paramsList.Add(pName);
                }

                cmd.CommandText = string.Format(SQLQueries.PricesGetBulkByUuids, string.Join(",", paramsList));

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var data = PopulatePriceData(reader);
                    if (!string.IsNullOrEmpty(data.UUID))
                    {
                        result[data.UUID] = data;
                    }
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
    /// Deletes all rows from price_history and runs VACUUM to reclaim disk space.
    /// Call once on startup to clean up history accumulated by older app versions.
    /// </summary>
    public async Task PruneAllHistoryAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!IsConnected) return;

            // Check if the table exists before trying to delete
            using (var check = _connection!.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='price_history'";
                var result = await check.ExecuteScalarAsync();
                if (result is not long count || count == 0) return;
            }

            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM price_history";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var vacuum = _connection!.CreateCommand())
            {
                vacuum.CommandText = "VACUUM";
                await vacuum.ExecuteNonQueryAsync();
            }

            Logger.LogStuff("PruneAllHistory: price_history cleared and VACUUM complete.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"PruneAllHistory failed: {ex.Message}", LogLevel.Warning);
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

    // ── Private Helpers ──────────────────────────────────────────────

    private async Task ExecuteAsync(string sql)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static CardPriceData PopulatePriceData(SqliteDataReader reader)
    {
        return new CardPriceData
        {
            UUID = SafeStr(reader, "card_uuid"),
            Paper = new PaperPlatform
            {
                TCGPlayer = PopulateVendor(reader, "tcg"),
                Cardmarket = PopulateVendor(reader, "cm"),
                CardKingdom = PopulateVendor(reader, "ck"),
                ManaPool = PopulateVendor(reader, "mp")
            },
            LastUpdated = DateTime.Now
        };
    }

    private static VendorPrices PopulateVendor(SqliteDataReader reader, string prefix)
    {
        return new VendorPrices
        {
            RetailNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, $"{prefix}_retail_normal")),
            RetailFoil = new PriceEntry(DateTime.Now, SafeDouble(reader, $"{prefix}_retail_foil")),
            BuylistNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, $"{prefix}_buylist_normal")),
            Currency = ParseCurrency(SafeStr(reader, $"{prefix}_currency")),
            // History populated separately
            RetailNormalHistory = [],
            RetailFoilHistory = [],
            BuylistNormalHistory = []
        };
    }

    private static void AddToHistory(CardPriceData data, string vendor, string type, PriceEntry entry)
    {
        VendorPrices? v = vendor switch
        {
            "tcg" => data.Paper.TCGPlayer,
            "cm" => data.Paper.Cardmarket,
            "ck" => data.Paper.CardKingdom,
            "mp" => data.Paper.ManaPool,
            _ => null
        };

        if (v == null) return;

        switch (type)
        {
            case "rn": v.RetailNormalHistory.Add(entry); break;
            case "rf": v.RetailFoilHistory.Add(entry); break;
            case "bn": v.BuylistNormalHistory.Add(entry); break;
        }
    }

    private static string SafeStr(SqliteDataReader reader, string col)
    {
        var ordinal = reader.GetOrdinal(col);
        return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
    }

    private static double SafeDouble(SqliteDataReader reader, string col)
    {
        var ordinal = reader.GetOrdinal(col);
        return reader.IsDBNull(ordinal) ? 0.0 : reader.GetDouble(ordinal);
    }

    private static PriceCurrency ParseCurrency(string s) =>
        s.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? PriceCurrency.EUR : PriceCurrency.USD;
}
