using AetherVault.Core;

namespace AetherVault.Data;

/// <summary>
/// Fluent API for building MTG card search queries.
/// Port of TMTGSearchHelper from MTGSearchHelper.pas.
/// Builds parameterized SQL without holding a connection.
/// </summary>
public class MtgSearchHelper
{
    private string _baseSql = "";
    private bool _includeAllFaces;
    private string _limitClause = "";
    private string _offsetClause = "";
    private string _orderByClause = "";
    private bool _collapseReprints;
    private int _paramCounter;
    private readonly Dictionary<string, object> _params = new();
    private readonly List<string> _whereConditions = [];

    private enum MtgSearchQueryBaseKind { None, Cards, CardsWithTokens, Collection, Sets }

    private MtgSearchQueryBaseKind _baseKind = MtgSearchQueryBaseKind.None;

    /// <summary>True when the helper was configured with <see cref="SearchMyCollection"/>.</summary>
    public bool IsCollectionQuery => _baseKind == MtgSearchQueryBaseKind.Collection;

    /// <summary>True when the helper was configured with <see cref="SearchSets"/>.</summary>
    public bool IsSetsQuery => _baseKind == MtgSearchQueryBaseKind.Sets;

    /// <summary>True when the helper was configured with <see cref="SearchCards"/>.</summary>
    public bool IsCardTableQuery =>
        _baseKind is MtgSearchQueryBaseKind.Cards or MtgSearchQueryBaseKind.CardsWithTokens;

    /// <summary>True when WhereFts was used; callers can skip setting OrderBy("c.name") and use FTS relevance order.</summary>
    public bool UsedFts { get; private set; }

    /// <summary>When true, search returns one representative printing per oracle card.</summary>
    public bool CollapseReprintsEnabled => _collapseReprints;

    // ════════════════════════════════════════════════════════════════
    // Build — returns SQL + parameters for CardRepository to execute
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the final SQL and parameter set.
    /// Call this instead of Execute — the repository handles execution.
    /// </summary>
    public (string sql, List<(string name, object value)> parameters) Build()
    {
        EnsureSideFilter();
        var sql = _collapseReprints ? BuildCollapsedSelect(includeResultTotal: false) : GetSql();
        var paramList = _params.Select(kv => (kv.Key, kv.Value)).ToList();
        return (sql, paramList);
    }

    /// <summary>
    /// Builds a COUNT query and returns the SQL + parameters.
    /// </summary>
    public (string sql, List<(string name, object value)> parameters) BuildCount()
    {
        EnsureSideFilter();
        var countSql = _collapseReprints
            ? BuildCollapsedCount()
            : SqlQueries.CountWrapper + GetBaseWhereSql() + ")";
        var paramList = _params.Select(kv => (kv.Key, kv.Value)).ToList();
        return (countSql, paramList);
    }

    /// <summary>
    /// Like <see cref="Build"/>, but appends <c>COUNT(*) OVER() AS av_result_total</c> to the select list
    /// so one query can return a page and the full match count (no separate <see cref="BuildCount"/> round trip).
    /// Only valid after <see cref="SearchCards"/> (with or without tokens), not collection or set search.
    /// </summary>
    public (string sql, List<(string name, object value)> parameters) BuildWithResultTotal()
    {
        if (_baseKind is not (MtgSearchQueryBaseKind.Cards or MtgSearchQueryBaseKind.CardsWithTokens))
            throw new InvalidOperationException("BuildWithResultTotal requires SearchCards (or cards+tokens).");

        EnsureSideFilter();
        var sql = _collapseReprints
            ? BuildCollapsedSelect(includeResultTotal: true)
            : AppendPaging(InsertResultTotalWindowColumn(GetBaseWhereSql()));
        var paramList = _params.Select(kv => (kv.Key, kv.Value)).ToList();
        return (sql, paramList);
    }

