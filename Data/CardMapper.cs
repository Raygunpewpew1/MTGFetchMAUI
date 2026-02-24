using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Models;
using System.Text.Json;

namespace MTGFetchMAUI.Data;

/// <summary>
/// Maps SQLite data reader rows to Card objects.
/// Replaces TCardFieldMap + PopulateCardFromFields from CardRepository.pas.
/// Ordinals are resolved once per query (CardOrdinals) then reused per row (MapCard).
/// </summary>
public static class CardMapper
{
    // ── Ordinal cache — resolve once before the read loop ──────────

    public sealed class CardOrdinals
    {
        public readonly int UUID, ScryfallId, Name, AsciiName, PrintedName;
        public readonly int ManaCost, CardType, Text, OriginalText, FlavorText, FlavorName;
        public readonly int Colors, ColorIdentity, ColorIndicator, Keywords, ProducedMana;
        public readonly int Subtypes, Supertypes, OtherFaceIds, FrameEffects, PromoTypes;
        public readonly int Finishes, Availability, AttractionLights, CardParts;
        public readonly int Power, Toughness, Loyalty, Defense, Hand, Life;
        public readonly int FaceName, FaceFlavorName, FacePrintedName, Watermark;
        public readonly int FrameVersion, SecurityStamp, Signature, Artist, BorderColor;
        public readonly int Language, LeadershipSkills, SourceProducts;
        public readonly int SetCode, SetName, Number, KeyruneCode;
        public readonly int CMC, FaceManaValue, EDHRecRank, EDHRecSaltiness;
        public readonly int HasFoil, HasNonFoil;
        public readonly int IsPromo, IsReprint, IsAlternative, IsReserved, IsFullArt;
        public readonly int IsFunny, IsOnlineOnly, IsOversized, IsRebalanced;
        public readonly int IsStorySpotlight, IsTextless, IsTimeshifted, IsGameChanger;
        public readonly int HasAlternativeDeckLimit, HasContentWarning;
        public readonly int Rarity, Layout, Side;
        public readonly int CardKingdom, CardKingdomFoil, CardKingdomEtched;
        public readonly int Cardmarket, Tcgplayer, TcgplayerEtched;
        public readonly Dictionary<DeckFormat, int> Legalities;

