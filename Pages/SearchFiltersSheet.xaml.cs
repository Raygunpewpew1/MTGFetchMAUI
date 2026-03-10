using AetherVault.ViewModels;
using CommunityToolkit.Maui.Views;

namespace AetherVault.Pages;

public partial class SearchFiltersSheet : BottomSheet
{
    private SearchFiltersViewModel ViewModel => (SearchFiltersViewModel)BindingContext;

    public SearchFiltersSheet(SearchFiltersViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// Call immediately after resolving from DI and before showing the sheet.
    /// Configures the ViewModel with the caller's context and subscribes to the close signal.
    /// </summary>
    public void Init(ISearchFilterTarget target, CardManager cardManager)
    {
        ViewModel.Configure(target, cardManager);
        ViewModel.RequestClose += OnRequestClose;
        Dismissed += OnSheetDismissed;
    }

    // Drag-to-dismiss is treated as Cancel — no filters applied.
    // Also fires when DismissAsync() is called, so we guard against double-unsubscribe.
    private void OnSheetDismissed(object? sender, DismissOrigin e)
    {
        ViewModel.RequestClose -= OnRequestClose;
        Dismissed -= OnSheetDismissed;
    }

    private async void OnRequestClose()
    {
        ViewModel.RequestClose -= OnRequestClose;
        Dismissed -= OnSheetDismissed;
        await DismissAsync();
    }
}
