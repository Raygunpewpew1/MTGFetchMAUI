namespace MTGFetchMAUI.Core;

/// <summary>
/// Centralized search filter state. Holds all possible filter values for a card search.
/// Unused/empty fields are ignored when applying to a search helper.
/// Port of TSearchOptions from SearchOptions.pas.
/// </summary>
public class SearchOptions
{
    // Text filters
    public string NameFilter { get; set; } = "";
    public string TextFilter { get; set; } = "";
    public string ArtistFilter { get; set; } = "";

    // Type filters
    public string TypeFilter { get; set; } = "";
    public string SubtypeFilter { get; set; } = "";
    public string SupertypeFilter { get; set; } = "";

    // Attribute filters
    public string ColorFilter { get; set; } = "";
    public string ColorIdentityFilter { get; set; } = "";
    public List<CardRarity> RarityFilter { get; set; } = [];
    public string SetFilter { get; set; } = "";

    // Numeric filters
    public double CMCMin { get; set; }
    public double CMCMax { get; set; }
    public double CMCExact { get; set; }
    public bool UseCMCRange { get; set; }
    public bool UseCMCExact { get; set; }
    public string PowerFilter { get; set; } = "";
    public string ToughnessFilter { get; set; } = "";

    // Legality filters
    public DeckFormat LegalFormat { get; set; } = DeckFormat.Standard;
    public bool UseLegalFormat { get; set; }

    // Special filters
    public bool PrimarySideOnly { get; set; } = true;
    public bool NoVariations { get; set; }
    public bool IncludeAllFaces { get; set; }

    public void Clear()
    {
        NameFilter = "";
        TextFilter = "";
        ArtistFilter = "";
        TypeFilter = "";
        SubtypeFilter = "";
        SupertypeFilter = "";
        ColorFilter = "";
        ColorIdentityFilter = "";
        RarityFilter = [];
        SetFilter = "";
        CMCMin = 0;
        CMCMax = 0;
        CMCExact = 0;
        UseCMCRange = false;
        UseCMCExact = false;
        PowerFilter = "";
        ToughnessFilter = "";
        LegalFormat = DeckFormat.Standard;
        UseLegalFormat = false;
        PrimarySideOnly = true;
        NoVariations = false;
        IncludeAllFaces = false;
    }

    public bool HasActiveFilters =>
        !string.IsNullOrEmpty(NameFilter) ||
        !string.IsNullOrEmpty(TextFilter) ||
        !string.IsNullOrEmpty(ArtistFilter) ||
        !string.IsNullOrEmpty(TypeFilter) ||
        !string.IsNullOrEmpty(SubtypeFilter) ||
        !string.IsNullOrEmpty(SupertypeFilter) ||
        !string.IsNullOrEmpty(ColorFilter) ||
        !string.IsNullOrEmpty(ColorIdentityFilter) ||
        RarityFilter.Count > 0 ||
        !string.IsNullOrEmpty(SetFilter) ||
        UseCMCRange ||
        UseCMCExact ||
        !string.IsNullOrEmpty(PowerFilter) ||
        !string.IsNullOrEmpty(ToughnessFilter) ||
        UseLegalFormat;

    public static SearchOptions Default() => new();
}
