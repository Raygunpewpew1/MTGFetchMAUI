using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace MTGFetchMAUI.Services;

public interface IToastService
{
    void Show(string message, int durationMs = 3000);
}

public class ToastService : IToastService
{
    public void Show(string message, int durationMs = 3000)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var duration = durationMs > 2000 ? ToastDuration.Long : ToastDuration.Short;
            var toast = Toast.Make(message, duration);
            await toast.Show();
        });
    }
}
