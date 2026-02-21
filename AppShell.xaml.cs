namespace MTGFetchMAUI;

using MTGFetchMAUI.Services;

public partial class AppShell : Shell
{
    public AppShell(CardManager cardManager)
    {
        InitializeComponent();

        // Register routes for navigation
        Routing.RegisterRoute("carddetail", typeof(Pages.CardDetailPage));
        Routing.RegisterRoute("searchfilters", typeof(Pages.SearchFiltersPage));
    }
}
