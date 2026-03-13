using System.Reflection;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public class RenderTargetDrawingContextPixelSnapTests
{
    [Theory]
    [InlineData(0.0, 0.0f)]
    [InlineData(12.0, 12.0f)]
    [InlineData(0.5, 0.5f)]
    [InlineData(43.5, 43.5f)]
    [InlineData(10.49, 10.0f)]
    [InlineData(10.51, 11.0f)]
    public void SnapCoordinate_PreservesWholeAndHalfPixelAlignment(double input, float expected)
    {
        Assert.Equal(expected, InvokeSnapCoordinate(input));
    }

    private static float InvokeSnapCoordinate(double value)
    {
        var method = typeof(RenderTargetDrawingContext).GetMethod(
            "SnapCoordinate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<float>(method!.Invoke(null, new object[] { value }));
    }
}
