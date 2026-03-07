using AetherVault.ViewModels;

namespace AetherVault.Services;

/// <summary>
/// Opens the Search Filters modal with the given target and CardManager.
/// Used by SearchViewModel and CardSearchPickerPage so filters always receive the correct context.
/// </summary>
public interface ISearchFiltersOpener
{
    Task OpenAsync(ISearchFilterTarget target, CardManager cardManager);
}
