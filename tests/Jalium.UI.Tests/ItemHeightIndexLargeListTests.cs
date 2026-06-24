using Jalium.UI.Controls.Virtualization;

namespace Jalium.UI.Tests;

/// <summary>
/// Reproduces the million-row offset drift: the cumulative arrays in ItemHeightIndex are
/// accumulated in float, which only resolves integers to ~16.7M and quantizes values near
/// 40M to multiples of 4px. Small per-item corrections get rounded away during prefix
/// propagation, producing block-boundary gaps (the "blank in the middle/bottom" symptom).
/// </summary>
public class ItemHeightIndexLargeListTests
{
    private const int Million = 1_000_000;

    // Largest absolute adjacent-item gap error across block boundaries.
    private static (double maxGapErr, int worstIndex) ScanBoundaryGaps(ItemHeightIndex index, double expectedHeight)
    {
        double maxErr = 0;
        int worst = -1;
        // Block size is 128; check a band of boundaries near the end of the list.
        for (int boundary = 128; boundary < index.Count; boundary += 128)
        {
            var prev = index.GetOffsetForIndex(boundary - 1);
            var here = index.GetOffsetForIndex(boundary);
            var gap = here - prev; // should equal the height of item (boundary-1)
            var err = Math.Abs(gap - expectedHeight);
            if (err > maxErr)
            {
                maxErr = err;
                worst = boundary;
            }
        }

        return (maxErr, worst);
    }

    [Fact]
    public void MillionRows_UniformHeight_NoBoundaryGap()
    {
        const double h = 40d;
        var index = new ItemHeightIndex(28d);
        index.Reset(Million, 28d);

        for (int i = 0; i < Million; i++)
        {
            index.SetMeasuredHeight(i, h);
        }

        var (maxErr, worst) = ScanBoundaryGaps(index, h);
        Assert.True(maxErr < 1.0,
            $"block-boundary gap error {maxErr:F2}px at index {worst} (total={index.TotalHeight:F1})");
    }

    [Fact]
    public void MillionRows_JumpToBottom_StickyEstimate_NoBoundaryGap()
    {
        // Faithful to the demo: estimate seeds at 28, then a window near the bottom gets
        // realized/measured to a real container height that differs slightly from the seed.
        var index = new ItemHeightIndex(28d);
        index.Reset(Million, 28d);

        // The realized container height in the demo lands around 40px.
        const double containerHeight = 40d;

        // Simulate the realization window after a jump to the bottom (~60 rows).
        for (int i = Million - 64; i < Million; i++)
        {
            index.SetMeasuredHeight(i, containerHeight);
        }

        // Offsets of consecutive realized rows must be one row apart.
        double maxErr = 0;
        int worst = -1;
        for (int i = Million - 63; i < Million; i++)
        {
            var gap = index.GetOffsetForIndex(i) - index.GetOffsetForIndex(i - 1);
            var err = Math.Abs(gap - containerHeight);
            if (err > maxErr) { maxErr = err; worst = i; }
        }

        Assert.True(maxErr < 1.0,
            $"adjacent realized rows {maxErr:F2}px apart vs {containerHeight} at index {worst} " +
            $"(estimate={index.EstimatedHeight:F2}, total={index.TotalHeight:F1})");
    }

    [Fact]
    public void MillionRows_UniformRowsButStickyEstimate_NoBoundaryGap()
    {
        // The faithful demo trigger: rows render at one uniform height, but the estimate
        // converged from the 28 seed and parks within 0.5px of the true average (the retarget
        // threshold), so it never lands exactly on it. Every realized row near the bottom then
        // carries a small (estimate - measured) delta. With float prefixes those deltas are
        // rounded away near 40M and pile up into a block-sized gap; doubles keep them.
        var index = new ItemHeightIndex(28d);
        index.Reset(Million, 28d);

        // Seed a slightly-high estimate so it sticks ~0.4px above the rows measured next.
        for (int i = 0; i < 8; i++)
        {
            index.SetMeasuredHeight(i, 40.4d);
        }

        // Realize a contiguous window across a block boundary near the bottom, all uniform.
        const double rowHeight = 40d;
        int windowStart = (Million / 128 - 3) * 128 - 20;
        int windowEnd = windowStart + 80;
        for (int i = windowStart; i < windowEnd; i++)
        {
            index.SetMeasuredHeight(i, rowHeight);
        }

        // The estimate must still be parked off the row height (otherwise the case is moot).
        Assert.True(Math.Abs(index.EstimatedHeight - rowHeight) > 0.05,
            $"estimate unexpectedly snapped to row height: {index.EstimatedHeight:F3}");

        double maxErr = 0;
        int worst = -1;
        for (int i = windowStart + 1; i < windowEnd; i++)
        {
            var gap = index.GetOffsetForIndex(i) - index.GetOffsetForIndex(i - 1);
            var err = Math.Abs(gap - rowHeight);
            if (err > maxErr) { maxErr = err; worst = i; }
        }

        Assert.True(maxErr < 1.0,
            $"adjacent realized rows {maxErr:F2}px apart vs {rowHeight} at index {worst} " +
            $"(estimate={index.EstimatedHeight:F3})");
    }

    [Fact]
    public void MillionRows_SlightHeightVariation_BoundaryGapStaysSubPixel()
    {
        // Estimate stays near 40 (many samples) while a late block measures a hair shorter.
        var index = new ItemHeightIndex(28d);
        index.Reset(Million, 28d);

        // Seed a converged 40px estimate from an early window.
        for (int i = 0; i < 256; i++)
        {
            index.SetMeasuredHeight(i, 40d);
        }

        // A late block of 128 rows measures 38px (2px under the sticky estimate). With float
        // prefixes, each 2px correction is lost near 40M, accumulating into a ~256px gap.
        int blockStart = (Million / 128 - 4) * 128; // a clean block boundary near the end
        for (int i = blockStart; i < blockStart + 128; i++)
        {
            index.SetMeasuredHeight(i, 38d);
        }

        var prevBlockBoundary = blockStart; // start of the short block
        var nextBlockBoundary = blockStart + 128; // start of the following block

        var gapAtNext = index.GetOffsetForIndex(nextBlockBoundary) - index.GetOffsetForIndex(nextBlockBoundary - 1);
        Assert.True(Math.Abs(gapAtNext - 38d) < 1.0,
            $"boundary gap after short block = {gapAtNext:F2}px (expected ~38); " +
            $"estimate={index.EstimatedHeight:F2}");
    }
}
