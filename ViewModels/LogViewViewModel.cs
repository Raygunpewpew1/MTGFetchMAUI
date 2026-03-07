using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for the in-app debug log viewer tab.
/// </summary>
public partial class LogViewViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _autoScroll = true;

    public ILogBufferService LogBuffer { get; }

    public LogViewViewModel(ILogBufferService logBuffer)
    {
        LogBuffer = logBuffer;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (LogBuffer.Entries.Count == 0)
            return;
        var text = string.Join(Environment.NewLine, LogBuffer.Entries.Select(e => e.Text));
        await Clipboard.Default.SetTextAsync(text);
    }

    [RelayCommand]
    private void Clear()
    {
        LogBuffer.Clear();
    }
}
