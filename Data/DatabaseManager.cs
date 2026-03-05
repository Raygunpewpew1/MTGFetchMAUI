using Dapper;
using Microsoft.Data.Sqlite;
using AetherVault.Services;

namespace AetherVault.Data;

/// <summary>
/// Thread-safe SQLite connection manager for MTG and Collection databases.
/// Port of TDatabaseManager from DatabaseManager.pas.
/// Uses Microsoft.Data.Sqlite instead of FireDAC.
/// </summary>
public sealed class DatabaseManager : IDisposable
{
    private SqliteConnection? _mtgConnection;
    private SqliteConnection? _collectionConnection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _isConnected;
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
                    await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateCollectionTable);
                    //    await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailCacheTable);
                    //      await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailIndexAccessed);
                    await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateDecksTable);
                    await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateDeckCardsTable);
                    //    await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailCacheTable);
                    //     await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailIndexAccessed);
                    await MigrateCollectionSchemaAsync(_collectionConnection);

                    // Connect MTG database (read-only)
                    if (_mtgConnection is null || _mtgConnection.State != System.Data.ConnectionState.Open)
                    {
                        _mtgConnection?.Dispose();
                        _mtgConnection = CreateConnection(mtgDbPath, readOnly: true);
                        await _mtgConnection.OpenAsync();
                        await ConfigureConnectionAsync(_mtgConnection, isCollection: false);

                        // Attach collection database so MTG queries can join collection tables
                        var escapedCollPath = collectionDbPath.Replace("'", "''");
                        await ExecuteNonQueryAsync(_mtgConnection, $"ATTACH DATABASE '{escapedCollPath}' AS col");
                    }

                    _isConnected = true;
                    return true;
                }
                catch
                {
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
    public SqliteConnection MTGConnection =>
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
        Disconnect();
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
    /// Executes a non-query SQL on the collection database with optional parameters.
    /// </summary>
    public async Task ExecuteCollectionSQLAsync(string sql, params (string name, object value)[] parameters)
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
        await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateCollectionTable);
        //        await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailCacheTable);
        //      await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailIndexAccessed);
        await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateDecksTable);
        await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateDeckCardsTable);
        //    await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailCacheTable);
        //     await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailIndexAccessed);
        await MigrateCollectionSchemaAsync(_collectionConnection);
    }

    private class PragmaTableInfo
    {
        public string name { get; set; } = "";
    }

    private static async Task MigrateCollectionSchemaAsync(SqliteConnection conn)
    {
        Logger.LogStuff("Starting collection schema migration check.", LogLevel.Info);

        bool hasSortOrder = false;
        bool hasIsFoil = false;
        bool hasIsEtched = false;

        var columns = await conn.QueryAsync<PragmaTableInfo>(SQLQueries.CollectionTableInfo);
        foreach (var col in columns)
        {
            if (col.name == "sort_order") hasSortOrder = true;
            if (col.name == "is_foil") hasIsFoil = true;
            if (col.name == "is_etched") hasIsEtched = true;
        }

        if (!hasSortOrder)
        {
            Logger.LogStuff("Migrating collection table: adding sort_order column and seeding values.", LogLevel.Info);
            await ExecuteNonQueryAsync(conn, SQLQueries.CollectionAddSortOrder);
            await ExecuteNonQueryAsync(conn, SQLQueries.CollectionSeedSortOrder);
        }

        if (!hasIsFoil)
        {
            Logger.LogStuff("Migrating collection table: adding is_foil column.", LogLevel.Info);
            await ExecuteNonQueryAsync(conn, SQLQueries.CollectionAddIsFoil);
        }

        if (!hasIsEtched)
        {
            Logger.LogStuff("Migrating collection table: adding is_etched column.", LogLevel.Info);
            await ExecuteNonQueryAsync(conn, SQLQueries.CollectionAddIsEtched);
        }

        // Migrate Decks table — add CommanderName if missing
        bool hasCommanderName = false;
        var deckColumns = await conn.QueryAsync<PragmaTableInfo>(SQLQueries.DecksTableInfo);
        foreach (var col in deckColumns)
        {
            if (col.name == "CommanderName")
                hasCommanderName = true;
        }

        if (!hasCommanderName)
        {
            Logger.LogStuff("Migrating decks table: adding CommanderName column.", LogLevel.Info);
            await ExecuteNonQueryAsync(conn, SQLQueries.DecksAddCommanderName);
        }

        Logger.LogStuff("Collection schema migration check completed.", LogLevel.Info);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql)
    {
        await connection.ExecuteAsync(sql);
    }

    private void DisconnectInternal()
    {
        if (_mtgConnection is { State: System.Data.ConnectionState.Open })
            _mtgConnection.Close();
        if (_collectionConnection is { State: System.Data.ConnectionState.Open })
            _collectionConnection.Close();
        _isConnected = false;
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
}
