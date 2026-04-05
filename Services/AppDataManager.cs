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
    /// <summary>
    /// Persisted fingerprint after a successful PRAGMA quick_check; avoids re-running quick_check every cold start.
    /// Format: <c>localVersion|fileLength|lastWriteUtcTicks</c> (single line).
    /// </summary>
    private const string QuickCheckMarkerFile = "mtg_db_quick_check.marker";

    // 1. ADDED: The Semaphore to prevent concurrent downloads
    private static readonly SemaphoreSlim DownloadLock = new SemaphoreSlim(1, 1);

    /// <summary>
    /// When set, startup runs a download even if a valid DB already exists (e.g. user accepted an in-app update).
    /// Cleared after consume. Do not delete the working DB beforehand — <see cref="DownloadDatabaseAsync"/>
    /// replaces the file only after a successful extract, so the previous DB remains if download fails.
    /// </summary>
    private static int _pendingForcedMtgDownload;

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
            MtgConstants.AppRootFolder);

        if (!Directory.Exists(_appDataPath))
            Directory.CreateDirectory(_appDataPath);

        return _appDataPath;
    }

    public static string GetMtgDatabasePath() =>
        Path.Combine(GetAppDataPath(), MtgConstants.FileAllPrintings);

    public static string GetCollectionDatabasePath() =>
        Path.Combine(GetAppDataPath(), MtgConstants.FileCollectionDb);

    public static string GetPricesDatabasePath() =>
        Path.Combine(GetAppDataPath(), MtgConstants.FilePricesDb);

    public static string GetLogPath() =>
        Path.Combine(GetAppDataPath(), "mtgfetch.log");

    public static string GetVersionFilePath() =>
        Path.Combine(GetAppDataPath(), VersionFile);

    private static string GetQuickCheckMarkerPath() =>
        Path.Combine(GetAppDataPath(), QuickCheckMarkerFile);

    /// <summary>
    /// Builds the expected marker line from <see cref="GetLocalDatabaseVersion"/> and current MTG DB file metadata.
    /// Returns null if the database file is missing.
    /// </summary>
    private static string? BuildQuickCheckMarkerFingerprint()
    {
        var path = GetMtgDatabasePath();
        if (!File.Exists(path))
            return null;

        try
        {
            var fi = new FileInfo(path);
            var version = GetLocalDatabaseVersion();
            return $"{version}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when the on-disk marker matches the current DB file and recorded version tag (startup fast path).
    /// </summary>
    public static bool IsMtgDatabaseQuickCheckMarkerCurrent()
    {
        var expected = BuildQuickCheckMarkerFingerprint();
        if (expected == null)
            return false;

        try
        {
            var markerPath = GetQuickCheckMarkerPath();
            if (!File.Exists(markerPath))
                return false;

            var stored = File.ReadAllText(markerPath).Trim();
            return string.Equals(stored, expected, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the quick_check marker after a successful validation (must match current DB + version file).
    /// </summary>
    public static void WriteMtgDatabaseQuickCheckMarker()
    {
        var expected = BuildQuickCheckMarkerFingerprint();
        if (expected == null)
            return;

        try
        {
            File.WriteAllText(GetQuickCheckMarkerPath(), expected);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogStuff($"Could not write MTG DB quick_check marker: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// Removes the marker (e.g. after failed validation or corrupted DB).
    /// </summary>
    public static void ClearMtgDatabaseQuickCheckMarker()
    {
        try
        {
            var p = GetQuickCheckMarkerPath();
            if (File.Exists(p))
                File.Delete(p);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogStuff($"Could not clear MTG DB quick_check marker: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// Startup path: skips <see cref="ValidateMtgDatabaseAsync"/> when the marker matches;
    /// otherwise runs quick_check and updates or clears the marker.
    /// </summary>
    public static async Task<bool> EnsureMtgDatabaseValidForStartupAsync(CancellationToken ct = default)
    {
        if (IsMtgDatabaseQuickCheckMarkerCurrent())
        {
            Logger.LogStuff("MTG DB: quick_check skipped (validation marker matches).", LogLevel.Info);
            return true;
        }

        var ok = await ValidateMtgDatabaseAsync(ct);
        if (ok)
            WriteMtgDatabaseQuickCheckMarker();
        else
            ClearMtgDatabaseQuickCheckMarker();

        return ok;
    }

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

    public static bool MtgDatabaseExists()
    {
        var path = GetMtgDatabasePath();
        if (!File.Exists(path)) return false;
        return new FileInfo(path).Length > MinValidDatabaseSize;
    }

    public static void RequestPendingMtgDatabaseDownload() =>
        Interlocked.Exchange(ref _pendingForcedMtgDownload, 1);

    /// <summary>Returns true once if a forced download was requested; clears the flag.</summary>
    public static bool TryConsumePendingMtgDatabaseDownload() =>
        Interlocked.CompareExchange(ref _pendingForcedMtgDownload, 0, 1) == 1;

    public static void ClearPendingMtgDatabaseDownload() =>
        Interlocked.Exchange(ref _pendingForcedMtgDownload, 0);

    /// <summary>
    /// Performs a lightweight integrity and sanity check on the MTG master database.
    /// Returns true if the database appears valid; false otherwise.
    /// </summary>
    public static async Task<bool> ValidateMtgDatabaseAsync(CancellationToken ct = default)
    {
        var path = GetMtgDatabasePath();
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

            // 2. Sanity: expect MTG master `cards` table
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT 1 FROM sqlite_master WHERE type='table' AND name='cards' LIMIT 1;
                    """;
                var hasCards = await cmd.ExecuteScalarAsync(ct);
                if (hasCards is null or DBNull)
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
            // Shorter timeout to avoid ANR window (10s) when device is offline; HEAD request is quick when online.
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(8), handler);
            var headUrl = MtgConstants.DatabaseDownloadUrl;
            using var request = new HttpRequestMessage(HttpMethod.Head, headUrl);
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
        await DownloadLock.WaitAsync(ct);

        var zipName = MtgConstants.FileAllPrintingsZip;
        var zipPath = Path.Combine(GetAppDataPath(), zipName);
        var downloadUrl = MtgConstants.DatabaseDownloadUrl;
        string? remoteVersion = null;

        try
        {
            Logger.LogStuff("Starting MTG database download (full catalog).", LogLevel.Info);
            UpdateProgress("Checking version...", 0);
            // Try to get version before download to save it later
            remoteVersion = await GetRemoteDatabaseVersionAsync();

            UpdateProgress("Connecting to GitHub...", 5);

            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(ResponseTimeoutSeconds));
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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

            // Run validation off the caller's sync context so a UI-thread-affined continuation cannot
            // compete with BeginInvokeOnMainThread progress updates during quick_check on a large DB.
            var valid = await Task.Run(async () => await ValidateMtgDatabaseAsync(ct).ConfigureAwait(false), ct)
                .ConfigureAwait(false);
            if (!valid)
            {
                UpdateProgress("Database is corrupted after download. Please try again.", 0);
                ClearMtgDatabaseQuickCheckMarker();

                // Best-effort cleanup of the bad DB so the next attempt starts fresh
                try
                {
                    var mtgPath = GetMtgDatabasePath();
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

            if (!string.IsNullOrEmpty(remoteVersion) && MtgDatabaseExists())
            {
                SetLocalDatabaseVersion(remoteVersion);
            }

            // Marker must be written after version file is updated so startup fingerprint matches.
            WriteMtgDatabaseQuickCheckMarker();

            var exists = MtgDatabaseExists();
            if (exists)
            {
                var dbSize = new FileInfo(GetMtgDatabasePath()).Length / (1024.0 * 1024.0);
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
            DownloadLock.Release();
        }
    }

    public static async Task<bool> TestDownloadAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(ResponseTimeoutSeconds));
            var url = MtgConstants.DatabaseDownloadUrl;
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
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
        var targetPath = GetMtgDatabasePath();

        // Use a temp file for extraction to avoid locking the real DB if the app crashes mid-extract
        var tempDbPath = targetPath + ".tmp";
        var dbFile = MtgConstants.FileAllPrintings;

        try
        {
            if (File.Exists(tempDbPath))
                File.Delete(tempDbPath);
        }
        catch (IOException ex)
        {
            Logger.LogStuff($"Extract: could not remove stale temp DB: {ex.Message}", LogLevel.Warning);
        }

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            ZipArchiveEntry? entry = null;
            foreach (var e in archive.Entries)
            {
                if (e.Name.Equals(dbFile, StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.EndsWith(dbFile, StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    break;
                }
            }

            if (entry == null)
            {
                var sample = string.Join("; ", archive.Entries.Take(12).Select(e => e.FullName));
                Logger.LogStuff($"Extract: {dbFile} missing from zip; first entries: {sample}", LogLevel.Warning);
                throw new FileNotFoundException($"{dbFile} not found in downloaded archive.");
            }

            // Stream extract with progress — ExtractToFile can sit at 95% for a long time on large DBs
            // and looks hung; manual copy also avoids some platform-specific ExtractToFile stalls.
            using (var entryStream = entry.Open())
            using (var outStream = new FileStream(
                       tempDbPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 262_144,
                       FileOptions.SequentialScan))
            {
                var buffer = new byte[262_144];
                long written = 0;
                var length = entry.Length;
                var lastUiPct = 95;

                int read;
                while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    outStream.Write(buffer, 0, read);
                    written += read;

                    if (length > 0)
                    {
                        var sub = (int)(written * 4 / length);
                        if (sub > 4)
                            sub = 4;
                        var pct = 95 + sub;
                        if (pct > lastUiPct)
                        {
                            lastUiPct = pct;
                            var mb = written / (1024.0 * 1024.0);
                            var totalMb = length / (1024.0 * 1024.0);
                            UpdateProgress($"Extracting database... {mb:F1}/{totalMb:F1} MB", pct);
                        }
                    }
                }

                outStream.Flush(flushToDisk: true);
            }
        }

        // Now swap the files safely
        if (File.Exists(targetPath))
        {
            try
            {
                File.Delete(targetPath);
            }
            catch (IOException io)
            {
                Logger.LogStuff($"Cannot replace database (file in use): {targetPath} — {io.Message}", LogLevel.Warning);
                throw new IOException($"Cannot replace database. The file {targetPath} is currently in use.", io);
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
