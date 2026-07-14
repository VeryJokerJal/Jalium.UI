using System.Runtime.InteropServices;
using Jalium.UI;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers the input-first top-level Windows pump (<c>Dispatcher.RunMainMessageLoop</c>)
/// that <c>Application.Run</c> routes through. These run a real Win32 message pump on a
/// dedicated thread (message-only window + posted messages), so they are serialized
/// with the other Application/dispatcher tests and bounded by a hard timeout — a pump
/// bug fails the test instead of hanging the run.
/// </summary>
[Collection("Application")]
public partial class DispatcherMainLoopTests
{
    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // Runs body on a dedicated background thread; fails (never hangs) if it does not
    // return within the timeout. Background so a hung pump cannot block process exit.
    private static void RunOnPumpThread(Action body, int timeoutMs = 5000)
    {
        Exception? captured = null;
        var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { captured = e; }
            finally { done.Set(); }
        })
        { IsBackground = true, Name = "PumpTest" };

        thread.Start();
        Assert.True(done.Wait(timeoutMs), "RunMainMessageLoop did not return within the timeout (possible hang).");
        if (captured != null) throw captured;
    }

    [Fact]
    public void RunMainMessageLoop_ConsumesWmQuit_AndReturnsExitCode()
    {
        if (!IsWindows) return; // Windows-only pump

        int result = -1;
        RunOnPumpThread(() =>
        {
            var dispatcher = Dispatcher.GetForCurrentThread();
            PostQuitMessage(42);                        // pre-arm quit on this thread's queue
            result = dispatcher.RunMainMessageLoop();   // must consume WM_QUIT and return its code
        });

        Assert.Equal(42, result);
    }

    [Fact]
    public void RunMainMessageLoop_ProcessesQueuedWork_ThenExitsOnQuit()
    {
        if (!IsWindows) return;

        bool ran = false;
        int result = -1;
        RunOnPumpThread(() =>
        {
            var dispatcher = Dispatcher.GetForCurrentThread();
            // Enqueued BEFORE the loop starts: the input-first pump must pick this up
            // (via the work-available event, step 2 ProcessQueue) and run it; the op
            // then posts WM_QUIT so the loop unwinds with a known exit code.
            dispatcher.BeginInvoke(() =>
            {
                ran = true;
                PostQuitMessage(7);
            });
            result = dispatcher.RunMainMessageLoop();
        });

        Assert.True(ran, "queued dispatcher work was not processed by the main loop");
        Assert.Equal(7, result);
    }

    [Fact]
    public void RunModalLoop_ExitsWhenPredicateBecomesFalse()
    {
        if (!IsWindows) return;

        RunOnPumpThread(() =>
        {
            var dispatcher = Dispatcher.GetForCurrentThread();
            bool keepRunning = true;
            // Enqueued before the modal loop starts: the input-first pump processes it,
            // flipping the predicate, and the loop must then exit (not block at idle).
            dispatcher.BeginInvoke(() => keepRunning = false);
            dispatcher.RunModalLoop(() => keepRunning);
        });
    }

    [Fact]
    public void PushFrame_WhileProcessingDisabled_ThrowsInsteadOfHanging()
    {
        // Pumping a nested frame inside a DisableProcessing region can never make
        // progress (ProcessQueue is suspended); the guard must surface this as an
        // immediate exception rather than a silent hang. RunOnPumpThread's timeout
        // is the backstop if the guard ever regresses.
        RunOnPumpThread(() =>
        {
            var dispatcher = Dispatcher.GetForCurrentThread();
            using (dispatcher.DisableProcessing())
            {
                Assert.Throws<InvalidOperationException>(Dispatcher.Run);
            }
        });
    }
}
