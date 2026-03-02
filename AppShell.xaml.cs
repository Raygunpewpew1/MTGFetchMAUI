namespace MTGFetchMAUI;

using MTGFetchMAUI.Pages;

public partial class AppShell : Shell
{
    public AppShell(SearchPage searchPage, CollectionPage collectionPage, StatsPage statsPage, DecksPage decksPage)
    {
        InitializeComponent();

        SearchTab.Content = searchPage;
        CollectionTab.Content = collectionPage;
        StatsTab.Content = statsPage;
        DecksTab.Content = decksPage;

        // Register routes for navigation
        Routing.RegisterRoute("carddetail", typeof(CardDetailPage));
        Routing.RegisterRoute("searchfilters", typeof(SearchFiltersPage));
        Routing.RegisterRoute("deckdetail", typeof(DeckDetailPage));
    }
}
