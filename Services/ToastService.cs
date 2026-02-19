namespace MTGFetchMAUI.Services;

public interface IToastService
{
    void Show(string message, int durationMs = 3000);
    event Action<string, int>? OnShow;
}

public class ToastService : IToastService
{
    public event Action<string, int>? OnShow;

    public void Show(string message, int durationMs = 3000)
    {
        OnShow?.Invoke(message, durationMs);
    }
}