    private static string InsertResultTotalWindowColumn(string baseWhereSql)
    {
        const string unionMark = "SELECT * FROM (";
        int u = baseWhereSql.IndexOf(unionMark, StringComparison.Ordinal);
        if (u >= 0)
        {
            return string.Concat(
                baseWhereSql.AsSpan(0, u),
                "SELECT c.*, COUNT(*) OVER() AS " + SqlQueries.ResultTotalCountColumnName + " FROM (",
                baseWhereSql.AsSpan(u + unionMark.Length));
        }

        int fromIdx = baseWhereSql.IndexOf("FROM cards c", StringComparison.Ordinal);
        if (fromIdx >= 0)
        {
            int nlBeforeFrom = baseWhereSql.LastIndexOf('\n', fromIdx);
            if (nlBeforeFrom >= 0)
                return baseWhereSql.Insert(
                    nlBeforeFrom,
                    ", COUNT(*) OVER() AS " + SqlQueries.ResultTotalCountColumnName);
        }

        throw new InvalidOperationException("Could not insert result total window column; unexpected card search SQL shape.");
    }

    // ════════════════════════════════════════════════════════════════
    // Base Query Selection
    // ════════════════════════════════════════════════════════════════

    public MtgSearchHelper SearchCards(bool includeTokens = false)
    {
        _baseKind = includeTokens ? MtgSearchQueryBaseKind.CardsWithTokens : MtgSearchQueryBaseKind.Cards;
        _baseSql = includeTokens ? SqlQueries.BaseCardsAndTokens : SqlQueries.BaseCards;
        return this;
    }

    public MtgSearchHelper SearchMyCollection()
    {
        _baseKind = MtgSearchQueryBaseKind.Collection;
        _baseSql = SqlQueries.BaseCollection;
        return this;
    }

