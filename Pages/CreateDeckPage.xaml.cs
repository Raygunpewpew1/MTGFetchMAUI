using AetherVault.Core;
using AetherVault.Services.DeckBuilder;

namespace AetherVault.Pages;

public partial class CreateDeckPage : ContentPage
{
    private readonly DeckBuilderService _deckService;
    private readonly TaskCompletionSource<int?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
        FormatPicker.SelectedValueChanged += (_, _) => UpdateFormatDescription();
        UpdateFormatDescription();
    }

    private void UpdateFormatDescription()
    {
        var format = FormatPicker.SelectedIndex >= 0 && FormatPicker.SelectedIndex < Formats.Length
            ? Formats[FormatPicker.SelectedIndex]
            : DeckFormat.Commander;
        FormatDescriptionLabel.Text = GetFormatDescription(format);
    }

    /// <summary>Short, friendly primer per format so first-time builders know what they're signing up for.</summary>
    private static string GetFormatDescription(DeckFormat format) => format switch
    {
        DeckFormat.Commander =>
            "100 cards, all different (except basic lands), built around a legendary commander. Cards must match your commander's colors.",
        DeckFormat.PauperCommander =>
            "Commander, but with common cards only — 100 singleton cards behind an uncommon commander.",
        DeckFormat.Oathbreaker =>
            "60 singleton cards led by a planeswalker and a signature spell.",
        DeckFormat.Brawl =>
            "Commander's little sibling: 60 singleton Standard-legal cards behind a legendary commander.",
        DeckFormat.Standard =>
            "60+ cards from the most recent sets, up to 4 copies each, plus an optional 15-card sideboard.",
        DeckFormat.Modern =>
            "60+ cards from 2003 onward, up to 4 copies each, plus an optional 15-card sideboard.",
        DeckFormat.Pioneer =>
            "60+ cards from 2012 onward, up to 4 copies each, plus an optional 15-card sideboard.",
        DeckFormat.Legacy =>
            "60+ cards from all of Magic's history (with a ban list), up to 4 copies each.",
        DeckFormat.Vintage =>
            "60+ cards, nearly everything is legal — some powerful cards are restricted to 1 copy.",
        DeckFormat.Pauper =>
            "60+ cards using commons only, up to 4 copies each.",
        DeckFormat.Historic =>
            "60+ cards from MTG Arena's full catalog, up to 4 copies each.",
        DeckFormat.Timeless =>
            "60+ cards, MTG Arena's most open format, up to 4 copies each.",
        _ => "60+ cards, up to 4 copies each."
    };

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
