using AetherVault.Services;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

/// <summary>
/// Collection tab: shows the user's saved cards in a grid. Binds to CollectionViewModel; supports sort, filter, reorder, import/export.
/// OnAppearing we load the collection unless returning from card detail (then we skip reload to keep scroll position).
/// </summary>
public partial class CollectionPage : ContentPage
{
    private readonly CollectionViewModel _viewModel;
    private readonly CardGalleryContext _galleryContext;
    private readonly DeckSynergyNavigationContext _deckSynergyNavigationContext;
    private readonly IServiceProvider _serviceProvider;
    /// <summary>When true, OnAppearing skips LoadCollectionAsync so we don't reload when coming back from card detail.</summary>
    private bool _skipNextReload;

    public CollectionPage(CollectionViewModel viewModel, CardGalleryContext galleryContext, DeckSynergyNavigationContext deckSynergyNavigationContext, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _galleryContext = galleryContext;
        _deckSynergyNavigationContext = deckSynergyNavigationContext;
        _serviceProvider = serviceProvider;
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
                if (!_viewModel.IsCollectionEmpty && CollectionGrid.Handler is not null)
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

        // Skip full reload when returning from card detail (see OnCardClicked setting _skipNextReload)
        if (_skipNextReload)
        {
            _skipNextReload = false;
            return;
        }
        await _viewModel.EnsureCollectionLoadedAsync();

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
        _deckSynergyNavigationContext.Clear();
        _galleryContext.SetContext(CollectionGrid.GetAllUuids(), uuid);
        _skipNextReload = true; // Avoid full reload when returning from detail
        await Shell.Current.GoToAsync($"carddetail?uuid={uuid}");
    }

    private async void OnCardLongPressed(string uuid)
    {
        var card = await _viewModel.GetCardDetailsAsync(uuid);
        if (card == null) return;

        int currentQty = await _viewModel.GetCollectionQuantityAsync(uuid);

        var page = _serviceProvider.GetRequiredService<CollectionAddPage>();
        page.CardName = card.Name;
        page.SetInfo = $"{card.SetCode} #{card.Number}";
        page.CurrentQty = currentQty;
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

    private async void OnCollectionMoreClicked(object? sender, EventArgs e)
    {
        const string layout = "Switch card layout";
        const string import = "Import";
        const string export = "Export";
        const string refresh = "Refresh";
        const string clearFilters = "Clear filters";
        const string captureBaselines = "Update price baselines…";
        const string clear = "Clear collection…";

        var pick = await DisplayActionSheetAsync("Collection", "Cancel", null, layout, import, export, refresh, clearFilters, captureBaselines, clear);
        if (pick == layout)
        {
            if (_viewModel.ToggleViewModeCommand.CanExecute(null))
                _viewModel.ToggleViewModeCommand.Execute(null);
            return;
        }
        if (pick == import && _viewModel.ImportCollectionCommand.CanExecute(null))
        {
            await _viewModel.ImportCollectionCommand.ExecuteAsync(null);
            return;
        }
        if (pick == export && _viewModel.ExportCollectionCommand.CanExecute(null))
        {
            await _viewModel.ExportCollectionCommand.ExecuteAsync(null);
            return;
        }
        if (pick == refresh && _viewModel.RefreshCommand.CanExecute(null))
        {
            await _viewModel.RefreshCommand.ExecuteAsync(null);
            return;
        }
        if (pick == clearFilters && _viewModel.ClearCollectionFiltersCommand.CanExecute(null))
        {
            await _viewModel.ClearCollectionFiltersCommand.ExecuteAsync(null);
            return;
        }
        if (pick == captureBaselines)
        {
            bool ok = await DisplayAlertAsync(
                "Update price baselines?",
                "Each collection line will store its current retail unit price as the baseline used for percent change on the grid. Quantities are not changed.",
                "Update",
                "Cancel");
            if (ok && _viewModel.RecapturePriceBaselinesCommand.CanExecute(null))
                await _viewModel.RecapturePriceBaselinesCommand.ExecuteAsync(null);
            return;
        }
        if (pick != clear)
            return;

        bool confirmed = await DisplayAlertAsync(
            UserMessages.ClearCollectionTitle,
            UserMessages.ClearCollectionMessage,
            "Clear",
            "Cancel");
        if (confirmed)
            await _viewModel.ClearCollectionAsync();
    }
}
