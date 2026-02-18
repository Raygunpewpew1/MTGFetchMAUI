using System.Collections.Concurrent;
using MTGFetchMAUI;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Log severity levels.
/// Port of TLogLevel from Logger.pas.
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// Async file-based logger with rotation.
/// Port of Logger.pas.
/// </summary>
public static class Logger
{
    private static readonly ConcurrentQueue<string> _queue = new();
    private static readonly CancellationTokenSource _cts = new();
    private static string _logFilePath = "";
    private static int _logCounter;
    private static readonly object _initLock = new();
    private static bool _initialized;

    private const int MaxLogFileSize = 100 * 1024 * 1024; // 100 MB
    private const int TrimmedLogLines = 1000;
    private const int LogCheckInterval = 100;
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly string[] LevelNames = ["DEBUG", "INFO", "WARNING", "ERROR"];

    /// <summary>
    /// Primary logging entry point.
    /// </summary>
    public static void LogStuff(string message, LogLevel level = LogLevel.Info)
    {
        EnsureInitialized();

        var threadId = Environment.CurrentManagedThreadId;
        var timestamp = DateTime.Now.ToString(TimestampFormat);
        var entry = $"[{timestamp}] [{LevelNames[(int)level]}] [Thread {threadId:D5}] {message}";

        _queue.Enqueue(entry);
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                MTGConstants.AppRootFolder);
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, "mtgfetch.log");

            _ = Task.Run(() => ProcessLoop(_cts.Token));
            _initialized = true;
        }
    }

    private static async Task ProcessLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryDequeue(out var entry))
                {
                    await File.AppendAllTextAsync(_logFilePath, entry + Environment.NewLine, ct);

                    Interlocked.Increment(ref _logCounter);
                    if (_logCounter % LogCheckInterval == 0)
                        TrimIfNeeded();
                }
                else
                {
                    await Task.Delay(50, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Logging should never crash the app
            }
        }
    }

    private static void TrimIfNeeded()
    {
        try
        {
            if (!File.Exists(_logFilePath)) return;
            var info = new FileInfo(_logFilePath);
            if (info.Length <= MaxLogFileSize) return;

            var lines = File.ReadAllLines(_logFilePath);
            var trimmed = lines.Skip(Math.Max(0, lines.Length - TrimmedLogLines));
            File.WriteAllLines(_logFilePath, trimmed);
        }
        catch
        {
            // Ignore trim failures
        }
    }

    /// <summary>
    /// Flush pending log entries and stop the background writer.
    /// </summary>
    public static void Shutdown()
    {
        _cts.Cancel();

        // Drain remaining entries
        try
        {
            while (_queue.TryDequeue(out var entry))
                File.AppendAllText(_logFilePath, entry + Environment.NewLine);
        }
        catch
        {
            // Best-effort drain
        }
    }
}
