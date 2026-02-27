using Android.App;
using Android.Content.PM;

namespace MTGFetchMAUI;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density |
                           // Extra changes that Samsung One UI (S24) can trigger on
                           // minimize/restore — without these the activity recreates,
                           // which shows a fresh LoadingPage and disrupts the live app.
                           ConfigChanges.FontScale | ConfigChanges.Keyboard |
                           ConfigChanges.KeyboardHidden | ConfigChanges.Navigation |
                           ConfigChanges.LayoutDirection)]
public class MainActivity : MauiAppCompatActivity
{
    // Raised when the activity window regains input focus (e.g. after minimize/restore).
    // More reliable than MAUI OnAppearing on Android 14+ (Galaxy S24 / One UI 7),
    // where onWindowFocusChanged fires even when OnAppearing is skipped.
    internal static event EventHandler? WindowFocusGained;

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            WindowFocusGained?.Invoke(this, EventArgs.Empty);
    }
}
