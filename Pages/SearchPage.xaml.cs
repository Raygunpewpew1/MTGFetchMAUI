using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.ViewModels;

namespace MTGFetchMAUI.Pages;

public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;
    private readonly IToastService _toastService;

    public SearchPage(SearchViewModel viewModel, IToastService toastService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toastService = toastService;
        BindingContext = _viewModel;

        _viewModel.AttachGrid(CardGrid);

        CardGrid.CardClicked += OnCardClicked;
        CardGrid.CardLongPressed += OnCardLongPressed;

        _viewModel.SearchCompleted += () =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await GridScrollView.ScrollToAsync(0, 0, false);
            });
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _toastService.OnShow += OnToastShow;
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
        _toastService.OnShow -= OnToastShow;
        CardGrid.StopTimers();
    }

    private void OnToastShow(string message, int duration)
    {
        MainThread.BeginInvokeOnMainThread(() => _ = GridSnackbar.ShowAsync(message, duration));
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

    private async void OnCardLongPressed(string uuid)
    {
        var card = await _viewModel.GetCardDetailsAsync(uuid);
        if (card == null) return;

        int currentQty = await _viewModel.GetCollectionQuantityAsync(uuid);

        var result = await AddSheet.ShowAsync(card.Name, $"{card.SetCode} #{card.Number}", currentQty);

        if (result.HasValue)
        {
            await _viewModel.UpdateCollectionAsync(uuid, result.Value);
            _toastService.Show($"{result.Value}x {card.Name} in collection");
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (AddSheet.IsVisible)
        {
            _ = AddSheet.HandleBackAsync();
            return true;
        }
        return base.OnBackButtonPressed();
    }
}
