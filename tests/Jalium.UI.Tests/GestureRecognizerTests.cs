using Jalium.UI;
using Jalium.UI.Input;
using Jalium.UI.Input.Gestures;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class GestureRecognizerTests
{
    private static PointerPoint TouchAt(uint id, double x, double y)
        => new(id, new Point(x, y), PointerDeviceType.Touch, isInContact: true,
            new PointerPointProperties(), timestamp: 0);

    [Fact]
    public void Tap_OnQuickDownAndUp_RaisesTappedOnce()
    {
        var recogniser = new GestureRecognizer { GestureSettings = GestureSettings.Tap };
        int tapCount = 0;
        recogniser.Tapped += (_, _) => tapCount++;

        recogniser.ProcessDownEvent(TouchAt(1, 100, 100));
        recogniser.ProcessUpEvent(TouchAt(1, 101, 101));

        Assert.Equal(1, tapCount);
    }

    [Fact]
    public void Tap_AfterMovingFar_DoesNotFire()
    {
        var recogniser = new GestureRecognizer { GestureSettings = GestureSettings.Tap | GestureSettings.Drag };
        int tapCount = 0;
        recogniser.Tapped += (_, _) => tapCount++;

        recogniser.ProcessDownEvent(TouchAt(1, 100, 100));
        recogniser.ProcessMoveEvents(new[] { TouchAt(1, 200, 200) }); // distance ~141 DIP
        recogniser.ProcessUpEvent(TouchAt(1, 200, 200));

        Assert.Equal(0, tapCount);
    }

    [Fact]
    public void DoubleTap_OnTwoTapsCloseInTimeAndSpace_RaisesDoubleTapped()
    {
        var recogniser = new GestureRecognizer
        {
            GestureSettings = GestureSettings.Tap | GestureSettings.DoubleTap
        };
        int singleTaps = 0, doubleTaps = 0;
        recogniser.Tapped += (_, _) => singleTaps++;
        recogniser.DoubleTapped += (_, _) => doubleTaps++;

        recogniser.ProcessDownEvent(TouchAt(1, 100, 100));
        recogniser.ProcessUpEvent(TouchAt(1, 100, 100));
        recogniser.ProcessDownEvent(TouchAt(2, 102, 102));
        recogniser.ProcessUpEvent(TouchAt(2, 102, 102));

        Assert.Equal(1, singleTaps);
        Assert.Equal(1, doubleTaps);
    }

    [Fact]
    public void Hold_AfterThreshold_RaisesHoldingStarted()
    {
        var recogniser = new GestureRecognizer { GestureSettings = GestureSettings.Hold };
        var states = new List<HoldingState>();
        recogniser.Holding += (_, e) => states.Add(e.HoldingState);

        recogniser.ProcessDownEvent(TouchAt(1, 100, 100));
        recogniser.AdvanceClockForTesting(GestureRecognizer.HoldThresholdMs + 50);

        Assert.Contains(HoldingState.Started, states);
    }

    [Fact]
    public void Hold_WhenContactMovesPastDragThreshold_IsCanceled()
    {
        var recogniser = new GestureRecognizer
        {
            GestureSettings = GestureSettings.Hold | GestureSettings.Drag
        };
        var holdingStates = new List<HoldingState>();
        var dragStates = new List<DraggingState>();
        recogniser.Holding += (_, e) => holdingStates.Add(e.HoldingState);
        recogniser.Dragging += (_, e) => dragStates.Add(e.DraggingState);

        recogniser.ProcessDownEvent(TouchAt(1, 100, 100));
        recogniser.AdvanceClockForTesting(GestureRecognizer.HoldThresholdMs + 10);
        // Now move past the drag threshold.
        recogniser.ProcessMoveEvents(new[] { TouchAt(1, 200, 100) });

        Assert.Contains(HoldingState.Started, holdingStates);
        Assert.Contains(HoldingState.Canceled, holdingStates);
        Assert.Contains(DraggingState.Started, dragStates);
    }

    [Fact]
    public void Drag_AfterDistanceThreshold_FiresStartedThenContinuingThenCompleted()
    {
        var recogniser = new GestureRecognizer { GestureSettings = GestureSettings.Drag };
        var states = new List<DraggingState>();
        recogniser.Dragging += (_, e) => states.Add(e.DraggingState);

        recogniser.ProcessDownEvent(TouchAt(1, 100, 100));
        recogniser.ProcessMoveEvents(new[] { TouchAt(1, 120, 100) }); // 20 DIP > threshold
        recogniser.ProcessMoveEvents(new[] { TouchAt(1, 140, 100) });
        recogniser.ProcessUpEvent(TouchAt(1, 140, 100));

        Assert.Equal(new[] { DraggingState.Started, DraggingState.Continuing, DraggingState.Completed }, states);
    }

    [Fact]
    public void RightTap_OnTouchHoldComplete_RaisesRightTapped()
    {
        var recogniser = new GestureRecognizer
        {
            GestureSettings = GestureSettings.Hold | GestureSettings.RightTap
        };
        int rightTapCount = 0;
        recogniser.RightTapped += (_, _) => rightTapCount++;

        recogniser.ProcessDownEvent(TouchAt(1, 100, 100));
        recogniser.AdvanceClockForTesting(GestureRecognizer.HoldThresholdMs + 10);
        recogniser.ProcessUpEvent(TouchAt(1, 100, 100));

        Assert.Equal(1, rightTapCount);
    }
}
