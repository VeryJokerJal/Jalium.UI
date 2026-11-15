using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

public sealed class DispatcherTimerFrameClockTests
{
    [Theory]
    [InlineData(240, 60, true)]
    [InlineData(144, 60, true)]
    [InlineData(120, 60, true)]
    [InlineData(60, 60, true)]
    [InlineData(30, 30, true)]
    [InlineData(240, 120, true)]
    [InlineData(240, 30, false)]
    [InlineData(60, 30, false)]
    public void AnimationTimersUseFrameClockAcrossRefreshRates(int refreshRate, int fps, bool expected)
    {
        Assert.Equal(expected, DispatcherTimer.ShouldUseCompositionTarget(
            TimeSpan.FromMilliseconds(1000.0 / fps), refreshRate));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void LongTimersKeepDedicatedSchedule(int refreshRate)
    {
        Assert.False(DispatcherTimer.ShouldUseCompositionTarget(TimeSpan.FromMilliseconds(500), refreshRate));
    }
}
