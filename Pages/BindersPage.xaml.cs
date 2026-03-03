using AetherVault.Models;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class BindersPage : ContentPage
{
    private readonly BindersViewModel _viewModel;

    public BindersPage(BindersViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadBindersAsync();
    }

    private async void OnCreateBinderClicked(object? sender, EventArgs e)
    {
        string? name = await DisplayPromptAsync(
            "New Binder",
            "Enter a name for your binder:",
            placeholder: "e.g. Trade Binder, Commander Staples...",
            maxLength: 60);

        if (!string.IsNullOrWhiteSpace(name))
            await _viewModel.CreateBinderAsync(name);
    }

    private async void OnBinderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not BinderEntity binder) return;
        await Shell.Current.GoToAsync($"binderdetail?binderId={binder.Id}&binderName={Uri.EscapeDataString(binder.Name)}");
    }

    private async void OnDeleteSwipeClicked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem item && item.CommandParameter is BinderEntity binder)
        {
            bool confirmed = await DisplayAlert(
                "Delete Binder",
                $"Delete \"{binder.Name}\"? Cards will remain in your collection.",
                "Delete",
                "Cancel");

            if (confirmed)
                await _viewModel.DeleteBinderAsync(binder.Id);
        }
    }
}
