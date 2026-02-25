using AppoMobi.Maui.Gestures;

namespace MTGFetchMAUI.Controls;

public sealed class SwipeGestureContainer : ContentView, IGestureListener
{
    public event Action? SwipedLeft;
    public event Action? SwipedRight;

    // Minimum horizontal travel (DIPs) required to register a swipe
    private const float MinSwipeDistance = 45f;
    // Movement before direction is committed (DIPs)
    private const float DirectionLockThreshold = 12f;

    private enum Direction { Undecided, Horizontal, Vertical }

    private float _startX, _startY, _currentX;
    private Direction _direction = Direction.Undecided;

    public SwipeGestureContainer()
    {
        // ContentView defaults: InputTransparent=false, IsLayout=true.
        // We want children to receive input, but we also want to intercept gestures.
        // TouchEffect attached to this container will see touches.

        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        TouchEffect.SetForceAttach(this, true);
        TouchEffect.SetShareTouch(this, TouchHandlingStyle.Manual);
    }

    // Explicit implementation to satisfy interface if needed, though ContentView has InputTransparent property.
    // The interface usually just wants to know if it *should* be transparent.
    // We return false because we want to receive touches.
    bool IGestureListener.InputTransparent => false;

    public void OnGestureEvent(
        TouchActionType type,
        TouchActionEventArgs args,
        TouchActionResult action)
    {
        float density = TouchEffect.Density > 0 ? TouchEffect.Density : 1f;
        float x = args.Location.X / density;
        float y = args.Location.Y / density;

        switch (action)
        {
            case TouchActionResult.Down:
                _startX = _currentX = x;
                _startY = y;
                _direction = Direction.Undecided;
                // Reset lock; Allow children to handle touch initially, and parent ScrollView to potentially scroll.
                if (TouchEffect.GetFrom(this) is { } e0)
                    e0.WIllLock = ShareLockState.Initial;
                break;

            case TouchActionResult.Panning:
                _currentX = x;
                if (_direction == Direction.Undecided)
                {
                    float dx = Math.Abs(x - _startX);
                    float dy = Math.Abs(y - _startY);

                    // Only make a decision if we've moved enough to be sure
                    if (dx > DirectionLockThreshold || dy > DirectionLockThreshold)
                    {
                        _direction = dx >= dy ? Direction.Horizontal : Direction.Vertical;
                        var effect = TouchEffect.GetFrom(this);
                        if (effect != null)
                        {
                            if (_direction == Direction.Horizontal)
                            {
                                // Horizontal: We capture the gesture (Lock).
                                // This should prevent the parent ScrollView from scrolling
                                // and (ideally) cancel children interactions.
                                effect.WIllLock = ShareLockState.Locked;
                            }
                            else
                            {
                                // Vertical: We release the gesture (Unlock).
                                // The parent ScrollView should pick this up.
                                effect.WIllLock = ShareLockState.Unlocked;
                            }
                        }
                    }
                }
                break;

            case TouchActionResult.Up:
                if (_direction == Direction.Horizontal)
                {
                    float totalDx = _currentX - _startX;
                    if (Math.Abs(totalDx) >= MinSwipeDistance)
                    {
                        if (totalDx < 0)
                            SwipedLeft?.Invoke();
                        else
                            SwipedRight?.Invoke();
                    }
                    // Release scroll lock for the next gesture.
                    if (TouchEffect.GetFrom(this) is { } e1)
                        e1.WIllLock = ShareLockState.Unlocked;
                }
                _direction = Direction.Undecided;
                break;
        }
    }
}
