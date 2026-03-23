using AetherVault.Pages;
using AetherVault.ViewModels;

namespace AetherVault.Services;

/// <summary>
/// Opens the full-screen Search Filters page by resolving SearchFiltersPage from DI,
/// initialising it with the caller's target and CardManager, then pushing it as a modal.
/// </summary>
public sealed class SearchFiltersOpenerService : ISearchFiltersOpener
{
    private readonly IServiceProvider _serviceProvider;

    public SearchFiltersOpenerService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task OpenAsync(ISearchFilterTarget target, CardManager cardManager)
    {
        var page = _serviceProvider.GetRequiredService<SearchFiltersPage>();
        page.Init(target, cardManager);

        var currentPage = Shell.Current?.Navigation?.ModalStack.LastOrDefault()
                          ?? Shell.Current?.CurrentPage
                          ?? Application.Current?.Windows.FirstOrDefault()?.Page;

        if (currentPage != null)
            await currentPage.Navigation.PushModalAsync(page, animated: true);
    }
}
