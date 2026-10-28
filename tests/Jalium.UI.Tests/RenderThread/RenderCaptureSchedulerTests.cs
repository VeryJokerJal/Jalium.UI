using Jalium.UI.Rendering;

namespace Jalium.UI.Tests.RenderThread;

public sealed class RenderCaptureSchedulerTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    [InlineData(360)]
    public void PhaseCadenceDoesNotAccumulateRepeatedLateHandoffs(int rate)
    {
        const long frequency = 10_000_000;
        long preparation = frequency / rate / 5;
        long replay = frequency / rate / 4;
        var timing = new RenderCaptureTiming();
        timing.Observe(preparation, replay, frequency, rate);
        long start = frequency, ready = start;
        const int frames = 1000;
        for (int frame = 0; frame < frames; ++frame)
        {
            long now = ready + replay;
            // Model a repeatable 350us wake/dispatch overrun. The fixed 50us
            // margin absorbs part of it; the remaining 300us must not accumulate.
            for (int wake = 0; ; ++wake)
            {
                long delay = timing.GetCaptureDelayMicroseconds(ready, now, frequency, rate);
                if (delay == 0) break;
                Assert.True(wake < 8, "A phase deadline must not need unbounded rearming.");
                now += delay * frequency / 1_000_000 + frequency * 350 / 1_000_000;
            }
            ready = now + preparation;
            timing.Observe(preparation, replay, frequency, rate);
        }
        double expected = start + frames * (double)frequency / rate;
        Assert.InRange(ready - expected, 0, frequency / 1000.0);
    }

    [Fact]
    public void PhaseCadenceSkipsLongStallsAndResetsForRefreshRateChanges()
    {
        const long frequency = 1_000_000;
        var timing = new RenderCaptureTiming();
        timing.Observe(1000, 1000, frequency, 240);
        _ = timing.GetCaptureDelayMicroseconds(1_000_000, 1_001_000, frequency, 240);
        long stalled = 1_100_000;
        long delay = timing.GetCaptureDelayMicroseconds(stalled, stalled + 1000, frequency, 240);
        Assert.InRange(delay, 0, 2083);
        Assert.Equal(delay, timing.GetCaptureDelayMicroseconds(stalled, stalled + 1000, frequency, 240));
        // A different display starts a fresh cadence at its actual native start.
        Assert.InRange(timing.GetCaptureDelayMicroseconds(1_200_000, 1_210_000, frequency, 60), 5615, 5617);
    }

    [Fact]
    public void PhaseCadenceRebasesWhenNativeStartsArriveBeforeTheOldGrid()
    {
        const long frequency = 1_000_000;
        var timing = new RenderCaptureTiming();
        timing.Observe(1000, 1000, frequency, 240);
        for (int frame = 0; frame < 100; ++frame)
        {
            long ready = 1_000_000 + frame * 1000;
            Assert.InRange(timing.GetCaptureDelayMicroseconds(ready, ready + 1500, frequency, 240), 1615, 1617);
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(165)]
    [InlineData(240)]
    [InlineData(360)]
    [InlineData(1000)]
    public void PhaseDeadlineKeepsPreparationWithinRefreshBudgetAcrossRates(int rate)
    {
        const long frequency = 1_000_000;
        long period = frequency / rate;
        long preparation = period / 5;
        long replay = period / 4;
        var timing = new RenderCaptureTiming();
        timing.Observe(preparation, replay, frequency, rate);
        Assert.False(timing.OverlapRecording);
        for (int frame = 1; frame <= 100; frame++)
        {
            long ready = (long)(frame * (double)frequency / rate);
            long afterPresent = ready + replay;
            long delay = timing.GetCaptureDelayMicroseconds(ready, afterPresent, frequency, rate);
            Assert.InRange(delay, 0, period / 2 + 1);
            Assert.True(afterPresent + delay + preparation < ready + period);
            Assert.Equal(0, timing.GetCaptureDelayMicroseconds(ready, ready + period * 2, frequency, rate));
        }
    }

    [Fact]
    public void PhaseInvalidationRequestsCoalesceWhileHeldWithoutReservingAFreeSlot()
    {
        var scheduler = new RenderCaptureScheduler();
        Assert.True(scheduler.RequestCapture());
        Assert.True(scheduler.RequestCapture());
        Assert.True(scheduler.TryBeginCapture(out long ticket));
        for (int i = 0; i < 100; i++) Assert.False(scheduler.RequestCapture());
        Assert.True(scheduler.HasPendingCapture(ticket));
        Assert.Equal(RenderCaptureCompletion.CaptureRequested, scheduler.Complete(ticket, retryPending: false));
        Assert.True(scheduler.RequestCapture());
        scheduler.Stop();
        Assert.False(scheduler.RequestCapture());
    }

    [Fact]
    public void PhaseTimingStartsWithOverlapUntilMeasurementsAreAvailable()
    {
        var timing = new RenderCaptureTiming();
        Assert.True(timing.OverlapRecording);
        timing.Observe(0, 1000, 1_000_000, 240);
        Assert.True(timing.OverlapRecording);
        Assert.Equal(0, timing.GetCaptureDelayMicroseconds(1_000_000, 1_000_000, 1_000_000, 240));
    }

    [Fact]
    public void PhaseTimingBudgetsTheFullUiPreparationAndBoundsDelay()
    {
        var timing = new RenderCaptureTiming();
        timing.Observe(1500, 1500, 1_000_000, 240);
        Assert.False(timing.OverlapRecording);
        Assert.InRange(timing.GetCaptureDelayMicroseconds(1_000_000, 1_001_000, 1_000_000, 240), 1615, 1617);
        Assert.Equal(0, timing.GetCaptureDelayMicroseconds(1_000_000, 1_005_000, 1_000_000, 240));
    }

    [Fact]
    public void PhaseTimingRetainsOverlapForExpensiveFramesAndUsesHysteresis()
    {
        var timing = new RenderCaptureTiming();
        timing.Observe(1000, 2000, 1_000_000, 240);
        Assert.False(timing.OverlapRecording);
        for (int i = 0; i < 64; i++) timing.Observe(1000, 3000, 1_000_000, 240);
        Assert.False(timing.OverlapRecording);
        for (int i = 0; i < 32; i++) timing.Observe(4000, 4000, 1_000_000, 240);
        Assert.True(timing.OverlapRecording);
        Assert.Equal(0, timing.GetCaptureDelayMicroseconds(1_000_000, 1_000_000, 1_000_000, 240));
        for (int i = 0; i < 64; i++) timing.Observe(1000, 1000, 1_000_000, 240);
        Assert.False(timing.OverlapRecording);
    }

    [Fact]
    public void BusyRendererCoalescesCapturesAndThenReadsLatestInputState()
    {
        var scheduler = new RenderCaptureScheduler();
        Assert.True(scheduler.TryBeginCapture(out var first));
        int latestInput = 0;
        for (int i = 1; i <= 1000; i++)
        {
            latestInput = i;
            Assert.False(scheduler.TryBeginCapture(out _));
        }
        Assert.Equal(RenderCaptureCompletion.CaptureRequested, scheduler.Complete(first, retryPending: false));
        Assert.True(scheduler.IsAwaitingCapture(first));
        Assert.True(scheduler.TryBeginCapture(out var next));
        Assert.Equal(1000, latestInput);
        Assert.NotEqual(first, next);
        Assert.False(scheduler.IsAwaitingCapture(first));
    }

    [Fact]
    public void StaticFailedPresentKeepsItsRetryUntilThereIsNewState()
    {
        var scheduler = new RenderCaptureScheduler();
        Assert.True(scheduler.TryBeginCapture(out var ticket));
        Assert.Equal(RenderCaptureCompletion.Retry, scheduler.Complete(ticket, retryPending: true));
        Assert.False(scheduler.TryBeginCapture(out _));
        Assert.True(scheduler.HasPendingCapture(ticket));
        Assert.Equal(RenderCaptureCompletion.CaptureRequested, scheduler.Complete(ticket, retryPending: true));
        Assert.True(scheduler.TryBeginCapture(out _));
    }

    [Fact]
    public void StaleCompletionCannotReleaseANewerCapture()
    {
        var scheduler = new RenderCaptureScheduler();
        Assert.True(scheduler.TryBeginCapture(out var first));
        Assert.Equal(RenderCaptureCompletion.Idle, scheduler.Complete(first, retryPending: false));
        Assert.True(scheduler.TryBeginCapture(out var next));
        Assert.Equal(RenderCaptureCompletion.Idle, scheduler.Complete(first, retryPending: false));
        Assert.False(scheduler.TryBeginCapture(out _));
        Assert.Equal(RenderCaptureCompletion.CaptureRequested, scheduler.Complete(next, retryPending: false));
    }

    [Fact]
    public void StopInvalidatesQueuedWakesAndRetries()
    {
        var scheduler = new RenderCaptureScheduler();
        Assert.True(scheduler.TryBeginCapture(out var ticket));
        Assert.False(scheduler.TryBeginCapture(out _));
        scheduler.Stop();
        Assert.False(scheduler.IsAwaitingCapture(ticket));
        Assert.False(scheduler.TryBeginCapture(out _));
        Assert.Equal(RenderCaptureCompletion.Idle, scheduler.Complete(ticket, retryPending: true));
    }

    [Fact]
    public void ConcurrentRequestsNeverGrantTwoCaptures()
    {
        var scheduler = new RenderCaptureScheduler();
        int granted = 0;
        long ticket = 0;
        Parallel.For(0, 128, _ =>
        {
            if (scheduler.TryBeginCapture(out var acquired))
            {
                Interlocked.Exchange(ref ticket, acquired);
                Interlocked.Increment(ref granted);
            }
        });
        Assert.Equal(1, granted);
        Assert.Equal(RenderCaptureCompletion.CaptureRequested, scheduler.Complete(ticket, retryPending: false));
    }
}
