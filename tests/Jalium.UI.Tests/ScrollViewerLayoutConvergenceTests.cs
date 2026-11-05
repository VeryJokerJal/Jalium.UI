using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ScrollViewerLayoutConvergenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    public void SmallerArrangeSlot_SettlesWithoutRepeatedMeasureInvalidation(double arrangedHeight)
    {
        var viewer = CreateViewer();
        var corrections = 0;

        for (int pass = 0; pass < 8; pass++)
        {
            viewer.Measure(new Size(200, 214));
            viewer.Arrange(new Rect(0, 0, 200, arrangedHeight));
            if (!viewer.IsMeasureValid) corrections++;
        }

        Assert.InRange(corrections, 0, 1);
        Assert.True(viewer.IsMeasureValid);
        Assert.True(viewer.IsArrangeValid);
        Assert.Equal(arrangedHeight, viewer.ViewportHeight);
    }

    [Fact]
    public void ChangedArrangeSlot_StillUpdatesViewportAndSettles()
    {
        var viewer = CreateViewer();

        foreach (double height in new double[] { 0, 40, 80, 0, 120 })
        {
            for (int pass = 0; pass < 2; pass++)
            {
                viewer.Measure(new Size(200, 214));
                viewer.Arrange(new Rect(0, 0, 200, height));
            }

            Assert.True(viewer.IsMeasureValid);
            Assert.True(viewer.IsArrangeValid);
            Assert.Equal(height, viewer.ViewportHeight);
            Assert.Equal(Visibility.Collapsed, viewer.ComputedVerticalScrollBarVisibility);
        }
    }

    private static ScrollViewer CreateViewer() => new()
    {
        Content = new Border(),
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
}
