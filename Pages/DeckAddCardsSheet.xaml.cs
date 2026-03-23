using AetherVault.ViewModels;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Devices;

namespace AetherVault.Pages;

public partial class DeckAddCardsSheet : Popup
{
    public DeckAddCardsSheet()
    {
        InitializeComponent();
        Loaded += OnSheetLoaded;
    }

    private void OnSheetLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnSheetLoaded;
        // Tall bottom sheet so the search row sits higher on screen and stays clear of the IME.
        var info = DeviceDisplay.MainDisplayInfo;
        if (info.Height <= 0 || info.Density <= 0)
            return;
        double h = info.Height / info.Density;
        SheetRootGrid.MaximumHeightRequest = Math.Min(h * 0.9, 820);
    }

    public void Init(DeckDetailViewModel viewModel)
    {
        BindingContext = viewModel;
        Closed -= OnSheetClosed;
        Closed += OnSheetClosed;
    }

    private void OnSheetClosed(object? sender, EventArgs e)
    {
        Closed -= OnSheetClosed;
        if (BindingContext is DeckDetailViewModel vm)
            vm.ClearAddCardSearch();
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        await CloseAsync();
    }
}
