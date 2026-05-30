using AetherVault.Models;

namespace AetherVault.Controls;

/// <summary>
/// Full-width deck row for the Decks hub list layout.
/// </summary>
public partial class DeckListItem : ContentView
{
    public event EventHandler<DeckEntity>? OverflowMenuRequested;

    public DeckListItem()
    {
        InitializeComponent();
    }

    private void OnOverflowClicked(object? sender, EventArgs e)
    {
        if (BindingContext is DeckEntity deck)
            OverflowMenuRequested?.Invoke(this, deck);
    }
}
