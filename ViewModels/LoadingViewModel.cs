using AetherVault.Pages;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for the initial loading/splash screen. Runs CardManager.InitializeAsync (download DB if needed, connect),
/// shows progress and tips, then navigates to AppShell when ready. Retry command restarts the download if it failed.
/// </summary>
public partial class LoadingViewModel : BaseViewModel
{
    /// <summary>
    /// Caps how long <see cref="FinalizeStartupAsync"/> waits for splash entrance animations after a download.
    /// After a long DB download, the animation task should already have finished; if it never completes
    /// (platform animation stall), we must not block leaving the loading screen forever.
    /// </summary>
    private static readonly TimeSpan MinimumDisplayWaitCap = TimeSpan.FromSeconds(6);

    /// <summary>
    /// On warm launch (no download), cap splash animation wait so daily startup is not held ~1s for branding.
    /// </summary>
    private static readonly TimeSpan FastConnectAnimationCap = TimeSpan.FromMilliseconds(200);

    /// <summary>DB connect faster than this is treated as a warm launch for animation trimming.</summary>
    private static readonly TimeSpan FastConnectThreshold = TimeSpan.FromMilliseconds(300);

    private readonly CardManager _cardManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _tipCts;
    private Stopwatch? _startupTiming;

    private static readonly string[] LoadingTips =
    [
        "The first Magic: The Gathering set, Alpha, was released in 1993.",
        "In Commander, your deck (including your commander) must contain exactly 100 cards.",
        "The five colors of Magic are white, blue, black, red, and green—often abbreviated as WUBRG.",
        "A good limited deck usually runs around 17 lands in a 40-card build.",
        "The color pie helps keep the game balanced by giving each color its own strengths and weaknesses.",
        "In Commander, you can only include cards that match your commander’s color identity.",
        "The legendary card \"Black Lotus\" is part of the original Power Nine.",
        "Removal spells are as important as powerful creatures when building a deck.",
        "Evergreen keywords like flying, trample, and lifelink appear in most sets.",
        "Try to keep your mana curve low so you can spend your mana efficiently every turn.",
        "Basic lands—Plains, Island, Swamp, Mountain, and Forest—are the only cards you can play any number of in constructed formats.",
        "When in doubt during combat, think through blocks from your opponent’s perspective first.",
        "The Commander format was originally known as Elder Dragon Highlander (EDH).",
        "In multiplayer games, card advantage and politics can matter more than early damage.",
        "Double-faced cards have been used for mechanics like transform, daybound/nightbound, and modal double-faced cards."
    ];

    [ObservableProperty]
    private string? _tipText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    public partial double Progress { get; set; }

    /// <summary>Progress as 0–100 for display on the splash screen.</summary>
    public int ProgressPercent => (int)Math.Round(Progress * 100.0);

    [ObservableProperty]
    public partial bool ShowRetry { get; set; }

    public LoadingViewModel(CardManager cardManager, IServiceProvider serviceProvider, IDialogService dialogService)
    {
        _cardManager = cardManager;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
    }

    private Task _minimumDisplayTask = Task.CompletedTask;

    /// <summary>
    /// Called by LoadingPage with the entrance-animation Task so that FinalizeStartupAsync
    /// can await it before navigating away — preventing fast startup from cutting the animation short.
    /// </summary>
    public void SetMinimumDisplayTask(Task t) => _minimumDisplayTask = t;

    [RelayCommand]
    private async Task RetryAsync()
    {
        await StartDownloadAsync();
    }

