using System.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class AnimationKeyTimeRuntimeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UniformKeyTimes_FillTheWholeDuration(int count)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1) };
        for (int i = 1; i <= count; i++) animation.KeyFrames.Add(new LinearDoubleKeyFrame(100d * i / count, KeyTime.Uniform));
        var clock = Sample(animation, .25);
        Assert.Equal(25d, clock.GetCurrentValue(0d, 0d));
        clock.Tick(Start + Ticks(.75));
        Assert.Equal(75d, clock.GetCurrentValue(0d, 0d));
    }

    [Fact]
    public void UniformKeyTimes_RespectTheirExplicitNeighbors()
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4) };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(20, KeyTime.Uniform));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(40, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(60, KeyTime.Uniform));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(80, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4))));
        var clock = Sample(animation, .5);
        Assert.Equal(10d, clock.GetCurrentValue(0d, 0d));
        clock.Tick(Start + Ticks(3.5));
        Assert.Equal(70d, clock.GetCurrentValue(0d, 0d));
    }

    [Fact]
    public void PacedKeyTimes_AllocateTimeByDistance()
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1) };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.Paced));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(10, KeyTime.Paced));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(100, KeyTime.Paced));
        var clock = Sample(animation, .05);
        Assert.Equal(5d, (double)clock.GetCurrentValue(0d, 0d), 5);
        clock.Tick(Start + Ticks(.5));
        Assert.Equal(50d, (double)clock.GetCurrentValue(0d, 0d), 5);
    }

    [Fact]
    public void PacedPointKeyTimes_UseGeometricDistance()
    {
        var animation = new PointAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1) };
        animation.KeyFrames.Add(new LinearPointKeyFrame(new Point(0, 0), KeyTime.Paced));
        animation.KeyFrames.Add(new LinearPointKeyFrame(new Point(30, 40), KeyTime.Paced));
        animation.KeyFrames.Add(new LinearPointKeyFrame(new Point(30, 190), KeyTime.Paced));
        var clock = Sample(animation, .125);
        Assert.Equal(new Point(15, 20), clock.GetCurrentValue(new Point(), new Point()));
    }

    [Fact]
    public void IdenticalKeyTimes_KeepCollectionOrderSoTheLastValueWins()
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1) };
        for (int i = 1; i <= 24; i++)
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(i, KeyTime.FromPercent(.5)));
        var clock = Sample(animation, .5);
        Assert.Equal(24d, clock.GetCurrentValue(0d, 0d));
    }

    [Fact]
    public void AccelerationRatio_ChangesKeyFrameTimeAsWellAsProgress()
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1), AccelerationRatio = 1 };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(100, KeyTime.FromPercent(1)));
        var clock = Sample(animation, .5);
        Assert.Equal(.25, clock.CurrentProgress, 5);
        Assert.Equal(25d, clock.GetCurrentValue(0d, 0d));
    }

    private const long Start = 1_000_000_000;
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);
    private static AnimationClock Sample(AnimationTimeline animation, double seconds)
    {
        var clock = animation.CreateClock();
        clock.BeginAt(Start);
        clock.Tick(Start + Ticks(seconds));
        return clock;
    }
}
