using SkiaSharp;

namespace AetherVault.Controls;

/// <summary>
/// Loads and displays a card image using the app's ImageDownloadService (cache + Scryfall CDN).
/// Use in list/grid templates where remote URL binding does not reliably show images (e.g. Android).
/// Bind CardUuid to Card.ImageId (ScryfallId when set, else UUID) so cache keys match the main grid.
/// </summary>
public class CachedCardImage : ContentView
{
    private readonly Image _image;
    private byte[]? _cachedBytes;
    private string? _lastUuid;
    private CancellationTokenSource? _loadCts;

    public static readonly BindableProperty CardUuidProperty = BindableProperty.Create(
        nameof(CardUuid), typeof(string), typeof(CachedCardImage), default(string),
        propertyChanged: OnCardUuidChanged);

    public string? CardUuid
    {
        get => (string?)GetValue(CardUuidProperty);
        set => SetValue(CardUuidProperty, value);
    }

    public CachedCardImage()
    {
        _image = new Image
        {
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        Content = _image;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // When control enters visual tree, Handler is set; retry load if we have an id but hadn't resolved the service yet.
        if (!string.IsNullOrWhiteSpace(CardUuid) && _image.Source == null)
            LoadImageAsync(CardUuid);
    }

    private static void OnCardUuidChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CachedCardImage control)
            control.LoadImageAsync((string?)newValue);
    }

    private static Services.ImageDownloadService? GetImageService(BindableObject bindable)
    {
        if (bindable is VisualElement ve && ve.Handler?.MauiContext?.Services != null)
            return ve.Handler.MauiContext.Services.GetService<Services.ImageDownloadService>();
        return AetherVault.App.ServiceProvider?.GetService<Services.ImageDownloadService>();
    }

    private void LoadImageAsync(string? uuid)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        if (string.IsNullOrWhiteSpace(uuid))
        {
            _lastUuid = null;
            _cachedBytes = null;
            _image.Source = null;
            return;
        }

        if (uuid == _lastUuid && _cachedBytes != null)
        {
            _image.Source = ImageSource.FromStream(() => new MemoryStream(_cachedBytes));
            return;
        }

        _lastUuid = uuid;
        _image.Source = null;

        var downloadService = GetImageService(this);
        if (downloadService == null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (token.IsCancellationRequested) return;

                // Try cache first, then download (small size for list thumbnails)
                var img = await downloadService.GetCachedImageAsync(uuid, "small", "");
                if (img == null && !token.IsCancellationRequested)
                    img = await downloadService.DownloadImageDirectAsync(uuid, "small", "");

                if (img == null || token.IsCancellationRequested)
                    return;

                using (img)
                {
                    var data = img.Encode(SKEncodedImageFormat.Png, 100);
                    if (data == null) return;
                    var bytes = data.ToArray();
                    data.Dispose();

                    if (token.IsCancellationRequested || uuid != _lastUuid)
                        return;

                    _cachedBytes = bytes;
                    var source = ImageSource.FromStream(() => new MemoryStream(_cachedBytes));

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (uuid == _lastUuid)
                            _image.Source = source;
                    });
                }
            }
            catch (OperationCanceledException) { /* Expected when load is cancelled. */ }
            catch (Exception ex)
            {
                Services.Logger.LogStuff($"CachedCardImage load failed for {uuid}: {ex.Message}", Services.LogLevel.Warning);
            }
        }, token);
    }
}
