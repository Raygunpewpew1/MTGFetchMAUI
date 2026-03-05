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
        ToolbarItems.Add(new ToolbarItem("Cancel", null, () =>
        {
            if (!_tcs.Task.IsCompleted)
            {
                _tcs.TrySetResult(null);
            }

            _ = Navigation.PopModalAsync();
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
    }

    private async void OnCardSelected(Card card)
    {
        await Navigation.PopModalAsync();

        if (!_tcs.Task.IsCompleted)
        {
            _tcs.TrySetResult(card);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_tcs.Task.IsCompleted)
        {
            _tcs.TrySetResult(null);
        }

        return base.OnBackButtonPressed();
    }
}