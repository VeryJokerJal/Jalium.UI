using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ScrollViewerMacOSElasticityTests : IDisposable
{
    private readonly List<ScrollViewer> _viewers = [];
    private readonly FieldInfo _reducedMotion = typeof(MacOSScrollBarSettings).GetField("s_prefersReducedMotion", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? _previousReducedMotion;

    public ScrollViewerMacOSElasticityTests()
    {
        _previousReducedMotion = _reducedMotion.GetValue(null);
        _reducedMotion.SetValue(null, false);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Gesture_PullsEachEdge_WithoutChangingLogicalOffsets_AndSpringsBack(bool vertical, bool end)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        double maximum = vertical ? viewer.ScrollableHeight : viewer.ScrollableWidth;
        if (vertical) viewer.ScrollToVerticalOffset(end ? maximum : 0);
        else viewer.ScrollToHorizontalOffset(end ? maximum : 0);
        double extent = vertical ? viewer.ExtentHeight : viewer.ExtentWidth;
        int changes = 0;
        viewer.ScrollChanged += (_, _) => changes++;
        Raise(viewer, vertical ? 0 : end ? 125 : -125, vertical ? end ? -125 : 125 : 0, MouseWheelPhase.Began);

        double stretch = Stretch(viewer, vertical);
        Assert.True(end ? stretch < 0 : stretch > 0);
        Assert.InRange(Math.Abs(stretch), 0.001, 50);
        Assert.Equal(end ? maximum : 0, vertical ? viewer.VerticalOffset : viewer.HorizontalOffset);
        Assert.Equal(extent, vertical ? viewer.ExtentHeight : viewer.ExtentWidth);
        Assert.Equal(0, changes);
        var bar = Field<ScrollBar>(viewer, vertical ? "_verticalScrollBar" : "_horizontalScrollBar");
        Assert.True(bar.Maximum > maximum);
        Assert.Equal(end ? bar.Maximum : 0, bar.Value, precision: 6);

        Raise(viewer, 0, 0, MouseWheelPhase.Ended);
        Assert.True(Bouncing(viewer));
        AdvanceBounce(viewer, 160);
        Assert.InRange(Math.Abs(Stretch(viewer, vertical)), 0.001, Math.Abs(stretch) - 0.001);
        AdvanceBounce(viewer, 1000);
        Assert.Equal(0, Stretch(viewer, vertical));
        Assert.False(Bouncing(viewer));
        Assert.Equal(maximum, bar.Maximum);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void DiagonalGesture_StretchesBothAxes_AndDisabledAxesRemainAvailable()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        Assert.True(Raise(viewer, -125, 125, MouseWheelPhase.Began).Handled);
        Assert.True(Stretch(viewer, false) > 0);
        Assert.True(Stretch(viewer, true) > 0);
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var wheel = Raise(viewer, -125, 125, MouseWheelPhase.Began);
        Assert.False(wheel.Handled);
        Assert.False(wheel.IsHorizontalDeltaHandled);
        Assert.True(wheel.IsVerticalDeltaHandled);
        Assert.Equal(0, Stretch(viewer, false));
    }

    [Fact]
    public void Reversal_UnwindsTheStretch_BeforeMovingTheOffset()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        Raise(viewer, 0, 125, MouseWheelPhase.Began);
        double stretch = Stretch(viewer, true);
        Raise(viewer, 0, -50, MouseWheelPhase.Changed);
        Assert.InRange(Stretch(viewer, true), 0.001, stretch - 0.001);
        Assert.Equal(0, viewer.VerticalOffset);
        Raise(viewer, 0, -125, MouseWheelPhase.Changed);
        Assert.Equal(0, Stretch(viewer, true));
        Assert.Equal(20, viewer.VerticalOffset, precision: 6);
    }

    [Fact]
    public void NewGesture_CatchesTheReturningContent_WhenPullingOutward()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        Raise(viewer, 0, 125, MouseWheelPhase.Began);
        Raise(viewer, 0, 0, MouseWheelPhase.Ended);
        AdvanceBounce(viewer, 80);
        double stretch = Stretch(viewer, true);
        Raise(viewer, 0, 0, MouseWheelPhase.Began);
        Assert.False(Bouncing(viewer));
        Assert.Equal(stretch, Stretch(viewer, true));
        Raise(viewer, 0, 10, MouseWheelPhase.Changed);
        Assert.True(Stretch(viewer, true) > stretch);
        Assert.Equal(0, viewer.VerticalOffset);
    }

    [Fact]
    public void ReleaseWithFinalMovement_ReturnsFromTheFinalVisualPosition()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        Raise(viewer, 0, 125, MouseWheelPhase.Began);
        double before = Stretch(viewer, true);
        Raise(viewer, 0, 25, MouseWheelPhase.Ended);
        Assert.True(Stretch(viewer, true) > before);
        Assert.Equal(Stretch(viewer, true), Field<double>(viewer, "_bounceFromY"));
        AdvanceBounce(viewer, 1000);
        Assert.Equal(0, Stretch(viewer, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReleaseWithInwardFinalMovement_FinishesUnwindingTheCurrentGesture(bool vertical)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        Raise(viewer, vertical ? 0 : -125, vertical ? 125 : 0, MouseWheelPhase.Began);
        double stretch = Stretch(viewer, vertical);

        Raise(viewer, vertical ? 0 : 5, vertical ? -5 : 0, MouseWheelPhase.Ended);

        Assert.Equal(0, vertical ? viewer.VerticalOffset : viewer.HorizontalOffset);
        Assert.InRange(Stretch(viewer, vertical), 0.001, stretch - 0.001);
        Assert.Equal(Stretch(viewer, vertical), Field<double>(viewer, vertical ? "_bounceFromY" : "_bounceFromX"));
        Assert.True(Bouncing(viewer));
    }

    [Theory]
    [InlineData(true, false, MouseWheelPhase.Began, false)]
    [InlineData(true, true, MouseWheelPhase.Began, false)]
    [InlineData(false, false, MouseWheelPhase.Began, false)]
    [InlineData(false, true, MouseWheelPhase.Began, false)]
    [InlineData(true, false, MouseWheelPhase.Changed, false)]
    [InlineData(true, true, MouseWheelPhase.Changed, false)]
    [InlineData(false, false, MouseWheelPhase.Changed, false)]
    [InlineData(false, true, MouseWheelPhase.Changed, false)]
    [InlineData(true, false, MouseWheelPhase.Changed, true)]
    [InlineData(true, true, MouseWheelPhase.Changed, true)]
    [InlineData(false, false, MouseWheelPhase.Changed, true)]
    [InlineData(false, true, MouseWheelPhase.Changed, true)]
    public void ReturningContent_InwardInputScrollsImmediately(bool vertical, bool end,
        MouseWheelPhase phase, bool momentum)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        double maximum = vertical ? viewer.ScrollableHeight : viewer.ScrollableWidth;
        if (vertical) viewer.ScrollToVerticalOffset(end ? maximum : 0);
        else viewer.ScrollToHorizontalOffset(end ? maximum : 0);
        double pull = end ? 125 : -125;
        Raise(viewer, vertical ? 0 : pull, vertical ? -pull : 0, MouseWheelPhase.Began);
        Raise(viewer, 0, 0, MouseWheelPhase.Ended);
        AdvanceBounce(viewer, 80);
        Assert.True(Bouncing(viewer));
        Assert.NotEqual(0, Stretch(viewer, vertical));

        double inward = end ? -5 : 5;
        var wheel = Raise(viewer, vertical ? 0 : inward, vertical ? -inward : 0,
            momentum ? MouseWheelPhase.None : phase, momentum ? phase : MouseWheelPhase.None);

        Assert.True(wheel.Handled);
        Assert.Equal(end ? maximum - 2 : 2, vertical ? viewer.VerticalOffset : viewer.HorizontalOffset, precision: 6);
        Assert.Equal(0, Stretch(viewer, vertical));
        Assert.False(Bouncing(viewer));
        AdvanceBounce(viewer, 160);
        Assert.Equal(0, Stretch(viewer, vertical));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ReturningContent_GrowingExtentDoesNotHoldBackMovement(bool vertical, bool momentum)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var content = new Border { Width = 600, Height = 1000 };
        var viewer = CreateViewer(content);
        double oldEnd = vertical ? viewer.ScrollableHeight : viewer.ScrollableWidth;
        if (vertical) viewer.ScrollToVerticalOffset(oldEnd);
        else viewer.ScrollToHorizontalOffset(oldEnd);
        Raise(viewer, vertical ? 0 : 50, vertical ? -50 : 0,
            momentum ? MouseWheelPhase.None : MouseWheelPhase.Began,
            momentum ? MouseWheelPhase.Began : MouseWheelPhase.None);
        if (!momentum) Raise(viewer, 0, 0, MouseWheelPhase.Ended);
        AdvanceBounce(viewer, 80);
        Assert.True(Bouncing(viewer));

        if (vertical) content.Height += 400;
        else content.Width += 400;
        viewer.InvalidateMeasure();
        viewer.Measure(new Size(200, 120));
        viewer.Arrange(new Rect(0, 0, 200, 120));
        Assert.Equal(oldEnd, vertical ? viewer.VerticalOffset : viewer.HorizontalOffset);
        Assert.True(oldEnd < (vertical ? viewer.ScrollableHeight : viewer.ScrollableWidth));

        Raise(viewer, vertical ? 0 : 50, vertical ? -50 : 0,
            momentum ? MouseWheelPhase.None : MouseWheelPhase.Changed,
            momentum ? MouseWheelPhase.Changed : MouseWheelPhase.None);

        Assert.Equal(oldEnd + 20, vertical ? viewer.VerticalOffset : viewer.HorizontalOffset, precision: 6);
        Assert.Equal(0, Stretch(viewer, vertical));
        Assert.False(Bouncing(viewer));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ReturningChild_HandsWheelToScrollableParent(bool vertical, bool momentum)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var inner = CreateViewer();
        var content = new Grid { Width = 600, Height = 1000 };
        content.Children.Add(inner);
        var outer = CreateViewer(content);
        double innerEnd = vertical ? inner.ScrollableHeight : inner.ScrollableWidth;
        if (vertical)
        {
            inner.ScrollToVerticalOffset(innerEnd);
            outer.ScrollToVerticalOffset(outer.ScrollableHeight);
        }
        else
        {
            inner.ScrollToHorizontalOffset(innerEnd);
            outer.ScrollToHorizontalOffset(outer.ScrollableWidth);
        }
        Raise(inner, vertical ? 0 : 50, vertical ? -50 : 0,
            momentum ? MouseWheelPhase.None : MouseWheelPhase.Began,
            momentum ? MouseWheelPhase.Began : MouseWheelPhase.None);
        if (!momentum) Raise(inner, 0, 0, MouseWheelPhase.Ended);
        AdvanceBounce(inner, 80);
        Assert.True(Bouncing(inner));
        if (vertical) outer.ScrollToVerticalOffset(100);
        else outer.ScrollToHorizontalOffset(100);

        var wheel = Raise(inner, vertical ? 0 : 50, vertical ? -50 : 0,
            momentum ? MouseWheelPhase.None : MouseWheelPhase.Changed,
            momentum ? MouseWheelPhase.Changed : MouseWheelPhase.None);

        Assert.True(wheel.Handled);
        Assert.Equal(120, vertical ? outer.VerticalOffset : outer.HorizontalOffset, precision: 6);
        Assert.Equal(innerEnd, vertical ? inner.VerticalOffset : inner.HorizontalOffset);
        Assert.Equal(0, Stretch(inner, vertical));
        Assert.False(Bouncing(inner));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DiagonalReturn_InwardMomentumDoesNotStopTheOtherAxisSpring(bool vertical)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        Raise(viewer, -125, 125, MouseWheelPhase.Began);
        Raise(viewer, 0, 0, MouseWheelPhase.Ended);
        AdvanceBounce(viewer, 80);
        double otherStretch = Stretch(viewer, !vertical);

        Raise(viewer, vertical ? 0 : 5, vertical ? -5 : 0, momentum: MouseWheelPhase.Changed);

        Assert.Equal(2, vertical ? viewer.VerticalOffset : viewer.HorizontalOffset, precision: 6);
        Assert.Equal(0, Stretch(viewer, vertical));
        Assert.Equal(otherStretch, Stretch(viewer, !vertical));
        Assert.True(Bouncing(viewer));
        AdvanceBounce(viewer, 160);
        Assert.Equal(0, Stretch(viewer, vertical));
        Assert.InRange(Stretch(viewer, !vertical), 0.001, otherStretch - 0.001);
    }

    [Fact]
    public void NativeMomentum_HittingTheEnd_StartsOneReturn_AndDoesNotRestartIt()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        viewer.ScrollToVerticalOffset(viewer.ScrollableHeight - 4);
        Raise(viewer, 0, -50, momentum: MouseWheelPhase.Began);
        Assert.Equal(viewer.ScrollableHeight, viewer.VerticalOffset);
        Assert.True(Stretch(viewer, true) < 0);
        Assert.True(Bouncing(viewer));
        AdvanceBounce(viewer, 160);
        double stretch = Stretch(viewer, true);
        long start = Field<long>(viewer, "_bounceStartTicks");
        Raise(viewer, 0, -500, momentum: MouseWheelPhase.Changed);
        Assert.Equal(stretch, Stretch(viewer, true));
        Assert.Equal(start, Field<long>(viewer, "_bounceStartTicks"));
        Raise(viewer, 0, 0, momentum: MouseWheelPhase.Ended);
        Assert.Equal(start, Field<long>(viewer, "_bounceStartTicks"));
        AdvanceBounce(viewer, 1000);
        Assert.Equal(0, Stretch(viewer, true));
    }

    [Fact]
    public void NestedGesture_UsesTheScrollableParent_BeforeStretchingTheBoundary()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var inner = CreateViewer();
        var panel = new Grid { Width = 600, Height = 1000 };
        panel.Children.Add(inner);
        var outer = CreateViewer(panel);
        outer.ScrollToVerticalOffset(100);
        Raise(inner, 0, 50, MouseWheelPhase.Began);
        Assert.Equal(0, Stretch(inner, true));
        Assert.Equal(0, Stretch(outer, true));
        Assert.Equal(80, outer.VerticalOffset);
        outer.ScrollToVerticalOffset(0);
        Raise(inner, 0, 50, MouseWheelPhase.Began);
        Assert.True(Stretch(inner, true) > 0);
        Assert.Equal(0, Stretch(outer, true));
        Raise(inner, 0, 0, MouseWheelPhase.Cancelled);
        Assert.True(Bouncing(inner));
        AdvanceBounce(inner, 1000);
        Assert.Equal(0, Stretch(inner, true));
    }

    [Fact]
    public void ReducedMotion_DisablesStretch_AndCancelsAnExistingReturn()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        Raise(viewer, 0, 125, MouseWheelPhase.Began);
        Raise(viewer, 0, 0, MouseWheelPhase.Ended);
        _reducedMotion.SetValue(null, true);
        viewer.ApplyMacOSScrollBarPreferences(true);
        Assert.Equal(0, Stretch(viewer, true));
        Assert.False(Bouncing(viewer));
        Assert.False(Raise(viewer, 0, 125, MouseWheelPhase.Began).Handled);
        Assert.Equal(0, Stretch(viewer, true));
    }

    [Fact]
    public void AbsoluteScroll_AndDetach_ClearElasticVisualState()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var viewer = CreateViewer();
        Raise(viewer, 0, 125, MouseWheelPhase.Began);
        Raise(viewer, 0, 0, MouseWheelPhase.Ended);
        viewer.ScrollToVerticalOffset(100);
        Assert.Equal(100, viewer.VerticalOffset);
        Assert.Equal(0, Stretch(viewer, true));
        Assert.False(Bouncing(viewer));
        viewer.ScrollToVerticalOffset(0);
        var root = new Grid();
        root.Children.Add(viewer);
        Raise(viewer, 0, 125, MouseWheelPhase.Began);
        Raise(viewer, 0, 0, MouseWheelPhase.Ended);
        root.Children.Remove(viewer);
        Assert.Equal(0, Stretch(viewer, true));
        Assert.False(Bouncing(viewer));
    }

    private ScrollViewer CreateViewer(FrameworkElement? content = null)
    {
        var viewer = new ScrollViewer
        {
            Width = 200, Height = 120,
            Content = content ?? new Border { Width = 600, Height = 1000 },
            IsOverlayScrollBarEnabled = true, IsScrollBarAutoHideEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        viewer.Measure(new Size(200, 120));
        viewer.Arrange(new Rect(0, 0, 200, 120));
        _viewers.Add(viewer);
        return viewer;
    }

    private static MouseWheelEventArgs Raise(ScrollViewer viewer, double x, double y,
        MouseWheelPhase phase = MouseWheelPhase.None, MouseWheelPhase momentum = MouseWheelPhase.None)
    {
        var e = new MouseWheelEventArgs(UIElement.PreviewMouseWheelEvent, new Point(10, 10), x, y, true,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1, phase, momentum);
        viewer.RaiseEvent(e);
        e.RoutedEvent = UIElement.MouseWheelEvent;
        viewer.RaiseEvent(e);
        return e;
    }

    private static double Stretch(ScrollViewer viewer, bool vertical) => Field<double>(viewer, vertical ? "_overscrollY" : "_overscrollX");
    private static bool Bouncing(ScrollViewer viewer) => Field<DispatcherTimer?>(viewer, "_bounceTimer")?.IsEnabled == true;
    private static T Field<T>(ScrollViewer viewer, string name) => (T)typeof(ScrollViewer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;
    private static void AdvanceBounce(ScrollViewer viewer, int elapsed)
    {
        typeof(ScrollViewer).GetField("_bounceStartTicks", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewer, Environment.TickCount64 - elapsed);
        typeof(ScrollViewer).GetMethod("OnBounceTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewer, [null, EventArgs.Empty]);
    }

    public void Dispose()
    {
        foreach (var viewer in _viewers)
            typeof(ScrollViewer).GetMethod("CompleteLifecycleCleanup", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewer, null);
        _reducedMotion.SetValue(null, _previousReducedMotion);
    }
}
