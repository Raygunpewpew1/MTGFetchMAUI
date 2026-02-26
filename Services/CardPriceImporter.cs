using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Data;
using System.Text;
using System.Text.Json;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Streams MTGJSON price JSON and batch-inserts into the prices SQLite database.
/// Optimized for large files using Utf8JsonReader and Bulk Inserts.
///
/// Note on JSON Library:
/// This class explicitly uses System.Text.Json.Utf8JsonReader instead of Newtonsoft.Json or
/// JsonDocument.Parse(stream) for memory efficiency on mobile devices.
/// MTGJSON files (e.g. AllPricesToday.json) can be large (100MB+), and loading the full DOM
/// or using a less efficient reader would cause high GC pressure and potential OOM crashes on Android.
/// The manual buffer management ensures we only hold a small chunk of the file in memory at once.
/// </summary>
public class CardPriceImporter
{
    private volatile bool _isImporting;
    private const int BatchSize = 2000;    // Increased from 500 for fewer SQL round-trips
    private const int CommitInterval = 4; // Commit every 4 batches (8,000 cards) to reduce checkpoint overhead

    public Action<bool, int, string>? OnComplete { get; set; }
    public Action<string, int>? OnProgress { get; set; }
    public bool IsImporting => _isImporting;

    /// <summary>
    /// Fire-and-forget import from a local JSON file path (used for leftover files on startup).
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
                await using var stream = File.OpenRead(jsonFilePath);
                await DoImportAsync(stream, stream.Length);
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

    /// <summary>
    /// Awaitable import from a stream (used by the download pipeline to avoid writing JSON to disk).
    /// Accepts non-seekable streams (e.g. a ZIP entry DeflateStream).
    /// </summary>
    public async Task ImportFromStreamAsync(Stream jsonStream, long uncompressedLength = 0)
    {
        if (_isImporting) return;
        _isImporting = true;
        try
        {
            await DoImportAsync(jsonStream, uncompressedLength > 0 ? uncompressedLength : null);
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
    }

    private async Task DoImportAsync(Stream jsonStream, long? streamLength = null)
    {
        var dbPath = AppDataManager.GetPricesDatabasePath();
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        // Tune for speed
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; " +
                                 "PRAGMA temp_store=MEMORY; PRAGMA synchronous=OFF; " +
                                 "PRAGMA cache_size=-16384";
            await pragma.ExecuteNonQueryAsync();
        }

        // Create/Ensure Schema
        await ExecuteAsync(conn, SQLQueries.CreatePricesTable);
        await ExecuteAsync(SQLQueries.CreatePriceHistoryTable, conn);
        await ExecuteAsync(conn, SQLQueries.CreatePricesIndex);
        await ExecuteAsync(SQLQueries.CreatePriceHistoryIndex, conn);

        OnProgress?.Invoke("Preparing import...", 5);

        var totalLength = streamLength ?? 0;

        // Sliding buffer: bufferedBytes tracks unconsumed bytes from the previous read
        // sitting at the start of the buffer. New data is appended after them.
        var buffer = new byte[1024 * 1024]; // 1MB buffer
        int bufferedBytes = 0;
        long bytesReadFromStream = 0;

        int totalCards = 0;
        var state = new JsonReaderState();
        bool inData = false;
        int depth = 0;

        int todayInt = int.Parse(DateTime.Now.ToString("yyyyMMdd"));

        // Buffers for bulk insert
        var pricesValues = new StringBuilder();
        var historyValues = new StringBuilder();
        int pendingCount = 0;
        int batchesSinceCommit = 0;

        var transaction = conn.BeginTransaction();

        while (true)
        {
            // Fill the buffer after any previously unconsumed bytes
            int spaceInBuffer = buffer.Length - bufferedBytes;
            int bytesRead = await jsonStream.ReadAsync(buffer.AsMemory(bufferedBytes, spaceInBuffer));
            bytesReadFromStream += bytesRead;
            int totalInBuffer = bufferedBytes + bytesRead;

            if (totalInBuffer == 0) break;

            bool isFinalBlock = bytesRead == 0 ||
                                (totalLength > 0 && bytesReadFromStream >= totalLength);

            // Local variables to capture reader state before it goes out of scope
            JsonReaderState nextState;
            long consumedBytes;
            bool incompleteCard = false;

            // Block scope for Utf8JsonReader (ref struct)
            {
                var reader = new Utf8JsonReader(buffer.AsSpan(0, totalInBuffer), isFinalBlock, state);
                var safeState = state;
                long safeConsumed = 0;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var propName = reader.GetString();
                        if (!inData && propName == "data")
                        {
                            inData = true;
                            depth = reader.CurrentDepth;
                            safeConsumed = reader.BytesConsumed;
                            safeState = reader.CurrentState;
                            continue;
                        }

                        if (inData && reader.CurrentDepth == depth + 1)
                        {
                            var uuid = propName;
                            if (string.IsNullOrEmpty(uuid))
                            {
                                safeConsumed = reader.BytesConsumed;
                                safeState = reader.CurrentState;
                                continue;
                            }

                            var tempReader = reader;
                            if (!tempReader.Read() || !tempReader.TrySkip())
                            {
                                incompleteCard = true;
                                break;
                            }

                            reader.Read();
                            using var doc = JsonDocument.ParseValue(ref reader);
                            var cardData = doc.RootElement;
                            var paperElement = cardData.TryGetProperty("paper", out var pEl) ? pEl : cardData;

                            var tcg = ReadVendorPrices(paperElement, "tcgplayer");
                            var cm = ReadVendorPrices(paperElement, "cardmarket");
                            var ck = ReadVendorPrices(paperElement, "cardkingdom");
                            var mp = ReadVendorPrices(paperElement, "manapool");

                            if (tcg.IsValid || cm.IsValid || ck.IsValid || mp.IsValid)
                            {
                                if (pendingCount > 0) pricesValues.Append(",");
                                pricesValues.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                                    "('{0}',{1},{2},{3},'{4}',{5},{6},{7},'{8}',{9},{10},{11},'{12}',{13},{14},{15},'{16}',CURRENT_TIMESTAMP)",
                                    uuid,
                                    tcg.RetailNormal.Price, tcg.RetailFoil.Price, tcg.BuylistNormal.Price, tcg.Currency,
                                    cm.RetailNormal.Price, cm.RetailFoil.Price, cm.BuylistNormal.Price, cm.Currency,
                                    ck.RetailNormal.Price, ck.RetailFoil.Price, ck.BuylistNormal.Price, ck.Currency,
                                    mp.RetailNormal.Price, mp.RetailFoil.Price, mp.BuylistNormal.Price, mp.Currency
                                );

                                AppendHistory(historyValues, uuid, todayInt, "tcg", tcg);
                                AppendHistory(historyValues, uuid, todayInt, "cm", cm);
                                AppendHistory(historyValues, uuid, todayInt, "ck", ck);
                                AppendHistory(historyValues, uuid, todayInt, "mp", mp);

                                totalCards++;
                                pendingCount++;
                            }
                        }
                    }
                    else if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth && inData)
                    {
                        inData = false;
                    }

