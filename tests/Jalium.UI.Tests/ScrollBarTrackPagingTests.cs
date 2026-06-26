using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

/// <summary>
/// Tests for ScrollBar "click-and-hold in the trough" paging: pressing a page region pages
/// immediately (ClickMode.Press); a held press stops once the thumb's leading edge reaches the
/// pointer (bottom edge for page-down, top edge for page-up — Microsoft's trough design) instead
/// of running all the way to Minimum/Maximum.
/// </summary>
public class ScrollBarTrackPagingTests
{
    [Fact]
    public void PageDown_HeldWithTrackPagingTarget_StopsAtTarget_NotMaximum()
    {
        var scrollBar = MakeVerticalScrollBar();
        scrollBar.LargeChange = 100;
        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var increase = Assert.IsType<RepeatButton>(track.IncreaseRepeatButton);

        // Arm paging directly so the clamp is isolated from the input wiring.
        SetField(scrollBar, "_trackPagingTargetValue", 500.0);
        SetField(scrollBar, "_hasTrackPagingTarget", true);

        // Far more auto-repeat ticks than needed to overshoot the whole range (50 * 100 = 5000).
        for (int i = 0; i < 50; i++)
        {
            increase.PerformClick();
        }

        Assert.Equal(500.0, scrollBar.Value, precision: 3);
    }

    [Fact]
    public void PageUp_HeldWithTrackPagingTarget_StopsAtTarget_NotMinimum()
    {
        var scrollBar = MakeVerticalScrollBar();
        scrollBar.LargeChange = 100;
        scrollBar.Value = 1000;
        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var decrease = Assert.IsType<RepeatButton>(track.DecreaseRepeatButton);

        SetField(scrollBar, "_trackPagingTargetValue", 500.0);
        SetField(scrollBar, "_hasTrackPagingTarget", true);

        for (int i = 0; i < 50; i++)
        {
            decrease.PerformClick();
        }

        Assert.Equal(500.0, scrollBar.Value, precision: 3);
    }

    [Fact]
    public void PageDown_HeldWithoutTarget_RunsToMaximum()
    {
        // Control case: without an armed target the legacy behavior (run to the end) must remain,
        // proving the clamp — not some unrelated cap — is what stops paging in the test above.
        var scrollBar = MakeVerticalScrollBar();
        scrollBar.LargeChange = 100;
        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var increase = Assert.IsType<RepeatButton>(track.IncreaseRepeatButton);

        for (int i = 0; i < 50; i++)
        {
            increase.PerformClick();
        }

        Assert.Equal(scrollBar.Maximum, scrollBar.Value, precision: 3);
    }

    [Fact]
    public void TrackPagingTarget_AlignsLeadingEdgeToPointer_PerMicrosoftDesign()
    {
        var scrollBar = MakeVerticalScrollBar();
        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var thumb = Assert.IsType<Thumb>(track.Thumb);

        var range = scrollBar.Maximum - scrollBar.Minimum;
        var thumbSize = thumb.RenderSize.Height;
        var available = track.RenderSize.Height - thumbSize;
        var origin = GetAbsoluteOrigin(track);

        // A pointer sitting below the thumb, at track-local Y = localY.
        var localY = available * 0.5 + thumbSize;
        var point = new Point(origin.X + (track.RenderSize.Width / 2.0), origin.Y + localY);

        var down = InvokeComputeTarget(scrollBar, MouseAt(point), pageDown: true);
        var up = InvokeComputeTarget(scrollBar, MouseAt(point), pageDown: false);

        Assert.True(down.ok);
        Assert.True(up.ok);

        // Page-down aligns the thumb's BOTTOM edge to the pointer; page-up aligns the TOP edge.
        var expectedDown = scrollBar.Minimum + ((localY - thumbSize) / available) * range;
        var expectedUp = scrollBar.Minimum + (localY / available) * range;
        Assert.Equal(expectedDown, down.value, precision: 3);
        Assert.Equal(expectedUp, up.value, precision: 3);

        // The two edges differ by a full thumb length in value units (not half) — proving the
        // target is edge-based, not centered.
        Assert.Equal((thumbSize / available) * range, up.value - down.value, precision: 3);
    }

