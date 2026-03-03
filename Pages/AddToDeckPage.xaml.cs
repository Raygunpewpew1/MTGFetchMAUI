using AetherVault.Models;
using AetherVault.Services.DeckBuilder;

namespace AetherVault.Pages;

public record AddToDeckResult(int DeckId, string DeckName, string Section, int Quantity);

public partial class AddToDeckPage : ContentPage
{
    private readonly DeckBuilderService _deckService;
    private int _quantity = 1;
    private List<DeckEntity> _decks = [];
    private readonly TaskCompletionSource<AddToDeckResult?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<AddToDeckResult?> WaitForResultAsync() => _tcs.Task;

    public AddToDeckPage(DeckBuilderService deckService, string cardUuid, string cardName)
    {
        InitializeComponent();
        _deckService = deckService;
        TitleLabel.Text = cardName;
        SectionPicker.SelectedIndex = 0; // "Main"
        UpdateQuantityUI();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDecksAsync();
    }

    private async Task LoadDecksAsync()
    {
        _decks = await _deckService.GetDecksAsync();

        bool hasDecks = _decks.Count > 0;
        NoDeckPanel.IsVisible = !hasDecks;
        DeckPickerPanel.IsVisible = hasDecks;

        DeckPicker.ItemsSource = _decks.Select(d => $"{d.Name} ({d.FormatDisplay})").ToList();
        if (hasDecks)
            DeckPicker.SelectedIndex = 0;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_tcs.Task.IsCompleted)
            _tcs.TrySetResult(null);
    }

    private void UpdateQuantityUI()
    {
        QuantityLabel.Text = _quantity.ToString();
        MinusBtn.IsEnabled = _quantity > 1;
        MinusBtn.Opacity = _quantity > 1 ? 1.0 : 0.4;
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (_quantity > 1) { _quantity--; UpdateQuantityUI(); }
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        _quantity++;
        UpdateQuantityUI();
    }

    private async void OnCreateDeckClicked(object? sender, EventArgs e)
    {
        var modal = new CreateDeckPage(_deckService);
        await Navigation.PushModalAsync(modal);
        int? newId = await modal.WaitForResultAsync();
        if (newId.HasValue)
            await LoadDecksAsync();
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (DeckPicker.SelectedIndex < 0)
        {
            await DisplayAlertAsync("No Deck", "Please select a deck.", "OK");
            return;
        }

        var deck = _decks[DeckPicker.SelectedIndex];
        string section = SectionPicker.SelectedIndex >= 0
            ? SectionPicker.Items[SectionPicker.SelectedIndex]
            : "Main";
        _tcs.TrySetResult(new AddToDeckResult(deck.Id, deck.Name, section, _quantity));
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        await Navigation.PopModalAsync();
    }
}
