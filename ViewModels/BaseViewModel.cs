using CommunityToolkit.Mvvm.ComponentModel;

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
}
