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
    private string? tipText;

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

    [RelayCommand]
    private async Task RetryAsync()
    {
        await StartDownloadAsync();
    }

    public async Task InitAsync()
    {
        // Guard against re-entry on Android 14+ (Samsung S24 / One UI 7) where an
        // unhandled configuration change can recreate the activity after minimize/restore,
        // causing a fresh LoadingPage to appear while the app is already running.
        // Re-running init would call Disconnect() on the live singleton database,
        // tearing down the already-initialized app state.
        if (_cardManager.DatabaseManager.IsConnected)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current?.Windows.FirstOrDefault() is { } window
                    && window.Page is not AppShell)
                {
                    window.Page = _serviceProvider.GetRequiredService<AppShell>();
                }
            });
            return;
        }

        StatusMessage = UserMessages.CheckingDatabase;
        ShowRetry = false;
        StatusIsError = false;
        Progress = 0;

        // Ensure disconnected before checking/downloading to avoid locks
        // unlikely to be connected at startup, but safe practice
        if (_cardManager.DatabaseManager.IsConnected)
            _cardManager.Disconnect();

        bool dbExists = AppDataManager.MTGDatabaseExists();

        // Kick off the network update check and local DB validation concurrently — they
        // are independent (network I/O vs. local disk I/O) so there is no reason to sequence them.
        var updateCheckTask = CheckForUpdateSafeAsync();
        var validationTask = dbExists
            ? AppDataManager.ValidateMTGDatabaseAsync()
            : Task.FromResult(false);

        // Await the update check first — its result determines whether we even need validation.
        var (updateAvailable, _, remoteVersion) = await updateCheckTask;

        if (updateAvailable)
        {
            bool shouldUpdate = await _dialogService.DisplayAlertAsync(UserMessages.UpdateAvailableTitle,
                UserMessages.UpdateAvailableMessage(remoteVersion),
                "Yes",
                "No");
            if (shouldUpdate)
            {
                await StartDownloadAsync();
                return;
            }
        }

        if (dbExists)
        {
            // Validation was running in parallel — await the already-in-flight task.
            var isValid = await validationTask;
            if (isValid)
            {
                await FinalizeStartupAsync();
            }
            else
            {
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
            await StartDownloadAsync();
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
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            return (false, string.Empty, string.Empty);
        }
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
        bool success = await AppDataManager.DownloadDatabaseAsync();

        if (success)
        {
            await FinalizeStartupAsync();
        }
        else
        {
            if (AppDataManager.MTGDatabaseExists())
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

        // Start prices in background
        _ = _cardManager.InitializePricesAsync();

        // Switch to main app
        // We need to dispatch this to the main thread to be safe,
        // though we should already be on it if called from UI events.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Application.Current != null && Application.Current.Windows.Count > 0)
            {
                var appShell = _serviceProvider.GetRequiredService<AppShell>();
                Application.Current.Windows[0].Page = appShell;
            }
        });
    }
}
