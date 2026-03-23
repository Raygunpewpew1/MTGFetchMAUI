using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for the Search Filters modal. Holds all filter state and builds SearchOptions.
/// Configure with target and CardManager via <see cref="Configure"/> before use.
/// </summary>
public partial class SearchFiltersViewModel : BaseViewModel
{
    private ISearchFilterTarget? _target;
    private CardManager? _cardManager;

    private static readonly string[] ColorCodes = ["W", "U", "B", "R", "G", "C"];
    private static readonly SetInfo AnySet = new("", "Any set");

    private static readonly string[] TypeOptionsSource =
    [
        "Any", "Artifact", "Battle", "Creature", "Enchantment", "Instant",
        "Land", "Planeswalker", "Sorcery", "Kindred"
    ];

    /// <summary>Raised when Apply or Cancel is used so the host can close the modal.</summary>
    public event Action? RequestClose;

    public SearchFiltersViewModel()
    {
        var formatList = new List<string> { "Any Format" };
        foreach (DeckFormat fmt in Enum.GetValues<DeckFormat>())
            formatList.Add(fmt.ToDisplayName());
        FormatOptions = formatList;

        TypeOptions = [.. TypeOptionsSource];
        SetList = [AnySet];
        SetList.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectedSetDisplayName));
        ColorFilters = new ObservableCollection<ColorFilterItem>(
            ColorCodes.Select(c => new ColorFilterItem(c, false)));
    }

    public IList<string> FormatOptions { get; }
    public IList<string> TypeOptions { get; }
    public ObservableCollection<SetInfo> SetList { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CmcMinLabel), nameof(CmcMaxLabel), nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private double _cmcMin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CmcMinLabel), nameof(CmcMaxLabel), nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private double _cmcMax = 16;

    public string CmcMinLabel => $"Min: {(int)CmcMin}";
    public string CmcMaxLabel => CmcMax >= 16 ? "Max: 16+" : $"Max: {(int)CmcMax}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private int _selectedTypeIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private int _selectedFormatIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText), nameof(SelectedSetDisplayName))]
    private int _selectedSetIndex;

    /// <summary>Display name of the selected set for the Set picker button.</summary>
    public string SelectedSetDisplayName =>
        SelectedSetIndex >= 0 && SelectedSetIndex < SetList.Count
            ? SetList[SelectedSetIndex].Name
            : "Any set";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private string _keywords = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private string _subtype = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private string _supertype = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private string _power = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private string _toughness = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private string _artist = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkCommon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkUncommon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkRare;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkMythic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkPrimarySide = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkNoVariations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkIncludeTokens;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkCommanderOnly;

    public ObservableCollection<ColorFilterItem> ColorFilters { get; }

    /// <summary>Number of active filters for the sticky header badge.</summary>
    public int ActiveFilterCount => BuildSearchOptions().ActiveFilterCount;

    /// <summary>True when any filters are active; used for summary row visibility.</summary>
    public bool HasActiveFilters => ActiveFilterCount > 0;

    /// <summary>Short summary of active filters for the sticky header.</summary>
    public string FiltersSummaryText => BuildFiltersSummary(BuildSearchOptions());

    /// <summary>Call before showing the page. Loads sets and applies current options from the target.</summary>
    public void Configure(ISearchFilterTarget target, CardManager cardManager)
    {
        _target = target;
        _cardManager = cardManager;
        LoadFromOptions(target.CurrentOptions);
        _ = LoadSetsAsync();
    }

    partial void OnCmcMinChanged(double value)
    {
        if (value > CmcMax)
            CmcMax = value;
    }

    partial void OnCmcMaxChanged(double value)
    {
        if (value < CmcMin)
            CmcMin = value;
    }

    [RelayCommand]
    private void ToggleColor(string code)
    {
        var item = ColorFilters.FirstOrDefault(c => c.Code == code);
        if (item != null)
        {
            item.IsSelected = !item.IsSelected;
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(HasActiveFilters));
            OnPropertyChanged(nameof(FiltersSummaryText));
        }
    }

    [RelayCommand]
    private void AdjustCmc(string? which)
    {
        switch (which)
        {
            case "min-":
                if (CmcMin > 0)
                    CmcMin--;
                break;
            case "min+":
                if (CmcMin < 16)
                    CmcMin++;
                break;
            case "max-":
                if (CmcMax > 0)
                    CmcMax--;
                break;
            case "max+":
                if (CmcMax < 16)
                    CmcMax++;
                break;
        }
    }

    [RelayCommand]
    private void ToggleRarity(string? key)
    {
        switch (key)
        {
            case "C":
                ChkCommon = !ChkCommon;
                break;
            case "U":
                ChkUncommon = !ChkUncommon;
                break;
            case "R":
                ChkRare = !ChkRare;
                break;
            case "M":
                ChkMythic = !ChkMythic;
                break;
        }
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (_target == null) return;
        var options = BuildSearchOptions();
        options.NameFilter = _target.SearchText ?? "";
        await _target.ApplyFiltersAndSearchAsync(options);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Reset()
    {
        LoadFromOptions(new SearchOptions());
    }

    [RelayCommand]
    private void ClearAll()
    {
        Reset();
    }

    public async Task LoadSetsAsync()
    {
        if (_cardManager == null || _target == null) return;
        try
        {
            var sets = await _cardManager.GetAllSetsAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SetList.Clear();
                SetList.Add(AnySet);
                foreach (var s in sets)
                    SetList.Add(s);

                var currentSet = _target.CurrentOptions.SetFilter;
                if (string.IsNullOrEmpty(currentSet))
                    SelectedSetIndex = 0;
                else
                {
                    var idx = SetList.ToList().FindIndex(s => s.Code.Equals(currentSet, StringComparison.OrdinalIgnoreCase));
                    SelectedSetIndex = idx >= 0 ? idx : 0;
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"SearchFilters: could not load sets: {ex.Message}", LogLevel.Warning);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SetList.Clear();
                SetList.Add(AnySet);
                SelectedSetIndex = 0;
            });
        }
    }

    private SearchOptions BuildSearchOptions()
    {
        var options = new SearchOptions();

        var selectedColors = ColorFilters.Where(c => c.IsSelected).Select(c => c.Code).ToList();
        if (selectedColors.Count > 0)
            options.ColorFilter = string.Join(", ", selectedColors);

        options.TextFilter = Keywords ?? "";
        if (SelectedTypeIndex > 0 && SelectedTypeIndex <= TypeOptions.Count)
            options.TypeFilter = TypeOptions[SelectedTypeIndex] ?? "";
        options.SubtypeFilter = Subtype ?? "";
        options.SupertypeFilter = Supertype ?? "";

        if (ChkCommon) options.RarityFilter.Add(CardRarity.Common);
        if (ChkUncommon) options.RarityFilter.Add(CardRarity.Uncommon);
        if (ChkRare) options.RarityFilter.Add(CardRarity.Rare);
        if (ChkMythic) options.RarityFilter.Add(CardRarity.Mythic);

        if (CmcMin > 0 || CmcMax < 16)
        {
            options.UseCmcRange = true;
            options.CmcMin = (int)CmcMin;
            options.CmcMax = (int)CmcMax;
        }

        options.PowerFilter = Power ?? "";
        options.ToughnessFilter = Toughness ?? "";

        if (SelectedFormatIndex > 0)
        {
            options.UseLegalFormat = true;
            options.LegalFormat = (DeckFormat)(SelectedFormatIndex - 1);
        }

        options.SetFilter = SelectedSetIndex >= 0 && SelectedSetIndex < SetList.Count
            ? SetList[SelectedSetIndex].Code
            : "";
        options.ArtistFilter = Artist ?? "";

        options.PrimarySideOnly = ChkPrimarySide;
        options.NoVariations = ChkNoVariations;
        options.IncludeTokens = ChkIncludeTokens;
        options.CommanderOnly = ChkCommanderOnly;

        return options;
    }

    private void LoadFromOptions(SearchOptions options)
    {
        var colors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(options.ColorFilter))
        {
            foreach (var c in options.ColorFilter.Split(','))
                colors.Add(c.Trim());
        }

        foreach (var item in ColorFilters)
            item.IsSelected = colors.Contains(item.Code);

        Keywords = options.TextFilter ?? "";
        Subtype = options.SubtypeFilter ?? "";
        Supertype = options.SupertypeFilter ?? "";
        Power = options.PowerFilter ?? "";
        Toughness = options.ToughnessFilter ?? "";
        Artist = options.ArtistFilter ?? "";

        if (string.IsNullOrEmpty(options.TypeFilter) || options.TypeFilter.Equals("Any", StringComparison.OrdinalIgnoreCase))
            SelectedTypeIndex = 0;
        else
        {
            var idx = Array.FindIndex(TypeOptionsSource, s => string.Equals(s, options.TypeFilter, StringComparison.OrdinalIgnoreCase));
            SelectedTypeIndex = idx >= 0 ? idx : 0;
        }

        ChkCommon = options.RarityFilter.Contains(CardRarity.Common);
        ChkUncommon = options.RarityFilter.Contains(CardRarity.Uncommon);
        ChkRare = options.RarityFilter.Contains(CardRarity.Rare);
        ChkMythic = options.RarityFilter.Contains(CardRarity.Mythic);

        if (options.UseCmcRange)
        {
            CmcMin = options.CmcMin;
            CmcMax = options.CmcMax;
        }
        else if (options.UseCmcExact)
        {
            CmcMin = options.CmcExact;
            CmcMax = options.CmcExact;
        }
        else
        {
            CmcMin = 0;
            CmcMax = 16;
        }

        SelectedFormatIndex = options.UseLegalFormat ? (int)options.LegalFormat + 1 : 0;

        if (SetList.Count > 0)
        {
            var idx = string.IsNullOrEmpty(options.SetFilter)
                ? 0
                : SetList.ToList().FindIndex(s => s.Code.Equals(options.SetFilter, StringComparison.OrdinalIgnoreCase));
            SelectedSetIndex = idx >= 0 ? idx : 0;
        }

        ChkPrimarySide = options.PrimarySideOnly;
        ChkNoVariations = options.NoVariations;
        ChkIncludeTokens = options.IncludeTokens;
        ChkCommanderOnly = options.CommanderOnly;
    }

    private static string BuildFiltersSummary(SearchOptions options)
    {
        var parts = new List<string>();
        AddTextAndTypeSummary(parts, options);
        AddColorAndRaritySummary(parts, options);
        AddCmcSummary(parts, options);
        AddPowerToughnessSummary(parts, options);
        AddFormatSetArtistSummary(parts, options);
        AddSpecialSummary(parts, options);

        if (parts.Count == 0)
            return string.Empty;

        var summary = string.Join(" • ", parts);
        return summary.Length <= 120 ? summary : summary[..120] + "…";
    }

    private static void AddTextAndTypeSummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TextFilter))
            parts.Add($"Text: \"{options.TextFilter}\"");

        if (!string.IsNullOrWhiteSpace(options.TypeFilter) &&
            !options.TypeFilter.Equals("Any", StringComparison.OrdinalIgnoreCase))
            parts.Add($"Type: {options.TypeFilter}");

        if (!string.IsNullOrWhiteSpace(options.SubtypeFilter))
            parts.Add($"Subtype: {options.SubtypeFilter}");

        if (!string.IsNullOrWhiteSpace(options.SupertypeFilter))
            parts.Add($"Supertype: {options.SupertypeFilter}");
    }

    private static void AddColorAndRaritySummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ColorFilter))
            parts.Add($"Colors: {ColorFilterDisplay.ToDisplayString(options.ColorFilter)}");

        if (options.RarityFilter.Count > 0)
            parts.Add($"Rarity: {string.Join("/", options.RarityFilter)}");
    }

    private static void AddCmcSummary(List<string> parts, SearchOptions options)
    {
        if (options.UseCmcRange)
            parts.Add($"CMC: {options.CmcMin}-{options.CmcMax}");
        else if (options.UseCmcExact)
            parts.Add($"CMC: {options.CmcExact}");
    }

    private static void AddPowerToughnessSummary(List<string> parts, SearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PowerFilter))
            parts.Add($"Power: {options.PowerFilter}");

        if (!string.IsNullOrWhiteSpace(options.ToughnessFilter))
            parts.Add($"Toughness: {options.ToughnessFilter}");
    }

    private static void AddFormatSetArtistSummary(List<string> parts, SearchOptions options)
    {
        if (options.UseLegalFormat)
            parts.Add($"Format: {options.LegalFormat.ToDisplayName()}");

        if (!string.IsNullOrWhiteSpace(options.SetFilter))
            parts.Add($"Set: {options.SetFilter}");

        if (!string.IsNullOrWhiteSpace(options.ArtistFilter))
            parts.Add($"Artist: {options.ArtistFilter}");
    }

    private static void AddSpecialSummary(List<string> parts, SearchOptions options)
    {
        if (options.NoVariations)
            parts.Add("No variations");

        if (options.IncludeTokens)
            parts.Add("Include tokens");

        if (options.CommanderOnly)
            parts.Add("Can be commander only");
    }
}
