using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;
using System.Collections.Immutable;

namespace MTGFetchMAUI.Core.Layout;

public readonly record struct CardId(string Value);

/// <summary>
/// Immutable representation of a card's data for rendering.
/// </summary>
public record CardState(
    CardId Id,
    string Name,
    string SetCode,
    string Number,
    string ScryfallId,
    int Quantity,
    bool IsOnlineOnly,
    CardPriceData? PriceData = null,
    string CachedDisplayPrice = ""
)
{
    // Factory method to create state and pre-calculate display price
    public static CardState FromCard(Card card, int quantity = 0, CardPriceData? prices = null)
    {
        var state = new CardState(
            new CardId(card.UUID),
            card.Name,
            card.SetCode,
            card.Number,
            card.ScryfallId,
            quantity,
            card.IsOnlineOnly,
            prices
        );
        return state with { CachedDisplayPrice = state.GetDisplayPrice() };
    }

    // Helper to calculate display price if not provided
    public string GetDisplayPrice()
    {
        if (!string.IsNullOrEmpty(CachedDisplayPrice)) return CachedDisplayPrice;
        if (PriceData == null) return "";

        // Priority: TCGPlayer > Cardmarket > CardKingdom > ManaPool
        var paper = PriceData.Paper;
        VendorPrices[] vendors = [paper.TCGPlayer, paper.Cardmarket, paper.CardKingdom, paper.ManaPool];
        foreach (var v in vendors)
        {
            if (v.RetailNormal.Price > 0) return $"${v.RetailNormal.Price:F2}";
            if (v.RetailFoil.Price > 0) return $"${v.RetailFoil.Price:F2}";
        }
        return "";
    }
}

public record GridConfig(
    float MinCardWidth = 100f,
    float CardSpacing = 8f,
    float LabelHeight = 42f,
    float CardImageRatio = 1.3968f,
    int OffscreenBuffer = 40
);

public record Viewport(
    float Width,
    float Height,
    float ScrollY
);

public record GridState(
    ImmutableArray<CardState> Cards,
    GridConfig Config,
    Viewport Viewport
)
{
    public static GridState Empty => new(
        ImmutableArray<CardState>.Empty,
        new GridConfig(),
        new Viewport(0, 0, 0)
    );
}
