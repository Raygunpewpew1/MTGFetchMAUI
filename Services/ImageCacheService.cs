using SkiaSharp;
using System.Collections.Concurrent;

namespace MTGFetchMAUI.Services;

public class ImageCacheService : IDisposable
{
    private readonly FileImageCache _fileCache;

    // L1 Cache: Map key -> (Image, Node)
    private readonly ConcurrentDictionary<string, (SKImage Image, LinkedListNode<string> Node)> _cache = new();
    private readonly LinkedList<string> _lruList = new();
    private readonly object _lruLock = new();

    // 400 thumbnails at ~270 KB each ≈ 108 MB — same budget as 100 full-res images,
    // but 4× more unique cards cached for large-collection browsing.
    private const int MaxMemoryImages = 400;

    public ImageCacheService(FileImageCache fileCache)
    {
        _fileCache = fileCache;
    }

    public SKImage? GetMemoryImage(string key)
    {
        lock (_lruLock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                TouchLru(entry.Node);
                return entry.Image;
            }
        }
        return null;
    }

    public async Task<SKImage?> GetImageAsync(string key, bool useFileCache = true)
    {
        // 1. L1 Memory
        if (_cache.TryGetValue(key, out var entry))
        {
            TouchLru(entry.Node);
            return entry.Image;
        }

        // 2. L2 File Cache
        if (useFileCache)
        {
            var img = await _fileCache.GetImageAsync(key);
            if (img != null)
            {
                AddToMemoryCache(key, img);
                return img;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads from L2 file cache only. Does NOT promote the result to L1 memory cache.
    /// Used by the card grid to temporarily load a full-res image for thumbnail generation.
    /// </summary>
    public async Task<SKImage?> GetFileOnlyAsync(string key)
    {
        return await _fileCache.GetImageAsync(key);
    }

    public void AddToMemoryCache(string key, SKImage image)
    {
        lock (_lruLock)
        {
            if (_cache.ContainsKey(key)) return;

            // Evict if full
            if (_cache.Count >= MaxMemoryImages)
            {
                var first = _lruList.First;
                if (first != null)
                {
                    _lruList.RemoveFirst();
                    // We remove from dictionary
                    if (_cache.TryRemove(first.Value, out var entry))
                    {
                        // Dispose the evicted image
                        entry.Image.Dispose();
                    }
                }
            }

            var node = new LinkedListNode<string>(key);
            _lruList.AddLast(node);
            _cache[key] = (image, node);
        }
    }

    private void TouchLru(LinkedListNode<string> node)
    {
        // Quick check to avoid locking if already at end?
        // LinkedList doesn't expose "IsLast" easily without checking Next == null.
        if (node.Next == null) return;

        lock (_lruLock)
        {
            if (node.List == _lruList)
            {
                _lruList.Remove(node);
                _lruList.AddLast(node);
            }
        }
    }

    public void Dispose()
    {
        foreach (var entry in _cache.Values)
        {
            entry.Image.Dispose();
        }
        _cache.Clear();
        _lruList.Clear();
    }
}
