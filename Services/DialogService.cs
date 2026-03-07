namespace AetherVault.Services;

/// <summary>
/// Uses the current application window's Page to show alerts.
/// </summary>
public sealed class DialogService : IDialogService
{
    public async Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return false;
        return await page.DisplayAlertAsync(title, message, accept, cancel);
    }

    public async Task DisplayAlertAsync(string title, string message, string cancel)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;
        await page.DisplayAlertAsync(title, message, cancel);
    }
}
