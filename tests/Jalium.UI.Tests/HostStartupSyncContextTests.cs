using Jalium.UI;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

/// <summary>
/// Reproduces the async-hosted-work startup deadlock that hangs apps whose hosted service /
/// <c>BackgroundService</c> does real asynchronous work. <see cref="JaliumApp"/> starts the host by
/// blocking on <c>StartAsync</c> while a <see cref="DispatcherSynchronizationContext"/> is current
/// and the Win32 message pump has not started, so any continuation captured on the UI thread is
/// queued to a dispatcher that never drains. <see cref="JaliumApp.RunHostOperationBlocking"/> must
/// suspend that context for the blocking wait so the continuations resume on the thread pool.
/// </summary>
public class HostStartupSyncContextTests
{
    [Fact]
    public void SyncOverAsync_BlockingOnUiThread_WithCapturedContinuation_DoesNotDeadlock()
    {
        // Reproduce JaliumApp.Run's host-start state on a dedicated worker thread: a
        // DispatcherSynchronizationContext is Current and there is NO running message pump. The
        // awaited work captures the context two different ways — Task.Yield posts on this thread,
        // Task.Delay's timer posts from a pool thread — and both must resume off the (idle) pump.
        Exception? workerError = null;
        SynchronizationContext? installedContext = null;
        SynchronizationContext? restoredContext = null;
        var completed = new ManualResetEventSlim(false);

        var worker = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.GetForCurrentThread();
                installedContext = new DispatcherSynchronizationContext(dispatcher);
                SynchronizationContext.SetSynchronizationContext(installedContext);

                JaliumApp.RunHostOperationBlocking(async () =>
                {
                    await Task.Yield();
                    await Task.Delay(20);
                });

                restoredContext = SynchronizationContext.Current;
                completed.Set();
            }
            catch (Exception ex)
            {
                workerError = ex;
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "HostStartupSyncContextTests.Worker",
        };

        worker.Start();

        // Without the context suspension the worker blocks forever in GetResult(); a finite wait
        // turns that deadlock into a deterministic failure instead of hanging the whole test run.
        bool finished = completed.Wait(TimeSpan.FromSeconds(10));

        Assert.True(finished,
            "RunHostOperationBlocking deadlocked: sync-over-async on the UI thread with a captured " +
            "continuation did not complete (the UI SynchronizationContext was not suspended).");
        Assert.Null(workerError);

        // The prior context is restored after the blocking call (finally block), not left null.
        Assert.Same(installedContext, restoredContext);
    }
}
