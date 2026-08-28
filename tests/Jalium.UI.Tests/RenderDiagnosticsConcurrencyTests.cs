using System.Diagnostics;
using Jalium.UI.Diagnostics;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression cover for the DevTools Perf tab crash: opening the tab flipped
/// <see cref="RenderDiagnostics.ApiStatsEnabled"/> on process-wide, and every draw call from
/// every thread then mutated ONE shared Dictionary. The render thread (on by default on
/// Windows) draws concurrently with the UI thread, so the buckets corrupted and the next
/// insert threw "Operations that change non-concurrent collections must have exclusive
/// access" straight out of RenderTarget.FillRectangle — unhandled, process gone.
/// </summary>
public class RenderDiagnosticsConcurrencyTests
{
    [Fact]
    public void RecordApi_FromManyThreadsConcurrently_DoesNotCorruptState()
    {
        bool previous = RenderDiagnostics.ApiStatsEnabled;
        RenderDiagnostics.ApiStatsEnabled = true;
        try
        {
            var failures = new List<Exception>();
            var start = new ManualResetEventSlim(false);
            var threads = new Thread[8];

            for (int t = 0; t < threads.Length; t++)
            {
                int id = t;
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        start.Wait();
                        for (int frame = 0; frame < 200; frame++)
                        {
                            for (int call = 0; call < 40; call++)
                            {
                                RenderDiagnostics.RecordApi(ApiNames[call % ApiNames.Length], call + id);
                            }
                            // Only one "window" is allowed to publish; the rest must still
                            // drop their own counters instead of letting them accumulate.
                            RenderDiagnostics.PublishAndResetApiStats(id == 0, 0, 0);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (failures) failures.Add(ex);
                    }
                });
            }

            foreach (var thread in threads) thread.Start();
            start.Set();
            foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(30)));

            Assert.Empty(failures);

            var published = RenderDiagnostics.LatestDrawApiStats;
            Assert.NotNull(published);
            // One publishing thread, one frame's worth of calls: 40 calls spread over the
            // distinct API names. A shared accumulator would have folded in the other seven
            // threads' work and blown this well past 40.
            Assert.Equal(40, published!.Entries.Sum(e => e.Count));
        }
        finally
        {
            RenderDiagnostics.ApiStatsEnabled = previous;
        }
    }

    private static readonly string[] ApiNames =
    {
        "FillRectangle", "DrawText", "PushClip", "PopClip", "FillPath", "DrawBitmap", "BeginDraw", "EndDraw",
    };

    [Fact]
    public void ShouldPublishFor_FiltersEveryWindowButTheSelectedOwner()
    {
        object target = new();
        object other = new();
        object? previous = RenderDiagnostics.StatsOwner;
        try
        {
            RenderDiagnostics.StatsOwner = null;
            Assert.True(RenderDiagnostics.ShouldPublishFor(target));
            Assert.True(RenderDiagnostics.ShouldPublishFor(other));

            RenderDiagnostics.StatsOwner = target;
            Assert.True(RenderDiagnostics.ShouldPublishFor(target));
            Assert.False(RenderDiagnostics.ShouldPublishFor(other));
        }
        finally
        {
            RenderDiagnostics.StatsOwner = previous;
        }
    }

    [Fact]
    public void FrameHistory_StampsEverySampleWithAWallClockTimestamp()
    {
        var history = new FrameHistory();
        long before = Stopwatch.GetTimestamp();
        history.Push(new FrameHistory.Sample(1, 2, 3, 6, 0));
        history.Push(new FrameHistory.Sample(1, 2, 3, 6, 0));
        long after = Stopwatch.GetTimestamp();

        var buffer = new FrameHistory.Sample[FrameHistory.Capacity];
        int count = history.CopyTo(buffer);

        Assert.Equal(2, count);
        for (int i = 0; i < count; i++)
        {
            Assert.InRange(buffer[i].TimestampTicks, before, after);
        }
        Assert.True(buffer[1].TimestampTicks >= buffer[0].TimestampTicks);
    }
}
