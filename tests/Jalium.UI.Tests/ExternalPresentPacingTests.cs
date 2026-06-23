using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Locks the present-credit contract of the event-driven swap scheduler
/// (external present pacing — see docs/present-pacing-design.md):
/// <list type="bullet">
/// <item>a credit is consumed by a successful BeginDraw and ONLY then;</item>
/// <item>a BeginDraw failure returns the credit (the frame spent no present slot);</item>
/// <item>a credit miss defers — it must NOT fall back to the 1 ms busy-retry
/// timer whose 16 ms native waits the scheduler exists to eliminate;</item>
/// <item>the recoverable-failure funnel returns the credit so a mid-frame
/// failure can never strand the scheduler waiting on a DWM retire that will
/// never come (credit conservation).</item>
/// </list>
/// Pacing state is injected by reflection: activating it through
/// StartExternalPresentPacingIfSupported needs a real DXGI frame-latency
/// waitable, which headless test targets (RenderTargetTestNative) don't have.
/// </summary>
/// <remarks>
/// [Collection("Application")] is mandatory: these tests construct Window
/// instances, and doing that in parallel with other Window-constructing test
/// classes races DependencyProperty metadata registration (project memory:
/// "测试须 [Collection(\"Application\")] 避 DP-metadata race").
/// </remarks>
[Collection("Application")]
public class ExternalPresentPacingTests
{
    [Fact]
    public void TryBeginDraw_WithCredit_ConsumesCreditAndBegins()
    {
        var (window, native) = CreatePacedWindow(beginDrawResult: (int)JaliumResult.Ok);
        SetSwapCredit(window, 1);

        bool began = InvokeTryBeginDrawOrScheduleRetry(window);

        Assert.True(began);
        Assert.Equal(0, GetSwapCredit(window));
    }

    [Fact]
    public void TryBeginDraw_BeginFails_ReturnsCredit()
    {
        // InvalidState = fence/device busy. The frame consumed no present slot,
        // so the credit must come back for the retry instead of leaking.
        var (window, native) = CreatePacedWindow(beginDrawResult: (int)JaliumResult.InvalidState);
        SetSwapCredit(window, 1);

        bool began = InvokeTryBeginDrawOrScheduleRetry(window);

        Assert.False(began);
        Assert.Equal(1, GetSwapCredit(window));
    }

    [Fact]
    public void TryBeginDraw_CreditMiss_DefersWithoutBusyRetryTimer()
    {
        var (window, native) = CreatePacedWindow(beginDrawResult: (int)JaliumResult.Ok);
        SetSwapCredit(window, 0);

        bool began = InvokeTryBeginDrawOrScheduleRetry(window);

        Assert.False(began);
        Assert.Equal(0, GetSwapCredit(window));
        // The whole point of the scheduler: a miss waits for the swap signal,
        // it does not re-arm the 1 ms timer loop (each lap of which blocked the
        // UI thread ~16 ms inside the native waitable wait).
        Assert.Null(GetPrivateField(window, "_renderThrottleTimer"));
    }

