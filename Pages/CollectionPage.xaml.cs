using AetherVault.Services;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

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

        string? action = await DisplayActionSheet(card.Name, "Cancel", null, "Edit Quantity", "Add to Binder...");

        if (action == "Edit Quantity")
        {
            int currentQty = await _viewModel.GetCollectionQuantityAsync(uuid);
            var page = new CollectionAddPage(card.Name, $"{card.SetCode} #{card.Number}", currentQty);
            await Navigation.PushModalAsync(page);
            var result = await page.WaitForResultAsync();
            if (result is CollectionAddResult r)
            {
                await _viewModel.UpdateCollectionAsync(uuid, r.NewQuantity, r.IsFoil, r.IsEtched);
                if (r.NewQuantity > 0)
                    _toastService.Show($"{r.NewQuantity}x {card.Name} in collection");
                else
                    _toastService.Show($"{card.Name} removed from collection");
                await _viewModel.LoadCollectionAsync();
            }
        }
        else if (action == "Add to Binder...")
        {
            await OnAddToBinderAsync(uuid, card.Name);
        }
    }

    private async Task OnAddToBinderAsync(string uuid, string cardName)
    {
        var binders = await _viewModel.GetAllBindersAsync();
        if (binders.Length == 0)
        {
            await DisplayAlert("No Binders", "Create a binder first by tapping the binders button.", "OK");
            return;
        }

        var binderNames = binders.Select(b => b.Name).ToArray();
        string? selected = await DisplayActionSheet("Add to Binder", "Cancel", null, binderNames);
        if (string.IsNullOrEmpty(selected) || selected == "Cancel") return;

        var binder = binders.First(b => b.Name == selected);
        await _viewModel.AddCardToBinderAsync(binder.Id, uuid);
        _toastService.Show($"{cardName} added to \"{binder.Name}\"");
    }

    private async void OnClearCollectionClicked(object? sender, EventArgs e)
    {
        int total = _viewModel.TotalCards;
        string message = total > 0
            ? $"This will permanently remove all {total} cards from your collection. This cannot be undone."
            : "Your collection is already empty.";

        if (total == 0)
        {
            await DisplayAlert("Collection Empty", message, "OK");
            return;
        }

        bool confirmed = await DisplayAlert("Clear Collection", message, "Clear", "Cancel");
        if (confirmed)
            await _viewModel.ClearCollectionAsync();
    }

    private async void OnBindersClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("binders");
    }

    private async void OnCardReorderRequested(int fromIndex, int toIndex)
    {
        await _viewModel.ReorderCollectionAsync(fromIndex, toIndex);
    }
}
