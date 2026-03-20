using AetherVault.Core;
using AetherVault.ViewModels;
using AetherVault.Services;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

namespace AetherVault.Pages;

public partial class SearchFiltersSheet : Popup
{
    private SearchFiltersViewModel ViewModel => (SearchFiltersViewModel)BindingContext;
    private Page? _hostPage;

    public SearchFiltersSheet(SearchFiltersViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// Call immediately after resolving from DI and before showing the sheet.
    /// Configures the ViewModel with the caller's context and subscribes to the close signal.
    /// </summary>
    public void Init(ISearchFilterTarget target, CardManager cardManager, Page? hostPage = null)
    {
        ViewModel.Configure(target, cardManager);
        ViewModel.RequestClose += OnRequestClose;
        Closed += OnSheetClosed;
        _hostPage = hostPage;
    }

    private void OnSheetClosed(object? sender, EventArgs e)
    {
        ViewModel.RequestClose -= OnRequestClose;
        Closed -= OnSheetClosed;
    }

    private async void OnRequestClose()
    {
        ViewModel.RequestClose -= OnRequestClose;
        Closed -= OnSheetClosed;

        await CloseAsync();
    }

    private async void OnSetFieldTapped(object? sender, TappedEventArgs e)
    {
        await ShowSetPickerAsync();
    }

    /// <summary>Shows the searchable set picker popup. Called when user taps the Set field.</summary>
    public async Task ShowSetPickerAsync()
    {
        var page = _hostPage ?? Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

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
        await page.ShowPopupAsync(popup, options);
    }
}
