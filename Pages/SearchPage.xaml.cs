using AetherVault.Controls;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

/// <summary>
/// Search tab: search box, filters, and card grid. Binds to SearchViewModel; grid events open card detail or add-to-collection/deck.
/// CardGalleryContext is set on tap so CardDetailPage can swipe between cards from this result set.
/// Easter egg: tap the top-right corner 7 times within 6 seconds to play a sound.
/// </summary>
public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;
    private readonly CardGalleryContext _galleryContext;
    private readonly DeckBuilderService _deckService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEasterEggSoundService _easterEggSound;
    private int _easterEggTapCount;
    private IDispatcherTimer? _easterEggResetTimer;

    public SearchPage(SearchViewModel viewModel, CardGalleryContext galleryContext, DeckBuilderService deckService, IServiceProvider serviceProvider, IEasterEggSoundService easterEggSound)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _galleryContext = galleryContext;
        _deckService = deckService;
        _serviceProvider = serviceProvider;
        _easterEggSound = easterEggSound;
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

    private void OnEasterEggTap(object? sender, TappedEventArgs e)
    {
        _easterEggResetTimer?.Stop();
        _easterEggTapCount++;
        Logger.LogStuff($"[EasterEgg] Tap {_easterEggTapCount}/7", LogLevel.Debug);
        if (_easterEggTapCount >= 7)
        {
            _easterEggTapCount = 0;
            Logger.LogStuff("[EasterEgg] Triggered — playing sound.", LogLevel.Debug);
            _easterEggSound.Play();
            return;
        }
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(6);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            _easterEggTapCount = 0;
            timer.Stop();
        };
        timer.Start();
        _easterEggResetTimer = timer;
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
            deckPage.CardUuid = card.Uuid;
            deckPage.CardName = card.Name;
            await Navigation.PushModalAsync(deckPage);
            var deckResult = await deckPage.WaitForResultAsync();

            if (deckResult != null)
            {
                var validation = await _deckService.AddCardAsync(
                    deckResult.DeckId, card.Uuid, deckResult.Quantity, deckResult.Section);
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
