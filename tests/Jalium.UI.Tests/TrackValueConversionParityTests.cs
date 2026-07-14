using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class TrackValueConversionParityTests
{
    [Fact]
    public void ValueConversionMethodsArePublicVirtual()
    {
        var pointMethod = typeof(Track).GetMethod(
            nameof(Track.ValueFromPoint),
            [typeof(Point)])!;
        var distanceMethod = typeof(Track).GetMethod(
            nameof(Track.ValueFromDistance),
            [typeof(double), typeof(double)])!;

        Assert.True(pointMethod.IsVirtual);
        Assert.False(pointMethod.IsFinal);
        Assert.True(distanceMethod.IsVirtual);
        Assert.False(distanceMethod.IsFinal);

        var track = new ProbeTrack();
        Assert.Equal(17, track.ValueFromPoint(default));
        Assert.Equal(23, track.ValueFromDistance(1, 2));
    }

    private sealed class ProbeTrack : Track
    {
        public override double ValueFromPoint(Point pt) => 17;
        public override double ValueFromDistance(double horizontal, double vertical) => 23;
    }
}
