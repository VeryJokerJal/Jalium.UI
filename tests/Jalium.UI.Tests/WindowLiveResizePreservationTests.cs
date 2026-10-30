namespace Jalium.UI.Tests;

public sealed class WindowLiveResizePreservationTests
{
    [Theory]
    [InlineData(120, 130, 900, 700, 0x00C0)] // Shrink from top-left.
    [InlineData(80, 70, 900, 700, 0x00C0)]   // Grow from top-left.
    [InlineData(120, 100, 900, 700, 0x0090)] // Left edge.
    [InlineData(100, 130, 900, 700, 0x0060)] // Top edge.
    [InlineData(100, 100, 940, 700, 0x0030)] // Right edge.
    [InlineData(100, 100, 900, 740, 0x0030)] // Bottom edge.
    [InlineData(120, 100, 900, 740, 0x0090)] // Bottom-left corner.
    [InlineData(100, 130, 940, 700, 0x0060)] // Top-right corner.
    [InlineData(100, 100, 940, 740, 0x0030)] // Bottom-right corner.
    [InlineData(100, 100, 900, 700, 0x0030)] // Unchanged rectangle.
    [InlineData(120, 130, 920, 730, 0x0030)] // Moving without resizing.
    public void RetainedImage_StaysAlignedWithTheStationaryEdges(
        int left, int top, int right, int bottom, int expected)
    {
        Assert.Equal(expected, Window.ComputeLiveResizePreservation(
            (100, 100, 900, 700), (left, top, right, bottom)));
    }

    [Fact]
    public void MissingClientGeometry_RequestsRedrawInsteadOfPreservingAnInvalidImage()
    {
        Assert.Equal(0x0300, Window.ComputeLiveResizePreservation(default, (100, 100, 900, 700)));
        Assert.Equal(0x0300, Window.ComputeLiveResizePreservation((100, 100, 900, 700), default));
    }
}
