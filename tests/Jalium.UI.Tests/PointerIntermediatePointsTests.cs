using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class PointerIntermediatePointsTests
{
    [Fact]
    public void GetIntermediatePoints_WithoutHistory_ReturnsSingleCurrentPoint()
    {
        var element = new Border();
        var pp = new PointerPoint(
            pointerId: 1,
            position: new Point(40, 50),
            deviceType: PointerDeviceType.Touch,
            isInContact: true,
            properties: new PointerPointProperties { Pressure = 0.5f },
            timestamp: 0)
        { SourceElement = element };

        var args = new PointerMoveEventArgs(pp, ModifierKeys.None, 0);
        var points = args.GetIntermediatePoints(null);
        Assert.Single(points);
        Assert.Equal(40, points[0].Position.X);
        Assert.Equal(50, points[0].Position.Y);
    }

    [Fact]
    public void GetIntermediatePoints_WithStylusPoints_ExposesEachPacket()
    {
        var element = new Border();
        var properties = new PointerPointProperties { Pressure = 0.5f };
        var pp = new PointerPoint(
            pointerId: 2,
            position: new Point(30, 30),
            deviceType: PointerDeviceType.Touch,
            isInContact: true,
            properties: properties,
            timestamp: 0)
        { SourceElement = element };

        var packets = new StylusPointCollection(new[]
        {
            new StylusPoint(10, 10, 0.4f),
            new StylusPoint(20, 20, 0.5f),
            new StylusPoint(30, 30, 0.6f)
        });

        var args = new PointerMoveEventArgs(pp, ModifierKeys.None, 0) { StylusPoints = packets };
        var points = args.GetIntermediatePoints(element);

        Assert.Equal(3, points.Count);
        Assert.Equal(10, points[0].Position.X);
        Assert.Equal(20, points[1].Position.X);
        Assert.Equal(30, points[2].Position.X);

        // Pressure on each packet must propagate (a non-matching pressure clones the props).
        Assert.Equal(0.4f, points[0].Properties.Pressure, precision: 4);
        Assert.Equal(0.5f, points[1].Properties.Pressure, precision: 4);
        Assert.Equal(0.6f, points[2].Properties.Pressure, precision: 4);
    }

    [Fact]
    public void GetIntermediatePoints_HonorsRelativeToTransform()
    {
        // Source element at origin; "relativeTo" is the same element so positions pass through.
        var element = new Border();
        var packets = new StylusPointCollection(new[] { new StylusPoint(7, 8, 0.5f) });
        var pp = new PointerPoint(3, new Point(7, 8), PointerDeviceType.Touch, true,
            new PointerPointProperties { Pressure = 0.5f }, 0)
        { SourceElement = element };
        var args = new PointerMoveEventArgs(pp, ModifierKeys.None, 0) { StylusPoints = packets };

        var direct = args.GetIntermediatePoints(element);
        Assert.Equal(7, direct[0].Position.X);
        Assert.Equal(8, direct[0].Position.Y);
    }
}
