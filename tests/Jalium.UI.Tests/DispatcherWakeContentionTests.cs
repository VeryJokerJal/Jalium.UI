using System.Reflection;
using System.Runtime.ExceptionServices;
using Jalium.UI.Threading;
using UiDispatcher = Jalium.UI.Threading.Dispatcher;

namespace Jalium.UI.Tests;

public sealed class DispatcherWakeContentionTests
{
    [Fact]
    public void PendingWindowsWakeDoesNotBlockQueuePublicationOrProcessing()
    {
        if (!OperatingSystem.IsWindows()) return;
        OnOwnerThread(dispatcher =>
        {
            int ran = 0;
            dispatcher.BeginInvoke(() => ran++);
            WithWakeBlocked(dispatcher, () =>
            {
                UiDispatcherOperationPost(dispatcher, () => ran++);
                dispatcher.ProcessQueue();
                Assert.Equal(2, ran);
            });
        });
    }

    [Fact]
    public void AnotherProducerCanPublishWhileTheFirstWindowsWakeIsPending()
    {
        if (!OperatingSystem.IsWindows()) return;
        OnOwnerThread(dispatcher =>
        {
            int ran = 0;
            WithWakeBlocked(dispatcher, () =>
            {
                Exception? firstFailure = null;
                var first = new Thread(() =>
                {
                    try { dispatcher.BeginInvoke(() => ran++); }
                    catch (Exception ex) { firstFailure = ex; }
                }) { IsBackground = true };
                first.Start();
                try
                {
                    var latch = dispatcher.CoreDispatcher.GetType().GetField(
                        "_wakeInFlight", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    Assert.True(SpinWait.SpinUntil(
                        () => (int)latch.GetValue(dispatcher.CoreDispatcher)! == 1,
                        TimeSpan.FromSeconds(1)), "First producer did not publish its wake claim.");
                    UiDispatcherOperationPost(dispatcher, () => ran++);
                    dispatcher.ProcessQueue();
                    Assert.Equal(2, ran);
                }
                finally
                {
                    // WithWakeBlocked releases the gate after this action returns.
                    // Keep the worker for the outer cleanup, without waiting on its wake here.
                    PendingWorkers.Add((first, () => firstFailure, null));
                }
            });
        });
    }

    [Fact]
    public void ShutdownClosesTheQueueBeforeWaitingForWindowsWakeResources()
    {
        if (!OperatingSystem.IsWindows()) return;
        OnOwnerThread(dispatcher =>
        {
            var core = dispatcher.CoreDispatcher;
            var gate = core.GetType().GetField("_windowsWakeLock", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? core.GetType().GetField("_instanceLock", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var held = new ManualResetEventSlim();
            Exception? failure = null;
            var holder = new Thread(() =>
            {
                try
                {
                    lock (gate.GetValue(core)!)
                    {
                        held.Set();
                        Assert.True(SpinWait.SpinUntil(() => dispatcher.HasShutdownStarted,
                            TimeSpan.FromSeconds(1)), "Shutdown held the queue gate while waiting for wake resources.");
                        var late = dispatcher.BeginInvoke(() => throw new InvalidOperationException("Late work ran."));
                        Assert.Equal(DispatcherOperationStatus.Aborted, late.Status);
                    }
                }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            holder.Start();
            Assert.True(held.Wait(TimeSpan.FromSeconds(5)));
            dispatcher.DisposeCore();
            Assert.True(holder.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.True(dispatcher.HasShutdownFinished);
        });
    }

    [Fact]
    public void ConcurrentProducersAreDrainedExactlyOnceInProducerOrderByTheWindowsPump()
    {
        if (!OperatingSystem.IsWindows()) return;
        OnOwnerThread(dispatcher =>
        {
            const int producers = 4, each = 128;
            var counts = new int[producers * each];
            var order = Enumerable.Range(0, producers).Select(_ => new List<int>()).ToArray();
            var frame = new DispatcherFrame();
            var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            int finished = 0;
            var threads = Enumerable.Range(0, producers).Select(producer => new Thread(() =>
            {
                try
                {
                    for (int index = 0; index < each; index++)
                    {
                        int item = index;
                        dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                        {
                            counts[producer * each + item]++;
                            order[producer].Add(item);
                        });
                    }
                }
                catch (Exception ex) { failures.Enqueue(ex); }
                finally
                {
                    if (Interlocked.Increment(ref finished) == producers)
                        dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);
                }
            }) { IsBackground = true }).ToArray();
            using var timeout = new System.Threading.Timer(_ => dispatcher.BeginInvoke(
                    DispatcherPriority.Send, () => frame.Continue = false),
                null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
            foreach (var thread in threads) thread.Start();
            UiDispatcher.PushFrame(frame);
            foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.Empty(failures);
            Assert.All(counts, count => Assert.Equal(1, count));
            Assert.All(order, sequence => Assert.Equal(Enumerable.Range(0, each), sequence));
        });
    }

    [ThreadStatic] private static List<(Thread Thread, Func<Exception?> Failure, IDisposable? Cleanup)>? s_pendingWorkers;
    private static List<(Thread Thread, Func<Exception?> Failure, IDisposable? Cleanup)> PendingWorkers => s_pendingWorkers ??= [];

    private static void UiDispatcherOperationPost(UiDispatcher dispatcher, Action callback)
    {
        var posted = new ManualResetEventSlim();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { dispatcher.BeginInvoke(callback); }
            catch (Exception ex) { failure = ex; }
            finally { posted.Set(); }
        }) { IsBackground = true };
        worker.Start();
        PendingWorkers.Add((worker, () => failure, posted));
        Assert.True(posted.Wait(TimeSpan.FromSeconds(1)),
            "Publishing coalesced work waited for the blocked Windows wake.");
        Assert.Null(failure);
    }

    private static void WithWakeBlocked(UiDispatcher dispatcher, Action action)
    {
        var core = dispatcher.CoreDispatcher;
        // The previous dispatcher used its queue gate for wake interop too.
        var gate = core.GetType().GetField("_windowsWakeLock", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? core.GetType().GetField("_instanceLock", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            lock (gate.GetValue(core)!)
            {
                held.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
        }) { IsBackground = true };
        holder.Start();
        try
        {
            Assert.True(held.Wait(TimeSpan.FromSeconds(5)));
            action();
        }
        finally
        {
            release.Set();
            Assert.True(holder.Join(TimeSpan.FromSeconds(5)));
            foreach (var pending in PendingWorkers)
            {
                Assert.True(pending.Thread.Join(TimeSpan.FromSeconds(5)));
                pending.Cleanup?.Dispose();
                Assert.Null(pending.Failure());
            }
            PendingWorkers.Clear();
        }
    }

    private static void OnOwnerThread(Action<UiDispatcher> action)
    {
        Exception? failure = null;
        var owner = new Thread(() =>
        {
            var dispatcher = UiDispatcher.CurrentDispatcher;
            try { action(dispatcher); }
            catch (Exception ex) { failure = ex; }
            finally { dispatcher.DisposeCore(); }
        }) { IsBackground = true };
        owner.Start();
        Assert.True(owner.Join(TimeSpan.FromSeconds(15)), "Dispatcher owner timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
