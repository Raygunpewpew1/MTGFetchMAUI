using AetherVault.Controls;
using AetherVault.Models;
using AetherVault.Services;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class CardSearchPickerPage : ContentPage
{
    private readonly CardSearchPickerViewModel _viewModel;
    private readonly CardManager _cardManager;
    private TaskCompletionSource<Card?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CardSearchPickerPage(CardSearchPickerViewModel viewModel, CardManager cardManager)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _cardManager = cardManager;
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

    private async void OnFiltersClicked(object? sender, EventArgs e)
    {
        var filtersPage = new SearchFiltersPage(_viewModel, _cardManager);
        await Navigation.PushAsync(filtersPage);
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