    public async Task InitAsync()
    {
        // Cross-instance guard: only one LoadingViewModel may run the startup sequence at a time.
        // This protects against Android 14+ config-change re-entrancy where a new Activity
        // (and thus a new Transient LoadingViewModel) is created while a first-time download is
        // still in progress on the previous instance. Without this guard both instances would
        // race: the second waits on _downloadLock, then after the first finishes connecting it
        // downloads again and calls File.Delete on the file that the first just opened — crash.
        if (!_cardManager.TryBeginStartup())
        {
            Logger.LogStuff(
                "LoadingViewModel.InitAsync: startup already in progress; waiting for peer then syncing navigation.",
                LogLevel.Warning);
            await _cardManager.WaitForStartupReleasedAsync().ConfigureAwait(false);
            if (_cardManager.DatabaseManager.IsConnected)
                await MainThread.InvokeOnMainThreadAsync(SwitchToShellWithToastOverlay);
            return;
        }

        _startupTiming = Stopwatch.StartNew();

        try
        {
            // If the DB is already connected (e.g. back-stack recreation after a successful
            // init), navigate straight to the shell — no re-initialization needed.
            if (_cardManager.DatabaseManager.IsConnected)
            {
                LogStartupPhase("already_connected");
                // Await the shell swap so EndStartup() does not run first and so failures surface
                // to LoadingPage.OnAppearing (same pattern as FinalizeStartupAsync).
                await MainThread.InvokeOnMainThreadAsync(SwitchToShellWithToastOverlay);
                LogStartupPhase("shell_swap");
                _ = WarmUpAfterShellVisibleAsync();
                return;
            }

            StatusMessage = UserMessages.CheckingDatabase;
            ShowRetry = false;
            StatusIsError = false;
            Progress = 0;

            // Ensure disconnected before checking/downloading to avoid locks.
            // Unlikely to be connected at this point, but safe practice.
            if (_cardManager.DatabaseManager.IsConnected)
            {
                await _cardManager.PrepareForMtgDatabaseReplacementAsync();
                await _cardManager.DisconnectAsync();
            }

            // Run file/DB I/O on thread pool to avoid blocking the main thread and causing ANR.
            bool dbExists = await Task.Run(AppDataManager.MtgDatabaseExists);

            if (dbExists)
            {
                var isValid = await Task.Run(async () => await AppDataManager.EnsureMtgDatabaseValidForStartupAsync());
                LogStartupPhase("db_validate");
                if (isValid)
                {
                    if (AppDataManager.TryConsumePendingMtgDatabaseDownload())
                    {
                        await StartDownloadAsync();
                        return;
                    }

                    await FinalizeStartupAsync();
                }
                else
                {
                    AppDataManager.ClearPendingMtgDatabaseDownload();
                    bool redownload = await _dialogService.DisplayAlertAsync(
                        UserMessages.DatabaseErrorTitle,
                        UserMessages.DatabaseErrorMessage,
                        "Download",
                        "Cancel");
                    if (redownload)
                    {
                        await StartDownloadAsync();
                        return;
                    }

                    ShowRetry = true;
                    IsBusy = false;
                    StatusIsError = true;
                    StatusMessage = UserMessages.DatabaseCorrupted;
                }
            }
            else
            {
                AppDataManager.ClearPendingMtgDatabaseDownload();
                await StartDownloadAsync();
            }
        }
        finally
        {
            // Always release — every return path inside the try still executes this finally.
            // On the success path, SwitchToShellWithToastOverlay() has been enqueued on the
            // main thread dispatch queue; any subsequent VM instance will see IsConnected==true
            // and navigate to the already-shown shell.
            _cardManager.EndStartup();
        }
    }

    private void LogStartupPhase(string phase)
    {
        if (_startupTiming == null)
            return;

        Logger.LogStuff($"[Startup] phase={phase} ms={_startupTiming.ElapsedMilliseconds}", LogLevel.Info);
    }

