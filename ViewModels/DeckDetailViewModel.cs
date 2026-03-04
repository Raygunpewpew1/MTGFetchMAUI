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
    public partial int SelectedSectionIndex { get; set; } = 1; // Default to Main tab

    public bool IsCommanderTab => SelectedSectionIndex == 0;
    public bool IsMainTab => SelectedSectionIndex == 1;
    public bool IsSideboardTab => SelectedSectionIndex == 2;
    public bool IsStatsTab => SelectedSectionIndex == 3;

    public Color Tab0Color => SelectedSectionIndex == 0 ? Color.FromArgb("#03DAC5") : Color.FromArgb("#2C2C2C");
    public Color Tab1Color => SelectedSectionIndex == 1 ? Color.FromArgb("#03DAC5") : Color.FromArgb("#2C2C2C");
    public Color Tab2Color => SelectedSectionIndex == 2 ? Color.FromArgb("#03DAC5") : Color.FromArgb("#2C2C2C");
    public Color Tab3Color => SelectedSectionIndex == 3 ? Color.FromArgb("#03DAC5") : Color.FromArgb("#2C2C2C");

    [ObservableProperty]
    public partial int TotalCardCount { get; set; }

    [ObservableProperty]
    public partial string DeckFormat { get; set; } = "";

    public bool HasNoCommander => CommanderCards.Count == 0;

    [RelayCommand]
    private void SelectCommander() => SelectedSectionIndex = 0;

    [RelayCommand]
    private void SelectMain() => SelectedSectionIndex = 1;

    [RelayCommand]
    private void SelectSideboard() => SelectedSectionIndex = 2;

    [RelayCommand]
    private void SelectStats() => SelectedSectionIndex = 3;

    public async Task ReloadAsync() => await LoadAsync(_deckId);

    public async Task LoadAsync(int deckId)
    {
        _deckId = deckId;
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

            var mainDeckGroups = BuildGroups(main);
            var totalCardCount = cardEntities.Sum(c => c.Quantity);
            var stats = ComputeStats(cardEntities, cardMap);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                CommanderCards = new ObservableCollection<DeckCardDisplayItem>(commander);
                SideboardCards = new ObservableCollection<DeckCardDisplayItem>(sideboard);
                MainDeckGroups = mainDeckGroups;
                TotalCardCount = totalCardCount;
                Stats = stats;
                OnPropertyChanged(nameof(HasNoCommander));
                StatusMessage = $"{TotalCardCount} cards";
            });
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
            await ReloadAsync();
    }

    [RelayCommand]
    private async Task DecrementCardAsync(DeckCardDisplayItem item)
    {
        if (Deck == null) return;
        int newQty = item.Entity.Quantity - 1;
        var result = await _deckService.UpdateQuantityAsync(
            Deck.Id, item.Entity.CardId, newQty, item.Entity.Section);
        if (result.IsSuccess)
            await ReloadAsync();
    }

    [RelayCommand]
    private async Task RemoveCardAsync(DeckCardDisplayItem item)
    {
        if (Deck == null) return;
        await _deckService.RemoveCardAsync(Deck.Id, item.Entity.CardId, item.Entity.Section);
        await ReloadAsync();
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
