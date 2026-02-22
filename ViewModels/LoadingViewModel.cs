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