    private async Task<(bool updateAvailable, string localVersion, string remoteVersion)> CheckForUpdateSafeAsync()
    {
        try
        {
            return await AppDataManager.CheckForDatabaseUpdateAsync();
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Update check failed: {ex.Message}", LogLevel.Warning);
            return (false, string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// Runs after the shell is visible. If a new DB version is found, prompts the user and
    /// navigates back to the loading screen to download it (reusing the full startup/download flow).
    /// </summary>
    private async Task CheckForUpdateAfterStartupAsync()
    {
        var (updateAvailable, _, remoteVersion) = await CheckForUpdateSafeAsync();
        if (!updateAvailable) return;

        bool shouldUpdate = await MainThread.InvokeOnMainThreadAsync(() =>
            _dialogService.DisplayAlertAsync(
                UserMessages.UpdateAvailableTitle,
                UserMessages.UpdateAvailableMessage(remoteVersion),
                "Yes",
                "No"));

        if (!shouldUpdate) return;

        // Stop singleton grids/queries before disconnect so ConnectionLock can drain safely.
        await _cardManager.PrepareForMtgDatabaseReplacementAsync();

        // Disconnect so ExtractDatabase can replace the file; keep the existing DB on disk until
        // DownloadDatabaseAsync succeeds (atomic swap). Pending flag forces InitAsync to download
        // even though MtgDatabaseExists — without deleting first, download failure still offers
        // "use existing database" in StartDownloadAsync.
        await _cardManager.DisconnectAsync();
        AppDataManager.RequestPendingMtgDatabaseDownload();

        // Navigate back to the loading screen — its OnAppearing will kick off the download flow.
        // Await the dispatcher so the swap is applied before this method returns; BeginInvoke-only
        // ordering was racy with lifecycle and could strand a fresh LoadingPage without a running InitAsync.
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Application.Current?.Windows.Count is not > 0)
                return;

            if (Application.Current.Windows[0].Page is AppShell oldShell)
                AppShell.DetachAllTabContent(oldShell);

            Application.Current.Windows[0].Page = _serviceProvider.GetRequiredService<LoadingPage>();
        });
    }

    private async Task StartDownloadAsync()
    {
        ShowRetry = false;
        StatusIsError = false;
        IsBusy = true;
        StartTipLoop();
        StatusMessage = UserMessages.DownloadingDatabase;
        Progress = 0;

        // Subscribe to progress
        AppDataManager.OnProgress = (msg, pct) =>
        {
            StatusMessage = msg;
            Progress = pct / 100.0;
        };

        // Use AppDataManager directly for the download task
        bool success;
        try
        {
            success = await AppDataManager.DownloadDatabaseAsync();
        }
        finally
        {
            // Always clear the static callback so this ViewModel instance is not kept
            // alive by the static field after the download completes (or fails/is abandoned).
            AppDataManager.OnProgress = null;
        }

        if (success)
        {
            await FinalizeStartupAsync(checkForUpdatesAfter: false, afterDownload: true);
        }
        else
        {
            if (AppDataManager.MtgDatabaseExists())
            {
                bool useExisting = await _dialogService.DisplayAlertAsync(UserMessages.DownloadFailedTitle,
                    UserMessages.DownloadFailedContinueMessage,
                    "Yes",
                    "Retry");
                if (useExisting)
                {
                    await FinalizeStartupAsync(checkForUpdatesAfter: false, afterDownload: true);
                    return;
                }
            }

            IsBusy = false;
            ShowRetry = true;
            StatusIsError = true;
            StatusMessage = UserMessages.DownloadFailed;
            StopTipLoop();
        }
    }

    private void StartTipLoop()
    {
        _tipCts?.Cancel();
        _tipCts?.Dispose();
        _tipCts = new CancellationTokenSource();
        var token = _tipCts.Token;

        // Set an initial tip immediately so the user sees something right away.
        SetRandomTip();

        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(8), token);
                    if (token.IsCancellationRequested)
                        break;

