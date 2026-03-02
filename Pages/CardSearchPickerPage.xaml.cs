using AetherVault.Controls;
using AetherVault.Models;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class CardSearchPickerPage : ContentPage
{
    private readonly CardSearchPickerViewModel _viewModel;
    private TaskCompletionSource<Card?> _tcs = new();

    public CardSearchPickerPage(CardSearchPickerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.AttachGrid(CardGrid);

        CardGrid.CardClicked += OnCardClicked;

        _viewModel.SearchCompleted += OnSearchCompleted;

        // Add a close button to toolbar
        ToolbarItems.Add(new ToolbarItem("Cancel", null, async () =>
        {
            _tcs.TrySetResult(null);
            await Navigation.PopModalAsync();
        }));
    }

    private void OnSearchCompleted()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await CardGrid.ScrollToAsync(0, false);
        });
    }

    public Task<Card?> WaitForResultAsync()
    {
        return _tcs.Task;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CardGrid.OnResume();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CardGrid.OnSleep();
        _viewModel.SearchCompleted -= OnSearchCompleted;
        CardGrid.CardClicked -= OnCardClicked;
        _tcs.TrySetResult(null);
    }

    private void OnGridScrolled(object? sender, ScrolledEventArgs e)
    {
        float scrollY = (float)e.ScrollY;
        float viewportHeight = (float)CardGrid.Height;
        float contentHeight = CardGrid.ContentHeight;
        _viewModel.OnScrollChanged(scrollY, viewportHeight, contentHeight);
    }

    private async void OnCardClicked(string uuid)
    {
        var card = await _viewModel.GetCardDetailsAsync(uuid);
        _tcs.TrySetResult(card);
        await Navigation.PopModalAsync();
    }
}