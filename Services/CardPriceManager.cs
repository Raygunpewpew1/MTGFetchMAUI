using System.Text.Json;

namespace AetherVault.Services;

/// <summary>
/// Coordinates price data lifecycle: checks for updates, downloads, syncs, and provides price lookups.
/// Port of TCardPriceManager from CardPriceManager.pas.
/// </summary>
public class CardPriceManager : IDisposable
{
    private readonly CardPriceDatabase _database = new();
    private readonly CardPriceSqLiteSync _sync = new();
    private Timer? _checkTimer;
    private volatile bool _isChecking;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    private const string MtgjsonMetaUrl = "https://mtgjson.com/api/v5/Meta.json";
    /// <summary>Full MTGJSON price zip; used only when GitHub trimmed bundle meta is unavailable.</summary>
    private const string MtgjsonPricesUrl = "https://mtgjson.com/api/v5/AllPricesToday.sqlite.zip";
    private const int ConnectionTimeoutSeconds = 15;
    private const int ResponseTimeoutSeconds = 300;
    private const int CheckIntervalMs = 12 * 60 * 60 * 1000; // 12 hours
    private const int DbCheckIntervalDays = 7;

    private const string MetaDateFile = "prices_meta_date.txt";
    private const string DbVersionFile = "db_meta_version.txt";
    private const string DbLastCheckFile = "db_last_checked.txt";

    /// <summary>Fired when price data load completes: (success, message).</summary>
    public Action<bool, string>? OnLoadComplete { get; set; }

    /// <summary>Progress callback: (message, percent).</summary>
    public Action<string, int>? OnProgress { get; set; }

    /// <summary>Fired when a new MTG database version is available.</summary>
    public Action<string>? OnDatabaseUpdateAvailable { get; set; }

    public bool DatabaseUpdateAvailable { get; private set; }
    public string RemoteDatabaseVersion { get; private set; } = "";

    /// <summary>
    /// Initializes the price database and wires up sync callbacks.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _database.EnsureDatabaseAsync();

        _sync.OnComplete = (success, count, error) =>
        {
            if (success)
                OnLoadComplete?.Invoke(true, $"Synced {count} card prices.");
            else
                OnLoadComplete?.Invoke(false, error);
        };

