using MTGFetchMAUI.ViewModels;

namespace MTGFetchMAUI.Pages;

public partial class TradePage : ContentPage
{
    public TradePage(TradeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
