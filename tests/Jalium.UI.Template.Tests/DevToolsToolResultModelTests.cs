using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class DevToolsToolResultModelTests
{
    [Fact]
    public void MeasurementPreservesCoordinatesAndSnapsWithoutChangingLength()
    {
        Assert.True(DevToolsRulerMeasurement.TryCreate(new(10, 20), new(13, 24), false, out var measurement));
        Assert.Equal(3, measurement.DeltaX); Assert.Equal(4, measurement.DeltaY); Assert.Equal(5, measurement.Distance);
        Assert.Equal("5.0", measurement.DistanceText); Assert.Equal("53.1°", measurement.AngleText);
        Assert.Contains("(10, 20) → (13, 24)", measurement.AutomationName);
        Assert.True(DevToolsRulerMeasurement.TrySnap(measurement.Start, measurement.End, out var end));
        Assert.True(DevToolsRulerMeasurement.TryCreate(measurement.Start, end, true, out var snapped));
        Assert.Equal(5, snapped.Distance, 10); Assert.Equal(45, snapped.AngleDegrees, 10); Assert.True(snapped.IsSnapped);
        Assert.True(DevToolsRulerMeasurement.TrySnap(new(10, 20), new(10.1, 20.1), out var near)); Assert.Equal(new Point(10, 20), near);
    }

    [Fact]
    public void MeasurementBoundsRejectInvalidResultsAndAvoidSquaredDistanceOverflow()
    {
        Assert.True(DevToolsRulerMeasurement.TryCreate(new(), new(3e200, 4e200), false, out var huge));
        Assert.True(double.IsFinite(huge.Distance)); Assert.InRange(huge.Distance / 1e200, 4.999999, 5.000001);
        Assert.InRange(huge.DistanceText.Length, 1, 16);
        Assert.False(DevToolsRulerMeasurement.TryCreate(new(double.NaN, 0), new(), false, out _));
        Assert.False(DevToolsRulerMeasurement.TryCreate(new(), new(0, double.PositiveInfinity), false, out _));
        Assert.False(DevToolsRulerMeasurement.TryCreate(new(-double.MaxValue, 0), new(double.MaxValue, 0), false, out _));
        Assert.True(DevToolsRulerMeasurement.TryCreate(new(2, 2), new(2, 2), false, out var zero)); Assert.Equal(0, zero.Distance); Assert.Equal("0.0°", zero.AngleText);
    }

    [Theory]
    [InlineData(5, .01)] [InlineData(-5, -.01)] [InlineData(.01, 5)] [InlineData(-.01, -5)]
    public void SnapUsesExactAxisCoordinates(double x, double y)
    {
        var start = new Point(10, 20);
        Assert.True(DevToolsRulerMeasurement.TrySnap(start, new(10 + x, 20 + y), out var end));
        if (Math.Abs(x) > Math.Abs(y)) Assert.Equal(start.Y, end.Y);
        else Assert.Equal(start.X, end.X);
    }

    [Fact]
    public void PointerSnapPreviewDoesNotAllocateFormattedResultObjects()
    {
        _ = DevToolsRulerMeasurement.TrySnap(new(), new(3, 4), out _);
        long before = GC.GetAllocatedBytesForCurrentThread(); double checksum = 0;
        for (int i = 0; i < 10000; i++)
            if (DevToolsRulerMeasurement.TrySnap(new(), new(i % 101, i % 79), out var end)) checksum += end.X;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(checksum > 0); Assert.InRange(allocated, 0, 512);
    }

    [Theory]
    [InlineData(255, 0, 0, 0, 1, .5)]
    [InlineData(0, 255, 0, 120, 1, .5)]
    [InlineData(0, 0, 255, 240, 1, .5)]
    [InlineData(255, 255, 255, 0, 0, 1)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    public void ColorSampleKeepsAlphaAndProvidesStableRgbAndHsl(byte r, byte g, byte b, double hue, double saturation, double lightness)
    {
        Assert.True(DevToolsColorSample.TryCreate(Color.FromArgb(128, r, g, b), new(3, 4), out var sample));
        Assert.StartsWith("#80", sample.ArgbHex); Assert.Equal(9, sample.ArgbHex.Length); Assert.Equal(7, sample.RgbHex.Length);
        Assert.Equal(hue, sample.Hue, 6); Assert.Equal(saturation, sample.Saturation, 6); Assert.Equal(lightness, sample.Lightness, 6);
        Assert.Equal(128 / 255d, sample.AlphaFraction); Assert.Contains("alpha 128", sample.AutomationName);
        Assert.False(DevToolsColorSample.TryCreate(sample.Color, new(double.NaN, 0), out _));
    }
}
