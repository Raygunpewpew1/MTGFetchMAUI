using MTGFetchMAUI.Core;
using MTGFetchMAUI.ViewModels;

namespace MTGFetchMAUI.Pages;

public partial class SearchFiltersPage : ContentPage
{
    private readonly SearchViewModel _searchViewModel;
    private readonly HashSet<string> _selectedColors = [];

    private static readonly (string code, string name, Color color)[] ColorDefs =
    [
        ("W", "White", Color.FromArgb("#F8E7B9")),
        ("U", "Blue", Color.FromArgb("#0E68AB")),
        ("B", "Black", Color.FromArgb("#150B00")),
        ("R", "Red", Color.FromArgb("#D3202A")),
        ("G", "Green", Color.FromArgb("#00733E")),
        ("C", "Colorless", Color.FromArgb("#CCC2C1")),
    ];

    public SearchFiltersPage(SearchViewModel searchViewModel)
    {
        InitializeComponent();
        _searchViewModel = searchViewModel;

        BuildColorButtons();
        BuildFormatPicker();

        LoadFromOptions(_searchViewModel.CurrentOptions);
    }

    private void BuildColorButtons()
    {
        foreach (var (code, name, color) in ColorDefs)
        {
            var btn = new Button
            {
                Text = code,
                BackgroundColor = color,
                TextColor = code is "W" or "C" ? Colors.Black : Colors.White,
                WidthRequest = 48,
                HeightRequest = 48,
                CornerRadius = 24,
                Margin = new Thickness(4),
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                Opacity = 0.5
            };
            btn.Clicked += (s, e) => ToggleColor(btn, code);
            ColorButtons.Add(btn);
        }
    }

    private void ToggleColor(Button btn, string code)
    {
        if (_selectedColors.Contains(code))
        {
            _selectedColors.Remove(code);
            btn.Opacity = 0.5;
            btn.Scale = 1.0;
        }
        else
        {
            _selectedColors.Add(code);
            btn.Opacity = 1.0;
            btn.Scale = 1.1;
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
        options.SetFilter = SetEntry.Text ?? "";
        options.ArtistFilter = ArtistEntry.Text ?? "";

        // Special
        options.PrimarySideOnly = ChkPrimarySide.IsChecked;
        options.NoVariations = ChkNoVariations.IsChecked;

        return options;
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        var options = BuildSearchOptions();
        // Preserve the existing name filter from the view model
        options.NameFilter = _searchViewModel.SearchText;

        await Shell.Current.GoToAsync("..");
        await _searchViewModel.PerformSearchAsync(options);
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
            if (child is Button btn && btn.Text != null)
            {
                bool isSelected = _selectedColors.Contains(btn.Text);
                btn.Opacity = isSelected ? 1.0 : 0.5;
                btn.Scale = isSelected ? 1.1 : 1.0;
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
        SetEntry.Text = options.SetFilter;
        ArtistEntry.Text = options.ArtistFilter;

        // Special
        ChkPrimarySide.IsChecked = options.PrimarySideOnly;
        ChkNoVariations.IsChecked = options.NoVariations;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
