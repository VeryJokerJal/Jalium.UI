using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class ScrollViewerAutoHideTests
{
    [Fact]
    public void ScrollViewer_AutoHide_Default_ShouldBeEnabled()
    {
        var viewer = new ScrollViewer();
        Assert.True(viewer.IsScrollBarAutoHideEnabled);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("gallery", true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void ScrollViewer_AutoHide_DefaultPolicy_ShouldOnlyHonorExplicitEnvironmentOverride(
        string? environmentValue,
        bool expected)
    {
        Assert.Equal(expected, ScrollViewer.DetermineDefaultScrollBarAutoHide(environmentValue));
    }

    [Fact]
    public void ScrollViewer_DefaultVerticalScrollBarVisibility_ShouldBeAuto()
    {
        var viewer = new ScrollViewer();
        Assert.Equal(ScrollBarVisibility.Auto, viewer.VerticalScrollBarVisibility);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_WithAutoHideEnabled_ShouldStartSlim()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.True(verticalBar.IsThumbSlim);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_MouseWheel_ShouldKeepSlim()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.True(verticalBar.IsThumbSlim);

        var wheel = CreateMouseWheel(new Point(8, 8), -120, ModifierKeys.None, timestamp: 1);
        viewer.RaiseEvent(wheel);

        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.True(verticalBar.IsThumbSlim);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_ContentMouseEnter_ShouldShowCollapsedState()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        Assert.True(verticalBar.IsThumbSlim);
        ForceAutoHideVisibilityProgress(verticalBar, 0.0);
        Assert.Equal(0.0, verticalBar.Opacity, precision: 3);

        RaiseViewerContentEnter(viewer);

        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.True(verticalBar.IsThumbSlim);
        Assert.False(GetPrivateField<bool>(viewer, "_areAutoHideScrollBarsRevealed"));
        Assert.Equal(1.0, GetPrivateField<double>(
            verticalBar,
            "_autoHideVisibilityAnimTo"), precision: 3);

        ForceAutoHideProgress(verticalBar, 1.0);
        ForceAutoHideVisibilityProgress(verticalBar, 1.0);
        Assert.Equal(1.0, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, verticalBar.Track.Opacity, precision: 3);
        Assert.Equal(2.0, verticalBar.Track.ThumbCrossAxisThickness, precision: 3);

        StopAutoHideTimers(viewer, verticalBar);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_ScrollBarMouseEnter_ShouldRevealScrollBar()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        Assert.True(verticalBar.IsThumbSlim);
        ForceAutoHideVisibilityProgress(verticalBar, 0.0);

        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: true);

        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.False(verticalBar.IsThumbSlim);
        Assert.True(GetPrivateField<bool>(viewer, "_areAutoHideScrollBarsRevealed"));
        Assert.Equal(1.0, GetPrivateField<double>(
            verticalBar,
            "_autoHideVisibilityAnimTo"), precision: 3);

        StopAutoHideTimers(viewer, verticalBar);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_ViewerMouseLeave_ShouldBeginFadeImmediately()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        var track = verticalBar.Track;

        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: true);
        ForceAutoHideProgress(verticalBar, 0.0);
        ForceAutoHideVisibilityProgress(verticalBar, 1.0);
        Assert.False(verticalBar.IsThumbSlim);

        RaiseViewerMouseLeave(viewer, verticalBar);

        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.True(verticalBar.IsThumbSlim);
        Assert.Equal(1.0, track.Opacity, precision: 3);
        Assert.Equal(1.0, GetPrivateField<double>(verticalBar, "_autoHideVisualAnimTo"), precision: 3);
        Assert.Equal(0.0, GetPrivateField<double>(
            verticalBar,
            "_autoHideVisibilityAnimTo"), precision: 3);
        Assert.Equal(1.0, verticalBar.Opacity, precision: 3);
        Assert.True(GetPrivateField<Jalium.UI.Threading.DispatcherTimer>(
            verticalBar,
            "_autoHideVisualTimer").IsEnabled);

        ForceAutoHideVisibilityProgress(verticalBar, 0.5);
        Assert.Equal(0.5, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, track.Opacity, precision: 3);

        StopAutoHideTimers(viewer, verticalBar);
    }

    [Fact]
    public void ScrollViewer_OverlayAutoVisibility_ViewerMouseLeave_ShouldBeginFadeImmediately()
    {
        var viewer = CreateConfiguredViewer(
            autoHideEnabled: true,
            verticalVisibility: ScrollBarVisibility.Auto,
            overlayEnabled: true);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: true);
        ForceAutoHideProgress(verticalBar, 0.0);
        RaiseViewerMouseLeave(viewer, verticalBar);

        Assert.True(verticalBar.IsThumbSlim);
        Assert.Equal(1.0, GetPrivateField<double>(verticalBar, "_autoHideVisualAnimTo"), precision: 3);
        Assert.True(GetPrivateField<Jalium.UI.Threading.DispatcherTimer>(
            verticalBar,
            "_autoHideVisualTimer").IsEnabled);

        StopAutoHideTimers(viewer, verticalBar);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_MovingFromScrollBarToContent_ShouldResumeIdleCountdown()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: true);
        ForceAutoHideProgress(verticalBar, 0.0);
        ForceAutoHideVisibilityProgress(verticalBar, 1.0);

        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: false);

        Assert.True(viewer.IsMouseOver);
        Assert.False(verticalBar.IsMouseOver);
        Assert.False(verticalBar.IsThumbSlim);
        Assert.True(GetPrivateField<bool>(viewer, "_areAutoHideScrollBarsRevealed"));
        Assert.Equal(0.0, GetPrivateField<double>(verticalBar, "_autoHideCollapseProgress"), precision: 3);
        Assert.Equal(1.0, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, verticalBar.Track.Opacity, precision: 3);
        Assert.True(GetPrivateField<Jalium.UI.Threading.DispatcherTimer>(
            viewer,
            "_scrollBarAutoHideTimer").IsEnabled);

        StopAutoHideTimers(viewer, verticalBar);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_ContentHover_ShouldNotPreventIdleCollapse()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: true);
        ForceAutoHideProgress(verticalBar, 0.0);
        ForceAutoHideVisibilityProgress(verticalBar, 1.0);
        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: false);
        SetPrivateField(viewer, "_scrollBarAutoHideDeadlineTick", long.MinValue);

        InvokePrivate(viewer, "OnScrollBarAutoHideTimerTick", null, EventArgs.Empty);

        Assert.True(viewer.IsMouseOver);
        Assert.False(verticalBar.IsMouseOver);
        Assert.True(verticalBar.IsThumbSlim);
        Assert.False(GetPrivateField<bool>(viewer, "_areAutoHideScrollBarsRevealed"));
        Assert.Equal(1.0, GetPrivateField<double>(verticalBar, "_autoHideVisualAnimTo"), precision: 3);
        Assert.Equal(1.0, GetPrivateField<double>(
            verticalBar,
            "_autoHideVisibilityAnimTo"), precision: 3);

        ForceAutoHideProgress(verticalBar, 1.0);
        ForceAutoHideVisibilityProgress(verticalBar, 1.0);
        Assert.Equal(1.0, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, verticalBar.Track.Opacity, precision: 3);
        Assert.Equal(2.0, verticalBar.Track.ThumbCrossAxisThickness, precision: 3);

        StopAutoHideTimers(viewer, verticalBar);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_Fade_ShouldBeBidirectionalAndInterruptible()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        var track = verticalBar.Track;

        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: true);
        ForceAutoHideProgress(verticalBar, 0.0);
        ForceAutoHideVisibilityProgress(verticalBar, 1.0);
        RaiseViewerMouseLeave(viewer, verticalBar);

        ForceAutoHideProgress(verticalBar, 0.5);
        ForceAutoHideVisibilityProgress(verticalBar, 0.5);
        Assert.Equal(0.5, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, track.Opacity, precision: 3);

        // Content entry reverses only the visibility fade while continuing
        // toward the collapsed/slim shape.
        RaiseViewerContentEnter(viewer);
        Assert.True(verticalBar.IsThumbSlim);
        Assert.Equal(0.5, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, track.Opacity, precision: 3);
        Assert.Equal(0.5, GetPrivateField<double>(
            verticalBar,
            "_autoHideVisibilityAnimFrom"), precision: 3);
        Assert.Equal(1.0, GetPrivateField<double>(
            verticalBar,
            "_autoHideVisibilityAnimTo"), precision: 3);
        Assert.Equal(1.0, GetPrivateField<double>(verticalBar, "_autoHideVisualAnimTo"), precision: 3);

        ForceAutoHideProgress(verticalBar, 1.0);
        ForceAutoHideVisibilityProgress(verticalBar, 1.0);
        Assert.True(verticalBar.IsThumbSlim);
        Assert.Equal(1.0, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, track.Opacity, precision: 3);

        // Entering the actual scroll bar expands the already-visible slim shape.
        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: true);
        Assert.False(verticalBar.IsThumbSlim);
        Assert.Equal(1.0, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, GetPrivateField<double>(verticalBar, "_autoHideVisualAnimFrom"), precision: 3);
        Assert.Equal(0.0, GetPrivateField<double>(verticalBar, "_autoHideVisualAnimTo"), precision: 3);

        // Outside the complete viewer, shape collapses while the whole bar fades.
        RaiseViewerMouseLeave(viewer, verticalBar);
        ForceAutoHideProgress(verticalBar, 1.0);
        ForceAutoHideVisibilityProgress(verticalBar, 0.0);
        Assert.Equal(0.0, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, track.Opacity, precision: 3);
        var contentHit = viewer.HitTest(new Point(40, 60));
        Assert.NotNull(contentHit);
        Assert.True(HasVisualAncestor<ScrollViewer>(contentHit!.VisualHit));
        var hiddenHit = viewer.HitTest(new Point(194, 60));
        Assert.NotNull(hiddenHit);
        Assert.True(HasVisualAncestor<ScrollBar>(hiddenHit!.VisualHit));

        RaiseViewerContentEnter(viewer);
        Assert.True(verticalBar.IsThumbSlim);
        Assert.Equal(0.0, verticalBar.Opacity, precision: 3);
        Assert.Equal(0.0, GetPrivateField<double>(
            verticalBar,
            "_autoHideVisibilityAnimFrom"), precision: 3);
        Assert.Equal(1.0, GetPrivateField<double>(
            verticalBar,
            "_autoHideVisibilityAnimTo"), precision: 3);

        ForceAutoHideVisibilityProgress(verticalBar, 1.0);
        ForceAutoHideProgress(verticalBar, 1.0);
        Assert.True(verticalBar.IsThumbSlim);
        Assert.Equal(1.0, verticalBar.Opacity, precision: 3);
        Assert.Equal(1.0, track.Opacity, precision: 3);

        RaiseScrollBarMouseTransition(viewer, verticalBar, isEnter: true);
        Assert.Equal(1.0, GetPrivateField<double>(verticalBar, "_autoHideVisualAnimFrom"), precision: 3);
        Assert.Equal(0.0, GetPrivateField<double>(verticalBar, "_autoHideVisualAnimTo"), precision: 3);
        Assert.Equal(1.0, track.Opacity, precision: 3);

        StopAutoHideTimers(viewer, verticalBar);
    }

    [Fact]
    public void ScrollBar_DesktopAutoHideCollapse_ShouldPreserveCustomTrackOpacity()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Maximum = 100,
            ViewportSize = 20
        };
        var track = scrollBar.Track;
        track.Opacity = 0.65;

        scrollBar.Measure(new Size(12, 120));
        scrollBar.Arrange(new Rect(0, 0, 12, 120));

        ForceAutoHideProgress(scrollBar, 0.5);
        Assert.Equal(0.65, track.Opacity, precision: 3);

        ForceAutoHideProgress(scrollBar, 1.0);
        Assert.Equal(0.65, track.Opacity, precision: 3);
        ForceAutoHideProgress(scrollBar, 0.0);
        Assert.Equal(0.65, track.Opacity, precision: 3);
    }

    [Fact]
    public void ScrollBar_AutoHideVisibilityFade_ShouldRestoreCustomOpacity()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Maximum = 100,
            ViewportSize = 20,
            Opacity = 0.65
        };

        scrollBar.Measure(new Size(12, 120));
        scrollBar.Arrange(new Rect(0, 0, 12, 120));

        ForceAutoHideVisibilityProgress(scrollBar, 0.5);
        Assert.Equal(0.325, scrollBar.Opacity, precision: 3);

        ForceAutoHideVisibilityProgress(scrollBar, 1.0);
        Assert.Equal(0.65, scrollBar.Opacity, precision: 3);
    }

    [Fact]
    public void ScrollBar_SettledAutoHideTarget_ShouldNotInvalidateAgain()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Maximum = 100,
            ViewportSize = 20
        };

        scrollBar.Measure(new Size(12, 120));
        scrollBar.Arrange(new Rect(0, 0, 12, 120));
        ForceAutoHideProgress(scrollBar, 1.0);
        ForceAutoHideVisibilityProgress(scrollBar, 1.0);
        scrollBar.ClearRenderDirty();

        scrollBar.StartAutoHideVisualTransition(1.0);
        scrollBar.StartAutoHideVisibilityTransition(1.0);

        Assert.False(scrollBar.IsRenderDirty);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_WithAutoHideDisabled_ShouldRemainVisible()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: false, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.False(verticalBar.IsThumbSlim);
    }

    [Fact]
    public void ScrollViewer_VisibleVisibility_ShouldNotAutoHide()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Visible);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.False(verticalBar.IsThumbSlim);
    }

    private static ScrollViewer CreateConfiguredViewer(
        bool autoHideEnabled,
        ScrollBarVisibility verticalVisibility,
        bool overlayEnabled = false)
    {
        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = verticalVisibility,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsScrollBarAutoHideEnabled = autoHideEnabled,
            IsOverlayScrollBarEnabled = overlayEnabled
        };

        // Measure BEFORE faking the scroll metrics: Arrange on a measure-dirty element
        // re-runs Measure first (WPF measure-dirty guard), and the ScrollViewer measure
        // pass recomputes the extent from its (empty) content, which would wipe the
        // reflection-poked values below.
        viewer.Measure(new Size(200, 120));

        SetPrivateField(viewer, "_extentHeight", 1000.0);
        SetPrivateField(viewer, "_extentWidth", 100.0);
        SetPrivateField(viewer, "_verticalOffset", 0.0);
        SetPrivateField(viewer, "_horizontalOffset", 0.0);

        viewer.Arrange(new Rect(0, 0, 200, 120));
        return viewer;
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

    private static void ForceAutoHideProgress(ScrollBar scrollBar, double progress)
    {
        InvokePrivate(scrollBar, "ApplyAutoHideVisualState", progress, null, false);
    }

    private static void ForceAutoHideVisibilityProgress(ScrollBar scrollBar, double progress)
    {
        InvokePrivate(scrollBar, "ApplyAutoHideVisibilityState", progress);
    }

    private static void RaiseViewerContentEnter(ScrollViewer viewer)
    {
        viewer.SetIsMouseOver(true);
        viewer.RaiseEvent(new MouseEventArgs(UIElement.MouseEnterEvent)
        {
            Source = viewer
        });
    }

    private static void RaiseScrollBarMouseTransition(
        ScrollViewer viewer,
        ScrollBar scrollBar,
        bool isEnter)
    {
        viewer.SetIsMouseOver(true);
        scrollBar.SetIsMouseOver(isEnter);
        scrollBar.RaiseEvent(new MouseEventArgs(
            isEnter ? UIElement.MouseEnterEvent : UIElement.MouseLeaveEvent)
        {
            Source = scrollBar
        });
    }

    private static void RaiseViewerMouseLeave(ScrollViewer viewer, ScrollBar scrollBar)
    {
        scrollBar.SetIsMouseOver(false);
        viewer.SetIsMouseOver(false);
        viewer.RaiseEvent(new MouseEventArgs(UIElement.MouseLeaveEvent)
        {
            Source = viewer
        });
    }

    private static void StopAutoHideTimers(ScrollViewer viewer, ScrollBar scrollBar)
    {
        StopPrivateTimer(viewer, "_scrollBarAutoHideTimer");
        StopPrivateTimer(scrollBar, "_autoHideVisualTimer");
    }

    private static void StopPrivateTimer(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        (field!.GetValue(target) as Jalium.UI.Threading.DispatcherTimer)?.Stop();
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

    private static object? InvokePrivate(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(target, arguments);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var value = field!.GetValue(target);
        Assert.NotNull(value);
        return (T)value!;
    }
}
