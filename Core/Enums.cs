using System.Diagnostics;

namespace MTGFetchMAUI.Core;

/// <summary>Legality status for a card in a format.</summary>
public enum LegalityStatus
{
    Legal,
    Banned,
    Restricted,
    NotLegal
}

/// <summary>Card rarity levels.</summary>
public enum CardRarity
{
    Common,
    Uncommon,
    Rare,
    Mythic,
    Special,
    Bonus
}

/// <summary>Different card layouts.</summary>
public enum CardLayout
{
    Normal,
    Split,
    Flip,
    Transform,
    ModalDFC,
    Meld,
    Leveler,
    Saga,
    Adventure,
    Planar,
    Scheme,
    Vanguard,
    Token,
    DoubleFacedToken,
    Emblem,
    Augment,
    Host,
    ArtSeries,
    ReversibleCard
}

/// <summary>MTG deck/play formats.</summary>
public enum DeckFormat
{
    Standard,
    Modern,
    Pioneer,
    Legacy,
    Vintage,
    Commander,
    Brawl,
    Pauper,
    PauperCommander,
    Oathbreaker,
    Historic,
    Alchemy,
    Timeless,
    Gladiator,
    Premodern,
    Oldschool,
    Duel,
    Future,
    Penny,
    Predh,
    StandardBrawl
}

/// <summary>Common commander deck archetypes.</summary>
public enum CommanderArchetype
{
    Unknown,
    Aggro,
    Midrange,
    Control,
    Combo,
    Tribal,
    Voltron,
    Spellslinger,
    Tokens,
    Graveyard,
    Landfall,
    Aristocrats,
    Stax,
    GroupHug,
    SuperFriends
}

/// <summary>Individual Magic colors.</summary>
public enum MtgColor
{
    White,
    Blue,
    Black,
    Red,
    Green
}

/// <summary>Sort modes for the user's collection view.</summary>
public enum CollectionSortMode
{
    Manual = 0,
    Name = 1,
    CMC = 2,
    Rarity = 3,
    Color = 4,
}

public static class EnumExtensions
{
    static EnumExtensions()
    {
        Debug.Assert(LegalityStrings.Length == Enum.GetValues<LegalityStatus>().Length,
            "LegalityStrings is out of sync with LegalityStatus enum.");
        Debug.Assert(RarityStrings.Length == Enum.GetValues<CardRarity>().Length,
            "RarityStrings is out of sync with CardRarity enum.");
        Debug.Assert(LayoutStrings.Length == Enum.GetValues<CardLayout>().Length,
            "LayoutStrings is out of sync with CardLayout enum.");
        Debug.Assert(FormatDbFields.Length == Enum.GetValues<DeckFormat>().Length,
            "FormatDbFields is out of sync with DeckFormat enum.");
        Debug.Assert(FormatDisplayNames.Length == Enum.GetValues<DeckFormat>().Length,
            "FormatDisplayNames is out of sync with DeckFormat enum.");
        Debug.Assert(ColorChars.Length == Enum.GetValues<MtgColor>().Length,
            "ColorChars is out of sync with MtgColor enum.");
        Debug.Assert(ColorNames.Length == Enum.GetValues<MtgColor>().Length,
            "ColorNames is out of sync with MtgColor enum.");
        Debug.Assert(ArchetypeNames.Length == Enum.GetValues<CommanderArchetype>().Length,
            "ArchetypeNames is out of sync with CommanderArchetype enum.");
    }

    // ── LegalityStatus ──────────────────────────────────────────────

    private static readonly string[] LegalityStrings =
        ["legal", "banned", "restricted", "not_legal"];

    public static string ToDbString(this LegalityStatus status) =>
        LegalityStrings[(int)status];

    public static LegalityStatus ParseLegalityStatus(string? value)
    {
        if (string.IsNullOrEmpty(value)) return LegalityStatus.NotLegal;
        return char.ToLower(value[0]) switch
        {
            'l' => LegalityStatus.Legal,
            'b' => LegalityStatus.Banned,
            'r' => LegalityStatus.Restricted,
            _ => LegalityStatus.NotLegal
        };
    }

    // ── CardRarity ──────────────────────────────────────────────────

    private static readonly string[] RarityStrings =
        ["common", "uncommon", "rare", "mythic", "special", "bonus"];

    public static string ToDbString(this CardRarity rarity) =>
        RarityStrings[(int)rarity];

