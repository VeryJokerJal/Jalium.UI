using System.Diagnostics;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

/// <summary>
/// Locks the frame-presented handshake: successful Presents are queued by
/// serial, a compositor retire signal pops the oldest serial, and callers can
/// wait for a serial beyond the one they captured when committing dry ink.
/// </summary>
/// <remarks>
/// [Collection("Application")] is mandatory for the Window tests: they
/// construct Window instances and would race DependencyProperty metadata
/// registration with other Window-constructing collections.
/// </remarks>
[Collection("Application")]
public class PresentHandshakeTests
{
    [Fact]
    public void Tracker_RetiresQueuedSerialsInOrder()
    {
        var tracker = new PresentCompletionTracker();

        tracker.Queue(1);
        tracker.Queue(2);
        tracker.Queue(3);

        Assert.Equal(1, tracker.Retire());
        Assert.Equal(1, tracker.LastPresentedSerial);
        Assert.Equal(2, tracker.PendingCount);

        Assert.Equal(2, tracker.Retire());
        Assert.Equal(2, tracker.LastPresentedSerial);
        Assert.Equal(1, tracker.PendingCount);

        Assert.Equal(3, tracker.Retire());
        Assert.Equal(3, tracker.LastPresentedSerial);
        Assert.Equal(0, tracker.PendingCount);
        Assert.Equal(3, tracker.LastQueuedSerial);
    }

    [Fact]
    public void Tracker_RetireWithoutPendingPresentIsIgnored()
    {
        var tracker = new PresentCompletionTracker();

        Assert.Null(tracker.Retire());
        Assert.Equal(0, tracker.LastPresentedSerial);
    }

    [Fact]
    public void Tracker_NonPositiveSerialIsIgnored()
    {
        var tracker = new PresentCompletionTracker();

        tracker.Queue(0);
        tracker.Queue(-1);

        Assert.Equal(0, tracker.PendingCount);
        Assert.Null(tracker.Retire());
    }

    [Fact]
    public void Tracker_ClearDropsPendingWithoutAdvancingPresented()
    {
        var tracker = new PresentCompletionTracker();

        tracker.Queue(1);
        tracker.Clear();

        Assert.Equal(0, tracker.PendingCount);
        Assert.Equal(0, tracker.LastPresentedSerial);
        Assert.Null(tracker.Retire());
    }

