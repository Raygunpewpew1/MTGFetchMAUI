using System.Globalization;

namespace AetherVault.Converters;

/// <summary>
/// Converts a boolean expanded state to a simple chevron text indicator.
/// true  -> "˄"
/// false -> "˅"
/// </summary>
public sealed class BoolToChevronConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? "˄" : "˅";
        }

        return "˅";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return false;
    }
}

