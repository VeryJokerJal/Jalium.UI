using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class WindowInputDispatcherMouseLeaveTrackingTests
{
    [Fact]
    public void RepeatedMouseMoves_ArmLeaveTrackingOnce_WithoutSuppressingMoveEvents()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = CreateTitleBarHost();
        var dispatcher = new WindowInputDispatcher(host);
        var previewMoves = 0;
        var bubbleMoves = 0;
        var pointerMoves = 0;
        var lastMouse = new Point();
        var lastPointer = new Point();
        host.Self.PreviewMouseMove += (_, _) => previewMoves++;
        host.Self.MouseMove += (_, e) => { bubbleMoves++; lastMouse = e.Position; };
        host.Self.PointerMove += (_, e) => { pointerMoves++; lastPointer = Assert.IsAssignableFrom<PointerEventArgs>(e).Pointer.Position; };

        try
        {
            Move(dispatcher, x: 10, timestamp: 1);
            Move(dispatcher, x: 11, timestamp: 2);
            Move(dispatcher, x: 12, timestamp: 3);

            Assert.Equal(1, host.TrackMouseLeaveRequestCount);
            Assert.Equal(3, previewMoves);
            Assert.Equal(3, bubbleMoves);
            Assert.Equal(3, pointerMoves);
            Assert.Equal(new Point(12, 10), lastMouse);
            Assert.Equal(lastMouse, lastPointer);
        }
        finally
        {
            dispatcher.HandleMouseLeave();
        }
    }

    [Fact]
    public void FailedLeaveRegistration_RetriesUntilItSucceeds()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = CreateTitleBarHost();
        host.TrackMouseLeaveResults.Enqueue(false);
        host.TrackMouseLeaveResults.Enqueue(true);
        var dispatcher = new WindowInputDispatcher(host);

        try
        {
            Move(dispatcher, x: 10, timestamp: 1);
            Move(dispatcher, x: 11, timestamp: 2);
            Move(dispatcher, x: 12, timestamp: 3);

            Assert.Equal(2, host.TrackMouseLeaveRequestCount);
        }
        finally
        {
            dispatcher.HandleMouseLeave();
        }
    }

    [Fact]
    public void MouseLeaveAndDeactivation_RearmTrackingOnTheNextMove()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = CreateTitleBarHost();
        var dispatcher = new WindowInputDispatcher(host);

        Move(dispatcher, x: 10, timestamp: 1);
        dispatcher.HandleMouseLeave();
        Move(dispatcher, x: 11, timestamp: 2);
        dispatcher.HandleWindowDeactivated(nint.Zero, clearKeyboardFocus: false);
        Move(dispatcher, x: 12, timestamp: 3);

        Assert.Equal(3, host.TrackMouseLeaveRequestCount);
        dispatcher.HandleMouseLeave();
    }

    [Fact]
    public void CaptureTransferWithinTheWindow_KeepsTracking_ButTransferAwayRearmsIt()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = CreateTitleBarHost();
        var dispatcher = new WindowInputDispatcher(host);

        try
        {
            Move(dispatcher, x: 10, timestamp: 1);

            dispatcher.HandleNativeCaptureChanged(host.NativeHandle, host.NativeHandle);
            Move(dispatcher, x: 11, timestamp: 2);
            Assert.Equal(1, host.TrackMouseLeaveRequestCount);

            dispatcher.HandleNativeCaptureChanged((nint)0x5678, host.NativeHandle);
            Move(dispatcher, x: 12, timestamp: 3);
            Assert.Equal(2, host.TrackMouseLeaveRequestCount);
        }
        finally
        {
            dispatcher.HandleMouseLeave();
        }
    }

    [Fact]
    public void NativeHandleChange_RearmsTrackingForTheReplacementWindow()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = CreateTitleBarHost();
        var dispatcher = new WindowInputDispatcher(host);

        try
        {
            Move(dispatcher, x: 10, timestamp: 1);
            host.NativeHandle = (nint)0x5678;
            Move(dispatcher, x: 11, timestamp: 2);

            Assert.Equal(2, host.TrackMouseLeaveRequestCount);
        }
        finally
        {
            dispatcher.HandleMouseLeave();
        }
    }

    private static CountingInputHost CreateTitleBarHost()
    {
        var host = new CountingInputHost
        {
            NativeHandle = (nint)0x1234,
            TitleBarVisible = true,
        };
        host.HitTarget = host.Self;
        return host;
    }

    private static void Move(WindowInputDispatcher dispatcher, double x, int timestamp)
    {
        dispatcher.HandleMouseMove(
            new Point(x, 10),
            MouseButtonStates.AllReleased,
            ModifierKeys.None,
            timestamp);
    }
}
