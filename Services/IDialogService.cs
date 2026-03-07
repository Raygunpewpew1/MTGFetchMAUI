namespace AetherVault.Services;

/// <summary>
/// Abstracts display of alerts and confirmations so ViewModels do not depend on the current Page.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows an alert with two buttons; returns true if the user chose the accept button.
    /// </summary>
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);

    /// <summary>
    /// Shows an alert with a single OK button.
    /// </summary>
    Task DisplayAlertAsync(string title, string message, string cancel);
}
