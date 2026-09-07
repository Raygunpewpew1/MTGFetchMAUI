using System.Collections.ObjectModel;
using AetherVault.Constants;
using AetherVault.Controls;
using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>Add-cards modal: search, browse chips, staging, and commander suggestions. Uses <see cref="DeckDetailViewModel"/> for deck state and reload.</summary>
public partial class DeckAddCardsViewModel(
    CardManager cardManager,
    DeckBuilderService deckService,
    IToastService toast,
    IGridPriceLoadService gridPriceLoadService,
    DeckBrowseListResultCache quickBrowseListCache) : ObservableObject
{
    private readonly CardManager _cardManager = cardManager;
    private readonly DeckBuilderService _deckService = deckService;
    private readonly IToastService _toast = toast;
    private readonly IGridPriceLoadService _gridPriceLoadService = gridPriceLoadService;
    private readonly DeckBrowseListResultCache _quickBrowseListCache = quickBrowseListCache;

    private DeckDetailViewModel? _host;
    private CardGrid? _grid;
    private SearchOptions? _addCardSynergyPreset;
    private SearchOptions? _addCardQuickBrowseOptions;
    private string? _quickBrowseListLabel;
    private string? _quickBrowseCatalogKey;
    private CancellationTokenSource? _addCardSearchCts;
    private int _addCardSearchGeneration;
    private bool _pickerSyncFromHost;

    /// <summary>Set by <see cref="Pages.DeckAddCardsPage"/> while the modal is open.</summary>
    public Func<Task>? AddCardsModalDismissAction { get; set; }

    /// <summary>Sets the add-modal target section (0 = Commander, 1 = Main, 2 = Sideboard).</summary>
    public void PrepareModalTarget(int targetSectionIndex)
    {
        AddCardsModalTargetSectionIndex = targetSectionIndex is 0 or 2 ? targetSectionIndex : 1;
    }

    [ObservableProperty]
    public partial string AddCardSearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyAddCardWorkBusy))]
    public partial bool IsAddCardSearchBusy { get; set; }

    [ObservableProperty]
    public partial bool AddCardSearchOnlyCollection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyAddCardWorkBusy))]
    public partial bool IsDeckSuggestionBusy { get; set; }

    /// <summary>Single busy spinner for search or suggestion loading.</summary>
    public bool IsAnyAddCardWorkBusy => IsAddCardSearchBusy || IsDeckSuggestionBusy;

    [ObservableProperty]
    public partial bool AddCardResultsAreSuggestions { get; set; }

    public string[] CommanderArchetypePickerItems { get; } =
        [.. Enum.GetValues<CommanderArchetype>().Select(static a => a.ToDisplayName())];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrategyChipText))]
    public partial int CommanderArchetypePickerIndex { get; set; }

    /// <summary>e.g. "Strategy: Aggro" — chip that opens the strategy action sheet.</summary>
    public string StrategyChipText =>
        UserMessages.DeckAddStrategyChip(
            CommanderArchetypePickerIndex >= 0 && CommanderArchetypePickerIndex < CommanderArchetypePickerItems.Length
                ? CommanderArchetypePickerItems[CommanderArchetypePickerIndex]
                : CommanderArchetype.Unknown.ToDisplayName());

    [ObservableProperty]
    public partial ObservableCollection<DeckAddSearchResultRow> AddCardSearchResultRows { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<StagedDeckAddItem> StagedAddItems { get; set; } = [];

    [ObservableProperty]
    public partial int AddCardsModalTargetSectionIndex { get; set; } = 1;

    [ObservableProperty]
    public partial ObservableCollection<DeckSynergyChipItem> AddCardSynergyPresetChips { get; set; } = [];

    public ObservableCollection<DeckBrowseListChipItem> QuickBrowseListChips { get; } =
        new(DeckBrowseListCatalog.CreateChipItems());

    public bool HasSynergyPresetChips => AddCardSynergyPresetChips.Count > 0;

    public bool HasAddCardStructuredFilter => _addCardSynergyPreset != null || _addCardQuickBrowseOptions != null;

    public string AddCardStructuredFilterCaption =>
        !string.IsNullOrEmpty(_quickBrowseListLabel)
            ? $"{UserMessages.DeckAddListFilterPrefix}{_quickBrowseListLabel}"
            : _addCardSynergyPreset != null ? UserMessages.DeckAddThemeFilterActiveCaption : "";

    public bool IsStagedAddActive => AddCardsModalTargetSectionIndex is 1 or 2;

    public bool IsAddModalCommanderFlow => AddCardsModalTargetSectionIndex == 0;

    public bool IsAddModalTargetMain => AddCardsModalTargetSectionIndex == 1;

    public bool IsAddModalTargetSideboard => AddCardsModalTargetSectionIndex == 2;

    public string AddStagedCardsToDeckButtonText =>
        AddCardsModalTargetSectionIndex == 2
            ? UserMessages.DeckAddApplyToSideboard
            : UserMessages.DeckAddApplyToMain;

    public string StagedAddSummaryText =>
        StagedAddItems.Count == 0
            ? UserMessages.DeckAddStagingEmpty
            : UserMessages.DeckAddStagingSummary(StagedAddItems.Count, StagedAddItems.Sum(s => s.Quantity));

    public bool HasStagedAddItems => StagedAddItems.Count > 0;

    public bool IsAddCardResultsEmpty => AddCardSearchResultRows.Count == 0;

    public bool ShowDeckSuggestionUi =>
        _host?.Deck != null
        && EnumExtensions.ParseDeckFormat(_host.Deck.Format).IsCommanderLikeRules()
        && !_host.HasNoCommander;

    public void AttachHost(DeckDetailViewModel host)
    {
        DetachHost();
        _host = host;
        _host.ReloadCompleted += OnHostReloadCompleted;
        _host.AddCardsCohesionProfileHook += OnCohesionProfileFromHost;
        SyncArchetypePickerFromHostDeck();
        var (_, profile) = _host.BuildCohesionSnapshot();
        ApplySynergyChipsFromProfile(profile);
    }

    public void DetachHost()
    {
        if (_host != null)
        {
            _host.ReloadCompleted -= OnHostReloadCompleted;
            _host.AddCardsCohesionProfileHook -= OnCohesionProfileFromHost;
            _host = null;
        }
    }

    public void AttachGrid(CardGrid grid)
    {
        DetachGrid();
        _grid = grid;
        _grid.VisibleRangeChanged += OnVisibleRangeChanged;
        _grid.IsDragEnabled = false;
        _grid.UseCompactGrid = true;
    }

    public void DetachGrid()
    {
        if (_grid != null)
        {
            _grid.VisibleRangeChanged -= OnVisibleRangeChanged;
            _grid = null;
        }
    }

    private void OnVisibleRangeChanged(int start, int end) =>
        _gridPriceLoadService.LoadVisiblePrices(_grid, start, end);

    private void OnHostReloadCompleted()
    {
        SyncArchetypePickerFromHostDeck();
        OnPropertyChanged(nameof(ShowDeckSuggestionUi));
    }

    private void SyncArchetypePickerFromHostDeck()
    {
        if (_host?.Deck == null) return;
        _pickerSyncFromHost = true;
        CommanderArchetypePickerIndex = (int)EnumExtensions.ParseCommanderArchetype(_host.Deck.CommanderArchetype);
        _pickerSyncFromHost = false;
    }

    private void OnCohesionProfileFromHost(DeckCohesionProfile profile) => ApplySynergyChipsFromProfile(profile);

    /// <summary>Rebuilds theme chips from cohesion profile (invoked when deck loads / stats refresh).</summary>
    public void ApplySynergyChipsFromProfile(DeckCohesionProfile profile)
    {
        var chips = new ObservableCollection<DeckSynergyChipItem>();
        foreach (var (label, count) in DeckCohesionAnalyzer.TopSubtypes(profile, 6))
        {
            chips.Add(new DeckSynergyChipItem
            {
                DisplayText = $"{label} ({count})",
                SubtypeOrKeywordValue = label,
                IsSubtype = true
            });
        }

        foreach (var (label, count) in DeckCohesionAnalyzer.TopKeywords(profile, 4))
        {
            chips.Add(new DeckSynergyChipItem
            {
                DisplayText = $"{label} ({count})",
                SubtypeOrKeywordValue = label,
                IsSubtype = false
            });
        }

        AddCardSynergyPresetChips = chips;
        OnPropertyChanged(nameof(HasSynergyPresetChips));
    }

    private void RaiseAddCardStructuredFilterChanged()
    {
        OnPropertyChanged(nameof(HasAddCardStructuredFilter));
        OnPropertyChanged(nameof(AddCardStructuredFilterCaption));
    }

    private void NotifyStagedAddPresentationChanged()
    {
        OnPropertyChanged(nameof(StagedAddSummaryText));
        OnPropertyChanged(nameof(HasStagedAddItems));
    }

    partial void OnAddCardsModalTargetSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsStagedAddActive));
        OnPropertyChanged(nameof(IsAddModalCommanderFlow));
        OnPropertyChanged(nameof(IsAddModalTargetMain));
        OnPropertyChanged(nameof(IsAddModalTargetSideboard));
        OnPropertyChanged(nameof(AddStagedCardsToDeckButtonText));
    }

    partial void OnAddCardSearchTextChanged(string value)
    {
        AddCardResultsAreSuggestions = false;
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
        AddCardResultsAreSuggestions = false;
        if (!string.IsNullOrWhiteSpace(AddCardSearchText) || _addCardSynergyPreset != null || _addCardQuickBrowseOptions != null)
            _ = ExecuteAddCardSearchAsync();
        else
            MainThread.BeginInvokeOnMainThread(() =>
            {
                AddCardSearchResultRows = [];
                RefreshAddCardGrid();
            });
    }

    partial void OnCommanderArchetypePickerIndexChanged(int value)
    {
        if (_pickerSyncFromHost || _host?.Deck == null) return;
        if (value < 0 || value >= Enum.GetValues<CommanderArchetype>().Length) return;
        _ = SaveDeckArchetypeAsync((CommanderArchetype)value);
    }

    private async Task SaveDeckArchetypeAsync(CommanderArchetype archetype)
    {
        if (_host?.Deck == null) return;
        try
        {
            await _deckService.UpdateCommanderArchetypeAsync(_host.Deck.Id, archetype);
            _host.Deck.CommanderArchetype = archetype.ToArchetypeDbValue();
        }
        catch (Exception ex)
        {
            _host.StatusIsError = true;
            _host.StatusMessage = UserMessages.Error(ex.Message);
        }
    }

    public IAsyncRelayCommand AddCardSearchCommand => _addCardSearchCommand ??= new AsyncRelayCommand(ExecuteAddCardSearchAsync);
    private IAsyncRelayCommand? _addCardSearchCommand;

    private async Task ExecuteAddCardSearchAsync()
    {
        if (_host == null) return;

        var query = (AddCardSearchText ?? "").Trim();
        int myGen = ++_addCardSearchGeneration;

        IsAddCardSearchBusy = true;
        try
        {
            bool useSynergy = _addCardSynergyPreset != null;
            bool useQuickBrowse = _addCardQuickBrowseOptions != null;
            bool useStructured = useSynergy || useQuickBrowse;
            if (!useStructured && string.IsNullOrEmpty(query))
            {
                if (AddCardResultsAreSuggestions)
                    return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (myGen != _addCardSearchGeneration) return;
                    AddCardSearchResultRows = [];
                    OnPropertyChanged(nameof(IsAddCardResultsEmpty));
                    RefreshAddCardGrid();
                });
                return;
            }

            var cohesionEntitiesSnap = _host.GetDeckEntitiesSnapshotForSynergy().ToList();
            var cohesionStagedSnap = new HashSet<string>(StagedAddItems.Select(s => s.Card.Uuid), StringComparer.OrdinalIgnoreCase);
            IReadOnlyDictionary<string, Card> cohesionMapSnap = _host.GetDeckCardMapSnapshotForSynergy();

            Card[] cards;
            if (useStructured)
            {
                var fmt = _host.Deck != null
                    ? EnumExtensions.ParseDeckFormat(_host.Deck.Format)
                    : DeckFormat.Standard;
                string? namePart = string.IsNullOrEmpty(query) ? null : query;
                var structuredOptions = useSynergy ? _addCardSynergyPreset! : _addCardQuickBrowseOptions!;
                int structuredLimit = useQuickBrowse
                    ? DeckBrowseListCatalog.QuickBrowseSqlRowLimit(_quickBrowseCatalogKey, namePart)
                    : 50;

                var quickListDidHitCache = false;
                if (useQuickBrowse
                    && !string.IsNullOrEmpty(_quickBrowseCatalogKey)
                    && _quickBrowseListCache.TryGet(
                        _quickBrowseCatalogKey,
                        fmt,
                        AddCardSearchOnlyCollection,
                        namePart,
                        out var cachedCards)
                    && cachedCards != null)
                {
                    cards = cachedCards;
                    quickListDidHitCache = true;
                }
                else
                {
                    cards = await _cardManager.SearchCardsWithOptionsAsync(
                        structuredOptions,
                        namePart,
                        AddCardSearchOnlyCollection,
                        structuredLimit,
                        restrictToDeckLegalFormat: true,
                        fmt).ConfigureAwait(false);
                }

                if (useQuickBrowse)
                    cards = DeckBrowseListNameCollapse.ApplyIfNeeded(_quickBrowseCatalogKey, namePart, cards);

                if (useQuickBrowse
                    && !string.IsNullOrEmpty(_quickBrowseCatalogKey)
                    && !quickListDidHitCache)
                    _quickBrowseListCache.Set(
                        _quickBrowseCatalogKey,
                        fmt,
                        AddCardSearchOnlyCollection,
                        namePart,
                        cards);
            }
            else
            {
                cards = AddCardSearchOnlyCollection
                    ? await _cardManager.SearchInCollectionAsync(query, 50).ConfigureAwait(false)
                    : await _cardManager.SearchCardsAsync(query, 50).ConfigureAwait(false);
            }

            if (myGen != _addCardSearchGeneration) return;

            var builtRows = await Task.Run(() =>
            {
                return cards.Select(c =>
                {
                    var row = new DeckAddSearchResultRow(c, cohesionStagedSnap.Contains(c.Uuid));
                    if (useSynergy)
                        row.SynergyHintText = DeckCohesionAnalyzer.FormatOverlapHint(
                            c, cohesionEntitiesSnap, cohesionMapSnap) ?? "";
                    return row;
                }).ToList();
            }).ConfigureAwait(false);

            if (myGen != _addCardSearchGeneration) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (myGen != _addCardSearchGeneration) return;
                AddCardSearchResultRows = new ObservableCollection<DeckAddSearchResultRow>(builtRows);
                OnPropertyChanged(nameof(IsAddCardResultsEmpty));
                RefreshAddCardGrid();
            });
        }
        finally
        {
            if (myGen == _addCardSearchGeneration)
                MainThread.BeginInvokeOnMainThread(() => IsAddCardSearchBusy = false);
        }
    }

    public IAsyncRelayCommand LoadDeckSuggestionsCommand =>
        _loadDeckSuggestionsCommand ??= new AsyncRelayCommand(ExecuteLoadDeckSuggestionsAsync);
    private IAsyncRelayCommand? _loadDeckSuggestionsCommand;

    private async Task ExecuteLoadDeckSuggestionsAsync()
    {
        if (_host == null || _host.HasNoCommander || _host.Deck == null
            || !EnumExtensions.ParseDeckFormat(_host.Deck.Format).IsCommanderLikeRules())
        {
            _toast.Show(UserMessages.DeckAddSuggestionsNeedCommander, 4000);
            return;
        }

        var commanderItem = _host.FirstCommander;
        if (commanderItem?.Card == null)
        {
            _toast.Show(UserMessages.DeckAddSuggestionsNeedCommander, 4000);
            return;
        }

        int myGen = ++_addCardSearchGeneration;
        IsDeckSuggestionBusy = true;
        AddCardResultsAreSuggestions = false;
        var cohesionEntitiesSnap = _host.GetDeckEntitiesSnapshotForSynergy().ToList();
        var cohesionStagedSnap = new HashSet<string>(StagedAddItems.Select(s => s.Card.Uuid), StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, Card> cohesionMapSnap = _host.GetDeckCardMapSnapshotForSynergy();
        try
        {
            await _cardManager.EnsureInitializedAsync().ConfigureAwait(false);
            var archetype = (CommanderArchetype)CommanderArchetypePickerIndex;
            var (stats, profile) = _host.BuildCohesionSnapshot();
            var cards = await _cardManager.GetDeckSuggestionsAsync(
                _host.Deck,
                archetype,
                commanderItem.Card,
                cohesionEntitiesSnap,
                cohesionMapSnap,
                stats,
                profile,
                AddCardSearchOnlyCollection,
                maxResults: 45).ConfigureAwait(false);

            if (myGen != _addCardSearchGeneration) return;

            if (cards.Length == 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    AddCardResultsAreSuggestions = false;
                    AddCardSearchResultRows = [];
                    OnPropertyChanged(nameof(IsAddCardResultsEmpty));
                    RefreshAddCardGrid();
                    _toast.Show(UserMessages.DeckAddSuggestionsNone, 4000);
                });
                return;
            }

            var builtRows = await Task.Run(() =>
            {
                return cards.Select(c =>
                {
                    var row = new DeckAddSearchResultRow(c, cohesionStagedSnap.Contains(c.Uuid));
                    row.SynergyHintText = DeckCohesionAnalyzer.FormatOverlapHint(
                        c, cohesionEntitiesSnap, cohesionMapSnap) ?? "";
                    return row;
                }).ToList();
            }).ConfigureAwait(false);

            if (myGen != _addCardSearchGeneration) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (myGen != _addCardSearchGeneration) return;
                AddCardResultsAreSuggestions = true;
                AddCardSearchResultRows = new ObservableCollection<DeckAddSearchResultRow>(builtRows);
                OnPropertyChanged(nameof(IsAddCardResultsEmpty));
                RefreshAddCardGrid();
            });
        }
        catch (Exception ex)
        {
            if (myGen == _addCardSearchGeneration)
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    AddCardResultsAreSuggestions = false;
                    if (_host != null)
                    {
                        _host.StatusIsError = true;
                        _host.StatusMessage = UserMessages.SearchFailed(ex.Message);
                    }
                });
        }
        finally
        {
            if (myGen == _addCardSearchGeneration)
                MainThread.BeginInvokeOnMainThread(() => IsDeckSuggestionBusy = false);
        }
    }

    public void ClearAddCardSearch()
    {
        _addCardSearchCts?.Cancel();
        AddCardSearchText = "";
        AddCardSearchResultRows = [];
        StagedAddItems = [];
        IsAddCardSearchBusy = false;
        AddCardResultsAreSuggestions = false;
        _addCardSynergyPreset = null;
        _addCardQuickBrowseOptions = null;
        _quickBrowseListLabel = null;
        _quickBrowseCatalogKey = null;
        RaiseAddCardStructuredFilterChanged();
        NotifyStagedAddPresentationChanged();
        _grid?.ClearCards();
        OnPropertyChanged(nameof(IsAddCardResultsEmpty));
    }

    /// <summary>On open: load deck suggestions right away when they apply, otherwise run the (possibly empty) search.</summary>
    public void NotifyAddCardsSheetAppeared()
    {
        if (ShouldAutoLoadSuggestions())
            _ = ExecuteLoadDeckSuggestionsAsync();
        else
            _ = ExecuteAddCardSearchAsync();
    }

    private bool ShouldAutoLoadSuggestions() =>
        AddCardsModalTargetSectionIndex != 0
        && string.IsNullOrWhiteSpace(AddCardSearchText)
        && _addCardSynergyPreset == null
        && _addCardQuickBrowseOptions == null
        && AddCardSearchResultRows.Count == 0
        && ShowDeckSuggestionUi;

    [RelayCommand]
    private void ApplyAddCardSynergyPreset(DeckSynergyChipItem? chip)
    {
        if (chip == null) return;
        AddCardResultsAreSuggestions = false;
        _addCardQuickBrowseOptions = null;
        _quickBrowseListLabel = null;
        _quickBrowseCatalogKey = null;
        _addCardSynergyPreset = chip.ToPresetSearchOptions();
        RaiseAddCardStructuredFilterChanged();
        _ = ExecuteAddCardSearchAsync();
    }

    private void ApplyQuickBrowseListCore(DeckBrowseListChipItem item)
    {
        AddCardResultsAreSuggestions = false;
        _addCardSynergyPreset = null;
        _addCardQuickBrowseOptions = DeckBrowseListCatalog.CreateOptions(item.Key);
        _quickBrowseListLabel = item.DisplayText;
        _quickBrowseCatalogKey = item.Key;
        RaiseAddCardStructuredFilterChanged();
    }

    [RelayCommand]
    private void ApplyQuickBrowseList(DeckBrowseListChipItem? item)
    {
        if (item == null) return;
        ApplyQuickBrowseListCore(item);
        _ = ExecuteAddCardSearchAsync();
    }

    [RelayCommand]
    private void ClearAddCardSynergyPreset()
    {
        AddCardResultsAreSuggestions = false;
        _addCardSynergyPreset = null;
        _addCardQuickBrowseOptions = null;
        _quickBrowseListLabel = null;
        _quickBrowseCatalogKey = null;
        RaiseAddCardStructuredFilterChanged();
        if (string.IsNullOrWhiteSpace(AddCardSearchText))
            MainThread.BeginInvokeOnMainThread(() => AddCardSearchResultRows = []);
        else
            _ = ExecuteAddCardSearchAsync();
    }

    [RelayCommand]
    private void SetAddModalTargetMain() => AddCardsModalTargetSectionIndex = 1;

    [RelayCommand]
    private void SetAddModalTargetSideboard() => AddCardsModalTargetSectionIndex = 2;

    public IRelayCommand ToggleStagedSearchRowCommand => _toggleStagedSearchRowCommand ??= new RelayCommand<DeckAddSearchResultRow?>(ToggleStagedSearchRow);
    private IRelayCommand? _toggleStagedSearchRowCommand;

    public IRelayCommand IncrementStagedAddQuantityCommand => _incrementStagedAddQuantityCommand ??= new RelayCommand<StagedDeckAddItem?>(IncrementStagedAddQuantity);
    private IRelayCommand? _incrementStagedAddQuantityCommand;

    public IRelayCommand DecrementStagedAddQuantityCommand => _decrementStagedAddQuantityCommand ??= new RelayCommand<StagedDeckAddItem?>(DecrementStagedAddQuantity);
    private IRelayCommand? _decrementStagedAddQuantityCommand;

    public IAsyncRelayCommand AddStagedCardsToDeckCommand => _addStagedCardsToDeckCommand ??= new AsyncRelayCommand(AddStagedCardsToDeckAsync);
    private IAsyncRelayCommand? _addStagedCardsToDeckCommand;

    public IRelayCommand ClearStagedAddsCommand => _clearStagedAddsCommand ??= new RelayCommand(ClearStagedAdds);
    private IRelayCommand? _clearStagedAddsCommand;

    private void ToggleStagedSearchRow(DeckAddSearchResultRow? row)
    {
        if (row == null || _host?.Deck == null) return;

        if (AddCardsModalTargetSectionIndex == 0)
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

        NotifyStagedAddPresentationChanged();
        RefreshAddCardGrid();
    }

    private void IncrementStagedAddQuantity(StagedDeckAddItem? item)
    {
        if (item == null) return;
        item.Quantity++;
        NotifyStagedAddPresentationChanged();
        RefreshAddCardGrid();
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

        NotifyStagedAddPresentationChanged();
        RefreshAddCardGrid();
    }

    private void SyncSearchRowStagedState(string cardUuid, bool staged)
    {
        var row = AddCardSearchResultRows.FirstOrDefault(r => r.Card.Uuid == cardUuid);
        if (row != null)
            row.IsStaged = staged;
    }

    private async Task AddCardFromSearchAsync(Card? card)
    {
        if (card == null || _host?.Deck == null) return;

        if (AddCardsModalTargetSectionIndex == 0)
        {
            var result = await _deckService.SetCommanderAsync(_host.Deck.Id, card.Uuid);
            if (result.IsError)
            {
                _host.StatusIsError = true;
                _host.StatusMessage = result.Message ?? UserMessages.CouldNotSetCommander();
            }
            else
            {
                _host.StatusIsError = false;
                _host.StatusMessage = !string.IsNullOrWhiteSpace(result.Message) ? result.Message : $"{card.Name} set as commander.";
                await _host.ReloadAsync(preserveState: true);
                var dismiss = AddCardsModalDismissAction;
                if (dismiss != null)
                    await dismiss();
            }

            return;
        }

        string section = AddCardsModalTargetSectionIndex == 2 ? "Sideboard" : "Main";
        var cardsBefore = await _deckService.GetDeckCardsAsync(_host.Deck.Id);
        int qtyBefore = cardsBefore.FirstOrDefault(c => c.CardId == card.Uuid && c.Section == section)?.Quantity ?? 0;
        var addResult = await _deckService.AddCardAsync(_host.Deck.Id, card.Uuid, 1, section);
        if (addResult.IsError)
        {
            _host.StatusIsError = true;
            _host.StatusMessage = addResult.Message ?? UserMessages.CouldNotAddCardToDeck();
        }
        else
        {
            _host.StatusIsError = false;
            _host.StatusMessage = UserMessages.CardsAddedToSection(1, card.Name, section);
            _host.PushDeckEditorUndoFrame(
            [
                new DeckEditorMutation(DeckEditorMutationKind.SetQuantity, card.Uuid, section, null, qtyBefore)
            ], UserMessages.CardsAddedToSection(1, card.Name, section));
            await _host.ReloadAsync(preserveState: true);
        }
    }

    private async Task AddStagedCardsToDeckAsync()
    {
        if (_host?.Deck == null || StagedAddItems.Count == 0) return;

        if (AddCardsModalTargetSectionIndex == 0)
        {
            var first = StagedAddItems[0].Card;
            var result = await _deckService.SetCommanderAsync(_host.Deck.Id, first.Uuid);
            if (result.IsError)
            {
                _host.StatusIsError = true;
                _host.StatusMessage = result.Message ?? UserMessages.CouldNotSetCommander();
            }
            else
            {
                _host.StatusIsError = false;
                _host.StatusMessage = StagedAddItems.Count > 1
                    ? $"{first.Name} set as commander. (Additional staged cards were not added.)"
                    : $"{first.Name} set as commander.";
                StagedAddItems.Clear();
                AddCardSearchResultRows = [];
                NotifyStagedAddPresentationChanged();
                await _host.ReloadAsync(preserveState: true);
            }

            return;
        }

        string section = AddCardsModalTargetSectionIndex == 2 ? "Sideboard" : "Main";
        var deckCardsBefore = await _deckService.GetDeckCardsAsync(_host.Deck.Id);
        var qtyBeforeList = StagedAddItems
            .Select(s => deckCardsBefore.FirstOrDefault(c => c.CardId == s.Card.Uuid && c.Section == section)?.Quantity ?? 0)
            .ToList();
        var mutations = StagedAddItems
            .Select(s => new DeckEditorMutation(DeckEditorMutationKind.Add, s.Card.Uuid, section, null, s.Quantity))
            .ToList();
        var addResult = await _deckService.ApplyEditorMutationsAsync(_host.Deck.Id, mutations);
        if (addResult.IsError)
        {
            _host.StatusIsError = true;
            _host.StatusMessage = addResult.Message ?? UserMessages.CouldNotAddCardToDeck();
            return;
        }

        int total = StagedAddItems.Sum(s => s.Quantity);
        _host.PushDeckEditorUndoFrame(
            [.. StagedAddItems.Select((s, i) =>
                new DeckEditorMutation(DeckEditorMutationKind.SetQuantity, s.Card.Uuid, section, null, qtyBeforeList[i]))],
            $"Added {total} card(s) to {section}.");
        _host.StatusIsError = false;
        _host.StatusMessage = $"Added {total} card(s) to {section}.";
        StagedAddItems.Clear();
        AddCardSearchResultRows = [];
        NotifyStagedAddPresentationChanged();
        await _host.ReloadAsync(preserveState: true);
    }

    private void ClearStagedAdds()
    {
        foreach (var row in AddCardSearchResultRows)
            row.IsStaged = false;
        StagedAddItems.Clear();
        NotifyStagedAddPresentationChanged();
        RefreshAddCardGrid();
    }

    /// <summary>Card grid tap: stage/unstage or commander pick by UUID.</summary>
    public void OnResultCardClicked(string cardUuid)
    {
        var row = AddCardSearchResultRows.FirstOrDefault(r => r.Card.Uuid == cardUuid);
        ToggleStagedSearchRow(row);
    }

    private void RefreshAddCardGrid()
    {
        if (_grid == null) return;
        var rows = AddCardSearchResultRows;
        if (rows.Count == 0)
        {
            _grid.ClearCards();
            return;
        }

        var cards = new Card[rows.Count];
        var badges = new int[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            cards[i] = rows[i].Card;
            if (rows[i].IsStaged)
            {
                int q = StagedAddItems.FirstOrDefault(s => s.Card.Uuid == rows[i].Card.Uuid)?.Quantity ?? 1;
                badges[i] = q;
            }
            else
                badges[i] = 0;
        }

        _grid.SetCards(cards, badges);
    }
}
