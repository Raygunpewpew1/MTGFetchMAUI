using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTGFetchMAUI.Core.Layout;

namespace MTGFetchMAUI.ViewModels;

/// <summary>
/// Base class for all ViewModels providing INotifyPropertyChanged.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTextColor))]
    private bool _statusIsError;

    public Color StatusTextColor => StatusIsError
        ? Color.FromArgb("#F44336")
        : Color.FromArgb("#888888");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeButtonText))]
    private ViewMode _viewMode = ViewMode.Grid;

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
