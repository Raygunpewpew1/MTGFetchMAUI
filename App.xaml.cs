namespace AetherVault;

using AetherVault.Pages;
using Microsoft.Extensions.DependencyInjection;
using System;

/// <summary>
/// Application root. Creates the first window with LoadingPage, then the shell takes over.
/// Also provides ScheduleResumeRedraw for Android to fix black screen after resume/file picker.
/// </summary>
public partial class App : Application
{
    /// <summary>Delay (ms) so the invalidate runs after the first post-resume frame and transition (logcat: "Start draw after previous draw not visible").</summary>
    private const int ResumeRedrawDelayMs = 220;

    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = (Exception)e.ExceptionObject;
            System.Diagnostics.Debug.WriteLine($"[Unhandled] {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);
        };
    }

    /// <summary>Service provider for controls that need to resolve services (e.g. image loading).</summary>
    public static IServiceProvider? ServiceProvider => (Current as App)?._serviceProvider;

    /// <summary>
    /// Schedules a delayed layout invalidation so a second frame is requested after activity/window focus.
    /// Call from Android MainActivity when WindowFocusGained (or after file picker/modal) to fix black screen
    /// when the first frame was drawn before MAUI content was ready.
    /// </summary>
    public static void ScheduleResumeRedraw()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(ResumeRedrawDelayMs);
            MainThread.BeginInvokeOnMainThread(DoResumeRedraw);
        });
    }

    private static void DoResumeRedraw()
    {
        try
        {
            if (Current?.Windows.Count > 0 && Current.Windows[0].Page is VisualElement root)
            {
                root.InvalidateMeasure();
            }
            var shell = Shell.Current;
            if (shell is VisualElement shellElement)
            {
                shellElement.InvalidateMeasure();
            }
            var page = shell?.CurrentPage;
            if (page is VisualElement pageElement)
            {
                pageElement.InvalidateMeasure();
            }
            if (page is ContentPage contentPage && contentPage.Content is VisualElement contentElement)
            {
                contentElement.InvalidateMeasure();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ResumeRedraw] {ex.Message}");
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var loadingPage = _serviceProvider.GetRequiredService<LoadingPage>();
        // Do not add toast overlay here — it can cause JavaProxyThrowable on Android during first layout.
        // LoadingViewModel creates the overlay when switching to AppShell.
        return new Window(loadingPage);
    }
}
