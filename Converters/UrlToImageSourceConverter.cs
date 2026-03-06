namespace AetherVault.Converters;

/// <summary>
/// Converts a URL string to an ImageSource so MAUI Image can load remote images
/// (e.g. Scryfall CDN). Plain string binding is unreliable for URLs on Android.
/// </summary>
public class UrlToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            return ImageSource.FromUri(new Uri(url));
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
