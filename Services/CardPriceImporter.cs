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
    private const int BatchSize = 2000;         // Cards per price INSERT flush
    private const int HistoryBatchSize = 2000;  // History rows per history INSERT flush

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
        await ExecuteAsync(conn, SQLQueries.CreatePriceHistoryTable);
        await ExecuteAsync(conn, SQLQueries.CreatePricesIndex);
        await ExecuteAsync(conn, SQLQueries.CreatePriceHistoryIndex);

        OnProgress?.Invoke("Preparing import...", 5);

        var totalLength = streamLength ?? 0;

        // 4 MB sliding buffer — larger buffer reduces the frequency of the
        // incompleteCard path where we refill mid-object.
        var buffer = new byte[4 * 1024 * 1024];
        int bufferedBytes = 0;
        long bytesReadFromStream = 0;

        int totalCards = 0;
        var state = new JsonReaderState();
        bool inData = false;
        int depth = 0;

        int todayInt = int.Parse(DateTime.Now.ToString("yyyyMMdd"));

        // Separate StringBuilders for price rows and history rows
        var pricesValues = new StringBuilder();
        var historyValues = new StringBuilder();
        int pendingCount = 0;         // cards pending flush
        int historyPendingCount = 0;  // history rows pending flush

        // Single transaction for the entire import — one commit at the end is
        // dramatically faster than multiple mid-import commits with WAL checkpoints.
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

            JsonReaderState nextState;
            long consumedBytes;
            bool incompleteCard = false;

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

                            // Preflight: confirm the full card object fits in the current buffer
                            // before attempting inline streaming (Utf8JsonReader can't span buffers).
                            var tempReader = reader;
                            if (!tempReader.Read() || !tempReader.TrySkip())
                            {
                                incompleteCard = true;
                                break;
                            }

                            // Advance reader to StartObject and parse prices via streaming
                            // (no JsonDocument allocation — eliminates per-card heap pressure).
                            reader.Read();
                            var (tcg, cm, ck, mp) = ParseCardPrices(ref reader);

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

                                historyPendingCount += AppendHistory(historyValues, uuid, todayInt, "tcg", tcg);
                                historyPendingCount += AppendHistory(historyValues, uuid, todayInt, "cm", cm);
                                historyPendingCount += AppendHistory(historyValues, uuid, todayInt, "ck", ck);
                                historyPendingCount += AppendHistory(historyValues, uuid, todayInt, "mp", mp);

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
                    // Note: flush checks happen outside this block (await is not allowed
                    // inside a scope that holds a ref struct like Utf8JsonReader).
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
            } // End of Utf8JsonReader scope — safe to await from here on

            state = nextState;

            int consumed = (int)consumedBytes;
            int unconsumed = totalInBuffer - consumed;
            if (unconsumed > 0 && consumed > 0)
            {
                buffer.AsSpan(consumed, unconsumed).CopyTo(buffer);
            }
            else if (consumed == 0 && !isFinalBlock && unconsumed == buffer.Length)
            {
                Logger.LogStuff("Price import: buffer exhausted without progress. Skipping.", LogLevel.Warning);
                break;
            }
            bufferedBytes = unconsumed;

            // Flush prices when the batch threshold is met.
            // Placed outside the reader block because await can't cross a ref struct scope.
            if (pendingCount >= BatchSize)
            {
                await FlushPricesAsync(conn, transaction, pricesValues);
                pricesValues.Clear();
                pendingCount = 0;

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

            // Flush history independently — each card contributes up to 12 history rows,
            // so this threshold is separate from the price batch to prevent enormous SQL strings
            // (previously history could reach 24k rows per statement when batched with prices).
            if (historyPendingCount >= HistoryBatchSize)
            {
                await FlushHistoryAsync(conn, transaction, historyValues);
                historyValues.Clear();
                historyPendingCount = 0;
            }

            if (isFinalBlock) break;
        }

        // Flush remaining
        if (pendingCount > 0)
            await FlushPricesAsync(conn, transaction, pricesValues);
        if (historyPendingCount > 0)
            await FlushHistoryAsync(conn, transaction, historyValues);

        // Single commit for the entire import
        await transaction.CommitAsync();
        await transaction.DisposeAsync();

        OnProgress?.Invoke("Import complete.", 100);
        OnComplete?.Invoke(true, totalCards, "");
        Logger.LogStuff($"Price import complete: {totalCards} cards imported", LogLevel.Info);
    }

    // ── Flush Helpers ─────────────────────────────────────────────────

    private static async Task FlushPricesAsync(SqliteConnection conn, SqliteTransaction trans, StringBuilder prices)
    {
        if (prices.Length == 0) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;
        cmd.CommandText = SQLQueries.PricesInsertOrReplace + prices.ToString();
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task FlushHistoryAsync(SqliteConnection conn, SqliteTransaction trans, StringBuilder history)
    {
        if (history.Length == 0) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;
        cmd.CommandText = SQLQueries.PriceHistoryInsertOrIgnore + history.ToString();
        await cmd.ExecuteNonQueryAsync();
    }

    // ── History Helpers ───────────────────────────────────────────────

    /// <summary>Appends history entries for one vendor. Returns the number of rows added.</summary>
    private static int AppendHistory(StringBuilder sb, string uuid, int date, string vendor, VendorPrices vp)
    {
        int count = 0;
        count += AddEntry(sb, uuid, date, vendor, "rn", vp.RetailNormal.Price);
        count += AddEntry(sb, uuid, date, vendor, "rf", vp.RetailFoil.Price);
        count += AddEntry(sb, uuid, date, vendor, "bn", vp.BuylistNormal.Price);
        return count;
    }

    /// <summary>Appends a single history entry if price > 0. Returns 1 if added, 0 otherwise.</summary>
    private static int AddEntry(StringBuilder sb, string uuid, int date, string vendor, string type, double price)
    {
        if (price > 0)
        {
            if (sb.Length > 0) sb.Append(",");
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "('{0}',{1},'{2}','{3}',{4})", uuid, date, vendor, type, price);
            return 1;
        }
        return 0;
    }

    // ── Streaming Price Parser ────────────────────────────────────────
    //
    // Replaces JsonDocument.ParseValue to eliminate per-card heap allocations.
    // The Utf8JsonReader is a ref struct so all methods that use it must be
    // static and accept it by ref (no closures, no async).

    /// <summary>
    /// Parses vendor prices from a card object using inline streaming.
    /// Reader must be positioned at the StartObject token of the card value.
    /// Returns with reader positioned at the EndObject of the card value.
    /// </summary>
    private static (VendorPrices tcg, VendorPrices cm, VendorPrices ck, VendorPrices mp)
        ParseCardPrices(ref Utf8JsonReader reader)
    {
        var tcg = VendorPrices.Empty;
        var cm = VendorPrices.Empty;
        var ck = VendorPrices.Empty;
        var mp = VendorPrices.Empty;

        int cardDepth = reader.CurrentDepth;

        while (reader.Read() && reader.CurrentDepth > cardDepth)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var prop = reader.GetString();
            reader.Read(); // advance to value

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                if (prop == "paper")
                {
                    // Standard: { "paper": { vendors... } }
                    ParseVendorMap(ref reader, ref tcg, ref cm, ref ck, ref mp);
                }
                else
                {
                    // Fallback: vendors appear at card root (non-paper platforms or unusual structure)
                    switch (prop)
                    {
                        case "tcgplayer": tcg = ParseVendor(ref reader); break;
                        case "cardmarket": cm = ParseVendor(ref reader); break;
                        case "cardkingdom": ck = ParseVendor(ref reader); break;
                        case "manapool": mp = ParseVendor(ref reader); break;
                        default: reader.TrySkip(); break;
                    }
                }
            }
            else if (reader.TokenType == JsonTokenType.StartArray)
            {
                reader.TrySkip();
            }
            // primitives (Number, String, etc.) are already consumed by the Read() above
        }

        return (tcg, cm, ck, mp);
    }

    /// <summary>
    /// Parses a vendor map object { "tcgplayer": {...}, "cardmarket": {...}, ... }.
    /// Reader must be positioned at the StartObject token of the vendor map.
    /// </summary>
    private static void ParseVendorMap(ref Utf8JsonReader reader,
        ref VendorPrices tcg, ref VendorPrices cm, ref VendorPrices ck, ref VendorPrices mp)
    {
        int depth = reader.CurrentDepth;

        while (reader.Read() && reader.CurrentDepth > depth)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var vendorName = reader.GetString();
            reader.Read(); // advance to vendor value

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                switch (vendorName)
                {
                    case "tcgplayer": tcg = ParseVendor(ref reader); break;
                    case "cardmarket": cm = ParseVendor(ref reader); break;
                    case "cardkingdom": ck = ParseVendor(ref reader); break;
                    case "manapool": mp = ParseVendor(ref reader); break;
                    default: reader.TrySkip(); break;
                }
            }
            else if (reader.TokenType == JsonTokenType.StartArray)
            {
                reader.TrySkip();
            }
        }
    }

    /// <summary>
    /// Parses a single vendor object { "currency": "USD", "retail": {...}, "buylist": {...} }.
    /// Reader must be positioned at the StartObject token of the vendor.
    /// Returns with reader positioned at the EndObject of the vendor.
    /// </summary>
    private static VendorPrices ParseVendor(ref Utf8JsonReader reader)
    {
        var currency = PriceCurrency.USD;
        double retailNormal = 0, retailFoil = 0, buylistNormal = 0;

        int depth = reader.CurrentDepth;

        while (reader.Read() && reader.CurrentDepth > depth)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var prop = reader.GetString();
            reader.Read(); // advance to value

            switch (prop)
            {
                case "currency":
                    if (reader.TokenType == JsonTokenType.String)
                        currency = reader.GetString()?.Equals("EUR", StringComparison.OrdinalIgnoreCase) == true
                            ? PriceCurrency.EUR : PriceCurrency.USD;
                    break;

                case "retail":
                    if (reader.TokenType == JsonTokenType.StartObject)
                        ParsePriceCategory(ref reader, ref retailNormal, ref retailFoil);
                    else if (reader.TokenType == JsonTokenType.StartArray)
                        reader.TrySkip();
                    break;

                case "buylist":
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        double unused = 0;
                        ParsePriceCategory(ref reader, ref buylistNormal, ref unused);
                    }
                    else if (reader.TokenType == JsonTokenType.StartArray)
                        reader.TrySkip();
                    break;

                default:
                    if (reader.TokenType == JsonTokenType.StartObject ||
                        reader.TokenType == JsonTokenType.StartArray)
                        reader.TrySkip();
                    break;
            }
        }

        if (retailNormal == 0 && retailFoil == 0)
            return VendorPrices.Empty;

        return new VendorPrices
        {
            RetailNormal = new PriceEntry(DateTime.Now, retailNormal),
            RetailFoil = new PriceEntry(DateTime.Now, retailFoil),
            BuylistNormal = new PriceEntry(DateTime.Now, buylistNormal),
            Currency = currency,
            RetailNormalHistory = [],
            RetailFoilHistory = [],
            BuylistNormalHistory = []
        };
    }

    /// <summary>
    /// Parses a price category object { "normal": ..., "foil": ... }.
    /// Values can be direct numbers (AllPricesToday) or date-keyed objects (historical format).
    /// Reader must be positioned at the StartObject token. Returns at EndObject.
    /// </summary>
    private static void ParsePriceCategory(ref Utf8JsonReader reader, ref double normal, ref double foil)
    {
        int depth = reader.CurrentDepth;

        while (reader.Read() && reader.CurrentDepth > depth)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var key = reader.GetString();
            reader.Read(); // advance to price value

            var price = ReadPriceValue(ref reader);

            if (key == "normal") normal = price;
            else if (key == "foil") foil = price;
            // other keys: ReadPriceValue already consumed them
        }
    }

    /// <summary>
    /// Reads a price value that may be a direct number or a date-keyed object.
    /// For date-keyed objects (historical format), returns the last (most recent) value.
    /// Reader must be positioned at the value token. Returns with reader at/past that value.
    /// </summary>
    private static double ReadPriceValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetDouble();

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Date-keyed history: { "2024-01-01": 9.99, "2024-01-02": 10.00 }
            // MTGJSON date keys are ISO 8601 and sort lexicographically,
            // so the last numeric value encountered is the most recent price.
            double lastPrice = 0;
            int depth = reader.CurrentDepth;
            while (reader.Read() && reader.CurrentDepth > depth)
            {
                if (reader.TokenType == JsonTokenType.Number)
                    reader.TryGetDouble(out lastPrice);
                // PropertyName tokens (date keys) are skipped implicitly
            }
            return lastPrice;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
            reader.TrySkip();

        return 0;
    }

    // ── Misc ──────────────────────────────────────────────────────────

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
