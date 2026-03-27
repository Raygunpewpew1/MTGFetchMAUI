using System.Text.Json;
using AetherVault.Core;
using AetherVault.Models;

namespace AetherVault.Data;

/// <summary>Maps <c>atomic_cards</c> rows to <see cref="Card"/> for lite catalog mode.</summary>
public static class AtomicCatalogMapper
{
    public sealed class AtomicRow
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public int face_index { get; set; }
        public string? ascii_name { get; set; }
        public string? face_name { get; set; }
        public string? mana_cost { get; set; }
        public double mana_value { get; set; }
        public string? type_line { get; set; }
        public string? oracle_text { get; set; }
        public string? power { get; set; }
        public string? toughness { get; set; }
        public string? loyalty { get; set; }
        public string? defense { get; set; }
        public string? layout { get; set; }
        public string? colors { get; set; }
        public string? color_identity { get; set; }
        public string? keywords { get; set; }
        public string? scryfall_id { get; set; }
        public string? scryfall_oracle_id { get; set; }
        public string? identifiers_json { get; set; }
        public string? first_printing { get; set; }
        public string? printings_json { get; set; }
        public string? legalities_json { get; set; }
        public string? rulings_json { get; set; }
        public string? related_json { get; set; }
        public string? leadership_json { get; set; }
        public int is_reserved { get; set; }
        public int is_funny { get; set; }
    }

    private sealed class IdentifiersDoc
    {
        public string? ScryfallId { get; init; }
        public string? MtgjsonV4Id { get; init; }
        public string? MtgjsonNonFoilVersionId { get; init; }
        public string? MtgjsonFoilVersionId { get; init; }
    }

    private static string? JsonStringCi(JsonElement root, ReadOnlySpan<string> names)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
                continue;
            foreach (var n in names)
            {
                if (prop.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetString();
            }
        }

        return null;
    }

    private static IdentifiersDoc? TryParseIdentifiers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            return new IdentifiersDoc
            {
                ScryfallId = JsonStringCi(root, ["scryfallId", "scryfall_id"]),
                MtgjsonV4Id = JsonStringCi(root, ["mtgjsonV4Id", "mtgjson_v4_id"]),
                MtgjsonNonFoilVersionId = JsonStringCi(root, ["mtgjsonNonFoilVersionId", "mtgjson_non_foil_version_id"]),
                MtgjsonFoilVersionId = JsonStringCi(root, ["mtgjsonFoilVersionId", "mtgjson_foil_version_id"])
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? TrimPrintingId(string? s)
    {
        var t = (s ?? "").Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>
    /// SQL predicate: table alias <paramref name="alias"/> matches bound parameter <paramref name="paramName"/> (without @).
    /// </summary>
    public static string SqlAliasMatchesIdParameter(string alias, string paramName)
    {
        var p = paramName.TrimStart('@');
        return $"""
            ({alias}.scryfall_id = @{p} OR {alias}.scryfall_oracle_id = @{p}
             OR ('atomic:' || CAST({alias}.id AS TEXT)) = @{p}
             OR (IFNULL({alias}.identifiers_json, '') != ''
                 AND (json_extract({alias}.identifiers_json, '$.mtgjsonV4Id') = @{p}
                   OR json_extract({alias}.identifiers_json, '$.mtgjsonNonFoilVersionId') = @{p}
                   OR json_extract({alias}.identifiers_json, '$.mtgjsonFoilVersionId') = @{p})))
            """;
    }

    /// <summary>Every id string that can resolve this row (for dictionary hydration and rulings lookups).</summary>
    public static IEnumerable<string> EnumerateLookupKeys(AtomicRow r)
    {
        string? t(string? s)
        {
            var v = (s ?? "").Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }

        var a = t(r.scryfall_id);
        if (a != null) yield return a;
        a = t(r.scryfall_oracle_id);
        if (a != null) yield return a;
        yield return $"atomic:{r.id}";

        if (TryParseIdentifiers(r.identifiers_json) is not { } ids)
            yield break;

        a = t(ids.ScryfallId);
        if (a != null) yield return a;
        a = t(ids.MtgjsonV4Id);
        if (a != null) yield return a;
        a = t(ids.MtgjsonNonFoilVersionId);
        if (a != null) yield return a;
        a = t(ids.MtgjsonFoilVersionId);
        if (a != null) yield return a;
    }

    public static bool RowMatchesLookupId(AtomicRow r, string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;
        foreach (var k in EnumerateLookupKeys(r))
        {
            if (string.Equals(k, id, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static void AddAtomicRowToLookupDictionary(Dictionary<string, Card> result, AtomicRow r, Card card)
    {
        foreach (var key in EnumerateLookupKeys(r))
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(card.Uuid))
                continue;
            if (!result.ContainsKey(key))
                result[key] = card;
        }
    }

    public static Card ToCard(AtomicRow r)
    {
        var ids = TryParseIdentifiers(r.identifiers_json);
        // Printing-specific Scryfall UUID (required for cards.scryfall.io). Prefer columns, then identifiers JSON.
        var printingRaw = TrimPrintingId(r.scryfall_id) ?? TrimPrintingId(ids?.ScryfallId);
        var printingCdn = string.IsNullOrEmpty(printingRaw) ? "" : printingRaw.ToLowerInvariant();

        var mtgv4 = (ids?.MtgjsonV4Id ?? "").Trim();
        var mtgNf = (ids?.MtgjsonNonFoilVersionId ?? "").Trim();
        var mtgF = (ids?.MtgjsonFoilVersionId ?? "").Trim();

        var publicUuid = !string.IsNullOrEmpty(mtgv4)
            ? mtgv4
            : !string.IsNullOrEmpty(mtgNf)
                ? mtgNf
                : !string.IsNullOrEmpty(mtgF)
                    ? mtgF
                    : !string.IsNullOrEmpty(printingRaw)
                        ? printingRaw
                        : $"atomic:{r.id}";

        var card = new Card
        {
            Uuid = publicUuid,
            ScryfallId = printingCdn,
            Name = r.name ?? "",
            PrintedName = r.face_name ?? "",
            ManaCost = r.mana_cost ?? "",
            Cmc = r.mana_value,
            CardType = r.type_line ?? "",
            Text = r.oracle_text ?? "",
            OriginalText = r.oracle_text ?? "",
            Power = r.power ?? "",
            Toughness = r.toughness ?? "",
            Loyalty = r.loyalty ?? "",
            Defense = r.defense ?? "",
            Layout = EnumExtensions.ParseCardLayout(r.layout),
            Colors = r.colors ?? "",
            Keywords = r.keywords ?? "",
            SetCode = r.first_printing ?? "",
            SetName = "",
            Number = "",
            KeyruneCode = r.first_printing ?? "",
            Side = r.face_index == 0 ? 'a' : 'b',
            IsReserved = r.is_reserved != 0,
            IsFunny = r.is_funny != 0,
            Legalities = ParseLegalities(r.legalities_json),
            ImageUrl = string.IsNullOrEmpty(printingCdn)
                ? ""
                : ScryfallCdn.GetImageUrl(printingCdn, ScryfallSize.Small, ScryfallFace.Front)
        };

        return card;
    }

    public static CardLegalities ParseLegalities(string? json)
    {
        var leg = new CardLegalities();
        if (string.IsNullOrWhiteSpace(json))
            return leg;

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var fmt = EnumExtensions.ParseDeckFormat(prop.Name);
                if (prop.Value.ValueKind == JsonValueKind.String)
                    leg[fmt] = EnumExtensions.ParseLegalityStatus(prop.Value.GetString());
            }
        }
        catch
        {
            // leave defaults
        }

        return leg;
    }

    public static List<CardRuling> ParseRulings(string? json)
    {
        var list = new List<CardRuling>();
        if (string.IsNullOrWhiteSpace(json))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var dateStr = el.TryGetProperty("date", out var d) ? d.GetString() : null;
                var text = el.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(text))
                    continue;
                DateTime.TryParse(dateStr, out var date);
                list.Add(new CardRuling(date, text));
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }
}
