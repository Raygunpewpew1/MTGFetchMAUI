using AetherVault.Services;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class SearchFiltersPage : ContentPage
{
    private SearchFiltersViewModel ViewModel => (SearchFiltersViewModel)BindingContext;

    public SearchFiltersPage(SearchFiltersViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>Call after resolving from DI, before showing the page. Configures the ViewModel and subscribes to close.</summary>
    public void Init(ISearchFilterTarget target, CardManager cardManager)
    {
        ViewModel.Configure(target, cardManager);
        ViewModel.RequestClose += OnRequestClose;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ViewModel.RequestClose -= OnRequestClose;
    }

    private async void OnRequestClose()
    {
        ViewModel.RequestClose -= OnRequestClose;
        var nav = Shell.Current?.Navigation;
        if (nav != null)
            await nav.PopModalAsync();
    }
}
