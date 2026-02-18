namespace MTGFetchMAUI;

using MTGFetchMAUI.Services;

public partial class AppShell : Shell
{
    public AppShell(CardManager cardManager)
    {
        InitializeComponent();

        _ = Task.Run(async () =>
        {
            await cardManager.InitializeAsync();
            await cardManager.InitializePricesAsync();
        });

        // Register routes for navigation
        Routing.RegisterRoute("carddetail", typeof(Pages.CardDetailPage));
        Routing.RegisterRoute("searchfilters", typeof(Pages.SearchFiltersPage));
    }
}
