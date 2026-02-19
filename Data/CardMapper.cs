using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Models;
using System.Text.Json;

namespace MTGFetchMAUI.Data;

/// <summary>
/// Maps SQLite data reader rows to Card objects.
/// Replaces TCardFieldMap + PopulateCardFromFields from CardRepository.pas.
/// Handles JSON array parsing, null-safe field access, and enum conversion.
/// </summary>
public static class CardMapper
{
    /// <summary>
    /// Populates a Card from the current row of a SqliteDataReader.
    /// Expects the full card query (cards + identifiers + sets + legalities joins).
    /// </summary>
    public static Card MapCard(SqliteDataReader reader)
    {
        var card = new Card();

        // ── Identifiers & Basic Info ────────────────────────────────
        card.UUID = SafeStr(reader, "uuid");
        card.ScryfallId = SafeStr(reader, "scryfallId");
        card.Name = SafeStr(reader, "name");
        card.AsciiName = SafeStr(reader, "asciiName");
        card.PrintedName = SafeStr(reader, "printedName");
        card.ManaCost = SafeStr(reader, "manaCost");
        card.CardType = SafeStr(reader, "type");
        card.Text = SafeStr(reader, "text");
        card.OriginalText = SafeStr(reader, "originalText");
        card.FlavorText = SafeStr(reader, "flavorText");
        card.FlavorName = SafeStr(reader, "flavorName");

        // ── JSON array fields → CSV ────────────────────────────────
        card.Colors = CleanJsonArray(SafeStr(reader, "colors"));
        card.ColorIdentity = CleanJsonArray(SafeStr(reader, "colorIdentity"));
        card.ColorIndicator = CleanJsonArray(SafeStr(reader, "colorIndicator"));
        card.Keywords = CleanJsonArray(SafeStr(reader, "keywords"));
        card.ProducedMana = CleanJsonArray(SafeStr(reader, "producedMana"));
        card.Subtypes = CleanJsonArray(SafeStr(reader, "subtypes"));
        card.Supertypes = CleanJsonArray(SafeStr(reader, "supertypes"));
        card.OtherFaceIds = CleanJsonArray(SafeStr(reader, "otherFaceIds"));
        card.FrameEffects = CleanJsonArray(SafeStr(reader, "frameEffects"));
        card.PromoTypes = CleanJsonArray(SafeStr(reader, "promoTypes"));
        card.Finishes = CleanJsonArray(SafeStr(reader, "finishes"));
        card.Availability = CleanJsonArray(SafeStr(reader, "availability"));
        card.AttractionLights = CleanJsonArray(SafeStr(reader, "attractionLights"));

        // CardParts kept as raw JSON (card names can contain commas)
        card.CardParts = SafeStr(reader, "cardParts");

        // ── Plain string fields ────────────────────────────────────
        card.Power = SafeStr(reader, "power");
        card.Toughness = SafeStr(reader, "toughness");
        card.Loyalty = SafeStr(reader, "loyalty");
        card.Defense = SafeStr(reader, "defense");
        card.Hand = SafeStr(reader, "hand");
        card.Life = SafeStr(reader, "life");
        card.FaceName = SafeStr(reader, "faceName");
        card.FaceFlavorName = SafeStr(reader, "faceFlavorName");
        card.FacePrintedName = SafeStr(reader, "facePrintedName");
        card.Watermark = SafeStr(reader, "watermark");
        card.FrameVersion = SafeStr(reader, "frameVersion");
        card.SecurityStamp = SafeStr(reader, "securityStamp");
        card.Signature = SafeStr(reader, "signature");
        card.Artist = SafeStr(reader, "artist");
        card.BorderColor = SafeStr(reader, "borderColor");
        card.Language = SafeStr(reader, "language");
        card.LeadershipSkills = SafeStr(reader, "leadershipSkills");
        card.SourceProducts = SafeStr(reader, "sourceProducts");

        // ── Set Info ───────────────────────────────────────────────
        card.SetCode = SafeStr(reader, "setCode");
        card.SetName = SafeStr(reader, "setName");
        card.Number = SafeStr(reader, "number");
        var keyrune = SafeStr(reader, "keyruneCode");
        card.KeyruneCode = string.IsNullOrEmpty(keyrune) ? card.SetCode : keyrune;

        // ── Numeric values ─────────────────────────────────────────
        card.CMC = SafeDouble(reader, "manaValue");
        card.FaceManaValue = SafeDouble(reader, "faceManaValue");
        card.EDHRecRank = SafeInt(reader, "edhrecRank");
        card.EDHRecSaltiness = SafeDouble(reader, "edhrecSaltiness");

        // ── Boolean flags ──────────────────────────────────────────
        // Foil flags: hasFoil/hasNonFoil removed in MTGJSON v5.3+, derive from finishes
        if (HasColumn(reader, "hasFoil"))
            card.IsFoil = SafeBool(reader, "hasFoil");
        else
            card.IsFoil = card.Finishes.Contains("foil", StringComparison.OrdinalIgnoreCase);

        if (HasColumn(reader, "hasNonFoil"))
            card.IsNonFoil = SafeBool(reader, "hasNonFoil");
        else
            card.IsNonFoil = card.Finishes.Contains("nonfoil", StringComparison.OrdinalIgnoreCase);

        card.IsPromo = SafeBool(reader, "isPromo");
        card.IsReprint = SafeBool(reader, "isReprint");
        card.IsAlternative = SafeBool(reader, "isAlternative");
        card.IsReserved = SafeBool(reader, "isReserved");
        card.IsFullArt = SafeBool(reader, "isFullArt");
        card.IsFunny = SafeBool(reader, "isFunny");
        card.IsOnlineOnly = SafeBool(reader, "isOnlineOnly");
        card.IsOversized = SafeBool(reader, "isOversized");
        card.IsRebalanced = SafeBool(reader, "isRebalanced");
        card.IsStorySpotlight = SafeBool(reader, "isStorySpotlight");
        card.IsTextless = SafeBool(reader, "isTextless");
        card.IsTimeshifted = SafeBool(reader, "isTimeshifted");
        card.IsGameChanger = SafeBool(reader, "isGameChanger");
        card.HasAlternativeDeckLimit = SafeBool(reader, "hasAlternativeDeckLimit");
        card.HasContentWarning = SafeBool(reader, "hasContentWarning");

        // ── Enums ──────────────────────────────────────────────────
        card.Rarity = EnumExtensions.ParseCardRarity(SafeStr(reader, "rarity"));
        card.Layout = EnumExtensions.ParseCardLayout(SafeStr(reader, "layout"));

        var sideStr = SafeStr(reader, "side");
        card.Side = string.IsNullOrEmpty(sideStr) ? 'a' : sideStr[0];

        // ── Legalities ─────────────────────────────────────────────
        card.Legalities = new CardLegalities();
        foreach (DeckFormat fmt in Enum.GetValues<DeckFormat>())
        {
            var dbField = fmt.ToDbField();
            var legalityStr = SafeStr(reader, dbField);
            if (!string.IsNullOrEmpty(legalityStr))
                card.Legalities[fmt] = EnumExtensions.ParseLegalityStatus(legalityStr);
        }

        return card;
    }