    public MtgSearchHelper SearchSets()
    {
        _baseKind = MtgSearchQueryBaseKind.Sets;
        _baseSql = SqlQueries.BaseSets;
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Result Modifiers
    // ════════════════════════════════════════════════════════════════

    public MtgSearchHelper Limit(int count)
    {
        _limitClause = SqlQueries.SqlLimit + count;
        return this;
    }

    public MtgSearchHelper Offset(int count)
    {
        _offsetClause = SqlQueries.SqlOffset + count;
        return this;
    }

    public MtgSearchHelper OrderBy(string field, bool desc = false)
    {
        _orderByClause = SqlQueries.SqlOrderBy + field;
        if (desc) _orderByClause += SqlQueries.SqlDesc;
        return this;
    }

    public MtgSearchHelper IncludeAllFaces(bool include = true)
    {
        _includeAllFaces = include;
        return this;
    }

    /// <summary>Deduplicate catalog search to one printing per Scryfall oracle id.</summary>
    public MtgSearchHelper CollapseReprints(bool collapse = true)
    {
        _collapseReprints = collapse;
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Text Filters (all use _params dictionary — never concatenate user input into SQL)
    // ════════════════════════════════════════════════════════════════

    public MtgSearchHelper WhereNameContains(string name)
    {
        var param = NextParam("Name");
        _whereConditions.Add(SqlQueries.CondName + param);
        _params.Add(param, "%" + name + "%");
        return this;
    }

    public MtgSearchHelper WhereNameEquals(string name)
    {
        var param = NextParam("NameEq");
        _whereConditions.Add($"(c.name = @{param} OR c.faceName = @{param})");
        _params.Add(param, name);
        return this;
    }

    /// <summary>Matches any exact English card name (primary or face name). Empty entries skipped.</summary>
    public MtgSearchHelper WhereNameEqualsAny(IReadOnlyList<string> exactNames)
    {
        if (exactNames == null || exactNames.Count == 0)
            return this;

        var namePlaceholders = new List<string>();
        var facePlaceholders = new List<string>();
        foreach (var raw in exactNames)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var name = raw.Trim();
            var pName = NextParam("NameIn");
            var pFace = NextParam("FaceIn");
            namePlaceholders.Add("@" + pName);
            facePlaceholders.Add("@" + pFace);
            _params.Add(pName, name);
            _params.Add(pFace, name);
        }

        if (namePlaceholders.Count == 0)
            return this;

        _whereConditions.Add(
            $"((c.name IN ({string.Join(",", namePlaceholders)})) OR (c.faceName IN ({string.Join(",", facePlaceholders)})))");
        return this;
    }

    public MtgSearchHelper WhereTextContains(string text)
    {
        var param = NextParam("Text");
        _whereConditions.Add(SqlQueries.CondText + param);
        _params.Add(param, "%" + text + "%");
        return this;
    }

    /// <summary>
    /// Restricts results to rows matching the FTS query (name, faceName, text, type, artist, setName).
    /// Only use when av_cards_fts exists. Escapes double-quotes in the query for FTS5 MATCH.
    /// </summary>
    public MtgSearchHelper WhereFts(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return this;
        var param = NextParam("Fts");
        _whereConditions.Add(SqlQueries.CondFtsMatchPrefix + param + ")");
        _params.Add(param, EscapeFtsQuery(query.Trim()));
        UsedFts = true;
        return this;
    }

    /// <summary>Orders by FTS relevance (bm25) then c.name. Call when FTS filter is active.</summary>
    public MtgSearchHelper OrderByFtsRelevance()
    {
        _orderByClause = " " + SqlQueries.OrderByFtsRelevanceThenName;
        return this;
    }

    public MtgSearchHelper WhereArtist(string artist)
    {
        var param = NextParam("Artist");
        _whereConditions.Add(SqlQueries.CondArtist + param);
        _params.Add(param, "%" + artist + "%");
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Type Filters
    // ════════════════════════════════════════════════════════════════

    public MtgSearchHelper WhereType(string type)
    {
        var param = NextParam("Type");
        _whereConditions.Add(SqlQueries.CondType + param);
        _params.Add(param, "%" + type + "%");
        return this;
    }

    public MtgSearchHelper WhereType(string[] types)
    {
        if (types.Length == 0) return this;

        var conditions = new List<string>();
        foreach (var t in types)
        {
            var param = NextParam("Type");
            conditions.Add(SqlQueries.CondType + param);
            _params.Add(param, "%" + t + "%");
        }
        _whereConditions.Add("(" + string.Join(" OR ", conditions) + ")");
        return this;
    }

    public MtgSearchHelper WhereTypeFull(string typeLine)
    {
        var param = NextParam("Type");
        _whereConditions.Add("c.type LIKE @" + param);
        _params.Add(param, "%" + typeLine + "%");
        return this;
    }

    public MtgSearchHelper WhereSubtype(string subtype)
    {
        if (string.IsNullOrEmpty(subtype)) return this;
        var param = NextParam("Subtype");
        _whereConditions.Add("c.subtypes LIKE @" + param);
        _params.Add(param, "%" + subtype + "%");
        return this;
    }

    public MtgSearchHelper WhereSubtype(string[] subtypes)
    {
        if (subtypes.Length == 0) return this;

        var conditions = new List<string>();
        foreach (var s in subtypes)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            var param = NextParam("Subtype");
            conditions.Add("c.subtypes LIKE @" + param);
            _params.Add(param, "%" + s + "%");
        }
        if (conditions.Count > 0)
            _whereConditions.Add("(" + string.Join(" OR ", conditions) + ")");
        return this;
    }

    public MtgSearchHelper WhereSupertype(string supertype)
    {
        if (string.IsNullOrEmpty(supertype)) return this;
        var param = NextParam("Supertype");
        _whereConditions.Add("c.supertypes LIKE @" + param);
        _params.Add(param, "%" + supertype + "%");
        return this;
    }

    public MtgSearchHelper WhereSupertype(string[] supertypes)
    {
        if (supertypes.Length == 0) return this;

        var conditions = new List<string>();
        foreach (var s in supertypes)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            var param = NextParam("Supertype");
            conditions.Add("c.supertypes LIKE @" + param);
            _params.Add(param, "%" + s + "%");
        }
        if (conditions.Count > 0)
            _whereConditions.Add("(" + string.Join(" OR ", conditions) + ")");
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Attribute Filters
    // ════════════════════════════════════════════════════════════════

    public MtgSearchHelper WhereScryfallId(string scryfallId)
    {
        var param = NextParam("ScryfallId");
        // Union base (cards+tokens) only exposes alias "c"; cards-only base has "ci" from JOIN.
        var column = _baseSql.Contains("UNION ALL") ? "c.scryfallId" : "ci.scryfallId";
        _whereConditions.Add($"{column} = @{param}");
        _params.Add(param, scryfallId);
        return this;
    }

    public MtgSearchHelper WhereNumber(string number)
    {
        var param = NextParam("Number");
        _whereConditions.Add($"c.number = @{param}");
        _params.Add(param, number);
        return this;
    }

    public MtgSearchHelper WhereColors(string colors)
    {
        if (string.IsNullOrWhiteSpace(colors)) return this;

        var colorList = colors.Split(',');
        var conditions = new List<string>();

        foreach (var color in colorList)
        {
            var trimmed = color.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            // Colorless: MTGJSON uses empty colors for colorless cards, not "C"
            if (string.Equals(trimmed, "C", StringComparison.OrdinalIgnoreCase))
            {
                conditions.Add(SqlQueries.CondColorless);
                continue;
            }
            var param = NextParam("Color" + trimmed);
            conditions.Add(SqlQueries.CondColors + param);
            _params.Add(param, "%" + trimmed + "%");
        }

        if (conditions.Count > 0)
            _whereConditions.Add("(" + string.Join(" OR ", conditions) + ")");
        return this;
    }

    public MtgSearchHelper WhereColorIdentity(string identity)
    {
        if (string.IsNullOrEmpty(identity)) return this;
        var param = NextParam("ColorId");
        _whereConditions.Add("c.colorIdentity LIKE @" + param);
        _params.Add(param, "%" + identity + "%");
        return this;
    }

    /// <summary>
    /// Matches if <c>colorIdentity</c> contains any selected mana symbol (WUBRG), or colorless when <c>C</c> is included.
    /// Comma-separated; same UX as <see cref="WhereColors"/>.
    /// </summary>
    public MtgSearchHelper WhereColorIdentityAny(string colorCsv)
    {
        if (string.IsNullOrWhiteSpace(colorCsv)) return this;

        var colorList = colorCsv.Split(',');
        var conditions = new List<string>();

        foreach (var color in colorList)
        {
            var trimmed = color.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (string.Equals(trimmed, "C", StringComparison.OrdinalIgnoreCase))
            {
                conditions.Add(SqlQueries.CondColorIdentityColorless);
                continue;
            }
            var param = NextParam("ColorId" + trimmed);
            conditions.Add("c.colorIdentity LIKE @" + param);
            _params.Add(param, "%" + trimmed + "%");
        }

        if (conditions.Count > 0)
            _whereConditions.Add("(" + string.Join(" OR ", conditions) + ")");
        return this;
    }

    /// <summary>
    /// Each comma-separated term must appear in <c>keywords</c>: JSON array entries (via <c>json_each</c>) or, if not valid JSON,
    /// as a substring of the raw column (comma-separated oracle keywords in some SQLite exports).
    /// </summary>
    public MtgSearchHelper WhereKeywordTermsAll(string commaSeparatedTerms)
    {
        if (string.IsNullOrWhiteSpace(commaSeparatedTerms)) return this;

        var terms = commaSeparatedTerms.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var t = term.Trim().ToLowerInvariant();
            var paramJson = NextParam("Kw");
            var paramPlain = NextParam("KwPlain");
            _params.Add(paramJson, "%" + t + "%");
            _params.Add(paramPlain, "%" + t + "%");
            _whereConditions.Add(KeywordTermFlexibleMatch(paramJson, paramPlain));
        }

        return this;
    }

