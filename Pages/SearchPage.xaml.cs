using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.ViewModels;

namespace MTGFetchMAUI.Pages;

public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;

    public SearchPage(SearchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.AttachGrid(CardGrid);

        CardGrid.CardClicked += OnCardClicked;

        _viewModel.SearchCompleted += () =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await GridScrollView.ScrollToAsync(0, 0, false);
            });
        };

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.IsBusy))
            {
                LoadingIndicator.IsRunning = _viewModel.IsBusy;
                LoadingIndicator.IsVisible = _viewModel.IsBusy;
            }
            else if (e.PropertyName == nameof(SearchViewModel.StatusMessage))
            {
                StatusLabel.Text = _viewModel.StatusMessage;
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CardGrid.StartTimers();

        // Ensure scroll is synced after a tab switch
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CardGrid.SetScrollOffset((float)GridScrollView.ScrollY);
            _viewModel.LoadVisibleImages(ImageQuality.Small);
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CardGrid.StopTimers();
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        _viewModel.SearchText = SearchEntry.Text ?? "";
        await _viewModel.PerformSearchAsync();
    }

    private async void OnSearchCompleted(object? sender, EventArgs e)
    {
        _viewModel.SearchText = SearchEntry.Text ?? "";
        await _viewModel.PerformSearchAsync();
    }

    private async void OnFiltersClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("searchfilters");
    }

    private void OnGridScrolled(object? sender, ScrolledEventArgs e)
    {
        float scrollY = (float)e.ScrollY;
        float viewportHeight = (float)GridScrollView.Height;
        float contentHeight = (float)CardGrid.TotalContentHeight;
        _viewModel.OnScrollChanged(scrollY, viewportHeight, contentHeight);
    }

    private async void OnCardClicked(string uuid)
    {
        await Shell.Current.GoToAsync($"carddetail?uuid={uuid}");
    }
}
