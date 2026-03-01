using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;

namespace MTGFetchMAUI.ViewModels;

/// <summary>
/// ViewModel for statistics page.
/// Displays collection stats and cache information.
/// </summary>
public partial class StatsViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsDisplay))]
    private CollectionStats _stats = new();

    [ObservableProperty]
    private string _cacheStats = "";

    [ObservableProperty]
    private string _databaseStatus = "";

    [ObservableProperty]
    private string _storageMtgDb = "—";

    [ObservableProperty]
    private string _storagePricesDb = "—";

    [ObservableProperty]
    private string _storageImageCache = "—";

    [ObservableProperty]
    private string _storageCollectionDb = "—";

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
            DatabaseStatus = "Connected";

            var (mtgDb, pricesDb, imageCache, collectionDb) = await _cardManager.GetStorageSizesAsync();
            StorageMtgDb = $"{mtgDb:F0} MB";
            StoragePricesDb = $"{pricesDb:F0} MB";
            StorageImageCache = $"{imageCache:F0} MB";
            StorageCollectionDb = $"{collectionDb:F0} MB";
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
}
