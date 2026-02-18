using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Data;
using SkiaSharp;

namespace MTGFetchMAUI.Services;

/// <summary>
/// SQLite-based thumbnail cache for small card images.
/// Uses the collection database's thumbnail_cache table.
/// Port of TDBImageCache from DBImageCache.pas.
/// </summary>
public class DBImageCache : IDisposable
{
    private readonly DatabaseManager _db;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _lastCleanupCheck;
    private int _cachedRowCount = -1;
    private long _cachedTotalSize = -1;

    public int MaxRows { get; set; }
    public int MaxSizeMB { get; set; }
    public int EvictBatchSize { get; set; }

    private const int CleanupCheckIntervalMs = 60_000;

    public DBImageCache(DatabaseManager databaseManager, int maxRows = 5000, int maxSizeMB = 200)
    {
        _db = databaseManager;
        MaxRows = maxRows;
        MaxSizeMB = maxSizeMB;
        EvictBatchSize = 500;
    }

    /// <summary>
    /// Retrieves a cached thumbnail by key, or null if not found.
    /// Updates the last_accessed timestamp for LRU tracking.
    /// </summary>
    public async Task<SKImage?> GetImageAsync(string key)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_db.IsConnected) return null;

            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.ThumbnailGet;
            cmd.Parameters.AddWithValue("@cache_key", key);
            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync()) return null;

            var data = (byte[])reader["image_data"];

            // Update last_accessed (fire-and-forget within lock)
            using var updateCmd = _db.CollectionConnection.CreateCommand();
            updateCmd.CommandText = SQLQueries.ThumbnailUpdateAccess;
            updateCmd.Parameters.AddWithValue("@cache_key", key);
            await updateCmd.ExecuteNonQueryAsync();

            return SKImage.FromEncodedData(data);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"DBImageCache.GetImage failed for {key}: {ex.Message}", LogLevel.Warning);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Saves raw image bytes to the thumbnail cache.
    /// </summary>
    public async Task SaveRawStreamAsync(string key, Stream stream,
        string scryfallId = "", string imageSize = "")
    {
        await _lock.WaitAsync();
        try
        {
            if (!_db.IsConnected) return;

            await CleanupIfNeededAsync();

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var data = ms.ToArray();

            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.ThumbnailInsert;
            cmd.Parameters.AddWithValue("@cache_key", key);
            cmd.Parameters.AddWithValue("@scryfall_id", scryfallId);
            cmd.Parameters.AddWithValue("@image_size", imageSize);
            cmd.Parameters.AddWithValue("@image_data", data);
            cmd.Parameters.AddWithValue("@file_size", data.Length);
            await cmd.ExecuteNonQueryAsync();

            UpdateStats(data.Length, 1);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"DBImageCache.SaveRawStream failed for {key}: {ex.Message}", LogLevel.Warning);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Checks if a key exists in the thumbnail cache.
    /// </summary>
    public async Task<bool> IsCachedAsync(string key)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_db.IsConnected) return false;

            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.ThumbnailExists;
            cmd.Parameters.AddWithValue("@cache_key", key);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync();
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

    /// <summary>
    /// Removes a specific cached thumbnail.
    /// </summary>
    public async Task RemoveImageAsync(string key)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_db.IsConnected) return;

            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.ThumbnailDeleteByKey;
            cmd.Parameters.AddWithValue("@cache_key", key);
            await cmd.ExecuteNonQueryAsync();

            // Invalidate stats so they're recomputed next time
            _cachedRowCount = -1;
            _cachedTotalSize = -1;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Deletes all cached thumbnails.
    /// </summary>
    public async Task ClearAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!_db.IsConnected) return;

            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.ThumbnailClear;
            await cmd.ExecuteNonQueryAsync();

            _cachedRowCount = 0;
            _cachedTotalSize = 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns formatted cache statistics.
    /// </summary>
    public async Task<string> GetCacheStatsAsync()
    {
        await InitializeStatsIfNeededAsync();
        var sizeMB = _cachedTotalSize / (1024.0 * 1024.0);
        return $"DB Thumb Cache: {_cachedRowCount} images, {sizeMB:F1}/{MaxSizeMB} MB";
    }

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private async Task InitializeStatsIfNeededAsync()
    {
        if (_cachedRowCount >= 0) return;

        try
        {
            if (!_db.IsConnected) { _cachedRowCount = 0; _cachedTotalSize = 0; return; }

            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.ThumbnailStats;
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                _cachedRowCount = reader.GetInt32(0);
                _cachedTotalSize = reader.GetInt64(1);
            }
            else
            {
                _cachedRowCount = 0;
                _cachedTotalSize = 0;
            }
        }
        catch
        {
            _cachedRowCount = 0;
            _cachedTotalSize = 0;
        }
    }

    private void UpdateStats(long deltaSize, int deltaCount)
    {
        if (_cachedRowCount < 0) return;
        _cachedTotalSize = Math.Max(0, _cachedTotalSize + deltaSize);
        _cachedRowCount += deltaCount;
    }

    private async Task CleanupIfNeededAsync()
    {
        var now = Environment.TickCount64;
        if (now - _lastCleanupCheck < CleanupCheckIntervalMs) return;
        _lastCleanupCheck = now;

        await InitializeStatsIfNeededAsync();

        var maxBytes = (long)MaxSizeMB * 1024 * 1024;
        if (_cachedRowCount <= MaxRows && _cachedTotalSize <= maxBytes) return;

        await EvictOldestAsync();
    }

    private async Task EvictOldestAsync()
    {
        try
        {
            using var cmd = _db.CollectionConnection.CreateCommand();
            cmd.CommandText = SQLQueries.ThumbnailEvictLru;
            cmd.Parameters.AddWithValue("@evict_count", EvictBatchSize);
            await cmd.ExecuteNonQueryAsync();

            // Invalidate stats to recompute from DB
            _cachedRowCount = -1;
            _cachedTotalSize = -1;

            Logger.LogStuff($"DBImageCache: evicted {EvictBatchSize} oldest entries", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"DBImageCache eviction failed: {ex.Message}", LogLevel.Error);
        }
    }
}
