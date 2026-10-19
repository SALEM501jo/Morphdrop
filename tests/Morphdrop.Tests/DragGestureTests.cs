using Morphdrop.Input;
using Morphdrop.Interop;

namespace Morphdrop.Tests;

public class DragGestureTests
{
    private static readonly NativeMethods.POINT Start = new(100, 100);
    private static readonly NativeMethods.POINT Far = new(160, 100);

    private static DragGesture Dragging(bool shiftAtStart = false)
    {
        var gesture = new DragGesture(4, 4);
        gesture.ButtonDown(DragButton.Left, Start, shiftAtStart);
        gesture.Move(Far, shiftAtStart);
        Assert.True(gesture.IsDragging);
        return gesture;
    }

    [Fact]
    public void PressingShiftDuringADrag_Triggers()
    {
        DragGesture gesture = Dragging();

        Assert.Equal(GestureEvent.Triggered, gesture.Move(Far, shiftDown: true));
    }

    [Fact]
    public void HoldingShift_TriggersOnlyOnce()
    {
        DragGesture gesture = Dragging();
        gesture.Move(Far, shiftDown: true);

        Assert.Equal(GestureEvent.None, gesture.Move(Far, shiftDown: true));
        Assert.Equal(GestureEvent.None, gesture.Poll(shiftDown: true, anyButtonDown: true));
    }

    [Fact]
    public void ShiftHeldBeforeTheDrag_KeepsItsNativeMeaning()
    {
        DragGesture gesture = Dragging(shiftAtStart: true);

        Assert.Equal(GestureEvent.None, gesture.Move(Far, shiftDown: true));
        Assert.Equal(GestureEvent.None, gesture.Move(Far, shiftDown: false));
        Assert.Equal(GestureEvent.Triggered, gesture.Move(Far, shiftDown: true));
    }

    [Fact]
    public void MovementWithinTheThreshold_IsNotADrag()
    {
        var gesture = new DragGesture(4, 4);
        gesture.ButtonDown(DragButton.Left, Start, shiftDown: false);

        Assert.Equal(GestureEvent.None, gesture.Move(new NativeMethods.POINT(103, 97), shiftDown: true));
        Assert.False(gesture.IsDragging);
    }

    [Fact]
    public void ShiftPressedWhileTheMouseIsStill_IsCaughtByPolling()
    {
        DragGesture gesture = Dragging();

        Assert.Equal(GestureEvent.Triggered, gesture.Poll(shiftDown: true, anyButtonDown: true));
    }

    [Fact]
    public void ReleasingTheButton_EndsTheDrag()
    {
        DragGesture gesture = Dragging();

        Assert.Equal(GestureEvent.None, gesture.ButtonUp(DragButton.Right));
        Assert.Equal(GestureEvent.DragEnded, gesture.ButtonUp(DragButton.Left));
        Assert.True(gesture.IsIdle);
    }

    [Fact]
    public void AReleaseTheHookMissed_IsCaughtByPolling()
    {
        DragGesture gesture = Dragging();

        Assert.Equal(GestureEvent.DragEnded, gesture.Poll(shiftDown: false, anyButtonDown: false));
        Assert.True(gesture.IsIdle);
    }

    [Fact]
    public void ShiftWithoutADrag_DoesNothing()
    {
        var gesture = new DragGesture(4, 4);

        Assert.Equal(GestureEvent.None, gesture.Move(Far, shiftDown: true));
        Assert.Equal(GestureEvent.None, gesture.Poll(shiftDown: true, anyButtonDown: false));
    }
}
