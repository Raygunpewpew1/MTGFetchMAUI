namespace AetherVault.Converters;

/// <summary>
/// Converts <see cref="Core.LegalityStatus"/> to a display color for format legality labels.
/// </summary>
public class LegalityToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not Core.LegalityStatus status)
            return Color.FromArgb("#666666");

        return status switch
        {
            Core.LegalityStatus.Legal => Color.FromArgb("#4CAF50"),
            Core.LegalityStatus.Banned => Color.FromArgb("#F44336"),
            Core.LegalityStatus.Restricted => Color.FromArgb("#FFC107"),
            _ => Color.FromArgb("#666666")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
