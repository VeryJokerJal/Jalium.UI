using Jalium.UI.Controls.Virtualization;

namespace Jalium.UI.Tests;

/// <summary>
/// Validates ItemHeightIndex, in particular that the incremental estimate-drift path in
/// SetMeasuredHeight (which replaced a full O(itemCount) RebuildBlockSums) stays numerically
/// consistent with the ground-truth "offset = sum of resolved heights" invariant.
/// </summary>
public class ItemHeightIndexTests
{
    [Fact]
    public void SetMeasuredHeight_IncrementalEstimateDrift_OffsetsMatchResolvedHeights()
    {
        const int count = 3000; // spans ~24 blocks of 128
        var index = new ItemHeightIndex(28d);
        index.Reset(count, 28d);

        var measured = new Dictionary<int, double>();
        // Measure a scattered subset with heights well above the 28 seed, so the running
        // average drifts past 0.5px repeatedly and exercises the incremental estimate-change
        // path (the one that used to do a full O(itemCount) rebuild on every drift).
        for (int i = 0; i < count; i += 5)
        {
            double h = 40 + (i % 4) * 5; // 40 / 45 / 50 / 55
            index.SetMeasuredHeight(i, h);
            measured[i] = h;
        }

        // The estimate must have moved off the seed, proving the drift path was taken.
        Assert.True(index.EstimatedHeight > 35,
            $"estimate did not converge upward: {index.EstimatedHeight}");

        double estimate = index.EstimatedHeight;
        double Resolved(int i) => measured.TryGetValue(i, out var h) ? h : estimate;

        // Ground-truth prefix offsets: measured rows use their height, others the estimate.
        var expected = new double[count + 1];
        for (int i = 0; i < count; i++)
        {
            expected[i + 1] = expected[i] + Resolved(i);
        }

        for (int i = 0; i <= count; i++)
        {
            Assert.True(Math.Abs(expected[i] - index.GetOffsetForIndex(i)) < 1.0,
                $"offset[{i}] expected {expected[i]:F2}, got {index.GetOffsetForIndex(i):F2}");
        }

        Assert.True(Math.Abs(expected[count] - index.TotalHeight) < 1.0,
            $"total expected {expected[count]:F2}, got {index.TotalHeight:F2}");
    }

    [Fact]
    public void GetIndexAtOffset_RoundTripsWithOffsets()
    {
        const int count = 2000;
        var index = new ItemHeightIndex(28d);
        index.Reset(count, 28d);

        for (int i = 0; i < count; i += 3)
        {
            index.SetMeasuredHeight(i, 50d);
        }

        for (int i = 0; i < count; i += 91)
        {
            var offset = index.GetOffsetForIndex(i);
            // A point just inside row i must map back to index i.
            var found = index.GetIndexAtOffset(offset + 0.1);
            Assert.Equal(i, found);
        }
    }

    [Fact]
    public void AllRowsMeasuredUniform_GivesExactOffsetsAndConvergedEstimate()
    {
        const int count = 1000;
        const double h = 40d;
        var index = new ItemHeightIndex(28d);
        index.Reset(count, 28d);

        for (int i = 0; i < count; i++)
        {
            index.SetMeasuredHeight(i, h);
        }

        Assert.True(Math.Abs(count * h - index.TotalHeight) < 1.0);
        Assert.True(Math.Abs(500 * h - index.GetOffsetForIndex(500)) < 1.0);
        // With every row the same height, the estimate converges to that height.
        Assert.Equal(h, index.EstimatedHeight, precision: 3);
    }

    [Fact]
    public void SetMeasuredHeight_ThenRemeasureSameHeight_IsStable()
    {
        const int count = 600;
        var index = new ItemHeightIndex(28d);
        index.Reset(count, 28d);

        for (int i = 0; i < count; i++)
        {
            index.SetMeasuredHeight(i, 32d);
        }

        var totalBefore = index.TotalHeight;
        var offsetBefore = index.GetOffsetForIndex(300);

        // Re-measuring already-measured rows to the same height must not drift state.
        for (int i = 0; i < count; i++)
        {
            index.SetMeasuredHeight(i, 32d);
        }

        Assert.Equal(totalBefore, index.TotalHeight, precision: 3);
        Assert.Equal(offsetBefore, index.GetOffsetForIndex(300), precision: 3);
    }
}
