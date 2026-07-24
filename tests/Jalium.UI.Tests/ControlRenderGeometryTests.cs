using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class ControlRenderGeometryTests
{
    [Fact]
    public void GetStrokeAlignedRect_InsetsOddStrokeByHalfPixel()
    {
        var rect = InvokeRectMethod("GetStrokeAlignedRect", new Rect(0, 0, 44, 20), 1.0);

        Assert.Equal(new Rect(0.5, 0.5, 43, 19), rect);
    }

    [Fact]
    public void GetStrokeAlignedCornerRadius_InsetsByHalfStrokeWidth()
    {
        var radius = InvokeCornerRadiusMethod("GetStrokeAlignedCornerRadius", new CornerRadius(10), 1.0);

        Assert.Equal(new CornerRadius(9.5), radius);
    }

    [Theory]
    [InlineData(12, 5, 5)]
    [InlineData(12, double.PositiveInfinity, 12)]
    [InlineData(12, 0, 0)]
    [InlineData(-1, 5, 0)]
    public void GetAvailableLength_AlwaysReturnsFiniteContainedLength(double requested, double available, double expected)
    {
        var actual = InvokeMethod<double>("GetAvailableLength", requested, available);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetContentRect_WhenChromeExceedsBounds_CollapsesInsteadOfGoingNegative()
    {
        var bounds = new Rect(0, 0, 20, 104.51953125);
        var rect = InvokeMethod<Rect>("GetContentRect", bounds, new Thickness(0, 0, 32, 14));

        Assert.Equal(new Rect(0, 0, 0, 90.51953125), rect);
    }

    [Fact]
    public void TrackAndTrailingRects_StayInsideTinyBounds()
    {
        var bounds = new Rect(0, 0, 8, 8);
        var trailing = InvokeMethod<Rect>("GetTrailingRect", bounds, 32.0);
        var track = InvokeMethod<Rect>("GetCenteredTrackRect", bounds, Orientation.Horizontal, 16.0, 12.0);

        Assert.Equal(bounds, trailing);
        Assert.Equal(new Rect(4, 0, 0, 8), track);
    }

    private static Rect InvokeRectMethod(string methodName, Rect bounds, double thickness)
    {
        var method = GetHelperType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<Rect>(method!.Invoke(null, new object[] { bounds, thickness }));
    }

    private static CornerRadius InvokeCornerRadiusMethod(string methodName, CornerRadius radius, double thickness)
    {
        var method = GetHelperType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<CornerRadius>(method!.Invoke(null, new object[] { radius, thickness }));
    }

    private static T InvokeMethod<T>(string methodName, params object[] arguments)
    {
        var method = GetHelperType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, arguments));
    }

    private static Type GetHelperType()
    {
        var helperType = typeof(TextBox).Assembly.GetType("Jalium.UI.Controls.ControlRenderGeometry");
        Assert.NotNull(helperType);
        return helperType!;
    }
}