    // ── Safe field accessors ────────────────────────────────────────

    private static string SafeStr(SqliteDataReader reader, string column)
    {
        var ordinal = GetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal)) return "";
        return reader.GetString(ordinal);
    }

    private static double SafeDouble(SqliteDataReader reader, string column)
    {
        var ordinal = GetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal)) return 0.0;
        try
        {
            return reader.GetDouble(ordinal);
        }
        catch
        {
            // Handle TEXT→REAL mismatch from MTGJSON schema changes
            var str = reader.GetString(ordinal);
            return double.TryParse(str, out var val) ? val : 0.0;
        }
    }

    private static int SafeInt(SqliteDataReader reader, string column)
    {
        var ordinal = GetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal)) return 0;
        try
        {
            return reader.GetInt32(ordinal);
        }
        catch
        {
            var str = reader.GetString(ordinal);
            if (int.TryParse(str, out var intVal)) return intVal;
            if (double.TryParse(str, out var dblVal)) return (int)Math.Round(dblVal);
            return 0;
        }
    }

    private static bool SafeBool(SqliteDataReader reader, string column) =>
        SafeInt(reader, column) == 1;

    private static int GetOrdinal(SqliteDataReader reader, string column)
    {
        try
        {
            return reader.GetOrdinal(column);
        }
        catch (ArgumentOutOfRangeException)
        {
            return -1;
        }
    }

    private static bool HasColumn(SqliteDataReader reader, string column) =>
        GetOrdinal(reader, column) >= 0;

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

        // Try JSON parse first
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
