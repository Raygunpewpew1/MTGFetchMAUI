using MTGFetchMAUI.Data;
using MTGFetchMAUI.Models;
using SkiaSharp;

namespace MTGFetchMAUI.Services;

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

    private ImageDownloadService? _imageService;
    private DBImageCache? _thumbnailCache;
    private CardPriceManager? _priceManager;
    private CancellationTokenSource? _downloadCts;
    private readonly SemaphoreSlim _priceInitLock = new(1, 1);

    // ── Events ───────────────────────────────────────────────────────

    /// <summary>Progress callback for downloads: (message, percent).</summary>
    public event Action<string, int>? OnProgress;

    /// <summary>Fired when the database is ready after download/connect.</summary>
    public event Action? OnDatabaseReady;

    /// <summary>Fired with success status on database operations.</summary>
    public event Action<bool>? OnDatabaseError;

    /// <summary>Fired when a new MTG database version is available remotely.</summary>
    public event Action<string>? OnDatabaseUpdateAvailable;

    /// <summary>One-shot callback when prices first become available.</summary>
    public Action? OnPricesReady { get; set; }

    /// <summary>Persistent callback for subsequent price refreshes.</summary>
    public event Action? OnPricesUpdated;

    // ── Properties ───────────────────────────────────────────────────

    public DatabaseManager DatabaseManager => _databaseManager;

    /// <summary>
    /// Lazy-initialized image download service.
    /// </summary>
    public ImageDownloadService ImageService => _imageService ??= CreateImageService();

    // ── Constructor ──────────────────────────────────────────────────

    public CardManager()
    {
        _databaseManager = new DatabaseManager();
        _cardRepository = new CardRepository(_databaseManager);
        _collectionRepository = new CollectionRepository(_databaseManager, _cardRepository);
    }

    /// <summary>
    /// Creates a separate CardManager instance for use on background threads.
    /// Has its own database connection.
    /// </summary>
    public static async Task<CardManager> CreateForThreadAsync()
    {
        var manager = new CardManager();
        await manager.InitializeAsync();
        return manager;
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
    /// Disconnects from databases.
    /// </summary>
    public void Disconnect()
    {
        if (_databaseManager.IsConnected)
            _databaseManager.Disconnect();
    }

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
            var success = await AppDataManager.DownloadDatabaseAsync(ct);
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

            _priceManager.OnProgress = (msg, pct) => OnProgress?.Invoke(msg, pct);
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

            // Try importing existing local data, then check for updates
            _priceManager.ImportDataAsync();
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

    public async Task<Card[]> SearchInCollectionAsync(string nameFilter = "")
    {
        var helper = _cardRepository.CreateSearchHelper();
        helper.SearchMyCollection();
        if (!string.IsNullOrEmpty(nameFilter))
            helper.WhereNameContains(nameFilter);
        helper.OrderBy("c.name");
        return await _cardRepository.SearchCardsAdvancedAsync(helper);
    }

    public MTGSearchHelper CreateSearchHelper() => _cardRepository.CreateSearchHelper();

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

    public async Task AddCardToCollectionAsync(string cardUUID, int quantity = 1)
    {
        await _collectionRepository.AddCardAsync(cardUUID, quantity);
    }

    public async Task RemoveCardFromCollectionAsync(string cardUUID)
    {
        await _collectionRepository.RemoveCardAsync(cardUUID);
    }

    public async Task UpdateCardQuantityAsync(string cardUUID, int quantity)
    {
        await _collectionRepository.UpdateQuantityAsync(cardUUID, quantity);
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

    public async Task<CollectionStats> GetCollectionStatsAsync()
    {
        return await _collectionRepository.GetCollectionStatsAsync();
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
        _imageService?.CancelPendingDownloads();
    }

    public async Task ClearImageCacheAsync()
    {
        if (_imageService != null)
            await _imageService.ClearCacheAsync();
    }

    public async Task<string> GetImageCacheStatsAsync()
    {
        return _imageService != null
            ? await _imageService.GetCacheStatsAsync()
            : "Image service not initialized.";
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
        _imageService?.Dispose();
        _thumbnailCache?.Dispose();
        _priceManager?.Dispose();
        _databaseManager.Dispose();
        _priceInitLock.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private ImageDownloadService CreateImageService()
    {
        var service = new ImageDownloadService();

        // Link thumbnail cache if DB is connected
        if (_databaseManager.IsConnected)
        {
            _thumbnailCache ??= new DBImageCache(_databaseManager);
            service.ThumbnailCache = _thumbnailCache;
        }

        return service;
    }
}
