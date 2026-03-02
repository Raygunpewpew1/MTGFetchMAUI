namespace MTGFetchMAUI.Services;

using Microsoft.Maui.Controls.Shapes;

public interface IToastService
{
    void Show(string message, int durationMs = 3000);
}

public class ToastService : IToastService
{
    public void Show(string message, int durationMs = 3000)
    {
        MainThread.BeginInvokeOnMainThread(() => _ = ShowBannerAsync(message, durationMs));
    }

    private static async Task ShowBannerAsync(string message, int durationMs)
    {
        var page = GetCurrentContentPage();
        if (page == null) return;

        var original = page.Content;

        var banner = new Border
        {
            BackgroundColor = Color.FromArgb("#DD1E1E1E"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(16, 10),
            Margin = new Thickness(16, 8, 16, 0),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            ZIndex = 999,
            Opacity = 0,
            Content = new Label
            {
                Text = message,
                TextColor = Colors.White,
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center,
            }
        };

        var wrapper = new Grid();
        wrapper.Add(original);
        wrapper.Add(banner);
        page.Content = wrapper;

        try
        {
            await banner.FadeToAsync(1, 150);
            await Task.Delay(Math.Max(durationMs - 300, 200));
            await banner.FadeToAsync(0, 150);
        }
        finally
        {
            page.Content = original;
        }
    }

    private static ContentPage? GetCurrentContentPage()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;

        // Walk modal stack to get the topmost visible page
        while (page?.Navigation?.ModalStack is { Count: > 0 } stack)
            page = stack[stack.Count - 1];

        // Unwrap Shell to get the current tab page
        if (page is Shell shell)
            page = shell.CurrentPage;

        return page as ContentPage;
    }
}