    /// <summary>Restricts results to cards that can be a commander (Legendary Creature or "can be your commander").</summary>
    public MtgSearchHelper WhereCommanderOnly()
    {
        _whereConditions.Add(SqlQueries.CondCommanderOnly);
        return this;
    }

    /// <summary>Restricts to cards with MTGJSON <c>isGameChanger</c> (Commander game changers).</summary>
    public MtgSearchHelper WhereGameChangerOnly()
    {
        _whereConditions.Add(SqlQueries.CondGameChangerOnly);
        return this;
    }

    public MtgSearchHelper WhereRarity(CardRarity rarity)
    {
        AddInClause("rarity", [rarity.ToDbString()], "Rarity");
        return this;
    }

    public MtgSearchHelper WhereRarity(CardRarity[] rarities)
    {
        AddInClause("rarity", rarities.Select(r => r.ToDbString()).ToArray(), "Rarity");
        return this;
    }

    public MtgSearchHelper WhereSet(string setCode)
    {
        AddInClause("setCode", [setCode], "SetCode");
        return this;
    }

    public MtgSearchHelper WhereSet(string[] setCodes)
    {
        AddInClause("setCode", setCodes, "SetCode");
        return this;
    }

    public MtgSearchHelper WhereLayout(CardLayout layout)
    {
        AddInClause("layout", [layout.ToDbString()], "Layout");
        return this;
    }

