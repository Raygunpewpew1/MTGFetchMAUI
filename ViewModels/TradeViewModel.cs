using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;

namespace MTGFetchMAUI.ViewModels;

public partial class TradeItem : ObservableObject
{
    public Card Card { get; }
    public double Price { get; }

    public TradeItem(Card card, double price)
    {
        Card = card;
        Price = price;
    }
}

public partial class TradeViewModel : BaseViewModel
{
    private readonly CardManager _cardManager;
    private readonly IServiceProvider _serviceProvider;

    public ObservableCollection<TradeItem> YouGiveCards { get; } = new();
    public ObservableCollection<TradeItem> YouGetCards { get; } = new();

    [ObservableProperty]
    private double _youGiveTotal;

    [ObservableProperty]
    private double _youGetTotal;

    [ObservableProperty]
    private string _balanceText = "Trade is fair";

    [ObservableProperty]
    private Color _balanceColor = Colors.Green;

    public TradeViewModel(CardManager cardManager, IServiceProvider serviceProvider)
    {
        _cardManager = cardManager;
        _serviceProvider = serviceProvider;
        YouGiveCards.CollectionChanged += (s, e) => RecalculateTotals();
        YouGetCards.CollectionChanged += (s, e) => RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        YouGiveTotal = YouGiveCards.Sum(c => c.Price);
        YouGetTotal = YouGetCards.Sum(c => c.Price);

        var difference = YouGetTotal - YouGiveTotal;

        if (Math.Abs(difference) < 1.0)
        {
            BalanceText = "Trade is fair";
            BalanceColor = Colors.Green;
        }
        else if (difference > 0)
        {
            BalanceText = $"You are up ${difference:F2}";
            BalanceColor = Colors.Green;
        }
        else
        {
            BalanceText = $"You are down ${Math.Abs(difference):F2}";
            BalanceColor = Colors.Red;
        }
    }

    public async Task AddCardAsync(Card card, bool toYouGive)
    {
        if (card == null) return;

        // Fetch price
        var (found, prices) = await _cardManager.GetCardPricesAsync(card.UUID);
        double price = 0;
        if (found && prices != null)
        {
            // Default to TCGPlayer Normal Retail
            price = prices.Paper?.TCGPlayer?.RetailNormal.Price ?? 0;
            if (price == 0) price = prices.Paper?.Cardmarket?.RetailNormal.Price ?? 0;
            if (price == 0) price = prices.Paper?.CardKingdom?.RetailNormal.Price ?? 0;
        }

        var item = new TradeItem(card, price);

        if (toYouGive)
            YouGiveCards.Add(item);
        else
            YouGetCards.Add(item);
    }

    [RelayCommand]
    private async Task AddYouGiveAsync()
    {
        var page = _serviceProvider.GetService<MTGFetchMAUI.Pages.CardSearchPickerPage>();
        if (page != null)
        {
            await App.Current.MainPage.Navigation.PushModalAsync(new NavigationPage(page));
            var card = await page.WaitForResultAsync();

            if (card != null)
                await AddCardAsync(card, true);
        }
    }

    [RelayCommand]
    private void RemoveYouGive(TradeItem item)
    {
        if (item != null)
            YouGiveCards.Remove(item);
    }

    [RelayCommand]
    private async Task AddYouGetAsync()
    {
        var page = _serviceProvider.GetService<MTGFetchMAUI.Pages.CardSearchPickerPage>();
        if (page != null)
        {
            await App.Current.MainPage.Navigation.PushModalAsync(new NavigationPage(page));
            var card = await page.WaitForResultAsync();

            if (card != null)
                await AddCardAsync(card, false);
        }
    }

    [RelayCommand]
    private void RemoveYouGet(TradeItem item)
    {
        if (item != null)
            YouGetCards.Remove(item);
    }
}