        public CardOrdinals(SqliteDataReader reader)
        {
            UUID = Ord(reader, "uuid");
            ScryfallId = Ord(reader, "scryfallId");
            Name = Ord(reader, "name");
            AsciiName = Ord(reader, "asciiName");
            PrintedName = Ord(reader, "printedName");
            ManaCost = Ord(reader, "manaCost");
            CardType = Ord(reader, "type");
            Text = Ord(reader, "text");
            OriginalText = Ord(reader, "originalText");
            FlavorText = Ord(reader, "flavorText");
            FlavorName = Ord(reader, "flavorName");
            Colors = Ord(reader, "colors");
            ColorIdentity = Ord(reader, "colorIdentity");
            ColorIndicator = Ord(reader, "colorIndicator");
            Keywords = Ord(reader, "keywords");
            ProducedMana = Ord(reader, "producedMana");
            Subtypes = Ord(reader, "subtypes");
            Supertypes = Ord(reader, "supertypes");
            OtherFaceIds = Ord(reader, "otherFaceIds");
            FrameEffects = Ord(reader, "frameEffects");
            PromoTypes = Ord(reader, "promoTypes");
            Finishes = Ord(reader, "finishes");
            Availability = Ord(reader, "availability");
            AttractionLights = Ord(reader, "attractionLights");
            CardParts = Ord(reader, "cardParts");
            Power = Ord(reader, "power");
            Toughness = Ord(reader, "toughness");
            Loyalty = Ord(reader, "loyalty");
            Defense = Ord(reader, "defense");
            Hand = Ord(reader, "hand");
            Life = Ord(reader, "life");
            FaceName = Ord(reader, "faceName");
            FaceFlavorName = Ord(reader, "faceFlavorName");
            FacePrintedName = Ord(reader, "facePrintedName");
            Watermark = Ord(reader, "watermark");
            FrameVersion = Ord(reader, "frameVersion");
            SecurityStamp = Ord(reader, "securityStamp");
            Signature = Ord(reader, "signature");
            Artist = Ord(reader, "artist");
            BorderColor = Ord(reader, "borderColor");
            Language = Ord(reader, "language");
            LeadershipSkills = Ord(reader, "leadershipSkills");
            SourceProducts = Ord(reader, "sourceProducts");
            SetCode = Ord(reader, "setCode");
            SetName = Ord(reader, "setName");
            Number = Ord(reader, "number");
            KeyruneCode = Ord(reader, "keyruneCode");
            CMC = Ord(reader, "manaValue");
            FaceManaValue = Ord(reader, "faceManaValue");
            EDHRecRank = Ord(reader, "edhrecRank");
            EDHRecSaltiness = Ord(reader, "edhrecSaltiness");
            HasFoil = Ord(reader, "hasFoil");
            HasNonFoil = Ord(reader, "hasNonFoil");
            IsPromo = Ord(reader, "isPromo");
            IsReprint = Ord(reader, "isReprint");
            IsAlternative = Ord(reader, "isAlternative");
            IsReserved = Ord(reader, "isReserved");
            IsFullArt = Ord(reader, "isFullArt");
            IsFunny = Ord(reader, "isFunny");
            IsOnlineOnly = Ord(reader, "isOnlineOnly");
            IsOversized = Ord(reader, "isOversized");
            IsRebalanced = Ord(reader, "isRebalanced");
            IsStorySpotlight = Ord(reader, "isStorySpotlight");
            IsTextless = Ord(reader, "isTextless");
            IsTimeshifted = Ord(reader, "isTimeshifted");
            IsGameChanger = Ord(reader, "isGameChanger");
            HasAlternativeDeckLimit = Ord(reader, "hasAlternativeDeckLimit");
            HasContentWarning = Ord(reader, "hasContentWarning");
            Rarity = Ord(reader, "rarity");
            Layout = Ord(reader, "layout");
            Side = Ord(reader, "side");
            CardKingdom = Ord(reader, "cardKingdom");
            CardKingdomFoil = Ord(reader, "cardKingdomFoil");
            CardKingdomEtched = Ord(reader, "cardKingdomEtched");
            Cardmarket = Ord(reader, "cardmarket");
            Tcgplayer = Ord(reader, "tcgplayer");
            TcgplayerEtched = Ord(reader, "tcgplayerEtched");

            Legalities = [];
            foreach (DeckFormat fmt in Enum.GetValues<DeckFormat>())
                Legalities[fmt] = Ord(reader, fmt.ToDbField());
        }

        private static int Ord(SqliteDataReader reader, string col)
        {
            try { return reader.GetOrdinal(col); }
            catch (ArgumentOutOfRangeException) { return -1; }
        }
    }

    // ── Mapping ─────────────────────────────────────────────────────