    public MtgSearchHelper WhereLayout(CardLayout[] layouts)
    {
        AddInClause("layout", layouts.Select(l => l.ToDbString()).ToArray(), "Layout");
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Numeric Filters
    // ════════════════════════════════════════════════════════════════

    public MtgSearchHelper WhereCmc(double value)
    {
        var param = NextParam("CMC");
        _whereConditions.Add(SqlQueries.CondManaValue + param);
        _params.Add(param, value);
        return this;
    }

    public MtgSearchHelper WhereCmcBetween(double min, double max)
    {
        var p1 = NextParam("CMCMin");
        var p2 = NextParam("CMCMax");
        _whereConditions.Add(SqlQueries.CondManaValueBetween + p1 + " AND @" + p2);
        _params.Add(p1, min);
        _params.Add(p2, max);
        return this;
    }

    public MtgSearchHelper WhereManaValue(double value, string op = "=")
    {
        var param = NextParam("MV");
        _whereConditions.Add($"c.manaValue {op} @{param}");
        _params.Add(param, value);
        return this;
    }

    public MtgSearchHelper WherePower(string power)
    {
        var param = NextParam("Power");
        _whereConditions.Add(SqlQueries.CondPower + param);
        _params.Add(param, power);
        return this;
    }

    public MtgSearchHelper WhereToughness(string toughness)
    {
        var param = NextParam("Toughness");
        _whereConditions.Add(SqlQueries.CondToughness + param);
        _params.Add(param, toughness);
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Legality Filters
    // ════════════════════════════════════════════════════════════════

    public MtgSearchHelper WhereLegalIn(DeckFormat format, LegalityStatus status = LegalityStatus.Legal)
    {
        var param = NextParam("Leg");
        var column = format.ToDbField();
        _whereConditions.Add($"cl.{column} LIKE @{param}");
        _params.Add(param, status.ToDbString());
        return this;
    }

    public MtgSearchHelper WhereLegalInAny(DeckFormat[] formats)
    {
        if (formats.Length == 0) return this;

        var conditions = new List<string>();
        foreach (var fmt in formats)
        {
            var param = NextParam("Leg");
            conditions.Add($"cl.{fmt.ToDbField()} LIKE @{param}");
            _params.Add(param, LegalityStatus.Legal.ToDbString());
        }
        _whereConditions.Add("(" + string.Join(" OR ", conditions) + ")");
        return this;
    }

    public MtgSearchHelper WhereBannedIn(DeckFormat format) =>
        WhereLegalIn(format, LegalityStatus.Banned);

    public MtgSearchHelper WhereNotLegalIn(DeckFormat format) =>
        WhereLegalIn(format, LegalityStatus.NotLegal);

    public MtgSearchHelper WhereRestrictedIn(DeckFormat format) =>
        WhereLegalIn(format, LegalityStatus.Restricted);

    // ════════════════════════════════════════════════════════════════
    // Special Filters
    // ════════════════════════════════════════════════════════════════

    public MtgSearchHelper WhereUuid(string uuid)
    {
        var param = NextParam("UUID");
        _whereConditions.Add(SqlQueries.CondUuid + param);
        _params.Add(param, uuid);
        return this;
    }

    public MtgSearchHelper WhereInCollection()
    {
        _whereConditions.Add(SqlQueries.CondInCollection);
        return this;
    }

    public MtgSearchHelper WhereNoVariations()
    {
        _whereConditions.Add(SqlQueries.CondNoVariations);
        return this;
    }

    public MtgSearchHelper WherePrimarySideOnly()
    {
        _whereConditions.Add(SqlQueries.CondSidePrimary);
        return this;
    }

    /// <summary>
    /// Restricts to rows whose <c>availability</c> includes <b>any</b> of the given tokens
    /// (<c>paper</c>, <c>mtgo</c>, <c>arena</c>). Supports JSON arrays (MTGJSON), quoted JSON substrings, and
    /// comma-separated text (e.g. <c>mtgo, paper</c>) as stored in some SQLite exports.
    /// </summary>
    public MtgSearchHelper WhereAvailabilityAny(IReadOnlyList<string> platforms)
    {
        if (platforms == null || platforms.Count == 0) return this;

        var allowed = AllowedAvailabilityTokens;
        var parts = new List<string>();
        foreach (var raw in platforms)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var p = raw.Trim().ToLowerInvariant();
            if (!allowed.Contains(p)) continue;
            var paramValue = NextParam("Avail");
            var paramJsonLike = NextParam("AvailLike");
            var paramCsvLike = NextParam("AvailCsv");
            _params.Add(paramValue, p);
            _params.Add(paramJsonLike, "%\"" + p + "\"%");
            // Normalized "token list": strip spaces, lower, wrap with commas so `mtgo,paper` matches `%,paper,%`.
            _params.Add(paramCsvLike, "%," + p + ",%");
            parts.Add(JsonQuotedOrCommaTokenMatch("c.availability", paramValue, paramJsonLike, paramCsvLike));
        }

        if (parts.Count > 0)
            _whereConditions.Add("(" + string.Join(" OR ", parts) + ")");
        return this;
    }

    private static readonly HashSet<string> AllowedAvailabilityTokens =
        new(StringComparer.Ordinal) { "paper", "mtgo", "arena" };

    /// <summary>
    /// Restricts to rows whose <c>finishes</c> includes <b>any</b> of the given tokens (<c>nonfoil</c>, <c>foil</c>, <c>etched</c>).
    /// Supports JSON arrays, quoted JSON substrings, and comma-separated plain text (e.g. <c>nonfoil</c>).
    /// </summary>
    public MtgSearchHelper WhereFinishesAny(IReadOnlyList<string> finishes)
    {
        if (finishes == null || finishes.Count == 0) return this;

        var allowed = AllowedFinishTokens;
        var parts = new List<string>();
        foreach (var raw in finishes)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var p = raw.Trim().ToLowerInvariant();
            if (!allowed.Contains(p)) continue;
            var paramValue = NextParam("Finish");
            var paramJsonLike = NextParam("FinishLike");
            var paramCsvLike = NextParam("FinishCsv");
            _params.Add(paramValue, p);
            _params.Add(paramJsonLike, "%\"" + p + "\"%");
            _params.Add(paramCsvLike, "%," + p + ",%");
            parts.Add(JsonQuotedOrCommaTokenMatch("c.finishes", paramValue, paramJsonLike, paramCsvLike));
        }

        if (parts.Count > 0)
            _whereConditions.Add("(" + string.Join(" OR ", parts) + ")");
        return this;
    }

