namespace MTGFetchMAUI;

using MTGFetchMAUI.Services;
using MTGFetchMAUI.Pages;

public partial class AppShell : Shell
{
    public AppShell(SearchPage searchPage, CollectionPage collectionPage, StatsPage statsPage)
    {
        InitializeComponent();

        SearchTab.Content = searchPage;
        CollectionTab.Content = collectionPage;
        StatsTab.Content = statsPage;

        // Register routes for navigation
        Routing.RegisterRoute("carddetail", typeof(CardDetailPage));
        Routing.RegisterRoute("searchfilters", typeof(SearchFiltersPage));
    }
}