    [Fact]
    public void PageDown_PressOnTrough_PagesFirstStepImmediately()
    {
        // The first page step must fire the instant the button goes down (ClickMode.Press),
        // without waiting out the RepeatButton's initial Delay or a separate click.
        var scrollBar = MakeVerticalScrollBar();
        scrollBar.LargeChange = 100;
        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var increase = Assert.IsType<RepeatButton>(track.IncreaseRepeatButton);
        var thumb = Assert.IsType<Thumb>(track.Thumb);

        // Press far down the trough so the first full page (100) is well short of the target.
        var point = PointForValue(track, thumb, scrollBar, 900, pageDown: true);

        Assert.Equal(0.0, scrollBar.Value, precision: 3);

        increase.RaiseEvent(CreatePreviewMouseDown(point)); // seeds the target
        increase.RaiseEvent(CreateMouseDown(point));        // ClickMode.Press → immediate first step

        Assert.Equal(100.0, scrollBar.Value, precision: 3);

        increase.RaiseEvent(CreateMouseUp(point)); // release: stop the repeat timer, disarm
        Assert.False(GetField<bool>(scrollBar, "_hasTrackPagingTarget"));
    }

    [Fact]
    public void PageDown_PressAndHold_SeedsBottomEdgeTargetFromPointer_AndStopsThere()
    {
        var scrollBar = MakeVerticalScrollBar();
        scrollBar.LargeChange = 100;
        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var increase = Assert.IsType<RepeatButton>(track.IncreaseRepeatButton);
        var thumb = Assert.IsType<Thumb>(track.Thumb);

        // Press in the lower trough at the spot whose bottom-edge target maps to value 500.
        var point = PointForValue(track, thumb, scrollBar, 500, pageDown: true);

        // PreviewMouseDown arms paging and seeds the target from the pointer location.
        increase.RaiseEvent(CreatePreviewMouseDown(point));

        Assert.True(GetField<bool>(scrollBar, "_hasTrackPagingTarget"));
        var seeded = GetField<double>(scrollBar, "_trackPagingTargetValue");
        Assert.Equal(500.0, seeded, precision: 3);

        for (int i = 0; i < 50; i++)
        {
            increase.PerformClick();
        }

        Assert.Equal(seeded, scrollBar.Value, precision: 3);
        Assert.True(scrollBar.Value < scrollBar.Maximum, $"Value={scrollBar.Value} reached Maximum");

        // Releasing the press disarms paging so later discrete clicks page normally again.
        increase.RaiseEvent(CreateMouseUp(point));
        Assert.False(GetField<bool>(scrollBar, "_hasTrackPagingTarget"));
    }

    private static ScrollBar MakeVerticalScrollBar()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 1000,
            ViewportSize = 100,
            Value = 0
        };

        scrollBar.Measure(new Size(16, 600));
        scrollBar.Arrange(new Rect(0, 0, 16, 600));
        return scrollBar;
    }

    // Returns the window-space point whose leading-edge paging target is the given value: the
    // bottom edge for a page-down press, the top edge for a page-up press (mirrors the handler).
    private static Point PointForValue(Track track, Thumb thumb, ScrollBar scrollBar, double value, bool pageDown)
    {
        var range = scrollBar.Maximum - scrollBar.Minimum;
        var thumbSize = thumb.RenderSize.Height;
        var available = track.RenderSize.Height - thumbSize;
        var ratio = (value - scrollBar.Minimum) / range;
        var localY = available * ratio + (pageDown ? thumbSize : 0.0);
        var origin = GetAbsoluteOrigin(track);
        return new Point(origin.X + (track.RenderSize.Width / 2.0), origin.Y + localY);
    }

    private static (bool ok, double value) InvokeComputeTarget(ScrollBar scrollBar, MouseEventArgs e, bool pageDown)
    {
        var method = typeof(ScrollBar).GetMethod("TryComputeTrackPagingTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var args = new object[] { e, pageDown, 0.0 };
        var ok = (bool)method!.Invoke(scrollBar, args)!;
        return (ok, (double)args[2]);
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(target)!;
    }

    private static Point GetAbsoluteOrigin(FrameworkElement element)
    {
        double x = 0;
        double y = 0;

        Visual? current = element;
        while (current != null)
        {
            if (current.VisualParent == null)
            {
                break;
            }

            if (current is FrameworkElement frameworkElement)
            {
                x += frameworkElement.VisualBounds.X;
                y += frameworkElement.VisualBounds.Y;
            }

            current = current.VisualParent;
        }

        return new Point(x, y);
    }

    private static MouseEventArgs MouseAt(Point position)
    {
        return new MouseEventArgs(
            UIElement.MouseMoveEvent,
            position,
            MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0);
    }

    private static MouseButtonEventArgs CreatePreviewMouseDown(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.PreviewMouseDownEvent,
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
            timestamp: 0);
    }

    private static MouseButtonEventArgs CreateMouseDown(Point position)
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
            timestamp: 0);
    }

    private static MouseButtonEventArgs CreateMouseUp(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseUpEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Released,
            clickCount: 1,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 1);
    }
}
