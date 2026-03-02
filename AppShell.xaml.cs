namespace MTGFetchMAUI;

using MTGFetchMAUI.Pages;

public partial class AppShell : Shell
{
    public AppShell(SearchPage searchPage, CollectionPage collectionPage, StatsPage statsPage, DecksPage decksPage, TradePage tradePage)
    {
        InitializeComponent();

        SearchTab.Content = searchPage;
        CollectionTab.Content = collectionPage;
        StatsTab.Content = statsPage;
        DecksTab.Content = decksPage;
        TradeTab.Content = tradePage;

        // Register routes for navigation
        Routing.RegisterRoute("carddetail", typeof(CardDetailPage));
        Routing.RegisterRoute("searchfilters", typeof(SearchFiltersPage));
        Routing.RegisterRoute("deckdetail", typeof(DeckDetailPage));
    }
}
