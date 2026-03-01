using MTGFetchMAUI.Core;

namespace MTGFetchMAUI;

/// <summary>
/// Centralized constants for the MTG Fetch application.
/// Port of MTGConstants.pas.
/// </summary>
public static class MTGConstants
{
    // ── Card Type Keywords ──────────────────────────────────────────

    public const string CardTypeArtifact = "Artifact";
    public const string CardTypeBattle = "Battle";
    public const string CardTypeConspiracy = "Conspiracy";
    public const string CardTypeCreature = "Creature";
    public const string CardTypeEnchantment = "Enchantment";
    public const string CardTypeInstant = "Instant";
    public const string CardTypeLand = "Land";
    public const string CardTypePhenomenon = "Phenomenon";
    public const string CardTypePlane = "Plane";
    public const string CardTypePlaneswalker = "Planeswalker";
    public const string CardTypeScheme = "Scheme";
    public const string CardTypeSorcery = "Sorcery";
    public const string CardTypeTribal = "Tribal";
    public const string CardTypeVanguard = "Vanguard";
    public const string CardTypeLegendary = "Legendary";
    public const string CardTypeBasicLand = "Basic Land";
    public const string CardTypeOther = "Other";

    // ── Basic Land Names ────────────────────────────────────────────

    public const string LandPlains = "Plains";
    public const string LandIsland = "Island";
    public const string LandSwamp = "Swamp";
    public const string LandMountain = "Mountain";
    public const string LandForest = "Forest";
    public const string LandWastes = "Wastes";

    // ── Deck Category Names ─────────────────────────────────────────

    public const string CategoryRamp = "ramp";
    public const string CategoryDraw = "draw";
    public const string CategoryRemoval = "removal";

    // ── Card Text Search Keywords ───────────────────────────────────

    public const string KeywordSearchLibrary = "search your library";
    public const string KeywordDraw = "draw";
    public const string KeywordCard = "card";
    public const string KeywordDestroy = "destroy";
    public const string KeywordExile = "exile";
    public const string KeywordDestroyAll = "destroy all";
    public const string KeywordExileAll = "exile all";
    public const string KeywordCounterTarget = "counter target";
    public const string KeywordCanBeCommander = "can be your commander";

    // ── Image Size Constants (Scryfall image versions) ──────────────

    public const string ImageSizeSmall = "small";
    public const string ImageSizeNormal = "normal";
    public const string ImageSizeLarge = "large";
    public const string ImageSizePng = "png";
    public const string ImageSizeArtCrop = "art_crop";
    public const string ImageSizeBorderCrop = "border_crop";

    // ── Scryfall API ────────────────────────────────────────────────

    public const string ScryfallImageUrlFormat =
        "https://api.scryfall.com/cards/{0}?format=image&version={1}";
    public const string ScryfallUserAgent = "MTGApp/2.0";

    // ── Database Configuration ──────────────────────────────────────

    public const string DbDriverSqlite = "SQLite";
    public const string DatabaseDownloadUrl = "https://github.com/Raygunpewpew1/MTGFetchMAUI/releases/latest/download/MTG_App_DB.zip";

    // ── File / Path Constants ──────────────────────────────────────

    // OLD:
    // public const string FileAllPrintings = "AllPrintings.sqlite";
    // public const string FileAllPrintingsZip = "AllPrintings.sqlite.zip";

    // NEW:
    public const string FileAllPrintings = "AllPrintings.sqlite";
    public const string FileAllPrintingsZip = "MTG_App_DB.zip"; // GitHub release asset 
    public const string FileCollectionDb = "MyCollection.sqlite";
    public const string FilePricesDb = "prices.sqlite";
    public const string FileSymbolCache = "SymbolCache.json";
    public const string FilePricesTempZip = "AllPricesToday_temp.zip";
    public const string FilePricesTempSqlite = "AllPricesToday_temp.sqlite";
    public const string AppRootFolder = "MTGApp";

    // How many days of daily price history to keep in card_price_history.
    // Without trimming, the table grows ~170 MB/month. 90 days ≈ 500 MB ceiling.
    public const int PriceHistoryRetentionDays = 90;

    // ── Legality Display Strings ────────────────────────────────────

    public static readonly string[] LegalityDisplay = ["Legal", "Banned", "Restricted", "Not Legal"];

    public static string GetLegalityDisplay(LegalityStatus status) =>
        LegalityDisplay[(int)status];
}
