namespace MTGFetchMAUI.Core;

/// <summary>
/// Information about card types, subtypes, and supertypes.
/// Port of TCardTypeInfo from MTGDataTypes.pas and TMTGCardTypeInfo from MTGCore.pas.
/// </summary>
public class CardTypeInfo
{
    public string TypeName { get; set; } = "";
    public List<string> SubTypes { get; set; } = [];
    public List<string> SuperTypes { get; set; } = [];

    public CardTypeInfo() { }

    public CardTypeInfo(string typeName)
    {
        TypeName = typeName;
    }

    public bool HasSubType(string subType) =>
        SubTypes.Any(st => st.Equals(subType, StringComparison.OrdinalIgnoreCase));

    public bool HasSuperType(string superType) =>
        SuperTypes.Any(st => st.Equals(superType, StringComparison.OrdinalIgnoreCase));

    public override string ToString() =>
        $"{TypeName} (Subtypes: {SubTypes.Count}, Supertypes: {SuperTypes.Count})";
}

/// <summary>
/// Collection of MTG metadata (card types, subtypes, supertypes).
/// Port of TMTGDataCollection from MTGDataTypes.pas.
/// </summary>
public class MTGDataCollection
{
    public List<CardTypeInfo> CardTypes { get; set; } = [];
    public DateTime MetaDate { get; set; }
    public string MetaVersion { get; set; } = "";

    public CardTypeInfo GetCardTypeInfo(string typeName)
    {
        return CardTypes.FirstOrDefault(
            ct => ct.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            ?? new CardTypeInfo();
    }

    public string[] GetAllSubTypes()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ct in CardTypes)
            foreach (var st in ct.SubTypes)
                set.Add(st);
        return set.ToArray();
    }

    public string[] GetAllSuperTypes()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ct in CardTypes)
            foreach (var st in ct.SuperTypes)
                set.Add(st);
        return set.ToArray();
    }

    public string[] FindCardTypesWithSubType(string subType) =>
        CardTypes.Where(ct => ct.HasSubType(subType))
                 .Select(ct => ct.TypeName)
                 .ToArray();

    public string[] FindCardTypesWithSuperType(string superType) =>
        CardTypes.Where(ct => ct.HasSuperType(superType))
                 .Select(ct => ct.TypeName)
                 .ToArray();

    public string[] GetSubTypesByCardType(string typeName) =>
        GetCardTypeInfo(typeName).SubTypes.ToArray();

    public string[] GetSuperTypesByCardType(string typeName) =>
        GetCardTypeInfo(typeName).SuperTypes.ToArray();

    public bool IsValidSubType(string cardType, string subType) =>
        GetCardTypeInfo(cardType).HasSubType(subType);

    public bool IsValidSuperType(string cardType, string superType) =>
        GetCardTypeInfo(cardType).HasSuperType(superType);
}
