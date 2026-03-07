using AetherVault.Models;
using AetherVault.Services;
using SkiaSharp;

namespace AetherVault.Controls;

/// <summary>
/// Image quality levels for card grid display.
/// </summary>
public enum ImageQuality
{
    None,
    Small,
    Normal,
    Large
}

/// <summary>
/// Data model for a single card cell in the card grid.
/// Port of TGridCardData from CustomCardGrid.pas.
/// </summary>
public class GridCardData
{
    public string UUID { get; set; } = "";
    public string Name { get; set; } = "";
    public string SetCode { get; set; } = "";
    public string Number { get; set; } = "";
    public string ScryfallId { get; set; } = "";
    public char Side { get; set; } = 'a';
    public int Quantity { get; set; }
    public bool IsOnlineOnly { get; set; }


    // Image state
    public SKImage? Image { get; set; }
    public ImageQuality Quality { get; set; } = ImageQuality.None;
    public bool IsLoading { get; set; }
    public bool IsUpgrading { get; set; }
    public bool FirstVisible { get; set; }

    // Animation
    public float AnimationProgress { get; set; }

    // Cached UI elements
    public string TruncatedName { get; set; } = "";
    public float LastKnownCardWidth { get; set; }

    public string DisplaySetInfo => $"{SetCode} #{Number}";

    // Price
    public CardPriceData? PriceData { get; set; }
    public string CachedDisplayPrice { get; set; } = "";

    public bool HasPriceData => PriceData != null && !string.IsNullOrEmpty(CachedDisplayPrice);

    public bool NeedsImage => Image == null && !IsLoading && !string.IsNullOrEmpty(ScryfallId);

    public bool NeedsUpgrade(ImageQuality target) =>
        Image != null && Quality < target && !IsUpgrading;

    public string GetDisplayPrice()
    {
        if (PriceData == null) return "";
        if (CachedDisplayPrice.Length > 0) return CachedDisplayPrice;
        CachedDisplayPrice = PriceDisplayHelper.GetDisplayPrice(PriceData);
        return CachedDisplayPrice;
    }

    public static GridCardData FromCard(Card card, int quantity = 0) => new()
    {
        UUID = card.UUID,
        Name = card.Name,
        SetCode = card.SetCode,
        Number = card.Number,
        ScryfallId = card.ScryfallId,
        Side = card.Side,
        Quantity = quantity,
        IsOnlineOnly = card.IsOnlineOnly
    };
}
