using AetherVault.Models;
using AetherVault.Services.DeckBuilder;

namespace AetherVault.Pages;

public record AddToDeckResult(int DeckId, string DeckName, string Section, int Quantity);

public partial class AddToDeckPage : ContentPage
{
    private readonly DeckBuilderService _deckService;
    private readonly IServiceProvider _serviceProvider;
    private int _quantity = 1;
    private List<DeckEntity> _decks = [];
    private readonly TaskCompletionSource<AddToDeckResult?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Set by caller after resolving from DI; used when opening for a specific card.</summary>
    public string CardUuid { get; set; } = "";

    /// <summary>Set by caller after resolving from DI; displayed as title.</summary>
    public string CardName { get; set; } = "";

    /// <summary>Optional; preselect this deck when available.</summary>
    public int? InitialDeckId { get; set; }

    /// <summary>Optional; preselect this section (e.g. "Main", "Sideboard").</summary>
    public string? InitialSection { get; set; }

    public Task<AddToDeckResult?> WaitForResultAsync() => _tcs.Task;

    public AddToDeckPage(DeckBuilderService deckService, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _deckService = deckService;
        _serviceProvider = serviceProvider;
        SectionPicker.SelectedIndex = 0; // "Main"
        SectionPicker.SelectedIndexChanged += (_, _) => UpdateConfirmText();
        DeckPicker.SelectedIndexChanged += (_, _) => UpdateConfirmText();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        TitleLabel.Text = CardName;
        QuantitySelector.Quantity = _quantity;
        QuantitySelector.Minimum = 1;
        QuantitySelector.Maximum = 999;
        QuantitySelector.QuantityChanged += OnQuantitySelectorQuantityChanged;
        QuantitySelector.EditRequested += OnQuantitySelectorEditRequested;
        UpdateQuantityUI();
        await LoadDecksAsync();
    }

    private void OnQuantitySelectorQuantityChanged(object? sender, int newQuantity)
    {
        _quantity = newQuantity;
        UpdateQuantityUI();
    }

    private async void OnQuantitySelectorEditRequested(object? sender, EventArgs e)
    {
        string current = QuantitySelector.Quantity.ToString();
        string? input = await DisplayPromptAsync(
            "Set Quantity",
            "Enter desired number of copies:",
            accept: "OK",
            cancel: "Cancel",
            keyboard: Keyboard.Numeric,
            initialValue: current);

        if (string.IsNullOrWhiteSpace(input))
            return;

        if (int.TryParse(input, out int value) && value >= 1)
        {
            _quantity = Math.Min(value, QuantitySelector.Maximum);
            QuantitySelector.Quantity = _quantity;
            UpdateQuantityUI();
        }
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

        if (InitialDeckId.HasValue)
        {
            int idx = _decks.FindIndex(d => d.Id == InitialDeckId.Value);
            if (idx >= 0)
                indexToSelect = idx;
        }
        else
        {
            var (lastDeckId, _) = _deckService.GetLastSelection();
            if (lastDeckId.HasValue)
            {
                int lastIdx = _decks.FindIndex(d => d.Id == lastDeckId.Value);
                if (lastIdx >= 0)
                    indexToSelect = lastIdx;
            }
        }

        DeckPicker.SelectedIndex = indexToSelect;

        string? section = string.IsNullOrWhiteSpace(InitialSection) ? null : InitialSection;
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
        QuantitySelector.QuantityChanged -= OnQuantitySelectorQuantityChanged;
        QuantitySelector.EditRequested -= OnQuantitySelectorEditRequested;
        if (!_tcs.Task.IsCompleted)
            _tcs.TrySetResult(null);
    }

    private void UpdateQuantityUI()
    {
        UpdateConfirmText();
    }

    private void UpdateConfirmText()
    {
        string section = SectionPicker.SelectedIndex >= 0
            ? SectionPicker.Items[SectionPicker.SelectedIndex]
            : "Main";

        ConfirmButton.Text = $"Add to {section}";
    }

    private void OnQuickAddOneClicked(object? sender, EventArgs e)
    {
        _quantity = Math.Min(_quantity + 1, QuantitySelector.Maximum);
        QuantitySelector.Quantity = _quantity;
        UpdateQuantityUI();
    }

    private void OnQuickAddTwoClicked(object? sender, EventArgs e)
    {
        _quantity = Math.Min(_quantity + 2, QuantitySelector.Maximum);
        QuantitySelector.Quantity = _quantity;
        UpdateQuantityUI();
    }

    private void OnQuickAddFourClicked(object? sender, EventArgs e)
    {
        _quantity = Math.Min(_quantity + 4, QuantitySelector.Maximum);
        QuantitySelector.Quantity = _quantity;
        UpdateQuantityUI();
    }

    private async void OnCreateDeckClicked(object? sender, EventArgs e)
    {
        var modal = _serviceProvider.GetRequiredService<CreateDeckPage>();
        await Navigation.PushModalAsync(modal);
        int? newId = await modal.WaitForResultAsync();
        if (newId.HasValue)
            await LoadDecksAsync();
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (DeckPicker.SelectedIndex < 0)
        {
            await DisplayAlertAsync(UserMessages.NoDeckTitle, UserMessages.PleaseSelectDeck, "OK");
            return;
        }

        var deck = _decks[DeckPicker.SelectedIndex];
        string section = SectionPicker.SelectedIndex >= 0
            ? SectionPicker.Items[SectionPicker.SelectedIndex]
            : "Main";
        _tcs.TrySetResult(new AddToDeckResult(deck.Id, deck.Name, section, QuantitySelector.Quantity));
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        await Navigation.PopModalAsync();
    }
}
