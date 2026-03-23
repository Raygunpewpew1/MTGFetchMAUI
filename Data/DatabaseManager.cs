using System.Data;
using System.Text.Json;
using AetherVault.Services;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AetherVault.Data;

/// <summary>
/// Thread-safe manager for the two SQLite databases: MTG (read-only card data) and Collection (read-write for user data).
/// ConnectAsync opens both and attaches the collection DB to the MTG connection so queries can join across them (e.g. col.my_collection).
/// All repository access goes through MTGConnection or CollectionConnection; never hold connections outside this class.
/// </summary>
public sealed class DatabaseManager : IDisposable
{
    private SqliteConnection? _mtgConnection;
    private SqliteConnection? _collectionConnection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private volatile bool _isConnected;
    private bool _disposed;

    private const int MaxConnectionRetries = 3;
    private const int BusyTimeoutMs = 5000;

    static DatabaseManager()
    {
        SqlMapper.AddTypeHandler(new JsonArrayTypeHandler());
    }

    /// <summary>
    /// Connects to both the MTG and Collection SQLite databases.
    /// Creates collection tables if they don't exist.
    /// </summary>
    public async Task<bool> ConnectAsync(string mtgDbPath, string collectionDbPath)
    {
        #region agent log
        AgentDebugLog("initial", "H1", "Data/DatabaseManager.cs:ConnectAsync:entry", "ConnectAsync entered", new
        {
            mtgDbPath,
            collectionDbPath,
            mtgExists = File.Exists(mtgDbPath),
            collectionExists = File.Exists(collectionDbPath),
            isConnected = _isConnected
        });
        #endregion
        await _connectionLock.WaitAsync();
        try
        {
            if (_isConnected) return true;

            if (!File.Exists(mtgDbPath))
            {
                // Try to create collection even without MTG db
                await InitializeCollectionAsync(collectionDbPath);
                return false;
            }

            var retryCount = 0;
            while (retryCount < MaxConnectionRetries)
            {
                try
                {
                    // Connect Collection database (read-write)
                    if (_collectionConnection is null || _collectionConnection.State != System.Data.ConnectionState.Open)
                    {
                        _collectionConnection?.Dispose();
                        _collectionConnection = CreateConnection(collectionDbPath, readOnly: false);
                        await _collectionConnection.OpenAsync();
                        await ConfigureConnectionAsync(_collectionConnection, isCollection: true);
                    }

                    // Create collection tables
                    await ExecuteNonQueryAsync(_collectionConnection, SqlQueries.CreateCollectionTable);
                    await ExecuteNonQueryAsync(_collectionConnection, SqlQueries.CreateDecksTable);
                    await ExecuteNonQueryAsync(_collectionConnection, SqlQueries.CreateDeckCardsTable);
                    await MigrateCollectionSchemaAsync(_collectionConnection);

                    // Connect MTG database (read-only) and attach collection so we can JOIN col.my_collection in search/collection queries
                    if (_mtgConnection is null || _mtgConnection.State != System.Data.ConnectionState.Open)
                    {
                        _mtgConnection?.Dispose();
                        _mtgConnection = CreateConnection(mtgDbPath, readOnly: true);
                        await _mtgConnection.OpenAsync();
                        await ConfigureConnectionAsync(_mtgConnection, isCollection: false);

                        // Attach collection DB as "col" so queries on MTG connection can reference col.my_collection, col.Decks, etc.
                        var escapedCollPath = collectionDbPath.Replace("'", "''");
                        await ExecuteNonQueryAsync(_mtgConnection, $"ATTACH DATABASE '{escapedCollPath}' AS col");
                    }

                    _isConnected = true;
                    #region agent log
                    AgentDebugLog("initial", "H1", "Data/DatabaseManager.cs:ConnectAsync:success", "ConnectAsync succeeded", new
                    {
                        mtgState = _mtgConnection?.State.ToString(),
                        collectionState = _collectionConnection?.State.ToString(),
                        isConnected = _isConnected
                    });
                    #endregion
                    return true;
                }
                catch (Exception ex)
                {
                    #region agent log
                    AgentDebugLog("initial", "H1", "Data/DatabaseManager.cs:ConnectAsync:retry", "ConnectAsync retry due to exception", new
                    {
                        retryCount,
                        error = ex.Message
                    });
                    #endregion
                    retryCount++;
                    if (retryCount >= MaxConnectionRetries) throw;
                    await Task.Delay(100 * retryCount);
                    DisconnectInternal();
                }
            }

            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Gets the MTG database connection. Throws if not connected.
    /// </summary>
    public SqliteConnection MtgConnection =>
        _mtgConnection ?? throw new InvalidOperationException("MTG database not connected.");

    /// <summary>
    /// Gets the Collection database connection. Throws if not connected.
    /// </summary>
    public SqliteConnection CollectionConnection =>
        _collectionConnection ?? throw new InvalidOperationException("Collection database not connected.");

    /// <summary>Thread-safe connection status check.</summary>
    public bool IsConnected => _isConnected &&
        (_mtgConnection?.State == System.Data.ConnectionState.Open) &&
        (_collectionConnection?.State == System.Data.ConnectionState.Open);

    /// <summary>Disconnect and reconnect.</summary>
    public async Task<bool> ReconnectAsync(string mtgDbPath, string collectionDbPath)
    {
        await DisconnectAsync();
        return await ConnectAsync(mtgDbPath, collectionDbPath);
    }

    /// <summary>Close all connections.</summary>
    public void Disconnect()
    {
        _connectionLock.Wait();
        try
        {
            DisconnectInternal();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Asynchronously closes all connections. Use from async code paths on the UI thread
    /// to avoid a sync-over-async deadlock that <see cref="Disconnect"/> can cause.
    /// </summary>
    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            DisconnectInternal();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Executes a non-query SQL on the collection database with optional parameters.
    /// </summary>
    public async Task ExecuteCollectionSqlAsync(string sql, params (string name, object value)[] parameters)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Database not connected.");

        var dynamicParams = new DynamicParameters();
        foreach (var (name, value) in parameters)
        {
            dynamicParams.Add(name, value);
        }

        await _collectionConnection!.ExecuteAsync(sql, dynamicParams);
    }

    /// <summary>
    /// Provides the connection lock for external callers that need
    /// to perform multiple operations atomically.
    /// </summary>
    public SemaphoreSlim ConnectionLock => _connectionLock;

    private static SqliteConnection CreateConnection(string dbPath, bool readOnly)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate
        };
        return new SqliteConnection(builder.ConnectionString);
    }

    private static async Task ConfigureConnectionAsync(SqliteConnection connection, bool isCollection)
    {
        if (isCollection)
        {
            // Collection DB holds user-authored data: favor safer durability.
            await connection.ExecuteAsync(
                $"""
                PRAGMA busy_timeout = {BusyTimeoutMs};
                PRAGMA locking_mode = NORMAL;
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA temp_store = MEMORY;
                """);

            Logger.LogStuff("Configured collection database PRAGMAs: journal_mode=WAL, synchronous=NORMAL.", LogLevel.Debug);
        }
        else
        {
            // MTG master DB is a re-downloadable read-only cache: keep aggressive read performance settings.
            await connection.ExecuteAsync(
                $"""
                PRAGMA busy_timeout = {BusyTimeoutMs};
                PRAGMA locking_mode = NORMAL;
                PRAGMA synchronous = OFF;
                PRAGMA temp_store = MEMORY;
                PRAGMA journal_mode = MEMORY;
                """);

            Logger.LogStuff("Configured MTG master database PRAGMAs for read-optimized access.", LogLevel.Debug);
        }
    }

    private async Task InitializeCollectionAsync(string collectionDbPath)
    {
        _collectionConnection?.Dispose();
        _collectionConnection = CreateConnection(collectionDbPath, readOnly: false);
        await _collectionConnection.OpenAsync();
        await ConfigureConnectionAsync(_collectionConnection, isCollection: true);
        await ExecuteNonQueryAsync(_collectionConnection, SqlQueries.CreateCollectionTable);
        await ExecuteNonQueryAsync(_collectionConnection, SqlQueries.CreateDecksTable);
        await ExecuteNonQueryAsync(_collectionConnection, SqlQueries.CreateDeckCardsTable);
        await MigrateCollectionSchemaAsync(_collectionConnection);
    }

    private class PragmaTableInfo
    {
        public string Name { get; set; } = "";
    }

    private static async Task MigrateCollectionSchemaAsync(SqliteConnection conn)
    {
        Logger.LogStuff("Starting collection schema migration check.", LogLevel.Info);

        bool hasSortOrder = false;
        bool hasIsFoil = false;
        bool hasIsEtched = false;

        var columns = await conn.QueryAsync<PragmaTableInfo>(SqlQueries.CollectionTableInfo);
        foreach (var col in columns)
        {
            if (col.Name == "sort_order") hasSortOrder = true;
            if (col.Name == "is_foil") hasIsFoil = true;
            if (col.Name == "is_etched") hasIsEtched = true;
        }

        if (!hasSortOrder)
        {
            Logger.LogStuff("Migrating collection table: adding sort_order column and seeding values.", LogLevel.Info);
            await ExecuteNonQueryAsync(conn, SqlQueries.CollectionAddSortOrder);
            await ExecuteNonQueryAsync(conn, SqlQueries.CollectionSeedSortOrder);
        }

        if (!hasIsFoil)
        {
            Logger.LogStuff("Migrating collection table: adding is_foil column.", LogLevel.Info);
            await ExecuteNonQueryAsync(conn, SqlQueries.CollectionAddIsFoil);
        }

        if (!hasIsEtched)
        {
            Logger.LogStuff("Migrating collection table: adding is_etched column.", LogLevel.Info);
            await ExecuteNonQueryAsync(conn, SqlQueries.CollectionAddIsEtched);
        }

        // Migrate Decks table — add CommanderName if missing
        bool hasCommanderName = false;
        var deckColumns = await conn.QueryAsync<PragmaTableInfo>(SqlQueries.DecksTableInfo);
        foreach (var col in deckColumns)
        {
            if (col.Name == "CommanderName")
                hasCommanderName = true;
        }

        if (!hasCommanderName)
        {
            Logger.LogStuff("Migrating decks table: adding CommanderName column.", LogLevel.Info);
            await ExecuteNonQueryAsync(conn, SqlQueries.DecksAddCommanderName);
        }

        Logger.LogStuff("Collection schema migration check completed.", LogLevel.Info);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql)
    {
        await connection.ExecuteAsync(sql);
    }

    private void DisconnectInternal()
    {
        _isConnected = false;

        // Close + dispose so the OS releases file handles before we replace AllPrintings.sqlite
        // (e.g. in-app DB update). A closed-but-not-disposed SqliteConnection can keep Android
        // from deleting/overwriting the DB during extract, causing a long stall or failure.
        if (_mtgConnection != null)
        {
            try
            {
                if (_mtgConnection.State == ConnectionState.Open)
                    _mtgConnection.Close();
            }
            catch (Exception ex)
            {
                Logger.LogStuff($"Disconnect: error closing MTG connection: {ex.Message}", LogLevel.Warning);
            }
            finally
            {
                _mtgConnection.Dispose();
                _mtgConnection = null;
            }
        }

        if (_collectionConnection != null)
        {
            try
            {
                if (_collectionConnection.State == ConnectionState.Open)
                    _collectionConnection.Close();
            }
            catch (Exception ex)
            {
                Logger.LogStuff($"Disconnect: error closing collection connection: {ex.Message}", LogLevel.Warning);
            }
            finally
            {
                _collectionConnection.Dispose();
                _collectionConnection = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectInternal();
        _mtgConnection?.Dispose();
        _collectionConnection?.Dispose();
        _connectionLock.Dispose();
    }

    private static void AgentDebugLog(string runId, string hypothesisId, string location, string message, object data)
    {
        try
        {
            var dataJson = JsonSerializer.Serialize(data);
            Logger.LogStuff($"DBG|session=068b48|run={runId}|h={hypothesisId}|loc={location}|msg={message}|data={dataJson}", LogLevel.Info);
        }
        catch
        {
            // Never fail app flow for debug logging.
        }
    }
}
