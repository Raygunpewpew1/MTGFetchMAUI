using MTGFetchMAUI.Core;
using MTGFetchMAUI.Services.DeckBuilder;

namespace MTGFetchMAUI.Pages;

public partial class CreateDeckPage : ContentPage
{
    private readonly DeckBuilderService _deckService;
    private readonly TaskCompletionSource<int?> _tcs = new();

    private static readonly DeckFormat[] Formats =
    [
        DeckFormat.Commander,
        DeckFormat.Standard,
        DeckFormat.Modern,
        DeckFormat.Pioneer,
        DeckFormat.Legacy,
        DeckFormat.Vintage,
        DeckFormat.Pauper,
        DeckFormat.PauperCommander,
        DeckFormat.Oathbreaker,
        DeckFormat.Brawl,
        DeckFormat.Historic,
        DeckFormat.Timeless,
    ];

    public CreateDeckPage(DeckBuilderService deckService)
    {
        InitializeComponent();
        _deckService = deckService;

        FormatPicker.ItemsSource = Formats.Select(f => f.ToDisplayName()).ToList();
        FormatPicker.SelectedIndex = 0; // Commander pre-selected
    }

    /// <summary>Awaitable result: new deck ID, or null if cancelled.</summary>
    public Task<int?> WaitForResultAsync() => _tcs.Task;

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            ErrorLabel.Text = "Please enter a deck name.";
            ErrorLabel.IsVisible = true;
            return;
        }

        ErrorLabel.IsVisible = false;

        try
        {
            var format = FormatPicker.SelectedIndex >= 0
                ? Formats[FormatPicker.SelectedIndex]
                : DeckFormat.Commander;

            var description = DescriptionEntry.Text?.Trim() ?? "";
            int newId = await _deckService.CreateDeckAsync(name, format, description);
            _tcs.TrySetResult(newId);
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = $"Failed: {ex.Message}";
            ErrorLabel.IsVisible = true;
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        await Navigation.PopModalAsync();
    }
}
