using AetherVault.Core.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>
/// Base class for all ViewModels providing INotifyPropertyChanged.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplayText))]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTextColor))]
    [NotifyPropertyChangedFor(nameof(StatusDisplayText))]
    public partial bool StatusIsError { get; set; }

    public Color StatusTextColor => StatusIsError
        ? Color.FromArgb("#F44336")
        : Color.FromArgb("#888888");

    public string StatusDisplayText => StatusIsError ? $"⚠ {StatusMessage}" : StatusMessage;

    [ObservableProperty]
    public partial bool IsImportingPrices { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeButtonText))]
    public partial ViewMode ViewMode { get; set; } = ViewMode.Grid;

    public string ViewModeButtonText => ViewMode switch
    {
        ViewMode.Grid => "☰",
        ViewMode.List => "≣",
        _ => "⊞"
    };

    [RelayCommand]
    private void ToggleViewMode()
    {
        ViewMode = ViewMode switch
        {
            ViewMode.Grid => ViewMode.List,
            ViewMode.List => ViewMode.TextOnly,
            _ => ViewMode.Grid
        };
    }

    partial void OnViewModeChanged(ViewMode value) => OnViewModeUpdated(value);

    /// <summary>
    /// Override in subclasses to react to view mode changes (e.g. update a card grid).
    /// </summary>
    protected virtual void OnViewModeUpdated(ViewMode value) { }
}