    private static readonly HashSet<string> AllowedFinishTokens =
        new(StringComparer.Ordinal) { "nonfoil", "foil", "etched" };

    /// <summary>
    /// One token in a column: valid JSON array via <c>json_each</c>, quoted JSON substring, or comma-wrapped list after stripping spaces.
    /// </summary>
    private static string JsonQuotedOrCommaTokenMatch(string columnPath, string valueParam, string jsonLikeParam, string csvLikeParam)
    {
        var csvNorm = "LOWER(REPLACE(TRIM(IFNULL(" + columnPath + ", '')), ' ', ''))";
        var csvWrapped = "',' || " + csvNorm + " || ','";
        return "((json_valid(" + columnPath + ") = 1 AND EXISTS (SELECT 1 FROM json_each(" + columnPath +
            ") WHERE LOWER(TRIM(CAST(json_each.value AS TEXT))) = @" + valueParam + ")) OR (" + columnPath +
            " LIKE @" + jsonLikeParam + ") OR (" + csvWrapped + " LIKE @" + csvLikeParam + "))";
    }

    /// <summary>Keyword term: JSON array match or plain-text <c>keywords</c> column substring (CSV oracle keywords).</summary>
    private static string KeywordTermFlexibleMatch(string jsonLikeParam, string plainLikeParam) =>
        "((json_valid(c.keywords) = 1 AND EXISTS (SELECT 1 FROM json_each(c.keywords) WHERE LOWER(CAST(json_each.value AS TEXT)) LIKE @" +
        jsonLikeParam + ")) OR (LOWER(IFNULL(c.keywords, '')) LIKE @" + plainLikeParam + "))";

