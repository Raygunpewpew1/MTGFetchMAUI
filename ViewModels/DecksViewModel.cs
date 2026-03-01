using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services.DeckBuilder;
using System.Collections.ObjectModel;

namespace MTGFetchMAUI.ViewModels;

public partial class DecksViewModel : BaseViewModel
{
    private readonly DeckBuilderService _deckService;

    [ObservableProperty]
    private ObservableCollection<DeckEntity> _decks = [];

    [ObservableProperty]
    private bool _isEmpty;

    public DecksViewModel(DeckBuilderService deckService)
    {
        _deckService = deckService;
    }

    [RelayCommand]
    public async Task LoadDecksAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = "";

        try
        {
            var list = await _deckService.GetDecksAsync();
            Decks = new ObservableCollection<DeckEntity>(list);
            IsEmpty = Decks.Count == 0;
            StatusMessage = Decks.Count == 0 ? "" : $"{Decks.Count} deck{(Decks.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteDeckAsync(DeckEntity deck)
    {
        try
        {
            await _deckService.DeleteDeckAsync(deck.Id);
            Decks.Remove(deck);
            IsEmpty = Decks.Count == 0;
            StatusMessage = Decks.Count == 0 ? "" : $"{Decks.Count} deck{(Decks.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Delete failed: {ex.Message}";
        }
    }

    public async Task<int> CreateDeckAsync(string name, Core.DeckFormat format, string description)
    {
        return await _deckService.CreateDeckAsync(name, format, description);
    }
}
