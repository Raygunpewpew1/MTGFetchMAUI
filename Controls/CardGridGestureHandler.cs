namespace MTGFetchMAUI.Controls;

internal sealed class CardGridGestureHandler
{
    public event Action<string>? Tapped;
    public event Action<string>? LongPressed;

    // Drag-and-drop events
    public event Action<string, int>? DragStarted;   // (uuid, sourceIndex)
    public event Action<float, float>? DragMoved;    // (canvasX, canvasY)
    public event Action? DragEnded;
    public event Action? DragCancelled;

    // Platform callbacks to manage ScrollView interception (wired on Android).
    // DisallowScrollIntercept is invoked when the drag arms so the ScrollView
    // stops trying to intercept subsequent touch events.
    internal Action? DisallowScrollIntercept;
    internal Action? AllowScrollIntercept;

    private enum GestureState { Idle, PressTracking, DragArmed, Dragging }

    private IDispatcherTimer? _longPressTimer;
    private Point _pressPoint;
    private GestureState _gestureState = GestureState.Idle;
    private bool _longPressHandled;
    private string? _armedUuid;
    private int _armedIndex;

    private readonly BoxView _spacer;
    private readonly IDispatcher _dispatcher;
    private readonly Func<float, float, (string? uuid, int index)> _hitTest;

    public CardGridGestureHandler(BoxView spacer, IDispatcher dispatcher, Func<float, float, (string? uuid, int index)> hitTest)
    {
        _spacer = spacer;
        _dispatcher = dispatcher;
        _hitTest = hitTest;

#if !ANDROID
        // On non-Android platforms use MAUI gesture recognizers (reliable for
        // mouse / stylus input on Windows etc.).
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnTapped;
        spacer.GestureRecognizers.Add(tapGesture);

        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed  += (s, e) => { var p = e.GetPosition(_spacer); if (p != null) HandleDown((float)p.Value.X, (float)p.Value.Y); };
        pointerGesture.PointerMoved    += (s, e) => { var p = e.GetPosition(_spacer); if (p != null) HandleMove((float)p.Value.X, (float)p.Value.Y); };
        pointerGesture.PointerReleased += (s, e) => HandleUp();
        pointerGesture.PointerExited   += (s, e) => HandleCancel();
        spacer.GestureRecognizers.Add(pointerGesture);
#endif
        // On Android a native View.OnTouchListener is attached from
        // CardGrid.OnLoaded() once the platform view is available.
        // Taps are surfaced via HandleUp() in that path, so no TapGestureRecognizer
        // is needed on Android (and adding one could conflict with the listener).
    }

    // ── Non-Android tap handler (TapGestureRecognizer path) ───────────────────
#if !ANDROID
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_longPressHandled) return;
        var point = e.GetPosition(_spacer);
        if (point == null) return;
        var (id, _) = _hitTest((float)point.Value.X, (float)point.Value.Y);
        if (id != null) Tapped?.Invoke(id);
    }
#endif

    // ── Platform-agnostic gesture state machine ───────────────────────────────

    internal void HandleDown(float x, float y)
    {
        _pressPoint = new Point(x, y);
        _gestureState = GestureState.PressTracking;
        _longPressHandled = false;
        _armedUuid = null;
        _armedIndex = -1;

        _longPressTimer?.Stop();
        _longPressTimer = _dispatcher.CreateTimer();
        _longPressTimer.Interval = TimeSpan.FromMilliseconds(500);
        _longPressTimer.IsRepeating = false;
        _longPressTimer.Tick += (s, args) =>
        {
            if (_gestureState == GestureState.PressTracking)
            {
                var (uuid, index) = _hitTest((float)_pressPoint.X, (float)_pressPoint.Y);
                if (uuid != null)
                {
                    _armedUuid = uuid;
                    _armedIndex = index;
                    _longPressHandled = true;
                    _gestureState = GestureState.DragArmed;
                    // Tell the ScrollView not to intercept subsequent moves so the
                    // drag gesture can proceed without the scroll view stealing events.
                    DisallowScrollIntercept?.Invoke();
                    try { HapticFeedback.Perform(HapticFeedbackType.LongPress); } catch { }
                }
                else
                {
                    _gestureState = GestureState.Idle;
                }
            }
        };
        _longPressTimer.Start();
    }

    internal void HandleMove(float x, float y)
    {
        switch (_gestureState)
        {
            case GestureState.PressTracking:
                // Cancel long-press if pointer drifts (lets the ScrollView scroll)
                if (Math.Abs(x - _pressPoint.X) > 10 || Math.Abs(y - _pressPoint.Y) > 10)
                {
                    _gestureState = GestureState.Idle;
                    _longPressTimer?.Stop();
                }
                break;

            case GestureState.DragArmed:
                // Transition to dragging once the finger moves from the hold point
                if (Math.Abs(x - _pressPoint.X) > 8 || Math.Abs(y - _pressPoint.Y) > 8)
                {
                    var uuid = _armedUuid!;
                    var index = _armedIndex;
                    _gestureState = GestureState.Dragging;
                    MainThread.BeginInvokeOnMainThread(() => DragStarted?.Invoke(uuid, index));
                    MainThread.BeginInvokeOnMainThread(() => DragMoved?.Invoke(x, y));
                }
                break;

            case GestureState.Dragging:
                MainThread.BeginInvokeOnMainThread(() => DragMoved?.Invoke(x, y));
                break;
        }
    }

    internal void HandleUp()
    {
        switch (_gestureState)
        {
#if ANDROID
            case GestureState.PressTracking:
                // Quick tap on Android (TapGestureRecognizer is not used in this path).
                _gestureState = GestureState.Idle;
                _longPressTimer?.Stop();
                var tapPoint = _pressPoint;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var (uuid, _) = _hitTest((float)tapPoint.X, (float)tapPoint.Y);
                    if (uuid != null) Tapped?.Invoke(uuid);
                });
                break;
#endif

            case GestureState.DragArmed:
                // Long-press without drag → open quantity sheet
                var armedUuid = _armedUuid;
                _gestureState = GestureState.Idle;
                _longPressTimer?.Stop();
                AllowScrollIntercept?.Invoke();
                if (armedUuid != null)
                    MainThread.BeginInvokeOnMainThread(() => LongPressed?.Invoke(armedUuid));
                break;

            case GestureState.Dragging:
                _gestureState = GestureState.Idle;
                AllowScrollIntercept?.Invoke();
                MainThread.BeginInvokeOnMainThread(() => DragEnded?.Invoke());
                break;

            default:
                _gestureState = GestureState.Idle;
                _longPressTimer?.Stop();
                break;
        }
    }

    internal void HandleCancel()
    {
        if (_gestureState == GestureState.Dragging)
        {
            _gestureState = GestureState.Idle;
            AllowScrollIntercept?.Invoke();
            MainThread.BeginInvokeOnMainThread(() => DragCancelled?.Invoke());
        }
        else
        {
            _gestureState = GestureState.Idle;
            _longPressTimer?.Stop();
        }
    }
}