    [Fact]
    public void RecoverableFailureFunnel_ReturnsCredit()
    {
        // HandleRecoverableRenderPipelineFailure is the single funnel all
        // recoverable mid-frame failures pass through (the RenderFrame inner
        // catches return via it without reaching the outer catch). It must
        // return the consumed credit — a deferred recovery does not rebuild
        // the render target, so nothing else would.
        var (window, native) = CreatePacedWindow(beginDrawResult: (int)JaliumResult.Ok);
        SetSwapCredit(window, 0);   // consumed by a hypothetical BeginDraw

        var exception = new RenderPipelineException(
            stage: "End",
            result: JaliumResult.DeviceLost,
            resultCode: (int)JaliumResult.DeviceLost,
            hwnd: nint.Zero,
            width: 300,
            height: 200,
            dpiX: 96f,
            dpiY: 96f,
            backend: nameof(RenderBackend.D3D12));

        var method = typeof(Window).GetMethod(
            "HandleRecoverableRenderPipelineFailure",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try
        {
            method!.Invoke(window, new object[] { exception, "Test" });
        }
        catch (TargetInvocationException)
        {
            // Headless recovery scheduling may fail (no dispatcher) — the
            // credit return happens at the funnel entry, before any of that.
        }

        Assert.Equal(1, GetSwapCredit(window));
    }

    [Fact]
    public void EndDraw_TransientPresentFailure_ReturnsCreditWithoutRebuildOrThrow()
    {
        // Native reports a transient Present failure as PresentFailed (instead
        // of swallowing it as Ok) only under external pacing — a failed Present
        // never signals the frame-latency waitable, so the credit consumed at
        // BeginDraw MUST come back through the unified non-OK EndDraw path or
        // the event-driven scheduler starves until the 500 ms heartbeat.
        var (window, native) = CreatePacedWindow(beginDrawResult: (int)JaliumResult.Ok);
        native.EndDrawResult = (int)JaliumResult.PresentFailed;
        var renderTarget = (RenderTarget)GetPrivateProperty(window, "RenderTarget")!;

        Assert.True(renderTarget.TryBeginDraw());   // opens the session so TryEndDraw reaches the seam
        SetSwapCredit(window, 0);                   // the open frame holds the consumed credit

        bool presented = (bool)InvokePrivateMethod(window, "CompleteEndDrawOrHandleFailure")!;

        Assert.False(presented);
        Assert.Equal(1, GetSwapCredit(window));
        // A dropped present is NOT a device failure: the render target must
        // survive untouched (no dispose/rebuild) and no recovery may be queued.
        Assert.Same(renderTarget, GetPrivateProperty(window, "RenderTarget"));
        Assert.False((bool)GetPrivateField(window, "_renderRecoveryInProgress")!);
        Assert.Equal(0, (int)GetPrivateField(window, "_consecutiveRecoverableRenderFailures")!);
    }

    [Fact]
    public void PresentFailed_IsNotClassifiedRecoverable()
    {
        // Guards the classification that keeps a dropped present OFF the
        // render-target rebuild path: IsRecoverableRenderPipelineFailure gates
        // TryRecoverFromRenderPipelineFailure (dispose + recreate), which would
        // turn a one-frame hiccup into a visible reconstruction stall.
        var method = typeof(Window).GetMethod(
            "IsRecoverableRenderPipelineFailure",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.False((bool)method!.Invoke(null, new object[] { JaliumResult.PresentFailed, "End" })!);
        // Sanity: the classification still recovers genuine device failures.
        Assert.True((bool)method.Invoke(null, new object[] { JaliumResult.DeviceLost, "End" })!);
    }

    [Fact]
    public void ResultMapper_MapsPresentFailedNativeCode()
    {
        // Locks the managed enum to the native ABI value (JALIUM_ERROR_PRESENT_FAILED = 9).
        // An unmapped code degrades to Unknown, which stage "End" classifies
        // non-recoverable and surfaces as a thrown RenderPipelineException —
        // the exact crash-adjacent behavior PresentFailed exists to avoid.
        Assert.Equal(JaliumResult.PresentFailed, JaliumResultMapper.FromCode(9));
    }

    [Fact]
    public void PacingInactive_BeginFailure_UsesLegacyRetryPath()
    {
        // Without pacing the legacy contract holds: failure schedules the
        // 1 ms retry timer and the credit field stays untouched.
        var (window, native) = CreatePacedWindow(
            beginDrawResult: (int)JaliumResult.InvalidState, pacingActive: false);
        SetSwapCredit(window, 0);

        bool began = InvokeTryBeginDrawOrScheduleRetry(window);

        Assert.False(began);
        Assert.Equal(0, GetSwapCredit(window));
    }

    [Fact]
    public void StopExternalPresentPacing_IsIdempotent_AndReachesNativeSeam()
    {
        var (window, native) = CreatePacedWindow(beginDrawResult: (int)JaliumResult.Ok);

        var stop = typeof(Window).GetMethod(
            "StopExternalPresentPacing", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(stop);
        stop!.Invoke(window, null);
        stop.Invoke(window, null);   // double-stop must not throw

        Assert.False((bool)GetPrivateField(window, "_swapPacingActive")!);
        Assert.False(native.LastExternalPresentPacing);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (Window window, RenderTargetTestNative native) CreatePacedWindow(
        int beginDrawResult, bool pacingActive = true)
    {
        var window = new Window { Width = 300, Height = 200 };
        var native = new RenderTargetTestNative { BeginDrawResult = beginDrawResult };
        var renderTarget = new RenderTarget(
            backend: RenderBackend.D3D12,
            contextHandle: new nint(0x1234),
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x2001)),
            width: 300,
            height: 200,
            useComposition: false,
            native: native);
        SetPrivateProperty(window, "RenderTarget", renderTarget);
        SetPrivateField(window, "_swapPacingActive", pacingActive);
        return (window, native);
    }

    private static bool InvokeTryBeginDrawOrScheduleRetry(Window window)
    {
        var method = typeof(Window).GetMethod(
            "TryBeginDrawOrScheduleRetry", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try
        {
            return (bool)method!.Invoke(window, null)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void SetSwapCredit(Window window, int value)
        => SetPrivateField(window, "_swapCredit", value);

    private static int GetSwapCredit(Window window)
        => (int)GetPrivateField(window, "_swapCredit")!;

    private static object? GetPrivateField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(instance);
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void SetPrivateProperty(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static object? GetPrivateProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return property!.GetValue(instance);
    }

    private static object? InvokePrivateMethod(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
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
}
