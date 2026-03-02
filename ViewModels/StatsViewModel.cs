using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AetherVault.Models;
using AetherVault.Services;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for statistics page.
/// Displays collection stats and cache information.
/// </summary>
public partial class StatsViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsDisplay))]
    public partial CollectionStats Stats { get; set; } = new();

    [ObservableProperty]
    public partial StorageStats Storage { get; set; } = new();

    [ObservableProperty]
    public partial string CacheStats { get; set; } = "";

    [ObservableProperty]
    public partial string DatabaseStatus { get; set; } = "";

    public string StatsDisplay => Stats.ToString();

    public StatsViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadStatsAsync();
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        IsBusy = true;
        StatusMessage = "Clearing cache...";
        try
        {
            await _cardManager.ClearImageCacheAsync();
            CacheStats = await _cardManager.GetImageCacheStatsAsync();
            Storage.ImageCacheSize = _cardManager.ImageService.Cache.GetTotalCacheSize();
            OnPropertyChanged(nameof(Storage)); // notify UI about the total size update
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

    public async Task LoadStatsAsync()
    {
        if (!await _cardManager.EnsureInitializedAsync())
        {
            DatabaseStatus = "Database not connected";
            return;
        }

        IsBusy = true;
        try
        {
            Stats = await _cardManager.GetCollectionStatsAsync();
            CacheStats = await _cardManager.GetImageCacheStatsAsync();

            // Calculate storage sizes
            Storage = new StorageStats
            {
                MtgDatabaseSize = GetFileSize(AppDataManager.GetMTGDatabasePath()),
                CollectionDatabaseSize = GetFileSize(AppDataManager.GetCollectionDatabasePath()),
                PricesDatabaseSize = GetFileSize(AppDataManager.GetPricesDatabasePath()),
                ImageCacheSize = _cardManager.ImageService.Cache.GetTotalCacheSize()
            };

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

    private long GetFileSize(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                // Ignore permissions/locking issues and just return 0
            }
        }
        return 0;
    }
}