                    safeConsumed = reader.BytesConsumed;
                    safeState = reader.CurrentState;
                }

                if (incompleteCard)
                {
                    nextState = safeState;
                    consumedBytes = safeConsumed;
                }
                else
                {
                    nextState = reader.CurrentState;
                    consumedBytes = reader.BytesConsumed;
                }
            } // End of reader scope

            state = nextState;

            // Slide any unconsumed bytes to the start of the buffer so they are
            // prepended to the next read. This avoids needing Stream.CanSeek.
            int consumed = (int)consumedBytes;
            int unconsumed = totalInBuffer - consumed;
            if (unconsumed > 0 && consumed > 0)
            {
                buffer.AsSpan(consumed, unconsumed).CopyTo(buffer);
            }
            else if (consumed == 0 && !isFinalBlock && unconsumed == buffer.Length)
            {
                // Nothing consumed and buffer is full — card too large for buffer. Should not happen.
                Logger.LogStuff("Price import: buffer exhausted without progress. Skipping.", LogLevel.Warning);
                break;
            }
            bufferedBytes = unconsumed;

            if (pendingCount >= BatchSize)
            {
                await FlushBatchAsync(conn, transaction, pricesValues, historyValues);
                pricesValues.Clear();
                historyValues.Clear();
                pendingCount = 0;
                batchesSinceCommit++;

                // Renew transaction periodically to keep WAL manageable
                if (batchesSinceCommit >= CommitInterval)
                {
                    await transaction.CommitAsync();
                    await transaction.DisposeAsync();
                    transaction = conn.BeginTransaction();
                    batchesSinceCommit = 0;
                }

                if (totalLength > 0)
                {
                    int percent = (int)(bytesReadFromStream * 100 / totalLength);
                    OnProgress?.Invoke($"Imported {totalCards} cards...", Math.Min(percent, 99));
                }
                else
                {
                    OnProgress?.Invoke($"Imported {totalCards} cards...", 50);
                }
            }

            if (isFinalBlock) break;
        }

        // Flush remaining
        if (pendingCount > 0)
        {
            await FlushBatchAsync(conn, transaction, pricesValues, historyValues);
        }

        await transaction.CommitAsync();
        await transaction.DisposeAsync();

        OnProgress?.Invoke("Import complete.", 100);
        OnComplete?.Invoke(true, totalCards, "");
        Logger.LogStuff($"Price import complete: {totalCards} cards imported", LogLevel.Info);
    }

    private async Task FlushBatchAsync(SqliteConnection conn, SqliteTransaction trans, StringBuilder prices, StringBuilder history)
    {
        if (prices.Length > 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText =
                "INSERT OR REPLACE INTO card_prices (" +
                "card_uuid, tcg_retail_normal, tcg_retail_foil, tcg_buylist_normal, tcg_currency, " +
                "cm_retail_normal, cm_retail_foil, cm_buylist_normal, cm_currency, " +
                "ck_retail_normal, ck_retail_foil, ck_buylist_normal, ck_currency, " +
                "mp_retail_normal, mp_retail_foil, mp_buylist_normal, mp_currency, last_updated) VALUES " +
                prices.ToString();
            await cmd.ExecuteNonQueryAsync();
        }

        if (history.Length > 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText =
                "INSERT OR IGNORE INTO card_price_history (card_uuid, price_date, vendor, price_type, price_value) VALUES " +
                history.ToString();
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private void AppendHistory(StringBuilder sb, string uuid, int date, string vendor, VendorPrices vp)
    {
        AddEntry(sb, uuid, date, vendor, "rn", vp.RetailNormal.Price);
        AddEntry(sb, uuid, date, vendor, "rf", vp.RetailFoil.Price);
        AddEntry(sb, uuid, date, vendor, "bn", vp.BuylistNormal.Price);
    }

    private void AddEntry(StringBuilder sb, string uuid, int date, string vendor, string type, double price)
    {
        if (price > 0)
        {
            if (sb.Length > 0) sb.Append(",");
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "('{0}',{1},'{2}','{3}',{4})", uuid, date, vendor, type, price);
        }
    }

    private static VendorPrices ReadVendorPrices(JsonElement paperElement, string vendorName)
    {
        if (!paperElement.TryGetProperty(vendorName, out var vendorElement))
            return VendorPrices.Empty;

        var currency = PriceCurrency.USD;
        if (vendorElement.TryGetProperty("currency", out var currEl))
            currency = currEl.GetString()?.Equals("EUR", StringComparison.OrdinalIgnoreCase) == true
                ? PriceCurrency.EUR : PriceCurrency.USD;

        var retailNormal = ReadLatestPrice(vendorElement, "retail", "normal");
        var retailFoil = ReadLatestPrice(vendorElement, "retail", "foil");
        var buylistNormal = ReadLatestPrice(vendorElement, "buylist", "normal");

        return new VendorPrices
        {
            RetailNormal = retailNormal,
            RetailFoil = retailFoil,
            BuylistNormal = buylistNormal,
            Currency = currency
            // History lists are populated from DB on read, not from JSON during import
        };
    }

    private static PriceEntry ReadLatestPrice(JsonElement vendorElement, string category, string type)
    {
        if (!vendorElement.TryGetProperty(category, out var catElement)) return PriceEntry.Empty;
        if (!catElement.TryGetProperty(type, out var typeElement)) return PriceEntry.Empty;

        // If it's a number (AllPricesToday format), use it directly
        if (typeElement.ValueKind == JsonValueKind.Number)
        {
            return new PriceEntry(DateTime.Now, typeElement.GetDouble());
        }
        // If it's an object (historical date-series), take the last entry.
        // MTGJSON date keys are ISO 8601 (YYYY-MM-DD) and sort lexicographically,
        // so the last key is the most recent — no DateTime parsing required.
        else if (typeElement.ValueKind == JsonValueKind.Object)
        {
            double lastPrice = 0;
            bool hasValue = false;
            foreach (var prop in typeElement.EnumerateObject())
            {
                if (prop.Value.TryGetDouble(out var p))
                {
                    lastPrice = p;
                    hasValue = true;
                }
            }
            if (hasValue)
                return new PriceEntry(DateTime.Now, lastPrice);
        }

        return PriceEntry.Empty;
    }

    private static async Task ExecuteAsync(string sql, SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
