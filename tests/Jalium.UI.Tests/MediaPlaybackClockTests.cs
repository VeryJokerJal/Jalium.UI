using System.Diagnostics;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class MediaPlaybackClockTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public async Task ClockAdvancesBySpeedRatherThanItsReciprocal(double speed)
    {
        using var clock = new AVSyncClock { SpeedRatio = speed };
        var wall = Stopwatch.StartNew();
        clock.Start(TimeSpan.Zero);
        await Task.Delay(100);
        double media = clock.GetMediaTime().TotalMilliseconds;
        double elapsed = wall.Elapsed.TotalMilliseconds;
        Assert.InRange(media / elapsed, speed * 0.85, speed * 1.15);
        clock.Pause();
        var paused = clock.GetMediaTime();
        await Task.Delay(30);
        Assert.Equal(paused, clock.GetMediaTime());
    }

    [Fact]
    public async Task ChangingSpeedRebasesWithoutJumpingAndDelayUsesWallTime()
    {
        using var clock = new AVSyncClock();
        clock.Start(TimeSpan.FromSeconds(10));
        await Task.Delay(50);
        double before = clock.GetMediaTime().TotalMilliseconds;
        clock.SpeedRatio = 4;
        double after = clock.GetMediaTime().TotalMilliseconds;
        Assert.InRange(after - before, 0, 50);
        var delay = clock.CalculateVideoDelay(clock.GetMediaTime().TotalMilliseconds + 1000);
        Assert.InRange(delay.TotalMilliseconds, 230, 250);
    }
}
