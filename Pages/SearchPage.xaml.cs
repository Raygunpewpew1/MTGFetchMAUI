using AetherVault.Controls;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

/// <summary>
/// Search tab: search box, filters, and card grid. Binds to SearchViewModel; grid events open card detail or add-to-collection/deck.
/// CardGalleryContext is set on tap so CardDetailPage can swipe between cards from this result set.
/// </summary>
public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;
    private readonly CardGalleryContext _galleryContext;
    private readonly DeckBuilderService _deckService;
    private readonly IServiceProvider _serviceProvider;

    public SearchPage(SearchViewModel viewModel, CardGalleryContext galleryContext, DeckBuilderService deckService, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _galleryContext = galleryContext;
        _deckService = deckService;
        _serviceProvider = serviceProvider;
        BindingContext = _viewModel;

        // ViewModel needs the grid reference for pagination and visible-range (e.g. price loading)
        _viewModel.AttachGrid(CardGrid);

        CardGrid.CardClicked += OnCardClicked;
        CardGrid.CardLongPressed += OnCardLongPressed;

        // After search completes, scroll grid back to top
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

    /// <summary>Open card detail; set gallery context so swipe left/right shows adjacent search results.</summary>
    private async void OnCardClicked(string uuid)
    {
        _galleryContext.SetContext(CardGrid.GetAllUuids(), uuid);
        await Shell.Current.GoToAsync($"carddetail?uuid={uuid}");
    }

    /// <summary>Long-press: show action sheet to add to collection or to a deck.</summary>
    private async void OnCardLongPressed(string uuid)
    {
        var card = await _viewModel.GetCardDetailsAsync(uuid);
        if (card == null) return;

        string action = await DisplayActionSheetAsync(
            card.Name, "Cancel", null,
            "Add to Collection", "Add to Deck");

        if (action == "Add to Collection")
        {
            int currentQty = await _viewModel.GetCollectionQuantityAsync(uuid);

            var page = _serviceProvider.GetRequiredService<CollectionAddPage>();
            page.CardName = card.Name;
            page.SetInfo = $"{card.SetCode} #{card.Number}";
            page.CurrentQty = currentQty;
            await Navigation.PushModalAsync(page);
            var result = await page.WaitForResultAsync();

            if (result is CollectionAddResult r)
            {
                await _viewModel.UpdateCollectionAsync(uuid, r.NewQuantity, r.IsFoil, r.IsEtched);
                _viewModel.StatusIsError = false;
                _viewModel.StatusMessage = UserMessages.CardAddedToCollection(r.NewQuantity, card.Name);
            }
        }
        else if (action == "Add to Deck")
        {
            var deckPage = _serviceProvider.GetRequiredService<AddToDeckPage>();
            deckPage.CardUuid = card.UUID;
            deckPage.CardName = card.Name;
            await Navigation.PushModalAsync(deckPage);
            var deckResult = await deckPage.WaitForResultAsync();

            if (deckResult != null)
            {
                var validation = await _deckService.AddCardAsync(
                    deckResult.DeckId, card.UUID, deckResult.Quantity, deckResult.Section);
                if (validation.IsError)
                {
                    _viewModel.StatusIsError = true;
                    _viewModel.StatusMessage = validation.Message ?? UserMessages.CouldNotAddCardToDeck();
                }
                else
                {
                    _viewModel.StatusIsError = false;
                    _viewModel.StatusMessage = UserMessages.CardAddedToDeck(deckResult.Quantity, card.Name, deckResult.DeckName);
                }
            }
        }
    }
}