        _sync.OnProgress = (msg, pct) => OnProgress?.Invoke(msg, pct);
    }

    /// <summary>
    /// Checks for price and database updates in the background.
    /// </summary>
    public void CheckForUpdates()
    {
        if (_isChecking) return;
        _isChecking = true;

        _ = Task.Run(async () =>
        {
            try
            {
                OnProgress?.Invoke("Checking for price updates...", 0);
                await DoCheckAndUpdateAsync();
            }
            catch (Exception ex)
            {
                Logger.LogStuff($"Update check failed: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                _isChecking = false;
            }
        });
    }

    /// <summary>
    /// Starts a periodic timer that checks for updates every 12 hours.
    /// </summary>
    public void StartPeriodicCheck()
    {
        _checkTimer?.Dispose();
        _checkTimer = new Timer(_ => CheckForUpdates(), null, CheckIntervalMs, CheckIntervalMs);
    }

    /// <summary>
    /// Stops the periodic update timer.
    /// </summary>
    public void StopPeriodicCheck()
    {
        _checkTimer?.Dispose();
        _checkTimer = null;
    }

    /// <summary>
    /// Looks up price data for a card by UUID.
    /// </summary>
    public async Task<(bool found, CardPriceData prices)> GetCardPricesAsync(string uuid)
    {
        return await _database.GetCardPricesAsync(uuid);
    }

    /// <summary>
    /// Looks up price data for multiple cards by UUID.
    /// </summary>
    public async Task<Dictionary<string, CardPriceData>> GetCardPricesBulkAsync(IEnumerable<string> uuids)
    {
        return await _database.GetCardPricesBulkAsync(uuids);
    }

    /// <summary>
    /// All price data for cards in the user's collection in one database round trip.
    /// </summary>
    public async Task<Dictionary<string, CardPriceData>> GetCardPricesForCollectionAsync()
    {
        return await _database.GetCardPricesForCollectionAsync();
    }

    /// <summary>
    /// Computes collection total value directly in SQLite for the given vendor priority.
    /// </summary>
    public async Task<double> GetCollectionTotalValueAsync(IReadOnlyList<PriceVendor> vendorPriority)
    {
        return await _database.GetCollectionTotalValueAsync(vendorPriority);
    }

    /// <summary>
    /// Returns true if the price database has any data.
    /// </summary>
    public async Task<bool> HasPriceDataAsync()
    {
        return await _database.HasPriceDataAsync();
    }

    /// <summary>
    /// Marks the database version as current, clearing the update-available flag.
    /// </summary>
    public async Task MarkDatabaseUpdatedAsync()
    {
        var meta = await FetchMetaAsync();
        if (!string.IsNullOrEmpty(meta.Version))
            SaveLocalDbVersion(meta.Version);
        DatabaseUpdateAvailable = false;
    }

    /// <summary>
    /// Dismisses the database update notification without downloading.
    /// </summary>
    public void DismissDatabaseUpdate()
    {
        DatabaseUpdateAvailable = false;
    }

    public void Dispose()
    {
        StopPeriodicCheck();
        _database.Dispose();
        _updateLock.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Update Check Logic ───────────────────────────────────────────

    private async Task DoCheckAndUpdateAsync()
    {
        // 1. MTGJSON metadata (card DB version check + fallback price date)
        var meta = await FetchMetaAsync();
        if (string.IsNullOrEmpty(meta.Date))
            Logger.LogStuff("Could not fetch MTGJSON metadata (card DB version check may be skipped).", LogLevel.Warning);

        // 2. Price bundle: prefer CI-trimmed GitHub release; fall back to full MTGJSON zip
        var (remotePriceDate, pricesZipUrl) = await ResolvePriceDownloadSourceAsync(meta);
        if (string.IsNullOrEmpty(remotePriceDate))
        {
            Logger.LogStuff("Could not resolve remote price bundle date; skipping price update.", LogLevel.Warning);
        }
        else
        {
            var localDate = GetLocalMetaDate();
            var hasData = await HasPriceDataAsync();

            if (!hasData || !remotePriceDate.Equals(localDate, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogStuff(
                    $"Prices out of date or missing. Local: {localDate}, Remote: {remotePriceDate}, HasData: {hasData}, Zip: {pricesZipUrl}",
                    LogLevel.Info);
                OnProgress?.Invoke("Downloading price data...", 0);
                var downloaded = await DownloadAndSyncPricesAsync(pricesZipUrl);
                if (downloaded)
                {
                    Logger.LogStuff("Price download and sync successful.", LogLevel.Info);
                    SaveLocalMetaDate(remotePriceDate);
                }
                else
                {
                    OnProgress?.Invoke("Price download failed.", 0);
                }
            }
            else
            {
                OnProgress?.Invoke("Prices up to date.", 100);
                PricePreferences.SetSyncPending(false);
            }
        }

        // 3. Check for DB version update (weekly)
        if (!string.IsNullOrEmpty(meta.Version))
            CheckDatabaseVersion(meta.Version);
    }

    /// <summary>
    /// Uses <see cref="MtgConstants.PricesBundleMetaUrl"/> when present; otherwise MTGJSON meta date + full price zip.
    /// </summary>
    private static async Task<(string date, string zipUrl)> ResolvePriceDownloadSourceAsync(MetaInfo mtgjsonMeta)
    {
        try
        {
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(ConnectionTimeoutSeconds));
            using var response = await client.GetAsync(MtgConstants.PricesBundleMetaUrl);
            if (response.IsSuccessStatusCode)
            {
                var text = (await response.Content.ReadAsStringAsync()).Trim();
                if (text.Length > 0)
                {
                    Logger.LogStuff("Using trimmed price bundle from GitHub release.", LogLevel.Info);
                    return (text, MtgConstants.PricesBundleDownloadUrl);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Trimmed price bundle meta fetch failed, using MTGJSON: {ex.Message}", LogLevel.Info);
        }

        if (string.IsNullOrEmpty(mtgjsonMeta.Date))
            return ("", MtgjsonPricesUrl);

        Logger.LogStuff("Using full MTGJSON AllPricesToday zip.", LogLevel.Info);
        return (mtgjsonMeta.Date, MtgjsonPricesUrl);
    }

    private async Task<MetaInfo> FetchMetaAsync()
    {
        try
        {
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(ResponseTimeoutSeconds));
            var json = await client.GetStringAsync(MtgjsonMetaUrl);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data) && !root.TryGetProperty("meta", out data))
                data = root;

            var date = data.TryGetProperty("date", out var dateEl) ? dateEl.GetString() ?? "" : "";
            var version = data.TryGetProperty("version", out var verEl) ? verEl.GetString() ?? "" : "";

            return new MetaInfo(date, version);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"FetchMeta failed: {ex.Message}", LogLevel.Warning);
            return MetaInfo.Empty;
        }
    }

    private async Task<bool> DownloadAndSyncPricesAsync(string pricesZipUrl)
    {
        await _updateLock.WaitAsync();
        PricePreferences.SetSyncPending(true);
        try
        {
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 1)
                        OnProgress?.Invoke($"Retrying price download (attempt {attempt})...", 0);

                    if (await DoDownloadAndSyncAsync(pricesZipUrl))
                        return true;
                }
                catch (Exception ex)
                {
                    Logger.LogStuff($"Price download attempt {attempt} failed: {ex.Message}", LogLevel.Error);
                    if (attempt == maxRetries)
                    {
                        OnProgress?.Invoke($"Price download failed: {ex.Message}", 0);
                        throw;
                    }
                    await Task.Delay(2000 * attempt); // Exponential backoff
                }
            }
            return false;
        }
        finally
        {
            PricePreferences.SetSyncPending(false);
            _updateLock.Release();
        }
    }

    private async Task<bool> DoDownloadAndSyncAsync(string pricesZipUrl)
    {
        var zipPath = Path.Combine(AppDataManager.GetAppDataPath(), MtgConstants.FilePricesTempZip);

        try
        {
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(ResponseTimeoutSeconds));
            using var response = await client.GetAsync(pricesZipUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await contentStream.CopyToAsync(fileStream);
            }

            // Close the read connection so the sync has sole access to the prices DB file.
            // This prevents WAL lock contention between the two concurrent connections.
            await _database.CloseAsync();

            OnProgress?.Invoke("Syncing price data...", 80);
            await _sync.SyncFromZipAsync(zipPath);
            return true;
        }
        finally
        {
            // Reopen read connection for subsequent price lookups.
            await _database.EnsureDatabaseAsync();
            try { if (File.Exists(zipPath)) File.Delete(zipPath); }
            catch (Exception ex) { Logger.LogStuff($"Cleanup: could not delete temp zip: {ex.Message}", LogLevel.Warning); }
        }
    }

    private void CheckDatabaseVersion(string remoteVersion)
    {
        if (string.IsNullOrEmpty(remoteVersion)) return;

        var lastCheck = GetLastDbCheckDate();
        if ((DateTime.Now - lastCheck).TotalDays < DbCheckIntervalDays) return;
        SaveLastDbCheckDate(DateTime.Now);

        var localVersion = GetLocalDbVersion();
        if (!remoteVersion.Equals(localVersion, StringComparison.OrdinalIgnoreCase))
        {
            DatabaseUpdateAvailable = true;
            RemoteDatabaseVersion = remoteVersion;
            OnDatabaseUpdateAvailable?.Invoke(remoteVersion);
        }
    }

    // ── Local Meta File Helpers ──────────────────────────────────────

    private static string GetLocalMetaDate()
    {
        var path = Path.Combine(AppDataManager.GetAppDataPath(), MetaDateFile);
        try { return File.ReadAllText(path).Trim(); } catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException) { return ""; }
    }

    private static void SaveLocalMetaDate(string date)
    {
        var path = Path.Combine(AppDataManager.GetAppDataPath(), MetaDateFile);
        File.WriteAllText(path, date);
    }

    private static string GetLocalDbVersion()
    {
        var path = Path.Combine(AppDataManager.GetAppDataPath(), DbVersionFile);
        try { return File.ReadAllText(path).Trim(); } catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException) { return ""; }
    }

    private static void SaveLocalDbVersion(string version)
    {
        var path = Path.Combine(AppDataManager.GetAppDataPath(), DbVersionFile);
        File.WriteAllText(path, version);
    }

    private static DateTime GetLastDbCheckDate()
    {
        var path = Path.Combine(AppDataManager.GetAppDataPath(), DbLastCheckFile);
        if (!File.Exists(path)) return DateTime.MinValue;
        return double.TryParse(File.ReadAllText(path).Trim(), out var oaDate)
            ? DateTime.FromOADate(oaDate) : DateTime.MinValue;
    }

    private static void SaveLastDbCheckDate(DateTime date)
    {
        var path = Path.Combine(AppDataManager.GetAppDataPath(), DbLastCheckFile);
        File.WriteAllText(path, date.ToOADate().ToString());
    }
}
