using Microsoft.Data.Sqlite;

namespace MTGFetchMAUI.Data;

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
                    // Connect MTG database (read-only)
                    if (_mtgConnection is null || _mtgConnection.State != System.Data.ConnectionState.Open)
                    {
                        _mtgConnection?.Dispose();
                        _mtgConnection = CreateConnection(mtgDbPath, readOnly: true);
                        await _mtgConnection.OpenAsync();
                        await ConfigureConnectionAsync(_mtgConnection);
                    }

                    // Connect Collection database (read-write)
                    if (_collectionConnection is null || _collectionConnection.State != System.Data.ConnectionState.Open)
                    {
                        _collectionConnection?.Dispose();
                        _collectionConnection = CreateConnection(collectionDbPath, readOnly: false);
                        await _collectionConnection.OpenAsync();
                        await ConfigureConnectionAsync(_collectionConnection);
                    }

                    // Create collection tables
                    await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateCollectionTable);
                    await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailCacheTable);
                    await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailIndexAccessed);
                    await MigrateCollectionSchemaAsync(_collectionConnection);

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

        using var cmd = _collectionConnection!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
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

    private static async Task ConfigureConnectionAsync(SqliteConnection connection)
    {
        // Match the Delphi performance pragmas
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            $"""
            PRAGMA busy_timeout = {BusyTimeoutMs};
            PRAGMA locking_mode = NORMAL;
            PRAGMA synchronous = OFF;
            PRAGMA temp_store = MEMORY;
            PRAGMA journal_mode = MEMORY;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InitializeCollectionAsync(string collectionDbPath)
    {
        _collectionConnection?.Dispose();
        _collectionConnection = CreateConnection(collectionDbPath, readOnly: false);
        await _collectionConnection.OpenAsync();
        await ConfigureConnectionAsync(_collectionConnection);
        await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateCollectionTable);
        await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailCacheTable);
        await ExecuteNonQueryAsync(_collectionConnection, SQLQueries.CreateThumbnailIndexAccessed);
        await MigrateCollectionSchemaAsync(_collectionConnection);
    }

    private static async Task MigrateCollectionSchemaAsync(SqliteConnection conn)
    {
        // Add sort_order column if it doesn't exist (upgrade from older schema)
        bool hasSortOrder = false;
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(my_collection)";
            using var reader = await pragma.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1) == "sort_order") { hasSortOrder = true; break; }
            }
        }

        if (!hasSortOrder)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE my_collection ADD COLUMN sort_order INTEGER DEFAULT 0";
            await alter.ExecuteNonQueryAsync();

            // Seed existing rows so relative order is preserved
            using var seed = conn.CreateCommand();
            seed.CommandText = "UPDATE my_collection SET sort_order = rowid WHERE sort_order = 0";
            await seed.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
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
