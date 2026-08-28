using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Pins the whole-pixel snapping policy for hard (scissor-only) rectangular clips.
/// Round-to-nearest is the settled trade-off: inward snapping (Ceiling/Floor) shaved
/// the top border off 1px strokes sitting flush against a fractional-Y ScrollViewer
/// (the Gallery filter-chip "clipped top edge" defect), while outward snapping
/// (Floor/Ceiling) let un-antialiased bitmap rows leak a full pixel past the clip
/// (the gradient-scrim seam defect). Nearest bounds both at half a pixel and matches
/// the straight-edge policy of the native rounded-clip path (EmitRoundedClipPair).
/// </summary>
public sealed class HardClipSnapTests
{
    [Fact]
    public void IntegralRect_PassesThroughUnchanged()
    {
        var (x, y, w, h) = RenderTargetDrawingContext.SnapHardClipRect(24, 162, 424, 196);

        Assert.Equal(24f, x);
        Assert.Equal(162f, y);
        Assert.Equal(400f, w);
        Assert.Equal(34f, h);
    }

    [Fact]
    public void SmallFraction_RoundsDown_KeepingFlushContentRow()
    {
        // The Gallery chip case: ScrollViewer clip top landed at 162.071 while the
        // chip's 1px top stroke occupied [162.071, 163.071). Inward snapping put the
        // scissor at 163 and erased the stroke's only row; nearest keeps it.
        var (x, y, _, h) = RenderTargetDrawingContext.SnapHardClipRect(
            272.071, 162.071, 700.071, 204.071);

        Assert.Equal(272f, x);
        Assert.Equal(162f, y);
        Assert.Equal(42f, h);
    }

    [Fact]
    public void LargeFraction_RoundsUp()
    {
        var (x, y, w, h) = RenderTargetDrawingContext.SnapHardClipRect(
            10.6, 20.9, 110.6, 60.9);

        Assert.Equal(11f, x);
        Assert.Equal(21f, y);
        Assert.Equal(100f, w);
        Assert.Equal(40f, h);
    }

    [Fact]
    public void HairlineRect_WidensToOnePixel_InsteadOfCollapsing()
    {
        var (_, _, w, h) = RenderTargetDrawingContext.SnapHardClipRect(
            5.3, 8.3, 5.7, 8.7);

        Assert.Equal(1f, w);
        Assert.Equal(1f, h);
    }

    [Fact]
    public void EmptyRect_StaysEmpty()
    {
        var (_, _, w, h) = RenderTargetDrawingContext.SnapHardClipRect(5, 8, 5, 8);

        Assert.Equal(0f, w);
        Assert.Equal(0f, h);
    }
}
