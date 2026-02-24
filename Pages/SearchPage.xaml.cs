using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.ViewModels;

namespace MTGFetchMAUI.Pages;

public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;
    private readonly IToastService _toastService;
    private readonly CardGalleryContext _galleryContext;

    public SearchPage(SearchViewModel viewModel, IToastService toastService, CardGalleryContext galleryContext)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toastService = toastService;
        _galleryContext = galleryContext;
        BindingContext = _viewModel;

        _viewModel.AttachGrid(CardGrid);

        CardGrid.CardClicked += OnCardClicked;
        CardGrid.CardLongPressed += OnCardLongPressed;

        _viewModel.SearchCompleted += () =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await CardGrid.ScrollToAsync(0, false);
            });
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CardGrid.OnResume();
        _toastService.OnShow += OnToastShow;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CardGrid.OnSleep();
        _toastService.OnShow -= OnToastShow;
    }

    private void OnToastShow(string message, int duration)
    {
        MainThread.BeginInvokeOnMainThread(() => _ = GridSnackbar.ShowAsync(message, duration));
    }

    private void OnGridScrolled(object? sender, ScrolledEventArgs e)
    {
        float scrollY = (float)e.ScrollY;
        float viewportHeight = (float)CardGrid.Height;
        float contentHeight = CardGrid.ContentHeight;
        _viewModel.OnScrollChanged(scrollY, viewportHeight, contentHeight);
    }

    private async void OnCardClicked(string uuid)
    {
        _galleryContext.SetContext(CardGrid.GetAllUuids(), uuid);
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
