using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services.DeckBuilder;
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
    private string _cachedTotalValueVendorKey = "";
    private static readonly TimeSpan TotalValueCacheTtl = TimeSpan.FromMinutes(15);
    private bool? _ftsAvailable;
    private int _collectionVersion;

    /// <summary>Startup prefetch for collection price sort; cleared when collection or vendor context changes.</summary>
    private readonly object _warmCollectionPricesLock = new();
    private Dictionary<string, CardPriceData>? _warmCollectionPrices;
    private int _warmCollectionPricesVersion = -1;
    private string _warmCollectionPricesVendorKey = "";

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

    /// <summary>Fired on the main thread before disconnecting for an MTG DB replace (cancel UI/DB work).</summary>
    public event Action? MtgDatabaseReplacing;

    /// <summary>Fired on the main thread after a new MTG DB file is connected.</summary>
    public event Action? MtgDatabaseReplaced;

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
        var mtgPath = AppDataManager.GetMtgDatabasePath();
        var collectionPath = AppDataManager.GetCollectionDatabasePath();
        var connected = await _databaseManager.ConnectAsync(mtgPath, collectionPath);
        if (connected)
            NotifyMtgDatabaseReplaced();
        return connected;
    }

    /// <summary>
    /// Cancels downloads, notifies singleton VMs/grids to reset, so disconnect can wait on
    /// <see cref="DatabaseManager.ConnectionLock"/> without use-after-dispose on SQLite.
    /// </summary>
    public async Task PrepareForMtgDatabaseReplacementAsync()
    {
        Logger.LogStuff("Preparing for MTG database replacement.", LogLevel.Info);
        _imageService.CancelPendingDownloads();
        _ftsAvailable = null;

        if (MainThread.IsMainThread)
            MtgDatabaseReplacing?.Invoke();
        else
            await MainThread.InvokeOnMainThreadAsync(() => MtgDatabaseReplacing?.Invoke()).ConfigureAwait(false);
    }

    /// <summary>Notifies listeners that a new MTG master DB is connected (main thread).</summary>
    public void NotifyMtgDatabaseReplaced()
    {
        _ftsAvailable = null;
        if (MainThread.IsMainThread)
            MtgDatabaseReplaced?.Invoke();
        else
            MainThread.BeginInvokeOnMainThread(() => MtgDatabaseReplaced?.Invoke());
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
    /// Waits until the current startup owner calls <see cref="EndStartup"/> (non-owning wait).
    /// Use when <see cref="TryBeginStartup"/> returned false so a redundant <c>LoadingPage</c>
    /// instance can dismiss after the peer that holds the lock finishes (avoids a stuck splash
    /// when Android delivers overlapping <c>OnAppearing</c> and the wrong <c>_initTask</c> is awaited).
    /// </summary>
    public async Task WaitForStartupReleasedAsync(CancellationToken cancellationToken = default)
    {
        await _startupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        _startupLock.Release();
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
        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        AppDataManager.OnProgress = (msg, pct) => OnProgress?.Invoke(msg, pct);

        _ = Task.Run(async () =>
        {
            bool success;
            try
            {
                await PrepareForMtgDatabaseReplacementAsync().ConfigureAwait(false);
                await DisconnectAsync().ConfigureAwait(false);
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
        if (!PricePreferences.PricesDataEnabled)
            return;

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
    /// Stops downloads/timers and releases price resources when the user disables price data in settings.
    /// </summary>
    public async Task DisablePricesSubsystemAsync()
    {
        if (PricePreferences.PricesDataEnabled)
            return;

        await _priceInitLock.WaitAsync();
        try
        {
            _priceManager?.Dispose();
            _priceManager = null;
            ClearWarmCollectionPrices();
        }
        finally
        {
            _priceInitLock.Release();
        }
    }

    /// <summary>
    /// After an interrupted sync, resumes the update check when the app returns to the foreground.
    /// </summary>
    public void NotifyAppResumedForPrices()
    {
        if (!PricePreferences.PricesDataEnabled)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await InitializePricesAsync();
                if (PricePreferences.SyncPending && _priceManager != null)
                    _priceManager.CheckForUpdates();
            }
            catch (Exception ex)
            {
                Logger.LogStuff($"Price resume check failed: {ex.Message}", LogLevel.Warning);
            }
        });
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
        return await _cardRepository.SearchAdvancedAsync(helper);
    }

    public async Task<Card[]> SearchByTypeAsync(string cardType, int limit = 25)
    {
        var helper = _cardRepository.CreateSearchHelper();
        helper.SearchCards()
            .WhereType(cardType)
            .WherePrimarySideOnly()
            .OrderBy("c.name")
            .Limit(limit);
        return await _cardRepository.SearchAdvancedAsync(helper);
    }

    public async Task<Card[]> SearchByColorsAsync(string colors, int limit = 25)
    {
        var helper = _cardRepository.CreateSearchHelper();
        helper.SearchCards()
            .WhereColors(colors)
            .WherePrimarySideOnly()
            .OrderBy("c.name")
            .Limit(limit);
        return await _cardRepository.SearchAdvancedAsync(helper);
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
        return await _cardRepository.SearchAdvancedAsync(helper);
    }

    /// <summary>
    /// Advanced search with optional name substring; use for deck synergy presets (subtype/keyword) without a name query.
    /// </summary>
    public async Task<Card[]> SearchCardsWithOptionsAsync(
        SearchOptions options,
        string? nameContains,
        bool inCollectionOnly,
        int limit,
        bool restrictToDeckLegalFormat,
        DeckFormat deckFormat)
    {
        var opt = options.Clone();
        if (restrictToDeckLegalFormat)
        {
            opt.UseLegalFormat = true;
            opt.LegalFormat = deckFormat;
        }

        var helper = _cardRepository.CreateSearchHelper();
        if (inCollectionOnly)
            helper.SearchMyCollection();
        else
            helper.SearchCards();

        SearchOptionsApplier.Apply(helper, opt);

        if (!string.IsNullOrWhiteSpace(nameContains))
            helper.WhereNameContains(nameContains.Trim());

        helper.OrderBy("c.name");
        if (limit > 0)
            helper.Limit(limit);
        return await _cardRepository.SearchAdvancedAsync(helper);
    }

    public MtgSearchHelper CreateSearchHelper() => _cardRepository.CreateSearchHelper();

    /// <summary>Returns all sets (code + name) for filter dropdowns, ordered by name.</summary>
    public async Task<IReadOnlyList<SetInfo>> GetAllSetsAsync() => await _cardRepository.GetAllSetsAsync();

    /// <summary>Browse metadata for Search → Sets (counts, release date, preview flag).</summary>
    public async Task<IReadOnlyList<SetBrowseRow>> GetSetsBrowseAsync() => await _cardRepository.GetSetsBrowseAsync();

    public async Task<Card[]> ExecuteSearchAsync(MtgSearchHelper searchHelper)
    {
        return await _cardRepository.SearchAdvancedAsync(searchHelper);
    }

    /// <summary>Runs a paged card search and returns the full match count without a second COUNT query (window column).</summary>
    public Task<(Card[] results, int totalCount)> ExecuteSearchWithResultTotalAsync(MtgSearchHelper searchHelper) =>
        _cardRepository.SearchAdvancedWithResultTotalAsync(searchHelper);

    public async Task<int> CountAdvancedAsync(MtgSearchHelper searchHelper)
    {
        return await _cardRepository.CountAdvancedAsync(searchHelper);
    }

    /// <summary>Commander-style deck suggestions (on-device heuristics + EDHRec ordering seed).</summary>
    public Task<Card[]> GetDeckSuggestionsAsync(
        DeckEntity deck,
        CommanderArchetype archetype,
        Card? commanderCard,
        IReadOnlyList<DeckCardEntity> deckEntities,
        IReadOnlyDictionary<string, Card> cardMap,
        DeckStats deckStats,
        DeckCohesionProfile cohesionProfile,
        bool collectionOnly,
        int maxResults = 40,
        CancellationToken cancellationToken = default) =>
        DeckSuggestionService.GetSuggestionsAsync(
            _cardRepository,
            deck,
            archetype,
            commanderCard,
            deckEntities,
            cardMap,
            deckStats,
            cohesionProfile,
            collectionOnly,
            maxResults,
            cancellationToken);

    // ── Card Detail Methods ──────────────────────────────────────────

    public async Task<Card> GetCardDetailsAsync(string uuid)
    {
        return await _cardRepository.GetCardDetailsAsync(uuid);
    }

    public Task<IReadOnlyList<OtherPrintingSummary>> GetOtherPrintingsAsync(string oracleId, string currentUuid) =>
        _cardRepository.GetOtherPrintingsByOracleIdAsync(oracleId, currentUuid);

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

    public async Task AddCardToCollectionAsync(string cardUuid, int quantity = 1, bool isFoil = false, bool isEtched = false)
    {
        await _collectionRepository.AddCardAsync(cardUuid, quantity, isFoil, isEtched);
        InvalidateTotalValueCache();
        await TrySeedReferenceBaselineForRowAsync(cardUuid).ConfigureAwait(false);
    }

    public async Task AddCardsToCollectionBulkAsync(IEnumerable<(string cardUUID, int quantity, bool isFoil, bool isEtched)> cards)
    {
        await _collectionRepository.AddCardsBulkAsync(cards);
        InvalidateTotalValueCache();
        foreach (var uuid in cards.Select(static c => c.cardUUID).Where(static u => !string.IsNullOrEmpty(u)).Distinct(StringComparer.Ordinal))
            await TrySeedReferenceBaselineForRowAsync(uuid).ConfigureAwait(false);
    }

    public async Task RemoveCardFromCollectionAsync(string cardUuid)
    {
        await _collectionRepository.RemoveCardAsync(cardUuid);
        InvalidateTotalValueCache();
    }

    public async Task UpdateCardQuantityAsync(string cardUuid, int quantity, bool isFoil = false, bool isEtched = false)
    {
        await _collectionRepository.UpdateQuantityAsync(cardUuid, quantity, isFoil, isEtched);
        InvalidateTotalValueCache();
        if (quantity > 0)
            await TrySeedReferenceBaselineForRowAsync(cardUuid).ConfigureAwait(false);
    }

    /// <summary>
    /// Overwrites each collection row's stored USD baseline with the current preferred retail unit price.
    /// </summary>
    public async Task<int> RecaptureAllCollectionPriceBaselinesAsync(CancellationToken cancellationToken = default)
    {
        if (!PricePreferences.PricesDataEnabled || _priceManager == null)
            return 0;

        var entries = await _collectionRepository.GetPricingEntriesAsync().ConfigureAwait(false);
        if (entries.Count == 0)
            return 0;

        var uuids = entries.Select(static e => e.Uuid).Where(static u => !string.IsNullOrEmpty(u)).Distinct(StringComparer.Ordinal).ToArray();
        if (uuids.Length == 0)
            return 0;

        var prices = await GetCardPricesBulkAsync(uuids).ConfigureAwait(false);
        var utc = DateTime.UtcNow;
        int n = 0;
        foreach (var e in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!prices.TryGetValue(e.Uuid, out var p))
                continue;
            var unit = PriceDisplayHelper.GetNumericPrice(p, e.IsFoil, e.IsEtched);
            if (unit <= 0)
                continue;
            await _collectionRepository.SetReferenceBaselineAsync(e.Uuid, unit, utc).ConfigureAwait(false);
            n++;
        }

        return n;
    }

    private async Task TrySeedReferenceBaselineForRowAsync(string cardUuid)
    {
        if (!PricePreferences.PricesDataEnabled || _priceManager == null)
            return;
        if (string.IsNullOrEmpty(cardUuid))
            return;

        var flags = await _collectionRepository.TryGetFinishFlagsAsync(cardUuid).ConfigureAwait(false);
        if (flags is not var (isFoil, isEtched))
            return;

        var (found, priceData) = await GetCardPricesAsync(cardUuid).ConfigureAwait(false);
        if (!found)
            return;

        var unit = PriceDisplayHelper.GetNumericPrice(priceData, isFoil, isEtched);
        if (unit <= 0)
            return;

        await _collectionRepository.TrySetReferenceBaselineIfMissingAsync(cardUuid, unit, DateTime.UtcNow).ConfigureAwait(false);
    }

    public async Task<bool> IsInCollectionAsync(string cardUuid)
    {
        return await _collectionRepository.IsInCollectionAsync(cardUuid);
    }

    public async Task<int> GetQuantityAsync(string cardUuid)
    {
        return await _collectionRepository.GetQuantityAsync(cardUuid);
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
        if (!PricePreferences.PricesDataEnabled || !PricePreferences.CollectionPriceDisplayEnabled)
            return 0;

        if (_priceManager == null) return 0;

        var vendorPriority = PriceDisplayHelper.GetVendorPriority();
        var vendorKey = string.Join(",", vendorPriority.Select(v => v.ToString()));

        if (DateTime.UtcNow < _totalValueCacheExpiry &&
            string.Equals(vendorKey, _cachedTotalValueVendorKey, StringComparison.Ordinal))
            return _cachedTotalValue;

        var total = await _priceManager.GetCollectionTotalValueAsync(vendorPriority);

        _cachedTotalValue = total;
        _totalValueCacheExpiry = DateTime.UtcNow.Add(TotalValueCacheTtl);
        _cachedTotalValueVendorKey = vendorKey;
        return total;
    }

    /// <summary>
    /// Preloads collection total value and per-UUID price map during splash so Stats and price-sort avoid cold queries.
    /// </summary>
    public async Task WarmCollectionPriceCachesAsync()
    {
        if (!PricePreferences.PricesDataEnabled || !PricePreferences.CollectionPriceDisplayEnabled || _priceManager == null)
            return;

        var stats = await GetCollectionStatsAsync().ConfigureAwait(false);
        if (stats.TotalCards == 0)
        {
            CommitWarmCollectionPrices(new Dictionary<string, CardPriceData>(StringComparer.Ordinal), CollectionVersion, VendorKeyForWarmCache());
            await GetCollectionTotalValueAsync().ConfigureAwait(false);
            return;
        }

        var versionStart = CollectionVersion;
        var vendorKeyStart = VendorKeyForWarmCache();

        var totalTask = GetCollectionTotalValueAsync();
        var mapTask = GetCollectionCardPricesAsync();
        await Task.WhenAll(totalTask, mapTask).ConfigureAwait(false);

        if (CollectionVersion != versionStart || !string.Equals(VendorKeyForWarmCache(), vendorKeyStart, StringComparison.Ordinal))
            return;

        CommitWarmCollectionPrices(mapTask.Result, versionStart, vendorKeyStart);
    }

    /// <summary>
    /// If startup prefetch matches the given collection version and vendor order, copies into <paramref name="target"/> (cleared first).
    /// </summary>
    public bool TryCopyWarmCollectionPricesIfCurrent(int version, string vendorKey, Dictionary<string, CardPriceData> target)
    {
        lock (_warmCollectionPricesLock)
        {
            if (_warmCollectionPrices is null
                || _warmCollectionPricesVersion != version
                || !string.Equals(_warmCollectionPricesVendorKey, vendorKey, StringComparison.Ordinal))
                return false;

            target.Clear();
            foreach (var kv in _warmCollectionPrices)
                target[kv.Key] = kv.Value;
            return true;
        }
    }

    private static string VendorKeyForWarmCache()
    {
        var p = PriceDisplayHelper.GetVendorPriority();
        return p.Length == 0 ? "" : string.Join(',', p.Select(static v => v.ToString()));
    }

    private void CommitWarmCollectionPrices(
        Dictionary<string, CardPriceData> map,
        int version,
        string vendorKey)
    {
        lock (_warmCollectionPricesLock)
        {
            _warmCollectionPrices = new Dictionary<string, CardPriceData>(map, StringComparer.Ordinal);
            _warmCollectionPricesVersion = version;
            _warmCollectionPricesVendorKey = vendorKey;
        }
    }

    private void ClearWarmCollectionPrices()
    {
        lock (_warmCollectionPricesLock)
        {
            _warmCollectionPrices = null;
            _warmCollectionPricesVersion = -1;
            _warmCollectionPricesVendorKey = "";
        }
    }

    private void InvalidateTotalValueCache()
    {
        _totalValueCacheExpiry = DateTime.MinValue;
        _cachedTotalValueVendorKey = "";
        ClearWarmCollectionPrices();
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

    /// <summary>
    /// Fetches encoded image bytes from Scryfall (use size <c>png</c> for the CDN PNG asset).
    /// </summary>
    public Task<byte[]?> DownloadCardImageBytesAsync(
        string scryfallId, string imageSize = "normal", string face = "")
    {
        return ImageService.DownloadImageBytesDirectAsync(scryfallId, imageSize, face);
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

    /// <summary>
    /// All price rows for distinct collection UUIDs in one query (for collection price sort / caching).
    /// </summary>
    public async Task<Dictionary<string, CardPriceData>> GetCollectionCardPricesAsync()
    {
        return _priceManager != null
            ? await _priceManager.GetCardPricesForCollectionAsync()
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
