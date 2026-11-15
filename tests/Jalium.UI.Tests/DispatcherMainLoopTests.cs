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

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out NativeMessage message, nint hwnd, uint first, uint last, uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetQueueStatus(uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SendMessageTimeoutW(nint hwnd, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, out nuint result);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

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

    [Theory]
    [InlineData(0x0101u)] // WM_KEYUP
    [InlineData(0x0109u)] // WM_UNICHAR
    [InlineData(0x0200u)] // WM_MOUSEMOVE
    [InlineData(0x020Eu)] // WM_MOUSEHWHEEL
    public void RunMainMessageLoop_DrainsPostedClientInputBeforeManagedWork(uint message)
    {
        if (!IsWindows) return;

        RunOnPumpThread(() =>
        {
            var dispatcher = Dispatcher.GetForCurrentThread();
            try
            {
                // A thread message has no HWND, so dispatch cannot synthesize
                // physical input or enter a control's pointer-data parser.
                Assert.True(PostThreadMessageW(GetCurrentThreadId(), message, 0, 0),
                    $"Could not post message 0x{message:X}: Win32 {Marshal.GetLastPInvokeError()}.");
                bool? stillQueued = null;
                dispatcher.BeginInvoke(() =>
                {
                    stillQueued = PeekMessageW(out _, 0, message, message, 0);
                    PostQuitMessage(0);
                });
                Assert.Equal(0, dispatcher.RunMainMessageLoop());
                Assert.False(stillQueued ?? true, "Posted client input must be drained before managed work.");
            }
            finally { dispatcher.DisposeCore(); }
        });
    }

    [Fact]
    public void RunMainMessageLoop_ServicesPendingSentMessagesBeforeManagedWork()
    {
        if (!IsWindows) return;

        RunOnPumpThread(() =>
        {
            const uint sentMessages = 0x0040;
            var dispatcher = Dispatcher.GetForCurrentThread();
            using var window = new HiddenNativeWindow(1, 1);
            nint sendResult = 0;
            var sender = new Thread(() => sendResult = SendMessageTimeoutW(
                window.Hwnd, 0, 0, 0, 0x0002, 2000, out _)) { IsBackground = true };
            try
            {
                sender.Start();
                Assert.True(SpinWait.SpinUntil(
                    () => ((GetQueueStatus(sentMessages) >> 16) & sentMessages) != 0,
                    1000), "The worker's sent message was not observed in the native queue.");
                bool? stillPending = null;
                dispatcher.BeginInvoke(() =>
                {
                    stillPending = ((GetQueueStatus(sentMessages) >> 16) & sentMessages) != 0;
                    PostQuitMessage(0);
                });
                Assert.Equal(0, dispatcher.RunMainMessageLoop());
                Assert.False(stillPending ?? true, "Pending sent messages must be serviced before managed work.");
                Assert.True(sender.Join(2500));
                Assert.NotEqual(0, sendResult);
            }
            finally
            {
                // Bound the sender before destroying its target on this thread.
                if (sender.IsAlive) sender.Join(2500);
                dispatcher.DisposeCore();
            }
        });
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
