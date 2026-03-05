using AetherVault.Models;
using AetherVault.Services.DeckBuilder;

namespace AetherVault.Pages;

public record AddToDeckResult(int DeckId, string DeckName, string Section, int Quantity);

public partial class AddToDeckPage : ContentPage
{
    private readonly DeckBuilderService _deckService;
    private int _quantity = 1;
    private List<DeckEntity> _decks = [];
    private readonly int? _initialDeckId;
    private readonly string? _initialSection;
    private readonly TaskCompletionSource<AddToDeckResult?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<AddToDeckResult?> WaitForResultAsync() => _tcs.Task;

    public AddToDeckPage(
        DeckBuilderService deckService,
        string cardUuid,
        string cardName,
        int? initialDeckId = null,
        string? initialSection = null)
    {
        InitializeComponent();
        _deckService = deckService;
        _initialDeckId = initialDeckId;
        _initialSection = string.IsNullOrWhiteSpace(initialSection) ? null : initialSection;
        TitleLabel.Text = cardName;
        SectionPicker.SelectedIndex = 0; // "Main"
        UpdateQuantityUI();

        SectionPicker.SelectedIndexChanged += (_, _) => UpdateConfirmText();
        DeckPicker.SelectedIndexChanged += (_, _) => UpdateConfirmText();
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

        if (!hasDecks)
        {
            UpdateConfirmText();
            return;
        }

        int indexToSelect = 0;

        // Prefer explicit deck (e.g., when opened from a specific Deck).
        if (_initialDeckId.HasValue)
        {
            int idx = _decks.FindIndex(d => d.Id == _initialDeckId.Value);
            if (idx >= 0)
                indexToSelect = idx;
        }
        else
        {
            // Fall back to last-used deck if available.
            var (lastDeckId, _) = _deckService.GetLastSelection();
            if (lastDeckId.HasValue)
            {
                int lastIdx = _decks.FindIndex(d => d.Id == lastDeckId.Value);
                if (lastIdx >= 0)
                    indexToSelect = lastIdx;
            }
        }

        DeckPicker.SelectedIndex = indexToSelect;

        // Section: explicit initial > last-used > default Main.
        string? section = _initialSection;
        if (string.IsNullOrWhiteSpace(section))
        {
            var (_, lastSection) = _deckService.GetLastSelection();
            section = lastSection;
        }

        if (!string.IsNullOrWhiteSpace(section))
        {
            for (int i = 0; i < SectionPicker.Items.Count; i++)
            {
                if (string.Equals(SectionPicker.Items[i], section, StringComparison.OrdinalIgnoreCase))
                {
                    SectionPicker.SelectedIndex = i;
                    break;
                }
            }
        }

        if (SectionPicker.SelectedIndex < 0)
            SectionPicker.SelectedIndex = 0;

        UpdateConfirmText();
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
        UpdateConfirmText();
    }

    private void UpdateConfirmText()
    {
        string section = SectionPicker.SelectedIndex >= 0
            ? SectionPicker.Items[SectionPicker.SelectedIndex]
            : "Main";

        ConfirmButton.Text = $"Add to {section}";
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

    private async void OnQuantityTapped(object? sender, TappedEventArgs e)
    {
        string current = _quantity.ToString();
        string? input = await this.DisplayPromptAsync(
            "Set Quantity",
            "Enter desired number of copies:",
            accept: "OK",
            cancel: "Cancel",
            keyboard: Keyboard.Numeric,
            initialValue: current);

        if (string.IsNullOrWhiteSpace(input))
            return;

        if (int.TryParse(input, out int value) && value > 0)
        {
            _quantity = value;
            UpdateQuantityUI();
        }
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
