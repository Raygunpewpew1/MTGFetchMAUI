using AetherVault.Core;

namespace AetherVault.Converters;

/// <summary>
/// Converts a <see cref="LegalityStatus"/> value to its corresponding display color.
/// </summary>
public class LegalityStatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not LegalityStatus status)
            return Application.Current?.Resources["NotLegalGray"] as Color ?? Colors.Gray;

        return status switch
        {
            LegalityStatus.Legal => Application.Current?.Resources["LegalGreen"] as Color ?? Color.FromArgb("#4CAF50"),
            LegalityStatus.Banned => Application.Current?.Resources["BannedRed"] as Color ?? Color.FromArgb("#F44336"),
            LegalityStatus.Restricted => Application.Current?.Resources["RestrictedYellow"] as Color ?? Color.FromArgb("#FFC107"),
            _ => Application.Current?.Resources["NotLegalGray"] as Color ?? Color.FromArgb("#666666"),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
