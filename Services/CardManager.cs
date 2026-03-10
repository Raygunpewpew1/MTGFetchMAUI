using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;
using SkiaSharp;

namespace AetherVault.Services;

/// <summary>
/// Facade coordinating repositories, image service, and price management.
/// Provides a unified API for the UI layer.
/// Port of TCardManager from CardManagerRefactored.pas.
/// </summary>
public class CardManager : IDisposable
{
    private readonly DatabaseManager _databaseManager;
    private readonly ICardRepository _cardRepository;
    private readonly ICollectionRepository _collectionRepository;

    private readonly ImageDownloadService _imageService;
    private CardPriceManager? _priceManager;
    private CancellationTokenSource? _downloadCts;
    private readonly SemaphoreSlim _priceInitLock = new(1, 1);
    private readonly SemaphoreSlim _startupLock = new(1, 1);

    private double _cachedTotalValue;
    private DateTime _totalValueCacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan TotalValueCacheTtl = TimeSpan.FromMinutes(5);
    private bool? _ftsAvailable;
    private int _collectionVersion;

    // ── Events ───────────────────────────────────────────────────────

    /// <summary>Progress callback for downloads: (message, percent).</summary>
    public event Action<string, int>? OnProgress;

    /// <summary>Progress callback for price syncs: (message, percent).</summary>
    public event Action<string, int>? OnPriceSyncProgress;

    /// <summary>Fired when the database is ready after download/connect.</summary>
    public event Action? OnDatabaseReady;

    /// <summary>Fired with success status on database operations.</summary>
    public event Action<bool>? OnDatabaseError;

    /// <summary>Fired when a download is cancelled by the user.</summary>
    public event Action? OnDownloadCancelled;

    /// <summary>Fired when a new MTG database version is available remotely.</summary>
    public event Action<string>? OnDatabaseUpdateAvailable;

    /// <summary>Fired after any collection mutation (add, remove, update, clear, bulk add). Use to invalidate stats caches.</summary>
    public event Action? CollectionChanged;

    /// <summary>One-shot callback when prices first become available.</summary>
    public Action? OnPricesReady { get; set; }

    /// <summary>Persistent callback for subsequent price refreshes.</summary>
    public event Action? OnPricesUpdated;

    // ── Properties ───────────────────────────────────────────────────

    public DatabaseManager DatabaseManager => _databaseManager;

    public ImageDownloadService ImageService => _imageService;

    /// <summary>
    /// Monotonically increasing version for the user's collection.
    /// Incremented on any collection mutation so consumers can detect when
    /// a reload is necessary. Read via <see cref="CollectionVersion"/>.
    /// </summary>
    public int CollectionVersion => Volatile.Read(ref _collectionVersion);

    // ── Constructor ──────────────────────────────────────────────────

    public CardManager(
        DatabaseManager databaseManager,
        ICardRepository cardRepository,
        ICollectionRepository collectionRepository,
        ImageDownloadService imageDownloadService)
    {
        _databaseManager = databaseManager;
        _cardRepository = cardRepository;
        _collectionRepository = collectionRepository;
        _imageService = imageDownloadService;
    }

    // ── Initialization ───────────────────────────────────────────────

