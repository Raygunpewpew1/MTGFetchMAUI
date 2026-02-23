using SkiaSharp;
using System.Collections.Concurrent;

namespace MTGFetchMAUI.Services;

public class ImageCacheService : IDisposable
{
    private readonly DBImageCache _dbCache;
    private readonly FileImageCache _fileCache;

    // L1 Cache: Map key -> (Image, Node)
    private readonly ConcurrentDictionary<string, (SKImage Image, LinkedListNode<string> Node)> _cache = new();
    private readonly LinkedList<string> _lruList = new();
    private readonly object _lruLock = new();

    // Keep enough images for ~3-4 screens of content to ensure smooth scrolling
    private const int MaxMemoryImages = 100;

    public ImageCacheService(DBImageCache dbCache, FileImageCache fileCache)
    {
        _dbCache = dbCache;
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

        // 2. L2 DB Cache (Thumbnails)
        var img = await _dbCache.GetImageAsync(key);
        if (img != null)
        {
            AddToMemoryCache(key, img);
            return img;
        }

        // 3. L3 File Cache (Larger images)
        if (useFileCache)
        {
            img = await _fileCache.GetImageAsync(key);
            if (img != null)
            {
                AddToMemoryCache(key, img);
                return img;
            }
        }

        return null;
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