    [Fact]
    public void Window_DisabledHandshake_DoesNotQueueOrRaise()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x9001));
        window.IsPresentHandshakeEnabled = false;

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NotePresentRetired");

        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);
        Assert.Equal(0, raised);
        Assert.Equal(2L, GetPrivateField<long>(window, "_successfulPresentCount"));
    }

    [Fact]
    public void Window_EnabledHandshake_QueuesOnPresentAndRaisesOnRetire()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x9002));
        window.IsPresentHandshakeEnabled = true;

        var serials = new List<long>();
        window.FramePresented += (_, args) => serials.Add(args.PresentSerial);

        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(1, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);
        Assert.Empty(serials);

        InvokePrivate(window, "NotePresentRetired");
        Assert.Equal(1, window.LastPresentedFrameSerial);
        Assert.Equal(new long[] { 1 }, serials);

        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NotePresentRetired");
        Assert.Equal(2, window.LastQueuedFrameSerial);
        Assert.Equal(2, window.LastPresentedFrameSerial);
        Assert.Equal(new long[] { 1, 2 }, serials);
    }

    [Fact]
    public void Window_NoWaitable_DoesNotQueue()
    {
        var (window, _) = CreateWindowWithWaitable(nint.Zero);
        window.IsPresentHandshakeEnabled = true;

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        Assert.False(window.IsPresentHandshakeSupported);

        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NotePresentRetired");

        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Window_DisableDropsPendingSerials()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x9003));
        window.IsPresentHandshakeEnabled = true;

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(1, window.LastQueuedFrameSerial);

        window.IsPresentHandshakeEnabled = false;
        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);

        InvokePrivate(window, "NotePresentRetired");
        Assert.Equal(0, raised);

        window.IsPresentHandshakeEnabled = true;
        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(0, window.LastPresentedFrameSerial);
        // Present serials are monotonic for the window lifetime, so the
        // re-enabled tracker starts at 2 after the earlier successful Present.
        Assert.Equal(2, window.LastQueuedFrameSerial);
    }

    [Fact]
    public void Window_PresentFailure_DoesNotQueue()
    {
        var (window, native) = CreateWindowWithWaitable(new nint(0x9004));
        window.IsPresentHandshakeEnabled = true;
        native.EndDrawResult = (int)JaliumResult.PresentFailed;

        var renderTarget = (RenderTarget)GetPrivateProperty(window, "RenderTarget")!;
        Assert.True(renderTarget.TryBeginDraw());

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        bool presented = (bool)InvokePrivate(window, "CompleteEndDrawOrHandleFailure")!;

        Assert.False(presented);
        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);
        Assert.Equal(0, raised);
        Assert.Equal(0L, GetPrivateField<long>(window, "_successfulPresentCount"));
    }

    [Fact]
    public void Window_SubscriberFailure_DoesNotBlockRemainingSubscribers()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x9005));
        window.IsPresentHandshakeEnabled = true;

        int healthyCalls = 0;
        window.FramePresented += (_, _) => throw new InvalidOperationException("Injected test failure.");
        window.FramePresented += (_, _) => healthyCalls++;

        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NotePresentRetired");

        Assert.Equal(1, healthyCalls);
        Assert.Equal(1, window.LastPresentedFrameSerial);
    }

    [Fact]
    public void Window_MaxFrameLatencyAboveOne_RefusesHandshake()
    {
        var (window, native) = CreateWindowWithWaitable(new nint(0x9006));
        native.PresentInfoValue = new PresentInfo { MaxFrameLatency = 2, WaitableEnabled = 1 };
        window.IsPresentHandshakeEnabled = true;

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        Assert.False(window.IsPresentHandshakeSupported);

        // The refusal must be diagnosable, not silent.
        Assert.Equal(0, window.LastQueuedFrameSerial);

        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NotePresentRetired");

        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);
        Assert.Equal(0, raised);
        Assert.Equal(1L, GetPrivateField<long>(window, "_successfulPresentCount"));

        // Latency 1 (the default configuration) stays supported.
        native.PresentInfoValue = new PresentInfo { MaxFrameLatency = 1, WaitableEnabled = 1 };
        Assert.True(window.IsPresentHandshakeSupported);
    }

    [Fact]
    public void Window_ResetDetachesTracker_FallbackTargetStopsQueueing()
    {
        // Simulate a recovery fallback: a tracked D3D12 window switches to a
        // render target without a waitable, then the reset sites run.
        var (window, _) = CreateWindowWithWaitable(new nint(0x9007));
        window.IsPresentHandshakeEnabled = true;

        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(1, window.LastQueuedFrameSerial);

        var fallbackNative = new RenderTargetTestNative { FrameLatencyWaitableHandle = nint.Zero };
        var fallbackTarget = new RenderTarget(
            backend: RenderBackend.D3D12,
            contextHandle: new nint(0x5678),
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x2002)),
            width: 300,
            height: 200,
            useComposition: false,
            native: fallbackNative);
        SetPrivateProperty(window, "RenderTarget", fallbackTarget);

        InvokePrivate(window, "ResetPresentHandshakeQueue");

        // Detached: serials read as 0 until a supported target presents again.
        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        // Presents on the fallback target must not queue against a swap chain
        // that never produces retire signals, and the non-pacing retire hook
        // must not fire events for them.
        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.True((bool)InvokePrivate(window, "TryBeginDrawOrScheduleRetry")!);
        Assert.Equal(0, raised);

        // A later supported target re-establishes tracking (the rejection was
        // cached per target, and the reset cleared it).
        var (supportedWindow, _) = CreateWindowWithWaitable(new nint(0x9008));
        supportedWindow.IsPresentHandshakeEnabled = true;
        InvokePrivate(supportedWindow, "NoteSuccessfulPresent");
        Assert.Equal(1, supportedWindow.LastQueuedFrameSerial);
    }

    [Fact]
    public void Window_DiscardingResize_ClearsQueueBeforeNativeResize()
    {
        var (window, native) = CreateWindowWithWaitable(new nint(0x9009));
        window.IsPresentHandshakeEnabled = true;
        native.ResizePreservesPendingPresentsValue = false;

        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(1, window.LastQueuedFrameSerial);

        // The queue must already be empty when the native resize runs — a
        // retire callback racing the ResizeBuffers window must not be able to
        // pop a serial the resize is invalidating.
        long? serialSeenByNativeResize = -1;
        native.OnResize = (_, _) =>
            serialSeenByNativeResize = window.LastQueuedFrameSerial;

        var result = InvokePrivate(
            window,
            "ApplyRenderTargetResize",
            400, 300, "test-stage", false);

        Assert.Equal("Completed", result!.ToString());
        Assert.Equal(0, serialSeenByNativeResize);
        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal((400, 300), (native.ResizeSizes[0].Width, native.ResizeSizes[0].Height));
    }

    [Fact]
    public void Window_PreservingResize_KeepsQueuedSerials()
    {
        var (window, native) = CreateWindowWithWaitable(new nint(0x900A));
        window.IsPresentHandshakeEnabled = true;
        // Composition in-capacity resize: metadata-only, pending presents keep
        // their retire pairing — clearing here would mislabel the NEXT
        // present's serial with the old frame's retire signal.
        native.ResizePreservesPendingPresentsValue = true;

        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(1, window.LastQueuedFrameSerial);

        long? serialSeenByNativeResize = -1;
        native.OnResize = (_, _) =>
            serialSeenByNativeResize = window.LastQueuedFrameSerial;

        var result = InvokePrivate(
            window,
            "ApplyRenderTargetResize",
            400, 300, "test-stage", false);

        Assert.Equal("Completed", result!.ToString());
        Assert.Equal(1, serialSeenByNativeResize);
        Assert.Equal(1, window.LastQueuedFrameSerial);
    }

    [Fact]
    public void Window_WorkerOwnedDiscardingResize_ClearsQueueBeforeNativeResize()
    {
        var (window, native) = CreateWindowWithWaitable(new nint(0x900E));
        window.IsPresentHandshakeEnabled = true;
        native.ResizePreservesPendingPresentsValue = false;

        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(1, window.LastQueuedFrameSerial);

        var cap = NewWorkerResizeCapture(
            window,
            (RenderTarget)GetPrivateProperty(window, "RenderTarget")!,
            width: 400,
            height: 300);
        long? serialSeenByNativeResize = -1;
        native.OnResize = (_, _) =>
            serialSeenByNativeResize = window.LastQueuedFrameSerial;

        var previousHost = Visual.RenderCacheHost;
        Visual.RenderCacheHost = new UnreachableRenderCacheHost();
        try
        {
            InvokePrivate(window, "PresentCaptureOnRenderThreadCore", cap);
        }
        finally
        {
            Visual.RenderCacheHost = previousHost;
        }

        // The worker-owned resize transaction must follow the same contract as
        // the UI-thread path: serials are already gone when the native resize
        // runs, so a retire callback racing the resize can never pop one of
        // them and confirm a frame the resize is about to discard.
        Assert.Equal(0, serialSeenByNativeResize);
        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal((400, 300), (native.ResizeSizes[0].Width, native.ResizeSizes[0].Height));
    }

    [Fact]
    public void Window_WorkerOwnedPreservingResize_KeepsQueuedSerials()
    {
        // A composition in-capacity resize performed by the render worker is
        // metadata-only (the swap chain and its queued presents survive), so
        // the handshake serials must keep their retire pairing — clearing here
        // would pair the NEXT present's serial with the old frame's retire.
        var (window, native) = CreateWindowWithWaitable(new nint(0x900F));
        window.IsPresentHandshakeEnabled = true;
        native.ResizePreservesPendingPresentsValue = true;

        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(1, window.LastQueuedFrameSerial);

        var cap = NewWorkerResizeCapture(
            window,
            (RenderTarget)GetPrivateProperty(window, "RenderTarget")!,
            width: 400,
            height: 300);
        long? serialSeenByNativeResize = -1;
        native.OnResize = (_, _) =>
            serialSeenByNativeResize = window.LastQueuedFrameSerial;

        var previousHost = Visual.RenderCacheHost;
        Visual.RenderCacheHost = new UnreachableRenderCacheHost();
        try
        {
            InvokePrivate(window, "PresentCaptureOnRenderThreadCore", cap);
        }
        finally
        {
            Visual.RenderCacheHost = previousHost;
        }

        Assert.Equal(1, serialSeenByNativeResize);
        Assert.Equal(1, window.LastQueuedFrameSerial);
        Assert.Equal((400, 300), (native.ResizeSizes[0].Width, native.ResizeSizes[0].Height));
    }

    [Fact]
    public void Window_ArmRetireProbe_QueuesIdenticalCaptureWhenIdle()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x900B));
        window.IsPresentHandshakeEnabled = true;

        InvokePrivate(window, "NoteSuccessfulPresent");

        var cap = NewFrameCapture(probe: false);
        InvokePrivate(window, "ArmRetireProbeIfIdle", cap);

        var queued = GetPrivateField<object?>(window, "_rtPendingFrame");
        Assert.NotNull(queued);
        Assert.True(GetCaptureField<bool>(queued!, "IsRetireProbe"));
        Assert.True(GetCaptureField<long>(queued!, "RetireProbeDeadlineTicks") > 0);

        // A probe never arms another probe.
        InvokePrivate(window, "ArmRetireProbeIfIdle", queued!);
        Assert.Same(queued, GetPrivateField<object?>(window, "_rtPendingFrame"));
    }

    [Fact]
    public void Window_ArmRetireProbe_SkippedWhenMailboxBusyOrNothingPending()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x900C));
        window.IsPresentHandshakeEnabled = true;

        // Nothing queued: no retire to observe.
        var idleCap = NewFrameCapture(probe: false);
        InvokePrivate(window, "ArmRetireProbeIfIdle", idleCap);
        Assert.Null(GetPrivateField<object?>(window, "_rtPendingFrame"));
        Assert.False(GetCaptureField<bool>(idleCap, "IsRetireProbe"));

        // Queued serial, but a real capture is already in the mailbox — its
        // BeginDraw observes the retire, so no probe.
        InvokePrivate(window, "NoteSuccessfulPresent");
        var realCap = NewFrameCapture(probe: false);
        SetPrivateField(window, "_rtPendingFrame", realCap);
        var secondCap = NewFrameCapture(probe: false);
        InvokePrivate(window, "ArmRetireProbeIfIdle", secondCap);
        Assert.Same(realCap, GetPrivateField<object?>(window, "_rtPendingFrame"));
        Assert.False(GetCaptureField<bool>(secondCap, "IsRetireProbe"));
    }

    [Fact]
    public void Window_ExpiredRetireProbe_DroppedWithoutReplay()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x900D));
        window.IsPresentHandshakeEnabled = true;

        InvokePrivate(window, "NoteSuccessfulPresent");

        // An expired probe with a live tracker and a matching lifecycle must
        // return before touching the capture's target — without the deadline
        // guard this reaches cap.Target (null here) and throws.
        var cap = NewFrameCapture(probe: true);
        SetCaptureField(
            cap,
            "RetireProbeDeadlineTicks",
            Stopwatch.GetTimestamp() - Stopwatch.Frequency);
        SetCaptureField(
            cap,
            "LifecycleGeneration",
            GetPrivateField<int>(window, "_renderLifecycleGeneration"));

        InvokePrivate(window, "PresentCaptureOnRenderThreadCore", cap);

        Assert.Null(GetPrivateField<object?>(window, "_rtPendingFrame"));
        Assert.Equal(1, window.LastQueuedFrameSerial);
    }

    [Fact]
    public void Window_PublicHandshakeSurface_MatchesReflectionContract()
    {
        // The ink layer bridges these symbols BY NAME through reflection; a
        // rename must fail here instead of silently breaking the bridge.
        Assert.NotNull(typeof(Window).GetEvent("FramePresented"));
        Assert.Equal(
            typeof(EventHandler<FramePresentedEventArgs>),
            typeof(Window).GetEvent("FramePresented")!.EventHandlerType);

        Assert.NotNull(typeof(FramePresentedEventArgs).GetProperty("PresentSerial"));
        Assert.Equal(typeof(long), typeof(FramePresentedEventArgs).GetProperty("PresentSerial")!.PropertyType);
        Assert.NotNull(typeof(FramePresentedEventArgs).GetProperty("Timestamp"));
        Assert.Equal(typeof(long), typeof(FramePresentedEventArgs).GetProperty("Timestamp")!.PropertyType);

        Assert.NotNull(typeof(Window).GetProperty("IsPresentHandshakeEnabled"));
        Assert.NotNull(typeof(Window).GetProperty("IsPresentHandshakeSupported"));
        Assert.NotNull(typeof(Window).GetProperty("LastQueuedFrameSerial"));
        Assert.NotNull(typeof(Window).GetProperty("LastPresentedFrameSerial"));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static object NewWorkerResizeCapture(
        Window window,
        RenderTarget target,
        int width,
        int height)
    {
        var cap = NewFrameCapture(probe: false);
        SetCaptureField(cap, "Target", target);
        // A fake render target has no owner context; PresentCaptureOnRender
        // ThreadCore verifies context ownership with ReferenceEquals, so null
        // matches null, and the method returns right after the resize
        // transaction — before any drawing happens.
        SetCaptureField(cap, "OwnerContext", null);
        SetCaptureField(cap, "ContextGeneration", 0);
        SetCaptureField(
            cap,
            "LifecycleGeneration",
            GetPrivateField<int>(window, "_renderLifecycleGeneration"));
        SetCaptureField(cap, "PhysicalWidth", width);
        SetCaptureField(cap, "PhysicalHeight", height);
        // A resize version above the committed one makes the worker apply the
        // native resize as part of this capture's frame transaction.
        SetCaptureField(cap, "ResizeVersion", 1);
        SetCaptureField(cap, "RequireNativeSameSize", false);
        return cap;
    }

    // PresentCaptureOnRenderThreadCore bails out before the frame transaction
    // when Visual.RenderCacheHost is null; with a context-less fake target the
    // method never reaches the drawing stage, so this host's members are
    // unreachable in the worker-resize tests.
    private sealed class UnreachableRenderCacheHost : Jalium.UI.Rendering.IRenderCacheHost
    {
        public DrawingContext CreateRecorder(DrawingContext targetDrawingContext)
            => throw new UnreachableException();

        public object FinishRecord(DrawingContext recorder)
            => throw new UnreachableException();

        public void Replay(object drawing, DrawingContext targetDrawingContext)
            => throw new UnreachableException();
    }

    private static (Window window, RenderTargetTestNative native) CreateWindowWithWaitable(nint waitable)
    {
        var window = new Window { Width = 300, Height = 200 };
        var native = new RenderTargetTestNative { FrameLatencyWaitableHandle = waitable };
        var renderTarget = new RenderTarget(
            backend: RenderBackend.D3D12,
            contextHandle: new nint(0x1234),
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x2001)),
            width: 300,
            height: 200,
            useComposition: false,
            native: native);

        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateProperty(window, "RenderTarget", renderTarget);
        return (window, native);
    }

    private static object? InvokePrivate(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try
        {
            return method!.Invoke(instance, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static object? InvokePrivate(object instance, string methodName, params object?[] args)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try
        {
            return method!.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static object NewFrameCapture(bool probe)
    {
        var captureType = typeof(Window).GetNestedType(
            "FrameCapture", BindingFlags.NonPublic);
        Assert.NotNull(captureType);
        var cap = Activator.CreateInstance(captureType!)!;
        if (probe)
        {
            SetCaptureField(cap, "IsRetireProbe", true);
        }
        return cap;
    }

    private static T GetCaptureField<T>(object capture, string fieldName)
    {
        var field = capture.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(field);
        return (T)field!.GetValue(capture)!;
    }

    private static void SetCaptureField(object capture, string fieldName, object? value)
    {
        var field = capture.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(field);
        field!.SetValue(capture, value);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void SetPrivateProperty(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static object? GetPrivateProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return property!.GetValue(instance);
    }
}
