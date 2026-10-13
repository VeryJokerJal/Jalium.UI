using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ScrollViewerMacOSBehaviorTests
{
    [Fact]
    public void PreciseWheel_IntegratesFractionalPacketsWithoutAdditionalInertia()
    {
        var viewer = CreateViewer();
        for (int index = 0; index < 20; index++)
        {
            var wheel = Wheel(0, -0.125);
            Assert.Equal(0, wheel.Delta);
            viewer.RaiseEvent(wheel);
            Assert.True(wheel.Handled);
        }

        Assert.Equal(1, viewer.VerticalOffset, precision: 10);
        Assert.False(IsSmoothScrolling(viewer));
    }

    [Fact]
    public void PreciseDiagonalWheel_MovesBothAxesAtNativeDistance()
    {
        var viewer = CreateViewer();
        viewer.RaiseEvent(Wheel(31.25, -50));

        Assert.Equal(12.5, viewer.HorizontalOffset, precision: 10);
        Assert.Equal(20, viewer.VerticalOffset, precision: 10);
        Assert.False(IsSmoothScrolling(viewer));
    }

    [Fact]
    public void PreciseWheel_InterruptsPendingMouseWheelAnimation()
    {
        var viewer = CreateViewer();
        viewer.RaiseEvent(Wheel(0, -120, precise: false));
        Assert.True(IsSmoothScrolling(viewer));

        viewer.RaiseEvent(Wheel(0, -25));
        Assert.Equal(10, viewer.VerticalOffset, precision: 10);
        Assert.False(IsSmoothScrolling(viewer));
    }

    [Fact]
    public void HorizontalMouseWheel_PreservesVerticalOffsetAndSmoothsOnlyHorizontalMovement()
    {
        var viewer = CreateViewer();
        viewer.ScrollToVerticalOffset(100);
        var wheel = Wheel(120, 0, precise: false);
        viewer.RaiseEvent(wheel);

        Assert.True(wheel.Handled);
        Assert.True(IsSmoothScrolling(viewer));
        Assert.Equal(48, GetField<double>(viewer, "_smoothTargetX"), precision: 3);
        Assert.Equal(100, GetField<double>(viewer, "_smoothTargetY"), precision: 3);
        viewer.IsScrollInertiaEnabled = false;
    }

    [Fact]
    public void HorizontalMouseWheel_ReversesPendingMovementBeforeTheFirstAnimationFrame()
    {
        var inner = CreateViewer();
        var content = new Grid { Width = 600, Height = 1000 };
        content.Children.Add(inner);
        var outer = CreateViewer(content);
        outer.IsScrollInertiaEnabled = false;
        outer.ScrollToHorizontalOffset(100);

        inner.RaiseEvent(Wheel(120, 0, precise: false));
        var reverse = Wheel(-120, 0, precise: false);
        inner.RaiseEvent(reverse);

        Assert.True(reverse.Handled);
        Assert.Equal(0, GetField<double>(inner, "_smoothTargetX"));
        Assert.Equal(100, outer.HorizontalOffset);
        inner.IsScrollInertiaEnabled = false;
    }

    [Fact]
    public void ShiftWheel_MovesHorizontallyWithoutDuplicatingNativeRemapping()
    {
        var viewer = CreateViewer();
        viewer.IsScrollInertiaEnabled = false;
        viewer.RaiseEvent(Wheel(0, -120, precise: false, ModifierKeys.Shift));
        Assert.Equal(48, viewer.HorizontalOffset, precision: 3);
        Assert.Equal(0, viewer.VerticalOffset);

        viewer.RaiseEvent(Wheel(120, 0, precise: false, ModifierKeys.Shift));
        Assert.Equal(96, viewer.HorizontalOffset, precision: 3);
        Assert.Equal(0, viewer.VerticalOffset);
    }

    [Fact]
    public void DiagonalWheel_BubblesOnlyTheAxisAtTheChildBoundary()
    {
        var inner = CreateViewer();
        var content = new Grid { Width = 600, Height = 1000 };
        content.Children.Add(inner);
        var outer = CreateViewer(content);
        outer.ScrollToHorizontalOffset(100);
        outer.ScrollToVerticalOffset(100);
        inner.ScrollToVerticalOffset(inner.ScrollableHeight);

        var wheel = Wheel(25, -50);
        inner.RaiseEvent(wheel);

        Assert.True(wheel.Handled);
        Assert.Equal(10, inner.HorizontalOffset, precision: 3);
        Assert.Equal(inner.ScrollableHeight, inner.VerticalOffset, precision: 3);
        Assert.Equal(100, outer.HorizontalOffset, precision: 3);
        Assert.Equal(120, outer.VerticalOffset, precision: 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreciseWheel_ReachingPhysicalEndAllowsTheNextPacketToBubble(bool vertical)
    {
        var inner = CreateViewer();
        var content = new Grid { Width = 600, Height = 1000 };
        content.Children.Add(inner);
        var outer = CreateViewer(content);
        var end = vertical ? inner.ScrollableHeight : inner.ScrollableWidth;
        if (vertical)
            inner.ScrollToVerticalOffset(end - 2);
        else
            inner.ScrollToHorizontalOffset(end - 2);

        inner.RaiseEvent(vertical ? Wheel(0, -25) : Wheel(25, 0));
        Assert.Equal(end, vertical ? inner.VerticalOffset : inner.HorizontalOffset, precision: 3);
        Assert.Equal(0, vertical ? outer.VerticalOffset : outer.HorizontalOffset);

        inner.RaiseEvent(vertical ? Wheel(0, -25) : Wheel(25, 0));
        Assert.Equal(10, vertical ? outer.VerticalOffset : outer.HorizontalOffset, precision: 3);
    }

    [Fact]
    public void PreciseWheel_ScrollInfoReceivesOffsetsInsteadOfDiscreteWheelCommands()
    {
        var viewer = CreateViewer();
        var provider = new TestScrollInfo();
        viewer.ScrollInfo = provider;
        var wheel = Wheel(0.625, -0.3125);
        viewer.RaiseEvent(wheel);

        Assert.True(wheel.Handled);
        Assert.Equal(0.25, provider.HorizontalOffset, precision: 10);
        Assert.Equal(0.125, provider.VerticalOffset, precision: 10);
        Assert.Equal(0, provider.WheelCommandCount);

        provider.SetVerticalOffset(provider.ExtentHeight - provider.ViewportHeight);
        wheel = Wheel(0, -25);
        viewer.RaiseEvent(wheel);
        Assert.False(wheel.Handled);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, double.PositiveInfinity)]
    public void EmptyOrInvalidWheel_DoesNotMoveOrConsumeInput(double horizontal, double vertical)
    {
        var viewer = CreateViewer();
        var wheel = Wheel(horizontal, vertical);
        viewer.RaiseEvent(wheel);
        Assert.False(wheel.Handled);
        Assert.Equal(0, viewer.HorizontalOffset);
        Assert.Equal(0, viewer.VerticalOffset);
    }

    [Fact]
    public void MacOSPreferenceChanges_UpdateDefaultsAndPreserveExplicitChoices()
    {
        var viewer = new ScrollViewer();
        viewer.ApplyMacOSScrollBarPreferences(true);
        Assert.True(viewer.IsOverlayScrollBarEnabled);
        viewer.ApplyMacOSScrollBarPreferences(false);
        Assert.False(viewer.IsOverlayScrollBarEnabled);
        Assert.Equal(ScrollViewer.DetermineDefaultScrollBarAutoHide(
            Environment.GetEnvironmentVariable("JALIUM_SCROLLBAR_AUTOHIDE"), false), viewer.IsScrollBarAutoHideEnabled);

        viewer.IsOverlayScrollBarEnabled = false;
        viewer.IsScrollBarAutoHideEnabled = true;
        viewer.ApplyMacOSScrollBarPreferences(true);
        viewer.ApplyMacOSScrollBarPreferences(false);
        Assert.False(viewer.IsOverlayScrollBarEnabled);
        Assert.True(viewer.IsScrollBarAutoHideEnabled);
    }

    [Fact]
    public void MacOSPreferenceChanges_CachedControlsRefreshWhenLoadedAgain()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var field = typeof(MacOSScrollBarSettings).GetField("s_prefersOverlayScrollBars", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, false);
            var viewer = new ScrollViewer();
            var bar = new ScrollBar();
            Assert.False(viewer.IsOverlayScrollBarEnabled);
            Assert.False(bar.IsOverlayStyle);

            field.SetValue(null, true);
            viewer.SetLoadedState(true);
            bar.SetLoadedState(true);
            Assert.True(viewer.IsOverlayScrollBarEnabled);
            Assert.True(bar.IsOverlayStyle);

            viewer.SetLoadedState(false);
            bar.SetLoadedState(false);
            viewer.IsOverlayScrollBarEnabled = false;
            bar.IsOverlayStyle = false;
            viewer.SetLoadedState(true);
            bar.SetLoadedState(true);
            Assert.False(viewer.IsOverlayScrollBarEnabled);
            Assert.False(bar.IsOverlayStyle);
            viewer.SetLoadedState(false);
            bar.SetLoadedState(false);
        }
        finally { field.SetValue(null, previous); }
    }

    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public void MacOSOverlay_HoverExpandsIndicatorAndRetainsUsableHitTarget(Orientation orientation)
    {
        var bar = CreateMacOSBar(orientation, overlay: true);
        var thumb = Assert.IsType<Thumb>(bar.Track.Thumb);
        var indicator = Assert.IsType<Border>(thumb.GetVisualChild(0));
        Assert.Equal(6, orientation == Orientation.Vertical ? indicator.RenderSize.Width : indicator.RenderSize.Height);
        Assert.NotNull(bar.HitTest(orientation == Orientation.Vertical ? new Point(5, 10) : new Point(10, 5)));
        Assert.Null(bar.HitTest(orientation == Orientation.Vertical ? new Point(5, 200) : new Point(200, 5)));

        bar.SetIsMouseOver(true);
        bar.RaiseEvent(MouseEvent(UIElement.MouseEnterEvent));
        ArrangeBar(bar);
        Assert.Equal(8, orientation == Orientation.Vertical ? indicator.RenderSize.Width : indicator.RenderSize.Height);

        bar.SetIsMouseOver(false);
        bar.RaiseEvent(MouseEvent(UIElement.MouseLeaveEvent));
        ArrangeBar(bar);
        Assert.Equal(6, orientation == Orientation.Vertical ? indicator.RenderSize.Width : indicator.RenderSize.Height);
    }

    [Fact]
    public void MacOSPersistentScrollBar_HasNoArrowButtonsAndKeepsTrackPaging()
    {
        var bar = CreateMacOSBar(Orientation.Vertical, overlay: false);
        Assert.Equal(Visibility.Collapsed, ((RepeatButton)bar.GetVisualChild(0)).Visibility);
        Assert.Equal(Visibility.Collapsed, ((RepeatButton)bar.GetVisualChild(2)).Visibility);
        Assert.Equal(3, bar.Track.VisualBounds.Y);
        Assert.Equal(214, bar.Track.RenderSize.Height);
        Assert.True(bar.Track.IncreaseRepeatButton!.IsHitTestVisible);
    }

    [Fact]
    public void MacOSReducedMotion_SnapsOverlayVisibilityWithoutStartingAnimation()
    {
        var field = typeof(MacOSScrollBarSettings).GetField("s_prefersReducedMotion", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, true);
            var bar = CreateMacOSBar(Orientation.Vertical, overlay: true);
            bar.StartAutoHideVisualTransition(1);
            Assert.Equal(0, bar.Track.Opacity);
            Assert.False(bar.Track.IsHitTestVisible);
            bar.StartAutoHideVisualTransition(0);
            Assert.Equal(1, bar.Track.Opacity);
            Assert.True(bar.Track.IsHitTestVisible);
        }
        finally { field.SetValue(null, previous); }
    }

    [Fact]
    public void MacOSOverlay_KeyboardScrollingAndAutomationRemainAvailable()
    {
        var viewer = CreateViewer();
        var key = new KeyEventArgs(UIElement.KeyDownEvent, Key.PageDown, ModifierKeys.None, true, false, 1);
        viewer.RaiseEvent(key);
        Assert.True(key.Handled);
        Assert.Equal(viewer.ViewportHeight, viewer.VerticalOffset, precision: 3);
        Assert.NotNull(Jalium.UI.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(viewer));
    }

    [Fact]
    public void MacOSOverlay_KeyboardFocusKeepsAutoHiddenThumbVisibleUntilFocusLeaves()
    {
        var field = typeof(MacOSScrollBarSettings).GetField("s_prefersReducedMotion", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, true);
            var viewer = CreateViewer();
            var bar = GetField<ScrollBar>(viewer, "_verticalScrollBar");
            bar.UseMacOSScrollBarBehavior = true;
            viewer.IsScrollBarAutoHideEnabled = true;
            typeof(ScrollViewer).GetField("_scrollBarAutoHideDeadlineTick", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewer, 0L);
            typeof(ScrollViewer).GetMethod("OnScrollBarAutoHideTimerTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewer, [null, EventArgs.Empty]);
            Assert.Equal(0, bar.Track.Opacity);
            bar.UpdateIsKeyboardFocusWithin(true);
            Assert.Equal(1, bar.Track.Opacity);
            Assert.True(bar.Track.IsHitTestVisible);
            typeof(ScrollViewer).GetField("_scrollBarAutoHideDeadlineTick", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewer, 0L);
            typeof(ScrollViewer).GetMethod("OnScrollBarAutoHideTimerTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewer, [null, EventArgs.Empty]);
            Assert.Equal(1, bar.Track.Opacity);
            bar.UpdateIsKeyboardFocusWithin(false);
            Assert.Equal(0, bar.Track.Opacity);
            viewer.IsScrollBarAutoHideEnabled = false;
        }
        finally { field.SetValue(null, previous); }
    }

    private static ScrollViewer CreateViewer(FrameworkElement? content = null)
    {
        var viewer = new ScrollViewer
        {
            Width = 200, Height = 120,
            Content = content ?? new Border { Width = 600, Height = 1000 },
            IsOverlayScrollBarEnabled = true,
            IsScrollBarAutoHideEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        viewer.Measure(new Size(200, 120));
        viewer.Arrange(new Rect(0, 0, 200, 120));
        return viewer;
    }

    private static ScrollBar CreateMacOSBar(Orientation orientation, bool overlay)
    {
        var bar = new ScrollBar
        {
            UseMacOSScrollBarBehavior = true, IsOverlayStyle = overlay,
            Orientation = orientation, Maximum = 1000, ViewportSize = 200, Padding = new Thickness(0)
        };
        ArrangeBar(bar);
        return bar;
    }

    private static void ArrangeBar(ScrollBar bar)
    {
        var size = bar.Orientation == Orientation.Vertical ? new Size(16, 220) : new Size(220, 16);
        bar.Measure(size);
        bar.Arrange(new Rect(new Point(), size));
    }

    private static MouseEventArgs MouseEvent(RoutedEvent routedEvent) => new(routedEvent, new Point(10, 10),
        MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
        MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1);

    private static MouseWheelEventArgs Wheel(double horizontal, double vertical, bool precise = true,
        ModifierKeys modifiers = ModifierKeys.None) => new(UIElement.MouseWheelEvent, new Point(10, 10),
        horizontal, vertical, precise, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
        MouseButtonState.Released, MouseButtonState.Released, modifiers, 1);

    private static bool IsSmoothScrolling(ScrollViewer viewer) => GetField<bool>(viewer, "_isSmoothScrolling");
    private static T GetField<T>(ScrollViewer viewer, string name) =>
        (T)typeof(ScrollViewer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;

    private sealed class TestScrollInfo : IScrollInfo
    {
        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth => 1000;
        public double ExtentHeight => 1000;
        public double ViewportWidth => 100;
        public double ViewportHeight => 100;
        public double HorizontalOffset { get; private set; }
        public double VerticalOffset { get; private set; }
        public ScrollViewer? ScrollOwner { get; set; }
        public int WheelCommandCount { get; private set; }
        public void SetHorizontalOffset(double offset) => HorizontalOffset = Math.Clamp(offset, 0, ExtentWidth - ViewportWidth);
        public void SetVerticalOffset(double offset) => VerticalOffset = Math.Clamp(offset, 0, ExtentHeight - ViewportHeight);
        public void MouseWheelUp() => WheelCommandCount++;
        public void MouseWheelDown() => WheelCommandCount++;
        public void MouseWheelLeft() => WheelCommandCount++;
        public void MouseWheelRight() => WheelCommandCount++;
        public void LineUp() { }
        public void LineDown() { }
        public void LineLeft() { }
        public void LineRight() { }
        public void PageUp() { }
        public void PageDown() { }
        public void PageLeft() { }
        public void PageRight() { }
        public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
    }
}