    /// <summary>
    /// Populates a Card from the current reader row using pre-resolved ordinals.
    /// Call new CardOrdinals(reader) once before your read loop, then pass it here.
    /// </summary>
    public static Card MapCard(SqliteDataReader reader, CardOrdinals o)
    {
        var card = new Card();

        // ── Identifiers & Basic Info ────────────────────────────────
        card.UUID = Str(reader, o.UUID);
        card.ScryfallId = Str(reader, o.ScryfallId);
        card.Name = Str(reader, o.Name);
        card.AsciiName = Str(reader, o.AsciiName);
        card.PrintedName = Str(reader, o.PrintedName);
        card.ManaCost = Str(reader, o.ManaCost);
        card.CardType = Str(reader, o.CardType);
        card.Text = Str(reader, o.Text);
        card.OriginalText = Str(reader, o.OriginalText);
        card.FlavorText = Str(reader, o.FlavorText);
        card.FlavorName = Str(reader, o.FlavorName);

        // ── JSON array fields → CSV ────────────────────────────────
        card.Colors = CleanJsonArray(Str(reader, o.Colors));
        card.ColorIdentity = CleanJsonArray(Str(reader, o.ColorIdentity));
        card.ColorIndicator = CleanJsonArray(Str(reader, o.ColorIndicator));
        card.Keywords = CleanJsonArray(Str(reader, o.Keywords));
        card.ProducedMana = CleanJsonArray(Str(reader, o.ProducedMana));
        card.Subtypes = CleanJsonArray(Str(reader, o.Subtypes));
        card.Supertypes = CleanJsonArray(Str(reader, o.Supertypes));
        card.OtherFaceIds = CleanJsonArray(Str(reader, o.OtherFaceIds));
        card.FrameEffects = CleanJsonArray(Str(reader, o.FrameEffects));
        card.PromoTypes = CleanJsonArray(Str(reader, o.PromoTypes));
        card.Finishes = CleanJsonArray(Str(reader, o.Finishes));
        card.Availability = CleanJsonArray(Str(reader, o.Availability));
        card.AttractionLights = CleanJsonArray(Str(reader, o.AttractionLights));

        // CardParts kept as raw JSON (card names can contain commas)
        card.CardParts = Str(reader, o.CardParts);

        // ── Plain string fields ────────────────────────────────────
        card.Power = Str(reader, o.Power);
        card.Toughness = Str(reader, o.Toughness);
        card.Loyalty = Str(reader, o.Loyalty);
        card.Defense = Str(reader, o.Defense);
        card.Hand = Str(reader, o.Hand);
        card.Life = Str(reader, o.Life);
        card.FaceName = Str(reader, o.FaceName);
        card.FaceFlavorName = Str(reader, o.FaceFlavorName);
        card.FacePrintedName = Str(reader, o.FacePrintedName);
        card.Watermark = Str(reader, o.Watermark);
        card.FrameVersion = Str(reader, o.FrameVersion);
        card.SecurityStamp = Str(reader, o.SecurityStamp);
        card.Signature = Str(reader, o.Signature);
        card.Artist = Str(reader, o.Artist);
        card.BorderColor = Str(reader, o.BorderColor);
        card.Language = Str(reader, o.Language);
        card.LeadershipSkills = Str(reader, o.LeadershipSkills);
        card.SourceProducts = Str(reader, o.SourceProducts);

        // ── Set Info ───────────────────────────────────────────────
        card.SetCode = Str(reader, o.SetCode);
        card.SetName = Str(reader, o.SetName);
        card.Number = Str(reader, o.Number);
        var keyrune = Str(reader, o.KeyruneCode);
        card.KeyruneCode = string.IsNullOrEmpty(keyrune) ? card.SetCode : keyrune;

        // ── Numeric values ─────────────────────────────────────────
        card.CMC = Dbl(reader, o.CMC);
        card.FaceManaValue = Dbl(reader, o.FaceManaValue);
        card.EDHRecRank = Int(reader, o.EDHRecRank);
        card.EDHRecSaltiness = Dbl(reader, o.EDHRecSaltiness);

        // ── Boolean flags ──────────────────────────────────────────
        // hasFoil/hasNonFoil removed in MTGJSON v5.3+, derive from finishes if absent
        card.IsFoil = o.HasFoil >= 0
            ? Int(reader, o.HasFoil) == 1
            : card.Finishes.Contains("foil", StringComparison.OrdinalIgnoreCase);

        card.IsNonFoil = o.HasNonFoil >= 0
            ? Int(reader, o.HasNonFoil) == 1
            : card.Finishes.Contains("nonfoil", StringComparison.OrdinalIgnoreCase);

        card.IsPromo = Int(reader, o.IsPromo) == 1;
        card.IsReprint = Int(reader, o.IsReprint) == 1;
        card.IsAlternative = Int(reader, o.IsAlternative) == 1;
        card.IsReserved = Int(reader, o.IsReserved) == 1;
        card.IsFullArt = Int(reader, o.IsFullArt) == 1;
        card.IsFunny = Int(reader, o.IsFunny) == 1;
        card.IsOnlineOnly = Int(reader, o.IsOnlineOnly) == 1;
        card.IsOversized = Int(reader, o.IsOversized) == 1;
        card.IsRebalanced = Int(reader, o.IsRebalanced) == 1;
        card.IsStorySpotlight = Int(reader, o.IsStorySpotlight) == 1;
        card.IsTextless = Int(reader, o.IsTextless) == 1;
        card.IsTimeshifted = Int(reader, o.IsTimeshifted) == 1;
        card.IsGameChanger = Int(reader, o.IsGameChanger) == 1;
        card.HasAlternativeDeckLimit = Int(reader, o.HasAlternativeDeckLimit) == 1;
        card.HasContentWarning = Int(reader, o.HasContentWarning) == 1;

        // ── Enums ──────────────────────────────────────────────────
        card.Rarity = EnumExtensions.ParseCardRarity(Str(reader, o.Rarity));
        card.Layout = EnumExtensions.ParseCardLayout(Str(reader, o.Layout));

        var sideStr = Str(reader, o.Side);
        card.Side = string.IsNullOrEmpty(sideStr) ? 'a' : sideStr[0];

        // ── Legalities ─────────────────────────────────────────────
        card.Legalities = new CardLegalities();
        foreach (DeckFormat fmt in Enum.GetValues<DeckFormat>())
        {
            var legalityStr = Str(reader, o.Legalities[fmt]);
            if (!string.IsNullOrEmpty(legalityStr))
                card.Legalities[fmt] = EnumExtensions.ParseLegalityStatus(legalityStr);
        }

        // ── Purchase URLs ──────────────────────────────────────────
        card.cardKingdom = Str(reader, o.CardKingdom);
        card.cardKingdomFoil = Str(reader, o.CardKingdomFoil);
        card.cardKingdomEtched = Str(reader, o.CardKingdomEtched);
        card.cardmarket = Str(reader, o.Cardmarket);
        card.tcgplayer = Str(reader, o.Tcgplayer);
        card.tcgplayerEtched = Str(reader, o.TcgplayerEtched);

        return card;
    }

