using AetherVault.Core.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>
/// Base class for all ViewModels. Provides common state (busy, status message, view mode)
/// and ensures property changes are notified so XAML bindings update automatically.
/// All ViewModels that back a Page should inherit from this.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    /// <summary>True while an async operation is running; often used to show a spinner or disable buttons.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Message shown in the UI (e.g. status bar). Use StatusIsError to control color.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplayText))]
    public partial string StatusMessage { get; set; } = "";

    /// <summary>When true, status is shown as an error (e.g. red).</summary>
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

    /// <summary>Grid / List / TextOnly. Used by search/collection to switch how cards are displayed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeButtonText))]
    public partial ViewMode ViewMode { get; set; } = ViewMode.Grid;

    /// <summary>Label for the view-mode toggle button (e.g. "☰" for grid).</summary>
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
