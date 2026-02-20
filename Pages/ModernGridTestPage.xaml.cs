using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Data;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;

namespace MTGFetchMAUI.Pages;

public partial class ModernGridTestPage : ContentPage
{
    private readonly CardManager _cardManager;
    private bool _loaded = false;

    public ModernGridTestPage(CardManager cardManager)
    {
        InitializeComponent();
        _cardManager = cardManager;

        CardGrid.CardClicked += (id) =>
        {
            Dispatcher.Dispatch(async () => await DisplayAlert("Clicked", $"Card UUID: {id}", "OK"));
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded) return;

        LoadingSpinner.IsVisible = true;
        LoadingSpinner.IsRunning = true;

        // Ensure database connection
        if (!_cardManager.DatabaseManager.IsConnected)
        {
            await _cardManager.InitializeAsync();
        }

        if (!_cardManager.DatabaseManager.IsConnected)
        {
            await DisplayAlert("Error", "MTG database not connected. Please ensure database is downloaded.", "OK");
            LoadingSpinner.IsVisible = false;
            LoadingSpinner.IsRunning = false;
            return;
        }

        _loaded = true;

        // Offload data fetching
        await Task.Run(async () =>
        {
            try
            {
                // Fetch 500 cards
                // Empty string might not be supported, try "type:creature" or similar if needed.
                // Assuming "e" matches many cards.
                var cards = await _cardManager.SearchCardsAsync("e", 500);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CardGrid.SetCards(cards);
                    LoadingSpinner.IsVisible = false;
                    LoadingSpinner.IsRunning = false;
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", ex.Message, "OK");
                    LoadingSpinner.IsVisible = false;
                });
            }
        });
    }
}
