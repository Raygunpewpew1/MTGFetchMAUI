using AetherVault.ViewModels;
using System.Collections.Specialized;

namespace AetherVault.Pages;

public partial class LogViewPage : ContentPage
{
    private readonly LogViewViewModel _vm;

    public LogViewPage(LogViewViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.LogBuffer.Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _vm.AutoScroll)
            MainThread.BeginInvokeOnMainThread(() => LogScroll.ScrollToAsync(LogEnd, ScrollToPosition.End, false));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.LogBuffer.Entries.Count > 0)
            LogScroll.ScrollToAsync(LogEnd, ScrollToPosition.End, false);
    }

}
