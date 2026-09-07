using AetherVault.Models;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using UraniumUI.Icons.FontAwesome;

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

    [ObservableProperty]
    public partial CardPriceData? PriceData { get; set; }

    /// <summary>Unit price from bundled DB (vendor order in Settings). Empty when prices off or missing.</summary>
    public string DeckUnitPriceDisplay =>
        !PricePreferences.PricesDataEnabled ? "" : PriceDisplayHelper.GetDeckUnitPriceDisplay(PriceData);

    /// <summary>Line total (unit × in-deck qty) for deck lists and grid.</summary>
    public string DeckLinePriceDisplay =>
        !PricePreferences.PricesDataEnabled ? "" : PriceDisplayHelper.GetDeckLinePriceDisplay(PriceData, Entity.Quantity);

    /// <summary>Short collection hint for list rows.</summary>
    public string OwnedShortText => OwnedQuantity <= 0 ? "—" : $"Own {OwnedQuantity}";

    /// <summary>True when the deck plays more copies than the user owns (both must be &gt; 0).</summary>
    public bool IsOverCollection => OwnedQuantity > 0 && Entity.Quantity > OwnedQuantity;

    partial void OnOwnedQuantityChanged(int value)
    {
        OnPropertyChanged(nameof(OwnedShortText));
        OnPropertyChanged(nameof(IsOverCollection));
    }

    partial void OnPriceDataChanged(CardPriceData? value)
    {
        OnPropertyChanged(nameof(DeckUnitPriceDisplay));
        OnPropertyChanged(nameof(DeckLinePriceDisplay));
    }

    partial void OnEntityChanged(DeckCardEntity value)
    {
        OnPropertyChanged(nameof(DeckLinePriceDisplay));
    }

    /// <summary>Updates in-deck quantity and notifies quantity-related bindings.</summary>
    public void SetDeckQuantity(int quantity)
    {
        Entity.Quantity = quantity;
        OnPropertyChanged(nameof(InDeckSummary));
        OnPropertyChanged(nameof(DeckQtyLabel));
        OnPropertyChanged(nameof(IsOverCollection));
        OnPropertyChanged(nameof(DeckLinePriceDisplay));
    }

    /// <summary>List row fill: solid for 1 or 3+ colors, horizontal WUBRG gradient for exactly two colors.</summary>
    public Brush StripBackground => DeckRowStripBrushes.GetDeckRowStripBackgroundBrush(Card);

    /// <summary>Commander rows hide the ± stepper (change via the row menu instead).</summary>
    public bool ShowQuantityControls =>
        !string.Equals(Entity.Section, DeckCardSections.Commander, StringComparison.OrdinalIgnoreCase);

    /// <summary>Multi-select mode for bulk deck edits.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnCardChanged(Card value) => OnPropertyChanged(nameof(StripBackground));

    public void NotifyDeckPriceBindingsChanged()
    {
        OnPropertyChanged(nameof(DeckUnitPriceDisplay));
        OnPropertyChanged(nameof(DeckLinePriceDisplay));
    }
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

    /// <summary>Overlap with current deck themes (main + commander).</summary>
    [ObservableProperty]
    public partial string SynergyHintText { get; set; } = "";

    public bool HasSynergyHint => !string.IsNullOrWhiteSpace(SynergyHintText);

    partial void OnSynergyHintTextChanged(string oldValue, string newValue) =>
        OnPropertyChanged(nameof(HasSynergyHint));
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

/// <summary>Chip wrapper for a <see cref="DeckNextStep"/> in the deck editor guidance strip.</summary>
public sealed class DeckNextStepItem(DeckNextStep step)
{
    public DeckNextStep Step { get; } = step;

    public string Title => Step.Title;

    /// <summary>Ready chip renders celebratory (accent) instead of actionable.</summary>
    public bool IsReady => Step.Kind == DeckNextStepKind.Ready;

    public string Glyph => Step.Kind switch
    {
        DeckNextStepKind.ChooseCommander => Solid.Crown,
        DeckNextStepKind.AddCards => Solid.Plus,
        DeckNextStepKind.AddLands => Solid.Mountain,
        DeckNextStepKind.RoleGap => Solid.WandMagicSparkles,
        DeckNextStepKind.TrimMain or DeckNextStepKind.TrimSideboard => Solid.Minus,
        DeckNextStepKind.Ready => Solid.Trophy,
        _ => Solid.CircleInfo
    };
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
