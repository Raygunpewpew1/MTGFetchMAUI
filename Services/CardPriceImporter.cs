using System.Text.Json;
using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Data;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Streams MTGJSON price JSON and batch-inserts into the prices SQLite database.
/// Port of TCardPriceImporter from CardPriceImporter.pas.
/// </summary>
public class CardPriceImporter
{
    private volatile bool _isImporting;

    private const int BatchSize = 5000;

    /// <summary>Fired on completion with (success, cardCount, errorMessage).</summary>
    public Action<bool, int, string>? OnComplete { get; set; }

    /// <summary>Progress callback: (message, percent).</summary>
    public Action<string, int>? OnProgress { get; set; }

    public bool IsImporting => _isImporting;

    /// <summary>
    /// Starts an async import of the given JSON file into the price database.
    /// </summary>
    public void ImportAsync(string jsonFilePath)
    {
        if (_isImporting) return;
        if (!File.Exists(jsonFilePath))
        {
            OnComplete?.Invoke(false, 0, "Price JSON file not found.");
            return;
        }

        _isImporting = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await DoImportAsync(jsonFilePath);
            }
            catch (Exception ex)
            {
                Logger.LogStuff($"Price import failed: {ex.Message}", LogLevel.Error);
                OnComplete?.Invoke(false, 0, ex.Message);
            }
            finally
            {
                _isImporting = false;
            }
        });
    }

    private async Task DoImportAsync(string jsonFilePath)
    {
        var dbPath = AppDataManager.GetPricesDatabasePath();
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        // Configure for bulk import
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; " +
                                 "PRAGMA temp_store=MEMORY; PRAGMA synchronous=OFF";
            await pragma.ExecuteNonQueryAsync();
        }

        // Ensure schema
        await ExecuteAsync(conn, SQLQueries.CreatePricesTable);
        await ExecuteAsync(conn, SQLQueries.CreatePricesIndex);

        // Clear existing data
        await ExecuteAsync(conn, SQLQueries.PricesDeleteAll);

        OnProgress?.Invoke("Reading price data...", 5);

        int totalCards = 0;
        await using var fileStream = File.OpenRead(jsonFilePath);
        using var doc = await JsonDocument.ParseAsync(fileStream);

        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var dataElement))
        {
            OnComplete?.Invoke(false, 0, "JSON missing 'data' property.");
            return;
        }

        // Count total for progress
        int totalEntries = 0;
        foreach (var _ in dataElement.EnumerateObject())
            totalEntries++;

        OnProgress?.Invoke($"Importing {totalEntries} cards...", 10);

        // Batch insert
        var currentTransaction = conn.BeginTransaction();
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = SQLQueries.PricesInsert;
        insertCmd.Transaction = currentTransaction;

        // Pre-create parameters
        var pUuid = insertCmd.Parameters.Add("@uuid", SqliteType.Text);
        var pTcgRn = insertCmd.Parameters.Add("@tcg_rn", SqliteType.Real);
        var pTcgRf = insertCmd.Parameters.Add("@tcg_rf", SqliteType.Real);
        var pTcgBn = insertCmd.Parameters.Add("@tcg_bn", SqliteType.Real);
        var pTcgCur = insertCmd.Parameters.Add("@tcg_cur", SqliteType.Text);
        var pCmRn = insertCmd.Parameters.Add("@cm_rn", SqliteType.Real);
        var pCmRf = insertCmd.Parameters.Add("@cm_rf", SqliteType.Real);
        var pCmBn = insertCmd.Parameters.Add("@cm_bn", SqliteType.Real);
        var pCmCur = insertCmd.Parameters.Add("@cm_cur", SqliteType.Text);
        var pCkRn = insertCmd.Parameters.Add("@ck_rn", SqliteType.Real);
        var pCkRf = insertCmd.Parameters.Add("@ck_rf", SqliteType.Real);
        var pCkBn = insertCmd.Parameters.Add("@ck_bn", SqliteType.Real);
        var pCkCur = insertCmd.Parameters.Add("@ck_cur", SqliteType.Text);
        var pMpRn = insertCmd.Parameters.Add("@mp_rn", SqliteType.Real);
        var pMpRf = insertCmd.Parameters.Add("@mp_rf", SqliteType.Real);
        var pMpBn = insertCmd.Parameters.Add("@mp_bn", SqliteType.Real);
        var pMpCur = insertCmd.Parameters.Add("@mp_cur", SqliteType.Text);

        int batchCount = 0;

        try
        {
            foreach (var cardEntry in dataElement.EnumerateObject())
            {
                var uuid = cardEntry.Name;
                var cardData = cardEntry.Value;

                // Extract paper prices
                if (!cardData.TryGetProperty("paper", out var paperElement))
                    continue;

                var tcg = ReadVendorPrices(paperElement, "tcgplayer");
                var cm = ReadVendorPrices(paperElement, "cardmarket");
                var ck = ReadVendorPrices(paperElement, "cardkingdom");
                var mp = ReadVendorPrices(paperElement, "manapool");

                // Skip if all zeros
                if (tcg.RetailNormal.Price == 0 && tcg.RetailFoil.Price == 0 &&
                    cm.RetailNormal.Price == 0 && cm.RetailFoil.Price == 0 &&
                    ck.RetailNormal.Price == 0 && ck.RetailFoil.Price == 0 &&
                    mp.RetailNormal.Price == 0 && mp.RetailFoil.Price == 0)
                    continue;

                pUuid.Value = uuid;
                pTcgRn.Value = tcg.RetailNormal.Price;
                pTcgRf.Value = tcg.RetailFoil.Price;
                pTcgBn.Value = tcg.BuylistNormal.Price;
                pTcgCur.Value = tcg.Currency.ToString();
                pCmRn.Value = cm.RetailNormal.Price;
                pCmRf.Value = cm.RetailFoil.Price;
                pCmBn.Value = cm.BuylistNormal.Price;
                pCmCur.Value = cm.Currency.ToString();
                pCkRn.Value = ck.RetailNormal.Price;
                pCkRf.Value = ck.RetailFoil.Price;
                pCkBn.Value = ck.BuylistNormal.Price;
                pCkCur.Value = ck.Currency.ToString();
                pMpRn.Value = mp.RetailNormal.Price;
                pMpRf.Value = mp.RetailFoil.Price;
                pMpBn.Value = mp.BuylistNormal.Price;
                pMpCur.Value = mp.Currency.ToString();

                await insertCmd.ExecuteNonQueryAsync();
                totalCards++;
                batchCount++;

                if (batchCount >= BatchSize)
                {
                    currentTransaction.Commit();
                    currentTransaction.Dispose();
                    currentTransaction = conn.BeginTransaction();
                    insertCmd.Transaction = currentTransaction;
                    batchCount = 0;

                    var percent = 10 + (int)(totalCards * 85.0 / totalEntries);
                    OnProgress?.Invoke($"Imported {totalCards} cards...", Math.Min(percent, 95));
                }
            }

            // Commit remaining
            if (batchCount > 0)
                currentTransaction.Commit();
        }
        finally
        {
            currentTransaction.Dispose();
        }

        OnProgress?.Invoke("Import complete.", 100);
        OnComplete?.Invoke(true, totalCards, "");

        Logger.LogStuff($"Price import complete: {totalCards} cards imported", LogLevel.Info);
    }

    // ── JSON Reading Helpers ─────────────────────────────────────────

    private static VendorPrices ReadVendorPrices(JsonElement paperElement, string vendorName)
    {
        if (!paperElement.TryGetProperty(vendorName, out var vendorElement))
            return VendorPrices.Empty;

        var currency = PriceCurrency.USD;
        if (vendorElement.TryGetProperty("currency", out var currEl))
            currency = currEl.GetString()?.Equals("EUR", StringComparison.OrdinalIgnoreCase) == true
                ? PriceCurrency.EUR : PriceCurrency.USD;

        return new VendorPrices
        {
            RetailNormal = ReadLatestPrice(vendorElement, "retail", "normal"),
            RetailFoil = ReadLatestPrice(vendorElement, "retail", "foil"),
            BuylistNormal = ReadLatestPrice(vendorElement, "buylist", "normal"),
            Currency = currency
        };
    }

    private static PriceEntry ReadLatestPrice(JsonElement vendorElement, string category, string type)
    {
        if (!vendorElement.TryGetProperty(category, out var catElement)) return PriceEntry.Empty;
        if (!catElement.TryGetProperty(type, out var typeElement)) return PriceEntry.Empty;

        // The type element contains date->price pairs; pick the latest
        DateTime latestDate = DateTime.MinValue;
        double latestPrice = 0;

        foreach (var datePricePair in typeElement.EnumerateObject())
        {
            var date = PriceDateParser.ParseISO8601Date(datePricePair.Name);
            if (datePricePair.Value.TryGetDouble(out var price) && date > latestDate)
            {
                latestDate = date;
                latestPrice = price;
            }
        }

        return latestPrice > 0 ? new PriceEntry(latestDate, latestPrice) : PriceEntry.Empty;
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
