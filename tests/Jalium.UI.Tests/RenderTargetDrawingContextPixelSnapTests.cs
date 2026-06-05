using System.Reflection;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public class RenderTargetDrawingContextPixelSnapTests
{
    [Theory]
    // Values that already sit ON a device-pixel boundary stay locked there so
    // that statically-positioned UI keeps sharp edges (no AA fringe).
    [InlineData(0.0, 0.0f)]
    [InlineData(12.0, 12.0f)]
    [InlineData(0.5, 0.5f)]      // half-pixel for odd-width strokes
    [InlineData(43.5, 43.5f)]
    // Genuinely fractional values must pass through unchanged. The renderer
    // does sub-pixel AA — snapping fractional values to the nearest integer
    // collapses smooth animations (e.g. a spring sweeping continuously through
    // 10.49 → 10.50 → 10.51) into {10, 10.5, 11}, which surfaces as 1px jitter.
    [InlineData(10.49, 10.49f)]
    [InlineData(10.51, 10.51f)]
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
