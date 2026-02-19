using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;
using System.Windows.Input;

namespace MTGFetchMAUI.ViewModels;

/// <summary>
/// ViewModel for statistics page.
/// Displays collection stats and cache information.
/// </summary>
public class StatsViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private CollectionStats _stats = new();
    private string _cacheStats = "";
    private string _databaseStatus = "";

    public CollectionStats Stats
    {
        get => _stats;
        set { SetProperty(ref _stats, value); OnPropertyChanged(nameof(StatsDisplay)); }
    }

    public string CacheStats
    {
        get => _cacheStats;
        set => SetProperty(ref _cacheStats, value);
    }

    public string DatabaseStatus
    {
        get => _databaseStatus;
        set => SetProperty(ref _databaseStatus, value);
    }

    public string StatsDisplay => _stats.ToString();

    public ICommand RefreshCommand { get; }
    public ICommand ClearCacheCommand { get; }

    public StatsViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;
        RefreshCommand = new Command(async () => await LoadStatsAsync());
        ClearCacheCommand = new Command(async () => await ClearCacheAsync());
    }

    public async Task LoadStatsAsync()
    {
        if (!_cardManager.DatabaseManager.IsConnected)
        {
            DatabaseStatus = "Connecting...";
            if (!await _cardManager.InitializeAsync())
            {
                DatabaseStatus = "Database not connected";
                return;
            }
        }

        IsBusy = true;
        try
        {
            Stats = await _cardManager.GetCollectionStatsAsync();
            CacheStats = await _cardManager.GetImageCacheStatsAsync();
            DatabaseStatus = "Connected";
        }
        catch (Exception ex)
        {
            DatabaseStatus = $"Error: {ex.Message}";
            Logger.LogStuff($"Stats load error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearCacheAsync()
    {
        IsBusy = true;
        StatusMessage = "Clearing cache...";
        try
        {
            await _cardManager.ClearImageCacheAsync();
            CacheStats = await _cardManager.GetImageCacheStatsAsync();
            StatusMessage = "Cache cleared";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
