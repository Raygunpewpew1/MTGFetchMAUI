using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Data;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services.DeckBuilder;
using System.Collections.ObjectModel;

namespace MTGFetchMAUI.ViewModels;

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
public class DeckCardGroup : ObservableCollection<DeckCardDisplayItem>
{
    public string GroupName { get; }

    public DeckCardGroup(string name, IEnumerable<DeckCardDisplayItem> items) : base(items)
    {
        GroupName = name;
    }
}

public partial class DeckDetailViewModel : BaseViewModel
{
    private readonly DeckBuilderService _deckService;
    private readonly ICardRepository _cardRepository;

    [ObservableProperty]
    private DeckEntity? _deck;

    [ObservableProperty]
    private ObservableCollection<DeckCardGroup> _mainDeckGroups = [];

    [ObservableProperty]
    private ObservableCollection<DeckCardDisplayItem> _sideboardCards = [];

    [ObservableProperty]
    private ObservableCollection<DeckCardDisplayItem> _commanderCards = [];

    [ObservableProperty]
    private DeckStats _stats = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommanderTab))]
    [NotifyPropertyChangedFor(nameof(IsMainTab))]
    [NotifyPropertyChangedFor(nameof(IsSideboardTab))]
    [NotifyPropertyChangedFor(nameof(IsStatsTab))]
    [NotifyPropertyChangedFor(nameof(Tab0Color))]
    [NotifyPropertyChangedFor(nameof(Tab1Color))]
    [NotifyPropertyChangedFor(nameof(Tab2Color))]
    [NotifyPropertyChangedFor(nameof(Tab3Color))]
    private int _selectedSectionIndex = 1; // Default to Main tab

    public bool IsCommanderTab => SelectedSectionIndex == 0;
    public bool IsMainTab => SelectedSectionIndex == 1;
    public bool IsSideboardTab => SelectedSectionIndex == 2;
    public bool IsStatsTab => SelectedSectionIndex == 3;

    public Color Tab0Color => SelectedSectionIndex == 0 ? Color.FromArgb("#03DAC5") : Color.FromArgb("#2C2C2C");
    public Color Tab1Color => SelectedSectionIndex == 1 ? Color.FromArgb("#03DAC5") : Color.FromArgb("#2C2C2C");
    public Color Tab2Color => SelectedSectionIndex == 2 ? Color.FromArgb("#03DAC5") : Color.FromArgb("#2C2C2C");
    public Color Tab3Color => SelectedSectionIndex == 3 ? Color.FromArgb("#03DAC5") : Color.FromArgb("#2C2C2C");

    [ObservableProperty]
    private int _totalCardCount;

    [ObservableProperty]
    private string _deckFormat = "";

    public bool HasNoCommander => CommanderCards.Count == 0;

    public DeckDetailViewModel(DeckBuilderService deckService, ICardRepository cardRepository)
    {
        _deckService = deckService;
        _cardRepository = cardRepository;
    }

    [RelayCommand]
    private void SelectCommander() => SelectedSectionIndex = 0;

    [RelayCommand]
    private void SelectMain() => SelectedSectionIndex = 1;

    [RelayCommand]
    private void SelectSideboard() => SelectedSectionIndex = 2;

    [RelayCommand]
    private void SelectStats() => SelectedSectionIndex = 3;

    public async Task LoadAsync(int deckId)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = "Loading deck...";

        try
        {
            Deck = await _deckService.GetDeckAsync(deckId);
            if (Deck == null)
            {
                StatusIsError = true;
                StatusMessage = "Deck not found.";
                return;
            }

            DeckFormat = EnumExtensions.ParseDeckFormat(Deck.Format).ToDisplayName();

            var cardEntities = await _deckService.GetDeckCardsAsync(deckId);
            var uuids = cardEntities.Select(c => c.CardId).Distinct().ToArray();

            Dictionary<string, Card> cardMap = uuids.Length > 0
                ? await _cardRepository.GetCardsByUUIDsAsync(uuids)
                : [];

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

            CommanderCards = new ObservableCollection<DeckCardDisplayItem>(commander);
            SideboardCards = new ObservableCollection<DeckCardDisplayItem>(sideboard);
            MainDeckGroups = BuildGroups(main);
            TotalCardCount = cardEntities.Sum(c => c.Quantity);
            Stats = ComputeStats(cardEntities, cardMap);
            OnPropertyChanged(nameof(HasNoCommander));

            StatusMessage = $"{TotalCardCount} cards";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task IncrementCardAsync(DeckCardDisplayItem item)
    {
        if (Deck == null) return;
        var result = await _deckService.UpdateQuantityAsync(
            Deck.Id, item.Entity.CardId, item.Entity.Quantity + 1, item.Entity.Section);
        if (result.IsSuccess)
        {
            item.Entity.Quantity++;
            TotalCardCount++;
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
            TotalCardCount--;
            if (newQty <= 0)
                RemoveItemFromCollections(item);
            else
                item.Entity.Quantity = newQty;
        }
    }

    [RelayCommand]
    private async Task RemoveCardAsync(DeckCardDisplayItem item)
    {
        if (Deck == null) return;
        await _deckService.RemoveCardAsync(Deck.Id, item.Entity.CardId, item.Entity.Section);
        TotalCardCount -= item.Entity.Quantity;
        RemoveItemFromCollections(item);
    }

    private void RemoveItemFromCollections(DeckCardDisplayItem item)
    {
        switch (item.Entity.Section)
        {
            case "Commander":
                CommanderCards.Remove(item);
                OnPropertyChanged(nameof(HasNoCommander));
                break;
            case "Sideboard":
                SideboardCards.Remove(item);
                break;
            default:
                foreach (var g in MainDeckGroups)
                    if (g.Remove(item)) break;
                break;
        }
        RefreshStats();
    }

    private void RefreshStats()
    {
        var allEntities = new List<DeckCardEntity>();
        var cardMap = new Dictionary<string, Card>();

        void Collect(DeckCardDisplayItem i)
        {
            allEntities.Add(i.Entity);
            if (i.Card != null) cardMap[i.Entity.CardId] = i.Card;
        }

        foreach (var i in CommanderCards) Collect(i);
        foreach (var i in SideboardCards) Collect(i);
        foreach (var g in MainDeckGroups) foreach (var i in g) Collect(i);

        Stats = ComputeStats(allEntities, cardMap);
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
}
