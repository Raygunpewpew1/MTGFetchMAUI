using AetherVault.Core;
using AetherVault.Models;
using AetherVault.ViewModels;

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
            await ShellDeckDetailNavigation.GoToAsync(newId.Value);
        }
    }

    private async void OnDeckListOverflowMenuRequested(object? sender, DeckEntity deck)
    {
        await ShowDeckOverflowMenuAsync(deck);
    }

    private async void OnDeckTileOverflowClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is DeckEntity deck)
            await ShowDeckOverflowMenuAsync(deck);
    }

    private async Task ShowDeckOverflowMenuAsync(DeckEntity deck)
    {
        if (_viewModel.IsBusy)
            return;

        const string cancel = "Cancel";
        string pick = await DisplayActionSheetAsync(
            deck.Name,
            cancel,
            null,
            UserMessages.DeckHubPictureChooseCard,
            UserMessages.RenameDeckTitle,
            UserMessages.DeleteDeckTitle);

        if (pick == UserMessages.DeckHubPictureChooseCard)
            await OnDeckHubPictureSheetAsync(deck);
        else if (pick == UserMessages.RenameDeckTitle)
            await PromptRenameDeckAsync(deck);
        else if (pick == UserMessages.DeleteDeckTitle)
            await ConfirmAndDeleteDeckAsync(deck);
    }

    private async Task OnDeckHubPictureSheetAsync(DeckEntity deck)
    {
        if (_viewModel.IsBusy) return;

        var choose = UserMessages.DeckHubPictureChooseCard;
        var clear = UserMessages.DeckHubPictureClear;
        const string cancel = "Cancel";
        string pick = await DisplayActionSheetAsync(
            UserMessages.DeckHubPictureSheetTitle,
            cancel,
            null,
            choose,
            clear);

        if (pick == clear)
        {
            await _viewModel.ClearDeckHubCoverAsync(deck);
            return;
        }

        if (pick != choose) return;

        var picker = _serviceProvider.GetRequiredService<CardSearchPickerPage>();
        picker.Title = UserMessages.DeckHubPicturePickerTitle;
        await Navigation.PushModalAsync(picker);
        var card = await picker.WaitForResultAsync();
        if (card == null) return;

        await _viewModel.ApplyDeckHubCoverFromPickerAsync(deck, card);
    }

    private async Task PromptRenameDeckAsync(DeckEntity deck)
    {
        string? newName = await DisplayPromptAsync(
            UserMessages.RenameDeckTitle,
            UserMessages.RenameDeckPrompt,
            initialValue: deck.Name,
            maxLength: 80);

        if (!string.IsNullOrWhiteSpace(newName) && newName != deck.Name)
            await _viewModel.RenameDeckAsync(deck, newName.Trim());
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

    private async void OnDeckToolsClicked(object? sender, EventArgs e)
    {
        if (_viewModel.IsBusy)
            return;

        const string cancel = "Cancel";
        string pick = await DisplayActionSheetAsync(
            UserMessages.DeckToolsActionSheetTitle,
            cancel,
            null,
            UserMessages.DeckToolsImportFile,
            UserMessages.DeckToolsImportLink,
            UserMessages.DeckToolsPasteList,
            UserMessages.DeckToolsBrowseMtgJson,
            UserMessages.DeckToolsExportAll);

        if (pick == UserMessages.DeckToolsImportFile)
            await _viewModel.ImportDecks();
        else if (pick == UserMessages.DeckToolsImportLink)
            await OnImportFromUrlAsync();
        else if (pick == UserMessages.DeckToolsPasteList)
            await OnPasteDecklistAsync();
        else if (pick == UserMessages.DeckToolsBrowseMtgJson)
            await Shell.Current.GoToAsync("mtgjsondecks");
        else if (pick == UserMessages.DeckToolsExportAll)
            await _viewModel.ExportDecks();
    }

    private async Task OnImportFromUrlAsync()
    {
        if (_viewModel.IsBusy)
            return;

        string? url = await DisplayPromptAsync(
            UserMessages.ImportDeckFromUrlTitle,
            UserMessages.ImportDeckFromUrlPrompt,
            accept: "Import",
            cancel: "Cancel",
            placeholder: "https://www.moxfield.com/decks/…",
            maxLength: 512);

        if (string.IsNullOrWhiteSpace(url))
            return;

        await _viewModel.ImportDeckFromUrlAsync(url.Trim());
    }

    private async Task OnPasteDecklistAsync()
    {
        if (_viewModel.IsBusy)
            return;

        var page = _serviceProvider.GetRequiredService<PasteDecklistPage>();
        await Navigation.PushModalAsync(page);
        string? text = await page.WaitForTextAsync();
        if (!string.IsNullOrWhiteSpace(text))
            await _viewModel.ImportDeckFromPlainTextAsync(text);
    }

    private async void OnDeckSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is DeckEntity deck)
            await _viewModel.DeckTappedCommand.ExecuteAsync(deck);

        if (sender is CollectionView cv)
            cv.SelectedItem = null;
    }
}
