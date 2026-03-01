using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services.DeckBuilder;
using MTGFetchMAUI.ViewModels;

namespace MTGFetchMAUI.Pages;

public partial class DecksPage : ContentPage
{
    private readonly DecksViewModel _viewModel;
    private readonly DeckBuilderService _deckService;

    public DecksPage(DecksViewModel viewModel, DeckBuilderService deckService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _deckService = deckService;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadDecksAsync();
    }

    private async void OnNewDeckClicked(object? sender, EventArgs e)
    {
        var modal = new CreateDeckPage(_deckService);
        await Navigation.PushModalAsync(modal);
        int? newId = await modal.WaitForResultAsync();
        if (newId.HasValue)
        {
            await _viewModel.LoadDecksAsync();
            await Shell.Current.GoToAsync($"deckdetail?deckId={newId.Value}");
        }
    }

    private async void OnDeckTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is DeckEntity deck)
        {
            await Shell.Current.GoToAsync($"deckdetail?deckId={deck.Id}");
        }
    }

    private async void OnDeleteSwipeInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem swipe && swipe.BindingContext is DeckEntity deck)
        {
            bool confirmed = await DisplayAlert(
                "Delete Deck",
                $"Delete \"{deck.Name}\"? This cannot be undone.",
                "Delete", "Cancel");

            if (confirmed)
                await _viewModel.DeleteDeckAsync(deck);
        }
    }
}
