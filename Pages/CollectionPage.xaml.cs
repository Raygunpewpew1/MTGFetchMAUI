using AetherVault.Services;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class CollectionPage : ContentPage
{
    private readonly CollectionViewModel _viewModel;
    private readonly IToastService _toastService;
    private readonly CardGalleryContext _galleryContext;
    private bool _skipNextReload;

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
            AetherVault.Services.Logger.LogStuff("[CollectionUI] CollectionLoaded fired (will ScrollToAsync grid)", AetherVault.Services.LogLevel.Debug);
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!_viewModel.IsCollectionEmpty)
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
            else if (e.PropertyName == nameof(CollectionViewModel.IsCollectionEmpty))
            {
                RunContentLayoutPass();
            }
        };
    }

    private void RunContentLayoutPass()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(RunContentLayoutPass);
            return;
        }
        CollectionContentArea.InvalidateMeasure();
        CollectionEmptyState.InvalidateMeasure();
        CollectionGrid.InvalidateMeasure();
        if (!_viewModel.IsCollectionEmpty)
            CollectionGrid.ForceRedraw();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.IsCollectionEmpty)
            CollectionGrid.OnResume();

        // Ensure scroll is synced after a tab switch
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_viewModel.IsCollectionEmpty)
                CollectionGrid.ForceRedraw();
        });

        // Skip full reload when returning from card detail to preserve scroll position and grid state
        if (_skipNextReload)
        {
            _skipNextReload = false;
            return;
        }

        await _viewModel.LoadCollectionAsync();

        RunContentLayoutPass();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_viewModel.IsCollectionEmpty)
            CollectionGrid.OnSleep();
    }

    private void OnCollectionScrolled(object? sender, ScrolledEventArgs e)
    {
        _viewModel.OnScrollChanged((float)e.ScrollY);
    }

    private async void OnCardClicked(string uuid)
    {
        _galleryContext.SetContext(CollectionGrid.GetAllUuids(), uuid);
        _skipNextReload = true; // Avoid full reload when returning from detail
        await Shell.Current.GoToAsync($"carddetail?uuid={uuid}");
    }

    private async void OnCardLongPressed(string uuid)
    {
        var card = await _viewModel.GetCardDetailsAsync(uuid);
        if (card == null) return;

        int currentQty = await _viewModel.GetCollectionQuantityAsync(uuid);

        var page = new CollectionAddPage(
            card.Name,
            $"{card.SetCode} #{card.Number}",
            currentQty);

        await Navigation.PushModalAsync(page);
        var result = await page.WaitForResultAsync();

        if (result is CollectionAddResult r)
        {
            await _viewModel.UpdateCollectionAsync(uuid, r.NewQuantity, r.IsFoil, r.IsEtched);
            await _viewModel.LoadCollectionAsync();
            RunContentLayoutPass();
        }
    }

    private async void OnCardReorderRequested(int fromIndex, int toIndex)
    {
        await _viewModel.ReorderCollectionAsync(fromIndex, toIndex);
    }

    private async void OnClearCollectionClicked(object? sender, EventArgs e)
    {
        bool confirmed = await DisplayAlertAsync(
            "Clear collection",
            "Remove all cards from your collection? This cannot be undone.",
            "Clear",
            "Cancel");
        if (confirmed)
        {
            await _viewModel.ClearCollectionAsync();
        }
    }
}
