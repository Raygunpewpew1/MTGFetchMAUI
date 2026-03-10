namespace AetherVault.Controls;

/// <summary>
/// Displays a single deck's summary (name, card count, format, commander, date).
/// Use inside a DataTemplate with BindingContext = DeckEntity.
/// Rename/Delete buttons remain in the page template.
/// </summary>
public partial class DeckListItem : ContentView
{
    public DeckListItem()
    {
        InitializeComponent();
    }
}