    // ── Safe field accessors (ordinal-based, no string lookup) ──────

    private static string Str(SqliteDataReader r, int ord) =>
        ord < 0 || r.IsDBNull(ord) ? "" : r.GetString(ord);

    private static double Dbl(SqliteDataReader r, int ord)
    {
        if (ord < 0 || r.IsDBNull(ord)) return 0.0;
        try { return r.GetDouble(ord); }
        catch
        {
            // Handle TEXT→REAL mismatch from MTGJSON schema changes
            return double.TryParse(r.GetString(ord), out var v) ? v : 0.0;
        }
    }

    private static int Int(SqliteDataReader r, int ord)
    {
        if (ord < 0 || r.IsDBNull(ord)) return 0;
        try { return r.GetInt32(ord); }
        catch
        {
            var s = r.GetString(ord);
            if (int.TryParse(s, out var i)) return i;
            if (double.TryParse(s, out var d)) return (int)Math.Round(d);
            return 0;
        }
    }

    // ── JSON array helpers ──────────────────────────────────────────

    /// <summary>
    /// Parses a JSON array like ["R","B"] into comma-separated "R,B".
    /// Returns empty string for null/empty/"[]".
    /// </summary>
    public static string CleanJsonArray(string? jsonString)
    {
        if (string.IsNullOrEmpty(jsonString) || jsonString == "[]")
            return "";

        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return jsonString;

            var values = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
                values.Add(element.GetString() ?? "");

            return string.Join(",", values);
        }
        catch
        {
            return jsonString;
        }
    }

    /// <summary>
    /// Parses a JSON array into a string array.
    /// Safe for values containing commas (e.g., card names).
    /// </summary>
    public static string[] ParseJsonArrayToStrings(string? jsonString)
    {
        if (string.IsNullOrEmpty(jsonString) || jsonString == "[]")
            return [];

        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            return doc.RootElement.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Parses otherFaceIds from either JSON array or CSV format.
    /// </summary>
    public static string[] ParseOtherFaceIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var result = ParseJsonArrayToStrings(value);
        if (result.Length > 0) return result;

        // Fallback: might already be CSV
        if (value!.Trim() != "[]")
        {
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToArray();
        }

        return [];
    }
}