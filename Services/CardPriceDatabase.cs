using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Data;
using System.Text.Json;

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
        await ExecuteAsync(SQLQueries.CreatePricesIndex);
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

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = SQLQueries.PricesGetByUuid;
            cmd.Parameters.AddWithValue("@uuid", uuid);
            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return (false, CardPriceData.Empty);

            var prices = PopulatePriceData(reader);
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

                cmd.CommandText = $"SELECT * FROM card_prices WHERE card_uuid IN ({string.Join(",", paramsList)})";

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
        var historyJson = SafeStr(reader, "history_json");
        var historyDict = ParseHistory(historyJson);

        return new CardPriceData
        {
            UUID = SafeStr(reader, "card_uuid"),
            Paper = new PaperPlatform
            {
                TCGPlayer = PopulateVendor(reader, "tcg", historyDict, "tcg"),
                Cardmarket = PopulateVendor(reader, "cm", historyDict, "cm"),
                CardKingdom = PopulateVendor(reader, "ck", historyDict, "ck"),
                ManaPool = PopulateVendor(reader, "mp", historyDict, "mp")
            },
            LastUpdated = DateTime.Now
        };
    }

    private static VendorPrices PopulateVendor(SqliteDataReader reader, string prefix, Dictionary<string, JsonElement> historyDict, string historyKey)
    {
        var vendorHistory = historyDict.TryGetValue(historyKey, out var el) ? el : (JsonElement?)null;

        return new VendorPrices
        {
            RetailNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, $"{prefix}_retail_normal")),
            RetailFoil = new PriceEntry(DateTime.Now, SafeDouble(reader, $"{prefix}_retail_foil")),
            BuylistNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, $"{prefix}_buylist_normal")),
            Currency = ParseCurrency(SafeStr(reader, $"{prefix}_currency")),
            RetailNormalHistory = ParsePriceEntries(vendorHistory, "rn"),
            RetailFoilHistory = ParsePriceEntries(vendorHistory, "rf"),
            BuylistNormalHistory = ParsePriceEntries(vendorHistory, "bn")
        };
    }

    private static Dictionary<string, JsonElement> ParseHistory(string json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        }
        catch { return []; }
    }

    private static List<PriceEntry> ParsePriceEntries(JsonElement? vendorHistory, string key)
    {
        if (vendorHistory == null || !vendorHistory.Value.TryGetProperty(key, out var listEl))
            return [];

        var list = new List<PriceEntry>();
        foreach (var item in listEl.EnumerateArray())
        {
            if (item.GetArrayLength() == 2)
            {
                var dateStr = item[0].GetString() ?? "";
                var price = item[1].GetDouble();
                var date = PriceDateParser.ParseCompactDate(dateStr);
                list.Add(new PriceEntry(date, price));
            }
        }
        return list;
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
