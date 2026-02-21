using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Data;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;
using System.Windows.Input;

namespace MTGFetchMAUI.ViewModels;

/// <summary>
/// ViewModel for search page. Handles search execution, pagination,
/// and image loading for the card grid.
/// Port of TSearchPresenter + TScrollHandler from MainUnit.Search.Custom.pas.
/// </summary>
public class SearchViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private string _searchText = "";
    private int _totalResults;
    private int _currentPage;
    private bool _isLoadingPage;
    private bool _hasMorePages;
    private MTGCardGrid? _grid;

    public SearchOptions CurrentOptions { get; private set; } = new();

    private const int PageSize = 50;
    private const int PreloadBuffer = 6;

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public int TotalResults
    {
        get => _totalResults;
        set => SetProperty(ref _totalResults, value);
    }

    public bool HasMorePages
    {
        get => _hasMorePages;
        set => SetProperty(ref _hasMorePages, value);
    }

    public ICommand SearchCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand GoToFiltersCommand { get; }

    public event Action? SearchCompleted;

    public SearchViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;
        SearchCommand = new Command(async () => await PerformSearchAsync());
        ClearCommand = new Command(ClearSearch);
        GoToFiltersCommand = new Command(async () => await Shell.Current.GoToAsync("searchfilters"));

        // Subscribe to CardManager events for status updates
        _cardManager.OnProgress += (msg, pct) =>
        {
            MainThread.BeginInvokeOnMainThread(() => StatusMessage = msg);
        };
        _cardManager.OnDatabaseReady += () =>
        {
            MainThread.BeginInvokeOnMainThread(() => StatusMessage = "Database ready");
        };
        _cardManager.OnPricesUpdated += () =>
        {
            // If we have cards, refresh the grid to show prices
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_grid != null && _grid.CardCount > 0)
                {
                    var range = _grid.GetVisibleRange();
                    LoadVisiblePrices(range.start, range.end);
                }
            });
        };
    }

    public void AttachGrid(MTGCardGrid grid)
    {
        _grid = grid;
        _grid.VisibleRangeChanged += OnVisibleRangeChanged;
    }

    public async Task PerformSearchAsync(SearchOptions? options = null)
    {
        if (IsBusy) return;

        // If no text and no options, don't search
        if (string.IsNullOrWhiteSpace(_searchText) && options == null)
        {
            StatusMessage = "Enter a search term";
            return;
        }

        if (!_cardManager.DatabaseManager.IsConnected)
        {
            StatusMessage = "Connecting to database...";
            if (!await _cardManager.InitializeAsync())
            {
                StatusMessage = "Database not found. Please download.";
                return;
            }
        }

        // Ensure prices are initialized
        await _cardManager.InitializePricesAsync();

        IsBusy = true;
        StatusMessage = "Searching...";

        if (options != null)
        {
            CurrentOptions = options;
        }
        else
        {
            CurrentOptions.NameFilter = _searchText;
        }

        _currentPage = 1;

        try
        {
            var helper = _cardManager.CreateSearchHelper();
            helper.SearchCards();
            ApplySearchOptions(helper, CurrentOptions);
            helper.OrderBy("c.name").Limit(PageSize).Offset(0);

            var results = await Task.Run(() => _cardManager.ExecuteSearchAsync(helper));

            if (results.Length < PageSize)
            {
                TotalResults = results.Length;
                HasMorePages = false;
            }
            else
            {
                // Get total count
                var countHelper = _cardManager.CreateSearchHelper();
                countHelper.SearchCards();
                ApplySearchOptions(countHelper, CurrentOptions);
                TotalResults = await _cardManager.GetCountAdvancedAsync(countHelper);
                HasMorePages = TotalResults > results.Length;
            }

            _grid?.SetCards(results);
            _cardManager.ImageService.CancelPendingDownloads();

            // Load images for initial visible cards
            await Task.Delay(50);
            LoadVisibleImages(ImageQuality.Small);

            StatusMessage = $"Found {TotalResults} cards";
            SearchCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
            Logger.LogStuff($"Search error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<Card?> GetCardDetailsAsync(string uuid)
    {
        try
        {
            return await _cardManager.GetCardDetailsAsync(uuid);
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> GetCollectionQuantityAsync(string uuid)
    {
        try
        {
            return await _cardManager.GetQuantityAsync(uuid);
        }
        catch
        {
            return 0;
        }
    }

    public async Task UpdateCollectionAsync(string uuid, int quantity)
    {
        try
        {
            await _cardManager.UpdateCardQuantityAsync(uuid, quantity);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to update collection: {ex.Message}", LogLevel.Error);
        }
    }

    public async Task LoadNextPageAsync()
    {
        if (_isLoadingPage || !HasMorePages || _grid == null) return;

        _isLoadingPage = true;
        _currentPage++;

        try
        {
            var helper = _cardManager.CreateSearchHelper();
            helper.SearchCards();
            ApplySearchOptions(helper, CurrentOptions);
            helper.OrderBy("c.name")
                  .Limit(PageSize)
                  .Offset((_currentPage - 1) * PageSize);

            // 1. Await directly. No Task.Run needed for true async I/O.
            var results = await _cardManager.ExecuteSearchAsync(helper);

            if (results.Length > 0)
            {
                // 2. Use the new chunked Add method so the UI doesn't stutter 
                // while inflating these new GridCardData objects.
                await _grid.AddCardsAsync(results, chunkSize: 50);
            }

            if (results.Length < PageSize)
            {
                HasMorePages = false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Page load error: {ex.Message}", LogLevel.Error);
            _currentPage--; // Rollback on failure so the user can try again
        }
        finally
        {
            _isLoadingPage = false;
        }
    }

    public void OnScrollChanged(float scrollY, float viewportHeight, float contentHeight)
    {
        _grid?.SetScrollOffset(scrollY);

        // Infinite scroll: load next page when near bottom
        if (HasMorePages && !_isLoadingPage)
        {
            if (scrollY + viewportHeight > contentHeight - 500)
                _ = LoadNextPageAsync();
        }
    }

    private void OnVisibleRangeChanged(int start, int end)
    {
        LoadVisibleImages(ImageQuality.Small);
        LoadVisiblePrices(start, end);

        // Delayed quality upgrade for center cards
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            LoadVisibleImages(ImageQuality.Normal);
        });
    }

    private void LoadVisiblePrices(int start, int end)
    {
        if (_grid == null) return;

        _ = Task.Run(async () =>
        {
            var uuids = new HashSet<string>();
            for (int i = start; i <= end; i++)
            {
                var card = _grid.GetCardAt(i);
                if (card == null || card.PriceData != null) continue;
                uuids.Add(card.UUID);
            }

            if (uuids.Count == 0) return;

            var pricesMap = await _cardManager.GetCardPricesBulkAsync(uuids);
            if (pricesMap.Count > 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _grid?.UpdateCardPricesBulk(pricesMap);
                });
            }
        });
    }

    public void LoadVisibleImages(ImageQuality quality)
    {
        if (_grid == null) return;

        var needed = _grid.GetCardsNeedingImages(quality);
        foreach (var (index, card) in needed)
        {
            if (string.IsNullOrEmpty(card.ScryfallId)) continue;

            string imageSize = quality switch
            {
                ImageQuality.Small => MTGConstants.ImageSizeSmall,
                ImageQuality.Normal => MTGConstants.ImageSizeNormal,
                ImageQuality.Large => MTGConstants.ImageSizeLarge,
                _ => MTGConstants.ImageSizeSmall
            };

            if (quality <= ImageQuality.Small)
                _grid.MarkLoading(card.UUID);
            else
                _grid.MarkUpgrading(card.UUID);

            string uuid = card.UUID;
            _cardManager.DownloadCardImageAsync(card.ScryfallId, (image, success) =>
            {
                if (success && image != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (_grid != null)
                            _grid.UpdateCardImage(uuid, image, quality);
                        else
                            image.Dispose();
                    });
                }
            }, imageSize);
        }
    }

    private void ClearSearch()
    {
        SearchText = "";
        CurrentOptions = new SearchOptions();
        _grid?.ClearCards();
        TotalResults = 0;
        HasMorePages = false;
        StatusMessage = "";
        SearchCompleted?.Invoke();
    }

    private static void ApplySearchOptions(MTGSearchHelper helper, SearchOptions options)
    {
        if (!string.IsNullOrEmpty(options.NameFilter))
            helper.WhereNameContains(options.NameFilter);

        if (!string.IsNullOrEmpty(options.TextFilter))
            helper.WhereTextContains(options.TextFilter);

        if (!string.IsNullOrEmpty(options.TypeFilter) &&
            !options.TypeFilter.Equals("Any", StringComparison.OrdinalIgnoreCase))
            helper.WhereType(options.TypeFilter);

        if (!string.IsNullOrEmpty(options.SubtypeFilter))
            helper.WhereSubtype(options.SubtypeFilter);

        if (!string.IsNullOrEmpty(options.SupertypeFilter))
            helper.WhereSupertype(options.SupertypeFilter);

        if (!string.IsNullOrEmpty(options.ColorFilter))
            helper.WhereColors(options.ColorFilter);

        if (options.RarityFilter.Count > 0)
            helper.WhereRarity([.. options.RarityFilter]);

        if (!string.IsNullOrEmpty(options.SetFilter))
            helper.WhereSet(options.SetFilter);

        if (options.UseCMCExact)
            helper.WhereCMC(options.CMCExact);
        else if (options.UseCMCRange)
            helper.WhereCMCBetween(options.CMCMin, options.CMCMax);

        if (!string.IsNullOrEmpty(options.PowerFilter))
            helper.WherePower(options.PowerFilter);

        if (!string.IsNullOrEmpty(options.ToughnessFilter))
            helper.WhereToughness(options.ToughnessFilter);

        if (options.UseLegalFormat)
            helper.WhereLegalIn(options.LegalFormat);

        if (!string.IsNullOrEmpty(options.ArtistFilter))
            helper.WhereArtist(options.ArtistFilter);

        if (options.PrimarySideOnly)
            helper.WherePrimarySideOnly();
        else
            helper.IncludeAllFaces();

        if (options.NoVariations)
            helper.WhereNoVariations();

        if (options.IncludeAllFaces)
            helper.IncludeAllFaces();
    }
}
