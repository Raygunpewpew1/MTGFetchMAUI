using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;

namespace AetherVault.ViewModels;

public partial class DeckDetailViewModel(
    DeckBuilderService deckService,
    ICardRepository cardRepository,
    ICollectionRepository collectionRepository,
    CardManager cardManager,
    IToastService toast,
    IDialogService dialogService) : BaseViewModel
{
    private readonly DeckBuilderService _deckService = deckService;
    private readonly ICardRepository _cardRepository = cardRepository;
    private readonly ICollectionRepository _collectionRepository = collectionRepository;
    private readonly CardManager _cardManager = cardManager;
    private readonly IToastService _toast = toast;
    private readonly IDialogService _dialogService = dialogService;
    private Dictionary<string, Card> _cardMapCache = [];
    private List<DeckCardEntity> _deckEntitiesCache = [];
    private int _deckId;
    private CancellationTokenSource? _deckListFilterCts;
    private const string PrefDeckEditorLayoutMode = "DeckEditorLayoutMode";
    private bool _deckEditorLayoutPrefLoaded;
    private static bool _gridLayoutHintShownThisProcess;
    private IReadOnlyList<string> _validationDetailLines = [];
    private bool _deckDetailStatusBindingsRegistered;

    /// <summary>Subscribes once so <see cref="ShowDeckDetailStatusRow"/> tracks <see cref="BaseViewModel.HasStatusMessage"/> and validation details.</summary>
    private void EnsureDeckDetailStatusBindings()
    {
        if (_deckDetailStatusBindingsRegistered) return;
        _deckDetailStatusBindingsRegistered = true;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(HasStatusMessage) or nameof(ShowValidationDetailsEntry))
                OnPropertyChanged(nameof(ShowDeckDetailStatusRow));
        };
    }

    /// <summary>Raised on the main thread after deck data has been reloaded (so the page can force layout/redraw).</summary>
    public event Action? ReloadCompleted;

    /// <summary>Raised when the user requests the quick-detail popup for a deck list card.</summary>
    public event Action<DeckCardDisplayItem>? RequestShowQuickDetail;

    private const int MaxDeckUndoDepth = 25;
    private readonly List<DeckEditorMutation[]> _undoStack = [];

    /// <summary>True when the deck editor has at least one reversible edit (toolbar Undo).</summary>
    public bool CanUndoDeckEdit => _undoStack.Count > 0;

    [ObservableProperty]
    public partial DeckEntity? Deck { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<DeckCardGroup> MainDeckGroups { get; set; } = [];

    /// <summary>Main-deck groups after applying the in-deck filter (same item refs as Main when filter is empty).</summary>
    [ObservableProperty]
    public partial ObservableCollection<DeckCardGroup> FilteredMainDeckGroups { get; set; } = [];

    /// <summary>Flattened filtered main-deck rows for grid layout (same item instances as in groups).</summary>
    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> MainDeckGridItems { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> SideboardCards { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DeckCardGroup> SideboardGroups { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DeckCardGroup> FilteredSideboardGroups { get; set; } = [];

    /// <summary>Flattened filtered sideboard rows for grid layout.</summary>
    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> SideboardGridItems { get; set; } = [];

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
    public partial DeckEditorLayoutMode DeckEditorLayoutMode { get; set; } = DeckEditorLayoutMode.Standard;

    public bool IsDeckEditorLayoutStandard => DeckEditorLayoutMode == DeckEditorLayoutMode.Standard;
    public bool IsDeckEditorLayoutCompact => DeckEditorLayoutMode == DeckEditorLayoutMode.Compact;
    public bool IsDeckEditorLayoutGrid => DeckEditorLayoutMode == DeckEditorLayoutMode.Grid;

    partial void OnDeckEditorLayoutModeChanged(DeckEditorLayoutMode value)
    {
        OnPropertyChanged(nameof(IsDeckEditorLayoutStandard));
        OnPropertyChanged(nameof(IsDeckEditorLayoutCompact));
        OnPropertyChanged(nameof(IsDeckEditorLayoutGrid));
        if (value == DeckEditorLayoutMode.Grid && !_gridLayoutHintShownThisProcess)
        {
            _gridLayoutHintShownThisProcess = true;
            _toast.Show(UserMessages.DeckGridLayoutHint, 5000);
        }
        try
        {
            Preferences.Default.Set(PrefDeckEditorLayoutMode, value.ToString());
        }
        catch
        {
            // ignore storage failures
        }
    }

    /// <summary>Show validation bullet list (page shows an alert).</summary>
    public bool ShowValidationDetailsEntry => _validationDetailLines.Count > 0;

    /// <summary>Bottom status strip: errors, warnings, transient edits, or validation details — hidden when only counts apply (see summary bar).</summary>
    public bool ShowDeckDetailStatusRow => HasStatusMessage || ShowValidationDetailsEntry;

    /// <summary>Raised with joined validation lines when user taps Details.</summary>
    public event Func<string, Task>? ValidationDetailsAlertRequested;

    [RelayCommand(CanExecute = nameof(CanShowValidationDetails))]
    private async Task ShowValidationDetails()
    {
        if (_validationDetailLines.Count == 0 || ValidationDetailsAlertRequested == null)
            return;
        string body = string.Join($"{Environment.NewLine}{Environment.NewLine}", _validationDetailLines);
        await ValidationDetailsAlertRequested.Invoke(body);
    }

    private bool CanShowValidationDetails() => _validationDetailLines.Count > 0;

    private void SetValidationDetailLines(ValidationResult validation)
    {
        _validationDetailLines = validation.DetailLines.Count > 0
            ? validation.DetailLines
            : (!string.IsNullOrWhiteSpace(validation.Message) ? [validation.Message] : []);
        OnPropertyChanged(nameof(ShowValidationDetailsEntry));
        OnPropertyChanged(nameof(ShowDeckDetailStatusRow));
        ShowValidationDetailsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Raised when FAB add-cards flow should open (e.g. commander empty state).</summary>
    public event Action? AddCardsModalRequested;

    [RelayCommand]
    private void RequestAddCardsModal() => AddCardsModalRequested?.Invoke();

    private async Task ShowDeckDataTruthHelpAsync()
    {
        await _dialogService.DisplayAlertAsync(UserMessages.DeckDataTruthHelpTitle, DataTruthLabels.HelpBody, "OK");
    }

    /// <summary>Called from <see cref="Pages.DeckDetailPage"/> layout action sheet (and tests).</summary>
    public void SetDeckEditorLayout(DeckEditorLayoutMode mode) => DeckEditorLayoutMode = mode;

    [RelayCommand]
    private void SelectDeckLayoutStandard() => SetDeckEditorLayout(DeckEditorLayoutMode.Standard);

    [RelayCommand]
    private void SelectDeckLayoutCompact() => SetDeckEditorLayout(DeckEditorLayoutMode.Compact);

    [RelayCommand]
    private void SelectDeckLayoutGrid() => SetDeckEditorLayout(DeckEditorLayoutMode.Grid);

    [ObservableProperty]
    public partial DeckStats Stats { get; set; } = new();

    partial void OnStatsChanged(DeckStats value) => OnPropertyChanged(nameof(ShowDeckManaPipStrip));

    /// <summary>Local MTG catalog + price bundle snapshot (Stats tab).</summary>
    [ObservableProperty]
    public partial string DeckDataTruthCatalogLine { get; set; } = "";

    [ObservableProperty]
    public partial string DeckDataTruthPricesLine { get; set; } = "";

    /// <summary>True when the deck has non-land mana symbols to chart.</summary>
    public bool ShowDeckManaPipStrip => Stats.ManaPipCounts.Sum() > 0;

    [ObservableProperty]
    public partial ObservableCollection<string> SynergySubtypeSummaryLines { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<string> SynergyKeywordSummaryLines { get; set; } = [];

    /// <summary>Deck editor toolbar: horizontal chips for curated lists (opens add-cards with list pre-selected).</summary>
    public ObservableCollection<DeckBrowseListChipItem> QuickBrowseListChips { get; } =
        new(DeckBrowseListCatalog.CreateChipItems());

    [ObservableProperty]
    public partial bool ShowQuickBrowseLists { get; set; }

    [RelayCommand]
    private void ToggleQuickBrowseLists() => ShowQuickBrowseLists = !ShowQuickBrowseLists;

    /// <summary>Deck stats tab: show subtype theme block.</summary>
    public bool HasSynergySubtypeSummary => SynergySubtypeSummaryLines.Count > 0;

    /// <summary>Deck stats tab: show keyword theme block.</summary>
    public bool HasSynergyKeywordSummary => SynergyKeywordSummaryLines.Count > 0;

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

    /// <summary>Main and Sideboard share list/grid layout controls (not Commander or Stats).</summary>
    public bool IsMainOrSideboardTab => IsMainTab || IsSideboardTab;

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
    public partial string DeckPriceSummaryDisplay { get; set; } = "";

    /// <summary>Bundled price DB + per-row display (Settings → price data).</summary>
    public bool ShowDeckPrices => PricePreferences.PricesDataEnabled;

    /// <summary>Per-row prices hidden while find-in-deck filter is active (less clutter).</summary>
    public bool HasActiveDeckListFilter => !string.IsNullOrWhiteSpace(DeckListFilterText);

    public bool ShowDeckRowPrices => ShowDeckPrices && !HasActiveDeckListFilter;

    [ObservableProperty]
    public partial string DeckFormat { get; set; } = "";

    public bool HasNoCommander => CommanderCards.Count == 0;
    /// <summary>True when there are two or more commander cards (e.g. partner).</summary>
    public bool HasMultipleCommanders => CommanderCards.Count > 1;

    /// <summary>Invoked when deck cohesion profile updates; <see cref="DeckAddCardsViewModel"/> subscribes while the add modal is open.</summary>
    public Action<DeckCohesionProfile>? AddCardsCohesionProfileHook { get; set; }

    private DeckBrowseListChipItem? _pendingAddModalQuickList;

    /// <summary>Consumed once when the add-cards modal opens after <see cref="OpenAddCardsFromQuickList"/>.</summary>
    internal DeckBrowseListChipItem? ConsumePendingAddModalQuickList()
    {
        var x = _pendingAddModalQuickList;
        _pendingAddModalQuickList = null;
        return x;
    }

    /// <summary>Raised for main/sideboard grid ⋯ menu (page shows action sheet).</summary>
    public event Action<DeckCardDisplayItem, bool>? DeckGridOverflowRequested;

    [ObservableProperty]
    public partial bool IsSelectionMode { get; set; }

    /// <summary>Selected rows in the current section (Commander / Main / Sideboard).</summary>
    public int SelectedCardCount => GetSelectedVisibleItems().Count();

    public bool HasSelection => SelectedCardCount > 0;

    /// <summary>Bulk toolbar label (e.g. "3 selected").</summary>
    public string BulkSelectionCountText => $"{SelectedCardCount} selected";

    private IAsyncRelayCommand? _undoDeckEditCommand;
    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand UndoDeckEditCommand => _undoDeckEditCommand ??= new AsyncRelayCommand(UndoDeckEditAsync, () => _undoStack.Count > 0);

    private async Task UndoDeckEditAsync()
    {
        if (Deck == null || _undoStack.Count == 0) return;

        DeckEditorMutation[] frame = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        NotifyUndoAvailability();

        try
        {
            var result = await _deckService.ApplyEditorMutationsAsync(Deck.Id, frame);
            if (result.IsError)
            {
                _undoStack.Add(frame);
                NotifyUndoAvailability();
                StatusIsError = true;
                StatusMessage = result.Message ?? UserMessages.CouldNotUndoDeckEdit();
                _toast.Show(UserMessages.CouldNotUndoDeckEdit(result.Message));
                return;
            }

            await ReloadAsync(preserveState: true);
            StatusIsError = false;
            StatusMessage = UserMessages.DeckEditUndone;
        }
        catch (Exception ex)
        {
            _undoStack.Add(frame);
            NotifyUndoAvailability();
            _toast.Show(UserMessages.CouldNotUndoDeckEdit(ex.Message));
        }
    }

    private void NotifyUndoAvailability()
    {
        OnPropertyChanged(nameof(CanUndoDeckEdit));
        _undoDeckEditCommand?.NotifyCanExecuteChanged();
    }

    private void PushUndoFrame(DeckEditorMutation[] inverseMutations, string? toastSummary = null)
    {
        if (inverseMutations.Length == 0) return;
        _undoStack.Add(inverseMutations);
        while (_undoStack.Count > MaxDeckUndoDepth)
            _undoStack.RemoveAt(0);
        NotifyUndoAvailability();
        if (!string.IsNullOrWhiteSpace(toastSummary))
            _toast.ShowWithAction(toastSummary, "Undo", () => _ = UndoDeckEditAsync(), durationMs: 5000);
    }

    /// <summary>Surfaces undo stack for <see cref="DeckAddCardsViewModel"/> batch mutations.</summary>
    public void PushDeckEditorUndoFrame(DeckEditorMutation[] inverseMutations, string? toastSummary = null) =>
        PushUndoFrame(inverseMutations, toastSummary);

    private IAsyncRelayCommand? _suggestLandsCommand;
    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand SuggestLandsCommand => _suggestLandsCommand ??= new AsyncRelayCommand(SuggestLandsAsync);

    private IAsyncRelayCommand? _showDeckDataTruthHelpCommand;
    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand ShowDeckDataTruthHelpCommand => _showDeckDataTruthHelpCommand ??= new AsyncRelayCommand(ShowDeckDataTruthHelpAsync);

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

    [RelayCommand]
    private void SelectCommander() => SelectedSectionIndex = 0;

    [RelayCommand]
    private void SelectMain() => SelectedSectionIndex = 1;

    [RelayCommand]
    private void SelectSideboard() => SelectedSectionIndex = 2;

    [RelayCommand]
    private void SelectStats() => SelectedSectionIndex = 3;

    [RelayCommand]
    private void RequestMainDeckGridMenu(DeckCardDisplayItem? item)
    {
        if (item != null)
            DeckGridOverflowRequested?.Invoke(item, true);
    }

    [RelayCommand]
    private void RequestSideboardDeckGridMenu(DeckCardDisplayItem? item)
    {
        if (item != null)
            DeckGridOverflowRequested?.Invoke(item, false);
    }

    [RelayCommand]
    private void ShowCardQuickDetail(DeckCardDisplayItem item) => RequestShowQuickDetail?.Invoke(item);

    partial void OnSelectedSectionIndexChanged(int value)
    {
        IsSelectionMode = false;
        ClearDeckListSelection();
        OnPropertyChanged(nameof(IsMainOrSideboardTab));
    }

    partial void OnIsSelectionModeChanged(bool value)
    {
        if (!value)
            ClearDeckListSelection();
        OnPropertyChanged(nameof(BulkSelectionCountText));
    }

    partial void OnDeckListFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveDeckListFilter));
        OnPropertyChanged(nameof(ShowDeckRowPrices));
        _deckListFilterCts?.Cancel();
        _deckListFilterCts = new CancellationTokenSource();
        var token = _deckListFilterCts.Token;
        Task.Delay(350, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                MainThread.BeginInvokeOnMainThread(RefreshDeckListFilter);
        }, TaskContinuationOptions.None);
    }

    /// <summary>Snapshot for commander suggestions and cohesion (shared with <see cref="DeckAddCardsViewModel"/>).</summary>
    public (DeckStats stats, DeckCohesionProfile profile) BuildCohesionSnapshot()
    {
        var stats = ComputeStatsAndCohesion(_deckEntitiesCache, _cardMapCache, out var profile);
        return (stats, profile);
    }

    /// <summary>From deck editor: open add-cards; optional quick list is applied via <see cref="ConsumePendingAddModalQuickList"/>.</summary>
    [RelayCommand]
    private void OpenAddCardsFromQuickList(DeckBrowseListChipItem? item)
    {
        if (item == null) return;
        _pendingAddModalQuickList = item;
        AddCardsModalRequested?.Invoke();
    }

    private static void ApplyOwnedQuantities(List<DeckCardDisplayItem> items, Dictionary<string, int> qtyOwned)
    {
        foreach (var item in items)
            item.OwnedQuantity = qtyOwned.GetValueOrDefault(item.Entity.CardId, 0);
    }

    public async Task ReloadAsync(bool preserveState = false) => await LoadAsync(_deckId, preserveState);

    /// <summary>Sets <see cref="DeckEntity.CoverCardId"/> for the Decks hub tile; clears nothing else.</summary>
    public async Task<ValidationResult> SetDeckHubCoverFromCardAsync(Card card)
    {
        if (Deck == null) return ValidationResult.Error("Deck not found.");
        var result = await _deckService.SetDeckCoverAsync(Deck.Id, card.Uuid);
        if (result.IsSuccess) await RefreshDeckCoverFromDbAsync();
        return result;
    }

    /// <summary>Clears custom hub art; commander (if any) is shown again on the Decks tab.</summary>
    public async Task<ValidationResult> ClearDeckHubCoverAsync()
    {
        if (Deck == null) return ValidationResult.Error("Deck not found.");
        var result = await _deckService.SetDeckCoverAsync(Deck.Id, null);
        if (result.IsSuccess) await RefreshDeckCoverFromDbAsync();
        return result;
    }

    private async Task RefreshDeckCoverFromDbAsync()
    {
        if (Deck == null) return;
        var d = await _deckService.GetDeckAsync(Deck.Id);
        if (d == null) return;
        Deck.CoverCardId = d.CoverCardId;
        Deck.DateModified = d.DateModified;
        OnPropertyChanged(nameof(Deck));
    }

    public async Task LoadAsync(int deckId, bool preserveState = false)
    {
        EnsureDeckDetailStatusBindings();
        _deckId = deckId;
        if (!_deckEditorLayoutPrefLoaded)
        {
            _deckEditorLayoutPrefLoaded = true;
            try
            {
                string s = Preferences.Default.Get(PrefDeckEditorLayoutMode, DeckEditorLayoutMode.Standard.ToString());
                if (Enum.TryParse(s, out DeckEditorLayoutMode layoutMode))
                    DeckEditorLayoutMode = layoutMode;
            }
            catch
            {
                // ignore
            }
        }

        if (!preserveState)
        {
            _undoStack.Clear();
            NotifyUndoAvailability();
        }

        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;

        if (!preserveState)
            StatusMessage = UserMessages.LoadingDeck;

        try
        {
            // Lets the UI thread pump once before DB + large collection updates — reduces “frozen”
            // deck editor when the Android debugger / Hot Reload slows the main thread.
            await Task.Yield();
            var newDeck = await _deckService.GetDeckAsync(deckId);
            if (newDeck == null)
            {
                _deckEntitiesCache = [];
                StatusIsError = true;
                StatusMessage = UserMessages.DeckNotFound;
                DeckPriceSummaryDisplay = "";
                return;
            }

            if (!preserveState)
            {
                Deck = newDeck;
            }

            DeckFormat = EnumExtensions.ParseDeckFormat(newDeck.Format).ToDisplayName();

            var cardEntities = await _deckService.GetDeckCardsAsync(deckId);
            _deckEntitiesCache = cardEntities;
            var uuids = cardEntities.Select(c => c.CardId).Distinct().ToArray();

            Dictionary<string, Card> cardMap;
            Dictionary<string, int> qtyOwned;
            if (uuids.Length == 0)
            {
                cardMap = [];
                qtyOwned = [];
            }
            else
            {
                var cardsTask = _cardRepository.GetCardsAsync(uuids);
                var qtyTask = _collectionRepository.GetQuantitiesAsync(uuids);
                await Task.WhenAll(cardsTask, qtyTask);
                cardMap = await cardsTask;
                qtyOwned = await qtyTask;
            }

            var (commander, main, sideboard) = MapEntitiesToSectionLists(cardEntities, cardMap);
            ApplyOwnedQuantities(commander, qtyOwned);
            ApplyOwnedQuantities(main, qtyOwned);
            ApplyOwnedQuantities(sideboard, qtyOwned);

            var mainDeckGroups = BuildGroups(main);
            int mainDeckCount = main.Sum(i => i.Entity.Quantity);
            int sideboardCount = sideboard.Sum(i => i.Entity.Quantity);
            var totalCardCount = cardEntities.Sum(c => c.Quantity);
            var stats = ComputeStatsAndCohesion(cardEntities, cardMap, out var cohesionProfile);
            var validation = await _deckService.ValidateDeckAsync(newDeck, cardEntities, cardMap);
            var statusMessage = GetValidationStatusMessage(validation, totalCardCount);

            _cardMapCache = cardMap;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ApplyDeckPricesToSectionLists(commander, main, sideboard, []);
                CommanderCards = new ObservableCollection<DeckCardDisplayItem>(commander);
                FirstCommander = commander.Count > 0 ? commander[0] : null;
                AdditionalCommanderCards = commander.Count > 1
                    ? new ObservableCollection<DeckCardDisplayItem>(commander.Skip(1))
                    : [];
                SideboardCards = new ObservableCollection<DeckCardDisplayItem>(sideboard);
                SideboardGroups = BuildGroups(sideboard);
                MainDeckGroups = mainDeckGroups;
                MainDeckCount = mainDeckCount;
                SideboardCount = sideboardCount;
                TotalCardCount = totalCardCount;
                Stats = stats;
                RefreshDeckDataTruthLabels();
                UpdateSynergyCollections(cohesionProfile);
                OnPropertyChanged(nameof(HasNoCommander));
                OnPropertyChanged(nameof(HasMultipleCommanders));
                RefreshDeckListFilter();
                RecalculateDeckPriceTotals();
                StatusIsError = validation.Level == ValidationLevel.Error;
                StatusMessage = statusMessage;
                SetValidationDetailLines(validation);
                ReloadCompleted?.Invoke();
            });

            if (PricePreferences.PricesDataEnabled && uuids.Length > 0)
            {
                try
                {
                    var priceMap = await _cardManager.GetCardPricesBulkAsync(uuids);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        ApplyDeckPricesToSectionLists(commander, main, sideboard, priceMap);
                        RecalculateDeckPriceTotals();
                    });
                }
                catch
                {
                    // Keep empty; rows show no unit price until retry.
                }
            }
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

    /// <summary>Snapshot for <see cref="DeckSynergyNavigationContext"/> when opening card detail from this deck.</summary>
    public IReadOnlyList<DeckCardEntity> GetDeckEntitiesSnapshotForSynergy() => _deckEntitiesCache;

    /// <summary>Card map snapshot paired with <see cref="GetDeckEntitiesSnapshotForSynergy"/>.</summary>
    public IReadOnlyDictionary<string, Card> GetDeckCardMapSnapshotForSynergy() => _cardMapCache;

    /// <summary>Returns the ordered list of card UUIDs for the current tab (Commander, Main, or Sideboard) for swipe context.</summary>
    public IReadOnlyList<string> GetOrderedUuidsForCurrentSection()
    {
        return SelectedSectionIndex switch
        {
            0 => [.. CommanderCards.Select(x => x.CardUuid)],
            1 => DeckEditorLayoutMode == DeckEditorLayoutMode.Grid
                ? [.. MainDeckGridItems.Select(x => x.CardUuid)]
                : [.. FilteredMainDeckGroups.SelectMany(g => g).Select(x => x.CardUuid)],
            2 => DeckEditorLayoutMode == DeckEditorLayoutMode.Grid
                ? [.. SideboardGridItems.Select(x => x.CardUuid)]
                : [.. FilteredSideboardGroups.SelectMany(g => g).Select(x => x.CardUuid)],
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
            FilteredSideboardGroups = SideboardGroups;
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

            var filteredSideboard = new ObservableCollection<DeckCardGroup>();
            foreach (var g in SideboardGroups)
            {
                var items = g.Where(i => MatchesDeckListFilter(i, q)).ToList();
                if (items.Count == 0) continue;
                int groupQty = items.Sum(x => x.Entity.Quantity);
                filteredSideboard.Add(new DeckCardGroup(g.GroupName, items, groupQty));
            }

            FilteredSideboardGroups = filteredSideboard;
            FilteredSideboardCards = new ObservableCollection<DeckCardDisplayItem>(
                SideboardCards.Where(i => MatchesDeckListFilter(i, q)));
            FilteredAdditionalCommanderCards = new ObservableCollection<DeckCardDisplayItem>(
                AdditionalCommanderCards.Where(i => MatchesDeckListFilter(i, q)));

            IsCommanderHeroVisible = FirstCommander != null && MatchesDeckListFilter(FirstCommander, q);
            ShowCommanderHiddenByFilterHint = FirstCommander != null && !IsCommanderHeroVisible;
        }

        OnPropertyChanged(nameof(ShowCommanderHeroArt));
        OnPropertyChanged(nameof(ShowFilteredPartnersSection));
        OnPropertyChanged(nameof(ShowDeckRowPrices));
        RebuildMainDeckGridItems();
        RebuildSideboardGridItems();
    }

    private void RebuildSideboardGridItems()
    {
        var flat = new List<DeckCardDisplayItem>();
        foreach (var g in FilteredSideboardGroups)
        {
            foreach (var item in g)
                flat.Add(item);
        }

        SideboardGridItems = new ObservableCollection<DeckCardDisplayItem>(flat);
    }

    private void RebuildMainDeckGridItems()
    {
        var flat = new List<DeckCardDisplayItem>();
        foreach (var g in FilteredMainDeckGroups)
        {
            foreach (var item in g)
                flat.Add(item);
        }

        MainDeckGridItems = new ObservableCollection<DeckCardDisplayItem>(flat);
    }

    private static void ApplyDeckPricesToSectionLists(
        List<DeckCardDisplayItem> commander,
        List<DeckCardDisplayItem> main,
        List<DeckCardDisplayItem> sideboard,
        Dictionary<string, CardPriceData> priceMap)
    {
        static void Apply(List<DeckCardDisplayItem> list, Dictionary<string, CardPriceData> map)
        {
            foreach (var item in list)
            {
                if (map.TryGetValue(item.CardUuid, out var p))
                    item.PriceData = p;
            }
        }

        Apply(commander, priceMap);
        Apply(main, priceMap);
        Apply(sideboard, priceMap);
    }

    /// <summary>Commander + main + sideboard rows (authoritative lists, not filtered).</summary>
    private IEnumerable<DeckCardDisplayItem> EnumerateAllDeckRowsForPricing()
    {
        foreach (var c in CommanderCards)
            yield return c;
        foreach (var g in MainDeckGroups)
        {
            foreach (var i in g)
                yield return i;
        }

        foreach (var s in SideboardCards)
            yield return s;
    }

    private void RecalculateDeckPriceTotals()
    {
        if (!PricePreferences.PricesDataEnabled)
        {
            DeckPriceSummaryDisplay = "";
            return;
        }

        double usd = 0;
        double eur = 0;
        foreach (var item in EnumerateAllDeckRowsForPricing())
        {
            if (item.PriceData == null) continue;
            if (!PriceDisplayHelper.TryGetPreferredUnitPrice(item.PriceData, false, false, out var unit, out var cur)) continue;
            int q = item.Entity.Quantity;
            if (cur == PriceCurrency.Eur) eur += unit * q;
            else usd += unit * q;
        }

        DeckPriceSummaryDisplay = FormatDeckDualCurrencyTotal(usd, eur);
    }

    private static string FormatDeckDualCurrencyTotal(double usd, double eur)
    {
        if (usd <= 0 && eur <= 0) return "Deck total: —";
        if (eur <= 0) return $"Deck total: ${usd:F2}";
        if (usd <= 0) return $"Deck total: €{eur:F2}";
        return $"Deck total: ${usd:F2} · €{eur:F2}";
    }

    public void OnPriceDisplayPreferencesChanged()
    {
        OnPropertyChanged(nameof(ShowDeckPrices));
        OnPropertyChanged(nameof(ShowDeckRowPrices));
        foreach (var item in EnumerateAllDeckRowsForPricing())
            item.NotifyDeckPriceBindingsChanged();

        if (!PricePreferences.PricesDataEnabled)
        {
            foreach (var item in EnumerateAllDeckRowsForPricing())
                item.PriceData = null;
            DeckPriceSummaryDisplay = "";
            return;
        }

        _ = ReloadDeckPricesAsync();
    }

    private async Task ReloadDeckPricesAsync()
    {
        var uuids = EnumerateAllDeckRowsForPricing().Select(i => i.CardUuid).Distinct().ToArray();
        if (uuids.Length == 0)
        {
            MainThread.BeginInvokeOnMainThread(RecalculateDeckPriceTotals);
            return;
        }

        try
        {
            var map = await _cardManager.GetCardPricesBulkAsync(uuids);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var item in EnumerateAllDeckRowsForPricing())
                {
                    if (map.TryGetValue(item.CardUuid, out var p))
                        item.PriceData = p;
                }

                RecalculateDeckPriceTotals();
            });
        }
        catch
        {
            MainThread.BeginInvokeOnMainThread(RecalculateDeckPriceTotals);
        }
    }

    private async Task HydrateMissingDeckPricesAsync()
    {
        if (!PricePreferences.PricesDataEnabled) return;
        var need = EnumerateAllDeckRowsForPricing()
            .Where(i => i.PriceData == null)
            .Select(i => i.CardUuid)
            .Distinct()
            .ToArray();
        if (need.Length == 0) return;

        try
        {
            var map = await _cardManager.GetCardPricesBulkAsync(need);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var item in EnumerateAllDeckRowsForPricing())
                {
                    if (item.PriceData == null && map.TryGetValue(item.CardUuid, out var p))
                        item.PriceData = p;
                }

                RecalculateDeckPriceTotals();
            });
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Plaintext lines for TCGPlayer Mass Entry (name, set code, collector number).</summary>
    public string BuildDeckBuyListForMassEntry()
    {
        var sb = new StringBuilder();
        foreach (var item in EnumerateAllDeckRowsForPricing()
                     .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (item.Card == null) continue;
            sb.Append(item.Entity.Quantity).Append(' ').Append(item.DisplayName);
            string set = (item.Card.SetCode ?? "").ToUpperInvariant();
            if (!string.IsNullOrEmpty(set))
                sb.Append(" (").Append(set).Append(')');
            string num = item.Card.Number ?? "";
            if (!string.IsNullOrEmpty(num))
                sb.Append(' ').Append(num);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
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
        if (validation.Level == ValidationLevel.Error)
            return string.IsNullOrWhiteSpace(validation.Message)
                ? UserMessages.DeckValidationUnknownError
                : validation.Message;

        if (validation.Level == ValidationLevel.Warning)
        {
            var baseMessage = $"{totalCardCount} cards";
            return string.IsNullOrWhiteSpace(validation.Message)
                ? baseMessage
                : $"{baseMessage} • {validation.Message}";
        }

        // Success: counts already appear in DeckSummaryText; keep the footer quiet.
        return "";
    }

    [RelayCommand]
    private async Task IncrementCardAsync(DeckCardDisplayItem item)
    {
        if (Deck == null) return;
        int prev = item.Entity.Quantity;
        var result = await _deckService.UpdateQuantityAsync(
            Deck.Id, item.Entity.CardId, prev + 1, item.Entity.Section);
        if (result.IsSuccess)
        {
            PushUndoFrame(
            [
                new DeckEditorMutation(DeckEditorMutationKind.SetQuantity, item.Entity.CardId, item.Entity.Section, null, prev)
            ]);
            ApplyLocalPatchAfterQuantitySuccess(item, prev + 1, item.Entity.Section);
        }
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
        int prev = item.Entity.Quantity;
        int newQty = prev - 1;
        var result = await _deckService.UpdateQuantityAsync(
            Deck.Id, item.Entity.CardId, newQty, item.Entity.Section);
        if (result.IsSuccess)
        {
            PushUndoFrame(
            [
                new DeckEditorMutation(DeckEditorMutationKind.SetQuantity, item.Entity.CardId, item.Entity.Section, null, prev)
            ], newQty <= 0 ? $"{item.DisplayName} removed from deck." : null);
            ApplyLocalPatchAfterQuantitySuccess(item, newQty, item.Entity.Section);
        }
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
        int q = item.Entity.Quantity;
        string name = item.DisplayName;
        await _deckService.RemoveCardAsync(Deck.Id, item.Entity.CardId, section);
        PushUndoFrame(
        [
            new DeckEditorMutation(DeckEditorMutationKind.Add, item.Entity.CardId, section, null, q)
        ], $"{name} removed.");
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
        OnPropertyChanged(nameof(BulkSelectionCountText));
    }

    private IEnumerable<DeckCardDisplayItem> GetSelectedVisibleItems()
    {
        return SelectedSectionIndex switch
        {
            0 => CommanderCards.Where(i => i.IsSelected),
            1 => FilteredMainDeckGroups.SelectMany(g => g).Where(i => i.IsSelected),
            2 => FilteredSideboardGroups.SelectMany(g => g).Where(i => i.IsSelected),
            _ => []
        };
    }

    private IEnumerable<DeckCardDisplayItem> GetAllItemsInCurrentSection()
    {
        return SelectedSectionIndex switch
        {
            0 => CommanderCards,
            1 => FilteredMainDeckGroups.SelectMany(g => g),
            2 => FilteredSideboardGroups.SelectMany(g => g),
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
        var snapshots = items.Select(i => (i.Entity.CardId, i.Entity.Section, i.Entity.Quantity)).ToList();
        var result = await _deckService.ApplyEditorMutationsAsync(Deck.Id, mutations);
        if (result.IsError)
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
            return;
        }

        PushUndoFrame(
            [.. snapshots.Select(s => new DeckEditorMutation(DeckEditorMutationKind.Add, s.CardId, s.Section, null, s.Quantity))],
            items.Count == 1 ? $"{items[0].DisplayName} removed." : $"Removed {items.Count} stack(s).");

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

        var snapshots = items.Select(i => (i.Entity.CardId, i.Entity.Quantity)).ToList();
        var mutations = items
            .Select(i => new DeckEditorMutation(DeckEditorMutationKind.Move, i.Entity.CardId, "Sideboard", "Main", 0))
            .ToList();
        await FinishBulkMoveAsync(mutations, "Main", snapshots);
    }

    private async Task BulkMoveSelectionToSideboardAsync()
    {
        if (Deck == null) return;
        var items = GetSelectedVisibleItems()
            .Where(i => string.Equals(i.Entity.Section, "Main", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (items.Count == 0) return;

        var snapshots = items.Select(i => (i.Entity.CardId, i.Entity.Quantity)).ToList();
        var mutations = items
            .Select(i => new DeckEditorMutation(DeckEditorMutationKind.Move, i.Entity.CardId, "Main", "Sideboard", 0))
            .ToList();
        await FinishBulkMoveAsync(mutations, "Sideboard", snapshots);
    }

    private async Task FinishBulkMoveAsync(
        List<DeckEditorMutation> mutations,
        string targetLabel,
        List<(string CardId, int Quantity)> moveSnapshots)
    {
        if (Deck == null || mutations.Count == 0) return;

        var result = await _deckService.ApplyEditorMutationsAsync(Deck.Id, mutations);
        if (result.IsError)
        {
            StatusIsError = true;
            StatusMessage = result.Message ?? UserMessages.CouldNotUpdateQuantity();
            return;
        }

        DeckEditorMutation[] inverse = string.Equals(targetLabel, "Main", StringComparison.OrdinalIgnoreCase)
            ? [.. moveSnapshots.Select(s => new DeckEditorMutation(DeckEditorMutationKind.Move, s.CardId, "Main", "Sideboard", s.Quantity))]
            : [.. moveSnapshots.Select(s => new DeckEditorMutation(DeckEditorMutationKind.Move, s.CardId, "Sideboard", "Main", s.Quantity))];
        PushUndoFrame(inverse);

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

        var snapshots = items.Select(i => (i.Entity.CardId, i.Entity.Section, i.Entity.Quantity)).ToList();
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

        PushUndoFrame(
            [.. snapshots.Select(s => new DeckEditorMutation(DeckEditorMutationKind.SetQuantity, s.CardId, s.Section, null, s.Quantity))]);

        StatusIsError = false;
        StatusMessage = $"+1 to {items.Count} stack(s).";
        await ReloadAsync(preserveState: true);
    }

    private async Task BulkDecrementSelectionAsync()
    {
        if (Deck == null) return;
        var items = GetSelectedVisibleItems().ToList();
        if (items.Count == 0) return;

        var snapshots = items.Select(i => (i.Entity.CardId, i.Entity.Section, i.Entity.Quantity)).ToList();
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

        PushUndoFrame(
            [.. snapshots.Select(s => new DeckEditorMutation(DeckEditorMutationKind.SetQuantity, s.CardId, s.Section, null, s.Quantity))]);

        StatusIsError = false;
        StatusMessage = $"-1 from {items.Count} stack(s).";
        await ReloadAsync(preserveState: true);
    }

    private async Task MoveCardRowToSideboardAsync(DeckCardDisplayItem? item)
    {
        if (Deck == null || item == null) return;
        if (!string.Equals(item.Entity.Section, "Main", StringComparison.OrdinalIgnoreCase))
            return;

        int q = item.Entity.Quantity;
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

        PushUndoFrame(
        [
            new DeckEditorMutation(DeckEditorMutationKind.Move, item.Entity.CardId, "Sideboard", "Main", q)
        ]);

        StatusIsError = false;
        StatusMessage = $"Moved {item.DisplayName} to Sideboard.";
        await ReloadAsync(preserveState: true);
    }

    private async Task MoveCardRowToMainAsync(DeckCardDisplayItem? item)
    {
        if (Deck == null || item == null) return;
        if (!string.Equals(item.Entity.Section, "Sideboard", StringComparison.OrdinalIgnoreCase))
            return;

        int q = item.Entity.Quantity;
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

        PushUndoFrame(
        [
            new DeckEditorMutation(DeckEditorMutationKind.Move, item.Entity.CardId, "Main", "Sideboard", q)
        ]);

        StatusIsError = false;
        StatusMessage = $"Moved {item.DisplayName} to Main.";
        await ReloadAsync(preserveState: true);
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
            else if (string.Equals(section, "Sideboard", StringComparison.OrdinalIgnoreCase))
                FindSideboardGroupContaining(item)?.RecalculateTotal();
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
                var sideboardGroup = FindSideboardGroupContaining(item);
                if (sideboardGroup != null)
                {
                    sideboardGroup.Remove(item);
                    sideboardGroup.RecalculateTotal();
                    if (sideboardGroup.Count == 0)
                        SideboardGroups.Remove(sideboardGroup);
                }
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

    private DeckCardGroup? FindSideboardGroupContaining(DeckCardDisplayItem item)
    {
        foreach (var g in SideboardGroups)
        {
            if (g.Contains(item))
                return g;
        }

        return null;
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
        _deckEntitiesCache = entities;
        MainDeckCount = MainDeckGroups.Sum(g => g.Sum(i => i.Entity.Quantity));
        SideboardCount = SideboardCards.Sum(i => i.Entity.Quantity);
        TotalCardCount = entities.Sum(e => e.Quantity);
        Stats = ComputeStatsAndCohesion(entities, _cardMapCache, out var cohesionProfile);
        UpdateSynergyCollections(cohesionProfile);
        OnPropertyChanged(nameof(DeckSummaryText));
        OnPropertyChanged(nameof(SideboardHeaderText));
        RefreshDeckListFilter();
        RecalculateDeckPriceTotals();
        _ = HydrateMissingDeckPricesAsync();
        _ = ApplyValidationUiAsync();
    }

    private async Task ApplyValidationUiAsync()
    {
        var v = Deck == null
            ? await _deckService.ValidateDeckAsync(_deckId)
            : await _deckService.ValidateDeckAsync(Deck, _deckEntitiesCache, _cardMapCache);
        int total = TotalCardCount;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusIsError = v.Level == ValidationLevel.Error;
            StatusMessage = GetValidationStatusMessage(v, total);
            SetValidationDetailLines(v);
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

    private static DeckStats ComputeStatsAndCohesion(
        List<DeckCardEntity> entities,
        Dictionary<string, Card> cardMap,
        out DeckCohesionProfile cohesionProfile)
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
                ManaCostPipAnalyzer.Accumulate(card.ManaCost, qty, stats.ManaPipCounts);
            }
        }

        stats.AvgCmc = cmcCount > 0 ? Math.Round(totalCmc / cmcCount, 2) : 0;
        cohesionProfile = DeckCohesionAnalyzer.BuildProfileAndRoles(entities, cardMap, stats);
        return stats;
    }

    private void RefreshDeckDataTruthLabels()
    {
        DeckDataTruthCatalogLine = DataTruthLabels.FormatCatalogLine(AppDataManager.GetLocalDatabaseVersion());
        DeckDataTruthPricesLine = DataTruthLabels.FormatPricesLine(CardPriceManager.GetPersistedPricesMetaDate());
    }

    private void UpdateSynergyCollections(DeckCohesionProfile profile)
    {
        var sub = new ObservableCollection<string>();
        foreach (var (label, count) in DeckCohesionAnalyzer.TopSubtypes(profile, 10))
            sub.Add($"{label} — {count}");
        SynergySubtypeSummaryLines = sub;

        var kw = new ObservableCollection<string>();
        foreach (var (label, count) in DeckCohesionAnalyzer.TopKeywords(profile, 8))
            kw.Add($"{label} — {count}");
        SynergyKeywordSummaryLines = kw;

        OnPropertyChanged(nameof(HasSynergySubtypeSummary));
        OnPropertyChanged(nameof(HasSynergyKeywordSummary));
        AddCardsCohesionProfileHook?.Invoke(profile);
    }
}
