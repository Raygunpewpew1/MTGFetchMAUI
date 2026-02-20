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
        _selectedColors.Clear();
        foreach (var child in ColorButtons.Children)
        {
            if (child is Button btn) { btn.Opacity = 0.5; btn.Scale = 1.0; }
        }

        KeywordsEntry.Text = "";
        TypePicker.SelectedIndex = 0;
        SubtypeEntry.Text = "";
        SupertypeEntry.Text = "";
        ChkCommon.IsChecked = false;
        ChkUncommon.IsChecked = false;
        ChkRare.IsChecked = false;
        ChkMythic.IsChecked = false;
        CMCMinSlider.Value = 0;
        CMCMaxSlider.Value = 16;
        PowerEntry.Text = "";
        ToughnessEntry.Text = "";
        FormatPicker.SelectedIndex = 0;
        SetEntry.Text = "";
        ArtistEntry.Text = "";
        ChkPrimarySide.IsChecked = true;
        ChkNoVariations.IsChecked = false;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
