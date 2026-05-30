using AetherVault.Core;

namespace AetherVault.Models;

/// <summary>Lightweight row for the card-detail other printings list.</summary>
public sealed class OtherPrintingSummary
{
    public string Uuid { get; init; } = "";
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string Number { get; init; } = "";
    public CardRarity Rarity { get; init; } = CardRarity.Common;
    public string SetReleaseDate { get; init; } = "";
    public bool IsCurrent { get; init; }

    public string DisplayLabel => $"{SetCode.ToUpperInvariant()} #{Number}";
}
