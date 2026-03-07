namespace AetherVault;

using AetherVault.Pages;

/// <summary>
/// Main shell: tab bar (Search, Collection, Stats, Decks) and registered routes for modal/detail pages.
/// The DI container injects the four tab pages; we assign them to the tab content placeholders.
/// </summary>
public partial class AppShell : Shell
{
    public AppShell(SearchPage searchPage, CollectionPage collectionPage, StatsPage statsPage, DecksPage decksPage)
    {
        InitializeComponent();

        // Assign injected pages to the tab content areas defined in AppShell.xaml (SearchTab, CollectionTab, etc.)
        SearchTab.Content = searchPage;
        CollectionTab.Content = collectionPage;
        StatsTab.Content = statsPage;
        DecksTab.Content = decksPage;

        // Register routes so we can navigate with Shell.Current.GoToAsync("carddetail", new Dictionary<string, object> { ... })
        Routing.RegisterRoute("carddetail", typeof(CardDetailPage));
        Routing.RegisterRoute("searchfilters", typeof(SearchFiltersPage));
        Routing.RegisterRoute("deckdetail", typeof(DeckDetailPage));
    }
}
