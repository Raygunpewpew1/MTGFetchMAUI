namespace MTGFetchMAUI.Core;

/// <summary>Scryfall CDN image size options.</summary>
public enum ScryfallSize
{
    Small,
    Normal,
    Large,
    Png,
    ArtCrop,
    BorderCrop
}

/// <summary>Scryfall card face (front or back).</summary>
public enum ScryfallFace
{
    Front,
    Back
}

/// <summary>
/// Helper for constructing Scryfall CDN image URLs.
/// Port of ScryfallCDN.pas.
/// </summary>
public static class ScryfallCDN
{
    private static readonly string[] SizeStrings =
        ["small", "normal", "large", "png", "art_crop", "border_crop"];

    private static readonly string[] FaceStrings = ["front", "back"];

    public static string GetImageUrl(
        string uuid,
        ScryfallSize size = ScryfallSize.Small,
        ScryfallFace face = ScryfallFace.Front)
    {
        if (uuid.Length < 2) return "";

        string sizeStr = SizeStrings[(int)size];
        string faceStr = FaceStrings[(int)face];
        string ext = size == ScryfallSize.Png ? "png" : "jpg";

        return $"https://cards.scryfall.io/{sizeStr}/{faceStr}/{uuid[0]}/{uuid[1]}/{uuid}.{ext}";
    }

    public static string GetImageUrlFromStrings(string uuid, string sizeStr, string faceStr)
    {
        var size = sizeStr?.ToLowerInvariant() switch
        {
            "small" => ScryfallSize.Small,
            "normal" => ScryfallSize.Normal,
            "large" => ScryfallSize.Large,
            "png" => ScryfallSize.Png,
            "art_crop" => ScryfallSize.ArtCrop,
            "border_crop" => ScryfallSize.BorderCrop,
            _ => ScryfallSize.Normal
        };

        var face = faceStr?.ToLowerInvariant() == "back"
            ? ScryfallFace.Back
            : ScryfallFace.Front;

        return GetImageUrl(uuid, size, face);
    }
}
