namespace AetherVault.Tests.Controls;

/// <summary>Runs dispatcher callbacks synchronously for CardGridGestureHandler unit tests.</summary>
internal sealed class TestDispatcher : IDispatcher
{
    private readonly Queue<(DateTime Due, Action Action)> _delayed = new();
    private readonly List<TestDispatcherTimer> _timers = [];

    public bool IsDispatchRequired => false;

    public bool Dispatch(Action action)
    {
        action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        _delayed.Enqueue((DateTime.UtcNow.Add(delay), action));
        return true;
    }

    public IDispatcherTimer CreateTimer()
    {
        var timer = new TestDispatcherTimer();
        _timers.Add(timer);
        return timer;
    }

    /// <summary>Runs all queued delayed actions (ignores due time — test helper).</summary>
    public void FlushDelayed(int maxPasses = 10)
    {
        for (int pass = 0; pass < maxPasses && _delayed.Count > 0; pass++)
        {
            int count = _delayed.Count;
            for (int i = 0; i < count; i++)
            {
                var (_, action) = _delayed.Dequeue();
                action();
            }
        }
    }

    public void FireLastTimerTick()
    {
        if (_timers.Count == 0)
            throw new InvalidOperationException("No timer was created.");
        _timers[^1].FireTick();
    }

    internal sealed class TestDispatcherTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public TimeSpan? DueTime { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick;

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        public void FireTick() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
