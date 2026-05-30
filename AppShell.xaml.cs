namespace AetherVault;

using AetherVault.Pages;

/// <summary>
/// Main shell: tab bar (Search, Collection, Stats, Decks, Settings) and registered routes for modal/detail pages.
/// Tab pages are DI singletons; the shell is transient so Android gets a fresh ShellItemRenderer after
/// the loading screen (DB update). Call DetachAllTabContent before leaving the shell.
/// </summary>
public partial class AppShell : Shell
{
    private readonly IServiceProvider _services;

    public AppShell(
        IServiceProvider services,
        SearchPage searchPage,
        CollectionPage collectionPage,
        StatsPage statsPage,
        DecksPage decksPage,
        SettingsPage settingsPage)
    {
        InitializeComponent();
        _services = services;

        Routing.RegisterRoute("carddetail", typeof(CardDetailPage));
        Routing.RegisterRoute("deckdetail", typeof(DeckDetailPage));
        Routing.RegisterRoute("mtgjsondecks", typeof(MtgJsonDecksPage));

        WireAllTabs(searchPage, collectionPage, statsPage, decksPage, settingsPage);
    }

    /// <summary>
    /// Re-binds singleton tab pages after Content was cleared when showing the loading page.
    /// </summary>
    public void PrepareForWindowActivation()
    {
        WireAllTabs(
            _services.GetRequiredService<SearchPage>(),
            _services.GetRequiredService<CollectionPage>(),
            _services.GetRequiredService<StatsPage>(),
            _services.GetRequiredService<DecksPage>(),
            _services.GetRequiredService<SettingsPage>());
    }

    /// <summary>
    /// Clears tab content so singleton pages are not parented to a detached shell (required before DB-update navigation).
    /// </summary>
    public static void DetachAllTabContent(AppShell shell)
    {
        shell.SearchTab.Content = null;
        shell.CollectionTab.Content = null;
        shell.StatsTab.Content = null;
        shell.DecksTab.Content = null;
        shell.SettingsTab.Content = null;
    }

    private void WireAllTabs(
        SearchPage searchPage,
        CollectionPage collectionPage,
        StatsPage statsPage,
        DecksPage decksPage,
        SettingsPage settingsPage)
    {
        AttachTab(SearchTab, searchPage);
        AttachTab(CollectionTab, collectionPage);
        AttachTab(StatsTab, statsPage);
        AttachTab(DecksTab, decksPage);
        AttachTab(SettingsTab, settingsPage);
    }

    /// <summary>
    /// MAUI allows one parent per Page; Android Shell fragments NRE if parent/section state is stale.
    /// </summary>
    private static void AttachTab(ShellContent tab, Page page)
    {
        if (page.Parent is ShellContent previousTab && !ReferenceEquals(previousTab, tab))
            previousTab.Content = null;

        tab.Content = null;
        tab.Content = page;
    }
}
