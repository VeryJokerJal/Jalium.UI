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

    private static Type GetHelperType()
    {
        var helperType = typeof(TextBox).Assembly.GetType("Jalium.UI.Controls.ControlRenderGeometry");
        Assert.NotNull(helperType);
        return helperType!;
    }
}
