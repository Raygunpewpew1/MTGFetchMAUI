using AetherVault.Controls;
using AetherVault.Core;
using AetherVault.Core.Layout;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for the Search tab. Handles search execution, pagination, and image loading for the card grid.
/// The Page binds to SearchText, SearchCommand, ClearCommand, and the grid; this class does the actual work.
/// Port of TSearchPresenter + TScrollHandler from MainUnit.Search.Custom.pas.
/// </summary>
public partial class SearchViewModel : BaseViewModel, ISearchFilterTarget
{
    private readonly CardManager _cardManager;
    private readonly IGridPriceLoadService _gridPriceLoadService;
    private readonly ISearchFiltersOpener _filtersOpener;
    private CancellationTokenSource? _searchDebounceCts;
    private int _currentPage;
    private bool _isLoadingPage;
    private CardGrid? _grid;

    // ── Bindable properties (XAML uses these via {Binding PropertyName}) ──

    [ObservableProperty]
    private string filtersSummaryText = "";

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial int TotalResults { get; set; }

    [ObservableProperty]
    public partial bool HasMorePages { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public SearchOptions CurrentOptions { get; set; } = new();

    public string FiltersButtonText
    {
        get
        {
            int count = CurrentOptions.ActiveFilterCount;
            return count > 0 ? $"Filters ({count})" : "Filters";
        }
    }

    public bool HasNonTextFilters
    {
        get
        {
            var count = CurrentOptions.ActiveFilterCount;
            if (!string.IsNullOrWhiteSpace(CurrentOptions.NameFilter))
                count--;
            return count > 0;
        }
    }

    private const int PageSize = 50;

    /// <summary>Raised when a search finishes (e.g. so the filters page can refresh).</summary>
    public event Action? SearchCompleted;

    public SearchViewModel(CardManager cardManager, IGridPriceLoadService gridPriceLoadService, ISearchFiltersOpener filtersOpener)
    {
        _cardManager = cardManager;
        _gridPriceLoadService = gridPriceLoadService;
        _filtersOpener = filtersOpener;

        // Subscribe to CardManager events for status updates
        _cardManager.OnProgress += (msg, pct) =>
        {
            MainThread.BeginInvokeOnMainThread(() => StatusMessage = msg);
        };
        _cardManager.OnPriceSyncProgress += (msg, pct) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsImportingPrices = pct < 100;
            });
        };
        _cardManager.OnDatabaseReady += () =>
        {
            MainThread.BeginInvokeOnMainThread(() => StatusMessage = UserMessages.DatabaseReady);
        };
        _cardManager.OnPricesUpdated += () =>
        {
            // If we have cards, refresh the grid to show prices
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_grid != null)
                {
                    var range = _grid.GetVisibleRange();
                    _gridPriceLoadService.LoadVisiblePrices(_grid, range.start, range.end);
                }
            });
        };
    }

    /// <summary>Called by SearchPage when the card grid is created. We need the grid reference for pagination and visible-range updates.</summary>
    public void AttachGrid(CardGrid grid)
    {
        _grid = grid;
        _grid.VisibleRangeChanged += OnVisibleRangeChanged;
    }

    // Debounce: wait 750ms after user stops typing before running search
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

    protected override void OnViewModeUpdated(ViewMode value)
    {
        if (_grid != null) _grid.ViewMode = value;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await PerformSearchAsync();
    }

    /// <summary>Clear search box, filters, grid, and reset state. Bound to the Clear button.</summary>
    [RelayCommand]
    private void Clear()
    {
        SearchText = "";
        CurrentOptions = new SearchOptions();
        _grid?.ClearCards();
        TotalResults = 0;
        HasMorePages = false;
        IsEmpty = false;
        StatusIsError = false;
        StatusMessage = UserMessages.StatusClear;
        UpdateFilterState();
        SearchCompleted?.Invoke();
    }

    [RelayCommand]
    private async Task GoToFiltersAsync()
    {
        await _filtersOpener.OpenAsync(this, _cardManager);
    }

    public async Task ApplyFiltersAndSearchAsync(SearchOptions options)
    {
        await PerformSearchAsync(options);
    }

    /// <summary>Runs the search: builds query via MTGSearchHelper, executes via CardManager, then updates the grid. Handles first page and total count.</summary>
    public async Task PerformSearchAsync(SearchOptions? options = null)
    {
        if (IsBusy) return;

        // No search term and no filters (e.g. from filters page) → prompt user
        if (string.IsNullOrWhiteSpace(SearchText) && options == null)
        {
            StatusMessage = UserMessages.EnterSearchTerm;
            return;
        }

        if (!await _cardManager.EnsureInitializedAsync())
        {
            StatusMessage = UserMessages.DatabaseNotFound;
            return;
        }

        // Ensure prices are initialized
        await _cardManager.InitializePricesAsync();

        IsBusy = true;
        IsEmpty = false;
        StatusIsError = false;
        StatusMessage = UserMessages.Searching;

        if (options != null)
        {
            CurrentOptions = options;
        }
        else
        {
            CurrentOptions.NameFilter = SearchText;
        }
        UpdateFilterState();

        _currentPage = 1;

        try
        {
            // Build parameterized SQL via the fluent helper (never concatenate user input into SQL)
            var helper = _cardManager.CreateSearchHelper();
            helper.SearchCards(CurrentOptions.IncludeTokens);
            SearchOptionsApplier.Apply(helper, CurrentOptions);
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
                countHelper.SearchCards(CurrentOptions.IncludeTokens);
                SearchOptionsApplier.Apply(countHelper, CurrentOptions);
                TotalResults = await _cardManager.GetCountAdvancedAsync(countHelper);
                HasMorePages = TotalResults > results.Length;
            }

            // Set empty state before updating grid so empty-state overlay shows (and grid hides) before grid repaints
            IsEmpty = TotalResults == 0;
            _grid?.SetCards(results);
            _cardManager.ImageService.CancelPendingDownloads();

            StatusMessage = UserMessages.FoundCards(TotalResults);
            SearchCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.SearchFailed(ex.Message);
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

    public async Task UpdateCollectionAsync(string uuid, int quantity, bool isFoil = false, bool isEtched = false)
    {
        try
        {
            await _cardManager.UpdateCardQuantityAsync(uuid, quantity, isFoil, isEtched);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to update collection: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>Loads the next page of results and appends to the grid. Called when user scrolls near the bottom.</summary>
    public async Task LoadNextPageAsync()
    {
        if (_isLoadingPage || !HasMorePages || _grid == null) return;

        _isLoadingPage = true;
        _currentPage++;

        try
        {
            var helper = _cardManager.CreateSearchHelper();
            helper.SearchCards(CurrentOptions.IncludeTokens);
            SearchOptionsApplier.Apply(helper, CurrentOptions);
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
        _gridPriceLoadService.LoadVisiblePrices(_grid, start, end);
    }

    private void UpdateFilterState()
    {
        FiltersSummaryText = BuildFiltersSummary(CurrentOptions);
        OnPropertyChanged(nameof(FiltersButtonText));
        OnPropertyChanged(nameof(HasNonTextFilters));
    }

    private static string BuildFiltersSummary(SearchOptions options)
    {
        var parts = new List<string>();
        AddTextAndTypeSummary(parts, options);
        AddColorAndRaritySummary(parts, options);
        AddCMCSummary(parts, options);
        AddPowerToughnessSummary(parts, options);
        AddFormatSetArtistSummary(parts, options);
        AddSpecialSummary(parts, options);

        if (parts.Count == 0)
            return string.Empty;

        var summary = string.Join(" • ", parts);
        return summary.Length <= 120 ? summary : summary[..120] + "…";
    }

    private static void AddTextAndTypeSummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TextFilter))
            parts.Add($"Text: \"{options.TextFilter}\"");

        if (!string.IsNullOrWhiteSpace(options.TypeFilter) &&
            !options.TypeFilter.Equals("Any", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Type: {options.TypeFilter}");

        if (!string.IsNullOrWhiteSpace(options.SubtypeFilter))
            parts.Add($"Subtype: {options.SubtypeFilter}");

        if (!string.IsNullOrWhiteSpace(options.SupertypeFilter))
            parts.Add($"Supertype: {options.SupertypeFilter}");
    }

    private static void AddColorAndRaritySummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ColorFilter))
            parts.Add($"Colors: {options.ColorFilter.Replace(",", "").Replace(" ", "")}");

        if (options.RarityFilter.Count > 0)
            parts.Add($"Rarity: {string.Join("/", options.RarityFilter)}");
    }

    private static void AddCMCSummary(List<string> parts, SearchOptions options)
    {
        if (options.UseCMCRange)
            parts.Add($"CMC: {options.CMCMin}-{options.CMCMax}");
        else if (options.UseCMCExact)
            parts.Add($"CMC: {options.CMCExact}");
    }

    private static void AddPowerToughnessSummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PowerFilter))
            parts.Add($"Power: {options.PowerFilter}");

        if (!string.IsNullOrWhiteSpace(options.ToughnessFilter))
            parts.Add($"Toughness: {options.ToughnessFilter}");
    }

    private static void AddFormatSetArtistSummary(List<string> parts, SearchOptions options)
    {
        if (options.UseLegalFormat)
            parts.Add($"Format: {options.LegalFormat}");

        if (!string.IsNullOrWhiteSpace(options.SetFilter))
            parts.Add($"Set: {options.SetFilter}");

        if (!string.IsNullOrWhiteSpace(options.ArtistFilter))
            parts.Add($"Artist: {options.ArtistFilter}");
    }

    private static void AddSpecialSummary(List<string> parts, SearchOptions options)
    {
        if (options.NoVariations)
            parts.Add("No variations");

        if (options.IncludeTokens)
            parts.Add("Include tokens");
    }
}
