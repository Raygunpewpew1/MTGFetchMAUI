using MTGFetchMAUI.Core;

namespace MTGFetchMAUI.Data;

/// <summary>
/// Fluent API for building MTG card search queries.
/// Port of TMTGSearchHelper from MTGSearchHelper.pas.
/// Builds parameterized SQL without holding a connection.
/// </summary>
public class MTGSearchHelper
{
    private string _baseSQL = "";
    private bool _includeAllFaces;
    private string _limitClause = "";
    private string _offsetClause = "";
    private string _orderByClause = "";
    private int _paramCounter;
    private readonly Dictionary<string, object> _params = new();
    private readonly List<string> _whereConditions = [];

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
        var sql = GetSQL();
        var paramList = _params.Select(kv => (kv.Key, kv.Value)).ToList();
        return (sql, paramList);
    }

    /// <summary>
    /// Builds a COUNT query and returns the SQL + parameters.
    /// </summary>
    public (string sql, List<(string name, object value)> parameters) BuildCount()
    {
        EnsureSideFilter();
        var countSQL = SQLQueries.CountWrapper + GetBaseWhereSQL() + ")";
        var paramList = _params.Select(kv => (kv.Key, kv.Value)).ToList();
        return (countSQL, paramList);
    }

    // ════════════════════════════════════════════════════════════════
    // Base Query Selection
    // ════════════════════════════════════════════════════════════════

    public MTGSearchHelper SearchCards()
    {
        _baseSQL = SQLQueries.BaseCards;
        return this;
    }

    public MTGSearchHelper SearchMyCollection()
    {
        _baseSQL = SQLQueries.BaseCollection;
        return this;
    }

    public MTGSearchHelper SearchSets()
    {
        _baseSQL = SQLQueries.BaseSets;
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Result Modifiers
    // ════════════════════════════════════════════════════════════════

    public MTGSearchHelper Limit(int count)
    {
        _limitClause = SQLQueries.SqlLimit + count;
        return this;
    }

    public MTGSearchHelper Offset(int count)
    {
        _offsetClause = SQLQueries.SqlOffset + count;
        return this;
    }

    public MTGSearchHelper OrderBy(string field, bool desc = false)
    {
        _orderByClause = SQLQueries.SqlOrderBy + field;
        if (desc) _orderByClause += SQLQueries.SqlDesc;
        return this;
    }

    public MTGSearchHelper IncludeAllFaces(bool include = true)
    {
        _includeAllFaces = include;
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Text Filters
    // ════════════════════════════════════════════════════════════════

    public MTGSearchHelper WhereNameContains(string name)
    {
        var param = NextParam("Name");
        _whereConditions.Add(SQLQueries.CondName + param);
        _params.Add(param, "%" + name + "%");
        return this;
    }

    public MTGSearchHelper WhereTextContains(string text)
    {
        var param = NextParam("Text");
        _whereConditions.Add(SQLQueries.CondText + param);
        _params.Add(param, "%" + text + "%");
        return this;
    }

    public MTGSearchHelper WhereArtist(string artist)
    {
        var param = NextParam("Artist");
        _whereConditions.Add(SQLQueries.CondArtist + param);
        _params.Add(param, "%" + artist + "%");
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Type Filters
    // ════════════════════════════════════════════════════════════════

    public MTGSearchHelper WhereType(string type)
    {
        var param = NextParam("Type");
        _whereConditions.Add(SQLQueries.CondType + param);
        _params.Add(param, "%" + type + "%");
        return this;
    }

    public MTGSearchHelper WhereType(string[] types)
    {
        if (types.Length == 0) return this;

        var conditions = new List<string>();
        foreach (var t in types)
        {
            var param = NextParam("Type");
            conditions.Add(SQLQueries.CondType + param);
            _params.Add(param, "%" + t + "%");
        }
        _whereConditions.Add("(" + string.Join(" OR ", conditions) + ")");
        return this;
    }

    public MTGSearchHelper WhereTypeFull(string typeLine)
    {
        var param = NextParam("Type");
        _whereConditions.Add("c.type LIKE @" + param);
        _params.Add(param, "%" + typeLine + "%");
        return this;
    }

    public MTGSearchHelper WhereSubtype(string subtype)
    {
        if (string.IsNullOrEmpty(subtype)) return this;
        var param = NextParam("Subtype");
        _whereConditions.Add("c.subtypes LIKE @" + param);
        _params.Add(param, "%" + subtype + "%");
        return this;
    }

    public MTGSearchHelper WhereSubtype(string[] subtypes)
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

    public MTGSearchHelper WhereSupertype(string supertype)
    {
        if (string.IsNullOrEmpty(supertype)) return this;
        var param = NextParam("Supertype");
        _whereConditions.Add("c.supertypes LIKE @" + param);
        _params.Add(param, "%" + supertype + "%");
        return this;
    }

    public MTGSearchHelper WhereSupertype(string[] supertypes)
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

    public MTGSearchHelper WhereColors(string colors)
    {
        if (string.IsNullOrWhiteSpace(colors)) return this;

        var colorList = colors.Split(',');
        var conditions = new List<string>();

        foreach (var color in colorList)
        {
            var trimmed = color.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var param = NextParam("Color" + trimmed);
            conditions.Add(SQLQueries.CondColors + param);
            _params.Add(param, "%" + trimmed + "%");
        }

        if (conditions.Count > 0)
            _whereConditions.Add("(" + string.Join(" OR ", conditions) + ")");
        return this;
    }

    public MTGSearchHelper WhereColorIdentity(string identity)
    {
        if (string.IsNullOrEmpty(identity)) return this;
        var param = NextParam("ColorId");
        _whereConditions.Add("c.colorIdentity LIKE @" + param);
        _params.Add(param, "%" + identity + "%");
        return this;
    }

    public MTGSearchHelper WhereRarity(CardRarity rarity)
    {
        AddInClause("rarity", [rarity.ToDbString()], "Rarity");
        return this;
    }

    public MTGSearchHelper WhereRarity(CardRarity[] rarities)
    {
        AddInClause("rarity", rarities.Select(r => r.ToDbString()).ToArray(), "Rarity");
        return this;
    }

    public MTGSearchHelper WhereSet(string setCode)
    {
        AddInClause("setCode", [setCode], "SetCode");
        return this;
    }

    public MTGSearchHelper WhereSet(string[] setCodes)
    {
        AddInClause("setCode", setCodes, "SetCode");
        return this;
    }

    public MTGSearchHelper WhereLayout(CardLayout layout)
    {
        AddInClause("layout", [layout.ToDbString()], "Layout");
        return this;
    }

    public MTGSearchHelper WhereLayout(CardLayout[] layouts)
    {
        AddInClause("layout", layouts.Select(l => l.ToDbString()).ToArray(), "Layout");
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Numeric Filters
    // ════════════════════════════════════════════════════════════════

    public MTGSearchHelper WhereCMC(double value)
    {
        var param = NextParam("CMC");
        _whereConditions.Add(SQLQueries.CondManaValue + param);
        _params.Add(param, value);
        return this;
    }

    public MTGSearchHelper WhereCMCBetween(double min, double max)
    {
        var p1 = NextParam("CMCMin");
        var p2 = NextParam("CMCMax");
        _whereConditions.Add(SQLQueries.CondManaValueBetween + p1 + " AND @" + p2);
        _params.Add(p1, min);
        _params.Add(p2, max);
        return this;
    }

    public MTGSearchHelper WhereManaValue(double value, string op = "=")
    {
        var param = NextParam("MV");
        _whereConditions.Add($"c.manaValue {op} @{param}");
        _params.Add(param, value);
        return this;
    }

    public MTGSearchHelper WherePower(string power)
    {
        var param = NextParam("Power");
        _whereConditions.Add(SQLQueries.CondPower + param);
        _params.Add(param, power);
        return this;
    }

    public MTGSearchHelper WhereToughness(string toughness)
    {
        var param = NextParam("Toughness");
        _whereConditions.Add(SQLQueries.CondToughness + param);
        _params.Add(param, toughness);
        return this;
    }

    // ════════════════════════════════════════════════════════════════
    // Legality Filters
    // ════════════════════════════════════════════════════════════════

    public MTGSearchHelper WhereLegalIn(DeckFormat format, LegalityStatus status = LegalityStatus.Legal)
    {
        var param = NextParam("Leg");
        var column = format.ToDbField();
        _whereConditions.Add($"cl.{column} LIKE @{param}");
        _params.Add(param, status.ToDbString());
        return this;
    }

    public MTGSearchHelper WhereLegalInAny(DeckFormat[] formats)
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

    public MTGSearchHelper WhereBannedIn(DeckFormat format) =>
        WhereLegalIn(format, LegalityStatus.Banned);

    public MTGSearchHelper WhereNotLegalIn(DeckFormat format) =>
        WhereLegalIn(format, LegalityStatus.NotLegal);

    public MTGSearchHelper WhereRestrictedIn(DeckFormat format) =>
        WhereLegalIn(format, LegalityStatus.Restricted);

    // ════════════════════════════════════════════════════════════════
    // Special Filters
    // ════════════════════════════════════════════════════════════════

    public MTGSearchHelper WhereUUID(string uuid)
    {
        var param = NextParam("UUID");
        _whereConditions.Add(SQLQueries.CondUuid + param);
        _params.Add(param, uuid);
        return this;
    }

    public MTGSearchHelper WhereInCollection()
    {
        _whereConditions.Add(SQLQueries.CondInCollection);
        return this;
    }

    public MTGSearchHelper WhereNoVariations()
    {
        _whereConditions.Add(SQLQueries.CondNoVariations);
        return this;
    }

    public MTGSearchHelper WherePrimarySideOnly()
    {
        _whereConditions.Add(SQLQueries.CondSidePrimary);
        return this;
    }

    public MTGSearchHelper WhereCustom(string condition)
    {
        _whereConditions.Add("(" + condition + ")");
        return this;
    }

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

    private string GetBaseWhereSQL()
    {
        var sql = _baseSQL;
        if (_whereConditions.Count > 0)
            sql += SQLQueries.SqlWhere + string.Join(SQLQueries.SqlAnd, _whereConditions);
        return sql;
    }

    private string GetSQL()
    {
        var sql = GetBaseWhereSQL();

        if (!string.IsNullOrEmpty(_orderByClause))
            sql += " " + _orderByClause;
        if (!string.IsNullOrEmpty(_limitClause))
            sql += " " + _limitClause;
        if (!string.IsNullOrEmpty(_offsetClause))
            sql += _offsetClause;

        return sql;
    }

    private void EnsureSideFilter()
    {
        if (_includeAllFaces) return;

        // Only add if no side filter already exists
        var currentSQL = GetSQL();
        if (!currentSQL.Contains("c.side"))
            _whereConditions.Add(SQLQueries.CondSidePrimary);
    }
}
