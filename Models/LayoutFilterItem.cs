using AetherVault.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherVault.Models;

/// <summary>Toggle chip for restricting search by <see cref="CardLayout"/>.</summary>
public partial class LayoutFilterItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public CardLayout Layout { get; }
    public string DisplayName { get; }

    public LayoutFilterItem(CardLayout layout, string displayName, bool selected = false)
    {
        Layout = layout;
        DisplayName = displayName;
        IsSelected = selected;
    }
}
