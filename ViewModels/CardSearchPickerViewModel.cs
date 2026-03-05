using AetherVault.Controls;
using AetherVault.Core.Layout;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;

namespace AetherVault.ViewModels;

public partial class CardSearchPickerViewModel : BaseViewModel
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

    public event Action<Card>? CardSelected;

    public CardSearchPickerViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;
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
    private async Task SearchAsync() => await ExecuteSearchAsync();

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
            if (string.IsNullOrEmpty(query))
            {
                _allCards = [];
                SearchResults.Clear();
                IsEmpty = true;
                StatusMessage = "Enter a search term";
                return;
            }

            if (SearchCollectionOnly)
            {
                _allCards = await _cardManager.SearchInCollectionAsync(query);
            }
            else
            {
                _allCards = await _cardManager.SearchCardsAsync(query, 100);
            }

            SearchResults = new ObservableCollection<Card>(_allCards);
            IsEmpty = _allCards.Length == 0;

            if (IsEmpty)
            {
                StatusMessage = "No cards found.";
            }
            else
            {
                StatusMessage = $"Found {_allCards.Length} cards";
            }

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
