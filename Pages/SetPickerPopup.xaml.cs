using AetherVault.Core;
using CommunityToolkit.Maui.Views;
using System.Collections.ObjectModel;

namespace AetherVault.Pages;

public partial class SetPickerPopup : Popup
{
    private readonly IReadOnlyList<SetInfo> _allSets;
    private readonly Action<int> _onSelected;
    private readonly ObservableCollection<SetInfo> _filteredSets = [];

    public SetPickerPopup(IReadOnlyList<SetInfo> sets, int initialIndex, Action<int> onSelected)
    {
        InitializeComponent();
        _allSets = sets;
        _onSelected = onSelected;

        ApplyFilter("");
        SearchEntry.TextChanged += OnSearchTextChanged;
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter(e.NewTextValue ?? "");
    }

    private void ApplyFilter(string search)
    {
        _filteredSets.Clear();
        var q = search.Trim();
        if (string.IsNullOrEmpty(q))
        {
            foreach (var s in _allSets)
                _filteredSets.Add(s);
        }
        else
        {
            var lower = q.ToLowerInvariant();
            foreach (var s in _allSets)
            {
                if (s.Name.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                    s.Code.Contains(lower, StringComparison.OrdinalIgnoreCase))
                    _filteredSets.Add(s);
            }
        }

        SetsList.ItemsSource = null;
        SetsList.ItemsSource = _filteredSets;
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0) return;

        if (e.CurrentSelection[0] is SetInfo selected)
        {
            var idx = _allSets.ToList().FindIndex(s => s.Code == selected.Code);
            if (idx >= 0)
            {
                _onSelected(idx);
                await CloseAsync();
            }
        }
    }

}
