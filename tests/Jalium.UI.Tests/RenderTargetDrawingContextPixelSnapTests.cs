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

    [Theory]
    [InlineData(1.28, 0.0, 0.0, 0.82, 1.28, 0.82, true)]
    [InlineData(0.97, 0.0, 0.0, 0.97, 0.97, 0.97, false)]
    [InlineData(1.001, 0.0, 0.0, 1.0, 1.001, 1.0, false)]
    [InlineData(1.28, 0.02, 0.0, 0.82, 1.2801562405, 0.82, false)]
    public void TextScaleDeformation_PreservesAxisAlignedAnisotropicTransforms(
        double m11,
        double m12,
        double m21,
        double m22,
        double scaleX,
        double scaleY,
        bool expected)
    {
        Assert.Equal(expected, InvokeTextScaleDeformationDecision(m11, m12, m21, m22, scaleX, scaleY));
    }

    private static float InvokeSnapCoordinate(double value)
    {
        var method = typeof(RenderTargetDrawingContext).GetMethod(
            "SnapCoordinate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<float>(method!.Invoke(null, new object[] { value }));
    }

    private static bool InvokeTextScaleDeformationDecision(
        double m11,
        double m12,
        double m21,
        double m22,
        double scaleX,
        double scaleY)
    {
        var method = typeof(RenderTargetDrawingContext).GetMethod(
            "ShouldPreserveNativeTextScaleDeformation",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, new object[] { m11, m12, m21, m22, scaleX, scaleY }));
    }
}
