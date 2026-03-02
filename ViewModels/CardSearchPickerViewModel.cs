using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTGFetchMAUI.Core.Layout;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.Controls;

namespace MTGFetchMAUI.ViewModels;

public partial class CardSearchPickerViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private CardGrid? _grid;
    private Card[] _allCards = [];
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isEmpty;

    public event Action? SearchCompleted;

    public CardSearchPickerViewModel(CardManager cardManager)
    {
        _cardManager = cardManager;
    }

    public void AttachGrid(CardGrid grid)
    {
        _grid = grid;
    }

    protected override void OnViewModeUpdated(ViewMode value)
    {
        if (_grid != null) _grid.ViewMode = value;
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
                _grid?.SetCards(_allCards);
                IsEmpty = true;
                StatusMessage = "Enter a search term";
                return;
            }

            _allCards = await _cardManager.SearchCardsAsync(query, 100);

            _grid?.SetCards(_allCards);
            IsEmpty = _allCards.Length == 0;

            if (IsEmpty)
            {
                StatusMessage = "No cards found.";
            }
            else
            {
                StatusMessage = $"Found {_allCards.Length} cards";
            }

            SearchCompleted?.Invoke();
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

    public void OnScrollChanged(float scrollY, float viewportHeight, float contentHeight)
    {
        // For pagination if needed
    }
}
