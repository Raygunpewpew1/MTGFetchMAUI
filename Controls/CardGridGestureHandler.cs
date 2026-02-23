namespace MTGFetchMAUI.Controls;

internal sealed class CardGridGestureHandler
{
    public event Action<string>? Tapped;
    public event Action<string>? LongPressed;

    private IDispatcherTimer? _longPressTimer;
    private Point _pressPoint;
    private bool _isLongPressing;
    private bool _longPressHandled;

    private readonly BoxView _spacer;
    private readonly IDispatcher _dispatcher;
    private readonly Func<float, float, string?> _hitTest;

    public CardGridGestureHandler(BoxView spacer, IDispatcher dispatcher, Func<float, float, string?> hitTest)
    {
        _spacer = spacer;
        _dispatcher = dispatcher;
        _hitTest = hitTest;

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnTapped;
        spacer.GestureRecognizers.Add(tapGesture);

        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        pointerGesture.PointerMoved += OnPointerMoved;
        pointerGesture.PointerExited += OnPointerReleased;
        spacer.GestureRecognizers.Add(pointerGesture);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_longPressHandled) return;
        var point = e.GetPosition(_spacer);
        if (point == null) return;

        var id = _hitTest((float)point.Value.X, (float)point.Value.Y);
        if (id != null) Tapped?.Invoke(id);
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(_spacer);
        if (point == null) return;

        _pressPoint = point.Value;
        _isLongPressing = true;
        _longPressHandled = false;

        _longPressTimer?.Stop();
        _longPressTimer = _dispatcher.CreateTimer();
        _longPressTimer.Interval = TimeSpan.FromMilliseconds(500);
        _longPressTimer.IsRepeating = false;
        _longPressTimer.Tick += (s, args) =>
        {
            if (_isLongPressing)
            {
                var id = _hitTest((float)_pressPoint.X, (float)_pressPoint.Y);
                if (id != null)
                {
                    _longPressHandled = true;
                    MainThread.BeginInvokeOnMainThread(() => LongPressed?.Invoke(id));
                    try { HapticFeedback.Perform(HapticFeedbackType.LongPress); } catch { }
                }
            }
            _isLongPressing = false;
        };
        _longPressTimer.Start();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isLongPressing) return;
        var point = e.GetPosition(_spacer);
        if (point == null) return;

        if (Math.Abs(point.Value.X - _pressPoint.X) > 10 ||
            Math.Abs(point.Value.Y - _pressPoint.Y) > 10)
        {
            _isLongPressing = false;
            _longPressTimer?.Stop();
        }
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        _isLongPressing = false;
        _longPressTimer?.Stop();
    }
}
