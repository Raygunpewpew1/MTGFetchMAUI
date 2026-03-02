using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Data;
using System.IO.Compression;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Syncs price data from a downloaded AllPricesToday.sqlite.zip into the local prices database.
/// Uses SQLite's ATTACH DATABASE to copy rows directly without any JSON parsing.
/// Replaces the old CardPriceImporter streaming JSON approach.
/// </summary>
public class CardPriceSQLiteSync
{
    public Action<bool, int, string>? OnComplete { get; set; }
    public Action<string, int>? OnProgress { get; set; }

    /// <summary>
    /// Extracts the SQLite file from the downloaded ZIP, syncs it into the local prices database,
    /// then deletes the extracted temp file. The ZIP itself is cleaned up by the caller.
    /// </summary>
    public async Task SyncFromZipAsync(string zipPath)
    {
        var tempSqlitePath = Path.Combine(
            Path.GetDirectoryName(zipPath)!,
            MTGConstants.FilePricesTempSqlite);

        try
        {
            OnProgress?.Invoke("Extracting price data...", 10);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    OnComplete?.Invoke(false, 0, "No .sqlite entry found in ZIP.");
                    return;
                }

                entry.ExtractToFile(tempSqlitePath, overwrite: true);
            }

            OnProgress?.Invoke("Syncing prices...", 30);
            await SyncFromFileAsync(tempSqlitePath);
        }
        finally
        {
            try { if (File.Exists(tempSqlitePath)) File.Delete(tempSqlitePath); } catch { }
        }
    }

    /// <summary>
    /// ATTACHes the given SQLite file and copies price rows into the local prices database.
    /// </summary>
    private async Task SyncFromFileAsync(string sourceSqlitePath)
    {
        var dbPath = AppDataManager.GetPricesDatabasePath();
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        await ExecuteAsync(conn, "PRAGMA journal_mode=WAL");
        await ExecuteAsync(conn, "PRAGMA synchronous=OFF");
        await ExecuteAsync(conn, "PRAGMA cache_size=-16384");
        await ExecuteAsync(conn, "PRAGMA busy_timeout=60000");

        // Migrate old wide-column schema if present
        await MigrateIfNeededAsync(conn);

        await ExecuteAsync(conn, SQLQueries.CreatePricesTable);
        await ExecuteAsync(conn, SQLQueries.CreatePriceHistoryTable);
        await ExecuteAsync(conn, SQLQueries.CreatePricesIndex);
        await ExecuteAsync(conn, SQLQueries.CreatePriceHistoryIndex);

        // ATTACH outside the transaction — SQLite allows this but keep it clean
        var escapedPath = sourceSqlitePath.Replace("'", "''");
        await ExecuteAsync(conn, $"ATTACH DATABASE '{escapedPath}' AS today");

        try
        {
            OnProgress?.Invoke("Writing prices...", 60);
            await DiagnoseAttachedDbAsync(conn);

            using var trans = conn.BeginTransaction();

            Logger.LogStuff("[PriceSync] Step 1: DELETE old prices", LogLevel.Info);
            await ExecuteTransactedAsync(conn, trans, SQLQueries.PricesDeleteAll);

            Logger.LogStuff("[PriceSync] Step 2: INSERT from attached DB", LogLevel.Info);
            await ExecuteTransactedAsync(conn, trans, SQLQueries.PricesSyncFromAttached);

            Logger.LogStuff("[PriceSync] Step 3: INSERT history", LogLevel.Info);
            await ExecuteTransactedAsync(conn, trans, SQLQueries.PriceHistorySyncFromAttached);

            Logger.LogStuff("[PriceSync] Step 4: Trim old history", LogLevel.Info);
            await ExecuteTransactedAsync(conn, trans,
                string.Format(SQLQueries.PriceHistoryTrimOld, MTGConstants.PriceHistoryRetentionDays));

            Logger.LogStuff("[PriceSync] Committing transaction...", LogLevel.Info);
            await trans.CommitAsync();
            Logger.LogStuff("[PriceSync] Commit successful.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"[PriceSync] Transaction failed: {ex.GetType().Name}: {ex.Message}", LogLevel.Error);
            OnProgress?.Invoke($"Sync error: {ex.Message}", 0);
            throw;
        }
        finally
        {
            try { await ExecuteAsync(conn, "DETACH DATABASE today"); } catch { }
        }

        // Report row count from the now-updated local table
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = SQLQueries.PricesCount;
        var count = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);

        OnProgress?.Invoke("Sync complete.", 100);
        OnComplete?.Invoke(true, (int)count, "");
        Logger.LogStuff($"Price sync complete: {count} rows in card_prices.", LogLevel.Info);
    }

    /// <summary>
    /// Probes the attached MTGJSON database and logs its table names, column names, and row count.
    /// Run before the sync transaction so any schema mismatch is visible in the log.
    /// </summary>
    private static async Task DiagnoseAttachedDbAsync(SqliteConnection conn)
    {
        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM today.sqlite_master WHERE type='table' ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }
        Logger.LogStuff($"[PriceSync] Attached DB tables: {string.Join(", ", tables)}", LogLevel.Info);

        if (tables.Contains("prices"))
        {
            var cols = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA today.table_info('prices')";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    cols.Add(reader.GetString(reader.GetOrdinal("name")));
            }
            Logger.LogStuff($"[PriceSync] today.prices columns: {string.Join(", ", cols)}", LogLevel.Info);

            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM today.prices";
            var count = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);
            Logger.LogStuff($"[PriceSync] today.prices row count: {count}", LogLevel.Info);
        }
        else
        {
            Logger.LogStuff("[PriceSync] WARNING: 'prices' table not found in attached DB!", LogLevel.Warning);
        }
    }

    /// <summary>
    /// Detects the old wide-column schema and drops both price tables so they can be recreated.
    /// History is lost, but the next sync immediately repopulates it.
    /// </summary>
    private static async Task MigrateIfNeededAsync(SqliteConnection conn)
    {
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = SQLQueries.PricesSchemaCheck;
        var oldColumnCount = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);

        if (oldColumnCount > 0)
        {
            await ExecuteAsync(conn, "DROP TABLE IF EXISTS card_prices");
            await ExecuteAsync(conn, "DROP TABLE IF EXISTS card_price_history");
            Logger.LogStuff("Price DB migrated to normalized schema.", LogLevel.Info);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteTransactedAsync(SqliteConnection conn, SqliteTransaction trans, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
