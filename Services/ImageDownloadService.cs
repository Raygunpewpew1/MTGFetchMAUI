using MTGFetchMAUI;
using MTGFetchMAUI.Core;
using SkiaSharp;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Async image download service for fetching card images from Scryfall CDN.
/// Features caching, rate limiting, duplicate request deduplication, and
/// generation-based cancellation.
/// Port of TImageDownloadService from ImageDownloadService.pas.
/// </summary>
public class ImageDownloadService : IDisposable
{
    private readonly FileImageCache _fileCache;
    private readonly HashSet<string> _pendingDownloads = [];
    private readonly SemaphoreSlim _pendingLock = new(1, 1);
    private readonly SemaphoreSlim _downloadSemaphore;
    private int _generation;
    private DBImageCache? _thumbnailCache;

    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

    private const int MaxConcurrentDownloads = 6;
    private const int MinRequestIntervalMs = 120;
    private const int MaxWaitAttempts = 20;
    private const int WaitIntervalMs = 100;
    private const int MaxRetries = 2;

    /// <summary>
    /// The file-based image cache.
    /// </summary>
    public FileImageCache Cache => _fileCache;

    /// <summary>
    /// Optional SQLite-based thumbnail cache for small images.
    /// </summary>
    public DBImageCache? ThumbnailCache
    {
        get => _thumbnailCache;
        set => _thumbnailCache = value;
    }

    /// <summary>
    /// Current generation counter. Downloads queued with an older generation are discarded.
    /// </summary>
    public int Generation => Volatile.Read(ref _generation);

    public ImageDownloadService(FileImageCache? fileCache = null)
    {
        _fileCache = fileCache ?? new FileImageCache(maxCacheSizeMB: 500, maxCacheAgeDays: 90);
        _downloadSemaphore = new SemaphoreSlim(MaxConcurrentDownloads, MaxConcurrentDownloads);
    }

    /// <summary>
    /// Downloads an image asynchronously. Callback is invoked with the result.
    /// </summary>
    public void DownloadImageAsync(
        string scryfallId,
        Action<SKBitmap?, bool> callback,
        string imageSize = "normal",
        string face = "")
    {
        var gen = Generation;

        _ = Task.Run(async () =>
        {
            SKBitmap? bitmap = null;
            bool success = false;

            try
            {
                bitmap = await DownloadImageCoreAsync(scryfallId, imageSize, gen, face);
                success = bitmap != null;
            }
            catch (Exception ex)
            {
                Logger.LogStuff($"Image download error for {scryfallId}: {ex.Message}", LogLevel.Error);
            }

            // Only deliver if generation hasn't changed
            if (gen == Generation)
                callback(bitmap, success);
        });
    }

    /// <summary>
    /// Downloads an image and returns it directly (async).
    /// </summary>
    public async Task<SKBitmap?> DownloadImageDirectAsync(
        string scryfallId,
        string imageSize = "normal",
        string face = "")
    {
        return await DownloadImageCoreAsync(scryfallId, imageSize, Generation, face);
    }

    /// <summary>
    /// Returns a cached image, or null if not cached.
    /// </summary>
    public async Task<SKBitmap?> GetCachedImageAsync(
        string scryfallId,
        string imageSize = "normal",
        string face = "")
    {
        var cacheKey = GetCacheKey(scryfallId, imageSize, face);

        // Check thumbnail cache first for small images
        if (imageSize == "small" && _thumbnailCache != null)
        {
            var thumbImage = await _thumbnailCache.GetImageAsync(cacheKey);
            if (thumbImage != null) return thumbImage;
        }

        return await _fileCache.GetImageAsync(cacheKey);
    }

    /// <summary>
    /// Cancels all pending downloads by incrementing the generation counter.
    /// </summary>
    public void CancelPendingDownloads()
    {
        Interlocked.Increment(ref _generation);

        _pendingLock.Wait();
        try { _pendingDownloads.Clear(); }
        finally { _pendingLock.Release(); }
    }

    /// <summary>
    /// Clears both the file cache and thumbnail cache.
    /// </summary>
    public async Task ClearCacheAsync()
    {
        _fileCache.Clear();
        if (_thumbnailCache != null)
            await _thumbnailCache.ClearAsync();
    }

    /// <summary>
    /// Returns formatted cache statistics.
    /// </summary>
    public async Task<string> GetCacheStatsAsync()
    {
        var fileStats = _fileCache.GetCacheStats();
        if (_thumbnailCache != null)
        {
            var thumbStats = await _thumbnailCache.GetCacheStatsAsync();
            return $"{fileStats} | {thumbStats}";
        }
        return fileStats;
    }

