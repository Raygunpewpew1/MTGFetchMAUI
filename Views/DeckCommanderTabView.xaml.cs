namespace AetherVault.Views;

/// <summary>
/// Commander tab content for DeckDetailPage. Raises CommanderMenuRequested when the three-dot button is tapped; page handles the action sheet.
/// </summary>
public partial class DeckCommanderTabView : ContentView
{
    public event EventHandler? CommanderMenuRequested;

    public DeckCommanderTabView()
    {
        InitializeComponent();
    }

    private void OnCommanderMenuClicked(object? sender, EventArgs e)
    {
        CommanderMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
