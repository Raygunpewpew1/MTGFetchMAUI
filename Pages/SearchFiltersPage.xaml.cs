using AetherVault.Controls;
using AetherVault.Core;
using AetherVault.Services;
using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class SearchFiltersPage : ContentPage
{
    private readonly ISearchFilterTarget _target;
    private readonly CardManager _cardManager;
    private readonly HashSet<string> _selectedColors = [];
    private List<SetInfo> _setList = [];

    private static readonly string[] ColorCodes = ["W", "U", "B", "R", "G", "C"];
    private static readonly SetInfo AnySet = new("", "Any set");

    public SearchFiltersPage(ISearchFilterTarget target, CardManager cardManager)
    {
        InitializeComponent();
        _target = target;
        _cardManager = cardManager;

        BuildColorButtons();
        BuildFormatPicker();

        LoadFromOptions(_target.CurrentOptions);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadSetsAsync();
    }

    private async Task LoadSetsAsync()
    {
        try
        {
            var sets = await _cardManager.GetAllSetsAsync();
            _setList = [AnySet, .. sets];
            SetPicker.ItemsSource = _setList;

            // Restore selection from current options if we had a set list before
            var currentSet = _target.CurrentOptions.SetFilter;
            if (string.IsNullOrEmpty(currentSet))
                SetPicker.SelectedIndex = 0;
            else
            {
                var idx = _setList.FindIndex(s => s.Code.Equals(currentSet, StringComparison.OrdinalIgnoreCase));
                SetPicker.SelectedIndex = idx >= 0 ? idx : 0;
            }
        }
        catch
        {
            _setList = [AnySet];
            SetPicker.ItemsSource = _setList;
            SetPicker.SelectedIndex = 0;
        }
    }

    private void BuildColorButtons()
    {
        foreach (var code in ColorCodes)
        {
            var symbolView = new ManaSymbolView
            {
                Symbol = code,
                InputTransparent = true,
                VerticalOptions = LayoutOptions.Fill,
                HorizontalOptions = LayoutOptions.Fill
            };

            var container = new Grid
            {
                WidthRequest = 32,
                HeightRequest = 32,
                Margin = new Thickness(4),
                Opacity = 0.5,
                StyleId = code
            };
            container.Children.Add(symbolView);

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => ToggleColor(container, code);
            container.GestureRecognizers.Add(tapGesture);

            ColorButtons.Add(container);
        }
    }

    private void ToggleColor(View view, string code)
    {
        if (_selectedColors.Contains(code))
        {
            _selectedColors.Remove(code);
            view.Opacity = 0.5;
            view.Scale = 1.0;
        }
        else
        {
            _selectedColors.Add(code);
            view.Opacity = 1.0;
            view.Scale = 1.1;
        }
    }

    private void BuildFormatPicker()
    {
        var items = new List<string> { "Any Format" };
        foreach (DeckFormat fmt in Enum.GetValues<DeckFormat>())
            items.Add(fmt.ToDisplayName());
        FormatPicker.ItemsSource = items;
        FormatPicker.SelectedIndex = 0;
    }

    private void OnCMCChanged(object? sender, ValueChangedEventArgs e)
    {
        int min = (int)CMCMinSlider.Value;
        int max = (int)CMCMaxSlider.Value;

        if (min > max)
        {
            if (sender == CMCMinSlider) CMCMinSlider.Value = max;
            else CMCMaxSlider.Value = min;
            return;
        }

        CMCMinLabel.Text = $"Min: {min}";
        CMCMaxLabel.Text = max >= 16 ? "Max: 16+" : $"Max: {max}";
    }

    private SearchOptions BuildSearchOptions()
    {
        var options = new SearchOptions();

        // Colors
        if (_selectedColors.Count > 0)
            options.ColorFilter = string.Join(", ", _selectedColors);

        // Keywords/text
        options.TextFilter = KeywordsEntry.Text ?? "";

        // Type
        if (TypePicker.SelectedIndex > 0)
            options.TypeFilter = TypePicker.SelectedItem?.ToString() ?? "";

        // Subtype / Supertype
        options.SubtypeFilter = SubtypeEntry.Text ?? "";
        options.SupertypeFilter = SupertypeEntry.Text ?? "";

        // Rarity
        if (ChkCommon.IsChecked) options.RarityFilter.Add(CardRarity.Common);
        if (ChkUncommon.IsChecked) options.RarityFilter.Add(CardRarity.Uncommon);
        if (ChkRare.IsChecked) options.RarityFilter.Add(CardRarity.Rare);
        if (ChkMythic.IsChecked) options.RarityFilter.Add(CardRarity.Mythic);

        // CMC
        int cmcMin = (int)CMCMinSlider.Value;
        int cmcMax = (int)CMCMaxSlider.Value;
        if (cmcMin > 0 || cmcMax < 16)
        {
            options.UseCMCRange = true;
            options.CMCMin = cmcMin;
            options.CMCMax = cmcMax;
        }

        // Power/Toughness
        options.PowerFilter = PowerEntry.Text ?? "";
        options.ToughnessFilter = ToughnessEntry.Text ?? "";

        // Format
        if (FormatPicker.SelectedIndex > 0)
        {
            options.UseLegalFormat = true;
            options.LegalFormat = (DeckFormat)(FormatPicker.SelectedIndex - 1);
        }

        // Set/Artist
        options.SetFilter = (SetPicker.SelectedItem as SetInfo)?.Code ?? "";
        options.ArtistFilter = ArtistEntry.Text ?? "";

        // Special
        options.PrimarySideOnly = ChkPrimarySide.IsChecked;
        options.NoVariations = ChkNoVariations.IsChecked;
        options.IncludeTokens = ChkIncludeTokens.IsChecked;

        return options;
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        var options = BuildSearchOptions();
        options.NameFilter = _target.SearchText ?? "";

        await _target.ApplyFiltersAndSearchAsync(options);
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else
            await Shell.Current.GoToAsync("..");
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        LoadFromOptions(new SearchOptions());
    }

    private void LoadFromOptions(SearchOptions options)
    {
        // Colors
        _selectedColors.Clear();
        if (!string.IsNullOrEmpty(options.ColorFilter))
        {
            var colors = options.ColorFilter.Split(',');
            foreach (var c in colors) _selectedColors.Add(c.Trim());
        }

        foreach (var child in ColorButtons.Children)
        {
            if (child is View view && !string.IsNullOrEmpty(view.StyleId))
            {
                bool isSelected = _selectedColors.Contains(view.StyleId);
                view.Opacity = isSelected ? 1.0 : 0.5;
                view.Scale = isSelected ? 1.1 : 1.0;
            }
        }

        // Keywords
        KeywordsEntry.Text = options.TextFilter;

        // Type
        if (string.IsNullOrEmpty(options.TypeFilter) || options.TypeFilter == "Any")
        {
            TypePicker.SelectedIndex = 0;
        }
        else
        {
            var src = TypePicker.ItemsSource as System.Collections.IList;
            if (src != null)
            {
                for (int i = 0; i < src.Count; i++)
                {
                    if (src[i]?.ToString() == options.TypeFilter)
                    {
                        TypePicker.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        // Subtype / Supertype
        SubtypeEntry.Text = options.SubtypeFilter;
        SupertypeEntry.Text = options.SupertypeFilter;

        // Rarity
        ChkCommon.IsChecked = options.RarityFilter.Contains(CardRarity.Common);
        ChkUncommon.IsChecked = options.RarityFilter.Contains(CardRarity.Uncommon);
        ChkRare.IsChecked = options.RarityFilter.Contains(CardRarity.Rare);
        ChkMythic.IsChecked = options.RarityFilter.Contains(CardRarity.Mythic);

        // CMC
        if (options.UseCMCRange)
        {
            CMCMinSlider.Value = options.CMCMin;
            CMCMaxSlider.Value = options.CMCMax;
        }
        else if (options.UseCMCExact)
        {
            CMCMinSlider.Value = options.CMCExact;
            CMCMaxSlider.Value = options.CMCExact;
        }
        else
        {
            CMCMinSlider.Value = 0;
            CMCMaxSlider.Value = 16;
        }

        // Power/Toughness
        PowerEntry.Text = options.PowerFilter;
        ToughnessEntry.Text = options.ToughnessFilter;

        // Format
        FormatPicker.SelectedIndex = options.UseLegalFormat ? (int)options.LegalFormat + 1 : 0;

        // Set/Artist
        if (_setList.Count > 0)
        {
            var idx = string.IsNullOrEmpty(options.SetFilter)
                ? 0
                : _setList.FindIndex(s => s.Code.Equals(options.SetFilter, StringComparison.OrdinalIgnoreCase));
            SetPicker.SelectedIndex = idx >= 0 ? idx : 0;
        }
        ArtistEntry.Text = options.ArtistFilter;

        // Special
        ChkPrimarySide.IsChecked = options.PrimarySideOnly;
        ChkNoVariations.IsChecked = options.NoVariations;
        ChkIncludeTokens.IsChecked = options.IncludeTokens;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else
            await Shell.Current.GoToAsync("..");
    }
}
