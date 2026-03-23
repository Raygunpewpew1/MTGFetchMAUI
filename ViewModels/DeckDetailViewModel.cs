using System.Collections.ObjectModel;
using System.ComponentModel;
using AetherVault.Constants;
using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>
/// Represents a single card row in the deck editor list.
/// </summary>
public partial class DeckCardDisplayItem : ObservableObject
{
    [ObservableProperty]
    public partial DeckCardEntity Entity { get; set; } = null!;

    [ObservableProperty]
    public partial Card Card { get; set; } = null!;

    /// <summary>Copies of this printing in the user's collection (0 if none).</summary>
    [ObservableProperty]
    public partial int OwnedQuantity { get; set; }

    public string DisplayName => Card?.Name ?? Entity.CardId;
    public string ManaCostText => Card?.ManaCost ?? "";
    public string CardTypeText => Card?.CardType ?? "";
    public string ImageId => Card?.ImageId ?? "";
    public double Cmc => Card?.EffectiveManaValue ?? 0;
    /// <summary>Rules text for quick-detail popup.</summary>
    public string RulesText => Card?.Text ?? "";
    /// <summary>Power/toughness or loyalty for quick-detail popup.</summary>
    public string PtOrLoyaltyText =>
        Card == null ? "" :
        !string.IsNullOrEmpty(Card.Power) && !string.IsNullOrEmpty(Card.Toughness) ? $"{Card.Power}/{Card.Toughness}" :
        !string.IsNullOrEmpty(Card.Loyalty) ? $"Loyalty: {Card.Loyalty}" : "";
    public string CardUuid => Entity.CardId;
    /// <summary>e.g. "2 in Main" for quick-detail popup.</summary>
    public string InDeckSummary => $"{Entity.Quantity} in {Entity.Section}";

    /// <summary>Quantity badge binding; use <see cref="SetDeckQuantity"/> so the UI updates without a full reload.</summary>
    public string DeckQtyLabel => Entity.Quantity.ToString();

    /// <summary>Short collection hint for list rows.</summary>
    public string OwnedShortText => OwnedQuantity <= 0 ? "—" : $"Own {OwnedQuantity}";

    /// <summary>True when the deck plays more copies than the user owns (both must be &gt; 0).</summary>
    public bool IsOverCollection => OwnedQuantity > 0 && Entity.Quantity > OwnedQuantity;

    partial void OnOwnedQuantityChanged(int value)
    {
        OnPropertyChanged(nameof(OwnedShortText));
        OnPropertyChanged(nameof(IsOverCollection));
    }

    /// <summary>Updates in-deck quantity and notifies quantity-related bindings.</summary>
    public void SetDeckQuantity(int quantity)
    {
        Entity.Quantity = quantity;
        OnPropertyChanged(nameof(InDeckSummary));
        OnPropertyChanged(nameof(DeckQtyLabel));
        OnPropertyChanged(nameof(IsOverCollection));
    }

    /// <summary>Dark tint for list row background based on card color identity (WUBRG).</summary>
    public Color StripBackgroundColor => DeckDetailViewModel.GetStripBackgroundColorFromIdentity(Card?.Colors ?? Card?.ColorIdentity ?? "");

    /// <summary>Multi-select mode for bulk deck edits.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Search result row in the add-cards sheet (staging + commander quick-add).</summary>
public partial class DeckAddSearchResultRow : ObservableObject
{
    public DeckAddSearchResultRow(Card card, bool initiallyStaged)
    {
        Card = card;
        IsStaged = initiallyStaged;
    }

    public Card Card { get; }

    [ObservableProperty]
    public partial bool IsStaged { get; set; }
}

/// <summary>Card queued for batch add from the add-cards sheet.</summary>
public partial class StagedDeckAddItem : ObservableObject
{
    public StagedDeckAddItem(Card card, int quantity = 1)
    {
        Card = card;
        Quantity = quantity;
    }

    public Card Card { get; }

    [ObservableProperty]
    public partial int Quantity { get; set; }
}

/// <summary>
/// Grouped list of DeckCardDisplayItems for CollectionView IsGrouped support.
/// </summary>
public class DeckCardGroup : ObservableCollection<DeckCardDisplayItem>
{
    private int _totalQuantity;

    public string GroupName { get; }

    /// <summary>Sum of Entity.Quantity for all items in this group (e.g. for "Creatures (32)" header).</summary>
    public int TotalQuantity
    {
        get => _totalQuantity;
        private set
        {
            if (_totalQuantity == value) return;
            _totalQuantity = value;
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalQuantity)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderText)));
        }
    }

    /// <summary>e.g. "Creatures (32)" for section header.</summary>
    public string HeaderText => $"{GroupName} ({TotalQuantity})";

    public DeckCardGroup(string name, IEnumerable<DeckCardDisplayItem> items, int count)
        : base([.. items])
    {
        GroupName = name;
        _totalQuantity = count;
    }

    public void RecalculateTotal() => TotalQuantity = this.Sum(i => i.Entity.Quantity);
}

