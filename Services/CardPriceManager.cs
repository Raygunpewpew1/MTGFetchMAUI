using System.IO.Compression;
using System.Text.Json;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Coordinates price data lifecycle: checks for updates, downloads, imports, and provides price lookups.
/// Port of TCardPriceManager from CardPriceManager.pas.
/// </summary>
public class CardPriceManager : IDisposable
{
    private readonly CardPriceDatabase _database = new();
    private readonly CardPriceImporter _importer = new();
    private Timer? _checkTimer;
    private volatile bool _isChecking;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    private const string MtgjsonMetaUrl = "https://mtgjson.com/api/v5/Meta.json";
    private const string MtgjsonPricesUrl = "https://mtgjson.com/api/v5/AllPricesToday.json.zip";
    private const int ConnectionTimeoutSeconds = 15;
    private const int ResponseTimeoutSeconds = 300;
    private const int CheckIntervalMs = 12 * 60 * 60 * 1000; // 12 hours
    private const int DbCheckIntervalDays = 7;

    private const string MetaDateFile = "prices_meta_date.txt";
    private const string DbVersionFile = "db_meta_version.txt";
    private const string DbLastCheckFile = "db_last_checked.txt";
    private const string PricesZipFile = "AllPricesToday.json.zip";

    /// <summary>Fired when price data load completes: (success, message).</summary>
    public Action<bool, string>? OnLoadComplete { get; set; }

    /// <summary>Progress callback: (message, percent).</summary>
    public Action<string, int>? OnProgress { get; set; }

    /// <summary>Fired when a new MTG database version is available.</summary>
    public Action<string>? OnDatabaseUpdateAvailable { get; set; }

    public bool DatabaseUpdateAvailable { get; private set; }
    public string RemoteDatabaseVersion { get; private set; } = "";

    /// <summary>
    /// Initializes the price database.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _database.EnsureDatabaseAsync();

        _importer.OnComplete = (success, count, error) =>
        {
            if (success)
            {
                // Delete JSON file after successful import
                var jsonPath = AppDataManager.GetPricesJsonPath();
                try { if (File.Exists(jsonPath)) File.Delete(jsonPath); } catch { }

                OnLoadComplete?.Invoke(true, $"Imported {count} card prices.");
            }
            else
            {
                OnLoadComplete?.Invoke(false, error);
            }
        };

        _importer.OnProgress = (msg, pct) => OnProgress?.Invoke(msg, pct);
    }

    /// <summary>
    /// Imports the local JSON price file if it exists.
    /// </summary>
    public void ImportDataAsync()
    {
        var jsonPath = AppDataManager.GetPricesJsonPath();
        if (File.Exists(jsonPath))
        {
            Logger.LogStuff($"Starting price import from {jsonPath}", LogLevel.Info);
            _importer.ImportAsync(jsonPath);
        }
        else
        {
            Logger.LogStuff("Price JSON file not found, skipping import.", LogLevel.Debug);
        }
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
            // New prices available or DB empty - download
            OnProgress?.Invoke("Downloading price data...", 0);
            var downloaded = await DownloadAndExtractPricesAsync();
            if (downloaded)
            {
                Logger.LogStuff("Price download and extraction successful.", LogLevel.Info);
                SaveLocalMetaDate(meta.Date);
                ImportDataAsync();
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
            using var client = CreateHttpClient();
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

    private async Task<bool> DownloadAndExtractPricesAsync()
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

                    if (await DoDownloadAndExtractAsync())
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

    private async Task<bool> DoDownloadAndExtractAsync()
    {
        var zipPath = Path.Combine(AppDataManager.GetAppDataPath(), PricesZipFile);
        var jsonPath = AppDataManager.GetPricesJsonPath();

        try
        {
            using var client = CreateHttpClient();
            using var response = await client.GetAsync(MtgjsonPricesUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            // 1. Download to ZIP file
            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            {
                await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await contentStream.CopyToAsync(fileStream);
                } // fileStream is DISPOSED here, releasing the lock
            }

            OnProgress?.Invoke("Extracting price data...", 80);

            // 2. Extract JSON from ZIP
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(jsonPath, overwrite: true);
                        break;
                    }
                }
            }

            return File.Exists(jsonPath);
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    private void CheckDatabaseVersion(string remoteVersion)
    {
        if (string.IsNullOrEmpty(remoteVersion)) return;

        // Only check weekly
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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(ResponseTimeoutSeconds)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(MTGConstants.ScryfallUserAgent);
        return client;
    }
}
