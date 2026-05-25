using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class TouchHighFrequencyPacketsTests
{
    [Fact]
    public void GetIntermediateTouchPoints_WithoutHistory_ReturnsSinglePoint()
    {
        var border = new Border { Width = 100, Height = 100 };
        var device = Touch.RegisterTouchPoint(1, new Point(50, 60), border);
        device.UpdatePosition(new Point(50, 60));
        try
        {
            var points = device.GetIntermediateTouchPoints(border);
            Assert.Single(points);
            Assert.Equal(50, points[0].Position.X, precision: 5);
            Assert.Equal(60, points[0].Position.Y, precision: 5);
        }
        finally
        {
            Touch.UnregisterTouchPoint(1);
        }
    }

    [Fact]
    public void GetIntermediateTouchPoints_WithHistory_ExposesEveryPacketInOrder()
    {
        var border = new Border { Width = 200, Height = 200 };
        var device = Touch.RegisterTouchPoint(2, new Point(0, 0), border);

        var history = new StylusPointCollection(new[]
        {
            new StylusPoint(10, 10, 0.4f),
            new StylusPoint(15, 12, 0.5f),
            new StylusPoint(20, 14, 0.6f),
            new StylusPoint(25, 16, 0.7f)
        });
        device.UpdatePosition(new Point(25, 16));
        device.RecordFrame(history, new Rect(20, 11, 10, 10), TouchAction.Move, timestamp: 100);

        try
        {
            var points = device.GetIntermediateTouchPoints(border);
            Assert.Equal(4, points.Count);
            Assert.Equal(10, points[0].Position.X);
            Assert.Equal(25, points[3].Position.X);

            // All but the last are reported as Move; the final one carries the active action.
            Assert.Equal(TouchAction.Move, points[0].Action);
            Assert.Equal(TouchAction.Move, points[1].Action);
            Assert.Equal(TouchAction.Move, points[2].Action);
            Assert.Equal(TouchAction.Move, points[3].Action);
        }
        finally
        {
            Touch.UnregisterTouchPoint(2);
        }
    }

    [Fact]
    public void GetIntermediateTouchPoints_DownActionPropagatesToFinalPacket()
    {
        var border = new Border { Width = 200, Height = 200 };
        var device = Touch.RegisterTouchPoint(3, new Point(5, 5), border);
        var history = new StylusPointCollection(new[]
        {
            new StylusPoint(5, 5, 0.5f),
            new StylusPoint(6, 6, 0.6f),
        });
        device.RecordFrame(history, Rect.Empty, TouchAction.Down, timestamp: 200);

        try
        {
            var points = device.GetIntermediateTouchPoints(border);
            Assert.Equal(2, points.Count);
            Assert.Equal(TouchAction.Move, points[0].Action);
            Assert.Equal(TouchAction.Down, points[1].Action);
        }
        finally
        {
            Touch.UnregisterTouchPoint(3);
        }
    }

    [Fact]
    public void TouchPoint_Bounds_DerivedFromContactRect()
    {
        var border = new Border();
        var device = Touch.RegisterTouchPoint(4, new Point(10, 10), border);
        device.RecordFrame(null, new Rect(0, 0, 20, 15), TouchAction.Move, timestamp: 300);

        try
        {
            var pt = device.GetTouchPoint(border);
            Assert.Equal(20, pt.Bounds.Width);
            Assert.Equal(15, pt.Bounds.Height);
            Assert.False(pt.Bounds.IsEmpty);
            Assert.Equal(20, pt.Size.Width);
            Assert.Equal(15, pt.Size.Height);
        }
        finally
        {
            Touch.UnregisterTouchPoint(4);
        }
    }
}
