using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MTGFetchMAUI.ViewModels;

/// <summary>
/// Base class for all ViewModels providing INotifyPropertyChanged.
/// </summary>
public abstract class BaseViewModel : INotifyPropertyChanged
{
    public bool IsBusy
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string StatusMessage
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;

        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
