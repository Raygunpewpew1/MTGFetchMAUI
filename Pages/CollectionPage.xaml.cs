using MTGFetchMAUI.ViewModels;

namespace MTGFetchMAUI.Pages;

public partial class CollectionPage : ContentPage
{
    private readonly CollectionViewModel _viewModel;
    private bool _loaded;

    public CollectionPage(CollectionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.AttachGrid(CollectionGrid);

        CollectionGrid.CardClicked += OnCardClicked;
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
        CollectionGrid.StartTimers();

        if (!_loaded)
        {
            _loaded = true;
            await _viewModel.LoadCollectionAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CollectionGrid.StopTimers();
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
        await Shell.Current.GoToAsync($"carddetail?uuid={uuid}");
    }
}
