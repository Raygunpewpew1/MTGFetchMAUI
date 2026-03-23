using AetherVault.Core;
using AetherVault.Services;
using AetherVault.ViewModels;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

namespace AetherVault.Pages;

/// <summary>
/// Full-screen search filters page. Replaces the old bottom sheet popup.
/// All filters are shown in a single scrollable list — no tabs, no popups.
/// Push modally via ISearchFiltersOpener; dismiss via APPLY or RESET+X buttons.
/// </summary>
public partial class SearchFiltersPage : ContentPage
{
    private SearchFiltersViewModel ViewModel => (SearchFiltersViewModel)BindingContext;

    public SearchFiltersPage(SearchFiltersViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// Call immediately after resolving from DI and before pushing the page.
    /// Configures the ViewModel with the caller's context and subscribes to the close signal.
    /// </summary>
    public void Init(ISearchFilterTarget target, CardManager cardManager)
    {
        ViewModel.Configure(target, cardManager);
        ViewModel.RequestClose += OnRequestClose;
    }

    private async void OnRequestClose()
    {
        ViewModel.RequestClose -= OnRequestClose;
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        ViewModel.RequestClose -= OnRequestClose;
        return base.OnBackButtonPressed();
    }

    private async void OnSetFieldTapped(object? sender, TappedEventArgs e)
    {
        var sets = ViewModel.SetList.ToList();
        var currentIndex = ViewModel.SelectedSetIndex;
        var popup = new SetPickerPopup(sets, currentIndex, idx =>
        {
            MainThread.BeginInvokeOnMainThread(() => ViewModel.SelectedSetIndex = idx);
        });

        var options = new PopupOptions
        {
            Shape = null,
            Shadow = null,
            PageOverlayColor = Color.FromRgba(0, 0, 0, 0.5)
        };
        await this.ShowPopupAsync(popup, options);
    }
}
