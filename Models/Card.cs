using AetherVault.Core;

namespace AetherVault.Models;

/// <summary>
/// Main card model containing all card data.
/// Port of TCard from MTGCore.pas.
/// </summary>
public class Card
{
    // ── Identifiers ─────────────────────────────────────────────────
    public string Uuid { get; set; } = "";
    public string ScryfallId { get; set; } = "";
    /// <summary>Scryfall oracle id — shared by all printings of the same card.</summary>
    public string ScryfallOracleId { get; set; } = "";
    public string BackScryfallId { get; set; } = "";

    // ── Basic Info ──────────────────────────────────────────────────
    public string Name { get; set; } = "";
    public string PrintedName { get; set; } = "";
    public string ManaCost { get; set; } = "";
    public double Cmc { get; set; }
    /// <summary>Mana value for deck curve and CMC sorting. Uses <see cref="Cmc"/> (MTGJSON <c>manaValue</c>) when &gt; 0; otherwise <see cref="FaceManaValue"/> (covers true 0-CMC spells and odd exports where only the face column is set).</summary>
    public double EffectiveManaValue => Cmc > 0 ? Cmc : FaceManaValue;
    public string CardType { get; set; } = "";
    public string Text { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public string FlavorText { get; set; } = "";
    public CardRarity Rarity { get; set; } = CardRarity.Common;

    // ── Purchase Urls ────────────────────────────────────────────────────
    public string CardKingdom { get; set; } = "";
    public string CardKingdomFoil { get; set; } = "";
    public string CardKingdomEtched { get; set; } = "";
    public string Cardmarket { get; set; } = "";
    public string Tcgplayer { get; set; } = "";
    public string TcgplayerEtched { get; set; } = "";


    // ── Set Info ────────────────────────────────────────────────────
    public string SetName { get; set; } = "";
    public string SetCode { get; set; } = "";
    /// <summary>Parent set release date (from MTGJSON <c>sets.releaseDate</c>).</summary>
    public string SetReleaseDate { get; set; } = "";
    public string Number { get; set; } = "";
    public string KeyruneCode { get; set; } = "";

    // ── Card Attributes ─────────────────────────────────────────────
    public string Colors { get; set; } = "";
    public string Power { get; set; } = "";
    public string Toughness { get; set; } = "";
    public string Loyalty { get; set; } = "";
    public string Defense { get; set; } = "";
    public CardLayout Layout { get; set; } = CardLayout.Normal;
    public string Artist { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string ProducedMana { get; set; } = "";

    // ── Type Information ────────────────────────────────────────────
    public string Subtypes { get; set; } = "";
    public string Supertypes { get; set; } = "";

    // ── Flags ───────────────────────────────────────────────────────
    public bool IsFoil { get; set; }
    public bool IsNonFoil { get; set; }
    public bool IsPromo { get; set; }
    public bool IsReprint { get; set; }
    public bool IsAlternative { get; set; }
    public bool IsReserved { get; set; }
    public bool HasAlternativeDeckLimit { get; set; }
    public bool HasContentWarning { get; set; }
    public bool IsFullArt { get; set; }
    public bool IsFunny { get; set; }
    public bool IsGameChanger { get; set; }
    public bool IsOnlineOnly { get; set; }
    public bool IsOversized { get; set; }
    public bool IsRebalanced { get; set; }
    public bool IsStorySpotlight { get; set; }
    public bool IsTextless { get; set; }
    public bool IsTimeshifted { get; set; }

    // ── Multi-face cards ────────────────────────────────────────────
    public string OtherFaceIds { get; set; } = "";
    public char Side { get; set; } = 'a';
    public string CardParts { get; set; } = "";

    // ── EDH/Commander Data ──────────────────────────────────────────
    public int EdhRecRank { get; set; }
    public double EdhRecSaltiness { get; set; }

    // ── Visual/Frame Properties ─────────────────────────────────────
    public string Watermark { get; set; } = "";
    public string FrameVersion { get; set; } = "";
    public string FrameEffects { get; set; } = "";
    public string SecurityStamp { get; set; } = "";

    // ── Additional Data ─────────────────────────────────────────────
    public CardLegalities Legalities { get; set; } = new();
    public List<CardRuling> Rulings { get; set; } = [];
    public string ImageUrl { get; set; } = "";
    /// <summary>ID to use for image cache/CDN (ScryfallId when set, else UUID).</summary>
    public string ImageId => string.IsNullOrEmpty(ScryfallId) ? Uuid : ScryfallId;
    public string[] RelatedCards { get; set; } = [];

    public string Finishes { get; set; } = "";
    public string Availability { get; set; } = "";
    public string BorderColor { get; set; } = "";
    public string ColorIdentity { get; set; } = "";
    public string ColorIndicator { get; set; } = "";
    public string FaceName { get; set; } = "";
    public string Language { get; set; } = "";
    public string AsciiName { get; set; } = "";
    public string AttractionLights { get; set; } = "";
    public string FaceFlavorName { get; set; } = "";
    public double FaceManaValue { get; set; }
    public string FacePrintedName { get; set; } = "";
    public string FlavorName { get; set; } = "";
    public string Hand { get; set; } = "";
    public string Life { get; set; } = "";
    public string PromoTypes { get; set; } = "";
    public string Signature { get; set; } = "";
    public string LeadershipSkills { get; set; } = "";
    public string SourceProducts { get; set; } = "";

    // ── Type Checking ───────────────────────────────────────────────

    private bool HasCardType(string typeName) =>
        CardType.Contains(typeName, StringComparison.OrdinalIgnoreCase);

    public bool IsCreature => HasCardType(MtgConstants.CardTypeCreature);
    public bool IsLand => HasCardType(MtgConstants.CardTypeLand);
    public bool IsBasicLand => HasCardType(MtgConstants.CardTypeBasicLand);
    public bool IsArtifact => HasCardType(MtgConstants.CardTypeArtifact);
    public bool IsEnchantment => HasCardType(MtgConstants.CardTypeEnchantment);
    public bool IsInstant => HasCardType(MtgConstants.CardTypeInstant);
    public bool IsSorcery => HasCardType(MtgConstants.CardTypeSorcery);
    public bool IsPlaneswalker => HasCardType(MtgConstants.CardTypePlaneswalker);
    public bool IsBattle => HasCardType(MtgConstants.CardTypeBattle);
    public bool IsLegendary => HasCardType(MtgConstants.CardTypeLegendary);

    // ── Card Analysis ───────────────────────────────────────────────

    public bool HasColor(string color) =>
        Colors.Contains(color, StringComparison.OrdinalIgnoreCase);

    public ColorIdentity GetColorIdentity() =>
        Core.ColorIdentity.FromString(Colors);

    public string GetMainCardType()
    {
        string separator = CardType.Contains('\u2014') ? "\u2014" : "-";
        var parts = CardType.Split(separator);
        return parts[0].Trim();
    }

    public string GetCardTypeCategory()
    {
        if (IsCreature) return MtgConstants.CardTypeCreature;
        if (IsLand) return MtgConstants.CardTypeLand;
        if (IsPlaneswalker) return MtgConstants.CardTypePlaneswalker;
        if (IsBattle) return MtgConstants.CardTypeBattle;
        if (IsArtifact) return MtgConstants.CardTypeArtifact;
        if (IsEnchantment) return MtgConstants.CardTypeEnchantment;
        if (IsInstant) return MtgConstants.CardTypeInstant;
        if (IsSorcery) return MtgConstants.CardTypeSorcery;
        return MtgConstants.CardTypeOther;
    }

    public bool HasKeyword(string keyword) =>
        Keywords.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    public string[] GetKeywordsArray() => SplitAndTrimCsv(Keywords);
    public string[] GetSubtypesArray() => SplitAndTrimCsv(Subtypes);
    public string[] GetSupertypesArray() => SplitAndTrimCsv(Supertypes);

    public bool ProducesManaColor(string manaColor) =>
        ProducedMana.Contains(manaColor, StringComparison.OrdinalIgnoreCase);

    // ── Multi-face Support ──────────────────────────────────────────

    public bool IsDoubleFaced => Layout.IsDoubleFaced();
    public bool IsTransformCard => Layout.IsTransform();
    public bool IsMdfc => Layout.IsMdfc();

    public string GetFirstOtherFaceId()
    {
        if (string.IsNullOrEmpty(OtherFaceIds)) return "";
        var ids = OtherFaceIds.Split(',');
        return ids.Length > 0 ? ids[0].Trim() : "";
    }

    public int GetOtherFaceCount()
    {
        if (string.IsNullOrEmpty(OtherFaceIds)) return 0;
        return OtherFaceIds.Split(',').Length;
    }

    // ── Display ─────────────────────────────────────────────────────

    public string GetDisplayText()
    {
        var result = Name;
        if (!string.IsNullOrEmpty(ManaCost))
            result += " " + ManaCost;
        if (!string.IsNullOrEmpty(CardType))
            result += Environment.NewLine + CardType;
        if (!string.IsNullOrEmpty(Text))
            result += Environment.NewLine + Text;
        if (!string.IsNullOrEmpty(Power) && !string.IsNullOrEmpty(Toughness))
            result += Environment.NewLine + Power + "/" + Toughness;
        else if (!string.IsNullOrEmpty(Loyalty))
            result += Environment.NewLine + "Loyalty: " + Loyalty;
        else if (!string.IsNullOrEmpty(Defense))
            result += Environment.NewLine + "Defense: " + Defense;
        return result;
    }

    public string GetSetAndNumber() => $"{SetCode} #{Number}";

    public string GetPowerToughness() =>
        !string.IsNullOrEmpty(Power) && !string.IsNullOrEmpty(Toughness)
            ? $"{Power}/{Toughness}"
            : "";

    // ── Helpers ─────────────────────────────────────────────────────

    private static string[] SplitAndTrimCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return [];
        return value.Split(',').Select(s => s.Trim()).ToArray();
    }
}
