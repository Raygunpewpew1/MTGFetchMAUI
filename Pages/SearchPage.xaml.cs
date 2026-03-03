using AetherVault.Controls;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;
    private readonly IToastService _toastService;
    private readonly CardGalleryContext _galleryContext;
    private readonly DeckBuilderService _deckService;

    public SearchPage(SearchViewModel viewModel, IToastService toastService, CardGalleryContext galleryContext, DeckBuilderService deckService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _toastService = toastService;
        _galleryContext = galleryContext;
        _deckService = deckService;
        BindingContext = _viewModel;

        _viewModel.AttachGrid(CardGrid);

        CardGrid.CardClicked += OnCardClicked;
        CardGrid.CardLongPressed += OnCardLongPressed;

        _viewModel.SearchCompleted += () =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await CardGrid.ScrollToAsync(0, false);
            });
        };
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
        _galleryContext.SetContext(CardGrid.GetAllUuids(), uuid);
        await Shell.Current.GoToAsync($"carddetail?uuid={uuid}");
    }

    private async void OnCardLongPressed(string uuid)
    {
        var card = await _viewModel.GetCardDetailsAsync(uuid);
        if (card == null) return;

        string action = await DisplayActionSheet(
            card.Name, "Cancel", null,
            "Add to Collection", "Add to Deck");

        if (action == "Add to Collection")
        {
            int currentQty = await _viewModel.GetCollectionQuantityAsync(uuid);

            var page = new CollectionAddPage(
                card.Name,
                $"{card.SetCode} #{card.Number}",
                currentQty);

            await Navigation.PushModalAsync(page);
            var result = await page.WaitForResultAsync();

            if (result is CollectionAddResult r)
            {
                await _viewModel.UpdateCollectionAsync(uuid, r.NewQuantity, r.IsFoil, r.IsEtched);
                if (r.NewQuantity > 0)
                    _toastService.Show($"{r.NewQuantity}x {card.Name} in collection");
                else
                    _toastService.Show($"{card.Name} removed from collection");
            }
        }
        else if (action == "Add to Deck")
        {
            var deckPage = new AddToDeckPage(_deckService, card.UUID, card.Name);
            await Navigation.PushModalAsync(deckPage);
            var deckResult = await deckPage.WaitForResultAsync();

            if (deckResult != null)
            {
                var validation = await _deckService.AddCardAsync(
                    deckResult.DeckId, card.UUID, deckResult.Quantity, deckResult.Section);
                if (validation.IsError)
                    _toastService.Show(validation.Message ?? "Could not add card to deck.");
                else
                    _toastService.Show($"{deckResult.Quantity}x {card.Name} added to {deckResult.DeckName}.");
            }
        }
    }
}
