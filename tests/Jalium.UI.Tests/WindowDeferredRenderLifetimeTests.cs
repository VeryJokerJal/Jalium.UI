using System.Reflection;
using Jalium.UI.Interop;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowDeferredRenderLifetimeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void ReplacingAnAlreadyQueuedRetry_AbortsOldOperation_AndRejectsItsTimer()
    {
        var window = CreateWindow();
        try
        {
            Arm(window);
            long oldGeneration = Read<long>(window, "_deferredRenderGeneration");
            Invoke(window, "OnDeferredRenderTimer", oldGeneration);
            var oldOperation = Assert.IsType<DispatcherOperation>(Read<object>(window, "_deferredRenderOperation"));
            Assert.Equal(DispatcherOperationStatus.Pending, oldOperation.Status);

            Arm(window);
            Assert.Equal(DispatcherOperationStatus.Aborted, oldOperation.Status);
            Assert.Null(Read<object?>(window, "_deferredRenderOperation"));
            Assert.False(IsScheduled(window));

            // Model a thread-pool callback that started before Timer.Dispose.
            Invoke(window, "OnDeferredRenderTimer", oldGeneration);
            Assert.Null(Read<object?>(window, "_deferredRenderOperation"));
            Assert.False(IsScheduled(window));

            Invoke(window, "OnDeferredRenderTimer", Read<long>(window, "_deferredRenderGeneration"));
            Assert.NotNull(Read<object?>(window, "_deferredRenderOperation"));
            Assert.False(IsScheduled(window));
        }
        finally { window.Close(); }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Close_CancelsQueuedRetry_AndStaleCallbacksCannotPresent()
    {
        var window = CreateWindow();
        try
        {
            Arm(window);
            long generation = Read<long>(window, "_deferredRenderGeneration");
            Invoke(window, "OnDeferredRenderTimer", generation);
            var operation = Assert.IsType<DispatcherOperation>(Read<object>(window, "_deferredRenderOperation"));
            long frames = window.FrameHistory.TotalFrames;
            window.Close();

            Assert.Equal(DispatcherOperationStatus.Aborted, operation.Status);
            Assert.Null(Read<object?>(window, "_renderThrottleTimer"));
            Assert.Null(Read<object?>(window, "_deferredRenderOperation"));
            Invoke(window, "OnDeferredRenderTimer", generation);
            Invoke(window, "ProcessDeferredRender", generation);
            Invoke(window, "OnEmptyStorageTimer");
            Assert.False(IsScheduled(window));
            Assert.Equal(frames, window.FrameHistory.TotalFrames);
            Assert.Equal(0, WindowInputLifecycleTests.CountFrameSubscriptions(window));
        }
        finally { window.Close(); }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void ReentrantReplacementDuringPost_DoesNotOverwriteTheLatestRequest()
    {
        var window = CreateWindow();
        int ownerThread = Environment.CurrentManagedThreadId;
        DispatcherOperation? obsolete = null;
        bool replaced = false;
        DispatcherHookEventHandler handler = (_, e) =>
        {
            if (replaced || Environment.CurrentManagedThreadId != ownerThread) return;
            replaced = true;
            obsolete = e.Operation;
            Arm(window);
        };
        try
        {
            Arm(window);
            long generation = Read<long>(window, "_deferredRenderGeneration");
            window.Dispatcher.Hooks.OperationPosted += handler;
            Invoke(window, "OnDeferredRenderTimer", generation);
            window.Dispatcher.Hooks.OperationPosted -= handler;

            Assert.True(replaced);
            Assert.NotNull(obsolete);
            Assert.Equal(DispatcherOperationStatus.Aborted, obsolete.Status);
            Assert.True(Read<long>(window, "_deferredRenderGeneration") > generation);
            Assert.NotNull(Read<object?>(window, "_renderThrottleTimer"));
            Assert.Null(Read<object?>(window, "_deferredRenderOperation"));
            Assert.False(IsScheduled(window));

            Invoke(window, "OnDeferredRenderTimer", Read<long>(window, "_deferredRenderGeneration"));
            Assert.False(IsScheduled(window));
            Assert.NotNull(Read<object?>(window, "_deferredRenderOperation"));
        }
        finally
        {
            window.Dispatcher.Hooks.OperationPosted -= handler;
            window.Close();
        }
    }

    private static Window CreateWindow()
    {
        var window = new Window { Width = 240, Height = 160, ShowActivated = false };
        window.Show();
        // Satisfy startup work synchronously before testing timer ownership.
        Invoke(window, "ProcessRender");
        return window;
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void AbortedRetry_PreservesDirtyWorkForTheFrameClock()
    {
        var window = CreateWindow();
        try
        {
            window.RequestFullInvalidation();
            Arm(window);
            Invoke(window, "OnDeferredRenderTimer", Read<long>(window, "_deferredRenderGeneration"));
            var operation = Assert.IsType<DispatcherOperation>(Read<object>(window, "_deferredRenderOperation"));
            Assert.True(operation.Abort());
            Assert.Null(Read<object?>(window, "_deferredRenderOperation"));
            Assert.False(IsScheduled(window));
            Assert.True((Read<int>(window, "_renderState") & (1 << 3)) != 0);
            Assert.True(Read<bool>(window, "_fullInvalidation"));
        }
        finally { window.Close(); }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void CancelledCloseWithNestedProcessing_DoesNotLeaveScheduledFlagLatched()
    {
        var window = CreateWindow();
        EventHandler<System.ComponentModel.CancelEventArgs> cancel = (_, e) =>
        {
            // An application Closing handler may pump dispatcher work before it
            // decides to cancel. That work cannot render while Close is tentative.
            Invoke(window, "ProcessRender");
            e.Cancel = true;
        };
        try
        {
            window.RequestFullInvalidation();
            window.InvalidateWindow();
            Invoke(window, "OnFrameStarting");
            Assert.True(IsScheduled(window));
            window.Closing += cancel;
            window.Close();
            Assert.False(IsScheduled(window));
            Assert.True((Read<int>(window, "_renderState") & (1 << 3)) != 0);
            Invoke(window, "OnFrameStarting");
            Assert.True(IsScheduled(window));
        }
        finally
        {
            window.Closing -= cancel;
            window.Close();
        }
    }

    private static bool IsScheduled(Window window) => (Read<int>(window, "_renderState") & 1) != 0;
    private static void Arm(Window window) => Invoke(window, "ScheduleDeferredRender", 60_000);
    private static T Read<T>(Window window, string name) => (T)typeof(Window)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Invoke(Window window, string name, params object[] arguments) => typeof(Window)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
}