    /// <summary>
    /// MTGJSON text columns are usually JSON arrays but some rows are empty or non-JSON; <c>json_each</c> alone throws
    /// <c>SQLite Error 1: malformed JSON</c>. Guard with <c>json_valid</c> first.
    /// </summary>
    private static string SafeJsonArrayTokenEquals(string columnPath, string dapperParamName) =>
        "(json_valid(" + columnPath + ") = 1 AND EXISTS (SELECT 1 FROM json_each(" + columnPath +
        ") WHERE json_each.value = @" + dapperParamName + "))";

    //public MTGSearchHelper WhereCustom(string condition)
    //{
    //    _whereConditions.Add("(" + condition + ")");
    //    return this;
    //}

    // ════════════════════════════════════════════════════════════════
    // Internal SQL building
    // ════════════════════════════════════════════════════════════════

    private string NextParam(string fieldName = "")
    {
        _paramCounter++;
        return string.IsNullOrEmpty(fieldName)
            ? $"pParam{_paramCounter}"
            : $"p{fieldName}{_paramCounter}";
    }

    private void AddInClause(string column, string[] values, string paramBaseName)
    {
        if (values.Length == 0) return;

        var paramNames = new List<string>();
        foreach (var value in values)
        {
            var trimmed = value.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var param = NextParam(paramBaseName);
            _params.Add(param, trimmed);
            paramNames.Add("@" + param);
        }

        if (paramNames.Count > 0)
            _whereConditions.Add($"c.{column} IN ({string.Join(",", paramNames)})");
    }

    private string GetBaseWhereSql()
    {
        var sql = _baseSql;
        if (_whereConditions.Count > 0)
            sql += SqlQueries.SqlWhere + string.Join(SqlQueries.SqlAnd, _whereConditions);
        return sql;
    }

