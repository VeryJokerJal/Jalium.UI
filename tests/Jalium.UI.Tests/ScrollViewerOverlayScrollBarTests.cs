using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Input.Internal.Gestures;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ScrollViewerOverlayScrollBarTests
{
    [Fact]
    public void VerticalOverlayScrollBar_OnlyVisibleTwoDipIndicatorIsHitTestable()
    {
        var scrollBar = new ScrollBar
        {
            IsOverlayStyle = true,
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 1000,
            ViewportSize = 200,
            Value = 0
        };

        scrollBar.Measure(new Size(40, 220));
        scrollBar.Arrange(new Rect(0, 0, 40, 220));

        var lineUp = Assert.IsType<RepeatButton>(scrollBar.GetVisualChild(0));
        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var lineDown = Assert.IsType<RepeatButton>(scrollBar.GetVisualChild(2));
        var thumb = Assert.IsType<Thumb>(track.Thumb);
        var indicator = Assert.IsType<Border>(thumb.GetVisualChild(0));

        Assert.Equal(36, thumb.Padding.Left, precision: 3);
        Assert.Equal(2, indicator.RenderSize.Width, precision: 3);
        Assert.Equal(1, track.Opacity, precision: 3);
        Assert.True(track.IsHitTestVisible);
        var indicatorHit = scrollBar.HitTest(new Point(37, 10));
        Assert.NotNull(indicatorHit);
        Assert.True(HasVisualAncestor<Thumb>(indicatorHit!.VisualHit));

        // The 40-DIP host only provides room for the 8-DIP long-press visual.
        // It must not widen the initial touch target beyond the visible 2-DIP strip.
        Assert.Null(scrollBar.HitTest(new Point(35.9, 10)));
        Assert.Null(scrollBar.HitTest(new Point(38.1, 10)));

        ForceOverlayProgress(scrollBar, progress: 1.0, crossAxisSize: 40);
        scrollBar.Measure(new Size(40, 220));
        scrollBar.Arrange(new Rect(0, 0, 40, 220));

        Assert.Equal(Visibility.Collapsed, lineUp.Visibility);
        Assert.Equal(Visibility.Collapsed, lineDown.Visibility);
        Assert.False(lineUp.IsHitTestVisible);
        Assert.False(lineDown.IsHitTestVisible);
        Assert.False(track.DecreaseRepeatButton!.IsHitTestVisible);
        Assert.False(track.IncreaseRepeatButton!.IsHitTestVisible);

        Assert.Equal(0, track.VisualBounds.X, precision: 3);
        Assert.Equal(3, track.VisualBounds.Y, precision: 3);
        Assert.Equal(40, track.RenderSize.Width, precision: 3);
        Assert.Equal(214, track.RenderSize.Height, precision: 3);

        // Thumb keeps a 40-DIP transparent layout host while its template draws
        // and hit-tests only the trailing 2 DIPs (40 - 36 leading - 2 edge inset).
        Assert.Equal(40, thumb.RenderSize.Width, precision: 3);
        Assert.Equal(36, thumb.Padding.Left, precision: 3);
        Assert.Equal(2, thumb.Padding.Right, precision: 3);
        Assert.Equal(2, indicator.RenderSize.Width, precision: 3);
        Assert.True(thumb.RenderSize.Height >= 40);
        Assert.Equal(0, track.Opacity, precision: 3);
        Assert.False(track.IsHitTestVisible);

        Assert.Null(scrollBar.HitTest(new Point(20, 10)));
        Assert.Null(scrollBar.HitTest(new Point(20, 200)));
    }

    [Fact]
    public void HorizontalOverlayScrollBar_OnlyVisibleTwoDipIndicatorIsHitTestable()
    {
        var scrollBar = new ScrollBar
        {
            IsOverlayStyle = true,
            Orientation = Orientation.Horizontal,
            Minimum = 0,
            Maximum = 1000,
            ViewportSize = 200,
            Value = 0
        };

        scrollBar.Measure(new Size(220, 40));
        scrollBar.Arrange(new Rect(0, 0, 220, 40));

        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var thumb = Assert.IsType<Thumb>(track.Thumb);
        var indicator = Assert.IsType<Border>(thumb.GetVisualChild(0));

        Assert.Equal(36, thumb.Padding.Top, precision: 3);
        Assert.Equal(2, indicator.RenderSize.Height, precision: 3);

        var indicatorHit = scrollBar.HitTest(new Point(10, 37));
        Assert.NotNull(indicatorHit);
        Assert.True(HasVisualAncestor<Thumb>(indicatorHit!.VisualHit));
        Assert.Null(scrollBar.HitTest(new Point(10, 35.9)));
        Assert.Null(scrollBar.HitTest(new Point(10, 38.1)));

        ForceOverlayProgress(scrollBar, progress: 1.0, crossAxisSize: 40);
        scrollBar.Measure(new Size(220, 40));
        scrollBar.Arrange(new Rect(0, 0, 220, 40));

        Assert.Equal(3, track.VisualBounds.X, precision: 3);
        Assert.Equal(0, track.VisualBounds.Y, precision: 3);
        Assert.Equal(214, track.RenderSize.Width, precision: 3);
        Assert.Equal(40, track.RenderSize.Height, precision: 3);
        Assert.Equal(40, thumb.RenderSize.Height, precision: 3);
        Assert.Equal(36, thumb.Padding.Top, precision: 3);
        Assert.Equal(2, thumb.Padding.Bottom, precision: 3);
        Assert.Equal(2, indicator.RenderSize.Height, precision: 3);
        Assert.True(thumb.RenderSize.Width >= 40);
        Assert.Equal(0, track.Opacity, precision: 3);
        Assert.False(track.IsHitTestVisible);
    }

    [Fact]
    public void OverlayScrollBars_DoNotReserveViewportSpace_AndEmptyGuttersPassThrough()
    {
        var content = new Border
        {
            Width = 300,
            Height = 300,
            Background = new SolidColorBrush(Color.Transparent)
        };
        var viewer = new ScrollViewer
        {
            Content = content,
            Width = 200,
            Height = 120,
            IsOverlayScrollBarEnabled = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        viewer.Measure(new Size(200, 120));
        viewer.Arrange(new Rect(0, 0, 200, 120));

        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        var horizontalBar = GetPrivateField<ScrollBar>(viewer, "_horizontalScrollBar");

        Assert.Equal(200, viewer.ViewportWidth, precision: 3);
        Assert.Equal(120, viewer.ViewportHeight, precision: 3);
        Assert.Equal(new Rect(160, 0, 40, 120), verticalBar.VisualBounds);
        Assert.Equal(new Rect(0, 80, 200, 40), horizontalBar.VisualBounds);
        Assert.True(verticalBar.IsOverlayStyle);
        Assert.True(horizontalBar.IsOverlayStyle);

        // Both transparent layout hosts overlap this point, but it is outside
        // each visible indicator. The empty overlay gutter must fall through.
        var gutterHit = viewer.HitTest(new Point(170, 110));
        Assert.NotNull(gutterHit);
        Assert.False(HasVisualAncestor<ScrollBar>(gutterHit!.VisualHit));

        // Even alongside the proportional Thumb, only its visible 2-DIP strip
        // is interactive; the adjacent transparent host passes through.
        var adjacentHit = viewer.HitTest(new Point(195.9, 10));
        Assert.NotNull(adjacentHit);
        Assert.False(HasVisualAncestor<ScrollBar>(adjacentHit!.VisualHit));

        var thumbHit = viewer.HitTest(new Point(197, 10));
        Assert.NotNull(thumbHit);
        Assert.True(
            HasVisualAncestor<Thumb>(thumbHit!.VisualHit),
            $"Expected outer Thumb, actual hit: {thumbHit.VisualHit.GetType().FullName}");
    }

    [Fact]
    public void NestedOverlayScrollBars_OverlappingEdgePrefersInnerThumb()
    {
        var inner = new ScrollViewer
        {
            Content = new Border { Width = 200, Height = 300 },
            Width = 200,
            Height = 120,
            VerticalAlignment = VerticalAlignment.Top,
            IsOverlayScrollBarEnabled = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var outerContent = new Grid { Width = 200, Height = 300 };
        outerContent.Children.Add(inner);
        var outer = new ScrollViewer
        {
            Content = outerContent,
            Width = 200,
            Height = 120,
            IsOverlayScrollBarEnabled = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        outer.Measure(new Size(200, 120));
        outer.Arrange(new Rect(0, 0, 200, 120));

        var innerBar = GetPrivateField<ScrollBar>(inner, "_verticalScrollBar");
        var outerBar = GetPrivateField<ScrollBar>(outer, "_verticalScrollBar");
        Assert.Equal(Visibility.Visible, innerBar.Visibility);
        Assert.Equal(Visibility.Visible, outerBar.Visibility);

        var directBarHit = innerBar.HitTest(new Point(197, 10));
        Assert.True(
            directBarHit != null,
            $"Direct bar hit was null; bar={innerBar.VisualBounds}/{innerBar.RenderSize}, " +
            $"track={innerBar.Track.VisualBounds}, thumb={innerBar.Track.Thumb?.VisualBounds}");

        var directInnerHit = inner.HitTest(new Point(197, 10));
        Assert.True(
            directInnerHit != null,
            $"Direct inner hit was null; inner={inner.VisualBounds}/{inner.RenderSize}, " +
            $"bar={innerBar.VisualBounds}/{innerBar.RenderSize}, track={innerBar.Track.VisualBounds}, " +
            $"thumb={innerBar.Track.Thumb?.VisualBounds}, opacity={innerBar.Track.Opacity}");
        Assert.True(
            HasVisualAncestor<Thumb>(directInnerHit!.VisualHit),
            $"Expected direct inner Thumb, actual hit: {directInnerHit.VisualHit.GetType().FullName}; " +
            $"inner={inner.VisualBounds}, bar={innerBar.VisualBounds}, thumb={innerBar.Track.Thumb?.VisualBounds}");

        var hit = outer.HitTest(new Point(197, 10));
        Assert.NotNull(hit);
        Assert.True(
            HasVisualAncestor<Thumb>(hit!.VisualHit),
            $"Expected nested Thumb, actual hit: {hit.VisualHit.GetType().FullName}");
        Assert.True(IsVisualDescendantOf(hit.VisualHit, innerBar));
        Assert.False(IsVisualDescendantOf(hit.VisualHit, outerBar));
    }

    [Fact]
    public void DesktopScrollBar_StillReservesItsTwelveDipGutter()
    {
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = 100, Height = 300 },
            Width = 200,
            Height = 120,
            IsOverlayScrollBarEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        viewer.Measure(new Size(200, 120));
        viewer.Arrange(new Rect(0, 0, 200, 120));

        Assert.Equal(188, viewer.ViewportWidth, precision: 3);
        Assert.Equal(120, viewer.ViewportHeight, precision: 3);
    }

    [Fact]
    public void LeavingOverlayStyle_RestoresCustomThumbPadding()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 100,
            ViewportSize = 20
        };
        var thumb = Assert.IsType<Thumb>(scrollBar.Track.Thumb);
        var track = scrollBar.Track;
        var customPadding = new Thickness(4, 1, 6, 2);
        thumb.Padding = customPadding;
        track.Opacity = 0.65;
        track.IsHitTestVisible = false;

        scrollBar.IsOverlayStyle = true;
        scrollBar.Measure(new Size(40, 220));
        scrollBar.Arrange(new Rect(0, 0, 40, 220));
        Assert.NotEqual(customPadding, thumb.Padding);

        scrollBar.IsOverlayStyle = false;

        Assert.Equal(customPadding, thumb.Padding);
        Assert.Equal(0.65, track.Opacity, precision: 3);
        Assert.False(track.IsHitTestVisible);
    }

    [Fact]
    public void OverlayThumb_LongPressExpandsFromTwoToEightDips_UntilTouchUp()
    {
        var scrollBar = CreateOverlayScrollBar(Orientation.Vertical);
        scrollBar.Measure(new Size(40, 220));
        scrollBar.Arrange(new Rect(0, 0, 40, 220));

        var thumb = Assert.IsType<Thumb>(scrollBar.Track.Thumb);
        var indicator = Assert.IsType<Border>(thumb.GetVisualChild(0));
        const int pointerId = 701;

        RaiseTouchDown(thumb, pointerId, new Point(37, 10));
        try
        {
            Assert.Equal(2, indicator.RenderSize.Width, precision: 3);
            Assert.Equal(36, thumb.Padding.Left, precision: 3);

            scrollBar.AdvanceOverlayLongPressClockForTesting(GestureRecognizer.HoldThresholdMs + 1);
            scrollBar.Arrange(new Rect(0, 0, 40, 220));

            Assert.Equal(8, indicator.RenderSize.Width, precision: 3);
            Assert.Equal(30, thumb.Padding.Left, precision: 3);
            Assert.Null(scrollBar.HitTest(new Point(31, 10)));
            Assert.True(HasVisualAncestor<Thumb>(scrollBar.HitTest(new Point(37, 10))!.VisualHit));

            RaiseTouchUp(thumb, pointerId, new Point(37, 10));
            scrollBar.Arrange(new Rect(0, 0, 40, 220));

            Assert.Equal(2, indicator.RenderSize.Width, precision: 3);
            Assert.Equal(36, thumb.Padding.Left, precision: 3);
        }
        finally
        {
            if (Touch.GetDevice(pointerId) != null)
            {
                RaiseTouchUp(thumb, pointerId, new Point(37, 10));
            }
        }
    }

    [Fact]
    public void OverlayThumb_MovementBeforeHoldThreshold_DoesNotScrollOrExpand()
    {
        var scrollBar = CreateOverlayScrollBar(Orientation.Vertical);
        scrollBar.Measure(new Size(40, 220));
        scrollBar.Arrange(new Rect(0, 0, 40, 220));

        var thumb = Assert.IsType<Thumb>(scrollBar.Track.Thumb);
        var indicator = Assert.IsType<Border>(thumb.GetVisualChild(0));
        const int pointerId = 702;
        var initialValue = scrollBar.Value;

        RaiseTouchDown(thumb, pointerId, new Point(37, 10));
        try
        {
            RaiseTouchMove(thumb, pointerId, new Point(37, 30));
            Assert.Equal(initialValue, scrollBar.Value);

            scrollBar.AdvanceOverlayLongPressClockForTesting(GestureRecognizer.HoldThresholdMs + 1);
            scrollBar.Arrange(new Rect(0, 0, 40, 220));

            Assert.Equal(2, indicator.RenderSize.Width, precision: 3);
            Assert.Equal(36, thumb.Padding.Left, precision: 3);
            Assert.Equal(initialValue, scrollBar.Value);
        }
        finally
        {
            if (Touch.GetDevice(pointerId) != null)
            {
                RaiseTouchUp(thumb, pointerId, new Point(37, 30));
            }
        }
    }

    [Fact]
    public void OverlayThumb_LongPressThenMoveOutsideThumb_TracksAndStaysEightDipsUntilTouchUp()
    {
        var scrollBar = CreateOverlayScrollBar(Orientation.Vertical);
        scrollBar.Measure(new Size(40, 220));
        scrollBar.Arrange(new Rect(0, 0, 40, 220));

        var thumb = Assert.IsType<Thumb>(scrollBar.Track.Thumb);
        var indicator = Assert.IsType<Border>(thumb.GetVisualChild(0));
        const int pointerId = 703;
        var initialValue = scrollBar.Value;

        RaiseTouchDown(thumb, pointerId, new Point(37, 10));
        try
        {
            scrollBar.AdvanceOverlayLongPressClockForTesting(GestureRecognizer.HoldThresholdMs + 1);
            scrollBar.Arrange(new Rect(0, 0, 40, 220));

            Assert.Equal(8, indicator.RenderSize.Width, precision: 3);
            Assert.Equal(30, thumb.Padding.Left, precision: 3);
            Assert.Same(thumb, Assert.IsAssignableFrom<TouchDevice>(Touch.GetDevice(pointerId)).Captured);
            Assert.Same(thumb, UIElement.GetTouchCapture(pointerId));

            // The contact is now outside both the original Thumb segment and
            // the entire 40-DIP overlay host. Touch capture must keep routing
            // movement to Thumb after the long press has unlocked dragging.
            RaiseTouchMove(thumb, pointerId, new Point(-20, 90));
            scrollBar.Arrange(new Rect(0, 0, 40, 220));

            var firstMovedValue = scrollBar.Value;
            Assert.True(firstMovedValue > initialValue);
            Assert.Equal(8, indicator.RenderSize.Width, precision: 3);
            Assert.Equal(30, thumb.Padding.Left, precision: 3);
            Assert.Same(thumb, Assert.IsAssignableFrom<TouchDevice>(Touch.GetDevice(pointerId)).Captured);
            Assert.Same(thumb, UIElement.GetTouchCapture(pointerId));

            RaiseTouchMove(thumb, pointerId, new Point(-30, 120));
            scrollBar.Arrange(new Rect(0, 0, 40, 220));

            Assert.True(scrollBar.Value > firstMovedValue);
            Assert.Equal(8, indicator.RenderSize.Width, precision: 3);
            Assert.True(thumb.IsDragging);
            Assert.True(scrollBar.IsThumbDragging);

            var device = Assert.IsAssignableFrom<TouchDevice>(Touch.GetDevice(pointerId));
            RaiseTouchUp(thumb, pointerId, new Point(-30, 120));

            Assert.Null(device.Captured);
            Assert.False(thumb.IsDragging);
            Assert.False(scrollBar.IsThumbDragging);
            Assert.False(GetPrivateField<bool>(scrollBar, "_isOverlayLongPressExpanded"));
            Assert.Equal(36, thumb.Padding.Left, precision: 3);
        }
        finally
        {
            if (Touch.GetDevice(pointerId) != null)
            {
                RaiseTouchUp(thumb, pointerId, new Point(-30, 120));
            }
        }
    }

    [Fact]
    public void OverlayThumb_SecondTouchDoesNotStealOrCollapseActiveGesture()
    {
        var scrollBar = CreateOverlayScrollBar(Orientation.Vertical);
        scrollBar.Measure(new Size(40, 220));
        scrollBar.Arrange(new Rect(0, 0, 40, 220));

        var thumb = Assert.IsType<Thumb>(scrollBar.Track.Thumb);
        var indicator = Assert.IsType<Border>(thumb.GetVisualChild(0));
        const int activePointerId = 704;
        const int secondPointerId = 705;
        var initialValue = scrollBar.Value;

        RaiseTouchDown(thumb, activePointerId, new Point(37, 10));
        try
        {
            scrollBar.AdvanceOverlayLongPressClockForTesting(GestureRecognizer.HoldThresholdMs + 1);
            scrollBar.Arrange(new Rect(0, 0, 40, 220));
            Assert.Equal(8, indicator.RenderSize.Width, precision: 3);

            RaiseTouchDown(thumb, secondPointerId, new Point(37, 10));
            Assert.Equal(activePointerId, GetPrivateField<int>(scrollBar, "_overlayThumbTouchId"));
            Assert.True(GetPrivateField<bool>(scrollBar, "_isOverlayThumbTouchDragUnlocked"));
            Assert.Equal(8, indicator.RenderSize.Width, precision: 3);
            Assert.Same(thumb, UIElement.GetTouchCapture(activePointerId));
            Assert.Null(UIElement.GetTouchCapture(secondPointerId));

            RaiseTouchUp(thumb, secondPointerId, new Point(37, 10));

            Assert.Equal(activePointerId, GetPrivateField<int>(scrollBar, "_overlayThumbTouchId"));
            Assert.True(GetPrivateField<bool>(scrollBar, "_isOverlayThumbTouchDragUnlocked"));
            Assert.Equal(8, indicator.RenderSize.Width, precision: 3);
            Assert.Same(thumb, UIElement.GetTouchCapture(activePointerId));

            RaiseTouchMove(thumb, activePointerId, new Point(-20, 90));

            Assert.True(scrollBar.Value > initialValue);
            Assert.Equal(8, indicator.RenderSize.Width, precision: 3);
        }
        finally
        {
            if (Touch.GetDevice(secondPointerId) != null)
            {
                RaiseTouchUp(thumb, secondPointerId, new Point(37, 10));
            }

            if (Touch.GetDevice(activePointerId) != null)
            {
                RaiseTouchUp(thumb, activePointerId, new Point(-20, 90));
            }
        }
    }

    [Fact]
    public void OverlayIndicator_HidesAfterTwoIdleSeconds_AndScrollRestoresTwoDips()
    {
        var viewer = CreateScrollableOverlayViewer();
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        var track = verticalBar.Track;
        var thumb = Assert.IsType<Thumb>(track.Thumb);
        var indicator = Assert.IsType<Border>(thumb.GetVisualChild(0));

        Assert.Equal(2000, viewer.EffectiveScrollBarAutoHideDelayMs);
        Assert.False(verticalBar.IsThumbSlim);
        Assert.Equal(2, indicator.RenderSize.Width, precision: 3);
        Assert.Equal(1, track.Opacity, precision: 3);

        ForceViewerAutoHideTimeout(viewer);
        ForceOverlayProgress(verticalBar, progress: 1.0, crossAxisSize: 40);

        Assert.True(verticalBar.IsThumbSlim);
        Assert.Equal(0, track.Opacity, precision: 3);
        Assert.False(track.IsHitTestVisible);
        var hiddenHit = viewer.HitTest(new Point(180, 10));
        Assert.NotNull(hiddenHit);
        Assert.False(HasVisualAncestor<Thumb>(hiddenHit!.VisualHit));

        viewer.ScrollToVerticalOffset(20);
        ForceOverlayProgress(verticalBar, progress: 0.0, crossAxisSize: 40);
        viewer.Arrange(new Rect(0, 0, 200, 120));

        Assert.False(verticalBar.IsThumbSlim);
        Assert.Equal(1, track.Opacity, precision: 3);
        Assert.True(track.IsHitTestVisible);
        Assert.Equal(2, indicator.RenderSize.Width, precision: 3);
    }

    [Fact]
    public void OverlayFade_RepeatedSameTargetRequest_DoesNotRestartAnimation()
    {
        var scrollBar = CreateOverlayScrollBar(Orientation.Vertical);
        scrollBar.Measure(new Size(40, 220));
        scrollBar.Arrange(new Rect(0, 0, 40, 220));

        scrollBar.StartAutoHideVisualTransition(1.0);
        SetPrivateField(scrollBar, "_autoHideVisualAnimStartTick", Environment.TickCount64 - 200);

        // Mirrors ScrollViewer arranging again while the same fade is active.
        scrollBar.StartAutoHideVisualTransition(1.0);
        InvokePrivate(scrollBar, "OnAutoHideVisualTimerTick", null, EventArgs.Empty);

        Assert.Equal(0, scrollBar.Track.Opacity, precision: 3);
        Assert.False(scrollBar.Track.IsHitTestVisible);
    }

    [Fact]
    public void TouchPanning_RevealsOverlayIndicator()
    {
        var viewer = CreateScrollableOverlayViewer();
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        ForceViewerAutoHideTimeout(viewer);
        ForceOverlayProgress(verticalBar, progress: 1.0, crossAxisSize: 40);
        Assert.True(verticalBar.IsThumbSlim);

        viewer.RaiseEvent(CreatePointerDown(501, new Point(80, 80), timestamp: 0));
        viewer.RaiseEvent(CreatePointerMove(501, new Point(80, 50), timestamp: 16));
        viewer.RaiseEvent(CreatePointerUp(501, new Point(80, 50), timestamp: 32));

        Assert.True(viewer.VerticalOffset > 0);
        Assert.False(verticalBar.IsThumbSlim);
        ForceOverlayProgress(verticalBar, progress: 0.0, crossAxisSize: 40);
        Assert.Equal(1, verticalBar.Track.Opacity, precision: 3);
        var remainingIdleMs = GetPrivateField<long>(viewer, "_scrollBarAutoHideDeadlineTick") - Environment.TickCount64;
        Assert.InRange(remainingIdleMs, 1500, 2100);
    }

    [Fact]
    public void PointerDownFromThumb_DoesNotAlsoStartContentPanning()
    {
        var viewer = CreateScrollableOverlayViewer();
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        var thumb = Assert.IsType<Thumb>(verticalBar.Track.Thumb);

        thumb.RaiseEvent(CreatePointerDown(502, new Point(180, 10), timestamp: 0));

        Assert.False(GetPrivateField<bool>(viewer, "_isPointerPanningActive"));
    }

    private static ScrollViewer CreateScrollableOverlayViewer()
    {
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = 200, Height = 600 },
            Width = 200,
            Height = 120,
            IsOverlayScrollBarEnabled = true,
            IsScrollBarAutoHideEnabled = true,
            IsScrollInertiaEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.VerticalOnly
        };

        viewer.Measure(new Size(200, 120));
        viewer.Arrange(new Rect(0, 0, 200, 120));
        return viewer;
    }

    private static ScrollBar CreateOverlayScrollBar(Orientation orientation)
    {
        return new ScrollBar
        {
            IsOverlayStyle = true,
            Orientation = orientation,
            Minimum = 0,
            Maximum = 1000,
            ViewportSize = 200,
            Value = 0
        };
    }

    private static void RaiseTouchDown(UIElement target, int pointerId, Point position)
    {
        var device = Touch.RegisterTouchPoint(pointerId, position, target);
        target.RaiseEvent(new TouchEventArgs(device, Environment.TickCount)
        {
            RoutedEvent = UIElement.TouchDownEvent,
            Source = target
        });
    }

    private static void RaiseTouchUp(UIElement target, int pointerId, Point position)
    {
        Touch.UpdateTouchPoint(pointerId, position);
        var device = Touch.GetDevice(pointerId) ?? Touch.RegisterTouchPoint(pointerId, position, target);
        target.RaiseEvent(new TouchEventArgs(device, Environment.TickCount)
        {
            RoutedEvent = UIElement.TouchUpEvent,
            Source = target
        });

        // A synthetic non-current contact is ignored by Thumb.OnTouchUp. Mirror
        // the platform dispatcher's forced release so static capture state does
        // not leak into the next test.
        UIElement.GetTouchCapture(pointerId)?.ReleaseTouchCapture(device);
        Touch.UnregisterTouchPoint(pointerId);
    }

    private static void RaiseTouchMove(UIElement target, int pointerId, Point position)
    {
        Touch.UpdateTouchPoint(pointerId, position);
        var device = Touch.GetDevice(pointerId) ?? Touch.RegisterTouchPoint(pointerId, position, target);
        target.RaiseEvent(new TouchEventArgs(device, Environment.TickCount)
        {
            RoutedEvent = UIElement.TouchMoveEvent,
            Source = target
        });
    }

    private static PointerDownEventArgs CreatePointerDown(uint pointerId, Point position, int timestamp)
    {
        var point = CreateTouchPoint(pointerId, position, inContact: true, timestamp);
        return new PointerDownEventArgs(point, ModifierKeys.None, timestamp)
        {
            RoutedEvent = UIElement.PointerDownEvent
        };
    }

    private static PointerMoveEventArgs CreatePointerMove(uint pointerId, Point position, int timestamp)
    {
        var point = CreateTouchPoint(pointerId, position, inContact: true, timestamp);
        return new PointerMoveEventArgs(point, ModifierKeys.None, timestamp)
        {
            RoutedEvent = UIElement.PointerMoveEvent
        };
    }

    private static PointerUpEventArgs CreatePointerUp(uint pointerId, Point position, int timestamp)
    {
        var point = CreateTouchPoint(pointerId, position, inContact: false, timestamp);
        return new PointerUpEventArgs(point, ModifierKeys.None, timestamp)
        {
            RoutedEvent = UIElement.PointerUpEvent
        };
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

    private static bool HasVisualAncestor<T>(DependencyObject visual) where T : DependencyObject
    {
        for (Visual? current = visual as Visual; current != null; current = current.VisualParent)
        {
            if (current is T)
                return true;
        }

        return false;
    }

    private static bool IsVisualDescendantOf(DependencyObject visual, DependencyObject ancestor)
    {
        for (Visual? current = visual as Visual; current != null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static void ForceOverlayProgress(ScrollBar scrollBar, double progress, double crossAxisSize)
    {
        var method = typeof(ScrollBar).GetMethod(
            "ApplyAutoHideVisualState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(scrollBar, new object?[] { progress, crossAxisSize, false });
    }

    private static void ForceViewerAutoHideTimeout(ScrollViewer viewer)
    {
        var deadlineField = typeof(ScrollViewer).GetField(
            "_scrollBarAutoHideDeadlineTick",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(deadlineField);
        deadlineField!.SetValue(viewer, long.MinValue);
        InvokePrivate(viewer, "OnScrollBarAutoHideTimerTick", null, EventArgs.Empty);
    }

    private static object? InvokePrivate(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(target, arguments);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(target);
        Assert.NotNull(value);
        return (T)value!;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
