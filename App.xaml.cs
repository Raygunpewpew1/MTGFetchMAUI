namespace MTGFetchMAUI;

using MTGFetchMAUI.Services;

public partial class App : Application
{
    private readonly CardManager _cardManager;

    public App(CardManager cardManager)
    {
        InitializeComponent();
        _cardManager = cardManager;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell(_cardManager));
    }
}
