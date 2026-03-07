using Microsoft.Data.Sqlite;
using System.IO.Compression;

namespace AetherVault.Services;

/// <summary>
/// Static utility for file paths, database downloads, and app data management.
/// Port of TAppDataManager from AppDataManager.pas.
/// </summary>
public static class AppDataManager
{
    private const int ResponseTimeoutSeconds = 300;
    private const long MinValidDatabaseSize = 1_000_000; // 1 MB
    private const string VersionFile = "main_db_version.txt";

    // 1. ADDED: The Semaphore to prevent concurrent downloads
    private static readonly SemaphoreSlim _downloadLock = new SemaphoreSlim(1, 1);

    private static string? _appDataPath;

    /// <summary>
    /// Progress callback: (message, percent 0-100).
    /// </summary>
    public static Action<string, int>? OnProgress { get; set; }

    // ── Path Helpers ─────────────────────────────────────────────────

    public static string GetAppDataPath()
    {
        if (_appDataPath != null) return _appDataPath;

        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            MTGConstants.AppRootFolder);

        if (!Directory.Exists(_appDataPath))
            Directory.CreateDirectory(_appDataPath);

        return _appDataPath;
    }

    public static string GetMTGDatabasePath() =>
        Path.Combine(GetAppDataPath(), MTGConstants.FileAllPrintings);

    public static string GetCollectionDatabasePath() =>
        Path.Combine(GetAppDataPath(), MTGConstants.FileCollectionDb);

    public static string GetPricesDatabasePath() =>
        Path.Combine(GetAppDataPath(), MTGConstants.FilePricesDb);

    public static string GetLogPath() =>
        Path.Combine(GetAppDataPath(), "mtgfetch.log");

    public static string GetVersionFilePath() =>
        Path.Combine(GetAppDataPath(), VersionFile);

    // ── Database Checks ──────────────────────────────────────────────

    public static string GetLocalDatabaseVersion()
    {
        var path = GetVersionFilePath();
        try { return File.ReadAllText(path).Trim(); } catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException) { return string.Empty; }
    }

    private static void SetLocalDatabaseVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return;
        File.WriteAllText(GetVersionFilePath(), version);
    }

    public static bool MTGDatabaseExists()
    {
        var path = GetMTGDatabasePath();
        if (!File.Exists(path)) return false;
        return new FileInfo(path).Length > MinValidDatabaseSize;
    }

    /// <summary>
    /// Performs a lightweight integrity and sanity check on the MTG master database.
    /// Returns true if the database appears valid; false otherwise.
    /// </summary>
    public static async Task<bool> ValidateMTGDatabaseAsync(CancellationToken ct = default)
    {
        var path = GetMTGDatabasePath();
        if (!File.Exists(path))
            return false;

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly
            };

            await using var connection = new SqliteConnection(builder.ConnectionString);
            await connection.OpenAsync(ct);

            // 1. Run PRAGMA quick_check (validates structure without the expensive full page scan
            //    that integrity_check performs; catches corruption in the vast majority of cases)
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA quick_check;";
                var resultObj = await cmd.ExecuteScalarAsync(ct);
                var result = resultObj?.ToString() ?? string.Empty;
                if (!result.Equals("ok", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogStuff($"MTG DB quick_check failed with result: '{result}'.", LogLevel.Error);
                    return false;
                }
            }

            // 2. Sanity check: ensure 'cards' table exists
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='cards';";
                var nameObj = await cmd.ExecuteScalarAsync(ct);
                var name = nameObj?.ToString();
                if (!string.Equals(name, "cards", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogStuff("MTG DB sanity check failed: 'cards' table not found.", LogLevel.Error);
                    return false;
                }
            }

            Logger.LogStuff("MTG DB quick_check and sanity checks passed.", LogLevel.Info);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"MTG DB integrity check threw: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// Checks if a new database version is available on GitHub.
    /// Returns (updateAvailable, localVersion, remoteVersion).
    /// </summary>
    public static async Task<(bool updateAvailable, string localVersion, string remoteVersion)> CheckForDatabaseUpdateAsync()
    {
        var localVersion = GetLocalDatabaseVersion();
        var remoteVersion = await GetRemoteDatabaseVersionAsync();

        if (string.IsNullOrEmpty(remoteVersion))
            return (false, localVersion, remoteVersion); // Failed to get remote version

        // If local is empty or different, update is available.
        bool updateAvailable = string.IsNullOrEmpty(localVersion) || !localVersion.Equals(remoteVersion, StringComparison.OrdinalIgnoreCase);

        return (updateAvailable, localVersion, remoteVersion);
    }

    /// <summary>
    /// Fetches the release tag from the GitHub release redirect.
    /// Returns the tag string (e.g. "2025-03-01"), which is sufficient for version comparison
    /// since the CI/CD pipeline uses date-based tags for each weekly release.
    /// </summary>
    public static async Task<string> GetRemoteDatabaseVersionAsync()
    {
        try
        {
            // A single HEAD request with redirect following disabled lets us read the
            // Location header, which encodes the release tag in its path segments:
            // .../releases/download/<TAG>/MTG_App_DB.zip
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(15), handler);
            using var request = new HttpRequestMessage(HttpMethod.Head, MTGConstants.DatabaseDownloadUrl);
            using var response = await client.SendAsync(request);

            var location = response.Headers.Location;
            if (location != null)
            {
                var segments = location.Segments;
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    if (segments[i].TrimEnd('/').Equals("download", StringComparison.OrdinalIgnoreCase))
                    {
                        var tag = segments[i + 1].TrimEnd('/');
                        Logger.LogStuff($"Remote database version resolved to '{tag}'.", LogLevel.Info);
                        return tag;
                    }
                }
            }

            Logger.LogStuff("Remote database version check did not return a release tag.", LogLevel.Warning);
            return string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to check remote database version: {ex.Message}", LogLevel.Warning);
            return string.Empty;
        }
    }

    // ── Download ─────────────────────────────────────────────────────

    public static async Task<bool> DownloadDatabaseAsync(CancellationToken ct = default)
    {
        // 2. ADDED: Wait for the lock before doing anything
        await _downloadLock.WaitAsync(ct);

        var zipPath = Path.Combine(GetAppDataPath(), MTGConstants.FileAllPrintingsZip);
        string? remoteVersion = null;

        try
        {
            Logger.LogStuff("Starting MTG master database download.", LogLevel.Info);
            UpdateProgress("Checking version...", 0);
            // Try to get version before download to save it later
            remoteVersion = await GetRemoteDatabaseVersionAsync();

            UpdateProgress("Connecting to GitHub...", 5);

            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(ResponseTimeoutSeconds));
            using var response = await client.GetAsync(MTGConstants.DatabaseDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            // 3. FIXED: Using 'await using' ensures the stream is CLOSED before we try to unzip
            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                // Increased buffer size to 256KB for faster throughput
                var buffer = new byte[262144];
                int read;
                long bytesRead = 0;
                int lastPercent = 0;

                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;

                    if (totalBytes > 0)
                    {
                        var percent = (int)(bytesRead * 100 / totalBytes);
                        // Update UI only when percentage changes to avoid spamming the main thread
                        if (percent > lastPercent)
                        {
                            lastPercent = percent;
                            var mbRead = bytesRead / (1024.0 * 1024.0);
                            var mbTotal = totalBytes / (1024.0 * 1024.0);
                            UpdateProgress($"Downloading... {mbRead:F1}/{mbTotal:F1} MB", percent);
                        }
                    }
                }
            } // fileStream closes here automatically

            UpdateProgress("Extracting database...", 95);

            // 4. FIXED: Extract logic moved here to ensure it runs inside the lock
            await Task.Run(() => ExtractDatabase(zipPath), ct);

            UpdateProgress("Verifying database...", 97);

            var valid = await ValidateMTGDatabaseAsync(ct);
            if (!valid)
            {
                UpdateProgress("Database is corrupted after download. Please try again.", 0);

                // Best-effort cleanup of the bad DB so the next attempt starts fresh
                try
                {
                    var mtgPath = GetMTGDatabasePath();
                    if (File.Exists(mtgPath))
                    {
                        File.Delete(mtgPath);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogStuff($"Failed to delete corrupted MTG DB after failed validation: {ex.Message}", LogLevel.Warning);
                }

                return false;
            }

            UpdateProgress("Download complete.", 100);

            if (!string.IsNullOrEmpty(remoteVersion) && MTGDatabaseExists())
            {
                SetLocalDatabaseVersion(remoteVersion);
            }

            var exists = MTGDatabaseExists();
            if (exists)
            {
                var dbSize = new FileInfo(GetMTGDatabasePath()).Length / (1024.0 * 1024.0);
                Logger.LogStuff($"MTG master database download completed successfully ({dbSize:F1} MB).", LogLevel.Info);
            }

            return exists;
        }
        catch (OperationCanceledException)
        {
            UpdateProgress("Download cancelled.", 0);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Database download failed: {ex.Message}", LogLevel.Error);
            UpdateProgress($"Download failed: {ex.Message}", 0);
            return false;
        }
        finally
        {
            // Cleanup zip (non-fatal if delete fails, e.g. in use)
            try { if (File.Exists(zipPath)) File.Delete(zipPath); }
            catch (Exception ex) { Logger.LogStuff($"Cleanup: could not delete temp zip: {ex.Message}", LogLevel.Warning); }

            // 5. CRITICAL: Release the lock so the next attempt can proceed (or not)
            _downloadLock.Release();
        }
    }

    public static async Task<bool> TestDownloadAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(ResponseTimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Head, MTGConstants.DatabaseDownloadUrl);
            using var response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private static void ExtractDatabase(string zipPath)
    {
        var targetPath = GetMTGDatabasePath();

        // Use a temp file for extraction to avoid locking the real DB if the app crashes mid-extract
        var tempDbPath = targetPath + ".tmp";

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(MTGConstants.FileAllPrintings, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                throw new FileNotFoundException("AllPrintings.sqlite not found in downloaded archive.");

            // Extract to .tmp file first
            entry.ExtractToFile(tempDbPath, overwrite: true);
        }

        // Now swap the files safely
        if (File.Exists(targetPath))
        {
            try
            {
                File.Delete(targetPath);
            }
            catch (IOException)
            {
                // If we can't delete it, it's locked. We can't proceed.
                throw new IOException($"Cannot replace database. The file {targetPath} is currently in use.");
            }
        }

        File.Move(tempDbPath, targetPath);
    }

    private static void UpdateProgress(string message, int percent)
    {
        // Ensure this runs on main thread if updating UI
        MainThread.BeginInvokeOnMainThread(() => OnProgress?.Invoke(message, percent));
    }
}