    private string GetSql()
    {
        return AppendPaging(GetBaseWhereSql());
    }

    private string AppendPaging(string sql)
    {
        if (!string.IsNullOrEmpty(_orderByClause))
            sql += " " + NormalizeOrderByClause(_orderByClause);
        if (!string.IsNullOrEmpty(_limitClause))
            sql += " " + _limitClause;
        if (!string.IsNullOrEmpty(_offsetClause))
            sql += _offsetClause;
        return sql;
    }

    private string BuildCollapsedSelect(bool includeResultTotal)
    {
        var filtered = GetBaseWhereSql();
        var totalColumn = includeResultTotal
            ? $", COUNT(*) OVER() AS {SqlQueries.ResultTotalCountColumnName}"
            : "";

        var sql = $"""
            WITH av_filtered AS (
            {filtered}
            ),
            av_deduped AS (
              SELECT av_filtered.*,
                ROW_NUMBER() OVER (
                  PARTITION BY COALESCE(NULLIF(TRIM(av_filtered.scryfallOracleId), ''), av_filtered.uuid)
                  ORDER BY
                    CASE WHEN lower(COALESCE(av_filtered.language, '')) IN ('', 'english') THEN 0 ELSE 1 END,
                    CASE WHEN instr(lower(COALESCE(av_filtered.availability, '')), 'paper') > 0 THEN 0 ELSE 1 END,
                    av_filtered.setReleaseDate DESC NULLS LAST,
                    CASE WHEN COALESCE(av_filtered.edhrecRank, 0) > 0 THEN av_filtered.edhrecRank ELSE 999999999 END,
                    av_filtered.setCode DESC,
                    av_filtered.uuid
                ) AS av_oracle_rn
              FROM av_filtered
            )
            SELECT d.*{totalColumn}
            FROM av_deduped d
            WHERE d.av_oracle_rn = 1
            """;

        return AppendPaging(sql);
    }

    private string BuildCollapsedCount()
    {
        var filtered = GetBaseWhereSql();
        return $"""
            WITH av_filtered AS (
            {filtered}
            ),
            av_deduped AS (
              SELECT
                ROW_NUMBER() OVER (
                  PARTITION BY COALESCE(NULLIF(TRIM(av_filtered.scryfallOracleId), ''), av_filtered.uuid)
                  ORDER BY
                    CASE WHEN lower(COALESCE(av_filtered.language, '')) IN ('', 'english') THEN 0 ELSE 1 END,
                    CASE WHEN instr(lower(COALESCE(av_filtered.availability, '')), 'paper') > 0 THEN 0 ELSE 1 END,
                    av_filtered.setReleaseDate DESC NULLS LAST,
                    CASE WHEN COALESCE(av_filtered.edhrecRank, 0) > 0 THEN av_filtered.edhrecRank ELSE 999999999 END,
                    av_filtered.setCode DESC,
                    av_filtered.uuid
                ) AS av_oracle_rn
              FROM av_filtered
            )
            SELECT COUNT(*) AS {SqlQueries.CountField}
            FROM av_deduped
            WHERE av_oracle_rn = 1
            """;
    }

    private string NormalizeOrderByClause(string orderByClause) =>
        _collapseReprints
            ? orderByClause.Replace("c.", "", StringComparison.Ordinal)
            : orderByClause;

    private void EnsureSideFilter()
    {
        if (_includeAllFaces) return;

        // Only add if no side filter already exists
        var currentSql = GetSql();
        if (!currentSql.Contains("c.side"))
            _whereConditions.Add(SqlQueries.CondSidePrimary);
    }

    /// <summary>Escapes double-quote for FTS5 MATCH query string.</summary>
    private static string EscapeFtsQuery(string query) => query.Replace("\"", "\"\"");
}
