using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Data;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Streams MTGJSON price JSON and batch-inserts into the prices SQLite database.
/// Optimized for large files using Utf8JsonReader.
/// </summary>
public class CardPriceImporter
{
    private volatile bool _isImporting;
    private const int BatchSize = 2000;

    public Action<bool, int, string>? OnComplete { get; set; }
    public Action<string, int>? OnProgress { get; set; }
    public bool IsImporting => _isImporting;

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

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; " +
                                 "PRAGMA temp_store=MEMORY; PRAGMA synchronous=OFF";
            await pragma.ExecuteNonQueryAsync();
        }

        await ExecuteAsync(conn, SQLQueries.CreatePricesTable);
        await ExecuteAsync(conn, SQLQueries.CreatePricesIndex);

        // NOTE: We no longer delete all, to support "prices right away" (keep old until updated)
        // await ExecuteAsync(conn, SQLQueries.PricesDeleteAll);

        OnProgress?.Invoke("Preparing import...", 5);

        await using var fileStream = File.OpenRead(jsonFilePath);
        var totalLength = fileStream.Length;

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = SQLQueries.PricesInsert;
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
        var pHistory = insertCmd.Parameters.Add("@history", SqliteType.Text);

        var buffer = new byte[1024 * 1024]; // 1MB buffer
        int bytesRead;
        int totalCards = 0;
        var state = new JsonReaderState();
        bool inData = false;
        int depth = 0;
        int batchCount = 0;
        var transaction = conn.BeginTransaction();
        insertCmd.Transaction = transaction;

        long streamPos = 0;

        while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
        {
            bool isFinalBlock = (streamPos + bytesRead >= totalLength);
            var reader = new Utf8JsonReader(buffer.AsSpan(0, bytesRead), isFinalBlock, state);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propName = reader.GetString();
                    if (!inData && propName == "data")
                    {
                        inData = true;
                        reader.Read(); // Start object
                        depth = reader.CurrentDepth;
                        continue;
                    }

                    if (inData && reader.CurrentDepth == depth)
                    {
                        // This is a UUID
                        var uuid = propName;

                        // Move to value (the card prices object)
                        reader.Read();

                        // Capture the card data element to parse it
                        using var doc = JsonDocument.ParseValue(ref reader);
                        var cardData = doc.RootElement;

                        if (cardData.TryGetProperty("paper", out var paperElement))
                        {
                            var tcg = ReadVendorPrices(paperElement, "tcgplayer");
                            var cm = ReadVendorPrices(paperElement, "cardmarket");
                            var ck = ReadVendorPrices(paperElement, "cardkingdom");
                            var mp = ReadVendorPrices(paperElement, "manapool");

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

                            // Serialize history compactly
                            pHistory.Value = SerializeHistory(tcg, cm, ck, mp);

                            await insertCmd.ExecuteNonQueryAsync();
                            totalCards++;
                            batchCount++;

                            if (batchCount >= BatchSize)
                            {
                                transaction.Commit();
                                transaction.Dispose();
                                transaction = conn.BeginTransaction();
                                insertCmd.Transaction = transaction;
                                batchCount = 0;

                                int percent = (int)(streamPos * 100 / totalLength);
                                OnProgress?.Invoke($"Imported {totalCards} cards...", Math.Min(percent, 99));
                            }
                        }
                    }
                }
                else if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth - 1 && inData)
                {
                    inData = false;
                }
            }

            streamPos += bytesRead;
            state = reader.CurrentState;

            // If the buffer ended mid-token, we need to handle it.
            // Utf8JsonReader does this via reader.BytesConsumed.
            if (reader.BytesConsumed < bytesRead)
            {
                var remaining = bytesRead - (int)reader.BytesConsumed;
                fileStream.Position -= remaining;
                streamPos -= remaining;
            }
        }

        transaction.Commit();
        transaction.Dispose();

        OnProgress?.Invoke("Import complete.", 100);
        OnComplete?.Invoke(true, totalCards, "");
        Logger.LogStuff($"Price import complete: {totalCards} cards imported", LogLevel.Info);
    }

    private static VendorPrices ReadVendorPrices(JsonElement paperElement, string vendorName)
    {
        if (!paperElement.TryGetProperty(vendorName, out var vendorElement))
            return VendorPrices.Empty;

        var currency = PriceCurrency.USD;
        if (vendorElement.TryGetProperty("currency", out var currEl))
            currency = currEl.GetString()?.Equals("EUR", StringComparison.OrdinalIgnoreCase) == true
                ? PriceCurrency.EUR : PriceCurrency.USD;

        var retailNormal = ReadHistory(vendorElement, "retail", "normal");
        var retailFoil = ReadHistory(vendorElement, "retail", "foil");
        var buylistNormal = ReadHistory(vendorElement, "buylist", "normal");

        return new VendorPrices
        {
            RetailNormal = GetLatest(retailNormal),
            RetailFoil = GetLatest(retailFoil),
            BuylistNormal = GetLatest(buylistNormal),
            RetailNormalHistory = retailNormal,
            RetailFoilHistory = retailFoil,
            BuylistNormalHistory = buylistNormal,
            Currency = currency
        };
    }

    private static List<PriceEntry> ReadHistory(JsonElement vendorElement, string category, string type)
    {
        if (!vendorElement.TryGetProperty(category, out var catElement)) return [];
        if (!catElement.TryGetProperty(type, out var typeElement)) return [];

        var history = new List<PriceEntry>();
        foreach (var datePricePair in typeElement.EnumerateObject())
        {
            var date = PriceDateParser.ParseISO8601Date(datePricePair.Name);
            if (datePricePair.Value.TryGetDouble(out var price))
            {
                history.Add(new PriceEntry(date, price));
            }
        }
        history.Sort((a, b) => a.Date.CompareTo(b.Date));
        return history;
    }

    private static PriceEntry GetLatest(List<PriceEntry> history)
    {
        return history.Count > 0 ? history[^1] : PriceEntry.Empty;
    }

    private static string SerializeHistory(VendorPrices tcg, VendorPrices cm, VendorPrices ck, VendorPrices mp)
    {
        // Simple JSON serialization of history data
        var historyObj = new Dictionary<string, object>();

        AddHistory(historyObj, "tcg", tcg);
        AddHistory(historyObj, "cm", cm);
        AddHistory(historyObj, "ck", ck);
        AddHistory(historyObj, "mp", mp);

        return JsonSerializer.Serialize(historyObj);
    }

    private static void AddHistory(Dictionary<string, object> root, string key, VendorPrices v)
    {
        if (v.RetailNormalHistory.Count == 0 && v.RetailFoilHistory.Count == 0 && v.BuylistNormalHistory.Count == 0)
            return;

        var vendorObj = new Dictionary<string, object>();
        if (v.RetailNormalHistory.Count > 0) vendorObj["rn"] = MapHistory(v.RetailNormalHistory);
        if (v.RetailFoilHistory.Count > 0) vendorObj["rf"] = MapHistory(v.RetailFoilHistory);
        if (v.BuylistNormalHistory.Count > 0) vendorObj["bn"] = MapHistory(v.BuylistNormalHistory);

        root[key] = vendorObj;
    }

    private static object MapHistory(List<PriceEntry> history)
    {
        // Store as array of [ticks, price] to save space
        return history.Select(h => new object[] { h.Date.ToString("yyyyMMdd"), h.Price }).ToArray();
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
