using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MouseWheelSignConsumerTests
{
    public static IEnumerable<object[]> Packets()
    {
        foreach (var phase in new[] { MouseWheelPhase.None, MouseWheelPhase.Began,
            MouseWheelPhase.Changed, MouseWheelPhase.Ended, MouseWheelPhase.Cancelled })
        {
            yield return [0d, true, phase, MouseWheelPhase.None];
            if (phase != MouseWheelPhase.None)
                yield return [0d, true, MouseWheelPhase.None, phase];
        }
        yield return [0d, false, MouseWheelPhase.None, MouseWheelPhase.None];
        yield return [double.NaN, true, MouseWheelPhase.None, MouseWheelPhase.None];
        yield return [double.PositiveInfinity, true, MouseWheelPhase.None, MouseWheelPhase.None];
        yield return [double.NegativeInfinity, true, MouseWheelPhase.None, MouseWheelPhase.None];
        foreach (double delta in new[] { -0.125, 0.125, -120d, 120d })
        {
            yield return [delta, true, MouseWheelPhase.Changed, MouseWheelPhase.None];
            // An end packet carrying real movement must not be dropped either.
            yield return [delta, true, MouseWheelPhase.Ended, MouseWheelPhase.None];
            yield return [delta, true, MouseWheelPhase.None, MouseWheelPhase.Changed];
            yield return [delta, true, MouseWheelPhase.None, MouseWheelPhase.Ended];
        }
        yield return [-120d, false, MouseWheelPhase.None, MouseWheelPhase.None];
        yield return [120d, false, MouseWheelPhase.None, MouseWheelPhase.None];
    }

    [Theory]
    [MemberData(nameof(Packets))]
    public void SignConsumers_IgnoreEmptyPacketsAndRetainOriginalDirection(
        double delta, bool precise, MouseWheelPhase phase, MouseWheelPhase momentum)
    {
        bool movement = double.IsFinite(delta) && delta != 0;
        var chart = new LineChart { IsZoomEnabled = true, IsLegendVisible = false };
        Arrange(chart);
        var chartWheel = Wheel(delta, precise, phase, momentum, new Point(150, 100));
        chart.RaiseEvent(chartWheel);
        Assert.Equal(movement, chartWheel.Handled);
        double min = Viewport(chart, "_viewportMinX");
        Assert.Equal(!movement, double.IsNaN(min));
        if (movement)
        {
            double range = Viewport(chart, "_viewportMaxX") - min;
            chart.ResetZoom();
            var opposite = Wheel(-delta, precise, phase, momentum, new Point(150, 100));
            chart.RaiseEvent(opposite);
            double oppositeRange = Viewport(chart, "_viewportMaxX") - Viewport(chart, "_viewportMinX");
            Assert.True(delta > 0 ? range < oppositeRange : range > oppositeRange);
        }

        var map = new MapView { IsZoomEnabled = true, ZoomLevel = 5 };
        Arrange(map);
        var center = map.Center;
        var mapWheel = Wheel(delta, precise, phase, momentum, new Point(150, 100));
        map.RaiseEvent(mapWheel);
        Assert.Equal(movement, mapWheel.Handled);
        Assert.Equal(movement ? 5 + Math.Sign(delta) : 5, map.ZoomLevel);
        if (!movement) Assert.Equal(center, map.Center);

        var dock = new DockTabPanel();
        for (int i = 0; i < 12; i++)
            dock.Items.Add(new DockItem { Header = $"Tab {i} Long Header", Content = new Border() });
        Arrange(dock);
        Assert.True(dock.IsTabStripScrollableForTesting);
        dock.SetTabStripScrollOffsetForTesting(120);
        double offset = dock.TabStripScrollOffsetForTesting;
        var strip = dock.GetTabStripInteractionRect();
        var dockWheel = Wheel(delta, precise, phase, momentum, new Point(strip.Left + 5, strip.Top + 5));
        dockWheel.RoutedEvent = UIElement.PreviewMouseWheelEvent;
        dock.RaiseEvent(dockWheel);
        Assert.Equal(movement, dockWheel.Handled);
        Assert.Equal(movement ? -Math.Sign(delta) : 0,
            Math.Sign(dock.TabStripScrollOffsetForTesting - offset));
    }

    [Fact]
    public void ScrollViewer_PreciseMovementAndMomentumSurviveZeroDeltaEndPackets()
    {
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = 900, Height = 900 },
            IsScrollInertiaEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Arrange(viewer);
        viewer.ScrollToHorizontalOffset(100);
        viewer.ScrollToVerticalOffset(100);
        var position = new Point(150, 100);
        viewer.RaiseEvent(Wheel(-0.125, true, MouseWheelPhase.Began, MouseWheelPhase.None, position, 0.125));
        Assert.Equal(100.05, viewer.HorizontalOffset, 6);
        Assert.Equal(100.05, viewer.VerticalOffset, 6);
        viewer.RaiseEvent(Wheel(0, true, MouseWheelPhase.Ended, MouseWheelPhase.None, position));
        Assert.Equal(100.05, viewer.HorizontalOffset, 6);
        Assert.Equal(100.05, viewer.VerticalOffset, 6);
        viewer.RaiseEvent(Wheel(0.125, true, MouseWheelPhase.None, MouseWheelPhase.Changed, position, -0.125));
        Assert.Equal(100, viewer.HorizontalOffset, 6);
        Assert.Equal(100, viewer.VerticalOffset, 6);
        viewer.RaiseEvent(Wheel(0, true, MouseWheelPhase.None, MouseWheelPhase.Ended, position));
        Assert.Equal(100, viewer.HorizontalOffset, 6);
        Assert.Equal(100, viewer.VerticalOffset, 6);
    }

    [Fact]
    public void HorizontalOnlyPackets_DoNotOperateVerticalSignConsumers()
    {
        UIElement[] controls = [new LineChart { IsZoomEnabled = true }, new MapView { IsZoomEnabled = true }, new DockTabPanel()];
        foreach (var control in controls)
        {
            Arrange(control);
            var wheel = Wheel(0, true, MouseWheelPhase.Changed, MouseWheelPhase.None, new Point(150, 100), horizontal: 0.125);
            control.RaiseEvent(wheel);
            Assert.False(wheel.Handled);
            Assert.Equal(0.125, wheel.HorizontalDelta);
        }
    }

    private static double Viewport(AxisChartBase chart, string name) =>
        (double)typeof(AxisChartBase).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!;

    private static void Arrange(UIElement element)
    {
        element.Measure(new Size(300, 220));
        element.Arrange(new Rect(0, 0, 300, 220));
    }

    private static MouseWheelEventArgs Wheel(double delta, bool precise, MouseWheelPhase phase,
        MouseWheelPhase momentum, Point position, double horizontal = 0) => precise
        ? new(UIElement.MouseWheelEvent, position, horizontal, delta, true,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1, phase, momentum)
        : new(UIElement.MouseWheelEvent, position, (int)delta,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1);
}
