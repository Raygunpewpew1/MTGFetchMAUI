namespace AetherVault.Core;

/// <summary>Default deck hub layout and tile sizing helpers.</summary>
public static class DeckHubLayoutDefaults
{
    public const string PreferenceKey = "DeckHubLayoutMode";

    /// <summary>List on phone, tiles on tablet and other idioms.</summary>
    public static DeckHubLayoutMode GetDefaultForDevice(DeviceIdiom idiom) =>
        idiom == DeviceIdiom.Phone ? DeckHubLayoutMode.List : DeckHubLayoutMode.Tiles;

    /// <summary>Tile height from grid cell width (aspect ~0.72), clamped for readability.</summary>
    public static double ComputeTileHeight(double cellWidth) =>
        cellWidth <= 0 ? 196 : Math.Clamp(cellWidth * 0.72, 160, 220);
}
