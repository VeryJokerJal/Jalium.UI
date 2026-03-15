using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class ScrollViewerInertiaTests
{
    [Fact]
    public void ScrollViewerInertia_DefaultSettings_ShouldBeEnabled()
    {
        var viewer = new ScrollViewer();
        Assert.True(viewer.IsScrollInertiaEnabled);
        Assert.Equal(300.0, viewer.ScrollInertiaDurationMs);
    }

    [Fact]
    public void ScrollViewerPanning_DefaultParameters_ShouldMatchWpfCompatibilityValues()
    {
        var viewer = new ScrollViewer();
        Assert.Equal(0.001, viewer.PanningDeceleration, precision: 6);
        Assert.Equal(1.0, viewer.PanningRatio, precision: 6);
    }

    [Fact]
    public void ScrollViewerInertia_FrameRateIndependent_ShouldMatchAcrossFps()
    {
        double offset60Fps = SimulateSmoothTickProgress(frameMs: 16, totalMs: 300);
        double offset20Fps = SimulateSmoothTickProgress(frameMs: 50, totalMs: 300);

        Assert.InRange(Math.Abs(offset60Fps - offset20Fps), 0, 3.0);
    }

    [Fact]
    public void ScrollViewerInertia_DurationZero_ShouldSnapToImmediateWheelPath()
    {
        var viewer = CreateConfiguredViewer();
        viewer.IsScrollInertiaEnabled = true;
        viewer.ScrollInertiaDurationMs = 0;

        var wheel = CreateMouseWheel(new Point(8, 8), -120, ModifierKeys.None, timestamp: 1);
        viewer.RaiseEvent(wheel);

        Assert.False(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));
        Assert.True(viewer.VerticalOffset > 0);
    }

    [Fact]
    public void ScrollViewer_WheelOverScrollBarTrack_ShouldMatchContentWheelDistance()
    {
        var contentViewer = CreateConfiguredViewer(initialVerticalOffset: 100);
        contentViewer.IsScrollInertiaEnabled = false;
        InvokePrivateMethod(contentViewer, "UpdateScrollBarMetrics");

        contentViewer.RaiseEvent(CreateMouseWheel(new Point(8, 8), -120, ModifierKeys.None, timestamp: 1));
        double contentOffset = contentViewer.VerticalOffset;

        var scrollBarViewer = CreateConfiguredViewer(initialVerticalOffset: 100);
        scrollBarViewer.IsScrollInertiaEnabled = false;
        InvokePrivateMethod(scrollBarViewer, "UpdateScrollBarMetrics");

        var verticalBar = GetPrivateField<ScrollBar>(scrollBarViewer, "_verticalScrollBar");
        verticalBar.RaiseEvent(CreateMouseWheel(new Point(6, 6), -120, ModifierKeys.None, timestamp: 2));

        Assert.Equal(contentOffset, scrollBarViewer.VerticalOffset, precision: 3);
    }

    [Fact]
    public void ScrollViewer_DisablingInertia_ShouldSnapPendingSmoothScrollImmediately()
    {
        var viewer = CreateConfiguredViewer(initialVerticalOffset: 100);
        viewer.IsScrollInertiaEnabled = true;
        viewer.ScrollInertiaDurationMs = 300;

        SetPrivateField(viewer, "_smoothTargetY", 420.0);
        SetPrivateField(viewer, "_smoothTargetX", 0.0);
        SetPrivateField(viewer, "_isSmoothScrolling", true);

        viewer.IsScrollInertiaEnabled = false;

        Assert.False(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));
        Assert.Equal(420.0, viewer.VerticalOffset, precision: 3);
    }

    [Fact]
    public void ScrollViewer_NonWheelInputs_ShouldRemainImmediate()
    {
        var viewer = CreateConfiguredViewer(initialVerticalOffset: 100);
        SetPrivateField(viewer, "_smoothTargetY", 900.0);
        SetPrivateField(viewer, "_isSmoothScrolling", true);

        var key = new KeyEventArgs(UIElement.KeyDownEvent, Key.PageDown, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 2);
        viewer.RaiseEvent(key);

        Assert.True(key.Handled);
        Assert.False(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));
        Assert.Equal(200.0, viewer.VerticalOffset, precision: 3);
    }

    [Fact]
    public void ScrollViewer_DraggingThumb_ShouldRemainImmediate()
    {
        var viewer = CreateConfiguredViewer(
            initialVerticalOffset: 100,
            extentHeight: 1000,
            viewportHeight: 100,
            extentWidth: 100,
            viewportWidth: 100,
            width: 200,
            height: 120);

        viewer.IsScrollInertiaEnabled = true;
        viewer.ScrollInertiaDurationMs = 3000;
        SetPrivateField(viewer, "_smoothTargetY", 800.0);
        SetPrivateField(viewer, "_isSmoothScrolling", true);

        var trackHeight = viewer.RenderSize.Height - 32; // minus 2 * ScrollButtonSize
        var thumbHeight = Math.Max(20, (viewer.ViewportHeight / viewer.ExtentHeight) * trackHeight);
        var thumbTop = 16 + (viewer.VerticalOffset / viewer.ScrollableHeight) * (trackHeight - thumbHeight);

        var start = new Point(viewer.RenderSize.Width - 8, thumbTop + 1);
        var end = new Point(start.X, start.Y + 30);

        viewer.RaiseEvent(CreateMouseDown(start, timestamp: 3));
        viewer.RaiseEvent(CreateMouseMove(end, MouseButtonState.Pressed, timestamp: 4));

        var scrollRange = trackHeight - thumbHeight;
        var expected = 100 + ((end.Y - start.Y) / scrollRange) * viewer.ScrollableHeight;

        Assert.False(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));
        Assert.Equal(expected, viewer.VerticalOffset, precision: 3);
    }

    private static double SimulateSmoothTickProgress(int frameMs, int totalMs)
    {
        var viewer = CreateConfiguredViewer(initialVerticalOffset: 0);
        viewer.ScrollInertiaDurationMs = 300;

        SetPrivateField(viewer, "_smoothTargetY", 600.0);
        SetPrivateField(viewer, "_smoothTargetX", 0.0);
        SetPrivateField(viewer, "_isSmoothScrolling", true);

        var tickMethod = typeof(ScrollViewer).GetMethod("AdvanceSmoothScrollByMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(tickMethod);

        int steps = (int)Math.Ceiling(totalMs / (double)frameMs);
        for (int i = 0; i < steps; i++)
        {
            if (!GetPrivateField<bool>(viewer, "_isSmoothScrolling"))
                break;

            tickMethod!.Invoke(viewer, new object?[] { (long)frameMs });
        }

        return viewer.VerticalOffset;
    }

    private static ScrollViewer CreateConfiguredViewer(
        double initialVerticalOffset = 0,
        double extentHeight = 2000,
        double viewportHeight = 100,
        double extentWidth = 200,
        double viewportWidth = 100,
        double width = 240,
        double height = 140)
    {
        var viewer = new ScrollViewer();
        viewer.Arrange(new Rect(0, 0, width, height));

        SetPrivateField(viewer, "_extentHeight", extentHeight);
        SetPrivateField(viewer, "_viewportHeight", viewportHeight);
        SetPrivateField(viewer, "_extentWidth", extentWidth);
        SetPrivateField(viewer, "_viewportWidth", viewportWidth);
        SetPrivateField(viewer, "_verticalOffset", initialVerticalOffset);
        SetPrivateField(viewer, "_horizontalOffset", 0.0);
        SetPrivateField(viewer, "_smoothTargetX", 0.0);
        SetPrivateField(viewer, "_smoothTargetY", initialVerticalOffset);

        return viewer;
    }

    private static MouseButtonEventArgs CreateMouseDown(Point position, int timestamp)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount: 1,
            leftButton: MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: timestamp);
    }

    private static MouseEventArgs CreateMouseMove(Point position, MouseButtonState leftButton, int timestamp)
    {
        return new MouseEventArgs(
            UIElement.MouseMoveEvent,
            position,
            leftButton,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp);
    }

    private static MouseWheelEventArgs CreateMouseWheel(Point position, int delta, ModifierKeys modifiers, int timestamp)
    {
        return new MouseWheelEventArgs(
            UIElement.MouseWheelEvent,
            position,
            delta,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: modifiers,
            timestamp: timestamp);
    }

    private static void SetPrivateField(ScrollViewer viewer, string fieldName, object value)
    {
        var field = typeof(ScrollViewer).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(viewer, value);
    }

    private static T GetPrivateField<T>(ScrollViewer viewer, string fieldName)
    {
        var field = typeof(ScrollViewer).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(viewer);
        Assert.NotNull(value);
        return (T)value!;
    }

    private static void InvokePrivateMethod(ScrollViewer viewer, string methodName)
    {
        var method = typeof(ScrollViewer).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewer, null);
    }
}
