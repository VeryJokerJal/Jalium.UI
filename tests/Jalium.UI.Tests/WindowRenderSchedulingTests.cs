using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

// [Collection("Application")] is mandatory: these tests construct Window
// instances; running in parallel with other Window-constructing test classes
// races DependencyProperty metadata registration (random flaky failures).
[Collection("Application")]
public class WindowRenderSchedulingTests
{
    private const int RenderFlag_Scheduled = 1 << 0;
    private const int RenderFlag_Rendering = 1 << 1;
    private const int RenderFlag_DirtyBetween = 1 << 3;

    [Fact]
    public void RenderTarget_TryBeginDraw_WhenGpuBusy_ReturnsFalse()
    {
        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InvalidState
        };

        using var renderTarget = CreateRenderTarget(native, width: 320, height: 240, hwnd: new nint(0x2101));

        Assert.False(renderTarget.TryBeginDraw());
    }

    [Fact]
    public void RenderTarget_TryBeginDraw_WhenBeginFails_Throws()
    {
        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.DeviceLost
        };

        using var renderTarget = CreateRenderTarget(native, width: 320, height: 240, hwnd: new nint(0x2102));

        var exception = Assert.Throws<RenderPipelineException>(() => renderTarget.TryBeginDraw());
        Assert.Equal("Begin", exception.Stage);
    }

    [Fact]
    public void Window_ForceRenderFrame_WhenGpuBusy_ArmsRetryWithoutUpdatingLastRenderTimestamp()
    {
        var window = new Window
        {
            Width = 300,
            Height = 200
        };

        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", new nint(0x2103));

        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InvalidState
        };

        using var renderTarget = CreateRenderTarget(native, width: 300, height: 200, hwnd: new nint(0x2103));
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        window.ForceRenderFrame();

        Assert.Equal(0L, GetPrivateField<long>(window, "_lastRenderTicks"));
        // The retry is an ARMED deferred timer; RenderFlag_Scheduled is claimed only
        // when it fires. Claiming at arm time blocked every animation tick's
        // InvalidateWindow for the timer's ~15.6 ms resolution window after each
        // present — the present-cadence stutter fixed with the animation rewrite.
        Assert.NotNull(GetPrivateField<Timer?>(window, "_renderThrottleTimer"));

        GetPrivateField<Timer?>(window, "_renderThrottleTimer")?.Dispose();
    }

    [Fact]
    public void Window_InvalidateWindow_WhenCompositionTargetIsUncapped_BanksDirtyUntilFrameStarting()
    {
        var window = new Window
        {
            Width = 300,
            Height = 200
        };

        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", new nint(0x2104));

        EventHandler noop = static (_, _) => { };
        CompositionTarget.Rendering += noop;
        CompositionTarget.Subscribe();

        try
        {
            window.InvalidateWindow();

            Assert.False(HasRenderFlag(window, RenderFlag_Scheduled));
            Assert.True(HasRenderFlag(window, RenderFlag_DirtyBetween));

            InvokePrivateMethod(window, "OnFrameStarting");

            Assert.True(HasRenderFlag(window, RenderFlag_Scheduled));
            Assert.False(HasRenderFlag(window, RenderFlag_DirtyBetween));
        }
        finally
        {
            CompositionTarget.Unsubscribe();
            CompositionTarget.Rendering -= noop;
            SetPrivateField(window, "<Handle>k__BackingField", nint.Zero);
            GetPrivateField<Timer?>(window, "_renderThrottleTimer")?.Dispose();
        }
    }

    [Fact]
    public void Window_ForceRenderFrame_WhenGpuBusyAndCompositionTargetIsUncapped_SchedulesDeferredRetry()
    {
        var window = new Window
        {
            Width = 300,
            Height = 200
        };

        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", new nint(0x2105));

        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InvalidState
        };

        using var renderTarget = CreateRenderTarget(native, width: 300, height: 200, hwnd: new nint(0x2105));
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        EventHandler noop = static (_, _) => { };
        CompositionTarget.Rendering += noop;
        CompositionTarget.Subscribe();

        try
        {
            window.ForceRenderFrame();

            // Deferred retry armed as a timer; the Scheduled flag must stay CLEAR
            // until the timer fires so that real invalidations (animation ticks)
            // arriving in the interim can schedule a render immediately instead
            // of being blocked behind the ~15.6 ms coarse timer.
            Assert.NotNull(GetPrivateField<Timer?>(window, "_renderThrottleTimer"));
            Assert.False(HasRenderFlag(window, RenderFlag_Scheduled));
            Assert.False(HasRenderFlag(window, RenderFlag_DirtyBetween));
        }
        finally
        {
            CompositionTarget.Unsubscribe();
            CompositionTarget.Rendering -= noop;
            GetPrivateField<Timer?>(window, "_renderThrottleTimer")?.Dispose();
        }
    }

    [Fact]
    public void Window_OnNativeDestroyed_ReleasesRenderResourcesAndIsIdempotent()
    {
        var window = new Window
        {
            Width = 300,
            Height = 200
        };

        var hwnd = new nint(0x2106);
        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", hwnd);
        SetPrivateField(window, "_renderState", RenderFlag_Scheduled | RenderFlag_DirtyBetween);

        var native = new RenderTargetTestNative();
        var renderTarget = CreateRenderTarget(native, width: 300, height: 200, hwnd);
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        var deferredTimer = new Timer(static _ => { }, null, Timeout.Infinite, Timeout.Infinite);
        SetPrivateField(window, "_renderThrottleTimer", deferredTimer);

        var closedCount = 0;
        window.Closed += (_, _) => closedCount++;

        InvokePrivateMethod(window, "OnNativeDestroyed", hwnd);

        Assert.Equal(nint.Zero, GetPrivateField<nint>(window, "<Handle>k__BackingField"));
        Assert.Null(window.RenderTarget);
        Assert.False(renderTarget.IsValid);
        Assert.Equal(1, native.DestroyCalls);
        Assert.Null(GetPrivateField<Timer?>(window, "_renderThrottleTimer"));
        Assert.False(deferredTimer.Change(1, Timeout.Infinite));
        Assert.False(HasRenderFlag(window, RenderFlag_Scheduled));
        Assert.False(HasRenderFlag(window, RenderFlag_DirtyBetween));
        Assert.Equal(1, closedCount);

        // A duplicate native teardown notification must not destroy resources or
        // raise the managed Closed event a second time.
        InvokePrivateMethod(window, "OnNativeDestroyed", hwnd);

        Assert.Equal(1, native.DestroyCalls);
        Assert.Equal(1, closedCount);
    }

    [Fact]
    public void Window_CloseReenteredFromOnRender_DefersTargetReleaseUntilFrameExit()
    {
        var window = new CloseOnRenderWindow
        {
            Width = 300,
            Height = 200
        };
        var hwnd = new nint(0x2107);
        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", hwnd);
        SetPrivateField(window, "_renderState", RenderFlag_Rendering);

        var native = new RenderTargetTestNative();
        var renderTarget = CreateRenderTarget(native, 300, 200, hwnd);
        SetPrivateProperty(window, "RenderTarget", renderTarget);
        var generation = GetPrivateField<int>(window, "_renderLifecycleGeneration");

        window.InvokeOnRender(renderTarget);

        Assert.True(GetPrivateField<bool>(window, "_managedTeardownPending"));
        Assert.False(GetPrivateField<bool>(window, "_managedTeardownCompleted"));
        Assert.NotEqual(generation, GetPrivateField<int>(window, "_renderLifecycleGeneration"));
        Assert.True(renderTarget.IsValid);
        Assert.Equal(0, native.DestroyCalls);
        Assert.False(InvokePrivateMethod<bool>(
            window, "IsRenderLifecycleCurrent", generation, renderTarget));

        // Model RenderFrame's finally boundary: release Rendering, then drain
        // the deferred close. Native resources may only be destroyed now.
        SetPrivateField(window, "_renderState", 0);
        InvokePrivateMethod(window, "CompletePendingManagedTeardown");

        Assert.True(GetPrivateField<bool>(window, "_managedTeardownCompleted"));
        Assert.Null(window.RenderTarget);
        Assert.False(renderTarget.IsValid);
        Assert.Equal(1, native.DestroyCalls);
    }

    [Fact]
    public void Window_CloseReenteredFromMeasure_DefersTargetReleaseUntilLayoutReturns()
    {
        var window = new Window { Width = 300, Height = 200 };
        var hwnd = new nint(0x2108);
        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", hwnd);
        SetPrivateField(window, "_renderState", RenderFlag_Rendering);

        var native = new RenderTargetTestNative();
        var renderTarget = CreateRenderTarget(native, 300, 200, hwnd);
        SetPrivateProperty(window, "RenderTarget", renderTarget);
        var element = new CloseOnMeasureElement(window);

        element.Measure(new Size(100, 100));

        Assert.True(element.CloseCalled);
        Assert.True(GetPrivateField<bool>(window, "_managedTeardownPending"));
        Assert.True(renderTarget.IsValid);
        Assert.Equal(0, native.DestroyCalls);

        SetPrivateField(window, "_renderState", 0);
        InvokePrivateMethod(window, "CompletePendingManagedTeardown");

        Assert.Null(window.RenderTarget);
        Assert.Equal(1, native.DestroyCalls);
    }

    [Fact]
    public void Window_RenderThreadJoinTimeout_DefersTargetReleaseUntilThreadActuallyExits()
    {
        using var releaseThread = new ManualResetEventSlim(false);
        var stalledThread = new Thread(() => releaseThread.Wait()) { IsBackground = true };
        stalledThread.Start();

        var window = new Window { Width = 300, Height = 200 };
        var hwnd = new nint(0x2109);
        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", hwnd);
        SetPrivateField(window, "_renderThread", stalledThread);
        SetPrivateField(window, "_rtActive", true);

        var native = new RenderTargetTestNative();
        var renderTarget = CreateRenderTarget(native, 300, 200, hwnd);
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        try
        {
            InvokePrivateMethod(window, "OnNativeDestroyed", hwnd);

            Assert.Same(stalledThread, GetPrivateField<Thread?>(window, "_renderThread"));
            Assert.Same(renderTarget, window.RenderTarget);
            Assert.True(renderTarget.IsValid);
            Assert.Equal(0, native.DestroyCalls);
        }
        finally
        {
            releaseThread.Set();
        }

        Assert.True(SpinWait.SpinUntil(
            () => native.DestroyCalls == 1,
            TimeSpan.FromSeconds(5)));
        Assert.Null(GetPrivateField<Thread?>(window, "_renderThread"));
        Assert.Null(window.RenderTarget);
        Assert.False(renderTarget.IsValid);
    }

    [Fact]
    public void OleDropTarget_RevokeAfterNativeDestroy_ReleasesManagedWindowRoot()
    {
        var weakWindow = RegisterSyntheticOleStateAndRevoke(new nint(0x2110));

        ForceFinalizers();

        Assert.False(weakWindow.TryGetTarget(out _));
    }

    [Fact]
    public void ReleaseDrawingContextBeforeRenderTargetDisposal_UnsubscribesStaticEvictionHandler()
    {
        var weakContext = CreateAndReleaseWindowDrawingContext();

        ForceFinalizers();

        Assert.False(weakContext.TryGetTarget(out _));
    }

    [Fact]
    public void Window_ResolveRenderTargetContext_PrefersTargetOwner()
    {
        using var ownerContext = new RenderContext(RenderBackend.Software);
        var native = new RenderTargetTestNative();
        using var renderTarget = new RenderTarget(
            backend: ownerContext.Backend,
            contextHandle: ownerContext.Handle,
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x2112)),
            width: 96,
            height: 64,
            useComposition: false,
            native: native,
            ownerContext: ownerContext);
        var window = new Window { Width = 96, Height = 64 };

        var resolved = InvokePrivateMethod<RenderContext>(
            window,
            "ResolveRenderTargetContext",
            renderTarget);

        Assert.Same(ownerContext, resolved);
    }

    [Fact]
    public void RenderThreadCapture_WithStaleLifecycle_DoesNotTouchTarget()
    {
        using var ownerContext = new RenderContext(RenderBackend.Software);
        var native = new RenderTargetTestNative();
        using var renderTarget = new RenderTarget(
            backend: ownerContext.Backend,
            contextHandle: ownerContext.Handle,
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x2113)),
            width: 96,
            height: 64,
            useComposition: false,
            native: native,
            ownerContext: ownerContext);
        var window = new Window { Width = 96, Height = 64 };
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        var captureType = typeof(Window).GetNestedType("FrameCapture", BindingFlags.NonPublic);
        Assert.NotNull(captureType);
        var capture = Activator.CreateInstance(captureType!, nonPublic: true);
        Assert.NotNull(capture);
        captureType!.GetField("Drawing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(capture, RecordedDrawing.Empty);
        captureType.GetField("Target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(capture, renderTarget);
        captureType.GetField("OwnerContext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(capture, ownerContext);
        captureType.GetField("ContextGeneration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(capture, ownerContext.Generation);
        captureType.GetField("LifecycleGeneration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(capture, 0);

        SetPrivateField(window, "_renderLifecycleGeneration", 1);
        InvokePrivateMethod(window, "PresentCaptureOnRenderThreadCore", capture);

        Assert.Equal(0, native.BeginDrawCalls);
    }

    [Fact]
    public void RenderThreadPublish_WhenWorkerOwnershipIsGone_PreservesDirtyState()
    {
        MediaRenderCacheHost.Bootstrap();
        using var ownerContext = new RenderContext(RenderBackend.Software);
        using var renderTarget = new RenderTarget(
            backend: ownerContext.Backend,
            contextHandle: ownerContext.Handle,
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x2114)),
            width: 96,
            height: 64,
            useComposition: false,
            native: new RenderTargetTestNative(),
            ownerContext: ownerContext);
        var window = new Window { Width = 96, Height = 64 };
        SetPrivateProperty(window, "RenderTarget", renderTarget);
        SetPrivateField(window, "_fullInvalidation", true);
        SetPrivateField(window, "_rtActive", false);
        SetPrivateField(window, "_renderThread", null);

        InvokePrivateMethod(
            window,
            "PublishFrameToRenderThread",
            GetPrivateField<int>(window, "_renderLifecycleGeneration"),
            renderTarget);

        Assert.True(GetPrivateField<bool>(window, "_fullInvalidation"));
        Assert.Null(GetPrivateField<object?>(window, "_rtPendingFrame"));
    }

    [Fact]
    public void Window_RenderThreadPublish_DoesNotEndWorkerOwnedDrawSession()
    {
        MediaRenderCacheHost.Bootstrap();
        using var ownerContext = new RenderContext(RenderBackend.Software);
        var native = new RenderTargetTestNative();
        using var renderTarget = new RenderTarget(
            backend: ownerContext.Backend,
            contextHandle: ownerContext.Handle,
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x2115)),
            width: 96,
            height: 64,
            useComposition: false,
            native: native,
            ownerContext: ownerContext);
        var window = new Window { Width = 96, Height = 64 };
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        // Model the exact publish/finally race deterministically: the worker
        // already owns an open native draw session when the UI-side RenderFrame
        // reaches its finally block after publishing a capture.
        Assert.True(renderTarget.TryBeginDraw());
        var workerMarker = new Thread(static () => { });
        SetPrivateField(window, "_rtActive", true);
        SetPrivateField(window, "_renderThread", workerMarker);
        SetPrivateField(window, "_fullInvalidation", true);

        try
        {
            window.ForceRenderFrame();

            Assert.Equal(0, native.EndDrawCalls);
            Assert.True(renderTarget.IsDrawing);
            Assert.NotNull(GetPrivateField<object?>(window, "_rtPendingFrame"));
        }
        finally
        {
            SetPrivateField(window, "_rtActive", false);
            SetPrivateField(window, "_renderThread", null);
            SetPrivateField(window, "_rtPendingFrame", null);
            _ = renderTarget.TryEndDraw();
        }
    }

    private static RenderTarget CreateRenderTarget(RenderTargetTestNative native, int width, int height, nint hwnd)
    {
        return new RenderTarget(
            backend: RenderBackend.D3D12,
            contextHandle: new nint(0x1234),
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(hwnd),
            width: width,
            height: height,
            useComposition: false,
            native: native);
    }

    private static bool HasRenderFlag(Window window, int flag)
    {
        var state = GetPrivateField<int>(window, "_renderState");
        return (state & flag) != 0;
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = FindInstanceField(instance.GetType(), fieldName);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = FindInstanceField(instance.GetType(), fieldName);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void InvokePrivateMethod(object instance, string methodName, params object?[]? arguments)
    {
        var method = FindInstanceMethod(instance.GetType(), methodName);
        Assert.NotNull(method);
        method!.Invoke(instance, arguments);
    }

    private static T InvokePrivateMethod<T>(object instance, string methodName, params object?[]? arguments)
    {
        var method = FindInstanceMethod(instance.GetType(), methodName);
        Assert.NotNull(method);
        return (T)method!.Invoke(instance, arguments)!;
    }

    private static FieldInfo? FindInstanceField(Type type, string fieldName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static MethodInfo? FindInstanceMethod(Type type, string methodName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    private static PropertyInfo? FindInstanceProperty(Type type, string propertyName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static void SetPrivateProperty(object instance, string propertyName, object? value)
    {
        var property = FindInstanceProperty(instance.GetType(), propertyName);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Window> RegisterSyntheticOleStateAndRevoke(nint hwnd)
    {
        var stateType = typeof(Window).Assembly.GetType("Jalium.UI.OleDropTarget+DropTargetState");
        Assert.NotNull(stateType);
        var state = Activator.CreateInstance(stateType!, nonPublic: true);
        Assert.NotNull(state);
        var window = new Window();
        var weakWindow = new WeakReference<Window>(window);
        var selfHandle = GCHandle.Alloc(state);
        var comObject = Marshal.AllocHGlobal(nint.Size * 2);

        stateType!.GetField("Window", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(state, window);
        stateType.GetField("SelfHandle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(state, selfHandle);
        stateType.GetField("ComObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(state, comObject);

        var statesField = typeof(OleDropTarget).GetField(
            "_states", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(statesField);
        var states = (IDictionary)statesField!.GetValue(null)!;
        states.Add(hwnd, state);

        OleDropTarget.RevokeWindow(hwnd, nativeWindowAlive: false);

        Assert.False(states.Contains(hwnd));
        return weakWindow;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<RenderTargetDrawingContext> CreateAndReleaseWindowDrawingContext()
    {
        using var ownerContext = new RenderContext(RenderBackend.Software);
        using var renderTarget = new RenderTarget(
            backend: ownerContext.Backend,
            contextHandle: ownerContext.Handle,
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x2111)),
            width: 64,
            height: 64,
            useComposition: false,
            native: new RenderTargetTestNative(),
            ownerContext: ownerContext);
        var window = new Window();
        var drawingContext = new RenderTargetDrawingContext(renderTarget, ownerContext);
        var weakContext = new WeakReference<RenderTargetDrawingContext>(drawingContext);

        SetPrivateField(window, "_drawingContext", drawingContext);
        InvokePrivateMethod(window, "ReleaseDrawingContextBeforeRenderTargetDisposal", true);
        Assert.Null(GetPrivateField<RenderTargetDrawingContext?>(window, "_drawingContext"));

        return weakContext;
    }

    private static void ForceFinalizers()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class CloseOnRenderWindow : Window
    {
        public void InvokeOnRender(RenderTarget renderTarget) => OnRender(renderTarget);

        protected override void OnRender(RenderTarget renderTarget) => Close();
    }

    private sealed class CloseOnMeasureElement : FrameworkElement
    {
        private readonly Window _window;

        public CloseOnMeasureElement(Window window) => _window = window;

        public bool CloseCalled { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            CloseCalled = true;
            _window.Close();
            return new Size(1, 1);
        }
    }
}
