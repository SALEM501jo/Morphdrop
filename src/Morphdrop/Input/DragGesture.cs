using Morphdrop.Interop;

namespace Morphdrop.Input;

internal enum DragButton { Left, Right }

internal enum GestureEvent { None, Triggered, DragEnded }

/// <summary>
/// The state machine behind <see cref="DragWatcher"/>, kept free of Win32 calls so it can be unit tested.
/// A mouse button held down and moved past the drag threshold counts as a drag; pressing Shift during a drag
/// triggers the wheel. Shift that is already held when the drag starts does not count, so Explorer's own
/// Shift+drag (force move) keeps working.
/// </summary>
internal sealed class DragGesture
{
    private enum State { Idle, Pressed, Dragging }

    private readonly int _thresholdX;
    private readonly int _thresholdY;
    private State _state;
    private DragButton _button;
    private NativeMethods.POINT _origin;
    private bool _shiftWasDown;

    public DragGesture(int thresholdX, int thresholdY)
    {
        _thresholdX = thresholdX;
        _thresholdY = thresholdY;
    }

    public bool IsIdle => _state == State.Idle;

    public bool IsDragging => _state == State.Dragging;

    public GestureEvent ButtonDown(DragButton button, NativeMethods.POINT point, bool shiftDown)
    {
        // A second button pressed mid-drag is ignored; the same button again means we missed its release.
        if (_state != State.Idle && button != _button)
            return GestureEvent.None;

        bool wasDragging = _state == State.Dragging;
        _state = State.Pressed;
        _button = button;
        _origin = point;
        _shiftWasDown = shiftDown;
        return wasDragging ? GestureEvent.DragEnded : GestureEvent.None;
    }

    public GestureEvent Move(NativeMethods.POINT point, bool shiftDown)
    {
        switch (_state)
        {
            case State.Pressed when Math.Abs(point.X - _origin.X) > _thresholdX || Math.Abs(point.Y - _origin.Y) > _thresholdY:
                _state = State.Dragging;
                return ShiftEdge(shiftDown);
            case State.Pressed:
                _shiftWasDown = shiftDown;
                return GestureEvent.None;
            case State.Dragging:
                return ShiftEdge(shiftDown);
            default:
                return GestureEvent.None;
        }
    }

    public GestureEvent ButtonUp(DragButton button)
    {
        if (_state == State.Idle || button != _button)
            return GestureEvent.None;

        bool wasDragging = _state == State.Dragging;
        _state = State.Idle;
        return wasDragging ? GestureEvent.DragEnded : GestureEvent.None;
    }

    /// <summary>
    /// Called on a timer while dragging: catches Shift pressed while the mouse is still,
    /// and a button release the hook never saw.
    /// </summary>
    public GestureEvent Poll(bool shiftDown, bool anyButtonDown)
    {
        if (_state != State.Dragging)
            return GestureEvent.None;

        if (!anyButtonDown)
        {
            _state = State.Idle;
            return GestureEvent.DragEnded;
        }

        return ShiftEdge(shiftDown);
    }

    private GestureEvent ShiftEdge(bool shiftDown)
    {
        bool pressed = shiftDown && !_shiftWasDown;
        _shiftWasDown = shiftDown;
        return pressed ? GestureEvent.Triggered : GestureEvent.None;
    }
}
