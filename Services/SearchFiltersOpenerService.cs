using AetherVault.Pages;
using AetherVault.ViewModels;

namespace AetherVault.Services;

/// <summary>
/// Opens the Search Filters modal by resolving the page from DI, initializing it with the target and CardManager, then pushing it modally.
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
        var shell = Shell.Current;
        if (shell?.Navigation != null)
            await shell.Navigation.PushModalAsync(page);
    }
}
