namespace MTGFetchMAUI.Controls;

public partial class Snackbar : ContentView
{
    private CancellationTokenSource? _cts;

    public Snackbar()
    {
        InitializeComponent();
    }

    public async Task ShowAsync(string message, int durationMs = 3000)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            MessageLabel.Text = message;
            IsVisible = true;

            // Slide up and fade in
            await Task.WhenAll(
                Container.TranslateToAsync(0, 0, 300, Easing.CubicOut),
                Container.FadeToAsync(1, 300, Easing.Linear)
            );

            await Task.Delay(durationMs, token);

            // Slide down and fade out
            await Task.WhenAll(
                Container.TranslateToAsync(0, 100, 300, Easing.CubicIn),
                Container.FadeToAsync(0, 300, Easing.Linear)
            );

            if (!token.IsCancellationRequested)
                IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            // Expected when overlapping shows occur
        }
        catch (Exception)
        {
            IsVisible = false;
        }
    }
}
