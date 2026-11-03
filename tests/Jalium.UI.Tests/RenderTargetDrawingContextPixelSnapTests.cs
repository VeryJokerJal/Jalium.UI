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
    // m12 = 0.02 exceeds the scale-relative rotation epsilon (1e-3 * 1.28):
    // native classifies this as rotated/skewed and rasterizes through the full
    // 2x2, so managed must also keep the live matrix. The two thresholds are
    // deliberately identical — see ShouldPreserveNativeTextScaleDeformation.
    [InlineData(1.28, 0.02, 0.0, 0.82, 1.2801562405, 0.82, true)]
    // Gallery CSS repro: transform: rotate(-4deg) scale(1.05). The card and
    // every child text run must take the same live native matrix.
    [InlineData(1.047442, -0.073244, 0.073244, 1.047442, 1.05, 1.05, true)]
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

    [Fact]
    public void TextFormatCacheUsesAnAllocationFreeValueKey()
    {
        var cacheField = typeof(RenderTargetDrawingContext).GetField(
            "_textFormatCache",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(cacheField);
        var keyType = cacheField!.FieldType.GetGenericArguments()[0];
        Assert.True(keyType.IsValueType);
        Assert.NotEqual(typeof(string), keyType);
    }

    // ── Axis-aligned stroke phase (DrawPathFigurePolygon snap) ──────────────
    //
    // The regression these pin down: a 1px stroke whose center is rounded onto a
    // pixel BOUNDARY is split by anti-aliasing into two ~half-covered columns and
    // reads as a fuzzy 2px line (the solution-tree "dependencies" icon's three
    // vertical bars). The snap must therefore land odd device widths on
    // half-pixel centers and even widths on integer centers, in DEVICE space.

    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(1.2, 1)]   // 16px icon stroke at 100% DPI
    [InlineData(1.8, 2)]   // same stroke at 150% DPI
    [InlineData(2.4, 2)]   // same stroke at 200% DPI
    [InlineData(3.0, 3)]
    [InlineData(0.3, 1)]   // hairline never rounds to 0
    [InlineData(1.5, 1)]   // exact midpoint resolves to the odd neighbour
    [InlineData(2.5, 3)]
    [InlineData(0.5, 1)]
    public void RoundWidthPreferOdd_PicksPhaseDecidingWidth(double deviceWidth, int expected)
    {
        Assert.Equal(expected, RenderTargetDrawingContext.RoundWidthPreferOdd(deviceWidth));
    }

    [Theory]
    // Odd rounded width → half-pixel center: one fully covered column.
    [InlineData(8.0, 1.2, 8.5)]
    [InlineData(2.6666666667, 1.2, 2.5)]
    [InlineData(8.0, 1.0, 8.5)]   // the old integer snap parked exactly here on the boundary
    // Even rounded width → integer center: whole columns on both sides.
    [InlineData(8.0, 1.8, 8.0)]
    [InlineData(8.3, 2.4, 8.0)]
    // No usable width → plain rounding.
    [InlineData(8.3, 0.0, 8.0)]
    public void SnapStrokeCenter_MatchesPhaseToDeviceWidth(double coord, double width, double expected)
    {
        Assert.Equal(expected, RenderTargetDrawingContext.SnapStrokeCenter(coord, width), 9);
    }

    [Fact]
    public void ComputeAxisAlignedSnap_OddWidthStroke_LandsOnHalfPixelCenter()
    {
        var ok = RenderTargetDrawingContext.ComputeAxisAlignedSnap(
            baseX: 2.6666666667, baseY: 4.0, strokeThickness: 1.2, hasStroke: true,
            m11: 1, m12: 0, m21: 0, m22: 1, tdx: 0, tdy: 0,
            dpiScaleX: 1.0, dpiScaleY: 1.0,
            out var snapDx, out var snapDy);

        Assert.True(ok);
        Assert.Equal(2.5 - 2.6666666667, snapDx, 9);
        Assert.Equal(0.5, snapDy, 9);
    }

    [Fact]
    public void ComputeAxisAlignedSnap_FoldsDpiIntoPhaseDecision()
    {
        // 1.2 DIP stroke at 150% DPI = 1.8 device px → even → integer center.
        // The DIP-space integer rounding this replaced could not see the DPI at all.
        var ok = RenderTargetDrawingContext.ComputeAxisAlignedSnap(
            baseX: 2.0, baseY: 2.0, strokeThickness: 1.2, hasStroke: true,
            m11: 1, m12: 0, m21: 0, m22: 1, tdx: 0, tdy: 0,
            dpiScaleX: 1.5, dpiScaleY: 1.5,
            out var snapDx, out var snapDy);

        Assert.True(ok);
        Assert.Equal(0.0, snapDx, 9);   // device 3.0 already on an integer center
        Assert.Equal(0.0, snapDy, 9);
    }

    [Fact]
    public void ComputeAxisAlignedSnap_FoldsNativeScaleIntoDeviceSpace()
    {
        // A ×2 native scale: DIP 2.6667 sits at device 5.3333; a 1.2 DIP stroke is
        // 2.4 device px → even → integer center 5.0 → snap of −0.3333/2 DIP.
        var ok = RenderTargetDrawingContext.ComputeAxisAlignedSnap(
            baseX: 2.6666666667, baseY: 0.0, strokeThickness: 1.2, hasStroke: true,
            m11: 2, m12: 0, m21: 0, m22: 2, tdx: 0, tdy: 0,
            dpiScaleX: 1.0, dpiScaleY: 1.0,
            out var snapDx, out _);

        Assert.True(ok);
        Assert.Equal((5.0 - 5.3333333334) / 2.0, snapDx, 8);
    }

    [Fact]
    public void ComputeAxisAlignedSnap_FillOnly_LandsEdgeOnInteger()
    {
        var ok = RenderTargetDrawingContext.ComputeAxisAlignedSnap(
            baseX: 2.3, baseY: 7.8, strokeThickness: 0, hasStroke: false,
            m11: 1, m12: 0, m21: 0, m22: 1, tdx: 0, tdy: 0,
            dpiScaleX: 1.0, dpiScaleY: 1.0,
            out var snapDx, out var snapDy);

        Assert.True(ok);
        Assert.Equal(-0.3, snapDx, 9);
        Assert.Equal(0.2, snapDy, 9);
    }

    [Theory]
    [InlineData(0.7071, 0.7071, -0.7071, 0.7071)]  // rotation
    [InlineData(1.0, 0.3, 0.0, 1.0)]               // skew
    [InlineData(-1.0, 0.0, 0.0, 1.0)]              // mirror
    [InlineData(0.0, 0.0, 0.0, 0.0)]               // degenerate
    public void ComputeAxisAlignedSnap_RefusesNonAxisPreservingTransforms(
        double m11, double m12, double m21, double m22)
    {
        var ok = RenderTargetDrawingContext.ComputeAxisAlignedSnap(
            baseX: 2.0, baseY: 2.0, strokeThickness: 1.2, hasStroke: true,
            m11, m12, m21, m22, tdx: 0, tdy: 0,
            dpiScaleX: 1.0, dpiScaleY: 1.0,
            out var snapDx, out var snapDy);

        Assert.False(ok);
        Assert.Equal(0.0, snapDx);
        Assert.Equal(0.0, snapDy);
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
