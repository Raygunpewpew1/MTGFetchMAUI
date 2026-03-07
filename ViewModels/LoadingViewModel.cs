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

    public LoadingViewModel(CardManager cardManager, IServiceProvider serviceProvider)
    {
        _cardManager = cardManager;
        _serviceProvider = serviceProvider;
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

        StatusMessage = "Checking database...";
        ShowRetry = false;
        StatusIsError = false;
        Progress = 0;

        // Ensure disconnected before checking/downloading to avoid locks
        // unlikely to be connected at startup, but safe practice
        if (_cardManager.DatabaseManager.IsConnected)
            _cardManager.Disconnect();

        // Check for updates
        bool updateAvailable = false;
        string remoteVersion = "";

        try
        {
            var updateInfo = await AppDataManager.CheckForDatabaseUpdateAsync();
            updateAvailable = updateInfo.updateAvailable;
            remoteVersion = updateInfo.remoteVersion;
        }
        catch (Exception ex)
        {
            // Silently fail update check
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
        }

        if (updateAvailable)
        {
            // Display only the TAG part to the user, not the timestamp
            var displayVersion = remoteVersion.Split('|')[0];

            // Use Windows[0].Page since MainPage might be null in some contexts
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                bool shouldUpdate = await page.DisplayAlertAsync("Update Available",
                    $"A new database version ({displayVersion}) is available. Would you like to download it?",
                    "Yes",
                    "No");

                if (shouldUpdate)
                {
                    await StartDownloadAsync();
                    return;
                }
            }
        }

        if (AppDataManager.MTGDatabaseExists())
        {
            // Run a quick integrity/sanity check on the existing DB before using it.
            var isValid = await AppDataManager.ValidateMTGDatabaseAsync();
            if (isValid)
            {
                await FinalizeStartupAsync();
            }
            else
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page != null)
                {
                    bool redownload = await page.DisplayAlertAsync(
                        "Database Error",
                        "The local card database appears to be corrupted. Download a fresh copy?",
                        "Download",
                        "Cancel");

                    if (redownload)
                    {
                        await StartDownloadAsync();
                        return;
                    }
                }

                ShowRetry = true;
                IsBusy = false;
                StatusIsError = true;
                StatusMessage = "Database is corrupted. Please retry the download.";
            }
        }
        else
        {
            await StartDownloadAsync();
        }
    }

    private async Task StartDownloadAsync()
    {
        ShowRetry = false;
        StatusIsError = false;
        IsBusy = true;
        StartTipLoop();
        StatusMessage = "Downloading database...";
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
            // If download failed but we have a database (e.g. update failed), offer to continue
            if (AppDataManager.MTGDatabaseExists())
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page != null)
                {
                    bool useExisting = await page.DisplayAlertAsync("Download Failed",
                        "Could not download the update. Continue with existing database?",
                        "Yes",
                        "Retry");

                    if (useExisting)
                    {
                        await FinalizeStartupAsync();
                        return;
                    }
                }
            }

            IsBusy = false;
            ShowRetry = true;
            StatusIsError = true;
            StatusMessage = "Download failed. Please check your internet connection.";
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
        StatusMessage = "Initializing...";
        IsBusy = true;
        StopTipLoop();

        // Connect to the DB
        bool connected = await _cardManager.InitializeAsync();

        if (!connected)
        {
            StatusMessage = "Failed to open database.";
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
