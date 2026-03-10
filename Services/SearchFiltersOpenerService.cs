using AetherVault.Pages;
using AetherVault.ViewModels;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

namespace AetherVault.Services;

/// <summary>
/// Opens the Search Filters sheet by resolving SearchFiltersSheet from DI, initialising it
/// with the caller's target and CardManager, then showing it as a bottom sheet.
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
        var sheet = _serviceProvider.GetRequiredService<SearchFiltersSheet>();
        sheet.Init(target, cardManager);

        // Prefer topmost modal page so the sheet appears above CardSearchPickerPage when active
        var page = Shell.Current?.Navigation?.ModalStack.LastOrDefault()
                   ?? Shell.Current?.CurrentPage
                   ?? Application.Current?.Windows.FirstOrDefault()?.Page;

        if (page != null)
            await page.ShowPopupAsync(sheet);
    }
}