    public static CardRarity ParseCardRarity(string? value)
    {
        if (string.IsNullOrEmpty(value)) return CardRarity.Common;
        if (value.Equals("mythic rare", StringComparison.OrdinalIgnoreCase))
            return CardRarity.Mythic;
        return char.ToLower(value[0]) switch
        {
            'c' => CardRarity.Common,
            'u' => CardRarity.Uncommon,
            'r' => CardRarity.Rare,
            'm' => CardRarity.Mythic,
            's' => CardRarity.Special,
            'b' => CardRarity.Bonus,
            _ => CardRarity.Common
        };
    }

    // ── CardLayout ──────────────────────────────────────────────────

    private static readonly string[] LayoutStrings =
    [
        "normal", "split", "flip", "transform", "modal_dfc", "meld",
        "leveler", "saga", "adventure", "planar", "scheme", "vanguard",
        "token", "double_faced_token", "emblem", "augment", "host",
        "art_series", "reversible_card"
    ];

    public static string ToDbString(this CardLayout layout) =>
        LayoutStrings[(int)layout];

    public static bool IsDoubleFaced(this CardLayout layout) =>
        layout is CardLayout.Transform or CardLayout.ModalDFC or
                  CardLayout.Meld or CardLayout.DoubleFacedToken or
                  CardLayout.ReversibleCard;

    public static bool IsTransform(this CardLayout layout) =>
        layout == CardLayout.Transform;

    public static bool IsMDFC(this CardLayout layout) =>
        layout == CardLayout.ModalDFC;

    public static CardLayout ParseCardLayout(string? value)
    {
        if (string.IsNullOrEmpty(value)) return CardLayout.Normal;
        for (int i = 0; i < LayoutStrings.Length; i++)
        {
            if (LayoutStrings[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                return (CardLayout)i;
        }
        return CardLayout.Normal;
    }

    // ── DeckFormat ──────────────────────────────────────────────────

    private static readonly string[] FormatDbFields =
    [
        "standard", "modern", "pioneer", "legacy", "vintage", "commander",
        "brawl", "pauper", "paupercommander", "oathbreaker", "historic",
        "alchemy", "timeless", "gladiator", "premodern", "oldschool",
        "duel", "future", "penny", "predh", "standardbrawl"
    ];

    private static readonly string[] FormatDisplayNames =
    [
        "Standard", "Modern", "Pioneer", "Legacy", "Vintage", "Commander",
        "Brawl", "Pauper", "Pauper Commander", "Oathbreaker", "Historic",
        "Alchemy", "Timeless", "Gladiator", "Premodern", "Oldschool",
        "Duel Commander", "Future Standard", "Penny Dreadful", "Pre-DH",
        "Standard Brawl"
    ];

    public static string ToDbField(this DeckFormat format) =>
        FormatDbFields[(int)format];

    public static string ToDisplayName(this DeckFormat format) =>
        FormatDisplayNames[(int)format];

    public static DeckFormat ParseDeckFormat(string? value)
    {
        if (string.IsNullOrEmpty(value)) return DeckFormat.Standard;
        for (int i = 0; i < FormatDbFields.Length; i++)
        {
            if (FormatDbFields[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                return (DeckFormat)i;
        }
        return DeckFormat.Standard;
    }

    // ── MtgColor ────────────────────────────────────────────────────

    private static readonly char[] ColorChars = ['W', 'U', 'B', 'R', 'G'];
    private static readonly string[] ColorNames = ["White", "Blue", "Black", "Red", "Green"];

    public static char ToChar(this MtgColor color) => ColorChars[(int)color];
    public static string ToDisplayName(this MtgColor color) => ColorNames[(int)color];

    public static MtgColor ParseMtgColor(char c)
    {
        c = char.ToUpper(c);
        for (int i = 0; i < ColorChars.Length; i++)
        {
            if (ColorChars[i] == c)
                return (MtgColor)i;
        }
        return MtgColor.White;
    }

    // ── CommanderArchetype ──────────────────────────────────────────

    private static readonly string[] ArchetypeNames =
    [
        "Unknown", "Aggro", "Midrange", "Control", "Combo", "Tribal",
        "Voltron", "Spellslinger", "Tokens", "Graveyard", "Landfall",
        "Aristocrats", "Stax", "Group Hug", "Superfriends"
    ];

    public static string ToDisplayName(this CommanderArchetype archetype) =>
        ArchetypeNames[(int)archetype];
}
