namespace AetherVault.Services;

/// <summary>
/// Shared file picker configuration and helpers to avoid duplicating platform-specific types.
/// </summary>
public static class FilePickerHelper
{
    /// <summary>
    /// File type filter for CSV files (collection import, deck import, etc.).
    /// </summary>
    public static FilePickerFileType CsvFileType { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.iOS, ["public.comma-separated-values-text"] },
        { DevicePlatform.Android, ["text/csv", "text/comma-separated-values", "application/csv"] },
        { DevicePlatform.WinUI, [".csv"] },
        { DevicePlatform.MacCatalyst, ["public.comma-separated-values-text"] },
    });

    /// <summary>
    /// Opens the file picker for selecting a CSV file with the given title.
    /// </summary>
    /// <param name="pickerTitle">Title shown in the picker dialog (e.g. "Select a CSV file to import").</param>
    /// <returns>The selected file, or null if the user cancelled.</returns>
    public static async Task<FileResult?> PickCsvFileAsync(string pickerTitle)
    {
        return await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = pickerTitle,
            FileTypes = CsvFileType,
        });
    }
}
