using CommunityToolkit.Maui.Views;
using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.ViewModels;

namespace MTGFetchMAUI.Pages;

public partial class CollectionPage : ContentPage
{
    private readonly CollectionViewModel _viewModel;
    private readonly IToastService _toastService;
    private readonly CardGalleryContext _galleryContext;
    private bool _loaded;

    public CollectionPage(CollectionViewModel viewModel, IToastService toastService, CardGalleryContext galleryContext)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toastService = toastService;
        _galleryContext = galleryContext;
        BindingContext = _viewModel;

        _viewModel.AttachGrid(CollectionGrid);

        CollectionGrid.CardClicked += OnCardClicked;
        CollectionGrid.CardLongPressed += OnCardLongPressed;
        CollectionGrid.CardReorderRequested += OnCardReorderRequested;

        _viewModel.CollectionLoaded += () =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await CollectionGrid.ScrollToAsync(0, false);
            });
        };

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CollectionViewModel.IsBusy))
            {
                CollectionLoading.IsRunning = _viewModel.IsBusy;
                CollectionLoading.IsVisible = _viewModel.IsBusy;
            }
            else if (e.PropertyName == nameof(CollectionViewModel.StatusMessage))
            {
                CollectionStatus.Text = _viewModel.StatusMessage;
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        CollectionGrid.OnResume();

        // Ensure scroll is synced after a tab switch
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CollectionGrid.ForceRedraw();
        });

        if (!_loaded)
        {
            _loaded = true;
            await _viewModel.LoadCollectionAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CollectionGrid.OnSleep();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await _viewModel.LoadCollectionAsync();
    }

    private void OnCollectionScrolled(object? sender, ScrolledEventArgs e)
    {
        _viewModel.OnScrollChanged((float)e.ScrollY);
    }

    private async void OnCardClicked(string uuid)
    {
        _galleryContext.SetContext(CollectionGrid.GetAllUuids(), uuid);
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

            // Refresh collection to show updated quantity
            await _viewModel.LoadCollectionAsync();
        }
    }

    private async void OnCardReorderRequested(int fromIndex, int toIndex)
    {
        await _viewModel.ReorderCollectionAsync(fromIndex, toIndex);
    }
}
