using AetherVault.Models;
using AetherVault.Services.DeckBuilder;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

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

    private async void OnRenameSwipeInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem swipe && swipe.BindingContext is DeckEntity deck)
        {
            string? newName = await DisplayPromptAsync(
                "Rename Deck",
                "Enter a new name:",
                initialValue: deck.Name,
                maxLength: 80);

            if (!string.IsNullOrWhiteSpace(newName) && newName != deck.Name)
                await _viewModel.RenameDeckAsync(deck, newName.Trim());
        }
    }

    private async void OnDeleteSwipeInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem swipe && swipe.BindingContext is DeckEntity deck)
        {
            await ConfirmAndDeleteDeckAsync(deck);
        }
    }

    private async void OnRenameDeckButtonClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is DeckEntity deck)
        {
            string? newName = await DisplayPromptAsync(
                "Rename deck",
                "Enter a new name:",
                initialValue: deck.Name,
                maxLength: 80);

            if (!string.IsNullOrWhiteSpace(newName) && newName != deck.Name)
                await _viewModel.RenameDeckAsync(deck, newName.Trim());
        }
    }

    private async void OnDeleteDeckButtonClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is DeckEntity deck)
        {
            await ConfirmAndDeleteDeckAsync(deck);
        }
    }

    private async Task ConfirmAndDeleteDeckAsync(DeckEntity deck)
    {
        bool confirmed = await DisplayAlertAsync(
            "Delete deck",
            $"Delete \"{deck.Name}\"? This cannot be undone.",
            "Delete", "Cancel");

        if (confirmed)
            await _viewModel.DeleteDeckAsync(deck);
    }

    private async void OnDeckSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is DeckEntity deck)
        {
            await _viewModel.DeckTappedCommand.ExecuteAsync(deck);
        }

        if (sender is CollectionView cv)
        {
            cv.SelectedItem = null;
        }
    }
}
