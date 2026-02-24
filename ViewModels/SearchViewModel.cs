using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Core.Layout;
using MTGFetchMAUI.Data;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;

namespace MTGFetchMAUI.ViewModels;

/// <summary>
/// ViewModel for search page. Handles search execution, pagination,
/// and image loading for the card grid.
/// Port of TSearchPresenter + TScrollHandler from MainUnit.Search.Custom.pas.
/// </summary>
public partial class SearchViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private CancellationTokenSource? _searchDebounceCts;
    private int _currentPage;
    private bool _isLoadingPage;
    private CardGrid? _grid;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _totalResults;

    [ObservableProperty]
    private bool _hasMorePages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeButtonText))]
    private ViewMode _viewMode = ViewMode.Grid;

    public SearchOptions CurrentOptions { get; private set; } = new();

    private const int PageSize = 50;

    public string ViewModeButtonText => ViewMode == ViewMode.Grid ? "☰" : "⊞";

    public event Action? SearchCompleted;

    public SearchViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;

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
                if (_grid != null)
                {
                    var range = _grid.GetVisibleRange();
                    LoadVisiblePrices(range.start, range.end);
                }
            });
        };
    }

    public void AttachGrid(CardGrid grid)
    {
        _grid = grid;
        _grid.VisibleRangeChanged += OnVisibleRangeChanged;
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        if (string.IsNullOrWhiteSpace(value) || value.Length < 3)
            return;

        Task.Delay(750, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() => SearchCommand.Execute(null));
        });
    }

    partial void OnViewModeChanged(ViewMode value)
    {
        if (_grid != null) _grid.ViewMode = value;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await PerformSearchAsync();
    }

    [RelayCommand]
    private void Clear()
    {
        SearchText = "";
        CurrentOptions = new SearchOptions();
        _grid?.ClearCards();
        TotalResults = 0;
        HasMorePages = false;
        StatusMessage = "";
        SearchCompleted?.Invoke();
    }

    [RelayCommand]
    private async Task GoToFiltersAsync()
    {
        await Shell.Current.GoToAsync("searchfilters");
    }

    [RelayCommand]
    private void ToggleViewMode()
    {
        ViewMode = ViewMode == ViewMode.Grid ? ViewMode.List : ViewMode.Grid;
    }

    public async Task PerformSearchAsync(SearchOptions? options = null)
    {
        if (IsBusy) return;

        // If no text and no options, don't search
        if (string.IsNullOrWhiteSpace(SearchText) && options == null)
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
            CurrentOptions.NameFilter = SearchText;
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
                await _grid.AddCardsAsync(results);
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
        // Infinite scroll: load next page when near bottom
        if (HasMorePages && !_isLoadingPage)
        {
            if (scrollY + viewportHeight > contentHeight - 500)
                _ = LoadNextPageAsync();
        }
    }

    private void OnVisibleRangeChanged(int start, int end)
    {
        LoadVisiblePrices(start, end);
    }

    private void LoadVisiblePrices(int start, int end)
    {
        if (_grid == null) return;

        _ = Task.Run(async () =>
        {
            var uuids = new HashSet<string>();
            for (int i = start; i <= end; i++)
            {
                var card = _grid.GetCardStateAt(i);
                if (card == null || card.PriceData != null) continue;
                uuids.Add(card.Id.Value);
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
