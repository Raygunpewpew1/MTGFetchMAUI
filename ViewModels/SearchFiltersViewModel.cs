using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

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

    private static ObservableCollection<LayoutFilterItem> CreateLayoutFilterItems() =>
        new(
        [
            new LayoutFilterItem(CardLayout.Transform, "Transform"),
            new LayoutFilterItem(CardLayout.ModalDfc, "MDFC"),
            new LayoutFilterItem(CardLayout.Split, "Split"),
            new LayoutFilterItem(CardLayout.Flip, "Flip"),
            new LayoutFilterItem(CardLayout.Adventure, "Adventure"),
            new LayoutFilterItem(CardLayout.Saga, "Saga"),
            new LayoutFilterItem(CardLayout.Meld, "Meld"),
            new LayoutFilterItem(CardLayout.Token, "Token"),
            new LayoutFilterItem(CardLayout.DoubleFacedToken, "DFC token"),
            new LayoutFilterItem(CardLayout.ReversibleCard, "Reversible"),
        ]);

    /// <summary>Raised when Apply or Cancel is used so the host can close the modal.</summary>
    public event Action? RequestClose;

    public SearchFiltersViewModel()
    {
        var formatList = new List<string> { "Any Format" };
        foreach (DeckFormat fmt in Enum.GetValues<DeckFormat>())
            formatList.Add(fmt.ToDisplayName());
        FormatOptions = formatList;

        TypeOptions = [.. TypeOptionsSource];
        SetList.CollectionChanged += OnSetListCollectionChanged;
        ColorFilters = new ObservableCollection<ColorFilterItem>(
            ColorCodes.Select(c => new ColorFilterItem(c, false)));
        ColorIdentityFilters = new ObservableCollection<ColorFilterItem>(
            ColorCodes.Select(c => new ColorFilterItem(c, false)));
        LayoutFilters = CreateLayoutFilterItems();
    }

    public IList<string> FormatOptions { get; }
    public IList<string> TypeOptions { get; }

    [ObservableProperty]
    private ObservableCollection<SetInfo> _setList = new([AnySet]);

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
    private string _rulesText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private string _oracleKeywords = "";

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

    /// <summary>Draft card name while the sheet is open; synced to the host on Apply.</summary>
    [ObservableProperty]
    private string _nameQueryPreview = "";

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
    private bool _chkShowAllPrintings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkIncludeTokens;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkCommanderOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkAvailPaper;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkAvailMtgo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkAvailArena;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkFinishNonfoil;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkFinishFoil;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(HasActiveFilters), nameof(FiltersSummaryText))]
    private bool _chkFinishEtched;

    public ObservableCollection<ColorFilterItem> ColorFilters { get; }
    public ObservableCollection<ColorFilterItem> ColorIdentityFilters { get; }
    public ObservableCollection<LayoutFilterItem> LayoutFilters { get; }

    /// <summary>Number of active filters for the sticky header badge.</summary>
    public int ActiveFilterCount => BuildSearchOptions().ActiveFilterCount;

    /// <summary>True when any filters are active; used for summary row visibility.</summary>
    public bool HasActiveFilters => ActiveFilterCount > 0;

    /// <summary>Short summary of active filters for the sticky header.</summary>
    public string FiltersSummaryText => SearchFiltersSummaryBuilder.Build(BuildSearchOptions());

    /// <summary>Call before showing the page. Loads sets and applies current options from the target.</summary>
    public void Configure(ISearchFilterTarget target, CardManager cardManager)
    {
        _target = target;
        _cardManager = cardManager;
        LoadFromOptions(target.CurrentOptions);
        RefreshNameQueryPreviewFromTarget();
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
    private void ToggleColorIdentity(string code)
    {
        var item = ColorIdentityFilters.FirstOrDefault(c => c.Code == code);
        if (item != null)
        {
            item.IsSelected = !item.IsSelected;
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(HasActiveFilters));
            OnPropertyChanged(nameof(FiltersSummaryText));
        }
    }

    [RelayCommand]
    private void ToggleLayout(object? param)
    {
        if (!TryGetCardLayout(param, out var layout))
            return;
        var item = LayoutFilters.FirstOrDefault(l => l.Layout == layout);
        if (item != null)
        {
            item.IsSelected = !item.IsSelected;
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(HasActiveFilters));
            OnPropertyChanged(nameof(FiltersSummaryText));
        }
    }

    private static bool TryGetCardLayout(object? param, out CardLayout layout)
    {
        switch (param)
        {
            case CardLayout cl:
                layout = cl;
                return true;
            case string s when Enum.TryParse(s, ignoreCase: true, out layout):
                return true;
            case int i when Enum.IsDefined(typeof(CardLayout), i):
                layout = (CardLayout)i;
                return true;
            default:
                layout = default;
                return false;
        }
    }

    [RelayCommand]
    private void ToggleFinish(string? key)
    {
        switch (key)
        {
            case "nonfoil":
                ChkFinishNonfoil = !ChkFinishNonfoil;
                break;
            case "foil":
                ChkFinishFoil = !ChkFinishFoil;
                break;
            case "etched":
                ChkFinishEtched = !ChkFinishEtched;
                break;
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
    private void ToggleAvailability(string? key)
    {
        switch (key)
        {
            case "paper":
                ChkAvailPaper = !ChkAvailPaper;
                break;
            case "mtgo":
                ChkAvailMtgo = !ChkAvailMtgo;
                break;
            case "arena":
                ChkAvailArena = !ChkAvailArena;
                break;
        }
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (_target == null) return;
        var name = NameQueryPreview?.Trim() ?? "";
        _target.SearchText = name;
        var options = BuildSearchOptions();
        options.NameFilter = name;
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
        RefreshNameQueryPreviewFromTarget();
    }

    private void RefreshNameQueryPreviewFromTarget()
    {
        NameQueryPreview = _target?.SearchText?.Trim() ?? "";
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
            var buffer = new List<SetInfo>(sets.Count + 1) { AnySet };
            buffer.AddRange(sets);
            var currentSet = _target.CurrentOptions.SetFilter;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ReplaceSetList(new ObservableCollection<SetInfo>(buffer));
                SelectedSetIndex = FindSetIndexByCode(SetList, currentSet);
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"SearchFilters: could not load sets: {ex.Message}", LogLevel.Warning);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ReplaceSetList(new ObservableCollection<SetInfo> { AnySet });
                SelectedSetIndex = 0;
            });
        }
    }

    private void OnSetListCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(SelectedSetDisplayName));

    private void ReplaceSetList(ObservableCollection<SetInfo> next)
    {
        SetList.CollectionChanged -= OnSetListCollectionChanged;
        SetList = next;
        SetList.CollectionChanged += OnSetListCollectionChanged;
        OnPropertyChanged(nameof(SelectedSetDisplayName));
    }

    private static int FindSetIndexByCode(ObservableCollection<SetInfo> list, string? code)
    {
        if (string.IsNullOrEmpty(code)) return 0;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private SearchOptions BuildSearchOptions()
    {
        var options = new SearchOptions();

        var selectedColors = ColorFilters.Where(c => c.IsSelected).Select(c => c.Code).ToList();
        if (selectedColors.Count > 0)
            options.ColorFilter = string.Join(", ", selectedColors);

        var idColors = ColorIdentityFilters.Where(c => c.IsSelected).Select(c => c.Code).ToList();
        if (idColors.Count > 0)
            options.ColorIdentityFilter = string.Join(", ", idColors);

        options.TextFilter = RulesText ?? "";
        options.KeywordsFilter = OracleKeywords ?? "";
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
        options.ShowAllPrintings = ChkShowAllPrintings;
        options.IncludeTokens = ChkIncludeTokens;
        options.CommanderOnly = ChkCommanderOnly;

        if (ChkAvailPaper) options.AvailabilityFilter.Add("paper");
        if (ChkAvailMtgo) options.AvailabilityFilter.Add("mtgo");
        if (ChkAvailArena) options.AvailabilityFilter.Add("arena");

        foreach (var lf in LayoutFilters.Where(l => l.IsSelected))
            options.LayoutFilter.Add(lf.Layout);

        if (ChkFinishNonfoil) options.FinishesFilter.Add("nonfoil");
        if (ChkFinishFoil) options.FinishesFilter.Add("foil");
        if (ChkFinishEtched) options.FinishesFilter.Add("etched");

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

        var idSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(options.ColorIdentityFilter))
        {
            foreach (var c in options.ColorIdentityFilter.Split(','))
                idSet.Add(c.Trim());
        }

        foreach (var item in ColorIdentityFilters)
            item.IsSelected = idSet.Contains(item.Code);

        RulesText = options.TextFilter ?? "";
        OracleKeywords = options.KeywordsFilter ?? "";
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
            SelectedSetIndex = FindSetIndexByCode(SetList, options.SetFilter);

        ChkPrimarySide = options.PrimarySideOnly;
        ChkNoVariations = options.NoVariations;
        ChkShowAllPrintings = options.ShowAllPrintings;
        ChkIncludeTokens = options.IncludeTokens;
        ChkCommanderOnly = options.CommanderOnly;

        var av = new HashSet<string>(options.AvailabilityFilter, StringComparer.OrdinalIgnoreCase);
        ChkAvailPaper = av.Contains("paper");
        ChkAvailMtgo = av.Contains("mtgo");
        ChkAvailArena = av.Contains("arena");

        var layoutSet = new HashSet<CardLayout>(options.LayoutFilter);
        foreach (var lf in LayoutFilters)
            lf.IsSelected = layoutSet.Contains(lf.Layout);

        var fin = new HashSet<string>(options.FinishesFilter, StringComparer.OrdinalIgnoreCase);
        ChkFinishNonfoil = fin.Contains("nonfoil");
        ChkFinishFoil = fin.Contains("foil");
        ChkFinishEtched = fin.Contains("etched");
    }

}
