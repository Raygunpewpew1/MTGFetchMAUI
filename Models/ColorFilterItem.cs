using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherVault.Models;

/// <summary>
/// Bindable color filter for the search filters UI (e.g. W, U, B, R, G, C).
/// </summary>
public partial class ColorFilterItem : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public string Code { get; }

    public ColorFilterItem(string code, bool selected = false)
    {
        Code = code;
        IsSelected = selected;
    }
}
