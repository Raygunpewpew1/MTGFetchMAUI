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
        ColorFilters = new ObservableCollection<ColorFilterItem>(
            ColorCodes.Select(c => new ColorFilterItem(c, false)));
    }

    public IList<string> FormatOptions { get; }
    public IList<string> TypeOptions { get; }
    public ObservableCollection<SetInfo> SetList { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CmcMinLabel))]
    [NotifyPropertyChangedFor(nameof(CmcMaxLabel))]
    private double _cmcMin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CmcMinLabel))]
    [NotifyPropertyChangedFor(nameof(CmcMaxLabel))]
    private double _cmcMax = 16;

    public string CmcMinLabel => $"Min: {(int)CmcMin}";
    public string CmcMaxLabel => CmcMax >= 16 ? "Max: 16+" : $"Max: {(int)CmcMax}";

    [ObservableProperty]
    private int _selectedTypeIndex;

    [ObservableProperty]
    private int _selectedFormatIndex;

    [ObservableProperty]
    private int _selectedSetIndex;

    [ObservableProperty]
    private string _keywords = "";

    [ObservableProperty]
    private string _subtype = "";

    [ObservableProperty]
    private string _supertype = "";

    [ObservableProperty]
    private string _power = "";

    [ObservableProperty]
    private string _toughness = "";

    [ObservableProperty]
    private string _artist = "";

    [ObservableProperty]
    private bool _chkCommon;

    [ObservableProperty]
    private bool _chkUncommon;

    [ObservableProperty]
    private bool _chkRare;

    [ObservableProperty]
    private bool _chkMythic;

    [ObservableProperty]
    private bool _chkPrimarySide = true;

    [ObservableProperty]
    private bool _chkNoVariations;

    [ObservableProperty]
    private bool _chkIncludeTokens;

    [ObservableProperty]
    private bool _chkCommanderOnly;

    public ObservableCollection<ColorFilterItem> ColorFilters { get; }

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
            item.IsSelected = !item.IsSelected;
    }

    [RelayCommand]
    private async Task ApplyAsync()
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
            options.UseCMCRange = true;
            options.CMCMin = (int)CmcMin;
            options.CMCMax = (int)CmcMax;
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

        if (options.UseCMCRange)
        {
            CmcMin = options.CMCMin;
            CmcMax = options.CMCMax;
        }
        else if (options.UseCMCExact)
        {
            CmcMin = options.CMCExact;
            CmcMax = options.CMCExact;
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
}
