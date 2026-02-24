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

    // Callbacks wired by GestureSpacerView to dynamically control ScrollView
    // interception via AppoMobi.Maui.Gestures WIllLock.
    internal Action? DisallowScrollIntercept;
    internal Action? AllowScrollIntercept;

    private enum GestureState { Idle, PressTracking, DragArmed, Dragging }

    private IDispatcherTimer? _longPressTimer;
    private Point _pressPoint;
    private GestureState _gestureState = GestureState.Idle;
    private string? _armedUuid;
    private int _armedIndex;

    private readonly IDispatcher _dispatcher;
    private readonly Func<float, float, (string? uuid, int index)> _hitTest;

    public CardGridGestureHandler(IDispatcher dispatcher, Func<float, float, (string? uuid, int index)> hitTest)
    {
        _dispatcher = dispatcher;
        _hitTest = hitTest;
        // Touch events are delivered by GestureSpacerView via OnGestureEvent.
    }

    // ── Platform-agnostic gesture state machine ───────────────────────────────

    internal void HandleDown(float x, float y)
    {
        _pressPoint = new Point(x, y);
        _gestureState = GestureState.PressTracking;
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
            case GestureState.PressTracking:
                // Quick tap: fire Tapped and clear state.
                _gestureState = GestureState.Idle;
                _longPressTimer?.Stop();
                var tapPoint = _pressPoint;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var (uuid, _) = _hitTest((float)tapPoint.X, (float)tapPoint.Y);
                    if (uuid != null) Tapped?.Invoke(uuid);
                });
                break;

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