                    MainThread.BeginInvokeOnMainThread(SetRandomTip);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the token is cancelled.
            }
        }, token);
    }

    private void StopTipLoop()
    {
        _tipCts?.Cancel();
        _tipCts?.Dispose();
        _tipCts = null;
    }

    private void SetRandomTip()
    {
        if (LoadingTips.Length == 0)
        {
            TipText = string.Empty;
            return;
        }

        var random = Random.Shared;
        string next;

        do
        {
            next = LoadingTips[random.Next(LoadingTips.Length)];
        } while (next == TipText && LoadingTips.Length > 1);

        TipText = next;
    }

    private async Task FinalizeStartupAsync(bool checkForUpdatesAfter = true, bool afterDownload = false)
    {
        StatusMessage = UserMessages.Initializing;
        IsBusy = true;
        StopTipLoop();

        var connectSw = Stopwatch.StartNew();
        bool connected = await _cardManager.InitializeAsync();
        connectSw.Stop();
        LogStartupPhase("db_connect");

        if (!connected)
        {
            StatusMessage = UserMessages.FailedToOpenDatabase;
            ShowRetry = true;
            StatusIsError = true;
            IsBusy = false;
            return;
        }

        await WaitForMinimumDisplayAsync(afterDownload, connectSw.Elapsed);

        // Switch to main app (deferred from CreateWindow to avoid Android startup crash).
        // Awaited so that any exception in SwitchToShellWithToastOverlay propagates to the caller
        // (LoadingPage.OnAppearing catch) rather than being silently swallowed by the dispatcher —
        // which was causing the loading screen to stay frozen after an in-app DB update.
        await MainThread.InvokeOnMainThreadAsync(SwitchToShellWithToastOverlay);
        LogStartupPhase("shell_swap");

        IsBusy = false;

        // Collection + price warm-up after the shell is visible so Search is interactive immediately.
        _ = WarmUpAfterShellVisibleAsync();

        // Check for DB updates in background after the shell is visible so the network request
        // does not block or delay startup. Skip right after a successful in-app download.
        if (checkForUpdatesAfter)
            _ = CheckForUpdateAfterStartupAsync();
    }

    /// <summary>
    /// Loads collection data and optionally warms price caches without blocking navigation to AppShell.
    /// </summary>
    private async Task WarmUpAfterShellVisibleAsync()
    {
        var warmSw = Stopwatch.StartNew();
        try
        {
            var collectionVm = _serviceProvider.GetRequiredService<CollectionViewModel>();
            await collectionVm.LoadCollectionAsync();
            LogStartupPhase("collection_warmup");

            if (PricePreferences.PricesDataEnabled && PricePreferences.CollectionPriceDisplayEnabled)
            {
                try
                {
                    await _cardManager.WarmCollectionPriceCachesAsync();
                    LogStartupPhase("price_warmup");
                }
                catch (Exception ex)
                {
                    Logger.LogStuff($"Collection price prefetch failed: {ex.Message}", LogLevel.Warning);
                }
            }

            Logger.LogStuff(
                $"[Startup] background_warmup_total ms={warmSw.ElapsedMilliseconds}",
                LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Background collection warm-up failed: {ex.Message}", LogLevel.Warning);
        }
    }

    private async Task WaitForMinimumDisplayAsync(bool afterDownload, TimeSpan dbConnectElapsed)
    {
        var minDisplay = _minimumDisplayTask ?? Task.CompletedTask;
        if (minDisplay.IsCompleted)
            return;

        var useFastCap = !afterDownload && dbConnectElapsed < FastConnectThreshold;
        var cap = useFastCap ? FastConnectAnimationCap : MinimumDisplayWaitCap;

        // Do not ConfigureAwait(false) here: staying on the UI sync context keeps SwitchToShell
        // ordered predictably with the MAUI dispatcher on Android after a long DB download.
        var finished = await Task.WhenAny(minDisplay, Task.Delay(cap));
        if (finished != minDisplay)
        {
            var capLabel = useFastCap ? "fast" : "full";
            Logger.LogStuff(
                $"Splash entrance animation wait ({capLabel} cap {cap.TotalMilliseconds:F0}ms) ended before animation; continuing startup.",
                LogLevel.Debug);
        }

        if (minDisplay.IsFaulted)
        {
            var inner = minDisplay.Exception?.GetBaseException();
            Logger.LogStuff(
                $"Splash entrance animation task faulted (non-fatal): {inner?.GetType().Name}: {inner?.Message}",
                LogLevel.Warning);
        }
    }

    private void SwitchToShellWithToastOverlay()
    {
        if (Application.Current == null || Application.Current.Windows.Count == 0)
            return;
        var window = Application.Current.Windows[0];
        // Fresh AppShell (transient) so Android ShellItemRenderer/fragments are not reused after LoadingPage.
        var appShell = _serviceProvider.GetRequiredService<AppShell>();
        if (window.Page is AppShell existing && ReferenceEquals(existing, appShell))
            return;

        appShell.PrepareForWindowActivation();

        // Set shell as window page directly. MAUI requires "Parent of a Page must also be a Page",
        // so we cannot put AppShell inside a Grid. Toasts use CommunityToolkit when overlay is not set.
        window.Page = appShell;
    }
}
