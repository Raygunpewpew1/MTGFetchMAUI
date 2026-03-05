using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services.DeckBuilder;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AetherVault.ViewModels;

public partial class DecksViewModel : BaseViewModel
{
    private readonly DeckBuilderService _deckService;
    private readonly IDeckRepository _deckRepository;

    [ObservableProperty]
    public partial ObservableCollection<DeckEntity> Decks { get; set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public DecksViewModel(DeckBuilderService deckService, IDeckRepository deckRepository)
    {
        _deckService = deckService;
        _deckRepository = deckRepository;
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

            foreach (var deck in list)
                deck.CardCount = await _deckRepository.GetDeckCardCountAsync(deck.Id);

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

    public async Task RenameDeckAsync(DeckEntity deck, string newName)
    {
        await _deckService.UpdateDeckNameAsync(deck.Id, newName);
        await LoadDecksAsync();
    }

    [RelayCommand]
    private async Task DeckTappedAsync(DeckEntity deck)
    {
        await Shell.Current.GoToAsync($"deckdetail?deckId={deck.Id}");
    }
}
