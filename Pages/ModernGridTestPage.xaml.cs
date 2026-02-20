using MTGFetchMAUI.Controls;
using MTGFetchMAUI.Data;
using MTGFetchMAUI.Models;

namespace MTGFetchMAUI.Pages;

public partial class ModernGridTestPage : ContentPage
{
    private readonly ICardRepository _cardRepo;
    private bool _loaded = false;

    public ModernGridTestPage(ICardRepository cardRepo)
    {
        InitializeComponent();
        _cardRepo = cardRepo;

        CardGrid.CardClicked += (id) =>
        {
            Dispatcher.Dispatch(async () => await DisplayAlertAsync("Clicked", $"Card UUID: {id}", "OK"));
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded) return;
        _loaded = true;

        LoadingSpinner.IsVisible = true;
        LoadingSpinner.IsRunning = true;

        // Offload data fetching
        await Task.Run(async () =>
        {
            try
            {
                // Fetch 500 cards
                // Empty string might not be supported, try "type:creature" or similar if needed.
                // Assuming "e" matches many cards.
                var cards = await _cardRepo.SearchCardsAsync("e", 500);

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
                    await DisplayAlertAsync("Error", ex.Message, "OK");
                    LoadingSpinner.IsVisible = false;
                });
            }
        });
    }
}
