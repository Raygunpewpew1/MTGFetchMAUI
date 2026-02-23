using System.IO.Compression;

namespace MTGFetchMAUI.Services;

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

    public static string GetPricesJsonPath() =>
        Path.Combine(GetAppDataPath(), MTGConstants.FilePricesJson);

    public static string GetVersionFilePath() =>
        Path.Combine(GetAppDataPath(), VersionFile);

    // ── Database Checks ──────────────────────────────────────────────

    public static string GetLocalDatabaseVersion()
    {
        var path = GetVersionFilePath();
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
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
    /// Fetches the version tag and Last-Modified header from GitHub release assets.
    /// Returns a composite string: "TAG|LastModified".
    /// </summary>
    public static async Task<string> GetRemoteDatabaseVersionAsync()
    {
        try
        {
            // 1. Get the TAG from the redirect (GitHub releases/latest -> download/TAG)
            string tag = string.Empty;
            string lastModified = string.Empty;

            // We need a client that DOES NOT follow redirects to capture the initial location header
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(15), handler))
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, MTGConstants.DatabaseDownloadUrl);
                using var response = await client.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                    response.StatusCode == System.Net.HttpStatusCode.MovedPermanently ||
                    response.StatusCode == System.Net.HttpStatusCode.Found ||
                    response.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect)
                {
                    var location = response.Headers.Location;
                    if (location != null)
                    {
                        // URL format: .../releases/download/<TAG>/MTG_App_DB.zip
                        var segments = location.Segments;
                        for (int i = 0; i < segments.Length - 1; i++)
                        {
                            if (segments[i].TrimEnd('/').Equals("download", StringComparison.OrdinalIgnoreCase))
                            {
                                tag = segments[i + 1].TrimEnd('/');
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(tag))
                return string.Empty;

            // 2. Get the Last-Modified header from the ACTUAL file location (following redirects)
            // GitHub releases/download/TAG/file.zip -> redirects to Amazon S3 / Azure Blob
            // We use a normal client that follows redirects here.
            using (var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(15)))
            {
                // The URL we found above is likely .../releases/download/TAG/MTG_App_DB.zip
                // But we can just hit the original URL again with a normal client to follow the chain.
                // Or construct the specific tag URL if needed. Let's stick to the original URL
                // because it redirects to the specific tag, then to the blob storage.

                // However, to be precise, let's construct the tag-specific URL so we are checking the *exact* version we found.
                // But the original URL is "latest", which is what we want.

                using var request = new HttpRequestMessage(HttpMethod.Head, MTGConstants.DatabaseDownloadUrl);
                using var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.LastModified.HasValue)
                    {
                        lastModified = response.Content.Headers.LastModified.Value.UtcDateTime.ToString("O");
                    }
                }
            }

            // Return composite key: TAG + LastModified
            // If LastModified is missing (unlikely), fallback to just TAG
            if (!string.IsNullOrEmpty(lastModified))
            {
                return $"{tag}|{lastModified}";
            }

            return tag;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Failed to check remote version: {ex.Message}");
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

            UpdateProgress("Download complete.", 100);

            if (!string.IsNullOrEmpty(remoteVersion) && MTGDatabaseExists())
            {
                SetLocalDatabaseVersion(remoteVersion);
            }

            return MTGDatabaseExists();
        }
        catch (OperationCanceledException)
        {
            UpdateProgress("Download cancelled.", 0);
            return false;
        }
        catch (Exception ex)
        {
            // Logger.LogStuff($"Database download failed: {ex.Message}", LogLevel.Error);
            Console.WriteLine($"[ERROR] Database download failed: {ex.Message}");
            UpdateProgress($"Download failed: {ex.Message}", 0);
            return false;
        }
        finally
        {
            // Cleanup zip
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }

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