    /// <summary>
    /// Connects to the MTG and collection databases.
    /// Returns true if the connection succeeded.
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        var mtgPath = AppDataManager.GetMTGDatabasePath();
        var collectionPath = AppDataManager.GetCollectionDatabasePath();
        return await _databaseManager.ConnectAsync(mtgPath, collectionPath);
    }

    /// <summary>
    /// Returns true immediately if already connected, otherwise calls InitializeAsync().
    /// </summary>
    public async Task<bool> EnsureInitializedAsync()
    {
        if (_databaseManager.IsConnected) return true;
        return await InitializeAsync();
    }

    /// <summary>
    /// Returns true if the MTG DB has the av_cards_fts table (built by CI). Cached per connection; when false, search uses LIKE fallback.
    /// </summary>
    public async Task<bool> IsFtsAvailableAsync()
    {
        if (_ftsAvailable.HasValue) return _ftsAvailable.Value;
        if (!_databaseManager.IsConnected) return false;
        _ftsAvailable = await _cardRepository.HasFtsAsync();
        return _ftsAvailable.Value;
    }

    /// <summary>
    /// Disconnects from databases. Only call from a background thread (e.g. inside Task.Run).
    /// </summary>
    public void Disconnect()
    {
        if (_databaseManager.IsConnected)
            _databaseManager.Disconnect();
        _ftsAvailable = null;
    }

    /// <summary>
    /// Asynchronously disconnects from databases. Use from async code paths on the UI thread
    /// to avoid deadlocking the synchronization context.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_databaseManager.IsConnected)
            await _databaseManager.DisconnectAsync();
        _ftsAvailable = null;
    }

    /// <summary>
    /// Attempts to claim the exclusive startup lock. Returns true if this caller is the
    /// active startup owner. Returns false immediately if another instance of
    /// LoadingViewModel is already running the startup sequence.
    /// Must be paired with <see cref="EndStartup"/> in a finally block.
    /// </summary>
    public bool TryBeginStartup() => _startupLock.Wait(0);

    /// <summary>
    /// Releases the startup lock. Always call in a finally block paired with
    /// a successful <see cref="TryBeginStartup"/> call.
    /// </summary>
    public void EndStartup() => _startupLock.Release();

    /// <summary>
    /// Checks if a new main database version is available.
    /// Returns (updateAvailable, localVersion, remoteVersion).
    /// </summary>
    public async Task<(bool updateAvailable, string localVersion, string remoteVersion)> CheckForMainDatabaseUpdateAsync()
    {
        return await AppDataManager.CheckForDatabaseUpdateAsync();
    }

    /// <summary>
    /// Downloads the MTG database from MTGJSON asynchronously.
    /// </summary>
    public void DownloadDatabase()
    {
        Disconnect();
        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        AppDataManager.OnProgress = (msg, pct) => OnProgress?.Invoke(msg, pct);

        _ = Task.Run(async () =>
        {
            bool success;
            try
            {
                success = await AppDataManager.DownloadDatabaseAsync(ct);
            }
            catch (OperationCanceledException)
            {
                OnDownloadCancelled?.Invoke();
                return;
            }

            if (ct.IsCancellationRequested)
            {
                OnDownloadCancelled?.Invoke();
                return;
            }

            if (success)
            {
                try
                {
                    await InitializeAsync();
                    OnDatabaseReady?.Invoke();
                }
                catch (Exception ex)
                {
                    Logger.LogStuff($"Post-download init failed: {ex.Message}", LogLevel.Error);
                    OnDatabaseError?.Invoke(false);
                }
            }
            else
            {
                OnDatabaseError?.Invoke(false);
            }
        });
    }

    /// <summary>
    /// Cancels an in-progress database download.
    /// </summary>
    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    /// <summary>
    /// Initializes the price management system.
    /// </summary>
    public async Task InitializePricesAsync()
    {
        await _priceInitLock.WaitAsync();
        try
        {
            if (_priceManager != null) return;

            _priceManager = new CardPriceManager();
            await _priceManager.InitializeAsync();

            _priceManager.OnProgress = (msg, pct) => OnPriceSyncProgress?.Invoke(msg, pct);
            _priceManager.OnDatabaseUpdateAvailable = version => OnDatabaseUpdateAvailable?.Invoke(version);
            _priceManager.OnLoadComplete = (success, message) =>
            {
                if (success)
                {
                    var ready = OnPricesReady;
                    OnPricesReady = null; // One-shot
                    ready?.Invoke();
                    OnPricesUpdated?.Invoke();
                }
            };

            _priceManager.CheckForUpdates();
            _priceManager.StartPeriodicCheck();
        }
        finally
        {
            _priceInitLock.Release();
        }
    }

    /// <summary>
    /// Marks the current database version as up-to-date.
    /// </summary>
    public async Task MarkDatabaseUpdatedAsync()
    {
        if (_priceManager != null)
            await _priceManager.MarkDatabaseUpdatedAsync();
    }

    // ── Search Methods ───────────────────────────────────────────────

    public async Task<Card[]> SearchCardsAsync(string nameFilter, int limit = 25)
    {
        return await _cardRepository.SearchCardsAsync(nameFilter, limit);
    }

    public async Task<Card[]> SearchByNameAsync(string name, int limit = 25)
    {
        var helper = _cardRepository.CreateSearchHelper();
        helper.SearchCards()
            .WhereNameContains(name)
            .WherePrimarySideOnly()
            .OrderBy("c.name")
            .Limit(limit);
        return await _cardRepository.SearchCardsAdvancedAsync(helper);
    }

    public async Task<Card[]> SearchByTypeAsync(string cardType, int limit = 25)
    {
        var helper = _cardRepository.CreateSearchHelper();
        helper.SearchCards()
            .WhereType(cardType)
            .WherePrimarySideOnly()
            .OrderBy("c.name")
            .Limit(limit);
        return await _cardRepository.SearchCardsAdvancedAsync(helper);
    }

    public async Task<Card[]> SearchByColorsAsync(string colors, int limit = 25)
    {
        var helper = _cardRepository.CreateSearchHelper();
        helper.SearchCards()
            .WhereColors(colors)
            .WherePrimarySideOnly()
            .OrderBy("c.name")
            .Limit(limit);
        return await _cardRepository.SearchCardsAdvancedAsync(helper);
    }

    /// <param name="nameFilter">Optional name filter (contains).</param>
    /// <param name="limit">Max results; 0 = no limit.</param>
    public async Task<Card[]> SearchInCollectionAsync(string nameFilter = "", int limit = 0)
    {
        var helper = _cardRepository.CreateSearchHelper();
        helper.SearchMyCollection();
        if (!string.IsNullOrEmpty(nameFilter))
            helper.WhereNameContains(nameFilter);
        helper.OrderBy("c.name");
        if (limit > 0)
            helper.Limit(limit);
        return await _cardRepository.SearchCardsAdvancedAsync(helper);
    }

    public MTGSearchHelper CreateSearchHelper() => _cardRepository.CreateSearchHelper();

    /// <summary>Returns all sets (code + name) for filter dropdowns, ordered by name.</summary>
    public async Task<IReadOnlyList<SetInfo>> GetAllSetsAsync() => await _cardRepository.GetAllSetsAsync();

    public async Task<Card[]> ExecuteSearchAsync(MTGSearchHelper searchHelper)
    {
        return await _cardRepository.SearchCardsAdvancedAsync(searchHelper);
    }

    public async Task<int> GetCountAdvancedAsync(MTGSearchHelper searchHelper)
    {
        return await _cardRepository.GetCountAdvancedAsync(searchHelper);
    }

    // ── Card Detail Methods ──────────────────────────────────────────

    public async Task<Card> GetCardDetailsAsync(string uuid)
    {
        return await _cardRepository.GetCardDetailsAsync(uuid);
    }

    public async Task<Card> GetCardWithLegalitiesAsync(string uuid)
    {
        return await _cardRepository.GetCardWithLegalitiesAsync(uuid);
    }

    public async Task<Card> GetCardWithRulingsAsync(string uuid)
    {
        return await _cardRepository.GetCardWithRulingsAsync(uuid);
    }

    public async Task<Card[]> GetFullCardPackageAsync(string uuid)
    {
        return await _cardRepository.GetFullCardPackageAsync(uuid);
    }

    // ── Collection Methods ───────────────────────────────────────────

    public async Task AddCardToCollectionAsync(string cardUUID, int quantity = 1, bool isFoil = false, bool isEtched = false)
    {
        await _collectionRepository.AddCardAsync(cardUUID, quantity, isFoil, isEtched);
        InvalidateTotalValueCache();
    }

    public async Task AddCardsToCollectionBulkAsync(IEnumerable<(string cardUUID, int quantity, bool isFoil, bool isEtched)> cards)
    {
        await _collectionRepository.AddCardsBulkAsync(cards);
        InvalidateTotalValueCache();
    }

    public async Task RemoveCardFromCollectionAsync(string cardUUID)
    {
        await _collectionRepository.RemoveCardAsync(cardUUID);
        InvalidateTotalValueCache();
    }

    public async Task UpdateCardQuantityAsync(string cardUUID, int quantity, bool isFoil = false, bool isEtched = false)
    {
        await _collectionRepository.UpdateQuantityAsync(cardUUID, quantity, isFoil, isEtched);
        InvalidateTotalValueCache();
    }

    public async Task<bool> IsInCollectionAsync(string cardUUID)
    {
        return await _collectionRepository.IsInCollectionAsync(cardUUID);
    }

    public async Task<int> GetQuantityAsync(string cardUUID)
    {
        return await _collectionRepository.GetQuantityAsync(cardUUID);
    }

    public async Task<CollectionItem[]> GetCollectionAsync()
    {
        return await _collectionRepository.GetCollectionAsync();
    }

    /// <summary>
    /// Returns collection stats (counts, CMC, etc.) without total value. Use for fast initial display.
    /// </summary>
    public async Task<CollectionStats> GetCollectionStatsAsync()
    {
        return await _collectionRepository.GetCollectionStatsAsync();
    }

    /// <summary>
    /// Computes collection total value using preferred vendor and bulk price lookup. Call in background after showing stats.
    /// Result is cached for <see cref="TotalValueCacheTtl"/>; use <see cref="InvalidateTotalValueCache"/> after collection mutations.
    /// </summary>
    public async Task<double> GetCollectionTotalValueAsync()
    {
        if (_priceManager == null) return 0;

        if (DateTime.UtcNow < _totalValueCacheExpiry)
            return _cachedTotalValue;

        var entries = await _collectionRepository.GetCollectionEntriesForPricingAsync();
        if (entries.Count == 0) return 0;

        var uuids = entries.Select(e => e.Uuid).Distinct().ToList();
        var pricesMap = await _priceManager.GetCardPricesBulkAsync(uuids);
        double total = 0;
        foreach (var (uuid, quantity, isFoil, isEtched) in entries)
        {
            if (pricesMap.TryGetValue(uuid, out var data))
                total += quantity * PriceDisplayHelper.GetNumericPrice(data, isFoil, isEtched);
        }

        _cachedTotalValue = total;
        _totalValueCacheExpiry = DateTime.UtcNow.Add(TotalValueCacheTtl);
        return total;
    }

    private void InvalidateTotalValueCache()
    {
        _totalValueCacheExpiry = DateTime.MinValue;
        BumpCollectionVersion();
    }

    public async Task ReorderCollectionAsync(IList<string> orderedUuids)
    {
        await _collectionRepository.ReorderAsync(orderedUuids);
        BumpCollectionVersion();
    }

    public async Task ClearCollectionAsync()
    {
        await _collectionRepository.ClearCollectionAsync();
        InvalidateTotalValueCache();
    }

    private void BumpCollectionVersion()
    {
        Interlocked.Increment(ref _collectionVersion);
        CollectionChanged?.Invoke();
    }

    // ── Image Methods ────────────────────────────────────────────────

    public void DownloadCardImageAsync(
        string scryfallId,
        Action<SKImage?, bool> callback,
        string imageSize = "normal",
        string face = "")
    {
        ImageService.DownloadImageAsync(scryfallId, callback, imageSize, face);
    }

    public async Task<SKImage?> GetCachedCardImageAsync(
        string scryfallId, string imageSize = "normal", string face = "")
    {
        return await ImageService.GetCachedImageAsync(scryfallId, imageSize, face);
    }

    public void CancelPendingImageDownloads()
    {
        _imageService.CancelPendingDownloads();
    }

    public async Task ClearImageCacheAsync()
    {
        await _imageService.ClearCacheAsync();
    }

    public async Task<string> GetImageCacheStatsAsync()
    {
        return await _imageService.GetCacheStatsAsync();
    }

    // ── Price Methods ────────────────────────────────────────────────

    public async Task<(bool found, CardPriceData prices)> GetCardPricesAsync(string uuid)
    {
        return _priceManager != null
            ? await _priceManager.GetCardPricesAsync(uuid)
            : (false, CardPriceData.Empty);
    }

    public async Task<Dictionary<string, CardPriceData>> GetCardPricesBulkAsync(IEnumerable<string> uuids)
    {
        return _priceManager != null
            ? await _priceManager.GetCardPricesBulkAsync(uuids)
            : [];
    }

    // ── Dispose ──────────────────────────────────────────────────────

    public void Dispose()
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _priceManager?.Dispose();
        _databaseManager.Dispose();
        _priceInitLock.Dispose();
        _startupLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
