using System.Collections.ObjectModel;
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
/// </summary>
public partial class SearchViewModel : BaseViewModel, ISearchFilterTarget
{
    private readonly CardManager _cardManager;
    private readonly IGridPriceLoadService _gridPriceLoadService;
    private readonly ISearchFiltersOpener _filtersOpener;
    private CancellationTokenSource? _searchDebounceCts;
    private CancellationTokenSource? _searchCts;
    private int _currentPage;
    private bool _isLoadingPage;
    private CardGrid? _grid;
    private List<SetBrowseRow> _setsBrowseCache = [];

    // ── Bindable properties ──

    [ObservableProperty]
    private string _filtersSummaryText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecentSearchesPanel))]
    [NotifyPropertyChangedFor(nameof(ShowCardGridEmptyState))]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCardGridResults))]
    [NotifyPropertyChangedFor(nameof(ShowRecentSearchesPanel))]
    [NotifyPropertyChangedFor(nameof(ShowRecentSearchesShortcut))]
    [NotifyPropertyChangedFor(nameof(ShowCardGridEmptyState))]
    public partial int TotalResults { get; set; }

    [ObservableProperty]
    public partial bool HasMorePages { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCardGridResults))]
    [NotifyPropertyChangedFor(nameof(ShowCardGridEmptyState))]
    public partial bool IsEmpty { get; set; }

    /// <summary>Card grid visible on the Cards tab when a search returned at least one card.</summary>
    public bool ShowCardGridResults => !IsSetsSearchTab && TotalResults > 0;

    /// <summary>Empty state on the Cards tab when there are no recents to show instead.</summary>
    public bool ShowCardGridEmptyState => !IsSetsSearchTab && IsEmpty && TotalResults == 0 && !ShowRecentSearchesPanel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecentSearchesPanel))]
    [NotifyPropertyChangedFor(nameof(ShowRecentSearchesShortcut))]
    [NotifyPropertyChangedFor(nameof(ShowCardGridEmptyState))]
    public partial bool HasRecentSearches { get; set; }

    /// <summary>Idle home list — not while results or a search is in flight.</summary>
    public bool ShowRecentSearchesPanel =>
        HasRecentSearches
        && !IsSetsSearchTab
        && string.IsNullOrWhiteSpace(SearchText)
        && TotalResults == 0
        && !IsBusy;

    /// <summary>Compact status-row link to pick a recent search without clearing results.</summary>
    public bool ShowRecentSearchesShortcut =>
        HasRecentSearches && !IsSetsSearchTab && TotalResults > 0;

    /// <summary>Active filter summary row hidden on the Sets tab.</summary>
    public bool ShowFiltersSummaryStrip => HasNonTextFilters && !IsSetsSearchTab;

    /// <summary>Most-recent plain name searches (no extra filters); tap to re-run.</summary>
    public ObservableCollection<string> RecentSearches { get; } = [];

    /// <summary>Sets tab: filtered rows from <see cref="_setsBrowseCache"/>.</summary>
    public ObservableCollection<SetBrowseRow> FilteredSets { get; } = [];

    [ObservableProperty]
    public partial bool IsSetsSearchTab { get; set; }

    [ObservableProperty]
    public partial string SetListFilterText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoadingSets { get; set; }

    /// <summary>Sets tab list is empty after filter (or DB returned nothing).</summary>
    [ObservableProperty]
    public partial bool SetsListIsEmpty { get; set; }

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

    /// <summary>Raised when a search finishes.</summary>
    public event Action? SearchCompleted;

    public SearchViewModel(CardManager cardManager, IGridPriceLoadService gridPriceLoadService, ISearchFiltersOpener filtersOpener)
    {
        _cardManager = cardManager;
        _gridPriceLoadService = gridPriceLoadService;
        _filtersOpener = filtersOpener;

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
        _cardManager.MtgDatabaseReplacing += OnMtgDatabaseReplacing;
        _cardManager.MtgDatabaseReplaced += OnMtgDatabaseReplaced;
        _cardManager.OnPricesUpdated += () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_grid != null)
                {
                    var range = _grid.GetVisibleRange();
                    _gridPriceLoadService.LoadVisiblePrices(_grid, range.start, range.end);
                }
            });
        };

        RefreshRecentSearches();

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
                OnPropertyChanged(nameof(ShowRecentSearchesPanel));
        };
    }

    private void OnMtgDatabaseReplacing()
    {
        _searchDebounceCts?.Cancel();
        _searchCts?.Cancel();
        _setsBrowseCache.Clear();
        FilteredSets.Clear();
        IsBusy = false;
        IsEmpty = true;
        TotalResults = 0;
        HasMorePages = false;
        _grid?.ResetForDatabaseReload();
    }

    private void OnMtgDatabaseReplaced()
    {
        _setsBrowseCache.Clear();
        FilteredSets.Clear();
    }

    /// <summary>Called by SearchPage when the card grid is created.</summary>
    public void AttachGrid(CardGrid grid)
    {
        _grid = grid;
        _grid.VisibleRangeChanged += OnVisibleRangeChanged;
    }

    // Debounce: wait 750ms after user stops typing before running search
    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowRecentSearchesPanel));
        OnPropertyChanged(nameof(ShowCardGridEmptyState));

        if (IsSetsSearchTab)
            return;

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

    partial void OnSetListFilterTextChanged(string value) => ApplySetListFilter();

    partial void OnIsSetsSearchTabChanged(bool value)
    {
        if (value && _setsBrowseCache.Count > 0)
            ApplySetListFilter();
        OnPropertyChanged(nameof(ShowRecentSearchesPanel));
        OnPropertyChanged(nameof(ShowRecentSearchesShortcut));
        OnPropertyChanged(nameof(ShowFiltersSummaryStrip));
        OnPropertyChanged(nameof(ShowCardGridEmptyState));
        OnPropertyChanged(nameof(ShowCardGridResults));
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

    /// <summary>Re-runs a saved plain-text search (cancels pending debounced search).</summary>
    [RelayCommand]
    private async Task ApplyRecentSearchAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        _searchDebounceCts?.Cancel();
        _searchDebounceCts = null;
        IsSetsSearchTab = false;
        SearchText = query.Trim();
        // Setting SearchText schedules a debounced search; cancel that — we run immediately below.
        _searchDebounceCts?.Cancel();
        await PerformSearchAsync();
    }

    /// <summary>Clear search box, filters, grid, and reset state.</summary>
    [RelayCommand]
    private void Clear()
    {
        SearchText = "";
        SetListFilterText = "";
        IsSetsSearchTab = false;
        _setsBrowseCache.Clear();
        FilteredSets.Clear();
        SetsListIsEmpty = false;
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

    /// <summary>Opens the full-screen filters page.</summary>
    [RelayCommand]
    private async Task GoToFiltersAsync()
    {
        await _filtersOpener.OpenAsync(this, _cardManager);
    }

    public async Task ApplyFiltersAndSearchAsync(SearchOptions options)
    {
        // SearchFiltersViewModel sets SearchText first, which schedules a debounced search — cancel it.
        _searchDebounceCts?.Cancel();
        IsSetsSearchTab = false;
        await PerformSearchAsync(options);
    }

    /// <summary>Search tab: Cards vs Sets (loads set list on first visit to Sets).</summary>
    [RelayCommand]
    private async Task SwitchSearchTabAsync(string? mode)
    {
        bool toSets = string.Equals(mode, "sets", StringComparison.OrdinalIgnoreCase);
        if (toSets == IsSetsSearchTab)
            return;

        IsSetsSearchTab = toSets;
        if (toSets)
            await EnsureSetsListLoadedAsync();
    }

    /// <summary>From Sets list: filter the grid to this printing code and return to the Cards tab.</summary>
    [RelayCommand]
    private async Task OpenSetFromBrowseAsync(SetBrowseRow? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Code))
            return;

        _searchDebounceCts?.Cancel();
        SearchText = "";
        SetListFilterText = "";
        IsSetsSearchTab = false;

        var o = CurrentOptions.Clone();
        o.NameFilter = "";
        o.SetFilter = row.Code;
        o.IncludeTokens = true;

        await PerformSearchAsync(o);
    }

    private async Task EnsureSetsListLoadedAsync()
    {
        if (_setsBrowseCache.Count > 0)
        {
            ApplySetListFilter();
            return;
        }

        if (!await _cardManager.EnsureInitializedAsync())
        {
            StatusMessage = UserMessages.DatabaseNotFound;
            return;
        }

        IsLoadingSets = true;
        StatusIsError = false;
        StatusMessage = UserMessages.Searching;
        try
        {
            var list = await _cardManager.GetSetsBrowseAsync();
            _setsBrowseCache = [.. list];
            ApplySetListFilter();
            StatusMessage = $"{_setsBrowseCache.Count} sets loaded";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.SearchFailed(ex.Message);
            Logger.LogStuff($"Set browse load error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsLoadingSets = false;
        }
    }

    private void ApplySetListFilter()
    {
        FilteredSets.Clear();
        var q = (SetListFilterText ?? "").Trim();
        if (_setsBrowseCache.Count == 0)
        {
            SetsListIsEmpty = true;
            return;
        }

        if (string.IsNullOrEmpty(q))
        {
            foreach (var r in _setsBrowseCache)
                FilteredSets.Add(r);
        }
        else
        {
            foreach (var r in _setsBrowseCache)
            {
                if (r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    r.Code.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredSets.Add(r);
                }
            }
        }

        SetsListIsEmpty = FilteredSets.Count == 0;
    }

    /// <summary>Runs the search: builds query via MTGSearchHelper, executes via CardManager, then updates the grid.</summary>
    public async Task PerformSearchAsync(SearchOptions? options = null)
    {
        if (IsBusy) return;

        if (options == null)
        {
            CurrentOptions.NameFilter = SearchText ?? "";
            if (string.IsNullOrWhiteSpace(CurrentOptions.NameFilter) && !CurrentOptions.HasActiveFilters)
            {
                StatusMessage = UserMessages.EnterSearchTerm;
                return;
            }
        }

        if (!await _cardManager.EnsureInitializedAsync())
        {
            StatusMessage = UserMessages.DatabaseNotFound;
            return;
        }

        await _cardManager.InitializePricesAsync();

        IsBusy = true;
        IsEmpty = false;
        StatusIsError = false;
        StatusMessage = UserMessages.Searching;

        if (options != null)
            CurrentOptions = options;

        UpdateFilterState();

        _currentPage = 1;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var searchToken = _searchCts.Token;

        try
        {
            var helper = _cardManager.CreateSearchHelper();
            helper.SearchCards(CurrentOptions.IncludeTokens);
            SearchOptionsApplier.Apply(helper, CurrentOptions);
            helper.OrderBy("c.name").Limit(PageSize).Offset(0);

            var (results, totalCount) = await Task.Run(() => _cardManager.ExecuteSearchWithResultTotalAsync(helper), searchToken);
            if (searchToken.IsCancellationRequested)
                return;

            TotalResults = totalCount;
            HasMorePages = totalCount > results.Length;

            IsEmpty = TotalResults == 0;
            _grid?.SetCards(results);
            _cardManager.ImageService.CancelPendingDownloads();

            StatusMessage = UserMessages.FoundCards(TotalResults);
            RecordRecentPlainSearchIfApplicable();
            SearchCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Search superseded or MTG DB replacement in progress.
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            if (searchToken.IsCancellationRequested)
                return;
            StatusIsError = true;
            StatusMessage = UserMessages.SearchFailed(ex.Message);
            Logger.LogStuff($"Search error: {ex.Message}", LogLevel.Error);
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.SearchFailed(ex.Message);
            Logger.LogStuff($"Search error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (!searchToken.IsCancellationRequested)
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

    /// <summary>Loads the next page of results and appends to the grid.</summary>
    public async Task LoadNextPageAsync()
    {
        if (_isLoadingPage || !HasMorePages || _grid == null) return;

        _isLoadingPage = true;
        _currentPage++;
        var searchToken = _searchCts?.Token ?? CancellationToken.None;

        try
        {
            if (searchToken.IsCancellationRequested)
                return;

            var pageHelper = _cardManager.CreateSearchHelper();
            pageHelper.SearchCards(CurrentOptions.IncludeTokens);
            SearchOptionsApplier.Apply(pageHelper, CurrentOptions);
            pageHelper.OrderBy("c.name")
                .Limit(PageSize)
                .Offset((_currentPage - 1) * PageSize);

            var results = await _cardManager.ExecuteSearchAsync(pageHelper);

            if (searchToken.IsCancellationRequested)
                return;

            if (results.Length > 0)
                await _grid.AddCardsAsync(results);

            if (results.Length < PageSize)
                HasMorePages = false;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Page load error: {ex.Message}", LogLevel.Error);
            _currentPage--;
        }
        finally
        {
            _isLoadingPage = false;
        }
    }

    public void OnScrollChanged(float scrollY, float viewportHeight, float contentHeight)
    {
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
        OnPropertyChanged(nameof(ShowFiltersSummaryStrip));
    }

    private static string BuildFiltersSummary(SearchOptions options)
    {
        var parts = new List<string>();
        AddTextAndTypeSummary(parts, options);
        AddOracleKeywordsSummary(parts, options);
        AddColorAndRaritySummary(parts, options);
        AddCmcSummary(parts, options);
        AddPowerToughnessSummary(parts, options);
        AddFormatSetArtistSummary(parts, options);
        AddAvailabilitySummary(parts, options);
        AddLayoutSummary(parts, options);
        AddFinishesSummary(parts, options);
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

    private static void AddOracleKeywordsSummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.KeywordsFilter))
            parts.Add($"Keywords: {options.KeywordsFilter}");
    }

    private static void AddColorAndRaritySummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ColorFilter))
            parts.Add($"Colors: {ColorFilterDisplay.ToDisplayString(options.ColorFilter)}");

        if (!string.IsNullOrWhiteSpace(options.ColorIdentityFilter))
            parts.Add($"Identity: {ColorFilterDisplay.ToDisplayString(options.ColorIdentityFilter)}");

        if (options.RarityFilter.Count > 0)
            parts.Add($"Rarity: {string.Join("/", options.RarityFilter)}");
    }

    private static void AddCmcSummary(List<string> parts, SearchOptions options)
    {
        if (options.UseCmcRange)
            parts.Add($"CMC: {options.CmcMin}-{options.CmcMax}");
        else if (options.UseCmcExact)
            parts.Add($"CMC: {options.CmcExact}");
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

    private static void AddAvailabilitySummary(List<string> parts, SearchOptions options)
    {
        if (options.AvailabilityFilter.Count == 0) return;
        var labels = options.AvailabilityFilter
            .Select(static t => t.ToLowerInvariant() switch
            {
                "paper" => "Paper",
                "mtgo" => "MTGO",
                "arena" => "Arena",
                _ => t
            })
            .Distinct();
        parts.Add($"Available: {string.Join("/", labels)}");
    }

    private static void AddLayoutSummary(List<string> parts, SearchOptions options)
    {
        if (options.LayoutFilter.Count == 0) return;
        var labels = options.LayoutFilter.Select(l => l switch
        {
            CardLayout.ModalDfc => "MDFC",
            CardLayout.DoubleFacedToken => "DFC token",
            _ => l.ToString()
        });
        parts.Add($"Layout: {string.Join("/", labels)}");
    }

    private static void AddFinishesSummary(List<string> parts, SearchOptions options)
    {
        if (options.FinishesFilter.Count == 0) return;
        var labels = options.FinishesFilter
            .Select(static t => t.ToLowerInvariant() switch
            {
                "nonfoil" => "Nonfoil",
                "foil" => "Foil",
                "etched" => "Etched",
                _ => t
            })
            .Distinct();
        parts.Add($"Finish: {string.Join("/", labels)}");
    }

    private static void AddSpecialSummary(List<string> parts, SearchOptions options)
    {
        if (options.NoVariations)
            parts.Add("No variations");

        if (options.ShowAllPrintings)
            parts.Add("All printings");

        if (options.IncludeTokens)
            parts.Add("Include tokens");

        if (options.CommanderOnly)
            parts.Add("Can be commander only");
    }

    private void RecordRecentPlainSearchIfApplicable()
    {
        if (HasNonTextFilters)
            return;

        var name = (CurrentOptions.NameFilter ?? "").Trim();
        if (name.Length == 0)
            return;

        SearchRecentQueriesStore.Push(name);
        RefreshRecentSearches();
    }

    private void RefreshRecentSearches()
    {
        RecentSearches.Clear();
        foreach (var q in SearchRecentQueriesStore.Load())
            RecentSearches.Add(q);
        HasRecentSearches = RecentSearches.Count > 0;
    }
}
