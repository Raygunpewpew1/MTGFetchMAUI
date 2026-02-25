using CommunityToolkit.Maui.Views;
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
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CardGrid.OnSleep();
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

        var resultObj = await this.ShowPopupAsync(new CollectionAddSheet(
            card.Name,
            $"{card.SetCode} #{card.Number}",
            currentQty));

        if (resultObj is int quantity)
        {
            await _viewModel.UpdateCollectionAsync(uuid, quantity);
            _toastService.Show($"{quantity}x {card.Name} in collection");
        }
    }
}
