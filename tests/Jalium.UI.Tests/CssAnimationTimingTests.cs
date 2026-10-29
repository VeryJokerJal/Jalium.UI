using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssAnimationTimingTests
{
    [Fact]
    public void PositiveDelayUsesBackwardsFillWithoutApplyingStepStart()
    {
        var timing = new CssAnimationTiming(1000, 200, 1,
            CssAnimationDirection.Normal, CssAnimationFillMode.Both);
        var element = new Border();
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes fade { from { opacity: 0; animation-timing-function: step-start } to { opacity: 1 } }
            """).Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(element), rule)[UIElement.OpacityProperty];

        var before = timing.Sample(100);
        Assert.True(before.Applies);
        Assert.True(before.DuringDelay);
        Assert.Equal(0d, sampler.Sample(in before, CssTimingFunction.EaseDefault));
        Assert.Equal(1d, sampler.Sample(timing.Sample(200), CssTimingFunction.EaseDefault));
    }

    [Fact]
    public void NegativeDelayStartsPartwayThroughTheFirstCycle()
    {
        var timing = new CssAnimationTiming(1000, -250, 1,
            CssAnimationDirection.Normal, CssAnimationFillMode.None);

        var sample = timing.Sample(0);
        Assert.True(sample.Applies);
        Assert.False(sample.DuringDelay);
        Assert.Equal(.25, sample.Progress);
        Assert.False(sample.Finished);
    }

    [Fact]
    public void AlternateReverseChangesDirectionEachCycleAndPreservesFinalEndpoint()
    {
        var timing = new CssAnimationTiming(1000, 0, 2,
            CssAnimationDirection.AlternateReverse, CssAnimationFillMode.Forwards);

        Assert.Equal(.75, timing.Sample(250).Progress);
        Assert.Equal(.25, timing.Sample(1250).Progress);
        var final = timing.Sample(2000);
        Assert.True(final.Applies);
        Assert.True(final.Finished);
        Assert.Equal(1d, final.Progress);
    }

    [Fact]
    public void FractionalIterationsFillAtThePartialFinalPosition()
    {
        var timing = new CssAnimationTiming(1000, 0, 2.5,
            CssAnimationDirection.Alternate, CssAnimationFillMode.Forwards);

        Assert.Equal(.5, timing.Sample(2500).Progress);
        Assert.Equal(.5, timing.Sample(3000).Progress);
        Assert.True(timing.Sample(3000).Finished);
    }

    [Fact]
    public void ZeroDurationAndZeroIterationsHonorTheFillMode()
    {
        var zeroDuration = new CssAnimationTiming(0, 100, 3,
            CssAnimationDirection.Reverse, CssAnimationFillMode.Both);
        Assert.Equal(1d, zeroDuration.Sample(0).Progress);
        Assert.Equal(0d, zeroDuration.Sample(100).Progress);
        Assert.True(zeroDuration.Sample(100).Finished);

        var zeroIterations = new CssAnimationTiming(1000, 0, 0,
            CssAnimationDirection.AlternateReverse, CssAnimationFillMode.Forwards);
        Assert.Equal(1d, zeroIterations.Sample(0).Progress);
        Assert.True(zeroIterations.Sample(0).Finished);
    }

    [Fact]
    public void InfiniteIterationCountKeepsRunning()
    {
        var timing = new CssAnimationTiming(1000, 0, double.PositiveInfinity,
            CssAnimationDirection.Alternate, CssAnimationFillMode.None);

        var sample = timing.Sample(2750);
        Assert.True(sample.Applies);
        Assert.False(sample.Finished);
        Assert.Equal(.75, sample.Progress);
    }
}
