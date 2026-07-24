using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class ScrollViewerTouchPanningTests
{
    [Fact]
    public void PanningMode_None_ShouldIgnorePointerPanning()
    {
        var viewer = CreateConfiguredViewer(horizontalOffset: 120, verticalOffset: 220);
        viewer.PanningMode = PanningMode.None;

        RaisePanGesture(viewer, start: new Point(30, 30), move: new Point(30, 90), pointerId: 10);

        Assert.Equal(120, viewer.HorizontalOffset, precision: 3);
        Assert.Equal(220, viewer.VerticalOffset, precision: 3);
    }

    [Fact]
    public void PanningMode_HorizontalOnly_ShouldLockToHorizontal()
    {
        var viewer = CreateConfiguredViewer(horizontalOffset: 200, verticalOffset: 240);
        viewer.PanningMode = PanningMode.HorizontalOnly;

        RaisePanGesture(viewer, start: new Point(10, 10), move: new Point(50, 60), pointerId: 11);

        Assert.True(viewer.HorizontalOffset < 200);
        Assert.Equal(240, viewer.VerticalOffset, precision: 3);
    }

    [Fact]
    public void PanningMode_HorizontalFirst_ShouldResolveAndLockAxis()
    {
        var viewer = CreateConfiguredViewer(horizontalOffset: 180, verticalOffset: 180);
        viewer.PanningMode = PanningMode.HorizontalFirst;

        viewer.RaiseEvent(CreatePointerDown(20, new Point(0, 0)));
        viewer.RaiseEvent(CreatePointerMove(20, new Point(22, 3), timestamp: 10));
        viewer.RaiseEvent(CreatePointerMove(20, new Point(22, 40), timestamp: 20));
        viewer.RaiseEvent(CreatePointerUp(20, new Point(22, 40), timestamp: 30));

        Assert.True(viewer.HorizontalOffset < 180);
        Assert.Equal(180, viewer.VerticalOffset, precision: 3);
    }

    [Fact]
    public void PanningMode_VerticalFirst_ShouldResolveAndLockAxis()
    {
        var viewer = CreateConfiguredViewer(horizontalOffset: 180, verticalOffset: 180);
        viewer.PanningMode = PanningMode.VerticalFirst;

        viewer.RaiseEvent(CreatePointerDown(21, new Point(0, 0)));
        viewer.RaiseEvent(CreatePointerMove(21, new Point(3, 22), timestamp: 10));
        viewer.RaiseEvent(CreatePointerMove(21, new Point(40, 22), timestamp: 20));
        viewer.RaiseEvent(CreatePointerUp(21, new Point(40, 22), timestamp: 30));

        Assert.Equal(180, viewer.HorizontalOffset, precision: 3);
        Assert.True(viewer.VerticalOffset < 180);
    }

    [Fact]
    public void PanningRatio_ShouldScaleScrollDelta()
    {
        var viewer = CreateConfiguredViewer(horizontalOffset: 100, verticalOffset: 200);
        viewer.PanningMode = PanningMode.VerticalOnly;
        viewer.PanningRatio = 0.5;

        RaisePanGesture(viewer, start: new Point(40, 40), move: new Point(40, 80), pointerId: 22);

        Assert.Equal(180, viewer.VerticalOffset, precision: 3);
    }

    [Fact]
    public void PanningDeceleration_ShouldAffectInertiaProjection()
    {
        var fastStop = CreateConfiguredViewer(verticalOffset: 100);
        var slowStop = CreateConfiguredViewer(verticalOffset: 100);
        fastStop.IsScrollInertiaEnabled = true;
        slowStop.IsScrollInertiaEnabled = true;
        fastStop.PanningMode = PanningMode.VerticalOnly;
        slowStop.PanningMode = PanningMode.VerticalOnly;
        fastStop.PanningDeceleration = 0.10;
        slowStop.PanningDeceleration = 0.01;

        RaiseTimedGesture(fastStop, pointerId: 23, start: new Point(0, 100), move: new Point(0, 90), downTs: 0, moveTs: 10, upTs: 20);
        RaiseTimedGesture(slowStop, pointerId: 24, start: new Point(0, 100), move: new Point(0, 90), downTs: 0, moveTs: 10, upTs: 20);

        double fastTarget = GetPrivateField<double>(fastStop, "_smoothTargetY");
        double slowTarget = GetPrivateField<double>(slowStop, "_smoothTargetY");

        Assert.True(slowTarget > fastTarget);
    }

    [Fact]
    public void PointerCancel_ShouldStopPanningAndInertia()
    {
        var viewer = CreateConfiguredViewer(verticalOffset: 150);
        viewer.IsScrollInertiaEnabled = true;
        viewer.PanningMode = PanningMode.VerticalOnly;
        viewer.PanningDeceleration = 0.01;

        RaiseTimedGesture(viewer, pointerId: 25, start: new Point(0, 100), move: new Point(0, 80), downTs: 0, moveTs: 10, upTs: 20);
        Assert.True(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));

        viewer.RaiseEvent(CreatePointerCancel(25, new Point(0, 80), timestamp: 30));

        Assert.False(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));
        Assert.False(GetPrivateField<bool>(viewer, "_isPointerPanningActive"));
    }

    [Fact]
    public void MovePackets_BeforeFrame_ShouldAccumulateWithoutLosingDistance()
    {
        var viewer = CreateConfiguredViewer(verticalOffset: 300);
        viewer.PanningMode = PanningMode.VerticalOnly;

        viewer.RaiseEvent(CreatePointerDown(26, new Point(0, 100), timestamp: 0));
        viewer.RaiseEvent(CreatePointerMove(26, new Point(0, 90), timestamp: 10));
        viewer.RaiseEvent(CreatePointerMove(26, new Point(0, 70), timestamp: 20));
        viewer.RaiseEvent(CreatePointerMove(26, new Point(0, 55), timestamp: 30));

        Assert.Equal(300, viewer.VerticalOffset, precision: 3);
        Assert.True(GetPrivateField<bool>(viewer, "_hasPendingPointerPanningDelta"));
        Assert.Equal(45, GetPrivateField<double>(viewer, "_pendingPointerPanningVerticalDelta"), precision: 3);

        viewer.RaiseEvent(CreatePointerUp(26, new Point(0, 55), timestamp: 40));

        Assert.Equal(345, viewer.VerticalOffset, precision: 3);
        Assert.False(GetPrivateField<bool>(viewer, "_hasPendingPointerPanningDelta"));
    }

    [Fact]
    public void CompositionFrame_ShouldApplyAccumulatedDeltaOnlyOnce()
    {
        var viewer = CreateConfiguredViewer(verticalOffset: 300);
        viewer.PanningMode = PanningMode.VerticalOnly;
        int scrollChangedCount = 0;
        viewer.ScrollChanged += (_, _) => scrollChangedCount++;

        viewer.RaiseEvent(CreatePointerDown(27, new Point(0, 100), timestamp: 0));
        viewer.RaiseEvent(CreatePointerMove(27, new Point(0, 90), timestamp: 10));
        viewer.RaiseEvent(CreatePointerMove(27, new Point(0, 70), timestamp: 20));

        Assert.Equal(0, scrollChangedCount);
        InvokePrivate(viewer, "OnPointerPanningCoalesceTick", null, EventArgs.Empty);

        Assert.Equal(330, viewer.VerticalOffset, precision: 3);
        Assert.Equal(1, scrollChangedCount);

        InvokePrivate(viewer, "OnPointerPanningCoalesceTick", null, EventArgs.Empty);

        Assert.Equal(330, viewer.VerticalOffset, precision: 3);
        Assert.Equal(1, scrollChangedCount);
        viewer.RaiseEvent(CreatePointerUp(27, new Point(0, 70), timestamp: 30));
    }

    [Fact]
    public void MoveVelocity_ShouldContinueUsingEachEventTimestamp()
    {
        var viewer = CreateConfiguredViewer(verticalOffset: 300);
        viewer.PanningMode = PanningMode.VerticalOnly;

        viewer.RaiseEvent(CreatePointerDown(28, new Point(0, 100), timestamp: 0));
        viewer.RaiseEvent(CreatePointerMove(28, new Point(0, 90), timestamp: 10));
        viewer.RaiseEvent(CreatePointerMove(28, new Point(0, 80), timestamp: 20));

        Assert.Equal(-0.5775, GetPrivateField<double>(viewer, "_pointerPanningVelocityY"), precision: 4);
        viewer.RaiseEvent(CreatePointerCancel(28, new Point(0, 80), timestamp: 30));
    }

    [Fact]
    public void PointerCancel_ShouldDiscardPendingFrameDelta()
    {
        var viewer = CreateConfiguredViewer(verticalOffset: 300);
        viewer.PanningMode = PanningMode.VerticalOnly;

        viewer.RaiseEvent(CreatePointerDown(29, new Point(0, 100), timestamp: 0));
        viewer.RaiseEvent(CreatePointerMove(29, new Point(0, 60), timestamp: 10));
        viewer.RaiseEvent(CreatePointerCancel(29, new Point(0, 60), timestamp: 20));

        Assert.False(GetPrivateField<bool>(viewer, "_hasPendingPointerPanningDelta"));
        InvokePrivate(viewer, "OnPointerPanningCoalesceTick", null, EventArgs.Empty);
        Assert.Equal(300, viewer.VerticalOffset, precision: 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedViewer_InnerConsumesMove_ReleaseOrCancelCleansBothCandidates(bool cancel)
    {
        var (outer, inner, source) = CreateNestedConfiguredViewers(
            outerVerticalOffset: 300,
            innerVerticalOffset: 300);
        const uint pointerId = 30;

        source.RaiseEvent(CreatePointerDown(pointerId, new Point(100, 100), timestamp: 0));
        source.RaiseEvent(CreatePointerMove(pointerId, new Point(100, 70), timestamp: 10));

        Assert.True(GetPrivateField<bool>(inner, "_isPointerPanningActive"));

        if (cancel)
        {
            source.RaiseEvent(CreatePointerCancel(pointerId, new Point(100, 70), timestamp: 20));
            Assert.Equal(300, inner.VerticalOffset, precision: 3);
        }
        else
        {
            source.RaiseEvent(CreatePointerUp(pointerId, new Point(100, 70), timestamp: 20));
            Assert.True(inner.VerticalOffset > 300);
        }

        Assert.False(GetPrivateField<bool>(inner, "_isPointerPanningActive"));
        Assert.False(GetPrivateField<bool>(outer, "_isPointerPanningActive"));
        Assert.False(GetPrivateField<bool>(inner, "_hasPendingPointerPanningDelta"));
        Assert.False(GetPrivateField<bool>(outer, "_hasPendingPointerPanningDelta"));
        Assert.Equal(300, outer.VerticalOffset, precision: 3);
    }

    [Fact]
    public void NestedViewer_InnerReachesBottom_ContinuedDragChainsToOuterWithoutInnerRubberBand()
    {
        var (outer, inner, source) = CreateNestedConfiguredViewers(
            outerVerticalOffset: 300,
            innerVerticalOffset: 790,
            innerExtentHeight: 1000,
            innerViewportHeight: 200);
        const uint pointerId = 31;

        source.RaiseEvent(CreatePointerDown(pointerId, new Point(100, 100), timestamp: 0));
        source.RaiseEvent(CreatePointerMove(pointerId, new Point(100, 80), timestamp: 10));

        // Model the display frame that commits the first packet at the inner boundary.
        InvokePrivate(inner, "OnPointerPanningCoalesceTick", null, EventArgs.Empty);
        Assert.Equal(inner.ScrollableHeight, inner.VerticalOffset, precision: 3);
        Assert.Equal(0, GetPrivateField<double>(inner, "_overscrollY"), precision: 3);

        source.RaiseEvent(CreatePointerMove(pointerId, new Point(100, 50), timestamp: 20));
        source.RaiseEvent(CreatePointerUp(pointerId, new Point(100, 50), timestamp: 30));

        Assert.Equal(inner.ScrollableHeight, inner.VerticalOffset, precision: 3);
        Assert.Equal(0, GetPrivateField<double>(inner, "_overscrollY"), precision: 3);
        Assert.True(outer.VerticalOffset > 300);
        Assert.False(GetPrivateField<bool>(inner, "_isPointerPanningActive"));
        Assert.False(GetPrivateField<bool>(outer, "_isPointerPanningActive"));
    }

    [Fact]
    public void NestedViewer_OneMoveCrossesBoundary_RemainderScrollsOuter()
    {
        var (outer, inner, source) = CreateNestedConfiguredViewers(
            outerVerticalOffset: 300,
            innerVerticalOffset: 790,
            innerExtentHeight: 1000,
            innerViewportHeight: 200);
        const uint pointerId = 33;

        source.RaiseEvent(CreatePointerDown(pointerId, new Point(100, 100), timestamp: 0));
        source.RaiseEvent(CreatePointerMove(pointerId, new Point(100, 50), timestamp: 10));
        source.RaiseEvent(CreatePointerUp(pointerId, new Point(100, 50), timestamp: 20));

        Assert.Equal(inner.ScrollableHeight, inner.VerticalOffset, precision: 3);
        Assert.Equal(340, outer.VerticalOffset, precision: 3);
        Assert.Equal(0, GetPrivateField<double>(inner, "_overscrollY"), precision: 3);
        Assert.False(GetPrivateField<bool>(inner, "_isPointerPanningActive"));
        Assert.False(GetPrivateField<bool>(outer, "_isPointerPanningActive"));
    }

    [Fact]
    public void NestedViewer_HorizontalInnerDoesNotStealDominantVerticalDragFromOuter()
    {
        var (outer, inner, source) = CreateNestedConfiguredViewers(
            outerVerticalOffset: 300,
            innerHorizontalOffset: 300,
            innerExtentWidth: 1000,
            innerViewportWidth: 200,
            innerExtentHeight: 200,
            innerViewportHeight: 200);
        inner.PanningMode = PanningMode.HorizontalOnly;
        outer.PanningMode = PanningMode.VerticalOnly;
        const uint pointerId = 32;

        source.RaiseEvent(CreatePointerDown(pointerId, new Point(100, 100), timestamp: 0));
        // One DIP of horizontal contact noise must not let the horizontal child
        // steal a clearly vertical drag from its scrollable vertical ancestor.
        source.RaiseEvent(CreatePointerMove(pointerId, new Point(99, 70), timestamp: 10));
        source.RaiseEvent(CreatePointerUp(pointerId, new Point(99, 70), timestamp: 20));

        Assert.Equal(300, inner.HorizontalOffset, precision: 3);
        Assert.True(outer.VerticalOffset > 300);
        Assert.False(GetPrivateField<bool>(inner, "_isPointerPanningActive"));
        Assert.False(GetPrivateField<bool>(outer, "_isPointerPanningActive"));
    }

    private static ScrollViewer CreateConfiguredViewer(
        double horizontalOffset = 0,
        double verticalOffset = 0,
        double extentWidth = 2000,
        double extentHeight = 2000,
        double viewportWidth = 200,
        double viewportHeight = 200)
    {
        var viewer = new ScrollViewer();
        viewer.IsScrollInertiaEnabled = false;
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        viewer.Arrange(new Rect(0, 0, viewportWidth, viewportHeight));

        SetPrivateField(viewer, "_extentWidth", extentWidth);
        SetPrivateField(viewer, "_extentHeight", extentHeight);
        SetPrivateField(viewer, "_viewportWidth", viewportWidth);
        SetPrivateField(viewer, "_viewportHeight", viewportHeight);
        SetPrivateField(viewer, "_horizontalOffset", horizontalOffset);
        SetPrivateField(viewer, "_verticalOffset", verticalOffset);
        SetPrivateField(viewer, "_requestedHorizontalOffset", horizontalOffset);
        SetPrivateField(viewer, "_requestedVerticalOffset", verticalOffset);
        SetPrivateField(viewer, "_smoothTargetX", horizontalOffset);
        SetPrivateField(viewer, "_smoothTargetY", verticalOffset);
        return viewer;
    }

    private static (ScrollViewer Outer, ScrollViewer Inner, Border Source) CreateNestedConfiguredViewers(
        double outerVerticalOffset,
        double innerVerticalOffset = 0,
        double innerHorizontalOffset = 0,
        double innerExtentWidth = 200,
        double innerExtentHeight = 2000,
        double innerViewportWidth = 200,
        double innerViewportHeight = 200)
    {
        var source = new Border { Width = 200, Height = 200 };
        var inner = new ScrollViewer
        {
            Content = source,
            Width = 200,
            Height = 200,
            IsScrollInertiaEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            PanningMode = PanningMode.VerticalOnly
        };
        var outer = new ScrollViewer
        {
            Content = inner,
            Width = 200,
            Height = 200,
            IsScrollInertiaEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            PanningMode = PanningMode.VerticalOnly
        };

        outer.Measure(new Size(200, 200));
        outer.Arrange(new Rect(0, 0, 200, 200));

        ConfigureViewerMetrics(
            inner,
            horizontalOffset: innerHorizontalOffset,
            verticalOffset: innerVerticalOffset,
            extentWidth: innerExtentWidth,
            extentHeight: innerExtentHeight,
            viewportWidth: innerViewportWidth,
            viewportHeight: innerViewportHeight);
        ConfigureViewerMetrics(
            outer,
            horizontalOffset: 0,
            verticalOffset: outerVerticalOffset,
            extentWidth: 200,
            extentHeight: 2000,
            viewportWidth: 200,
            viewportHeight: 200);

        return (outer, inner, source);
    }

    private static void ConfigureViewerMetrics(
        ScrollViewer viewer,
        double horizontalOffset,
        double verticalOffset,
        double extentWidth,
        double extentHeight,
        double viewportWidth,
        double viewportHeight)
    {
        SetPrivateField(viewer, "_extentWidth", extentWidth);
        SetPrivateField(viewer, "_extentHeight", extentHeight);
        SetPrivateField(viewer, "_viewportWidth", viewportWidth);
        SetPrivateField(viewer, "_viewportHeight", viewportHeight);
        SetPrivateField(viewer, "_horizontalOffset", horizontalOffset);
        SetPrivateField(viewer, "_verticalOffset", verticalOffset);
        SetPrivateField(viewer, "_requestedHorizontalOffset", horizontalOffset);
        SetPrivateField(viewer, "_requestedVerticalOffset", verticalOffset);
        SetPrivateField(viewer, "_smoothTargetX", horizontalOffset);
        SetPrivateField(viewer, "_smoothTargetY", verticalOffset);
    }

    private static void RaisePanGesture(ScrollViewer viewer, Point start, Point move, uint pointerId)
    {
        RaiseTimedGesture(viewer, pointerId, start, move, downTs: 0, moveTs: 16, upTs: 32);
    }

    private static void RaiseTimedGesture(ScrollViewer viewer, uint pointerId, Point start, Point move, int downTs, int moveTs, int upTs)
    {
        viewer.RaiseEvent(CreatePointerDown(pointerId, start, downTs));
        viewer.RaiseEvent(CreatePointerMove(pointerId, move, moveTs));
        viewer.RaiseEvent(CreatePointerUp(pointerId, move, upTs));
    }

    private static PointerDownEventArgs CreatePointerDown(uint pointerId, Point position, int timestamp = 0)
    {
        var point = CreateTouchPoint(pointerId, position, inContact: true, timestamp: timestamp);
        return new PointerDownEventArgs(point, ModifierKeys.None, timestamp) { RoutedEvent = UIElement.PointerDownEvent };
    }

    private static PointerMoveEventArgs CreatePointerMove(uint pointerId, Point position, int timestamp)
    {
        var point = CreateTouchPoint(pointerId, position, inContact: true, timestamp: timestamp);
        return new PointerMoveEventArgs(point, ModifierKeys.None, timestamp) { RoutedEvent = UIElement.PointerMoveEvent };
    }

    private static PointerUpEventArgs CreatePointerUp(uint pointerId, Point position, int timestamp)
    {
        var point = CreateTouchPoint(pointerId, position, inContact: false, timestamp: timestamp);
        return new PointerUpEventArgs(point, ModifierKeys.None, timestamp) { RoutedEvent = UIElement.PointerUpEvent };
    }

    private static PointerCancelEventArgs CreatePointerCancel(uint pointerId, Point position, int timestamp)
    {
        var point = CreateTouchPoint(pointerId, position, inContact: false, timestamp: timestamp);
        return new PointerCancelEventArgs(point, ModifierKeys.None, timestamp) { RoutedEvent = UIElement.PointerCancelEvent };
    }

    private static PointerPoint CreateTouchPoint(uint pointerId, Point position, bool inContact, int timestamp)
    {
        return new PointerPoint(
            pointerId,
            position,
            PointerDeviceType.Touch,
            inContact,
            new PointerPointProperties { IsPrimary = true, PointerUpdateKind = PointerUpdateKind.Other },
            (ulong)timestamp,
            0);
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

    private static object? InvokePrivate(ScrollViewer viewer, string methodName, params object?[] arguments)
    {
        var method = typeof(ScrollViewer).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(viewer, arguments);
    }
}
