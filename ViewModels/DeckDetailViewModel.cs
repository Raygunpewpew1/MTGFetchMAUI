using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services.DeckBuilder;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AetherVault.ViewModels;

/// <summary>
/// Represents a single card row in the deck editor list.
/// </summary>
public class DeckCardDisplayItem
{
    public DeckCardEntity Entity { get; set; } = null!;
    public Card Card { get; set; } = null!;

    public string DisplayName => Card?.Name ?? Entity.CardId;
    public string ManaCostText => Card?.ManaCost ?? "";
    public double CMC => Card?.FaceManaValue ?? 0;
}

/// <summary>
/// Grouped list of DeckCardDisplayItems for CollectionView IsGrouped support.
/// </summary>
public class DeckCardGroup(string name, IEnumerable<DeckCardDisplayItem> items)
    : ObservableCollection<DeckCardDisplayItem>(items)
{
    public string GroupName { get; } = name;
}

public partial class DeckDetailViewModel(DeckBuilderService deckService, ICardRepository cardRepository) : BaseViewModel
{
    private readonly DeckBuilderService _deckService = deckService;
    private readonly ICardRepository _cardRepository = cardRepository;
    private int _deckId;

    /// <summary>Raised on the main thread after deck data has been reloaded (so the page can force layout/redraw).</summary>
    public event Action? ReloadCompleted;

    private LastAddedInfo? _lastAdded;

    [ObservableProperty]
    public partial DeckEntity? Deck { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<DeckCardGroup> MainDeckGroups { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> SideboardCards { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DeckCardDisplayItem> CommanderCards { get; set; } = [];

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
    public partial string DeckFormat { get; set; } = "";

    public bool HasNoCommander => CommanderCards.Count == 0;

    [ObservableProperty]
    public partial string LastAddedSummaryText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasLastAdded { get; set; }

    private IAsyncRelayCommand? _undoLastAddedCommand;
    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand UndoLastAddedCommand => _undoLastAddedCommand ??= new AsyncRelayCommand(UndoLastAddedAsync);

    private IAsyncRelayCommand? _suggestLandsCommand;
    /// <summary>Explicit command for XAML compiled bindings (MAUIG2045).</summary>
    public IAsyncRelayCommand SuggestLandsCommand => _suggestLandsCommand ??= new AsyncRelayCommand(SuggestLandsAsync);

    [RelayCommand]
    private void SelectCommander() => SelectedSectionIndex = 0;

    [RelayCommand]
    private void SelectMain() => SelectedSectionIndex = 1;

    [RelayCommand]
    private void SelectSideboard() => SelectedSectionIndex = 2;

    [RelayCommand]
    private void SelectStats() => SelectedSectionIndex = 3;

    public void RegisterLastAdded(string cardId, string cardName, string section, int quantity)
    {
        _lastAdded = new LastAddedInfo(cardId, section, quantity, cardName);
        LastAddedSummaryText = $"{quantity}× {cardName} added to {section}.";
        HasLastAdded = true;
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
                StatusIsError = false;
                StatusMessage = UserMessages.UndidLastAdd(_lastAdded.Quantity, _lastAdded.CardName, _lastAdded.Section);
                HasLastAdded = false;
                _lastAdded = null;
                await ReloadAsync(preserveState: true);
            }
            else
            {
                StatusIsError = true;
                StatusMessage = result.Message ?? UserMessages.CouldNotUndoLastAdd();
            }
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.CouldNotUndoLastAdd(ex.Message);
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
                ? await _cardRepository.GetCardsByUUIDsAsync(uuids)
                : [];

            var (commander, main, sideboard) = MapEntitiesToSectionLists(cardEntities, cardMap);

            var mainDeckGroups = BuildGroups(main);
            var totalCardCount = cardEntities.Sum(c => c.Quantity);
            var stats = ComputeStats(cardEntities, cardMap);
            var validation = await _deckService.ValidateDeckAsync(deckId);
            var statusMessage = GetValidationStatusMessage(validation, totalCardCount);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                CommanderCards = new ObservableCollection<DeckCardDisplayItem>(commander);
                SideboardCards = new ObservableCollection<DeckCardDisplayItem>(sideboard);
                MainDeckGroups = mainDeckGroups;
                TotalCardCount = totalCardCount;
                Stats = stats;
                OnPropertyChanged(nameof(HasNoCommander));
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
        var result = await _deckService.UpdateQuantityAsync(
            Deck.Id, item.Entity.CardId, item.Entity.Quantity + 1, item.Entity.Section);
        if (result.IsSuccess)
        {
            StatusIsError = false;
            StatusMessage = UserMessages.UpdatedQuantity(item.DisplayName);
            await ReloadAsync(preserveState: true);
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
        int newQty = item.Entity.Quantity - 1;
        var result = await _deckService.UpdateQuantityAsync(
            Deck.Id, item.Entity.CardId, newQty, item.Entity.Section);
        if (result.IsSuccess)
        {
            StatusIsError = false;
            StatusMessage = UserMessages.UpdatedQuantity(item.DisplayName);
            await ReloadAsync(preserveState: true);
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
        await _deckService.RemoveCardAsync(Deck.Id, item.Entity.CardId, item.Entity.Section);
        await ReloadAsync(preserveState: true);
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
                groups.Add(new DeckCardGroup(key, list));
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
                double cmc = card.FaceManaValue;
                totalCmc += cmc * qty;
                cmcCount += qty;
                int slot = Math.Min((int)cmc, 10);
                stats.ManaCurve[slot] += qty;
            }
        }

        stats.AvgCMC = cmcCount > 0 ? Math.Round(totalCmc / cmcCount, 2) : 0;
        return stats;
    }

    private sealed record LastAddedInfo(string CardId, string Section, int Quantity, string CardName);
}
