using System.Reflection;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Rendering;
using Jalium.UI.Tests.RenderThread;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowRenderCaptureCoalescingTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void HiddenPhaseWorkerKeepsDirtyStateWithoutSchedulingCapture()
    {
        using var setup = new CaptureWindow();
        Write(setup.Window, "_rtPhaseCaptureEnabled", true);
        setup.Window.ForceRenderFrame();
        var first = Read(setup.Window, "_rtPendingFrame")!;
        Invoke(setup.Window, "CompleteRenderThreadCapture", first);
        Invoke(setup.Window, "ClearPendingRenderThreadCapture");
        Write(setup.Window, "_nativeWindowHidden", true);
        setup.Content.Value = 1000;
        setup.Content.InvalidateVisual();
        Invoke(setup.Window, "OnFrameStarting");
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.Null(Read(setup.Window, "_rtPendingFrame"));
        Assert.Equal(1L, Read(setup.Window, "_rtRecordedFrameCount"));
        Assert.NotEqual(0, (int)Read(setup.Window, "_renderState")! & 8);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void CaptureQueuedBeforeHideRemainsDirtyAndRecordsOnRestore()
    {
        using var setup = new CaptureWindow();
        Write(setup.Window, "_rtPhaseCaptureEnabled", true);
        setup.Window.ForceRenderFrame();
        var first = Read(setup.Window, "_rtPendingFrame")!;
        Invoke(setup.Window, "CompleteRenderThreadCapture", first);
        Invoke(setup.Window, "ClearPendingRenderThreadCapture");
        setup.Content.Value = 1000;
        setup.Content.InvalidateVisual();
        Write(setup.Window, "_nativeWindowHidden", true);
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.Null(Read(setup.Window, "_rtPendingFrame"));
        Assert.Equal(1L, Read(setup.Window, "_rtRecordedFrameCount"));
        Write(setup.Window, "_nativeWindowHidden", false);
        setup.Window.InvalidateWindow();
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.Equal(2L, Read(setup.Window, "_rtRecordedFrameCount"));
        var latest = Read(setup.Window, "_rtPendingFrame")!;
        var sink = new RecordingRenderSink();
        DrawingReplayer.Replay((RecordedDrawing)latest.GetType().GetField("Drawing")!.GetValue(latest)!, sink);
        Assert.Contains(sink.Events, e => e.StartsWith("DrawRectangle") && e.Contains("#FF0000FF"));
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void PhaseTimerTimeoutRestoresImmediateCompletionAndDisposesTimer()
        => VerifyPhaseTimerFailure(throws: false);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void PhaseTimerExceptionRestoresImmediateCompletionAndDisposesTimer()
        => VerifyPhaseTimerFailure(throws: true);

    private static void VerifyPhaseTimerFailure(bool throws)
    {
        using var setup = new CaptureWindow();
        Write(setup.Window, "_rtPhaseCaptureEnabled", true);
        setup.Window.ForceRenderFrame();
        var cap = Read(setup.Window, "_rtPendingFrame")!;
        var type = cap.GetType();
        type.GetField("ReplayStartedTimestampTicks")!.SetValue(cap, System.Diagnostics.Stopwatch.GetTimestamp());
        type.GetField("DeferCaptureCompletion")!.SetValue(cap, true);
        type.GetField("PresentedSuccessfully")!.SetValue(cap, true);
        var timing = new RenderCaptureTiming();
        timing.Observe(1000, 1000, 1_000_000, 240);
        Write(setup.Window, "_rtCaptureTiming", timing);
        using var timer = new DeadlineTestTimer(() => { if (throws) throw new InvalidOperationException("Timer failure"); }, fire: false);
        Write(setup.Window, "_rtCaptureTimer", timer);
        Invoke(setup.Window, "WaitForRenderCaptureDeadline", cap);
        Assert.True(timer.Disposed);
        Assert.Null(Read(setup.Window, "_rtCaptureTimer"));
        Assert.Equal(false, Read(setup.Window, "_rtPhaseCaptureEnabled"));
        Invoke(setup.Window, "CompleteRenderThreadCapture", cap);
        long ticket = (long)type.GetField("SchedulerTicket")!.GetValue(cap)!;
        Assert.True(((RenderCaptureScheduler)Read(setup.Window, "_rtCaptureScheduler")!).IsAwaitingCapture(ticket));
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void PhaseInvalidationNotifiesHeldCaptureWithoutWaitingForAClockTick()
    {
        using var setup = new CaptureWindow();
        Write(setup.Window, "_rtPhaseCaptureEnabled", true);
        setup.Window.ForceRenderFrame();
        var first = Read(setup.Window, "_rtPendingFrame")!;
        long ticket = (long)first.GetType().GetField("SchedulerTicket")!.GetValue(first)!;
        setup.Content.Value = 1000;
        setup.Content.InvalidateVisual();
        Assert.True(((RenderCaptureScheduler)Read(setup.Window, "_rtCaptureScheduler")!).HasPendingCapture(ticket));
        Assert.Equal(1L, Read(setup.Window, "_rtRecordedFrameCount"));
        Invoke(setup.Window, "CompleteRenderThreadCapture", first);
        Dispatcher.GetForCurrentThread().ProcessQueue();
        var latest = Read(setup.Window, "_rtPendingFrame")!;
        Assert.NotSame(first, latest);
        Assert.Equal(2L, Read(setup.Window, "_rtRecordedFrameCount"));
        var sink = new RecordingRenderSink();
        DrawingReplayer.Replay((RecordedDrawing)latest.GetType().GetField("Drawing")!.GetValue(latest)!, sink);
        Assert.Contains(sink.Events, e => e.StartsWith("DrawRectangle") && e.Contains("#FF0000FF"));
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void PhaseInvalidationOnAFreeSlotQueuesCaptureAfterTheInputCallback()
    {
        using var setup = new CaptureWindow();
        Write(setup.Window, "_rtPhaseCaptureEnabled", true);
        setup.Window.ForceRenderFrame();
        var first = Read(setup.Window, "_rtPendingFrame")!;
        Invoke(setup.Window, "CompleteRenderThreadCapture", first);
        Invoke(setup.Window, "ClearPendingRenderThreadCapture");
        setup.Content.Value = 1000;
        setup.Content.InvalidateVisual();
        Assert.Null(Read(setup.Window, "_rtPendingFrame"));
        Assert.Equal(1L, Read(setup.Window, "_rtRecordedFrameCount"));
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.NotNull(Read(setup.Window, "_rtPendingFrame"));
        Assert.Equal(2L, Read(setup.Window, "_rtRecordedFrameCount"));
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void PhaseDeadlineHoldsTheSlotEvenBeforeAnotherRequestArrives()
    {
        using var setup = new CaptureWindow();
        Write(setup.Window, "_rtPhaseCaptureEnabled", true);
        setup.Window.ForceRenderFrame();
        var cap = Read(setup.Window, "_rtPendingFrame")!;
        var type = cap.GetType();
        long ticket = (long)type.GetField("SchedulerTicket")!.GetValue(cap)!;
        Assert.False(((RenderCaptureScheduler)Read(setup.Window, "_rtCaptureScheduler")!).HasPendingCapture(ticket));
        type.GetField("ReplayStartedTimestampTicks")!.SetValue(cap, System.Diagnostics.Stopwatch.GetTimestamp());
        type.GetField("DeferCaptureCompletion")!.SetValue(cap, true);
        type.GetField("PresentedSuccessfully")!.SetValue(cap, true);
        var timing = new RenderCaptureTiming();
        timing.Observe(1000, 1000, 1_000_000, 240);
        Write(setup.Window, "_rtCaptureTiming", timing);
        using var timer = new DeadlineTestTimer(() =>
            type.GetField("ReplayStartedTimestampTicks")!.SetValue(cap, System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency));
        Write(setup.Window, "_rtCaptureTimer", timer);
        try
        {
            Invoke(setup.Window, "WaitForRenderCaptureDeadline", cap);
            Assert.Equal(1, timer.Arms);
            Assert.False(((RenderCaptureScheduler)Read(setup.Window, "_rtCaptureScheduler")!).HasPendingCapture(ticket));
        }
        finally { Write(setup.Window, "_rtCaptureTimer", null); }
    }

    private sealed class DeadlineTestTimer(Action onWait, bool fire = true) : Jalium.UI.Core.Platform.IFrameTimer
    {
        internal int Arms;
        internal bool Disposed;
        public void Arm(long intervalMicroseconds) { Assert.True(intervalMicroseconds > 0); Arms++; }
        public bool Wait(uint timeoutMs = 0) { onWait(); return fire; }
        public void ArmRepeating(long intervalMicroseconds) => throw new NotSupportedException();
        public void Disarm() { }
        public void Dispose() { Disposed = true; }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void ReplayOverlapsOneNewCaptureWithoutReleasingTheWorkingDrawing()
        => VerifyOverlappingReplay(throwDuringReplay: false);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void ReplayFailureDoesNotCompleteOrReleaseTheNewerQueuedCapture()
        => VerifyOverlappingReplay(throwDuringReplay: true);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void HeavyPhaseFramesPreserveOverlappingRecordingAndDrawingLifetime()
        => VerifyOverlappingReplay(throwDuringReplay: false, phaseHeavy: true);

    private static void VerifyOverlappingReplay(bool throwDuringReplay, bool phaseHeavy = false)
    {
        using var setup = new CaptureWindow(realTarget: true);
        if (phaseHeavy)
        {
            Write(setup.Window, "_rtPhaseCaptureEnabled", true);
            var timing = new RenderCaptureTiming();
            timing.Observe(8000, 8000, 1_000_000, 240);
            Write(setup.Window, "_rtCaptureTiming", timing);
        }
        var previousHost = Visual.RenderCacheHost;
        var host = new TrackingRenderCacheHost();
        Visual.RenderCacheHost = host;
        RecordedDrawing? workingDrawing = null;
        try
        {
            setup.Window.ForceRenderFrame();
            var working = Read(setup.Window, "_rtPendingFrame")!;
            workingDrawing = (RecordedDrawing)working.GetType().GetField("Drawing")!.GetValue(working)!;
            Write(setup.Window, "_rtPendingFrame", null);
            setup.Content.Value = 1000;
            setup.Content.InvalidateVisual();
            setup.Window.ForceRenderFrame();
            Assert.Equal(1L, Read(setup.Window, "_rtRecordedFrameCount"));

            object? queued = null;
            host.ReplayFrame = _ =>
            {
                Assert.False(workingDrawing.IsReleased);
                Assert.True(setup.Target.IsDrawing);
                Assert.Equal(0, setup.Window.FrameHistory.TotalFrames);
                Dispatcher.GetForCurrentThread().ProcessQueue();
                queued = Read(setup.Window, "_rtPendingFrame");
                Assert.NotNull(queued);
                Assert.NotSame(working, queued);
                Assert.Equal(2L, Read(setup.Window, "_rtRecordedFrameCount"));
                for (int i = 0; i < 100; i++)
                {
                    setup.Content.Value++;
                    setup.Content.InvalidateVisual();
                    setup.Window.ForceRenderFrame();
                }
                Assert.Same(queued, Read(setup.Window, "_rtPendingFrame"));
                Assert.Equal(2L, Read(setup.Window, "_rtRecordedFrameCount"));
                if (throwDuringReplay) throw new InvalidOperationException("Replay interrupted");
            };
            if (throwDuringReplay)
            {
                var error = Assert.Throws<TargetInvocationException>(
                    () => Invoke(setup.Window, "PresentCaptureOnRenderThreadCore", working));
                Assert.IsType<InvalidOperationException>(error.InnerException);
            }
            else Invoke(setup.Window, "PresentCaptureOnRenderThreadCore", working);
            Assert.NotNull(queued);
            Invoke(setup.Window, "CompleteRenderThreadCapture", working);
            Assert.Same(queued, Read(setup.Window, "_rtPendingFrame"));
            Assert.False(workingDrawing.IsReleased);
            var queuedDrawing = (RecordedDrawing)queued!.GetType().GetField("Drawing")!.GetValue(queued)!;
            Assert.False(queuedDrawing.IsReleased);
        }
        finally
        {
            workingDrawing?.Release();
            Visual.RenderCacheHost = previousHost;
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void StalledSupersededCaptureRefreshesButQuietStaticAndFreshCapturesRemainUsable()
    {
        using var setup = new CaptureWindow();
        setup.Window.ForceRenderFrame();
        var capture = Read(setup.Window, "_rtPendingFrame")!;
        var timestamp = capture.GetType().GetField("CaptureTimestampTicks")!;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        timestamp.SetValue(capture, now - System.Diagnostics.Stopwatch.Frequency);
        Assert.False(ShouldRefresh(capture)); // old, but still the current static scene
        setup.Content.Value = 1000;
        setup.Content.InvalidateVisual();
        setup.Window.ForceRenderFrame();
        Assert.True(ShouldRefresh(capture));
        timestamp.SetValue(capture, now + System.Diagnostics.Stopwatch.Frequency);
        Assert.False(ShouldRefresh(capture));
        timestamp.SetValue(capture, 0L);
        Assert.False(ShouldRefresh(capture));
        capture.GetType().GetField("Requeued")!.SetValue(capture, true);
        Assert.True(ShouldRefresh(capture));

        static bool ShouldRefresh(object capture) => (bool)typeof(Window)
            .GetMethod("ShouldRefreshRenderThreadCapture", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [capture])!;
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void BusyWorkerKeepsAllInputStateAndRecordsLatestValueOnCompletion()
    {
        MediaRenderCacheHost.Bootstrap();
        using var context = new RenderContext(RenderBackend.Software);
        var native = new RenderTargetTestNative();
        using var target = new RenderTarget(context.Backend, context.Handle,
            NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x6191)), 96, 64,
            useComposition: false, native: native, ownerContext: context);
        var content = new StateVisual { Width = 30, Height = 20 };
        var window = new Window { Width = 96, Height = 64, Content = content };
        var scheduler = new RenderCaptureScheduler();
        typeof(Window).GetProperty("RenderTarget", BindingFlags.Instance | BindingFlags.Public)!.SetValue(window, target);
        Write(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        Write(window, "_rtCaptureScheduler", scheduler);
        Write(window, "_renderThread", new Thread(static () => { }));
        Write(window, "_rtActive", true);
        Write(window, "<Handle>k__BackingField", new nint(0x6191));
        try
        {
            window.ForceRenderFrame();
            var first = Read(window, "_rtPendingFrame");
            Assert.NotNull(first);
            Assert.Equal(1L, Read(window, "_rtRecordedFrameCount"));

            for (int i = 1; i <= 1000; i++)
            {
                content.Value = i;
                content.InvalidateVisual();
                window.ForceRenderFrame();
            }
            Assert.Equal(1000, content.Value);
            Assert.Equal(1L, Read(window, "_rtRecordedFrameCount"));

            Write(window, "_rtPendingFrame", null);
            typeof(Window).GetMethod("CompleteRenderThreadCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [first]);
            Dispatcher.GetForCurrentThread().ProcessQueue();

            Assert.Equal(2L, Read(window, "_rtRecordedFrameCount"));
            var latest = Read(window, "_rtPendingFrame")!;
            var drawing = (RecordedDrawing)latest.GetType().GetField("Drawing")!.GetValue(latest)!;
            var sink = new RecordingRenderSink();
            DrawingReplayer.Replay(drawing, sink);
            Assert.Contains(sink.Events, e => e.StartsWith("DrawRectangle") && e.Contains("#FF0000FF"));
            Assert.Equal(0, native.BeginDrawCalls);
        }
        finally
        {
            scheduler.Stop();
            Write(window, "_rtActive", false);
            Write(window, "_renderThread", null);
            Write(window, "_rtPendingFrame", null);
            Write(window, "<Handle>k__BackingField", nint.Zero);
            window.Close();
        }
    }

    private static object? Read(Window window, string field)
        => typeof(Window).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
    private static void Write(Window window, string field, object? value)
        => typeof(Window).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void BusyNativeTargetRetainsStaticCaptureAndThenReplacesItWithLatestState()
    {
        using var setup = new CaptureWindow();
        setup.Window.ForceRenderFrame();
        var first = Read(setup.Window, "_rtPendingFrame")!;
        Write(setup.Window, "_rtPendingFrame", null);
        setup.Native.BeginDrawResult = (int)JaliumResult.InvalidState;
        Invoke(setup.Window, "PresentCaptureOnRenderThreadCore", first);
        Invoke(setup.Window, "CompleteRenderThreadCapture", first);
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.Same(first, Read(setup.Window, "_rtPendingFrame"));
        Assert.Equal(1L, Read(setup.Window, "_rtRecordedFrameCount"));
        Assert.Equal(0, setup.Native.EndDrawCalls);

        setup.Content.Value = 1000;
        setup.Content.InvalidateVisual();
        setup.Window.ForceRenderFrame();
        Invoke(setup.Window, "CompleteRenderThreadCapture", first);
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.NotSame(first, Read(setup.Window, "_rtPendingFrame"));
        Assert.Equal(2L, Read(setup.Window, "_rtRecordedFrameCount"));
        Assert.Equal(1, setup.Native.BeginDrawCalls);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void ResizeWhileWorkerIsBusyRecordsLatestViewportWithoutResizingOnUiThread()
    {
        using var setup = new CaptureWindow();
        setup.Window.ForceRenderFrame();
        var first = Read(setup.Window, "_rtPendingFrame")!;
        setup.Window.Width = 160;
        setup.Window.Height = 120;
        Invoke(setup.Window, "TryResizeRenderTarget", 160, 120, "CaptureCoalescingTest");
        setup.Window.ForceRenderFrame();
        setup.Window.Width = 200;
        setup.Window.Height = 140;
        Invoke(setup.Window, "TryResizeRenderTarget", 200, 140, "CaptureCoalescingTest");
        setup.Window.ForceRenderFrame();
        Assert.Equal(1L, Read(setup.Window, "_rtRecordedFrameCount"));
        Assert.Equal(0, setup.Native.ResizeCalls);

        Write(setup.Window, "_rtPendingFrame", null);
        Invoke(setup.Window, "CompleteRenderThreadCapture", first);
        Dispatcher.GetForCurrentThread().ProcessQueue();
        var latest = Read(setup.Window, "_rtPendingFrame")!;
        Assert.Equal(2L, Read(setup.Window, "_rtRecordedFrameCount"));
        Assert.Equal(200, latest.GetType().GetField("PhysicalWidth")!.GetValue(latest));
        Assert.Equal(140, latest.GetType().GetField("PhysicalHeight")!.GetValue(latest));
        Assert.Equal(Read(setup.Window, "_pendingResizeVersion"), latest.GetType().GetField("ResizeVersion")!.GetValue(latest));
        Assert.Equal(0, setup.Native.ResizeCalls);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void CloseCancelsWorkerCompletionAlreadyQueuedOnDispatcher()
    {
        using var setup = new CaptureWindow();
        setup.Window.ForceRenderFrame();
        var first = Read(setup.Window, "_rtPendingFrame")!;
        setup.Content.InvalidateVisual();
        setup.Window.ForceRenderFrame();
        Write(setup.Window, "_rtPendingFrame", null);
        Invoke(setup.Window, "CompleteRenderThreadCapture", first);
        setup.Dispose();
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.Equal(1L, Read(setup.Window, "_rtRecordedFrameCount"));
        Assert.Null(Read(setup.Window, "_rtPendingFrame"));
        Assert.Equal(0, setup.Native.BeginDrawCalls);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void QueuedRetryKeepsDrawingAliveUntilBothWorkerAndChannelReleaseIt()
    {
        using var setup = new CaptureWindow();
        setup.Window.ForceRenderFrame();
        var capture = Read(setup.Window, "_rtPendingFrame")!;
        var drawing = (RecordedDrawing)capture.GetType().GetField("Drawing")!.GetValue(capture)!;
        Write(setup.Window, "_rtPendingFrame", null); // transfer channel ownership to the worker
        setup.Native.BeginDrawResult = (int)JaliumResult.InvalidState;
        Invoke(setup.Window, "PresentCaptureOnRenderThreadCore", capture);
        drawing.Release(); // the worker's finally; the retry still owns one reference
        Assert.False(drawing.IsReleased);
        Assert.Same(capture, Read(setup.Window, "_rtPendingFrame"));
        Invoke(setup.Window, "ClearPendingRenderThreadCapture");
        Assert.True(drawing.IsReleased);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void SupersedingRetryReleasesOldDrawingAndPreservesNewestCapture()
    {
        using var setup = new CaptureWindow();
        setup.Window.ForceRenderFrame();
        var capture = Read(setup.Window, "_rtPendingFrame")!;
        var drawing = (RecordedDrawing)capture.GetType().GetField("Drawing")!.GetValue(capture)!;
        Write(setup.Window, "_rtPendingFrame", null);
        setup.Native.BeginDrawResult = (int)JaliumResult.InvalidState;
        Invoke(setup.Window, "PresentCaptureOnRenderThreadCore", capture);
        setup.Content.Value = 1000;
        setup.Content.InvalidateVisual();
        setup.Window.ForceRenderFrame();
        Invoke(setup.Window, "CompleteRenderThreadCapture", capture);
        Assert.False(drawing.IsReleased); // worker still owns it after the channel was cleared
        drawing.Release();
        Assert.True(drawing.IsReleased);
        Dispatcher.GetForCurrentThread().ProcessQueue();
        var latest = Read(setup.Window, "_rtPendingFrame")!;
        Assert.NotSame(capture, latest);
        var newestDrawing = (RecordedDrawing)latest.GetType().GetField("Drawing")!.GetValue(latest)!;
        Assert.False(newestDrawing.IsReleased);
        Invoke(setup.Window, "ClearPendingRenderThreadCapture");
        Assert.True(newestDrawing.IsReleased);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void LifecycleChangeDuringRecordingReleasesUnpublishedSnapshot()
    {
        using var setup = new CaptureWindow();
        var previous = Visual.RenderCacheHost;
        var host = new TrackingRenderCacheHost();
        Visual.RenderCacheHost = host;
        setup.Content.AfterDraw = () =>
            Write(setup.Window, "_renderLifecycleGeneration", (int)Read(setup.Window, "_renderLifecycleGeneration")! + 1);
        try
        {
            setup.Window.ForceRenderFrame();
            Assert.NotNull(host.LastFrameDrawing);
            Assert.True(host.LastFrameDrawing!.IsReleased);
            Assert.Null(Read(setup.Window, "_rtPendingFrame"));
            Assert.Equal(0, setup.Native.BeginDrawCalls);
        }
        finally { Visual.RenderCacheHost = previous; }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void InlineWholeFrameReplayReleasesSnapshotAndRestoresOffset()
        => VerifyInlineCaptureCleanup(failureStage: 0);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void InlineWholeFrameRecordingFailureReleasesAbandonedSnapshot()
        => VerifyInlineCaptureCleanup(failureStage: 1);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void InlineWholeFrameReplayFailureReleasesSnapshotAndRestoresOffset()
        => VerifyInlineCaptureCleanup(failureStage: 2);

    private static void VerifyInlineCaptureCleanup(int failureStage)
    {
        using var setup = new CaptureWindow();
        using var target = new RenderTargetDrawingContext(setup.Target, setup.Context);
        var previous = Visual.RenderCacheHost;
        int replays = 0;
        var host = new TrackingRenderCacheHost
        {
            ReplayFrame = context =>
            {
                Assert.Equal(Point.Zero, ((IOffsetDrawingContext)context).Offset);
                replays++;
                if (failureStage == 2) throw new InvalidOperationException("Replay failure");
            }
        };
        setup.Window.Measure(new Size(96, 64));
        setup.Window.Arrange(new Rect(0, 0, 96, 64));
        var offset = new Point(9, 17);
        target.Offset = offset;
        if (failureStage == 1)
            setup.Content.AfterDraw = () => throw new InvalidOperationException("Record failure");
        Visual.RenderCacheHost = host;
        try
        {
            if (failureStage == 0) Invoke(setup.Window, "RenderTreeOrCapture", target);
            else
            {
                var error = Assert.Throws<TargetInvocationException>(
                    () => Invoke(setup.Window, "RenderTreeOrCapture", target));
                Assert.IsType<InvalidOperationException>(error.InnerException);
            }
            Assert.NotNull(host.LastFrameDrawing);
            Assert.True(host.LastFrameDrawing!.IsReleased);
            Assert.Equal(failureStage == 1 ? 0 : 1, replays);
            Assert.Equal(offset, target.Offset);
        }
        finally { Visual.RenderCacheHost = previous; }
    }

    private static void Invoke(Window window, string method, params object[] arguments)
        => typeof(Window).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private sealed class CaptureWindow : IDisposable
    {
        internal readonly RenderContext Context;
        internal readonly RenderTarget Target;
        internal readonly RenderTargetTestNative Native = new();
        internal readonly StateVisual Content = new() { Width = 30, Height = 20 };
        internal readonly Window Window;
        private readonly RenderCaptureScheduler _scheduler = new();
        private readonly HiddenNativeWindow? _nativeWindow;
        private bool _disposed;

        internal CaptureWindow(bool realTarget = false)
        {
            MediaRenderCacheHost.Bootstrap();
            Context = new RenderContext(RenderBackend.Software);
            if (realTarget)
            {
                _nativeWindow = new HiddenNativeWindow(96, 64);
                Target = Context.CreateRenderTarget(_nativeWindow.Hwnd, 96, 64);
            }
            else
            {
                Target = new RenderTarget(Context.Backend, Context.Handle,
                    NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x6192)), 96, 64,
                    useComposition: false, native: Native, ownerContext: Context);
            }
            Window = new Window { Width = 96, Height = 64, Content = Content };
            typeof(Window).GetProperty("RenderTarget", BindingFlags.Instance | BindingFlags.Public)!.SetValue(Window, Target);
            Write(Window, "_dispatcher", Dispatcher.GetForCurrentThread());
            Write(Window, "_rtCaptureScheduler", _scheduler);
            Write(Window, "_renderThread", new Thread(static () => { }));
            Write(Window, "_rtActive", true);
            Write(Window, "_nativeWindowHidden", false); // model a presentable render-worker window
            Write(Window, "<Handle>k__BackingField", _nativeWindow?.Hwnd ?? new nint(0x6192));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scheduler.Stop();
            Write(Window, "_rtActive", false);
            Write(Window, "_renderThread", null);
            Invoke(Window, "ClearPendingRenderThreadCapture");
            Write(Window, "<Handle>k__BackingField", nint.Zero);
            Window.Close();
            Target.Dispose();
            Context.Dispose();
            _nativeWindow?.Dispose();
        }
    }

    private sealed class StateVisual : FrameworkElement
    {
        internal int Value;
        internal Action? AfterDraw;
        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawRectangle(new SolidColorBrush(Value == 1000 ? Colors.Blue : Colors.Red), null, new Rect(0, 0, 30, 20));
            AfterDraw?.Invoke();
        }
    }
}
