using AetherVault.Models;
using AetherVault.Services;
using AetherVault.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AetherVault.Pages;

public partial class DecksPage : ContentPage
{
    private readonly DecksViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;

    public DecksPage(DecksViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadDecksAsync();
    }

    private async void OnNewDeckClicked(object? sender, EventArgs e)
    {
        var modal = _serviceProvider.GetRequiredService<CreateDeckPage>();
        await Navigation.PushModalAsync(modal);
        int? newId = await modal.WaitForResultAsync();
        if (newId.HasValue)
        {
            await _viewModel.LoadDecksAsync();
            await Shell.Current.GoToAsync($"deckdetail?deckId={newId.Value}");
        }
    }

    private async void OnRenameDeckButtonClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is DeckEntity deck)
        {
            string? newName = await DisplayPromptAsync(
                UserMessages.RenameDeckTitle,
                UserMessages.RenameDeckPrompt,
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
            UserMessages.DeleteDeckTitle,
            UserMessages.DeleteDeckMessage(deck.Name),
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
