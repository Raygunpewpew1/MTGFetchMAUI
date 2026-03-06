using AetherVault.Controls;
using AetherVault.Core;
using AetherVault.Core.Layout;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;

namespace AetherVault.ViewModels;

public partial class CardSearchPickerViewModel : BaseViewModel, ISearchFilterTarget
{
    private readonly CardManager _cardManager;
    private Card[] _allCards = [];
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool SearchCollectionOnly { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<Card> SearchResults { get; set; } = [];

    public SearchOptions CurrentOptions { get; set; } = new();

    public string FiltersButtonText =>
        CurrentOptions.ActiveFilterCount > 0 ? $"Filters ({CurrentOptions.ActiveFilterCount})" : "Filters";

    public event Action<Card>? CardSelected;

    public CardSearchPickerViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;
    }

    public async Task ApplyFiltersAndSearchAsync(SearchOptions options)
    {
        CurrentOptions = options;
        OnPropertyChanged(nameof(FiltersButtonText));
        await ExecuteSearchAsync();
    }

    [RelayCommand]
    private void ToggleCollectionOnly()
    {
        SearchCollectionOnly = !SearchCollectionOnly;
    }

    partial void OnSearchCollectionOnlyChanged(bool value)
    {
        // Don't auto-search if text is empty when toggling collection only
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            _ = ExecuteSearchAsync();
        }
        else
        {
            SearchResults.Clear();
            IsEmpty = true;
            StatusMessage = "Enter a search term";
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        var token = _searchCts.Token;
        Task.Delay(750, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                MainThread.BeginInvokeOnMainThread(async () => await ExecuteSearchAsync());
            }
        });
    }

    [RelayCommand]
    private async Task SelectCardAsync(Card card)
    {
        var fullCard = await GetCardDetailsAsync(card.UUID);
        if (fullCard != null)
        {
            CardSelected?.Invoke(fullCard);
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await ExecuteSearchAsync();

        // If triggered explicitly via the keyboard's Return/Go key,
        // auto-accept the first result for a fast \"type → Go → pick\" flow.
        if (!IsEmpty && _allCards.Length > 0)
        {
            try
            {
                var first = _allCards[0];
                var full = await GetCardDetailsAsync(first.UUID);
                if (full != null)
                {
                    CardSelected?.Invoke(full);
                }
            }
            catch
            {
                // Ignore errors here; failures are already surfaced via status text.
            }
        }
    }

    private async Task ExecuteSearchAsync()
    {
        if (IsBusy) return;
        var query = SearchText?.Trim() ?? "";

        IsBusy = true;
        IsEmpty = false;
        StatusIsError = false;
        StatusMessage = "Searching...";

        try
        {
            if (string.IsNullOrEmpty(query) && !CurrentOptions.HasActiveFilters)
            {
                _allCards = [];
                SearchResults = new ObservableCollection<Card>(_allCards);
                IsEmpty = true;
                StatusMessage = "Enter a search term";
                return;
            }

            if (CurrentOptions.HasActiveFilters)
            {
                var options = new SearchOptions
                {
                    NameFilter = query,
                    TextFilter = CurrentOptions.TextFilter,
                    TypeFilter = CurrentOptions.TypeFilter,
                    SubtypeFilter = CurrentOptions.SubtypeFilter,
                    SupertypeFilter = CurrentOptions.SupertypeFilter,
                    ColorFilter = CurrentOptions.ColorFilter,
                    ColorIdentityFilter = CurrentOptions.ColorIdentityFilter,
                    RarityFilter = [.. CurrentOptions.RarityFilter],
                    SetFilter = CurrentOptions.SetFilter,
                    CMCMin = CurrentOptions.CMCMin,
                    CMCMax = CurrentOptions.CMCMax,
                    CMCExact = CurrentOptions.CMCExact,
                    UseCMCRange = CurrentOptions.UseCMCRange,
                    UseCMCExact = CurrentOptions.UseCMCExact,
                    PowerFilter = CurrentOptions.PowerFilter,
                    ToughnessFilter = CurrentOptions.ToughnessFilter,
                    LegalFormat = CurrentOptions.LegalFormat,
                    UseLegalFormat = CurrentOptions.UseLegalFormat,
                    ArtistFilter = CurrentOptions.ArtistFilter,
                    PrimarySideOnly = CurrentOptions.PrimarySideOnly,
                    NoVariations = CurrentOptions.NoVariations,
                    IncludeAllFaces = CurrentOptions.IncludeAllFaces,
                    IncludeTokens = CurrentOptions.IncludeTokens
                };

                var helper = _cardManager.CreateSearchHelper();
                if (SearchCollectionOnly)
                    helper.SearchMyCollection();
                else
                    helper.SearchCards(options.IncludeTokens);
                SearchOptionsApplier.Apply(helper, options);
                helper.OrderBy("c.name").Limit(100);

                _allCards = await _cardManager.ExecuteSearchAsync(helper);
            }
            else
            {
                if (SearchCollectionOnly)
                    _allCards = await _cardManager.SearchInCollectionAsync(query);
                else
                    _allCards = await _cardManager.SearchCardsAsync(query, 100);
            }

            SearchResults = new ObservableCollection<Card>(_allCards);
            IsEmpty = _allCards.Length == 0;

            if (IsEmpty)
                StatusMessage = "No cards found.";
            else
                StatusMessage = $"Found {_allCards.Length} cards";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = "Search failed.";
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
}
