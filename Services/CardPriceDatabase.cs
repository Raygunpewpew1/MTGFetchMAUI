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
                TCGPlayer = new VendorPrices
                {
                    RetailNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, "tcg_retail_normal")),
                    RetailFoil = new PriceEntry(DateTime.Now, SafeDouble(reader, "tcg_retail_foil")),
                    BuylistNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, "tcg_buylist_normal")),
                    Currency = ParseCurrency(SafeStr(reader, "tcg_currency"))
                },
                Cardmarket = new VendorPrices
                {
                    RetailNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, "cm_retail_normal")),
                    RetailFoil = new PriceEntry(DateTime.Now, SafeDouble(reader, "cm_retail_foil")),
                    BuylistNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, "cm_buylist_normal")),
                    Currency = ParseCurrency(SafeStr(reader, "cm_currency"))
                },
                CardKingdom = new VendorPrices
                {
                    RetailNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, "ck_retail_normal")),
                    RetailFoil = new PriceEntry(DateTime.Now, SafeDouble(reader, "ck_retail_foil")),
                    BuylistNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, "ck_buylist_normal")),
                    Currency = ParseCurrency(SafeStr(reader, "ck_currency"))
                },
                ManaPool = new VendorPrices
                {
                    RetailNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, "mp_retail_normal")),
                    RetailFoil = new PriceEntry(DateTime.Now, SafeDouble(reader, "mp_retail_foil")),
                    BuylistNormal = new PriceEntry(DateTime.Now, SafeDouble(reader, "mp_buylist_normal")),
                    Currency = ParseCurrency(SafeStr(reader, "mp_currency"))
                }
            },
            LastUpdated = DateTime.Now
        };
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
