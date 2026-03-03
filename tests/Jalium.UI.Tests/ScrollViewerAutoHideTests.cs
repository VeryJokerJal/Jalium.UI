using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class ScrollViewerAutoHideTests
{
    [Fact]
    public void ScrollViewer_AutoHide_Default_ShouldBeEnabled()
    {
        var viewer = new ScrollViewer();
        Assert.True(viewer.IsScrollBarAutoHideEnabled);
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
    public void ScrollViewer_AutoVisibility_ScrollBarMouseEnter_ShouldRevealScrollBar()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        Assert.True(verticalBar.IsThumbSlim);

        verticalBar.RaiseEvent(new RoutedEventArgs(UIElement.MouseEnterEvent, verticalBar));

        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.False(verticalBar.IsThumbSlim);
    }

    [Fact]
    public void ScrollViewer_AutoVisibility_ScrollBarMouseLeave_ShouldDelaySlimming()
    {
        var viewer = CreateConfiguredViewer(autoHideEnabled: true, verticalVisibility: ScrollBarVisibility.Auto);
        var verticalBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        verticalBar.RaiseEvent(new RoutedEventArgs(UIElement.MouseEnterEvent, verticalBar));
        Assert.False(verticalBar.IsThumbSlim);

        verticalBar.RaiseEvent(new RoutedEventArgs(UIElement.MouseLeaveEvent, verticalBar));

        Assert.Equal(Visibility.Visible, verticalBar.Visibility);
        Assert.False(verticalBar.IsThumbSlim);
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

    private static ScrollViewer CreateConfiguredViewer(bool autoHideEnabled, ScrollBarVisibility verticalVisibility)
    {
        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = verticalVisibility,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsScrollBarAutoHideEnabled = autoHideEnabled
        };

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
}