    public void Dispose()
    {
        _pendingLock.Dispose();
        _downloadSemaphore.Dispose();
        _fileCache.Dispose();
        _thumbnailCache?.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Core Download Logic ──────────────────────────────────────────

    private async Task<SKBitmap?> DownloadImageCoreAsync(
        string scryfallId, string imageSize, int generation, string face)
    {
        if (string.IsNullOrEmpty(scryfallId)) return null;

        var cacheKey = GetCacheKey(scryfallId, imageSize, face);

        // 1. Check file cache
        if (_fileCache.IsCached(cacheKey))
        {
            var cached = await _fileCache.GetImageAsync(cacheKey);
            if (cached != null) return cached;
        }

        // 2. Check thumbnail DB cache for small images
        if (imageSize == "small" && _thumbnailCache != null)
        {
            var thumbImage = await _thumbnailCache.GetImageAsync(cacheKey);
            if (thumbImage != null) return thumbImage;
        }

        // 3. Check if already being downloaded (wait for it)
        if (await IsDownloadPending(cacheKey))
        {
            for (int i = 0; i < MaxWaitAttempts; i++)
            {
                await Task.Delay(WaitIntervalMs);
                if (generation != Generation) return null; // Cancelled

                if (!await IsDownloadPending(cacheKey))
                {
                    var result = await _fileCache.GetImageAsync(cacheKey);
                    if (result != null) return result;
                    break;
                }
            }
        }

        // 4. Generation check
        if (generation != Generation) return null;

        // 5. Mark as pending and download
        await MarkDownloadPending(cacheKey);
        try
        {
            await _downloadSemaphore.WaitAsync();
            try
            {
                if (generation != Generation) return null;

                return await DownloadWithRetryAsync(scryfallId, imageSize, face, cacheKey, generation);
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }
        finally
        {
            await ClearDownloadPending(cacheKey);
        }
    }

    private async Task<SKBitmap?> DownloadWithRetryAsync(
        string scryfallId, string imageSize, string face, string cacheKey, int generation)
    {
        var scryfallFace = face.Equals("back", StringComparison.OrdinalIgnoreCase)
            ? ScryfallFace.Back : ScryfallFace.Front;
        var scryfallSize = imageSize.ToLowerInvariant() switch
        {
            "small" => ScryfallSize.Small,
            "large" => ScryfallSize.Large,
            "png" => ScryfallSize.Png,
            "art_crop" => ScryfallSize.ArtCrop,
            "border_crop" => ScryfallSize.BorderCrop,
            _ => ScryfallSize.Normal
        };

        var url = ScryfallCDN.GetImageUrl(scryfallId, scryfallSize, scryfallFace);

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            if (generation != Generation) return null;

            try
            {
                await Task.Delay(MinRequestIntervalMs); // Rate limiting

                using var response = await SharedHttpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) continue;

                var data = await response.Content.ReadAsByteArrayAsync();
                if (data.Length == 0) continue;

                // Save to file cache
                using var saveStream = new MemoryStream(data);
                await _fileCache.SaveRawStreamAsync(cacheKey, saveStream);

                // Also save to thumbnail DB cache for small images
                if (imageSize == "small" && _thumbnailCache != null)
                {
                    using var thumbStream = new MemoryStream(data);
                    await _thumbnailCache.SaveRawStreamAsync(cacheKey, thumbStream, scryfallId, imageSize);
                }

                // Decode and return
                var bitmap = SKBitmap.Decode(data);
                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.LogStuff(
                    $"Image download attempt {attempt + 1} failed for {scryfallId}: {ex.Message}",
                    LogLevel.Warning);

                if (attempt < MaxRetries - 1)
                    await Task.Delay(100);
            }
        }

        return null;
    }

    // ── Pending Download Tracking ────────────────────────────────────

    private async Task<bool> IsDownloadPending(string key)
    {
        await _pendingLock.WaitAsync();
        try { return _pendingDownloads.Contains(key); }
        finally { _pendingLock.Release(); }
    }

    private async Task MarkDownloadPending(string key)
    {
        await _pendingLock.WaitAsync();
        try { _pendingDownloads.Add(key); }
        finally { _pendingLock.Release(); }
    }

    private async Task ClearDownloadPending(string key)
    {
        await _pendingLock.WaitAsync();
        try { _pendingDownloads.Remove(key); }
        finally { _pendingLock.Release(); }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string GetCacheKey(string scryfallId, string imageSize, string face)
    {
        var raw = string.IsNullOrEmpty(face) ? $"{scryfallId}_{imageSize}" : $"{scryfallId}_{imageSize}_{face}";
        return FileImageCache.GenerateKey(raw);
    }

    private static HttpClient CreateSharedHttpClient()
    {
        var handler = new HttpClientHandler();
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(MTGConstants.ScryfallUserAgent);
        return client;
    }
}
