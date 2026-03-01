using MTGFetchMAUI.Services;
using MTGFetchMAUI.ViewModels;

namespace MTGFetchMAUI.Pages;

public partial class StatsPage : ContentPage
{
    private readonly StatsViewModel _viewModel;
    private readonly CardManager _cardManager;
    private bool _loaded;

    public StatsPage(StatsViewModel viewModel, CardManager cardManager)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _cardManager = cardManager;
        BindingContext = _viewModel;

        // Subscribe to database events once
        _cardManager.OnProgress += (msg, pct) =>
        {
            if (DownloadProgress.IsVisible)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DownloadProgress.Progress = pct / 100.0;
                    DownloadStatusLabel.Text = msg;
                });
            }
        };

        _cardManager.OnDatabaseReady += () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DbStatusLabel.Text = "Connected";
                DbStatusLabel.TextColor = Color.FromArgb("#4CAF50");
                DownloadProgress.IsVisible = false;
                DownloadStatusLabel.Text = "Download complete!";
                CancelDownloadBtn.IsVisible = false;
            });
        };

        _cardManager.OnDatabaseError += success =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DbStatusLabel.Text = "Download failed";
                DbStatusLabel.TextColor = Color.FromArgb("#F44336");
                DownloadDbBtn.IsVisible = true;
                DownloadProgress.IsVisible = false;
                DownloadStatusLabel.Text = "Download failed. Please try again.";
                CancelDownloadBtn.IsVisible = false;
            });
        };

        _cardManager.OnDownloadCancelled += () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DownloadProgress.IsVisible = false;
                CancelDownloadBtn.IsVisible = false;
                DownloadDbBtn.IsVisible = true;
                DownloadStatusLabel.Text = "Download cancelled.";
            });
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Check database status
        if (_cardManager.DatabaseManager.IsConnected)
        {
            DbStatusLabel.Text = "Connected";
            DbStatusLabel.TextColor = Color.FromArgb("#4CAF50");
            DownloadDbBtn.IsVisible = false;
        }
        else if (!AppDataManager.MTGDatabaseExists())
        {
            DbStatusLabel.Text = "Database not downloaded";
            DbStatusLabel.TextColor = Color.FromArgb("#F44336");
            DownloadDbBtn.IsVisible = true;
        }
        else
        {
            DbStatusLabel.Text = "Database exists but not connected";
            DbStatusLabel.TextColor = Color.FromArgb("#FFC107");
        }

        if (!_loaded || _cardManager.DatabaseManager.IsConnected)
        {
            _loaded = true;
            await _viewModel.LoadStatsAsync();
            UpdateStatsUI();
        }
    }

    private void UpdateStatsUI()
    {
        var stats = _viewModel.Stats;
        TotalCardsLabel.Text = stats.TotalCards.ToString();
        UniqueCardsLabel.Text = stats.UniqueCards.ToString();
        AvgCMCLabel.Text = stats.AvgCMC.ToString("F2");
        FoilCountLabel.Text = stats.FoilCount.ToString();

        CreatureCountLabel.Text = stats.CreatureCount.ToString();
        SpellCountLabel.Text = stats.SpellCount.ToString();
        LandCountLabel.Text = stats.LandCount.ToString();

        CommonLabel.Text = stats.CommonCount.ToString();
        UncommonLabel.Text = stats.UncommonCount.ToString();
        RareLabel.Text = stats.RareCount.ToString();
        MythicLabel.Text = stats.MythicCount.ToString();

        CacheStatsLabel.Text = _viewModel.CacheStats;
    }

    private void OnDownloadDbClicked(object? sender, EventArgs e)
    {
        DownloadDbBtn.IsVisible = false;
        DownloadProgress.IsVisible = true;
        DownloadStatusLabel.IsVisible = true;
        CancelDownloadBtn.IsVisible = true;

        _cardManager.DownloadDatabase();
    }

    private void OnCancelDownloadClicked(object? sender, EventArgs e)
    {
        _cardManager.CancelDownload();
    }

    private async void OnClearCacheClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlertAsync("Clear Cache", "Clear all cached card images?", "Yes", "No");
        if (!confirm) return;

        await _viewModel.ClearCacheCommand.ExecuteAsync(null);
        await _viewModel.LoadStatsAsync();
        UpdateStatsUI();
    }
}
