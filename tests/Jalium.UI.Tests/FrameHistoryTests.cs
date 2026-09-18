using Jalium.UI.Diagnostics;
using Xunit.Abstractions;

namespace Jalium.UI.Tests;

public sealed class FrameHistoryTests
{
    private const int AllocationInstanceCount = 128;

    private readonly ITestOutputHelper _output;

    public FrameHistoryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ConstructionAndFirstPush_AllocateFarLessThanEagerFullCapacityBuffers()
    {
        // Warm the constructor, Push path, measurement helper, and Stopwatch timestamp path.
        var warmup = new FrameHistory();
        warmup.Push(CreateSample(-1));
        _ = MeasureAllocatedBytes(static () => GC.KeepAlive(new FrameHistory()));

        long eagerFullBufferBytes = MeasureAllocatedBytes(static () =>
        {
            var buffers = new FrameHistory.Sample[AllocationInstanceCount][];
            for (int i = 0; i < buffers.Length; i++)
            {
                buffers[i] = new FrameHistory.Sample[FrameHistory.Capacity];
            }

            GC.KeepAlive(buffers);
        });

        long constructionBytes = MeasureAllocatedBytes(static () =>
        {
            var histories = new FrameHistory[AllocationInstanceCount];
            for (int i = 0; i < histories.Length; i++)
            {
                histories[i] = new FrameHistory();
            }

            GC.KeepAlive(histories);
        });

        long firstPushBytes = MeasureAllocatedBytes(static () =>
        {
            var histories = new FrameHistory[AllocationInstanceCount];
            var sample = CreateSample(1);
            for (int i = 0; i < histories.Length; i++)
            {
                var history = new FrameHistory();
                history.Push(sample);
                histories[i] = history;
            }

            GC.KeepAlive(histories);
        });

        _output.WriteLine(
            "{0} instances: eager Sample[{1}] payloads={2:N0} B; constructors={3:N0} B; constructors + first Push={4:N0} B",
            AllocationInstanceCount,
            FrameHistory.Capacity,
            eagerFullBufferBytes,
            constructionBytes,
            firstPushBytes);

        Assert.True(
            constructionBytes <= eagerFullBufferBytes / 8,
            $"Constructing {AllocationInstanceCount} FrameHistory instances allocated {constructionBytes:N0} bytes " +
            $"versus {eagerFullBufferBytes:N0} bytes for eager full-capacity payload arrays.");
        Assert.True(
            firstPushBytes <= eagerFullBufferBytes / 4,
            $"Constructing and pushing one sample into {AllocationInstanceCount} FrameHistory instances allocated " +
            $"{firstPushBytes:N0} bytes versus {eagerFullBufferBytes:N0} bytes for eager full-capacity payload arrays.");
    }

    [Fact]
    public void CopyTo_AfterMoreThanTwoWraps_ReturnsOldestToNewestAndShortDestinationGetsOldestSubset()
    {
        var history = new FrameHistory();
        int pushes = (FrameHistory.Capacity * 4) + 37;

        for (int marker = 1; marker <= pushes; marker++)
        {
            history.Push(CreateSample(marker));
        }

        Assert.Equal(FrameHistory.Capacity, history.Count);
        Assert.Equal(pushes, history.TotalFrames);

        var fullSnapshot = new FrameHistory.Sample[FrameHistory.Capacity];
        int copied = history.CopyTo(fullSnapshot);
        Assert.Equal(FrameHistory.Capacity, copied);

        int oldestRetainedMarker = pushes - FrameHistory.Capacity + 1;
        AssertMarkers(fullSnapshot, copied, oldestRetainedMarker);

        var shortSnapshot = new FrameHistory.Sample[11];
        copied = history.CopyTo(shortSnapshot);
        Assert.Equal(shortSnapshot.Length, copied);
        AssertMarkers(shortSnapshot, copied, oldestRetainedMarker);
    }

    [Fact]
    public void Clear_ResetsCountsAndSubsequentPushStartsANewChronologicalHistory()
    {
        var history = new FrameHistory();
        for (int marker = 1; marker <= FrameHistory.Capacity + 19; marker++)
        {
            history.Push(CreateSample(marker));
        }

        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Equal(0, history.TotalFrames);
        Assert.Equal(0, history.CopyTo(new FrameHistory.Sample[FrameHistory.Capacity]));

        history.Push(CreateSample(7_001));
        history.Push(CreateSample(7_002));
        history.Push(CreateSample(7_003));

        var snapshot = new FrameHistory.Sample[FrameHistory.Capacity];
        int copied = history.CopyTo(snapshot);

        Assert.Equal(3, history.Count);
        Assert.Equal(3, history.TotalFrames);
        Assert.Equal(3, copied);
        AssertMarkers(snapshot, copied, 7_001);
    }