public partial class DeckDetailViewModel(
    DeckBuilderService deckService,
    ICardRepository cardRepository,
    ICollectionRepository collectionRepository,
    CardManager cardManager,
    IToastService toast) : BaseViewModel
{
    private readonly DeckBuilderService _deckService = deckService;
    private readonly ICardRepository _cardRepository = cardRepository;
    private readonly ICollectionRepository _collectionRepository = collectionRepository;
    private readonly CardManager _cardManager = cardManager;
    private readonly IToastService _toast = toast;
    private Dictionary<string, Card> _cardMapCache = [];
    private int _deckId;
    private CancellationTokenSource? _addCardSearchCts;
    private int _addCardSearchGeneration;
    private CancellationTokenSource? _deckListFilterCts;

    /// <summary>Raised on the main thread after deck data has been reloaded (so the page can force layout/redraw).</summary>
    public event Action? ReloadCompleted;

    /// <summary>Raised when the user requests the quick-detail popup for a deck list card.</summary>
    public event Action<DeckCardDisplayItem>? RequestShowQuickDetail;

    private LastAddedInfo? _lastAdded;

    [ObservableProperty]
    public partial DeckEntity? Deck { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<DeckCardGroup> MainDeckGroups { get; set; } = [];

    /// <summary>Main-deck groups after applying the in-deck filter (same item refs as Main when filter is empty).</summary>
    [ObservableProperty]
    public partial ObservableCollection<DeckCardGroup> FilteredMainDeckGroups { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> SideboardCards { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> FilteredSideboardCards { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> CommanderCards { get; set; } = [];

    /// <summary>First commander card for the full-size hero display (partner: primary only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCommander))]
    public partial DeckCardDisplayItem? FirstCommander { get; set; }

    /// <summary>Commander cards after the first (e.g. partner), for the compact list below the hero.</summary>
    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> AdditionalCommanderCards { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> FilteredAdditionalCommanderCards { get; set; } = [];

    /// <summary>False when deck filter text hides the primary commander art (name/type mismatch).</summary>
    [ObservableProperty]
    public partial bool IsCommanderHeroVisible { get; set; }

    [ObservableProperty]
    public partial bool ShowCommanderHiddenByFilterHint { get; set; }

    /// <summary>Filter for cards already in this deck (Main / Sideboard / Commander lists).</summary>
    [ObservableProperty]
    public partial string DeckListFilterText { get; set; } = "";

    /// <summary>Hero art + menu when commander exists and passes in-deck filter.</summary>
    public bool ShowCommanderHeroArt => !HasNoCommander && IsCommanderHeroVisible;

    /// <summary>Show partner / other commander rows when any remain after filter.</summary>
    public bool ShowFilteredPartnersSection => FilteredAdditionalCommanderCards.Count > 0;

    [ObservableProperty]
    public partial DeckStats Stats { get; set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommanderTab))]
    [NotifyPropertyChangedFor(nameof(IsMainTab))]
    [NotifyPropertyChangedFor(nameof(IsSideboardTab))]
    [NotifyPropertyChangedFor(nameof(IsStatsTab))]
    [NotifyPropertyChangedFor(nameof(Tab0Color))]
    [NotifyPropertyChangedFor(nameof(Tab1Color))]
    [NotifyPropertyChangedFor(nameof(Tab2Color))]
    [NotifyPropertyChangedFor(nameof(Tab3Color))]
    [NotifyPropertyChangedFor(nameof(Tab0Font))]
    [NotifyPropertyChangedFor(nameof(Tab1Font))]
    [NotifyPropertyChangedFor(nameof(Tab2Font))]
    [NotifyPropertyChangedFor(nameof(Tab3Font))]
    [NotifyPropertyChangedFor(nameof(Tab0Indicator))]
    [NotifyPropertyChangedFor(nameof(Tab1Indicator))]
    [NotifyPropertyChangedFor(nameof(Tab2Indicator))]
    [NotifyPropertyChangedFor(nameof(Tab3Indicator))]
    public partial int SelectedSectionIndex { get; set; } = 1; // Default to Main tab

    public bool IsCommanderTab => SelectedSectionIndex == 0;
    public bool IsMainTab => SelectedSectionIndex == 1;
    public bool IsSideboardTab => SelectedSectionIndex == 2;
    public bool IsStatsTab => SelectedSectionIndex == 3;

    private static readonly Color TabSelectedColor = Color.FromArgb("#03DAC5");
    private static readonly Color TabUnselectedColor = Color.FromArgb("#888888");

    private Color GetTabColor(int index) => SelectedSectionIndex == index ? TabSelectedColor : TabUnselectedColor;
    private FontAttributes GetTabFont(int index) => SelectedSectionIndex == index ? FontAttributes.Bold : FontAttributes.None;
    private double GetTabIndicator(int index) => SelectedSectionIndex == index ? 1 : 0;

    public Color Tab0Color => GetTabColor(0);
    public Color Tab1Color => GetTabColor(1);
    public Color Tab2Color => GetTabColor(2);
    public Color Tab3Color => GetTabColor(3);

    public FontAttributes Tab0Font => GetTabFont(0);
    public FontAttributes Tab1Font => GetTabFont(1);
    public FontAttributes Tab2Font => GetTabFont(2);
    public FontAttributes Tab3Font => GetTabFont(3);

    public double Tab0Indicator => GetTabIndicator(0);
    public double Tab1Indicator => GetTabIndicator(1);
    public double Tab2Indicator => GetTabIndicator(2);
    public double Tab3Indicator => GetTabIndicator(3);

    [ObservableProperty]
    public partial int TotalCardCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeckSummaryText))]
    public partial int MainDeckCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeckSummaryText))]
    [NotifyPropertyChangedFor(nameof(SideboardHeaderText))]
    public partial int SideboardCount { get; set; }

    /// <summary>e.g. "100 main deck / 0 sideboard" for the bottom summary bar.</summary>
    public string DeckSummaryText => $"{MainDeckCount} main deck / {SideboardCount} sideboard";

    /// <summary>e.g. "Sideboard (15)" for the sideboard section header.</summary>
    public string SideboardHeaderText => $"Sideboard ({SideboardCount})";

    [ObservableProperty]
    public partial string DeckFormat { get; set; } = "";

    public bool HasNoCommander => CommanderCards.Count == 0;
    /// <summary>True when there are two or more commander cards (e.g. partner).</summary>
    public bool HasMultipleCommanders => CommanderCards.Count > 1;

    // ── Inline add-card search (visible on Commander, Main, Sideboard tabs) ──

    [ObservableProperty]
    public partial string AddCardSearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsAddCardSearchBusy { get; set; }

    /// <summary>When true, add-card search only returns cards that are in the user's collection.</summary>
    [ObservableProperty]
    public partial bool AddCardSearchOnlyCollection { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<DeckAddSearchResultRow> AddCardSearchResultRows { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<StagedDeckAddItem> StagedAddItems { get; set; } = [];

    /// <summary>True when the add-cards sheet should show batch-add UI (Main / Sideboard).</summary>
    public bool IsStagedAddActive => SelectedSectionIndex is 1 or 2;

    /// <summary>Summary line for staged cards in the add sheet.</summary>
    public string StagedAddSummaryText =>
        StagedAddItems.Count == 0
            ? "No cards staged."
            : $"{StagedAddItems.Count} type(s) • {StagedAddItems.Sum(s => s.Quantity)} card(s) staged";

    [ObservableProperty]
    public partial bool IsSelectionMode { get; set; }

    /// <summary>Selected rows in the current section (Commander / Main / Sideboard).</summary>
    public int SelectedCardCount => GetSelectedVisibleItems().Count();

    public bool HasSelection => SelectedCardCount > 0;

    private IAsyncRelayCommand? _undoLastAddedCommand;
    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand UndoLastAddedCommand => _undoLastAddedCommand ??= new AsyncRelayCommand(UndoLastAddedAsync);

    private IAsyncRelayCommand? _suggestLandsCommand;
    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand SuggestLandsCommand => _suggestLandsCommand ??= new AsyncRelayCommand(SuggestLandsAsync);

    private IRelayCommand? _toggleSelectionModeCommand;
    public IRelayCommand ToggleSelectionModeCommand => _toggleSelectionModeCommand ??= new RelayCommand(ToggleSelectionMode);

    private IRelayCommand? _deckListItemTappedCommand;
    public IRelayCommand DeckListItemTappedCommand => _deckListItemTappedCommand ??= new RelayCommand<DeckCardDisplayItem?>(DeckListItemTapped);

    private IRelayCommand? _selectAllInCurrentSectionCommand;
    public IRelayCommand SelectAllInCurrentSectionCommand => _selectAllInCurrentSectionCommand ??= new RelayCommand(SelectAllInCurrentSection);

    private IRelayCommand? _clearDeckSelectionCommand;
    public IRelayCommand ClearDeckSelectionCommand => _clearDeckSelectionCommand ??= new RelayCommand(ClearDeckSelection);

    private IAsyncRelayCommand? _bulkRemoveSelectionCommand;
    public IAsyncRelayCommand BulkRemoveSelectionCommand => _bulkRemoveSelectionCommand ??= new AsyncRelayCommand(BulkRemoveSelectionAsync);

    private IAsyncRelayCommand? _bulkMoveSelectionToMainCommand;
    public IAsyncRelayCommand BulkMoveSelectionToMainCommand => _bulkMoveSelectionToMainCommand ??= new AsyncRelayCommand(BulkMoveSelectionToMainAsync);

    private IAsyncRelayCommand? _bulkMoveSelectionToSideboardCommand;
    public IAsyncRelayCommand BulkMoveSelectionToSideboardCommand => _bulkMoveSelectionToSideboardCommand ??= new AsyncRelayCommand(BulkMoveSelectionToSideboardAsync);

    private IAsyncRelayCommand? _bulkIncrementSelectionCommand;
    public IAsyncRelayCommand BulkIncrementSelectionCommand => _bulkIncrementSelectionCommand ??= new AsyncRelayCommand(BulkIncrementSelectionAsync);

    private IAsyncRelayCommand? _bulkDecrementSelectionCommand;
    public IAsyncRelayCommand BulkDecrementSelectionCommand => _bulkDecrementSelectionCommand ??= new AsyncRelayCommand(BulkDecrementSelectionAsync);

    private IAsyncRelayCommand? _moveCardRowToSideboardCommand;
    public IAsyncRelayCommand MoveCardRowToSideboardCommand => _moveCardRowToSideboardCommand ??= new AsyncRelayCommand<DeckCardDisplayItem?>(MoveCardRowToSideboardAsync);

    private IAsyncRelayCommand? _moveCardRowToMainCommand;
    public IAsyncRelayCommand MoveCardRowToMainCommand => _moveCardRowToMainCommand ??= new AsyncRelayCommand<DeckCardDisplayItem?>(MoveCardRowToMainAsync);

    private IRelayCommand? _toggleStagedSearchRowCommand;
    public IRelayCommand ToggleStagedSearchRowCommand => _toggleStagedSearchRowCommand ??= new RelayCommand<DeckAddSearchResultRow?>(ToggleStagedSearchRow);

    private IRelayCommand? _incrementStagedAddQuantityCommand;
    public IRelayCommand IncrementStagedAddQuantityCommand => _incrementStagedAddQuantityCommand ??= new RelayCommand<StagedDeckAddItem?>(IncrementStagedAddQuantity);

    private IRelayCommand? _decrementStagedAddQuantityCommand;
    public IRelayCommand DecrementStagedAddQuantityCommand => _decrementStagedAddQuantityCommand ??= new RelayCommand<StagedDeckAddItem?>(DecrementStagedAddQuantity);

    private IAsyncRelayCommand? _addStagedCardsToDeckCommand;
    public IAsyncRelayCommand AddStagedCardsToDeckCommand => _addStagedCardsToDeckCommand ??= new AsyncRelayCommand(AddStagedCardsToDeckAsync);

    private IRelayCommand? _clearStagedAddsCommand;
    public IRelayCommand ClearStagedAddsCommand => _clearStagedAddsCommand ??= new RelayCommand(ClearStagedAdds);

    [RelayCommand]
    private void SelectCommander() => SelectedSectionIndex = 0;

    [RelayCommand]
    private void SelectMain() => SelectedSectionIndex = 1;

    [RelayCommand]
    private void SelectSideboard() => SelectedSectionIndex = 2;

    [RelayCommand]
    private void SelectStats() => SelectedSectionIndex = 3;

    [RelayCommand]
    private void ShowCardQuickDetail(DeckCardDisplayItem item) => RequestShowQuickDetail?.Invoke(item);

    partial void OnAddCardSearchTextChanged(string value)
    {
        _addCardSearchCts?.Cancel();
        _addCardSearchCts = new CancellationTokenSource();
        var token = _addCardSearchCts.Token;
        Task.Delay(750, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                MainThread.BeginInvokeOnMainThread(() => _ = ExecuteAddCardSearchAsync());
        }, TaskContinuationOptions.None);
    }

    partial void OnAddCardSearchOnlyCollectionChanged(bool value)
    {
        if (!string.IsNullOrWhiteSpace(AddCardSearchText))
            _ = ExecuteAddCardSearchAsync();
    }

    partial void OnSelectedSectionIndexChanged(int value)
    {
        IsSelectionMode = false;
        ClearDeckListSelection();
        OnPropertyChanged(nameof(IsStagedAddActive));
    }

    partial void OnIsSelectionModeChanged(bool value)
    {
        if (!value)
            ClearDeckListSelection();
    }

    partial void OnDeckListFilterTextChanged(string value)
    {
        _deckListFilterCts?.Cancel();
        _deckListFilterCts = new CancellationTokenSource();
        var token = _deckListFilterCts.Token;
        Task.Delay(350, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                MainThread.BeginInvokeOnMainThread(RefreshDeckListFilter);
        }, TaskContinuationOptions.None);
    }

    private IAsyncRelayCommand? _addCardSearchCommand;
    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand AddCardSearchCommand => _addCardSearchCommand ??= new AsyncRelayCommand(ExecuteAddCardSearchAsync);

    private async Task ExecuteAddCardSearchAsync()
    {
        var query = (AddCardSearchText ?? "").Trim();
        int myGen = ++_addCardSearchGeneration;

        IsAddCardSearchBusy = true;
        try
        {
            if (string.IsNullOrEmpty(query))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (myGen != _addCardSearchGeneration) return;
                    AddCardSearchResultRows = [];
                });
                return;
            }

            var cards = AddCardSearchOnlyCollection
                ? await _cardManager.SearchInCollectionAsync(query, 50)
                : await _cardManager.SearchCardsAsync(query, 50);
            if (myGen != _addCardSearchGeneration) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var stagedUuids = new HashSet<string>(StagedAddItems.Select(s => s.Card.Uuid));
                AddCardSearchResultRows = new ObservableCollection<DeckAddSearchResultRow>(
                    cards.Select(c => new DeckAddSearchResultRow(c, stagedUuids.Contains(c.Uuid))));
            });
        }
        finally
        {
            if (myGen == _addCardSearchGeneration)
                MainThread.BeginInvokeOnMainThread(() => IsAddCardSearchBusy = false);
        }
    }

    /// <summary>Clears add-card search state when the sheet closes.</summary>
    public void ClearAddCardSearch()
    {
        _addCardSearchCts?.Cancel();
        AddCardSearchText = "";
        AddCardSearchResultRows = [];
        StagedAddItems = [];
        IsAddCardSearchBusy = false;
        OnPropertyChanged(nameof(StagedAddSummaryText));
    }

    private static void ApplyOwnedQuantities(List<DeckCardDisplayItem> items, Dictionary<string, int> qtyOwned)
    {
        foreach (var item in items)
            item.OwnedQuantity = qtyOwned.GetValueOrDefault(item.Entity.CardId, 0);
    }

    [RelayCommand]
    private async Task AddCardFromSearchAsync(Card? card)
    {
        if (card == null || Deck == null) return;

        if (SelectedSectionIndex == 0) // Commander
        {
            var result = await _deckService.SetCommanderAsync(Deck.Id, card.Uuid);
            if (result.IsError)
            {
                StatusIsError = true;
                StatusMessage = result.Message ?? UserMessages.CouldNotSetCommander();
            }
            else
            {
                StatusIsError = false;
                StatusMessage = !string.IsNullOrWhiteSpace(result.Message) ? result.Message : $"{card.Name} set as commander.";
                await ReloadAsync(preserveState: true);
            }
            return;
        }

        string section = SelectedSectionIndex == 2 ? "Sideboard" : "Main";
        var addResult = await _deckService.AddCardAsync(Deck.Id, card.Uuid, 1, section);
        if (addResult.IsError)
        {
            StatusIsError = true;
            StatusMessage = addResult.Message ?? UserMessages.CouldNotAddCardToDeck();
        }
        else
        {
            StatusIsError = false;
            StatusMessage = UserMessages.CardsAddedToSection(1, card.Name, section);
            RegisterLastAdded(card.Uuid, card.Name, section, 1);
            await ReloadAsync(preserveState: true);
        }
    }

    public void RegisterLastAdded(string cardId, string cardName, string section, int quantity)
    {
        _lastAdded = new LastAddedInfo(cardId, section, quantity, cardName);
        var summary = $"{quantity}× {cardName} added to {section}.";
        _toast.ShowWithAction(summary, "Undo", () => _ = UndoLastAddedAsync(), durationMs: 5000);
    }

    private async Task UndoLastAddedAsync()
    {
        if (Deck == null || _lastAdded is null) return;

        try
        {
            var cards = await _deckService.GetDeckCardsAsync(Deck.Id);
            var existing = cards.FirstOrDefault(c => c.CardId == _lastAdded.CardId && c.Section == _lastAdded.Section);
            if (existing == null) return;

            int newQty = existing.Quantity - _lastAdded.Quantity;
            if (newQty < 0) newQty = 0;

            var result = await _deckService.UpdateQuantityAsync(Deck.Id, _lastAdded.CardId, newQty, _lastAdded.Section);
            if (result.IsSuccess)
            {
                _toast.Show(UserMessages.UndidLastAdd(_lastAdded.Quantity, _lastAdded.CardName, _lastAdded.Section));
                _lastAdded = null;
                await ReloadAsync(preserveState: true);
            }
            else
            {
                _toast.Show(result.Message ?? UserMessages.CouldNotUndoLastAdd());
            }
        }
        catch (Exception ex)
        {
            _toast.Show(UserMessages.CouldNotUndoLastAdd(ex.Message));
        }
    }

    public async Task ReloadAsync(bool preserveState = false) => await LoadAsync(_deckId, preserveState);

    public async Task LoadAsync(int deckId, bool preserveState = false)
    {
        _deckId = deckId;
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;

        if (!preserveState)
            StatusMessage = UserMessages.LoadingDeck;

        try
        {
            var newDeck = await _deckService.GetDeckAsync(deckId);
            if (newDeck == null)
            {
                StatusIsError = true;
                StatusMessage = UserMessages.DeckNotFound;
                return;
            }

            if (!preserveState)
            {
                Deck = newDeck;
            }

            DeckFormat = EnumExtensions.ParseDeckFormat(newDeck.Format).ToDisplayName();

            var cardEntities = await _deckService.GetDeckCardsAsync(deckId);
            var uuids = cardEntities.Select(c => c.CardId).Distinct().ToArray();

            Dictionary<string, Card> cardMap = uuids.Length > 0
                ? await _cardRepository.GetCardsByUuiDsAsync(uuids)
                : [];

            var (commander, main, sideboard) = MapEntitiesToSectionLists(cardEntities, cardMap);

            var qtyOwned = await _collectionRepository.GetQuantitiesByUuidsAsync(uuids);
            ApplyOwnedQuantities(commander, qtyOwned);
            ApplyOwnedQuantities(main, qtyOwned);
            ApplyOwnedQuantities(sideboard, qtyOwned);

            var mainDeckGroups = BuildGroups(main);
            int mainDeckCount = main.Sum(i => i.Entity.Quantity);
            int sideboardCount = sideboard.Sum(i => i.Entity.Quantity);
            var totalCardCount = cardEntities.Sum(c => c.Quantity);
            var stats = ComputeStats(cardEntities, cardMap);
            var validation = await _deckService.ValidateDeckAsync(deckId);
            var statusMessage = GetValidationStatusMessage(validation, totalCardCount);

            _cardMapCache = cardMap;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                CommanderCards = new ObservableCollection<DeckCardDisplayItem>(commander);
                FirstCommander = commander.Count > 0 ? commander[0] : null;
                AdditionalCommanderCards = commander.Count > 1
                    ? new ObservableCollection<DeckCardDisplayItem>(commander.Skip(1))
                    : [];
                SideboardCards = new ObservableCollection<DeckCardDisplayItem>(sideboard);
                MainDeckGroups = mainDeckGroups;
                MainDeckCount = mainDeckCount;
                SideboardCount = sideboardCount;
                TotalCardCount = totalCardCount;
                Stats = stats;
                OnPropertyChanged(nameof(HasNoCommander));
                OnPropertyChanged(nameof(HasMultipleCommanders));
                RefreshDeckListFilter();
                StatusIsError = validation.Level == ValidationLevel.Error;
                StatusMessage = statusMessage;
                ReloadCompleted?.Invoke();
            });
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.LoadFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Returns the ordered list of card UUIDs for the current tab (Commander, Main, or Sideboard) for swipe context.</summary>
    public IReadOnlyList<string> GetOrderedUuidsForCurrentSection()
    {
        return SelectedSectionIndex switch
        {
            0 => [.. CommanderCards.Select(x => x.CardUuid)],
            1 => [.. FilteredMainDeckGroups.SelectMany(g => g).Select(x => x.CardUuid)],
            2 => [.. FilteredSideboardCards.Select(x => x.CardUuid)],
            _ => []
        };
    }

    private static bool MatchesDeckListFilter(DeckCardDisplayItem item, string qTrimmed)
    {
        if (string.IsNullOrEmpty(qTrimmed)) return true;
        return item.DisplayName.Contains(qTrimmed, StringComparison.OrdinalIgnoreCase)
               || (item.CardTypeText ?? "").Contains(qTrimmed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rebuilds filtered collections from the authoritative lists (call after load and local mutations).</summary>
    public void RefreshDeckListFilter()
    {
        var q = (DeckListFilterText ?? "").Trim();

        if (string.IsNullOrEmpty(q))
        {
            FilteredMainDeckGroups = MainDeckGroups;
            FilteredSideboardCards = SideboardCards;
            FilteredAdditionalCommanderCards = AdditionalCommanderCards;
            IsCommanderHeroVisible = FirstCommander != null;
            ShowCommanderHiddenByFilterHint = false;
        }
        else
        {
            var filteredMain = new ObservableCollection<DeckCardGroup>();
            foreach (var g in MainDeckGroups)
            {
                var items = g.Where(i => MatchesDeckListFilter(i, q)).ToList();
                if (items.Count == 0) continue;
                int groupQty = items.Sum(x => x.Entity.Quantity);
                filteredMain.Add(new DeckCardGroup(g.GroupName, items, groupQty));
            }

            FilteredMainDeckGroups = filteredMain;
            FilteredSideboardCards = new ObservableCollection<DeckCardDisplayItem>(
                SideboardCards.Where(i => MatchesDeckListFilter(i, q)));
            FilteredAdditionalCommanderCards = new ObservableCollection<DeckCardDisplayItem>(
                AdditionalCommanderCards.Where(i => MatchesDeckListFilter(i, q)));

            IsCommanderHeroVisible = FirstCommander != null && MatchesDeckListFilter(FirstCommander, q);
            ShowCommanderHiddenByFilterHint = FirstCommander != null && !IsCommanderHeroVisible;
        }

        OnPropertyChanged(nameof(ShowCommanderHeroArt));
        OnPropertyChanged(nameof(ShowFilteredPartnersSection));
    }

    private static (List<DeckCardDisplayItem> commander, List<DeckCardDisplayItem> main, List<DeckCardDisplayItem> sideboard)
        MapEntitiesToSectionLists(List<DeckCardEntity> cardEntities, Dictionary<string, Card> cardMap)
    {
        var commander = new List<DeckCardDisplayItem>();
        var main = new List<DeckCardDisplayItem>();
        var sideboard = new List<DeckCardDisplayItem>();

        foreach (var entity in cardEntities)
        {
            cardMap.TryGetValue(entity.CardId, out var card);
            var item = new DeckCardDisplayItem
            {
                Entity = entity,
                Card = card ?? new Card { Name = entity.CardId }
            };

            switch (entity.Section)
            {
                case "Commander": commander.Add(item); break;
                case "Sideboard": sideboard.Add(item); break;
                default: main.Add(item); break;
            }
        }

        return (commander, main, sideboard);
    }

    private static string GetValidationStatusMessage(ValidationResult validation, int totalCardCount)
    {
        var baseMessage = $"{totalCardCount} cards";
        if (validation.Level == ValidationLevel.Warning && !string.IsNullOrWhiteSpace(validation.Message))
            return $"{baseMessage} • {validation.Message}";
        if (validation.Level == ValidationLevel.Error && !string.IsNullOrWhiteSpace(validation.Message))
            return validation.Message;
        return baseMessage;
    }

    [RelayCommand]
    private async Task IncrementCardAsync(DeckCardDisplayItem item)
    {
        if (Deck == null) return;
        int prev = item.Entity.Quantity;
        var result = await _deckService.UpdateQuantityAsync(
            Deck.Id, item.Entity.CardId, prev + 1, item.Entity.Section);
        if (result.IsSuccess)
            ApplyLocalPatchAfterQuantitySuccess(item, prev + 1, item.Entity.Section);
        else
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
        }
    }

    [RelayCommand]
    private async Task DecrementCardAsync(DeckCardDisplayItem item)
    {
        if (Deck == null) return;
        int newQty = item.Entity.Quantity - 1;
        var result = await _deckService.UpdateQuantityAsync(
            Deck.Id, item.Entity.CardId, newQty, item.Entity.Section);
        if (result.IsSuccess)
            ApplyLocalPatchAfterQuantitySuccess(item, newQty, item.Entity.Section);
        else
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
        }
    }

    [RelayCommand]
    private async Task RemoveCardAsync(DeckCardDisplayItem item)
    {
        if (Deck == null) return;
        string section = item.Entity.Section;
        await _deckService.RemoveCardAsync(Deck.Id, item.Entity.CardId, section);
        ApplyLocalPatchAfterQuantitySuccess(item, 0, section);
    }

    private void ClearDeckListSelection()
    {
        foreach (var x in CommanderCards)
            x.IsSelected = false;
        foreach (var g in MainDeckGroups)
        {
            foreach (var x in g)
                x.IsSelected = false;
        }
        foreach (var x in SideboardCards)
            x.IsSelected = false;
        NotifyDeckSelectionUi();
    }

    private void NotifyDeckSelectionUi()
    {
        OnPropertyChanged(nameof(SelectedCardCount));
        OnPropertyChanged(nameof(HasSelection));
    }

    private IEnumerable<DeckCardDisplayItem> GetSelectedVisibleItems()
    {
        return SelectedSectionIndex switch
        {
            0 => CommanderCards.Where(i => i.IsSelected),
            1 => FilteredMainDeckGroups.SelectMany(g => g).Where(i => i.IsSelected),
            2 => FilteredSideboardCards.Where(i => i.IsSelected),
            _ => []
        };
    }

    private IEnumerable<DeckCardDisplayItem> GetAllItemsInCurrentSection()
    {
        return SelectedSectionIndex switch
        {
            0 => CommanderCards,
            1 => FilteredMainDeckGroups.SelectMany(g => g),
            2 => FilteredSideboardCards,
            _ => []
        };
    }

    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
    }

    private void DeckListItemTapped(DeckCardDisplayItem? item)
    {
        if (item == null) return;
        if (IsSelectionMode)
        {
            item.IsSelected = !item.IsSelected;
            NotifyDeckSelectionUi();
            return;
        }

        ShowCardQuickDetailCommand.Execute(item);
    }

    private void SelectAllInCurrentSection()
    {
        foreach (var x in GetAllItemsInCurrentSection())
            x.IsSelected = true;
        NotifyDeckSelectionUi();
    }

    private void ClearDeckSelection()
    {
        foreach (var x in GetAllItemsInCurrentSection())
            x.IsSelected = false;
        NotifyDeckSelectionUi();
    }

    private async Task BulkRemoveSelectionAsync()
    {
        if (Deck == null) return;
        var items = GetSelectedVisibleItems().ToList();
        if (items.Count == 0) return;

        var mutations = items
            .Select(i => new DeckEditorMutation(DeckEditorMutationKind.Remove, i.Entity.CardId, i.Entity.Section))
            .ToList();
        var result = await _deckService.ApplyEditorMutationsAsync(Deck.Id, mutations);
        if (result.IsError)
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
            return;
        }

        StatusIsError = false;
        StatusMessage = $"Removed {items.Count} stack(s).";
        IsSelectionMode = false;
        await ReloadAsync(preserveState: true);
    }

    private async Task BulkMoveSelectionToMainAsync()
    {
        if (Deck == null) return;
        var items = GetSelectedVisibleItems()
            .Where(i => string.Equals(i.Entity.Section, "Sideboard", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (items.Count == 0) return;

        var mutations = items
            .Select(i => new DeckEditorMutation(DeckEditorMutationKind.Move, i.Entity.CardId, "Sideboard", "Main", 0))
            .ToList();
        await FinishBulkMoveAsync(mutations, "Main");
    }

    private async Task BulkMoveSelectionToSideboardAsync()
    {
        if (Deck == null) return;
        var items = GetSelectedVisibleItems()
            .Where(i => string.Equals(i.Entity.Section, "Main", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (items.Count == 0) return;

        var mutations = items
            .Select(i => new DeckEditorMutation(DeckEditorMutationKind.Move, i.Entity.CardId, "Main", "Sideboard", 0))
            .ToList();
        await FinishBulkMoveAsync(mutations, "Sideboard");
    }

    private async Task FinishBulkMoveAsync(List<DeckEditorMutation> mutations, string targetLabel)
    {
        if (Deck == null || mutations.Count == 0) return;

        var result = await _deckService.ApplyEditorMutationsAsync(Deck.Id, mutations);
        if (result.IsError)
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
            return;
        }

        StatusIsError = false;
        StatusMessage = $"Moved {mutations.Count} stack(s) to {targetLabel}.";
        IsSelectionMode = false;
        await ReloadAsync(preserveState: true);
    }

    private async Task BulkIncrementSelectionAsync()
    {
        if (Deck == null) return;
        var items = GetSelectedVisibleItems().ToList();
        if (items.Count == 0) return;

        var mutations = items
            .Select(i => new DeckEditorMutation(DeckEditorMutationKind.Add, i.Entity.CardId, i.Entity.Section, null, 1))
            .ToList();
        var result = await _deckService.ApplyEditorMutationsAsync(Deck.Id, mutations);
        if (result.IsError)
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
            return;
        }

        StatusIsError = false;
        StatusMessage = $"+1 to {items.Count} stack(s).";
        await ReloadAsync(preserveState: true);
    }

    private async Task BulkDecrementSelectionAsync()
    {
        if (Deck == null) return;
        var items = GetSelectedVisibleItems().ToList();
        if (items.Count == 0) return;

        var mutations = items
            .Select(i => new DeckEditorMutation(
                DeckEditorMutationKind.SetQuantity,
                i.Entity.CardId,
                i.Entity.Section,
                null,
                Math.Max(0, i.Entity.Quantity - 1)))
            .ToList();
        var result = await _deckService.ApplyEditorMutationsAsync(Deck.Id, mutations);
        if (result.IsError)
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
            return;
        }

        StatusIsError = false;
        StatusMessage = $"-1 from {items.Count} stack(s).";
        await ReloadAsync(preserveState: true);
    }

    private async Task MoveCardRowToSideboardAsync(DeckCardDisplayItem? item)
    {
        if (Deck == null || item == null) return;
        if (!string.Equals(item.Entity.Section, "Main", StringComparison.OrdinalIgnoreCase))
            return;

        var result = await _deckService.ApplyEditorMutationsAsync(Deck.Id,
        [
            new DeckEditorMutation(DeckEditorMutationKind.Move, item.Entity.CardId, "Main", "Sideboard", 0)
        ]);
        if (result.IsError)
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
            return;
        }

        StatusIsError = false;
        StatusMessage = $"Moved {item.DisplayName} to Sideboard.";
        await ReloadAsync(preserveState: true);
    }

    private async Task MoveCardRowToMainAsync(DeckCardDisplayItem? item)
    {
        if (Deck == null || item == null) return;
        if (!string.Equals(item.Entity.Section, "Sideboard", StringComparison.OrdinalIgnoreCase))
            return;

        var result = await _deckService.ApplyEditorMutationsAsync(Deck.Id,
        [
            new DeckEditorMutation(DeckEditorMutationKind.Move, item.Entity.CardId, "Sideboard", "Main", 0)
        ]);
        if (result.IsError)
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
            return;
        }

        StatusIsError = false;
        StatusMessage = $"Moved {item.DisplayName} to Main.";
        await ReloadAsync(preserveState: true);
    }

    private void ToggleStagedSearchRow(DeckAddSearchResultRow? row)
    {
        if (row == null || Deck == null) return;

        if (SelectedSectionIndex == 0)
        {
            _ = AddCardFromSearchAsync(row.Card);
            return;
        }

        if (row.IsStaged)
        {
            var existing = StagedAddItems.FirstOrDefault(s => s.Card.Uuid == row.Card.Uuid);
            if (existing != null)
                StagedAddItems.Remove(existing);
            row.IsStaged = false;
        }
        else
        {
            StagedAddItems.Add(new StagedDeckAddItem(row.Card));
            row.IsStaged = true;
        }

        OnPropertyChanged(nameof(StagedAddSummaryText));
    }

    private void IncrementStagedAddQuantity(StagedDeckAddItem? item)
    {
        if (item == null) return;
        item.Quantity++;
        OnPropertyChanged(nameof(StagedAddSummaryText));
    }

    private void DecrementStagedAddQuantity(StagedDeckAddItem? item)
    {
        if (item == null) return;
        if (item.Quantity <= 1)
        {
            StagedAddItems.Remove(item);
            SyncSearchRowStagedState(item.Card.Uuid, false);
        }
        else
        {
            item.Quantity--;
        }

        OnPropertyChanged(nameof(StagedAddSummaryText));
    }

    private void SyncSearchRowStagedState(string cardUuid, bool staged)
    {
        var row = AddCardSearchResultRows.FirstOrDefault(r => r.Card.Uuid == cardUuid);
        if (row != null)
            row.IsStaged = staged;
    }

    private async Task AddStagedCardsToDeckAsync()
    {
        if (Deck == null || StagedAddItems.Count == 0) return;

        if (SelectedSectionIndex == 0)
        {
            var first = StagedAddItems[0].Card;
            var result = await _deckService.SetCommanderAsync(Deck.Id, first.Uuid);
            if (result.IsError)
            {
                StatusIsError = true;
                StatusMessage = result.Message ?? UserMessages.CouldNotSetCommander();
            }
            else
            {
                StatusIsError = false;
                StatusMessage = StagedAddItems.Count > 1
                    ? $"{first.Name} set as commander. (Additional staged cards were not added.)"
                    : $"{first.Name} set as commander.";
                StagedAddItems.Clear();
                AddCardSearchResultRows = [];
                OnPropertyChanged(nameof(StagedAddSummaryText));
                await ReloadAsync(preserveState: true);
            }

            return;
        }

        string section = SelectedSectionIndex == 2 ? "Sideboard" : "Main";
        var mutations = StagedAddItems
            .Select(s => new DeckEditorMutation(DeckEditorMutationKind.Add, s.Card.Uuid, section, null, s.Quantity))
            .ToList();
        var addResult = await _deckService.ApplyEditorMutationsAsync(Deck.Id, mutations);
        if (addResult.IsError)
        {
            StatusIsError = true;
            StatusMessage = addResult.Message ?? UserMessages.CouldNotAddCardToDeck();
            return;
        }

        int total = StagedAddItems.Sum(s => s.Quantity);
        StatusIsError = false;
        StatusMessage = $"Added {total} card(s) to {section}.";
        StagedAddItems.Clear();
        AddCardSearchResultRows = [];
        OnPropertyChanged(nameof(StagedAddSummaryText));
        await ReloadAsync(preserveState: true);
    }

    private void ClearStagedAdds()
    {
        foreach (var row in AddCardSearchResultRows)
            row.IsStaged = false;
        StagedAddItems.Clear();
        OnPropertyChanged(nameof(StagedAddSummaryText));
    }

    private void ApplyLocalPatchAfterQuantitySuccess(DeckCardDisplayItem item, int newQty, string section)
    {
        if (newQty <= 0)
            RemoveDisplayItemFromPresentation(item, section);
        else
        {
            item.SetDeckQuantity(newQty);
            if (section == "Main")
                FindGroupContaining(item)?.RecalculateTotal();
        }

        FinalizePresentationMutation();
    }

    private void RemoveDisplayItemFromPresentation(DeckCardDisplayItem item, string section)
    {
        switch (section)
        {
            case "Commander":
                CommanderCards.Remove(item);
                SyncCommanderHero();
                break;
            case "Sideboard":
                SideboardCards.Remove(item);
                break;
            default:
                var g = FindGroupContaining(item);
                if (g != null)
                {
                    g.Remove(item);
                    g.RecalculateTotal();
                    if (g.Count == 0)
                        MainDeckGroups.Remove(g);
                }
                break;
        }
    }

    private DeckCardGroup? FindGroupContaining(DeckCardDisplayItem item)
    {
        foreach (var g in MainDeckGroups)
        {
            if (g.Contains(item))
                return g;
        }
        return null;
    }

    private void SyncCommanderHero()
    {
        FirstCommander = CommanderCards.Count > 0 ? CommanderCards[0] : null;
        AdditionalCommanderCards = CommanderCards.Count > 1
            ? new ObservableCollection<DeckCardDisplayItem>([.. CommanderCards.Skip(1)])
            : [];
        OnPropertyChanged(nameof(HasNoCommander));
        OnPropertyChanged(nameof(HasMultipleCommanders));
        RefreshDeckListFilter();
    }

    private List<DeckCardEntity> GatherEntitiesFromPresentation()
    {
        var list = new List<DeckCardEntity>();
        foreach (var x in CommanderCards)
            list.Add(x.Entity);
        foreach (var g in MainDeckGroups)
        {
            foreach (var x in g)
                list.Add(x.Entity);
        }
        foreach (var x in SideboardCards)
            list.Add(x.Entity);
        return list;
    }

    private void FinalizePresentationMutation()
    {
        var entities = GatherEntitiesFromPresentation();
        MainDeckCount = MainDeckGroups.Sum(g => g.Sum(i => i.Entity.Quantity));
        SideboardCount = SideboardCards.Sum(i => i.Entity.Quantity);
        TotalCardCount = entities.Sum(e => e.Quantity);
        Stats = ComputeStats(entities, _cardMapCache);
        OnPropertyChanged(nameof(DeckSummaryText));
        OnPropertyChanged(nameof(SideboardHeaderText));
        RefreshDeckListFilter();
        _ = ApplyValidationUiAsync();
    }

    private async Task ApplyValidationUiAsync()
    {
        var v = await _deckService.ValidateDeckAsync(_deckId);
        int total = TotalCardCount;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusIsError = v.Level == ValidationLevel.Error;
            StatusMessage = GetValidationStatusMessage(v, total);
        });
    }

    private async Task SuggestLandsAsync()
    {
        if (Deck == null) return;

        StatusIsError = false;
        StatusMessage = UserMessages.SuggestingLands;

        try
        {
            int added = await _deckService.AutoSuggestLandsAsync(Deck.Id);
            if (added > 0)
            {
                StatusIsError = false;
                StatusMessage = UserMessages.AddedLandsToMain(added);
            }
            else
            {
                StatusIsError = false;
                StatusMessage = UserMessages.NoLandsAdded;
            }

            await ReloadAsync(preserveState: true);
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.LoadFailed(ex.Message);
        }
    }

    /// <summary>Maps color identity string (e.g. "W", "GU") to a dark tint for list strip backgrounds. Matches WUBRG logic used in commander header.</summary>
    public static Color GetStripBackgroundColorFromIdentity(string colorIdentity)
    {
        bool w = colorIdentity.Contains('W');
        bool u = colorIdentity.Contains('U');
        bool b = colorIdentity.Contains('B');
        bool r = colorIdentity.Contains('R');
        bool g = colorIdentity.Contains('G');
        int count = (w ? 1 : 0) + (u ? 1 : 0) + (b ? 1 : 0) + (r ? 1 : 0) + (g ? 1 : 0);

        if (count == 0) return Color.FromArgb("#2A2A33");
        if (count >= 3) return Color.FromArgb("#3B2C0A"); // multicolor / gold dark

        // Single or dual: use first color, darkened for strip
        return (u ? Color.FromArgb("#0D2E4F")
             : g ? Color.FromArgb("#0D351D")
             : r ? Color.FromArgb("#4F0D0D")
             : b ? Color.FromArgb("#1D1528")
             : Color.FromArgb("#3D3528")); // white -> tan
    }

    private static ObservableCollection<DeckCardGroup> BuildGroups(List<DeckCardDisplayItem> items)
    {
        string[] order = ["Creatures", "Instants", "Sorceries", "Artifacts", "Enchantments", "Planeswalkers", "Lands", "Other"];

        var grouped = items
            .GroupBy(i => GetTypeCategory(i.Card?.CardType))
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.DisplayName).ToList());

        var groups = new ObservableCollection<DeckCardGroup>();
        foreach (var key in order)
        {
            if (grouped.TryGetValue(key, out var list) && list.Count > 0)
            {
                int groupCount = list.Sum(i => i.Entity.Quantity);
                groups.Add(new DeckCardGroup(key, list, groupCount));
            }
        }
        return groups;
    }

    private static string GetTypeCategory(string? cardType)
    {
        if (string.IsNullOrEmpty(cardType)) return "Other";
        if (cardType.Contains("Creature")) return "Creatures";
        if (cardType.Contains("Instant")) return "Instants";
        if (cardType.Contains("Sorcery")) return "Sorceries";
        if (cardType.Contains("Artifact")) return "Artifacts";
        if (cardType.Contains("Enchantment")) return "Enchantments";
        if (cardType.Contains("Planeswalker")) return "Planeswalkers";
        if (cardType.Contains("Land")) return "Lands";
        return "Other";
    }

    private static DeckStats ComputeStats(List<DeckCardEntity> entities, Dictionary<string, Card> cardMap)
    {
        var stats = new DeckStats();
        double totalCmc = 0;
        int cmcCount = 0;

        foreach (var entity in entities)
        {
            if (entity.Section == "Commander") continue;

            int qty = entity.Quantity;
            stats.TotalCards += qty;

            if (!cardMap.TryGetValue(entity.CardId, out var card)) continue;

            string type = card.CardType ?? "";
            if (type.Contains("Creature")) stats.Creatures += qty;
            else if (type.Contains("Instant")) stats.Instants += qty;
            else if (type.Contains("Sorcery")) stats.Sorceries += qty;
            else if (type.Contains("Artifact")) stats.Artifacts += qty;
            else if (type.Contains("Enchantment")) stats.Enchantments += qty;
            else if (type.Contains("Planeswalker")) stats.Planeswalkers += qty;
            else if (type.Contains("Land")) stats.Lands += qty;

            if (!type.Contains("Land"))
            {
                double cmc = card.EffectiveManaValue;
                totalCmc += cmc * qty;
                cmcCount += qty;
                int slot = Math.Min((int)cmc, 10);
                stats.ManaCurve[slot] += qty;
            }
        }

        stats.AvgCmc = cmcCount > 0 ? Math.Round(totalCmc / cmcCount, 2) : 0;
        return stats;
    }

    private sealed record LastAddedInfo(string CardId, string Section, int Quantity, string CardName);
}
