namespace AetherVault.Core;

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
    /// <summary>MTGJSON <c>keywords</c> array (comma-separated; all terms must match).</summary>
    public string KeywordsFilter { get; set; } = "";
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
    public double CmcMin { get; set; }
    public double CmcMax { get; set; }
    public double CmcExact { get; set; }
    public bool UseCmcRange { get; set; }
    public bool UseCmcExact { get; set; }
    public string PowerFilter { get; set; } = "";
    public string ToughnessFilter { get; set; } = "";

    // Legality filters
    public DeckFormat LegalFormat { get; set; } = DeckFormat.Standard;
    public bool UseLegalFormat { get; set; }

    // Special filters
    public bool PrimarySideOnly { get; set; } = true;
    public bool NoVariations { get; set; }
    public bool IncludeAllFaces { get; set; }
    public bool IncludeTokens { get; set; }
    /// <summary>When true, only cards that can be a commander (Legendary Creature or "can be your commander").</summary>
    public bool CommanderOnly { get; set; }

    /// <summary>
    /// MTGJSON <c>availability</c> tokens to require (lowercase: paper, mtgo, arena).
    /// When non-empty, a row matches if it lists <b>any</b> of these platforms.
    /// </summary>
    public List<string> AvailabilityFilter { get; set; } = [];

    /// <summary>When non-empty, layout must be one of these values (OR).</summary>
    public List<CardLayout> LayoutFilter { get; set; } = [];

    /// <summary>MTGJSON <c>finishes</c> tokens (e.g. nonfoil, foil, etched); row matches if it has <b>any</b> selected.</summary>
    public List<string> FinishesFilter { get; set; } = [];

    public void Clear()
    {
        NameFilter = "";
        TextFilter = "";
        KeywordsFilter = "";
        ArtistFilter = "";
        TypeFilter = "";
        SubtypeFilter = "";
        SupertypeFilter = "";
        ColorFilter = "";
        ColorIdentityFilter = "";
        RarityFilter = [];
        SetFilter = "";
        CmcMin = 0;
        CmcMax = 0;
        CmcExact = 0;
        UseCmcRange = false;
        UseCmcExact = false;
        PowerFilter = "";
        ToughnessFilter = "";
        LegalFormat = DeckFormat.Standard;
        UseLegalFormat = false;
        PrimarySideOnly = true;
        NoVariations = false;
        IncludeAllFaces = false;
        IncludeTokens = false;
        CommanderOnly = false;
        AvailabilityFilter = [];
        LayoutFilter = [];
        FinishesFilter = [];
    }

    public int ActiveFilterCount
    {
        get
        {
            int count = 0;
            if (!string.IsNullOrWhiteSpace(NameFilter)) count++;
            if (!string.IsNullOrWhiteSpace(TextFilter)) count++;
            if (!string.IsNullOrWhiteSpace(KeywordsFilter)) count++;
            if (!string.IsNullOrWhiteSpace(TypeFilter) && !TypeFilter.Equals("Any", StringComparison.OrdinalIgnoreCase)) count++;
            if (!string.IsNullOrWhiteSpace(SubtypeFilter)) count++;
            if (!string.IsNullOrWhiteSpace(SupertypeFilter)) count++;
            if (!string.IsNullOrWhiteSpace(ColorFilter)) count++;
            if (!string.IsNullOrWhiteSpace(ColorIdentityFilter)) count++;
            if (RarityFilter.Count > 0) count++;
            if (!string.IsNullOrWhiteSpace(SetFilter)) count++;
            if (UseCmcRange || UseCmcExact) count++;
            if (!string.IsNullOrWhiteSpace(PowerFilter)) count++;
            if (!string.IsNullOrWhiteSpace(ToughnessFilter)) count++;
            if (UseLegalFormat) count++;
            if (!string.IsNullOrWhiteSpace(ArtistFilter)) count++;
            if (IncludeTokens) count++;
            if (CommanderOnly) count++;
            if (AvailabilityFilter.Count > 0) count++;
            if (LayoutFilter.Count > 0) count++;
            if (FinishesFilter.Count > 0) count++;
            return count;
        }
    }

    public bool HasActiveFilters =>
        !string.IsNullOrEmpty(NameFilter) ||
        !string.IsNullOrEmpty(TextFilter) ||
        !string.IsNullOrWhiteSpace(KeywordsFilter) ||
        !string.IsNullOrEmpty(ArtistFilter) ||
        !string.IsNullOrEmpty(TypeFilter) ||
        !string.IsNullOrEmpty(SubtypeFilter) ||
        !string.IsNullOrEmpty(SupertypeFilter) ||
        !string.IsNullOrEmpty(ColorFilter) ||
        !string.IsNullOrEmpty(ColorIdentityFilter) ||
        RarityFilter.Count > 0 ||
        !string.IsNullOrEmpty(SetFilter) ||
        UseCmcRange ||
        UseCmcExact ||
        !string.IsNullOrEmpty(PowerFilter) ||
        !string.IsNullOrEmpty(ToughnessFilter) ||
        UseLegalFormat ||
        IncludeTokens ||
        CommanderOnly ||
        AvailabilityFilter.Count > 0 ||
        LayoutFilter.Count > 0 ||
        FinishesFilter.Count > 0;

    public static SearchOptions Default() => new();
}