    [Fact]
    public void CopyTo_DuringConcurrentPushes_AlwaysReturnsACompleteChronologicalSnapshot()
    {
        const int pushes = 20_000;
        var history = new FrameHistory();
        using var start = new ManualResetEventSlim(false);
        using var firstSnapshotReady = new ManualResetEventSlim(false);
        using var firstSnapshotObserved = new ManualResetEventSlim(false);
        int producerFinished = 0;
        int snapshotsWhileProducerActive = 0;
        Exception? producerFailure = null;
        Exception? consumerFailure = null;

        var producer = new Thread(() =>
        {
            try
            {
                start.Wait();
                for (int marker = 1; marker <= pushes; marker++)
                {
                    history.Push(CreateSample(marker));
                    if (marker == FrameHistory.Capacity + 37)
                    {
                        firstSnapshotReady.Set();
                        if (!firstSnapshotObserved.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("The snapshot thread did not observe the live producer.");
                        }
                    }

                    if ((marker & 31) == 0)
                    {
                        Thread.Yield();
                    }
                }
            }
            catch (Exception ex)
            {
                producerFailure = ex;
            }
            finally
            {
                Volatile.Write(ref producerFinished, 1);
            }
        })
        {
            IsBackground = true,
        };

        var consumer = new Thread(() =>
        {
            try
            {
                start.Wait();
                if (!firstSnapshotReady.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The producer did not publish its first snapshot checkpoint.");
                }

                var snapshot = new FrameHistory.Sample[FrameHistory.Capacity];
                long previousTotalFrames = 0;

                do
                {
                    int copied = history.CopyTo(snapshot);
                    ValidateChronologicalSnapshot(snapshot, copied);

                    int visibleCount = history.Count;
                    if (visibleCount < copied || visibleCount > FrameHistory.Capacity)
                    {
                        throw new InvalidOperationException(
                            $"Count {visibleCount} is inconsistent with a {copied}-sample snapshot.");
                    }

                    long totalFrames = history.TotalFrames;
                    if (totalFrames < previousTotalFrames)
                    {
                        throw new InvalidOperationException(
                            $"TotalFrames regressed from {previousTotalFrames} to {totalFrames}.");
                    }

                    if (copied != 0 && Volatile.Read(ref producerFinished) == 0)
                    {
                        Interlocked.Increment(ref snapshotsWhileProducerActive);
                    }

                    previousTotalFrames = totalFrames;
                    firstSnapshotObserved.Set();
                    Thread.Yield();
                }
                while (Volatile.Read(ref producerFinished) == 0);
            }
            catch (Exception ex)
            {
                consumerFailure = ex;
            }
            finally
            {
                firstSnapshotObserved.Set();
            }
        })
        {
            IsBackground = true,
        };

        producer.Start();
        consumer.Start();
        start.Set();
        Assert.True(producer.Join(TimeSpan.FromSeconds(15)), "Producer thread did not finish.");
        Assert.True(consumer.Join(TimeSpan.FromSeconds(15)), "Snapshot thread did not finish.");
        Assert.Null(producerFailure);
        Assert.Null(consumerFailure);
        Assert.True(snapshotsWhileProducerActive > 0, "No non-empty snapshot overlapped the live producer.");

        var finalSnapshot = new FrameHistory.Sample[FrameHistory.Capacity];
        int finalCount = history.CopyTo(finalSnapshot);
        Assert.Equal(FrameHistory.Capacity, finalCount);
        Assert.Equal(FrameHistory.Capacity, history.Count);
        Assert.Equal(pushes, history.TotalFrames);
        AssertMarkers(finalSnapshot, finalCount, pushes - FrameHistory.Capacity + 1);
    }

    private static FrameHistory.Sample CreateSample(int marker) =>
        new(marker, marker + 0.25, marker + 0.5, marker + 0.75, marker);

    private static long MeasureAllocatedBytes(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void AssertMarkers(FrameHistory.Sample[] samples, int count, int firstMarker)
    {
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(firstMarker + i, samples[i].DirtyElements);
        }
    }

    private static void ValidateChronologicalSnapshot(FrameHistory.Sample[] samples, int count)
    {
        if ((uint)count > FrameHistory.Capacity)
        {
            throw new InvalidOperationException($"Snapshot count {count} exceeds capacity.");
        }

        for (int i = 0; i < count; i++)
        {
            int marker = samples[i].DirtyElements;
            if (marker <= 0)
            {
                throw new InvalidOperationException($"Snapshot contains an unwritten sample at index {i}.");
            }

            if (i > 0 && marker != samples[i - 1].DirtyElements + 1)
            {
                throw new InvalidOperationException(
                    $"Snapshot is not chronological at index {i}: {samples[i - 1].DirtyElements}, {marker}.");
            }

            if (i > 0 && samples[i].TimestampTicks < samples[i - 1].TimestampTicks)
            {
                throw new InvalidOperationException(
                    $"Snapshot timestamp regressed at index {i}: " +
                    $"{samples[i - 1].TimestampTicks}, {samples[i].TimestampTicks}.");
            }
        }
    }
}
