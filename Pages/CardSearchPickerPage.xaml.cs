using AetherVault.Controls;
using AetherVault.Models;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class CardSearchPickerPage : ContentPage
{
    private readonly CardSearchPickerViewModel _viewModel;
    private TaskCompletionSource<Card?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CardSearchPickerPage(CardSearchPickerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.CardSelected += OnCardSelected;

        // Add a close button to toolbar
        ToolbarItems.Add(new ToolbarItem("Cancel", null, async () =>
        {
            _tcs.TrySetResult(null);
            await Navigation.PopModalAsync();
        }));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Focus the search entry so the keyboard pops immediately.
        MainThread.BeginInvokeOnMainThread(() => SearchEntry.Focus());
    }

    public Task<Card?> WaitForResultAsync()
    {
        return _tcs.Task;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.CardSelected -= OnCardSelected;
        _tcs.TrySetResult(null);
    }

    private async void OnCardSelected(Card card)
    {
        _tcs.TrySetResult(card);
        await Navigation.PopModalAsync();
    }
}