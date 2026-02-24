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

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnTapped;
        spacer.GestureRecognizers.Add(tapGesture);

        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        pointerGesture.PointerMoved += OnPointerMoved;
        pointerGesture.PointerExited += OnPointerExited;
        spacer.GestureRecognizers.Add(pointerGesture);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_longPressHandled) return;
        var point = e.GetPosition(_spacer);
        if (point == null) return;

        var (id, _) = _hitTest((float)point.Value.X, (float)point.Value.Y);
        if (id != null) Tapped?.Invoke(id);
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(_spacer);
        if (point == null) return;

        _pressPoint = point.Value;
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

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(_spacer);
        if (point == null) return;

        switch (_gestureState)
        {
            case GestureState.PressTracking:
                // Cancel long-press if pointer drifts (allows native scroll)
                if (Math.Abs(point.Value.X - _pressPoint.X) > 10 ||
                    Math.Abs(point.Value.Y - _pressPoint.Y) > 10)
                {
                    _gestureState = GestureState.Idle;
                    _longPressTimer?.Stop();
                }
                break;

            case GestureState.DragArmed:
                // Transition to dragging once the finger moves from the hold point
                if (Math.Abs(point.Value.X - _pressPoint.X) > 8 ||
                    Math.Abs(point.Value.Y - _pressPoint.Y) > 8)
                {
                    var uuid = _armedUuid!;
                    var index = _armedIndex;
                    _gestureState = GestureState.Dragging;
                    MainThread.BeginInvokeOnMainThread(() => DragStarted?.Invoke(uuid, index));
                    MainThread.BeginInvokeOnMainThread(() =>
                        DragMoved?.Invoke((float)point.Value.X, (float)point.Value.Y));
                }
                break;

            case GestureState.Dragging:
                MainThread.BeginInvokeOnMainThread(() =>
                    DragMoved?.Invoke((float)point.Value.X, (float)point.Value.Y));
                break;
        }
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        switch (_gestureState)
        {
            case GestureState.DragArmed:
                // Long-press without drag → open quantity sheet
                var uuid = _armedUuid;
                _gestureState = GestureState.Idle;
                _longPressTimer?.Stop();
                if (uuid != null)
                    MainThread.BeginInvokeOnMainThread(() => LongPressed?.Invoke(uuid));
                break;

            case GestureState.Dragging:
                _gestureState = GestureState.Idle;
                MainThread.BeginInvokeOnMainThread(() => DragEnded?.Invoke());
                break;

            default:
                _gestureState = GestureState.Idle;
                _longPressTimer?.Stop();
                break;
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_gestureState == GestureState.Dragging)
        {
            _gestureState = GestureState.Idle;
            MainThread.BeginInvokeOnMainThread(() => DragCancelled?.Invoke());
        }
        else
        {
            _gestureState = GestureState.Idle;
            _longPressTimer?.Stop();
        }
    }
}
