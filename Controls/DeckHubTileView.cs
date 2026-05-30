namespace AetherVault.Controls;

using AetherVault.Core;

/// <summary>
/// Deck hub art tile whose height tracks cell width (aspect ratio) instead of a fixed dp value.
/// </summary>
public class DeckHubTileView : Border
{
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
            return;

        var target = DeckHubLayoutDefaults.ComputeTileHeight(width);
        if (Math.Abs(HeightRequest - target) > 0.5)
            HeightRequest = target;
    }
}
