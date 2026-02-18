using MTGFetchMAUI;
using SkiaSharp;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Thread-safe, file-based image cache storing images as WebP files on disk.
/// LRU cleanup when size limits are exceeded.
/// Port of TFileImageCache from FileImageCache.pas.
/// </summary>
public class FileImageCache : IDisposable
{
    private readonly string _cacheDir;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _currentCacheSize = -1;
    private int _fileCount = -1;
    private long _lastCleanupCheck;
    private const int CleanupCheckIntervalMs = 60_000;
    private const string FileExtension = ".webp";

    public int MaxCacheSizeMB { get; set; }
    public int MaxCacheAgeDays { get; set; }
    public string CacheDir => _cacheDir;

    public FileImageCache(string? cacheDir = null, int maxCacheSizeMB = 300, int maxCacheAgeDays = 30)
    {
        MaxCacheSizeMB = maxCacheSizeMB;
        MaxCacheAgeDays = maxCacheAgeDays;

        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            MTGConstants.AppRootFolder, "ImageCache");

        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// Generates a hash-based cache key from a URL/path and optional image size.
    /// </summary>
    public static string GenerateKey(string urlOrPath, string imageSize = "")
    {
        var combined = string.IsNullOrEmpty(imageSize) ? urlOrPath : $"{urlOrPath}_{imageSize}";
        // Simple deterministic hash using SHA256 truncated to 32 hex chars
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    /// <summary>
    /// Returns a cached SKImage, or null if not found or expired.
    /// </summary>
    public async Task<SKImage?> GetImageAsync(string key)
    {
        var filePath = GetCacheFilePath(key);
        if (!File.Exists(filePath)) return null;

        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(filePath) || IsFileExpired(filePath))
            {
                TryDeleteFile(filePath);
                return null;
            }

            var data = await File.ReadAllBytesAsync(filePath);
            return SKImage.FromEncodedData(data);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"FileImageCache.GetImage failed for {key}: {ex.Message}", LogLevel.Warning);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Convenience: generates key, gets image, returns success.
    /// </summary>
    public async Task<(bool found, SKImage? image)> TryGetImageAsync(string urlOrPath, string size = "")
    {
        var key = GenerateKey(urlOrPath, size);
        var image = await GetImageAsync(key);
        return (image != null, image);
    }

    /// <summary>
    /// Saves an SKImage to the cache as a WebP file.
    /// </summary>
    public async Task SaveImageAsync(string key, SKImage image)
    {
        await _lock.WaitAsync();
        try
        {
            CleanupIfNeeded();

            var filePath = GetCacheFilePath(key);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 85);
            await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoded.SaveTo(stream);

            UpdateCacheSize(new FileInfo(filePath).Length);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"FileImageCache.SaveImage failed for {key}: {ex.Message}", LogLevel.Warning);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Saves raw downloaded bytes directly to a cache file.
    /// </summary>
    public async Task SaveRawStreamAsync(string key, Stream stream)
    {
        await _lock.WaitAsync();
        try
        {
            CleanupIfNeeded();

            var filePath = GetCacheFilePath(key);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);

            UpdateCacheSize(new FileInfo(filePath).Length);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"FileImageCache.SaveRawStream failed for {key}: {ex.Message}", LogLevel.Warning);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Checks if a key exists in the cache and is not expired.
    /// </summary>
    public bool IsCached(string key)
    {
        var filePath = GetCacheFilePath(key);
        if (!File.Exists(filePath)) return false;

        if (IsFileExpired(filePath))
        {
            TryDeleteFile(filePath);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes a specific cached image.
    /// </summary>
    public void RemoveImage(string key)
    {
        _lock.Wait();
        try
        {
            var filePath = GetCacheFilePath(key);
            if (File.Exists(filePath))
            {
                var size = new FileInfo(filePath).Length;
                File.Delete(filePath);
                UpdateCacheSize(-size);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Deletes all cached images.
    /// </summary>
    public void Clear()
    {
        _lock.Wait();
        try
        {
            foreach (var file in Directory.GetFiles(_cacheDir, $"*{FileExtension}"))
            {
                try { File.Delete(file); } catch { }
            }

            _currentCacheSize = 0;
            _fileCount = 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns total cache size in bytes.
    /// </summary>
    public long GetTotalCacheSize()
    {
        if (_currentCacheSize < 0)
            InitializeCacheStats();
        return _currentCacheSize;
    }

    /// <summary>
    /// Returns formatted cache stats string.
    /// </summary>
    public string GetCacheStats()
    {
        if (_currentCacheSize < 0)
            InitializeCacheStats();

        var sizeMB = _currentCacheSize / (1024.0 * 1024.0);
        return $"File Cache: {_fileCount} images, {sizeMB:F1}/{MaxCacheSizeMB} MB";
    }

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private string GetCacheFilePath(string key) => Path.Combine(_cacheDir, key + FileExtension);

    private bool IsFileExpired(string filePath)
    {
        var lastWrite = File.GetLastWriteTimeUtc(filePath);
        return (DateTime.UtcNow - lastWrite).TotalDays > MaxCacheAgeDays;
    }

    private void CleanupIfNeeded()
    {
        var now = Environment.TickCount64;
        if (now - _lastCleanupCheck < CleanupCheckIntervalMs) return;
        _lastCleanupCheck = now;

        if (_currentCacheSize < 0)
            InitializeCacheStats();

        var maxBytes = (long)MaxCacheSizeMB * 1024 * 1024;
        if (_currentCacheSize <= maxBytes) return;

        // Delete oldest 25% of files
        var files = Directory.GetFiles(_cacheDir, $"*{FileExtension}")
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToArray();

        var deleteCount = Math.Max(1, files.Length / 4);
        long freedBytes = 0;

        for (int i = 0; i < deleteCount && i < files.Length; i++)
        {
            try
            {
                freedBytes += files[i].Length;
                files[i].Delete();
            }
            catch { }
        }

        _currentCacheSize = Math.Max(0, _currentCacheSize - freedBytes);
        _fileCount = Math.Max(0, _fileCount - deleteCount);

        Logger.LogStuff($"FileImageCache cleanup: freed {freedBytes / (1024.0 * 1024.0):F1} MB, " +
                        $"deleted {deleteCount} files", LogLevel.Info);
    }

    private void InitializeCacheStats()
    {
        try
        {
            var files = Directory.GetFiles(_cacheDir, $"*{FileExtension}");
            long totalSize = 0;

            foreach (var file in files)
            {
                try { totalSize += new FileInfo(file).Length; }
                catch { }
            }

            _currentCacheSize = totalSize;
            _fileCount = files.Length;
        }
        catch
        {
            _currentCacheSize = 0;
            _fileCount = 0;
        }
    }

    private void UpdateCacheSize(long delta)
    {
        if (_currentCacheSize < 0) return;
        _currentCacheSize = Math.Max(0, _currentCacheSize + delta);
        if (delta > 0) _fileCount++;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
