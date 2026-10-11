using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowInputDispatcherThumbCaptureTests
{
    [Fact]
    public void MouseWheel_PreviewAndBubbleRetainBothAxesAndPreciseDeviceMetadata()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var target = new Border();
        host.HitTarget = target;
        var received = new List<MouseWheelEventArgs>();
        target.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) => received.Add(e)));
        target.AddHandler(UIElement.MouseWheelEvent, new MouseWheelEventHandler((_, e) =>
        {
            received.Add(e);
            e.Handled = true;
        }));
        var dispatcher = new WindowInputDispatcher(host);
        dispatcher.HandleMouseWheel(new Point(6, 10), 0.25, -0.125, true,
            MouseButtonStates.AllReleased, ModifierKeys.Shift, 1, MouseWheelPhase.Changed, MouseWheelPhase.Began);

        Assert.Equal(2, received.Count);
        Assert.All(received, e =>
        {
            Assert.Equal(0.25, e.HorizontalDelta);
            Assert.Equal(-0.125, e.VerticalDelta);
            Assert.True(e.HasPreciseScrollingDeltas);
            Assert.Equal(MouseWheelPhase.Changed, e.Phase);
            Assert.Equal(MouseWheelPhase.Began, e.MomentumPhase);
            Assert.Equal(ModifierKeys.Shift, e.KeyboardModifiers);
        });
    }

    [Fact]
    public void MouseWheel_PreviewAxisConsumptionSurvivesTheBubbleTransition()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var target = new Border();
        host.HitTarget = target;
        target.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) => e.IsVerticalDeltaHandled = true));
        MouseWheelEventArgs? received = null;
        target.AddHandler(UIElement.MouseWheelEvent, new MouseWheelEventHandler((_, e) => { received = e; e.Handled = true; }));
        new WindowInputDispatcher(host).HandleMouseWheel(new Point(), 10, -10, true,
            MouseButtonStates.AllReleased, ModifierKeys.None, 1);
        Assert.NotNull(received);
        Assert.True(received.IsVerticalDeltaHandled);
        Assert.False(received.IsHorizontalDeltaHandled);
    }

    [Fact]
    public void MouseWheel_GestureTargetSurvivesMovingContent_ThroughReleaseAndMomentum()
    {
        var root = new Grid();
        var first = new Border();
        var second = new Border();
        root.Children.Add(first);
        root.Children.Add(second);
        var routing = new MouseWheelGestureRouting();
        Assert.Same(first, routing.ResolveTarget(first, root, MouseWheelPhase.Began, MouseWheelPhase.None));
        Assert.Same(first, routing.ResolveTarget(second, root, MouseWheelPhase.Changed, MouseWheelPhase.None));
        Assert.Same(first, routing.ResolveTarget(second, root, MouseWheelPhase.Ended, MouseWheelPhase.None));
        Assert.Same(first, routing.ResolveTarget(second, root, MouseWheelPhase.None, MouseWheelPhase.Began));
        Assert.Same(first, routing.ResolveTarget(second, root, MouseWheelPhase.None, MouseWheelPhase.Ended));
        Assert.Same(second, routing.ResolveTarget(second, root, MouseWheelPhase.Began, MouseWheelPhase.None));
        MouseWheelEventArgs? cancelled = null;
        second.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) => cancelled = e));
        routing.Cancel();
        Assert.Equal(MouseWheelPhase.Cancelled, cancelled?.Phase);
        Assert.Same(first, routing.ResolveTarget(first, root, MouseWheelPhase.Changed, MouseWheelPhase.None));
        root.Children.Remove(first);
        Assert.Same(second, routing.ResolveTarget(second, root, MouseWheelPhase.Changed, MouseWheelPhase.None));
    }

    [Fact]
    public void CapturedThumb_MoveAndRelease_BypassHitTestAndReleaseImmediately()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var thumb = new Thumb { Width = 12, Height = 80 };
        host.HitTarget = thumb;
        var dispatcher = new WindowInputDispatcher(host);

        var pressed = MouseButtonStates.AllReleased with { Left = MouseButtonState.Pressed };
        dispatcher.HandleMouseDown(
            MouseButton.Left,
            new Point(6, 10),
            pressed,
            ModifierKeys.None,
            clickCount: 1,
            timestamp: 1);

        Assert.True(thumb.IsMouseCaptured);
        var hitTestsAfterPress = host.HitTestCount;

        dispatcher.HandleMouseMove(
            new Point(6, 60),
            pressed,
            ModifierKeys.None,
            timestamp: 2);
        dispatcher.HandleMouseUp(
            MouseButton.Left,
            new Point(6, 60),
            MouseButtonStates.AllReleased,
            ModifierKeys.None,
            timestamp: 3);

        Assert.Equal(hitTestsAfterPress, host.HitTestCount);
        Assert.False(thumb.IsMouseCaptured);
    }

    [Fact]
    public void CapturedScrollBarThumb_SuspendsOverlayLightDismissUntilDragEnds()
    {
        UIElement.ForceReleaseMouseCapture();
        var popup = new Popup
        {
            IsOpen = true,
            StaysOpen = false
        };
        var thumb = new Thumb { Width = 12, Height = 80 };
        var popupRoot = new PopupRoot(popup, thumb, isLightDismiss: true)
        {
            Width = 100,
            Height = 100
        };
        var overlay = new OverlayLayer();
        Canvas.SetLeft(popupRoot, 0);
        Canvas.SetTop(popupRoot, 0);
        overlay.AddPopupRoot(popupRoot);

        try
        {
            Assert.True(thumb.CaptureMouse());
            Assert.True(popupRoot.HasPointerCaptureWithin);

            Assert.False(overlay.TryHandleLightDismiss(new Point(200, 200)));
            Assert.True(popup.IsOpen);

            thumb.ReleaseMouseCapture();

            Assert.True(overlay.TryHandleLightDismiss(new Point(200, 200)));
            Assert.False(popup.IsOpen);
        }
        finally
        {
            UIElement.ForceReleaseMouseCapture();
            overlay.RemovePopupRoot(popupRoot);
            popupRoot.Detach();
        }
    }

    [Fact]
    public void CapturedPopupContent_SuspendsExternalLightDismissUntilCaptureEnds()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var popup = new Popup
        {
            IsOpen = true,
            StaysOpen = false
        };
        var captureOwner = new Border { Width = 12, Height = 80 };
        var popupRoot = new PopupRoot(popup, captureOwner, isLightDismiss: true);
        SetPopupRootForTest(popup, popupRoot);
        host.ExternalPopups.Add(popup);
        var dispatcher = new WindowInputDispatcher(host);

        try
        {
            Assert.True(captureOwner.CaptureMouse());
            Assert.True(popup.HasPointerCaptureWithin);

            dispatcher.HandleMouseDown(
                MouseButton.Left,
                new Point(200, 200),
                MouseButtonStates.AllReleased with { Left = MouseButtonState.Pressed },
                ModifierKeys.None,
                clickCount: 1,
                timestamp: 1);

            Assert.True(popup.IsOpen);

            captureOwner.ReleaseMouseCapture();

            dispatcher.HandleMouseDown(
                MouseButton.Left,
                new Point(200, 200),
                MouseButtonStates.AllReleased with { Left = MouseButtonState.Pressed },
                ModifierKeys.None,
                clickCount: 1,
                timestamp: 2);

            Assert.False(popup.IsOpen);
        }
        finally
        {
            UIElement.ForceReleaseMouseCapture();
            host.ExternalPopups.Clear();
        }
    }

    [Fact]
    public void OpenExternalLightDismissPopup_SuppressesContentHover_UntilDismissed()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var button = new Button { Width = 100, Height = 30 };
        host.HitTarget = button;
        var dispatcher = new WindowInputDispatcher(host);

        try
        {
            dispatcher.HandleMouseMove(
                new Point(10, 10), MouseButtonStates.AllReleased, ModifierKeys.None, timestamp: 1);
            Assert.True(button.IsMouseOver);

            // A popup promoted to its own window cannot capture the parent's pointer,
            // so without an explicit block the parent keeps hovering its own content.
            var popup = new Popup { IsOpen = true, StaysOpen = false };
            host.ExternalPopups.Add(popup);

            dispatcher.HandleMouseMove(
                new Point(20, 12), MouseButtonStates.AllReleased, ModifierKeys.None, timestamp: 2);

            Assert.False(button.IsMouseOver);
            Assert.Null(UIElement.MouseDirectlyOverElement);

            host.ExternalPopups.Clear();

            dispatcher.HandleMouseMove(
                new Point(20, 12), MouseButtonStates.AllReleased, ModifierKeys.None, timestamp: 3);
            Assert.True(button.IsMouseOver);
        }
        finally
        {
            UIElement.ForceReleaseMouseCapture();
            host.ExternalPopups.Clear();
        }
    }

    [Fact]
    public void OpenExternalPopupThatStaysOpen_LeavesContentHoverAlone()
    {
        UIElement.ForceReleaseMouseCapture();
        using var host = new CountingInputHost();
        var button = new Button { Width = 100, Height = 30 };
        host.HitTarget = button;
        var dispatcher = new WindowInputDispatcher(host);

        try
        {
            // StaysOpen popups are not light dismiss — a ToolTip is the common case, and
            // blocking hover for one would freeze the very hover that keeps it alive.
            host.ExternalPopups.Add(new Popup { IsOpen = true, StaysOpen = true });

            dispatcher.HandleMouseMove(
                new Point(10, 10), MouseButtonStates.AllReleased, ModifierKeys.None, timestamp: 1);

            Assert.True(button.IsMouseOver);
        }
        finally
        {
            UIElement.ForceReleaseMouseCapture();
            host.ExternalPopups.Clear();
        }
    }

    [Fact]
    public void NonClientPress_ClosesBothPopupFormsAndConsumesTheGesture()
    {
        UIElement.ForceReleaseMouseCapture();
        var window = new Window();

        var overlayPopup = new Popup { IsOpen = true, StaysOpen = false };
        var overlayRoot = new PopupRoot(overlayPopup, new Border(), isLightDismiss: true);
        window.OverlayLayer.AddPopupRoot(overlayRoot);

        var externalPopup = new Popup { IsOpen = true, StaysOpen = false };
        window.ActiveExternalPopups.Add(externalPopup);

        try
        {
            Assert.True(window.HasOpenLightDismissPopup());

            // Caption drag and border resize run inside DefWindowProc's modal loop and
            // never reach the client-area dispatcher, so the press has to dismiss here
            // — and be consumed, so the window does not also start moving.
            Assert.True(InvokeNonClientPressDismiss(window));
            Assert.False(overlayPopup.IsOpen);
            Assert.False(externalPopup.IsOpen);

            // A real close detaches the roots; these stand-ins never opened through
            // Popup.OpenPopup, so retire them by hand before the negative case.
            window.ActiveExternalPopups.Clear();
            window.OverlayLayer.RemovePopupRoot(overlayRoot);

            // With nothing left to dismiss the press falls through to the drag as usual.
            Assert.False(window.HasOpenLightDismissPopup());
            Assert.False(InvokeNonClientPressDismiss(window));
        }
        finally
        {
            UIElement.ForceReleaseMouseCapture();
            window.ActiveExternalPopups.Clear();
            overlayRoot.Detach();
        }
    }

    private static bool InvokeNonClientPressDismiss(Window window)
    {
        var method = typeof(Window).GetMethod(
            "TryDismissLightDismissPopupsForNonClientPress",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(window, null)!;
    }

    private static void SetPopupRootForTest(Popup popup, PopupRoot root)
    {
        var field = typeof(Popup).GetField("_popupRoot", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(popup, root);
    }
}
