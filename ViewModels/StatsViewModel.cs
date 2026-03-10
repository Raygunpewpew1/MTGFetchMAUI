using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for statistics page.
/// Displays collection stats and cache information.
/// </summary>
public partial class StatsViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private bool _storageStatsCached;

    /// <summary>True when stats need to be reloaded (e.g. collection was mutated since last load).</summary>
    public bool IsStatsStale { get; private set; } = true;

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
        _cardManager.CollectionChanged += InvalidateStats;
    }

    /// <summary>Marks stats as stale so the next OnAppearing triggers a reload.</summary>
    public void InvalidateStats()
    {
        IsStatsStale = true;
        _storageStatsCached = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        InvalidateStats();
        await LoadStatsAsync();
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        IsBusy = true;
        StatusMessage = UserMessages.ClearingCache;
        try
        {
            await _cardManager.ClearImageCacheAsync();
            CacheStats = await _cardManager.GetImageCacheStatsAsync();
            Storage.ImageCacheSize = _cardManager.ImageService.Cache.GetTotalCacheSize();
            OnPropertyChanged(nameof(Storage)); // notify UI about the total size update
            _storageStatsCached = false; // storage sizes changed after cache clear
            StatusMessage = UserMessages.CacheCleared;
        }
        catch (Exception ex)
        {
            StatusMessage = UserMessages.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task LoadStatsAsync()
    {
        // Show placeholders immediately so the page doesn't block or lag
        IsBusy = true;
        CacheStats = "…";
        Storage = new StorageStats();
        IsBusy = false;
        IsStatsStale = false;

        // Launch all three independent tasks in parallel — none depends on the others
        _ = LoadStatsInBackgroundAsync();
        _ = LoadTotalValueInBackgroundAsync();
        _ = LoadStorageAndCacheInBackgroundAsync();
        return Task.CompletedTask;
    }

    /// <summary>Refreshes only the total collection value (e.g. after changing the price vendor).</summary>
    public Task RefreshTotalValueAsync()
    {
        _ = LoadTotalValueInBackgroundAsync();
        return Task.CompletedTask;
    }

    private async Task LoadStatsInBackgroundAsync()
    {
        try
        {
            if (!await _cardManager.EnsureInitializedAsync())
            {
                MainThread.BeginInvokeOnMainThread(() => DatabaseStatus = "Database not connected");
                return;
            }

            var stats = await _cardManager.GetCollectionStatsAsync();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Stats = stats;
                DatabaseStatus = "Connected";
                OnPropertyChanged(nameof(Stats));
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Stats load error: {ex.Message}", LogLevel.Error);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DatabaseStatus = $"Error: {ex.Message}";
            });
        }
    }

    private async Task LoadTotalValueInBackgroundAsync()
    {
        try
        {
            var total = await _cardManager.GetCollectionTotalValueAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var s = Stats;
                s.TotalValue = total;
                Stats = s;
                OnPropertyChanged(nameof(Stats));
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Total value load failed: {ex.Message}", LogLevel.Warning);
        }
    }

    private async Task LoadStorageAndCacheInBackgroundAsync()
    {
        if (_storageStatsCached) return;

        try
        {
            var cacheStats = await _cardManager.GetImageCacheStatsAsync();
            var mtgSize = GetFileSize(AppDataManager.GetMTGDatabasePath());
            var collSize = GetFileSize(AppDataManager.GetCollectionDatabasePath());
            var pricesSize = GetFileSize(AppDataManager.GetPricesDatabasePath());
            var cacheSize = _cardManager.ImageService.Cache.GetTotalCacheSize();

            _storageStatsCached = true;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                CacheStats = cacheStats;
                Storage = new StorageStats
                {
                    MtgDatabaseSize = mtgSize,
                    CollectionDatabaseSize = collSize,
                    PricesDatabaseSize = pricesSize,
                    ImageCacheSize = cacheSize
                };
                OnPropertyChanged(nameof(Storage));
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Storage stats load failed: {ex.Message}", LogLevel.Warning);
            MainThread.BeginInvokeOnMainThread(() => CacheStats = "—");
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
