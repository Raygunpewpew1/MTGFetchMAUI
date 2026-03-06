using AetherVault.Core;

namespace AetherVault.ViewModels;

/// <summary>
/// Target for the shared search filters UI (SearchFiltersPage).
/// Implemented by SearchViewModel and CardSearchPickerViewModel.
/// </summary>
public interface ISearchFilterTarget
{
    string SearchText { get; }
    SearchOptions CurrentOptions { get; set; }
    Task ApplyFiltersAndSearchAsync(SearchOptions options);
}
