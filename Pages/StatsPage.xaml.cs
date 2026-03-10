using AetherVault.Services;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class StatsPage : ContentPage
{
    private readonly StatsViewModel _viewModel;
    private readonly CardManager _cardManager;
    private bool _initializingPricePicker;

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

        // Refresh labels when total value or storage stats arrive from background
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StatsViewModel.Stats) or nameof(StatsViewModel.Storage) or nameof(StatsViewModel.CacheStats))
                MainThread.BeginInvokeOnMainThread(UpdateStatsUI);
        };

        InitPriceVendorPicker();
    }

    private static readonly string[] PriceVendorNames = ["TCG Player", "Cardmarket", "Card Kingdom", "ManaPool"];
    private static readonly PriceVendor[] PriceVendorValues = [PriceVendor.TCGPlayer, PriceVendor.Cardmarket, PriceVendor.CardKingdom, PriceVendor.ManaPool];

    private void InitPriceVendorPicker()
    {
        _initializingPricePicker = true;
        try
        {
            PriceVendorPicker.ItemsSource = PriceVendorNames;
            var priority = PriceDisplayHelper.GetVendorPriority();
            var first = priority.Length > 0 ? priority[0] : PriceVendor.TCGPlayer;
            var idx = Array.IndexOf(PriceVendorValues, first);
            PriceVendorPicker.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally
        {
            _initializingPricePicker = false;
        }
    }

    private async void OnPriceVendorPickerChanged(object? sender, EventArgs e)
    {
        if (_initializingPricePicker || PriceVendorPicker.SelectedIndex < 0 || PriceVendorPicker.SelectedIndex >= PriceVendorValues.Length)
            return;
        PriceDisplayHelper.SetPreferredVendor(PriceVendorValues[PriceVendorPicker.SelectedIndex]);
        await _viewModel.RefreshTotalValueAsync();
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

        if (_viewModel.IsStatsStale)
        {
            await _viewModel.LoadStatsAsync();
            UpdateStatsUI();
        }
    }

    private void UpdateStatsUI()
    {
        var stats = _viewModel.Stats;
        TotalCardsLabel.Text = stats.TotalCards.ToString();
        UniqueCardsLabel.Text = stats.UniqueCards.ToString();
        TotalValueLabel.Text = stats.TotalValue > 0 ? $"${stats.TotalValue:F2}" : "—";
        AvgCMCLabel.Text = stats.AvgCMC.ToString("F2");
        FoilCountLabel.Text = stats.FoilCount.ToString();

        CreatureCountLabel.Text = stats.CreatureCount.ToString();
        SpellCountLabel.Text = stats.SpellCount.ToString();
        LandCountLabel.Text = stats.LandCount.ToString();

        CommonLabel.Text = stats.CommonCount.ToString();
        UncommonLabel.Text = stats.UncommonCount.ToString();
        RareLabel.Text = stats.RareCount.ToString();
        MythicLabel.Text = stats.MythicCount.ToString();

        var storage = _viewModel.Storage;
        MtgDbSizeLabel.Text = FormatSize(storage.MtgDatabaseSize);
        CollectionDbSizeLabel.Text = FormatSize(storage.CollectionDatabaseSize);
        PricesDbSizeLabel.Text = FormatSize(storage.PricesDatabaseSize);
        ImageCacheSizeLabel.Text = FormatSize(storage.ImageCacheSize);
        TotalStorageSizeLabel.Text = FormatSize(storage.TotalSize);

        CacheStatsLabel.Text = _viewModel.CacheStats;
    }

    private string FormatSize(long bytes)
    {
        return (bytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
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
        bool confirm = await DisplayAlertAsync(UserMessages.ClearCacheTitle, UserMessages.ClearCacheMessage, "Yes", "No");
        if (!confirm) return;

        _viewModel.ClearCacheCommand.Execute(null);
        await Task.Delay(500);
        CacheStatsLabel.Text = _viewModel.CacheStats;
    }
}
