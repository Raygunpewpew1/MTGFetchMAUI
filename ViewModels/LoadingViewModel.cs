using MTGFetchMAUI.Services;
using System.Windows.Input;

namespace MTGFetchMAUI.ViewModels;

public class LoadingViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private readonly IServiceProvider _serviceProvider;
    private double _progress;
    private bool _showRetry;

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public bool ShowRetry
    {
        get => _showRetry;
        set => SetProperty(ref _showRetry, value);
    }

    public ICommand RetryCommand { get; }

    public LoadingViewModel(CardManager cardManager, IServiceProvider serviceProvider)
    {
        _cardManager = cardManager;
        _serviceProvider = serviceProvider;
        RetryCommand = new Command(async () => await StartDownloadAsync());
    }

    public async Task InitAsync()
    {
        StatusMessage = "Checking database...";
        ShowRetry = false;
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
            // Use Windows[0].Page since MainPage might be null in some contexts
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                bool shouldUpdate = await page.DisplayAlertAsync("Update Available",
                    $"A new database version ({remoteVersion}) is available. Would you like to download it?",
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
            await FinalizeStartupAsync();
        }
        else
        {
            await StartDownloadAsync();
        }
    }

    private async Task StartDownloadAsync()
    {
        ShowRetry = false;
        IsBusy = true;
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
            StatusMessage = "Download failed. Please check your internet connection.";
        }
    }

    private async Task FinalizeStartupAsync()
    {
        StatusMessage = "Initializing...";
        IsBusy = true;

        // Connect to the DB
        bool connected = await _cardManager.InitializeAsync();

        if (!connected)
        {
            StatusMessage = "Failed to open database.";
            ShowRetry = true;
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
