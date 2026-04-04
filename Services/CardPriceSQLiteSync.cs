using AetherVault.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using System.IO.Compression;

namespace AetherVault.Services;

/// <summary>
/// Syncs current price data from a downloaded AllPricesToday.sqlite.zip into the local prices database.
/// Uses SQLite's ATTACH DATABASE to copy rows directly without any JSON parsing.
/// Replaces the old CardPriceImporter streaming JSON approach.
/// </summary>
public class CardPriceSqLiteSync
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
            MtgConstants.FilePricesTempSqlite);

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
            try { if (File.Exists(tempSqlitePath)) File.Delete(tempSqlitePath); }
            catch (Exception ex) { Logger.LogStuff($"[PriceSync] Cleanup: could not delete temp file: {ex.Message}", LogLevel.Warning); }
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
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ConnectionString;

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        await ExecuteAsync(conn, "PRAGMA journal_mode=WAL");
        // Stronger durability than OFF for the bulk sync only (slower I/O but safer if the process dies mid-commit).
        await ExecuteAsync(conn, "PRAGMA synchronous=NORMAL");
        await ExecuteAsync(conn, "PRAGMA cache_size=-65536");  // 64 MB — larger cache for bulk inserts
        await ExecuteAsync(conn, "PRAGMA temp_store=MEMORY");
        await ExecuteAsync(conn, "PRAGMA busy_timeout=60000");

        // Migrate old wide-column schema if present
        await MigrateIfNeededAsync(conn);

        await ExecuteAsync(conn, SqlQueries.CreatePricesTable);
        await ExecuteAsync(conn, SqlQueries.CreatePricesIndex);

        // ATTACH outside the transaction — SQLite allows this but keep it clean
        var escapedPath = sourceSqlitePath.Replace("'", "''");
        await ExecuteAsync(conn, $"ATTACH DATABASE '{escapedPath}' AS today");

        var hasHistoryTable = await TableExistsAsync(conn, "card_price_history");

        try
        {
            OnProgress?.Invoke("Writing prices...", 60);
            await DiagnoseAttachedDbAsync(conn);

            using var trans = conn.BeginTransaction();

            Logger.LogStuff("[PriceSync] Step 1: DELETE old prices", LogLevel.Info);
            await ExecuteTransactedAsync(conn, trans, SqlQueries.PricesDeleteAll);
            // Drop secondary index before bulk insert — rebuilt in a single pass at the end
            await ExecuteTransactedAsync(conn, trans, SqlQueries.DropPricesIndex);

            Logger.LogStuff("[PriceSync] Step 2: INSERT from attached DB", LogLevel.Info);
            await ExecuteTransactedAsync(conn, trans, SqlQueries.PricesSyncFromAttached);

            Logger.LogStuff("[PriceSync] Step 3: DROP legacy price history table", LogLevel.Info);
            await ExecuteTransactedAsync(conn, trans, SqlQueries.DropPriceHistoryTable);

            // Rebuild index in a single pass now that all data is in place
            Logger.LogStuff("[PriceSync] Rebuilding indexes...", LogLevel.Info);
            await ExecuteTransactedAsync(conn, trans, SqlQueries.CreatePricesIndex);

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
            try { await ExecuteAsync(conn, "DETACH DATABASE today"); }
            catch (Exception ex) { Logger.LogStuff($"[PriceSync] DETACH today failed (non-fatal): {ex.Message}", LogLevel.Warning); }
        }

        // Reclaim freed pages after dropping legacy history.
        // VACUUM cannot run inside a transaction or while databases are attached — both are clear here.
        if (hasHistoryTable)
        {
            Logger.LogStuff("[PriceSync] VACUUMing price DB to reclaim freed space...", LogLevel.Info);
            await ExecuteAsync(conn, "VACUUM");
            Logger.LogStuff("[PriceSync] VACUUM complete.", LogLevel.Info);
        }

        // Report row count from the now-updated local table
        var count = await conn.ExecuteScalarAsync<long>(SqlQueries.PricesCount);

        OnProgress?.Invoke("Sync complete.", 100);
        OnComplete?.Invoke(true, (int)count, "");
        Logger.LogStuff($"Price sync complete: {count} rows in card_prices.", LogLevel.Info);
    }

    private class TableInfo { public string Name { get; set; } = ""; }
    private class ColumnInfo { public string Name { get; set; } = ""; }

    /// <summary>
    /// Probes the attached MTGJSON database and logs its table names, column names, and row count.
    /// Run before the sync transaction so any schema mismatch is visible in the log.
    /// </summary>
    private static async Task DiagnoseAttachedDbAsync(SqliteConnection conn)
    {
        var tables = (await conn.QueryAsync<TableInfo>(
            "SELECT name FROM today.sqlite_master WHERE type='table' ORDER BY name"
        )).Select(t => t.Name).ToList();

        Logger.LogStuff($"[PriceSync] Attached DB tables: {string.Join(", ", tables)}", LogLevel.Info);

        if (tables.Contains("prices"))
        {
            var cols = (await conn.QueryAsync<ColumnInfo>("PRAGMA today.table_info('prices')"))
                .Select(c => c.Name).ToList();
            Logger.LogStuff($"[PriceSync] today.prices columns: {string.Join(", ", cols)}", LogLevel.Info);

            var count = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM today.prices");
            Logger.LogStuff($"[PriceSync] today.prices row count: {count}", LogLevel.Info);
        }
        else
        {
            Logger.LogStuff("[PriceSync] WARNING: 'prices' table not found in attached DB!", LogLevel.Warning);
        }
    }

    /// <summary>
    /// Detects the old wide-column schema and drops old price tables so they can be recreated.
    /// </summary>
    private static async Task MigrateIfNeededAsync(SqliteConnection conn)
    {
        var oldColumnCount = await conn.ExecuteScalarAsync<long>(SqlQueries.PricesSchemaCheck);

        if (oldColumnCount > 0)
        {
            await ExecuteAsync(conn, "DROP TABLE IF EXISTS card_prices");
            await ExecuteAsync(conn, "DROP TABLE IF EXISTS card_price_history");
            Logger.LogStuff("Price DB migrated to normalized schema.", LogLevel.Info);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        await conn.ExecuteAsync(sql);
    }

    private static async Task ExecuteTransactedAsync(SqliteConnection conn, SqliteTransaction trans, string sql)
    {
        await conn.ExecuteAsync(sql, transaction: trans);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection conn, string tableName)
    {
        var exists = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @tableName",
            new { tableName });
        return exists > 0;
    }
}
