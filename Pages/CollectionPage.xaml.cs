using AetherVault.Services;
using AetherVault.ViewModels;
using UraniumUI.Icons.FontAwesome;

namespace AetherVault.Pages;

public partial class CollectionPage : ContentPage
{
    private readonly CollectionViewModel _viewModel;
    private readonly IToastService _toastService;
    private readonly CardGalleryContext _galleryContext;
    private readonly View _emptyStateView;
    private bool _skipNextReload;

    public CollectionPage(CollectionViewModel viewModel, IToastService toastService, CardGalleryContext galleryContext)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toastService = toastService;
        _galleryContext = galleryContext;
        BindingContext = _viewModel;

        _emptyStateView = BuildEmptyStateView();

        _viewModel.AttachGrid(CollectionGrid);

        CollectionGrid.CardClicked += OnCardClicked;
        CollectionGrid.CardLongPressed += OnCardLongPressed;
        CollectionGrid.CardReorderRequested += OnCardReorderRequested;

        _viewModel.CollectionLoaded += () =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (CollectionContentHost.Content == CollectionGrid)
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
                UpdateContentHostContent();
            }
        };

        // Set initial content when VM state is already known (e.g. returning to tab)
        Loaded += (_, _) => UpdateContentHostContent();
    }

    private View BuildEmptyStateView()
    {
        var background = (Color)(Application.Current?.Resources["Background"] ?? Colors.Black);
        var textPrimary = (Color)(Application.Current?.Resources["TextPrimary"] ?? Colors.White);
        var textSecondary = (Color)(Application.Current?.Resources["TextSecondary"] ?? Colors.Gray);

        var layout = new Grid
        {
            BackgroundColor = background,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        var stack = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Spacing = 8
        };
        stack.Add(new Label
        {
            FontFamily = "FASolid",
            Text = Solid.BoxArchive,
            FontSize = 44,
            TextColor = textSecondary,
            HorizontalOptions = LayoutOptions.Center
        });
        stack.Add(new Label
        {
            Text = "Your collection is empty",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = textPrimary,
            HorizontalOptions = LayoutOptions.Center
        });
        stack.Add(new Label
        {
            Text = "Search for cards and long-press to add them",
            FontSize = 13,
            TextColor = textSecondary,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center
        });
        layout.Add(stack);
        return layout;
    }

    private void UpdateContentHostContent()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CollectionContentHost.Content = _viewModel.IsCollectionEmpty ? _emptyStateView : CollectionGrid;
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateContentHostContent();
        if (CollectionContentHost.Content == CollectionGrid)
            CollectionGrid.OnResume();

        // Ensure scroll is synced after a tab switch
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (CollectionContentHost.Content == CollectionGrid)
                CollectionGrid.ForceRedraw();
        });

        // Skip full reload when returning from card detail to preserve scroll position and grid state
        if (_skipNextReload)
        {
            _skipNextReload = false;
            return;
        }

        await _viewModel.LoadCollectionAsync();
        UpdateContentHostContent();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (CollectionContentHost.Content == CollectionGrid)
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
            if (r.NewQuantity > 0)
                _toastService.Show($"{r.NewQuantity}x {card.Name} in collection");
            else
                _toastService.Show($"{card.Name} removed from collection");
            await _viewModel.LoadCollectionAsync();
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
            await _viewModel.ClearCollectionAsync();
    }
}
