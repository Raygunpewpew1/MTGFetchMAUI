using System.Text.Json;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Coordinates price data lifecycle: checks for updates, downloads, syncs, and provides price lookups.
/// Port of TCardPriceManager from CardPriceManager.pas.
/// </summary>
public class CardPriceManager : IDisposable
{
    private readonly CardPriceDatabase _database = new();
    private readonly CardPriceSQLiteSync _sync = new();
    private Timer? _checkTimer;
    private volatile bool _isChecking;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    private const string MtgjsonMetaUrl = "https://mtgjson.com/api/v5/Meta.json";
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
        // 1. Fetch MTGJSON metadata
        var meta = await FetchMetaAsync();
        if (string.IsNullOrEmpty(meta.Date))
        {
            Logger.LogStuff("Could not fetch MTGJSON metadata.", LogLevel.Warning);
            return;
        }

        // 2. Compare with local meta date or check if DB is empty
        var localDate = GetLocalMetaDate();
        var hasData = await HasPriceDataAsync();

        if (!hasData || !meta.Date.Equals(localDate, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogStuff($"Prices out of date or missing. Local: {localDate}, Remote: {meta.Date}, HasData: {hasData}", LogLevel.Info);
            OnProgress?.Invoke("Downloading price data...", 0);
            var downloaded = await DownloadAndSyncPricesAsync();
            if (downloaded)
            {
                Logger.LogStuff("Price download and sync successful.", LogLevel.Info);
                SaveLocalMetaDate(meta.Date);
            }
            else
            {
                OnProgress?.Invoke("Price download failed.", 0);
            }
        }
        else
        {
            OnProgress?.Invoke("Prices up to date.", 100);
        }

        // 3. Check for DB version update (weekly)
        CheckDatabaseVersion(meta.Version);
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

    private async Task<bool> DownloadAndSyncPricesAsync()
    {
        await _updateLock.WaitAsync();
        try
        {
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 1)
                        OnProgress?.Invoke($"Retrying price download (attempt {attempt})...", 0);

                    if (await DoDownloadAndSyncAsync())
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
            _updateLock.Release();
        }
    }

    private async Task<bool> DoDownloadAndSyncAsync()
    {
        var zipPath = Path.Combine(AppDataManager.GetAppDataPath(), MTGConstants.FilePricesTempZip);

        try
        {
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(ResponseTimeoutSeconds));
            using var response = await client.GetAsync(MtgjsonPricesUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await contentStream.CopyToAsync(fileStream);
            }

            OnProgress?.Invoke("Syncing price data...", 80);
            await _sync.SyncFromZipAsync(zipPath);
            return true;
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
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
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
    }

    private static void SaveLocalMetaDate(string date)
    {
        var path = Path.Combine(AppDataManager.GetAppDataPath(), MetaDateFile);
        File.WriteAllText(path, date);
    }

    private static string GetLocalDbVersion()
    {
        var path = Path.Combine(AppDataManager.GetAppDataPath(), DbVersionFile);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
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
