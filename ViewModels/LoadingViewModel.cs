using AetherVault.Pages;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for the initial loading/splash screen. Runs CardManager.InitializeAsync (download DB if needed, connect),
/// shows progress and tips, then navigates to AppShell when ready. Retry command restarts the download if it failed.
/// </summary>
public partial class LoadingViewModel : BaseViewModel
{
    /// <summary>
    /// Caps how long <see cref="FinalizeStartupAsync"/> waits for splash entrance animations.
    /// After a long DB download, the animation task should already have finished; if it never completes
    /// (platform animation stall), we must not block leaving the loading screen forever.
    /// </summary>
    private static readonly TimeSpan MinimumDisplayWaitCap = TimeSpan.FromSeconds(6);

    private readonly CardManager _cardManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _tipCts;

    private static readonly string[] LoadingTips =
    [
        "Magic fact: The first Magic: The Gathering set, Alpha, was released in 1993.",
        "Tip: In Commander, your deck (including your commander) must contain exactly 100 cards.",
        "Magic fact: The five colors of Magic are white, blue, black, red, and green—often abbreviated as WUBRG.",
        "Tip: A good limited deck usually runs around 17 lands in a 40-card build.",
        "Magic fact: The color pie helps keep the game balanced by giving each color its own strengths and weaknesses.",
        "Tip: In Commander, you can only include cards that match your commander’s color identity.",
        "Magic fact: The legendary card \"Black Lotus\" is part of the original Power Nine.",
        "Tip: Removal spells are as important as powerful creatures when building a deck.",
        "Magic fact: Evergreen keywords like flying, trample, and lifelink appear in most sets.",
        "Tip: Try to keep your mana curve low so you can spend your mana efficiently every turn.",
        "Magic fact: Basic lands—Plains, Island, Swamp, Mountain, and Forest—are the only cards you can play any number of in constructed formats.",
        "Tip: When in doubt during combat, think through blocks from your opponent’s perspective first.",
        "Magic fact: The Commander format was originally known as Elder Dragon Highlander (EDH).",
        "Tip: In multiplayer games, card advantage and politics can matter more than early damage.",
        "Magic fact: Double-faced cards have been used for mechanics like transform, daybound/nightbound, and modal double-faced cards."
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
            Logger.LogStuff("LoadingViewModel.InitAsync: startup already in progress; skipping.", LogLevel.Warning);
            return;
        }

        try
        {
            // If the DB is already connected (e.g. back-stack recreation after a successful
            // init), navigate straight to the shell — no re-initialization needed.
            if (_cardManager.DatabaseManager.IsConnected)
            {
                MainThread.BeginInvokeOnMainThread(() => SwitchToShellWithToastOverlay());
                return;
            }

            StatusMessage = UserMessages.CheckingDatabase;
            ShowRetry = false;
            StatusIsError = false;
            Progress = 0;

            // Ensure disconnected before checking/downloading to avoid locks.
            // Unlikely to be connected at this point, but safe practice.
            if (_cardManager.DatabaseManager.IsConnected)
                await _cardManager.DisconnectAsync();

            // Run file/DB I/O on thread pool to avoid blocking the main thread and causing ANR.
            bool dbExists = await Task.Run(AppDataManager.MtgDatabaseExists);

            if (dbExists)
            {
                var isValid = await Task.Run(async () => await AppDataManager.EnsureMtgDatabaseValidForStartupAsync());
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
            if (Application.Current?.Windows.Count > 0)
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
            await FinalizeStartupAsync();
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
                    await FinalizeStartupAsync();
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

    private async Task FinalizeStartupAsync()
    {
        StatusMessage = UserMessages.Initializing;
        IsBusy = true;
        StopTipLoop();

        // Ensure the entrance animation has finished before navigating away (usually already done
        // while the download ran). Cap the wait so a stalled FadeToAsync cannot trap the user here forever.
        var minDisplay = _minimumDisplayTask ?? Task.CompletedTask;
        if (!minDisplay.IsCompleted)
        {
            var finished = await Task.WhenAny(minDisplay, Task.Delay(MinimumDisplayWaitCap)).ConfigureAwait(false);
            if (finished != minDisplay)
                Logger.LogStuff(
                    $"Splash entrance animation wait exceeded {MinimumDisplayWaitCap.TotalSeconds:F0}s; continuing startup.",
                    LogLevel.Warning);
        }

        // Connect to the DB
        bool connected = await _cardManager.InitializeAsync();

        if (!connected)
        {
            StatusMessage = UserMessages.FailedToOpenDatabase;
            ShowRetry = true;
            StatusIsError = true;
            IsBusy = false;
            return;
        }

        // Preload collection before the shell so the Collection tab has data on first open.
        // CollectionViewModel.ApplyFilterAndSort runs before CardGrid exists; AttachGrid replays apply to fill the grid.
        StatusMessage = UserMessages.LoadingCollection;
        StatusIsError = false;
        var collectionVm = _serviceProvider.GetRequiredService<CollectionViewModel>();
        await collectionVm.LoadCollectionAsync();

        // Start prices in background (non-critical; app continues to function without price data)
        _ = InitializePricesSafeAsync();

        // Switch to main app and create toast overlay (deferred from CreateWindow to avoid Android startup crash).
        // Awaited so that any exception in SwitchToShellWithToastOverlay propagates to the caller
        // (LoadingPage.OnAppearing catch) rather than being silently swallowed by the dispatcher —
        // which was causing the loading screen to stay frozen after an in-app DB update.
        await MainThread.InvokeOnMainThreadAsync(SwitchToShellWithToastOverlay);

        // Check for DB updates in background after the shell is visible so the network request
        // does not block or delay startup.
        _ = CheckForUpdateAfterStartupAsync();
    }

    private async Task InitializePricesSafeAsync()
    {
        if (!PricePreferences.PricesDataEnabled)
            return;

        try
        {
            await _cardManager.InitializePricesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Price subsystem init failed: {ex.Message}", LogLevel.Error);
            // Non-fatal: the app continues to function without price data.
        }
    }

    private void SwitchToShellWithToastOverlay()
    {
        if (Application.Current == null || Application.Current.Windows.Count == 0)
            return;
        var window = Application.Current.Windows[0];
        var appShell = _serviceProvider.GetRequiredService<AppShell>();
        if (window.Page is AppShell)
            return;
        // Set shell as window page directly. MAUI requires "Parent of a Page must also be a Page",
        // so we cannot put AppShell inside a Grid. Toasts use CommunityToolkit when overlay is not set.
        window.Page = appShell;
    }
}
