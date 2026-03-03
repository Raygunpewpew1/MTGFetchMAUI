using AetherVault.Services;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class BinderDetailPage : ContentPage
{
    private readonly BinderDetailViewModel _viewModel;
    private readonly IToastService _toastService;

    public BinderDetailPage(BinderDetailViewModel viewModel, IToastService toastService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toastService = toastService;
        BindingContext = _viewModel;

        _viewModel.AttachGrid(BinderGrid);

        BinderGrid.CardClicked += OnCardClicked;
        BinderGrid.CardLongPressed += OnCardLongPressed;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(BinderDetailViewModel.IsBusy))
            {
                BinderLoading.IsRunning = _viewModel.IsBusy;
                BinderLoading.IsVisible = _viewModel.IsBusy;
            }
            else if (e.PropertyName == nameof(BinderDetailViewModel.BinderName))
            {
                Title = _viewModel.BinderName;
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Title = _viewModel.BinderName;
        BinderGrid.OnResume();
        await _viewModel.LoadBinderAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        BinderGrid.OnSleep();
    }

    private async void OnCardClicked(string uuid)
    {
        _viewModel.SetGalleryContext(BinderGrid.GetAllUuids(), uuid);
        await Shell.Current.GoToAsync($"carddetail?uuid={uuid}");
    }

    private async void OnCardLongPressed(string uuid)
    {
        var card = await _viewModel.GetCardDetailsAsync(uuid);
        string cardName = card?.Name ?? uuid;

        bool confirmed = await DisplayAlert(
            cardName,
            "Remove this card from the binder? It will stay in your collection.",
            "Remove from Binder",
            "Cancel");

        if (confirmed)
        {
            await _viewModel.RemoveCardFromBinderAsync(uuid);
            _toastService.Show($"{cardName} removed from binder");
        }
    }

    private async void OnDeleteBinderClicked(object? sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert(
            "Delete Binder",
            $"Delete \"{_viewModel.BinderName}\"? Cards will remain in your collection.",
            "Delete",
            "Cancel");

        if (confirmed)
        {
            await _viewModel.DeleteSelfAsync();
            await Shell.Current.GoToAsync("..");
        }
    }
}
