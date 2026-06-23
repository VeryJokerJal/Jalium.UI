// Device-removal injection harness — runtime verification for the GPU-switch
// (mid-frame DEVICE_REMOVED) crash hardening.
//
// Run by DeviceRemovalInjectionTests as a child process: a hardening regression
// dies with an uncatchable AV / 0xC000041D that must not take the xunit test
// host down, and JALIUM_RENDER_THREAD is latched at process start so the
// render-thread variant needs its own process anyway.
//
// Scenarios (argv[0]):
//   midframe     — effect-heavy scene + retained layer; the device is removed
//                  inside Window.OnRender(RenderTarget), i.e. between BeginDraw
//                  and EndDraw; asserts recovery (RenderTarget replaced, layers
//                  drained through the old RT), layer re-realize on the new
//                  device, post-recovery stability, and a second incident
//                  (proves the failure counter reset — no backend downgrade).
//   capture      — the device is removed inside an element OnRender that runs
//                  within a BlurEffect offscreen-capture scope; asserts the
//                  capture CPU state unwinds and recovery still works twice.
//   multiwindow  — two windows sharing the device; staggered recovery, then
//                  drives BOTH DestroyRetainedLayer branches cross-window and
//                  asserts the orphan/graveyard discrimination counters.
//   escapedhandle— a retained layer that ESCAPES the recovery sweep (subtree
//                  detached at recovery time, reattached after) must be refused
//                  by the native cross-generation guard and then released by the
//                  managed escaped-handle path — not retained forever, not an AV.
//   renderthread — run with JALIUM_RENDER_THREAD=1; the injection fires on the
//                  render thread; asserts the injection really ran off the UI
//                  thread, exactly one marshaled recovery per incident
//                  (_recoveryMarshalPending latch), and no downgrade.
//
// Out of scope (verified statically / by other tests, NOT by this harness):
//   * recovery park-failure deferral (RequestRenderThreadIdle returning false):
//     RemoveDevice aborts the frame immediately, so the render thread is never
//     actually wedged in a long present — would need a fault hook inside Present.
//   * present-credit return on a DEFERRED recovery: only weakly covered (the
//     120s watchdog converts a leaked-credit deadlock into a TIMEOUT exit).
//   * PopupWindow / DockIndicatorWindow recovery mirrors: near-verbatim clones
//     of the Window path the midframe scenario already exercises.
//
// Exit codes: 0 = PASS, 1 = FAIL, 2 = SKIP (no D3D12), 3 = watchdog timeout.
// Progress and assertions are written to stdout as "## MARKER ..." lines that
// the xunit runner matches.

using System.Reflection;
using System.Runtime.InteropServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Threading;

namespace Jalium.UI.DeviceLostHarness;

internal static class Program
{
    private const int ExitPass = 0;
    private const int ExitFail = 1;
    private const int ExitSkip = 2;
    private const int ExitTimeout = 3;

    // P/Invoke the debug ABI directly instead of growing the framework's managed
    // API: the export is env-gated (JALIUM_DEBUG_DEVICE_REMOVE) and test-only.
    [DllImport("jalium.native.core", EntryPoint = "jalium_render_target_debug_remove_device")]
    internal static extern int DebugRemoveDevice(nint renderTarget);

    [DllImport("jalium.native.core", EntryPoint = "jalium_render_target_debug_retained_destroy_counts")]
    internal static extern int DebugRetainedDestroyCounts(nint renderTarget, out ulong orphaned, out ulong graveyard);

    [DllImport("jalium.native.core", EntryPoint = "jalium_render_target_debug_device_pointer")]
    internal static extern ulong DebugDevicePointer(nint renderTarget);

    [DllImport("jalium.native.core", EntryPoint = "jalium_render_target_destroy_retained_layer")]
    internal static extern void DestroyRetainedLayer(nint renderTarget, nint layer);

    [DllImport("jalium.native.core", EntryPoint = "jalium_render_target_debug_in_offscreen_capture")]
    internal static extern int DebugInOffscreenCapture(nint renderTarget);

    [DllImport("jalium.native.core", EntryPoint = "jalium_render_target_debug_force_leaked_command_list_resize")]
    internal static extern int DebugForceLeakedCommandListResize(nint renderTarget, int width, int height, out int listClosed);

    [DllImport("jalium.native.core", EntryPoint = "jalium_render_target_debug_force_vello_output_orphan")]
    internal static extern int DebugForceVelloOutputOrphan(nint renderTarget, out int alive);

    private static readonly FieldInfo CachedLayerField =
        typeof(Visual).GetField("_cachedLayer", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("Visual._cachedLayer");

    // Captured on the UI thread at startup so the renderthread scenario can prove
    // the injection actually ran off it (an inline fallback would inject here).
    internal static int UiThreadId;

    private static IEnumerator<object?> _script = null!;
    private static DispatcherTimer _pump = null!;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // markers cross a redirected pipe
        UiThreadId = Environment.CurrentManagedThreadId;

        string scenario = args.Length > 0 ? args[0].ToLowerInvariant() : "midframe";
        Console.WriteLine(
            $"## HARNESS start scenario={scenario} pid={Environment.ProcessId} " +
            $"renderThreadEnv={Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD") ?? "<null>"}");
        Console.Out.Flush();

        if (Environment.GetEnvironmentVariable("JALIUM_DEBUG_DEVICE_REMOVE") != "1")
        {
            Console.WriteLine("## RESULT FAIL JALIUM_DEBUG_DEVICE_REMOVE is not 1 — the injection ABI would be inert");
            return ExitFail;
        }

        // The scenario scripts have generous per-step budgets; if the dispatcher
        // wedges (the exact failure a deadlock regression produces) nothing
        // would ever fail an assertion, so a hard watchdog converts "stuck"
        // into a distinct exit code.
        var watchdog = new Thread(static () =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(120));
            Console.WriteLine("## RESULT TIMEOUT watchdog (120s) — dispatcher wedged or scenario stuck");
            Console.Out.Flush();
            Environment.Exit(ExitTimeout);
        })
        { IsBackground = true, Name = "harness-watchdog" };
        watchdog.Start();

        try
        {
            RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        }
        catch (Exception ex)
        {
            Console.WriteLine("## RESULT SKIP cannot create a D3D12 render context: " + ex.Message);
            return ExitSkip;
        }

        // Install the retained-mode cache host BEFORE Show. It is otherwise
        // bootstrapped lazily by RenderTargetDrawingContext's static ctor on the
        // FIRST frame — which is AFTER Show's StartRenderThreadIfSupported has
        // already run and bailed (it requires Visual.RenderCacheHost != null).
        // Without this the render thread only starts on the first recovery, so
        // the renderthread scenario's first incident would silently run inline.
        Jalium.UI.Media.Rendering.MediaRenderCacheHost.Bootstrap();

        var app = new Application();

        bool withProbe = scenario == "capture";
        var scene = SceneBuilder.Build(withProbe);
        var window = new HarnessWindow
        {
            DisplayName = "A",
            Title = $"DeviceLost harness — {scenario}",
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 520,
            Height = 360,
            Content = scene.Root,
        };

        _script = scenario switch
        {
            "midframe" => MidframeScript(window, scene),
            "capture" => CaptureScript(window, scene),
            "multiwindow" => MultiwindowScript(window, scene),
            "escapedhandle" => EscapedHandleScript(window, scene),
            "renderthread" => RenderThreadScript(window, scene),
            "leakedresize" => LeakedResizeScript(window, scene),
            "vellooutputorphan" => VelloOutputOrphanScript(window, scene),
            _ => FailScript($"unknown scenario '{scenario}'"),
        };

        _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        _pump.Tick += OnPumpTick;
        _pump.Start();

        app.Run(window);

        // The scripts terminate the process via Pass()/Fail(); falling out of
        // the message loop means the window closed underneath the scenario.
        Console.WriteLine("## RESULT FAIL window closed before the scenario completed");
        return ExitFail;
    }

    private static void OnPumpTick(object? sender, EventArgs e)
    {
        try
        {
            if (!_script.MoveNext())
            {
                Pass();
            }
        }
        catch (HarnessAssertException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Fail("unhandled scenario exception: " + ex);
        }
    }

    // ── scenario scripts ────────────────────────────────────────────────────
    // Iterator-based: each `yield return null` waits one pump tick (~10 ms),
    // which also lets the window's own dispatcher timers (recovery retry,
    // marshaled recovery callbacks) run in between.

    private static IEnumerator<object?> MidframeScript(HarnessWindow window, Scene scene)
    {
        foreach (var x in WaitForFirstFrame(window)) yield return x;
        foreach (var x in Warmup(window, 3)) yield return x;

        foreach (var x in RealizeLayer(window, scene, "initial")) yield return x;
        nint layer1 = GetCachedLayer(scene.Animated);

        ReadCounters(window, out ulong o0, out ulong g0);
        Marker($"COUNTS before o={o0} g={g0}");

        foreach (var x in InjectAndRecover(window, scene, "round1")) yield return x;

        AssertTrue(GetCachedLayer(scene.Animated) == 0,
            "recovery cleared the retained layer (_cachedLayer == 0)");
        ReadCounters(window, out ulong o1, out ulong g1);
        Marker($"COUNTS afterRecovery o={o1} g={g1}");
        AssertTrue(g1 > g0,
            $"recovery drained the layer through the OLD render target's fence graveyard (graveyard {g0} -> {g1})");

        foreach (var x in RealizeLayer(window, scene, "re-realize")) yield return x;
        nint layer2 = GetCachedLayer(scene.Animated);
        // _cachedLayer==0 after recovery (asserted above) + a fresh non-zero
        // realize here together prove re-realization on the new device. We do
        // NOT assert layer2 != layer1: layer1 was deleted during recovery and
        // the allocator may legitimately hand back the same address, which would
        // flake a pointer-inequality check without indicating any real problem.
        AssertTrue(layer2 != 0,
            $"layer re-realized on the new device (old=0x{layer1:x} new=0x{layer2:x})");
        Marker($"LAYER_REREALIZED old=0x{layer1:x} new=0x{layer2:x}");

        foreach (var x in WaitForBackoffReset(window)) yield return x;
        AssertBackendStillD3D12(window);
        foreach (var x in StableFrames(window, scene, 20)) yield return x;

        // Second incident: ResetRenderRecoveryBackoff must have run, so this is
        // again a single-failure recovery — DeviceLostBackendFallbackThreshold
        // (2) must NOT be reached and the window must stay on D3D12.
        foreach (var x in InjectAndRecover(window, scene, "round2")) yield return x;
        foreach (var x in WaitForBackoffReset(window)) yield return x;
        AssertBackendStillD3D12(window);
        foreach (var x in StableFrames(window, scene, 10)) yield return x;
        Marker("SCENARIO midframe complete");
    }

    // #921 same-thread leaked-open command-list resize self-check. The cross-thread
    // BUSY/defer half of #921 is covered by a managed seam test
    // (RenderTargetFailureTests.Resize_WhenNativeReportsBusy_...); this scenario
    // covers the SAME-THREAD branch, which needs a real device + HWND swap chain
    // and the internal "list open while isDrawing_==0" race that is unreachable
    // through the public C ABI — hence the env-gated native debug hook.
    private static IEnumerator<object?> LeakedResizeScript(HarnessWindow window, Scene scene)
    {
        foreach (var x in WaitForFirstFrame(window)) yield return x;
        foreach (var x in Warmup(window, 5)) yield return x;

        var rt = window.RenderTarget;
        AssertTrue(rt is not null, "render target present before leaked-resize");

        int w0 = rt!.Width;
        int h0 = rt.Height;
        AssertTrue(w0 > 0 && h0 > 0, $"render target has a positive size ({w0}x{h0})");
        Marker($"LEAKED_RESIZE before size={w0}x{h0}");

        // Stage the #921 same-thread leaked-open command list and resize through
        // it. The native hook opens the command list exactly as BeginDraw does but
        // WITHOUT marking the target drawing (the BeginFrame open-gap), then does a
        // same-thread Resize to a deliberately different size. The same-thread guard
        // MUST AbortFrame (Close the leaked list) BEFORE the back buffers it
        // references are freed — otherwise #921 OBJECT_DELETED_WHILE_STILL_IN_USE.
        int listClosed = -1;
        int result = -100;
        bool staged = false;
        for (int i = 0; i < 60 && !staged; i++)
        {
            result = Program.DebugForceLeakedCommandListResize(rt.Handle, w0 + 64, h0 + 48, out listClosed);
            if (result == -4)
            {
                // BeginFrame could not open the list yet (fence/device settling) —
                // pump a real frame and retry rather than failing the stage.
                window.ForceRenderFrame();
                yield return null;
                continue;
            }
            staged = true;
        }
        AssertTrue(staged,
            $"leaked-open command list could be staged (BeginFrame opened it; last result={result})");
        Marker($"LEAKED_RESIZE result={result} listClosed={listClosed}");

        // (c) The same-thread leaked-open resize is a genuine successful resize
        // (JALIUM_OK == 0), NOT the cross-thread BUSY defer (10).
        AssertTrue(result == 0,
            $"same-thread leaked-open Resize returned JALIUM_OK (result={result})");
        // (b) The leaked command list was Closed by the guard before the back
        // buffers were freed — the #921 fix. A regression that dropped the
        // AbortFrame leaves the list open and this reads 0.
        AssertTrue(listClosed == 1,
            $"leaked command list was Closed before back buffers freed (listClosed={listClosed})");

        // The native hook resized the swap chain to a deliberately different size
        // (so Resize would actually run the guard rather than no-op) BEHIND the
        // managed window's back. Resize it back to the window's size so the
        // stability frames below render against a matched swap chain instead of
        // driving a size-mismatch recovery that has nothing to do with #921.
        int resync = (int)rt.Resize(w0, h0);
        AssertTrue(resync == 0, $"re-sync resize back to {w0}x{h0} returned Ok (got {resync})");

        // (a) No OBJECT_DELETED_WHILE_STILL_IN_USE / UAF: proven by survival — keep
        // rendering on the resized-and-restored target. A single transient Window-
        // level recovery on the FIRST frame after the resize is tolerated: it is the
        // window's own resilience (observed ~1 per run, always fails=1, rebuilt on
        // the SAME D3D12 device with override=Auto — no downgrade), provoked by this
        // harness driving Resize from outside the normal draw loop, NOT a #921
        // symptom — the #921 assertions above already passed deterministically. What
        // must hold is that the window keeps rendering and stays on D3D12 rather than
        // crashing or downgrading, which AssertBackendStillD3D12 enforces.
        for (int i = 0; i < 15; i++)
        {
            scene.Animated.Opacity = 0.85 + 0.1 * Math.Abs(Math.Sin(i * 0.4));
            window.ForceRenderFrame();
            yield return null;
        }
        AssertBackendStillD3D12(window);
        Marker("SCENARIO leakedresize complete");
    }

    // TEST-ONLY (#921 Vello-output regression self-check). Drives the in-process
    // 'JaliumVelloOutput' orphan: the native hook composites the Vello output texture
    // into the bitmap keep-alive list, calls ForceNewOutputTexture(), then runs the
    // mid-frame FlushGraphicsForCompute() clear — all with the command list open. The
    // fix parks the texture on the fence-gated retired list so it survives the clear;
    // a regression (bare outputTexture_.Reset()) frees it while still referenced
    // (#921 OBJECT_DELETED_WHILE_STILL_IN_USE). The survival is observed WITHOUT the
    // debug layer (alive==1), the same way leakedresize asserts listClosed==1.
    private static IEnumerator<object?> VelloOutputOrphanScript(HarnessWindow window, Scene scene)
    {
        foreach (var x in WaitForFirstFrame(window)) yield return x;
        foreach (var x in Warmup(window, 5)) yield return x;

        var rt = window.RenderTarget;
        AssertTrue(rt is not null, "render target present before vello-output-orphan");

        // Stage the orphan. The native hook opens the command list exactly as BeginDraw
        // does (without marking the target drawing — the #921 open-gap), forces the
        // Vello path on, dispatches + composites + ForceNewOutputTexture, then clears
        // the bitmap keep-alive via a mid-frame FlushGraphicsForCompute. result==-4 is
        // the BeginFrame settling case (pump a real frame and retry, like leakedresize).
        int alive = -1;
        int result = -100;
        bool staged = false;
        for (int i = 0; i < 60 && !staged; i++)
        {
            result = Program.DebugForceVelloOutputOrphan(rt!.Handle, out alive);
            if (result == -4)
            {
                window.ForceRenderFrame();
                yield return null;
                continue;
            }
            staged = true;
        }
        AssertTrue(staged,
            $"vello-output-orphan could be staged (BeginFrame opened the list; last result={result})");
        Marker($"VELLO_ORPHAN result={result} alive={alive}");

        AssertTrue(result == 0,
            $"vello-output-orphan staged with JALIUM_OK (result={result})");
        // The decisive #921 assertion: 'JaliumVelloOutput' survived the mid-frame
        // bitmapTextures_.clear() because RetireOutputTexture parked it on the
        // fence-gated retired list. A regression (bare Reset) frees it -> alive==0.
        AssertTrue(alive == 1,
            $"JaliumVelloOutput retired on the fence-gated list, not freed while the open command list referenced it (#921) (alive={alive})");

        // Survival/stability: keep rendering real frames; the target must stay healthy
        // and on D3D12 (AssertBackendStillD3D12), not crash or downgrade.
        for (int i = 0; i < 15; i++)
        {
            scene.Animated.Opacity = 0.85 + 0.1 * Math.Abs(Math.Sin(i * 0.4));
            window.ForceRenderFrame();
            yield return null;
        }
        AssertBackendStillD3D12(window);
        Marker("SCENARIO vellooutputorphan complete");
    }

    private static IEnumerator<object?> CaptureScript(HarnessWindow window, Scene scene)
    {
        foreach (var x in WaitForFirstFrame(window)) yield return x;
        foreach (var x in Warmup(window, 3)) yield return x;

        var probe = scene.Probe!;
        probe.ResolveRenderTargetHandle = () => window.RenderTarget?.Handle ?? 0;

        for (int round = 1; round <= 2; round++)
        {
            var rtBefore = window.RenderTarget;
            AssertTrue(rtBefore is not null, "render target present before capture injection");
            int firedBefore = Volatile.Read(ref probe.InjectionsFired);
            probe.ArmInjection();

            bool fired = false;
            for (int i = 0; i < 400 && !fired; i++)
            {
                probe.InvalidateVisual();
                window.ForceRenderFrame();
                fired = Volatile.Read(ref probe.InjectionsFired) > firedBefore;
                if (!fired) yield return null;
            }
            AssertTrue(fired, $"capture-scope injection fired (round {round})");
            AssertTrue(probe.LastInjectionResult == 1,
                $"debug_remove_device returned {probe.LastInjectionResult} inside the effect capture (round {round})");
            AssertTrue(probe.WasInsideCapture == 1,
                $"device was removed INSIDE an open offscreen capture (insideCapture={probe.WasInsideCapture}, round {round}) — " +
                "otherwise this scenario is not exercising the capture-unwind path it claims to");

            for (int i = 0; i < 600; i++)
            {
                var rt = window.RenderTarget;
                if (rt is not null && !ReferenceEquals(rt, rtBefore))
                {
                    Marker($"RECOVERED tag=capture{round} rtReplaced=true");
                    break;
                }
                AssertTrue(i < 599, $"render target replaced after capture injection (round {round})");
                yield return null;
            }

            foreach (var x in WaitForBackoffReset(window)) yield return x;
            AssertBackendStillD3D12(window);
            // The probe firing again next round also proves the effect-capture
            // path is still being walked (a wedged inOffscreenCapture_ flag
            // would have been the old failure mode).
            foreach (var x in StableFrames(window, scene, 15)) yield return x;
        }

        Marker("SCENARIO capture complete");
    }

    private static IEnumerator<object?> MultiwindowScript(HarnessWindow windowA, Scene sceneA)
    {
        foreach (var x in WaitForFirstFrame(windowA)) yield return x;
        foreach (var x in Warmup(windowA, 3)) yield return x;

        var sceneB = SceneBuilder.Build(withProbe: false);
        var windowB = new HarnessWindow
        {
            DisplayName = "B",
            Title = "DeviceLost harness — multiwindow B",
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 420,
            Height = 300,
            Content = sceneB.Root,
        };
        windowB.Show();
        foreach (var x in Warmup(windowB, 3)) yield return x;

        foreach (var x in RealizeLayer(windowA, sceneA, "A-initial")) yield return x;
        foreach (var x in RealizeLayer(windowB, sceneB, "B-initial")) yield return x;

        // ── Phase 1: staggered recovery off one shared-device removal ──
        var rtB0 = windowB.RenderTarget;
        foreach (var x in InjectAndRecover(windowA, sceneA, "A-staggered")) yield return x;
        Marker("STAGGERED_A_RECOVERED");

        // B still sits on the removed device; its next frame must classify as
        // DeviceLost and recover onto its own fresh context.
        bool bRecovered = false;
        for (int i = 0; i < 600 && !bRecovered; i++)
        {
            sceneB.Animated.Opacity = 0.93;
            windowB.ForceRenderFrame();
            var rt = windowB.RenderTarget;
            bRecovered = rt is not null && !ReferenceEquals(rt, rtB0);
            if (!bRecovered) yield return null;
        }
        AssertTrue(bRecovered, "window B recovered onto a new render target after window A's staggered recovery");
        Marker("STAGGERED_B_RECOVERED");

        foreach (var x in WaitForBackoffReset(windowA)) yield return x;
        foreach (var x in WaitForBackoffReset(windowB)) yield return x;
        AssertBackendStillD3D12(windowA);
        AssertBackendStillD3D12(windowB);

        foreach (var x in RealizeLayer(windowA, sceneA, "A-postrecovery")) yield return x;
        foreach (var x in RealizeLayer(windowB, sceneB, "B-postrecovery")) yield return x;

        // A and B now live on two DIFFERENT but both-healthy devices — the
        // precondition for the cross-device discrimination. Prove it.
        var rtA = windowA.RenderTarget!;
        var rtB = windowB.RenderTarget!;
        ulong devA = DebugDevicePointer(rtA.Handle);
        ulong devB = DebugDevicePointer(rtB.Handle);
        AssertTrue(devA != 0 && devB != 0, "both windows report a device pointer");
        // A hard assertion, NOT a skip: GetOrCreateCurrent(forceReplace:true)
        // unconditionally builds a fresh context/device per recovery, so two
        // staggered recoveries MUST land on different devices. Skipping here
        // would let a regression that reuses one device hide the orphan-vs-
        // graveyard discrimination as a perpetual green skip.
        AssertTrue(devA != devB,
            $"staggered recovery put A and B on DIFFERENT devices (devA=0x{devA:x} devB=0x{devB:x}) — " +
            "same device means forceReplace stopped making fresh contexts");
        Marker($"CROSS_DEVICE devA=0x{devA:x} devB=0x{devB:x}");

        // The two destroy branches are otherwise unobservable, so drive them
        // directly: take a layer A realized (created on A's device) and destroy
        // it through B's render target (B's device). The native
        // DestroyRetainedLayer then sees a foreign creating device and chooses
        // orphan vs. graveyard purely on whether that device was removed.

        // ── Phase 2: foreign + HEALTHY creating device -> fence-gated graveyard ──
        foreach (var x in RealizeLayer(windowA, sceneA, "A-healthy-setup")) yield return x;
        nint healthyLayer = StealCachedLayer(sceneA.Animated); // A no longer owns it
        AssertTrue(healthyLayer != 0, "stole a realized layer handle from A (healthy)");
        ReadCounters(windowB, out ulong o2, out ulong g2);
        DestroyRetainedLayer(rtB.Handle, healthyLayer); // foreign(A) + healthy -> graveyard
        ReadCounters(windowB, out ulong o3, out ulong g3);
        AssertTrue(o3 == o2, $"healthy foreign-device layer must NOT be orphaned (orphan {o2} -> {o3})");
        AssertTrue(g3 == g2 + 1, $"healthy foreign-device layer took the fence-gated graveyard exactly once (graveyard {g2} -> {g3})");
        Marker($"CROSS_HEALTHY_GRAVEYARD o={o3} g={g3}");

        // ── Phase 3: foreign + REMOVED creating device -> orphan ──
        foreach (var x in RealizeLayer(windowA, sceneA, "A-orphan-setup")) yield return x;
        nint orphanLayer = StealCachedLayer(sceneA.Animated);
        AssertTrue(orphanLayer != 0, "stole a realized layer handle from A (to-be-orphaned)");

        int ok = DebugRemoveDevice(rtA.Handle); // remove ONLY A's device (B's stays healthy)
        AssertTrue(ok == 1, $"out-of-frame debug_remove_device on A returned {ok}");
        Marker("INJECTED window=A outOfFrame=true");

        ReadCounters(windowB, out ulong o4, out ulong g4);
        DestroyRetainedLayer(rtB.Handle, orphanLayer); // foreign(A) + removed -> orphan
        ReadCounters(windowB, out ulong o5, out ulong g5);
        AssertTrue(o5 == o4 + 1, $"removed foreign-device layer orphaned exactly once (orphan {o4} -> {o5})");
        AssertTrue(g5 == g4, $"removed foreign-device layer did NOT take the graveyard (graveyard {g4} -> {g5})");
        Marker($"CROSS_REMOVED_ORPHAN o={o5} g={g5}");

        // A's next frame classifies the removal and recovers again.
        var rtA1 = rtA;
        bool aRecovered = false;
        for (int i = 0; i < 600 && !aRecovered; i++)
        {
            sceneA.Animated.Opacity = 0.92;
            windowA.ForceRenderFrame();
            var rt = windowA.RenderTarget;
            aRecovered = rt is not null && !ReferenceEquals(rt, rtA1);
            if (!aRecovered) yield return null;
        }
        AssertTrue(aRecovered, "window A recovered from the out-of-frame removal");
        Marker("A_RECOVERED_AGAIN");

        foreach (var x in WaitForBackoffReset(windowA)) yield return x;
        AssertBackendStillD3D12(windowA);
        AssertBackendStillD3D12(windowB);
        foreach (var x in StableFrames(windowA, sceneA, 10)) yield return x;
        foreach (var x in StableFrames(windowB, sceneB, 10)) yield return x;
        Marker("SCENARIO multiwindow complete");
    }

    private static IEnumerator<object?> EscapedHandleScript(HarnessWindow window, Scene scene)
    {
        foreach (var x in WaitForFirstFrame(window)) yield return x;
        foreach (var x in Warmup(window, 3)) yield return x;

        var root = (Panel)scene.Root;
        foreach (var x in RealizeLayer(window, scene, "escaped-setup")) yield return x;
        nint escaped = GetCachedLayer(scene.Animated);
        AssertTrue(escaped != 0, "animated subtree realized a layer before detach");

        // Detach the subtree so the recovery sweep (ReleaseRetainedLayersRecursive,
        // which walks the WINDOW tree) cannot reach it: its _cachedLayer stays set
        // but points at the about-to-be-removed device. This is the inverse of
        // StealCachedLayer — the handle keeps living in _cachedLayer but leaves
        // the tree, which is exactly how a real escaped handle arises.
        root.Children.Remove(scene.Animated);
        AssertTrue(GetCachedLayer(scene.Animated) == escaped,
            "detached subtree kept its stale-device layer handle (escaped the tree)");

        ReadCounters(window, out ulong o0, out ulong g0);

        // Remove the device mid-frame and recover. The sweep cannot touch the
        // detached subtree, so `escaped` is now a stale-device handle the window
        // no longer knows about.
        foreach (var x in InjectAndRecover(window, scene, "escaped")) yield return x;
        AssertTrue(GetCachedLayer(scene.Animated) == escaped,
            "escaped layer survived the recovery sweep (still cached, points at the removed device)");

        // Reattach. On a content-clean eligible frame the parent tries to realize
        // through the cached handle; the native generation guard
        // (BeginRetainedLayerCapture: layer->Device() != device_) refuses it
        // FOREVER, and the managed escaped-handle path (Visual.cs:858-869) must
        // release it rather than leak the wrapper / pin the dead device.
        root.Children.Add(scene.Animated);
        bool released = false;
        for (int i = 0; i < 90 && !released; i++)
        {
            scene.Animated.Opacity = 0.95 - (i % 8) * 0.005;
            window.ForceRenderFrame();
            if (GetCachedLayer(scene.Animated) == 0) released = true;
            else yield return null;
        }
        AssertTrue(released,
            "escaped stale-device layer was RELEASED after the native guard refused it (not retained forever / leaked)");
        Marker("ESCAPED_HANDLE_RELEASED");

        // The released handle was enqueued; the next frame-start drain destroys
        // it through the NEW device's RT — foreign + removed creating device ->
        // the orphan branch (the graveyard would be a use-after-free here).
        window.ForceRenderFrame();
        window.ForceRenderFrame();
        ReadCounters(window, out ulong o1, out ulong g1);
        AssertTrue(o1 == o0 + 1,
            $"escaped handle took the ORPHAN branch (foreign + removed creating device) (orphan {o0} -> {o1})");
        Marker($"ESCAPED_HANDLE_ORPHANED o={o1} g={g1}");

        foreach (var x in WaitForBackoffReset(window)) yield return x;
        AssertBackendStillD3D12(window);
        foreach (var x in StableFrames(window, scene, 10)) yield return x;
        Marker("SCENARIO escapedhandle complete");
    }

    private static IEnumerator<object?> RenderThreadScript(HarnessWindow window, Scene scene)
    {
        AssertTrue(Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD") == "1",
            "JALIUM_RENDER_THREAD=1 must be set for the renderthread scenario");
        var enableField = typeof(Window).GetField("EnableRenderThread", BindingFlags.Static | BindingFlags.NonPublic);
        AssertTrue(enableField is not null && (bool)enableField.GetValue(null)!,
            "Window.EnableRenderThread latched true (env reached the process before type init)");

        foreach (var x in WaitForFirstFrame(window)) yield return x;
        foreach (var x in Warmup(window, 5)) yield return x;

        // The static flag being on is NOT enough: StartRenderThreadIfSupported
        // bails for composition windows / missing waitable, and RenderFrame then
        // silently takes the inline branch. Without this, an inline fallback
        // would pass every marshal-latch assertion below WITHOUT ever exercising
        // the render thread — the whole point of this scenario. Poll: the thread
        // starts during Show but may take a published frame to flip _rtActive.
        bool rtActive = false;
        for (int i = 0; i < 90 && !rtActive; i++)
        {
            window.ForceRenderFrame();
            rtActive = GetField<bool>(window, "_rtActive");
            bool hasThread = GetField<object?>(window, "_renderThread") is not null;
            if (i == 0 || i == 89 || rtActive) Marker($"RT_STATE _rtActive={rtActive} hasThread={hasThread} frame={i}");
            if (!rtActive) yield return null;
        }
        AssertTrue(rtActive,
            "render thread is actually active (_rtActive) — not the inline fallback");

        for (int round = 1; round <= 2; round++)
        {
            var seen = new HashSet<RenderTarget>(ReferenceEqualityComparer.Instance);
            var rtBefore = window.RenderTarget;
            AssertTrue(rtBefore is not null, $"render target present before injection (round {round})");
            seen.Add(rtBefore!);

            int firedBefore = Volatile.Read(ref window.InjectionsFired);
            window.ArmInjection();

            bool fired = false;
            for (int i = 0; i < 400 && !fired; i++)
            {
                scene.Animated.Opacity = 0.93;
                window.ForceRenderFrame();
                fired = Volatile.Read(ref window.InjectionsFired) > firedBefore;
                if (!fired) yield return null;
            }
            AssertTrue(fired, $"render-thread injection fired (round {round})");
            AssertTrue(window.LastInjectionResult == 1,
                $"debug_remove_device returned {window.LastInjectionResult} on the render thread (round {round})");
            // The decisive proof this is the render-thread path: the injection
            // (fired from OnRender, between BeginDraw and EndDraw) ran on a
            // thread OTHER than the UI thread. An inline fallback would fire it
            // on UiThreadId and the marshal latch would never be involved.
            AssertTrue(window.InjectionThreadId != Program.UiThreadId && window.InjectionThreadId > 0,
                $"injection ran OFF the UI thread (injection thread {window.InjectionThreadId}, UI thread {Program.UiThreadId}, round {round})");

            // The recovery is marshaled to the UI thread; track every render
            // target instance we observe. A broken _recoveryMarshalPending
            // latch produces a SECOND recovery that disposes the first rebuilt
            // target — i.e. a third instance. Observe a FIXED window AFTER the
            // swap is first seen (an absolute counter could collapse the post-
            // swap budget to zero and miss a late second marshal).
            bool replaced = false;
            int swapIter = -1;
            for (int i = 0; i < 1000; i++)
            {
                var rt = window.RenderTarget;
                if (rt is not null)
                {
                    seen.Add(rt);
                    if (!ReferenceEquals(rt, rtBefore) && swapIter < 0) { replaced = true; swapIter = i; }
                }
                if (swapIter >= 0 && i - swapIter > 150) break; // ~1.5s of observation AFTER the swap
                yield return null;
            }
            AssertTrue(replaced, $"render target replaced after render-thread injection (round {round})");
            AssertTrue(seen.Count == 2,
                $"exactly one recovery per incident — {seen.Count} distinct render targets observed (round {round})");
            int marshalPending = GetIntField(window, "_recoveryMarshalPending");
            AssertTrue(marshalPending == 0, $"_recoveryMarshalPending released (actual {marshalPending})");
            Marker($"SINGLE_RECOVERY round={round} instances={seen.Count}");

            foreach (var x in WaitForBackoffReset(window)) yield return x;
            AssertBackendStillD3D12(window);
            foreach (var x in StableFrames(window, scene, 20)) yield return x;
        }

        Marker("SCENARIO renderthread complete");
    }

    private static IEnumerator<object?> FailScript(string reason)
    {
        throw new HarnessAssertException(reason);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    // ── script building blocks ──────────────────────────────────────────────

    private static IEnumerable<object?> WaitForFirstFrame(HarnessWindow window)
    {
        for (int i = 0; i < 600; i++)
        {
            var rt = window.RenderTarget;
            if (rt is not null)
            {
                if (rt.Backend != RenderBackend.D3D12)
                {
                    Console.WriteLine($"## RESULT SKIP window came up on {rt.Backend}, not D3D12 — no real-GPU coverage here");
                    Console.Out.Flush();
                    Environment.Exit(ExitSkip);
                }
                Marker($"FIRST_FRAME window={window.DisplayName} backend={rt.Backend}");
                yield break;
            }
            yield return null;
        }
        throw new HarnessAssertException("window never produced a render target");
    }

    private static IEnumerable<object?> Warmup(HarnessWindow window, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            window.ForceRenderFrame();
            yield return null;
        }
    }

    private static IEnumerable<object?> RealizeLayer(HarnessWindow window, Scene scene, string tag)
    {
        for (int i = 0; i < 60; i++)
        {
            // Composition-only change: keeps the subtree content-clean (a layer
            // eligibility requirement) while forcing a fresh present.
            scene.Animated.Opacity = 0.95 - (i % 8) * 0.005;
            window.ForceRenderFrame();
            nint layer = GetCachedLayer(scene.Animated);
            if (layer != 0)
            {
                Marker($"LAYER_REALIZED tag={tag} handle=0x{layer:x} frames={i + 1}");
                yield break;
            }
            yield return null;
        }
        throw new HarnessAssertException($"retained layer failed to realize ({tag}) within 60 frames");
    }

    /// <summary>
    /// Detaches the animated container's realized layer handle WITHOUT going
    /// through the pending-destroy queue: returns the handle and zeroes
    /// _cachedLayer so the owning window will neither composite nor drain it.
    /// The caller then destroys it directly through a chosen render target,
    /// which is how the harness drives a specific DestroyRetainedLayer branch.
    /// </summary>
    private static nint StealCachedLayer(Visual visual)
    {
        nint handle = (nint)CachedLayerField.GetValue(visual)!;
        CachedLayerField.SetValue(visual, (nint)0);
        return handle;
    }

    private static IEnumerable<object?> InjectAndRecover(HarnessWindow window, Scene scene, string tag)
    {
        var rtBefore = window.RenderTarget;
        AssertTrue(rtBefore is not null, $"render target present before injection ({tag})");

        int firedBefore = Volatile.Read(ref window.InjectionsFired);
        window.ArmInjection();

        bool fired = false;
        for (int i = 0; i < 400 && !fired; i++)
        {
            scene.Animated.Opacity = 0.93;
            window.ForceRenderFrame();
            fired = Volatile.Read(ref window.InjectionsFired) > firedBefore;
            if (!fired) yield return null;
        }
        AssertTrue(fired, $"mid-frame injection fired ({tag})");
        AssertTrue(window.LastInjectionResult == 1,
            $"debug_remove_device returned {window.LastInjectionResult} ({tag}) — env gate or ID3D12Device5 QI failed");

        // Track that the failure counter actually moved: a regression turning
        // MarkRecoverableRenderFailure into a no-op would still let recovery
        // swap the RT and emit BACKOFF_RESET, masking dead backoff machinery.
        bool sawFailureCount = false;
        for (int i = 0; i < 600; i++)
        {
            if (GetIntField(window, "_consecutiveRecoverableRenderFailures") >= 1) sawFailureCount = true;
            var rt = window.RenderTarget;
            if (rt is not null && !ReferenceEquals(rt, rtBefore))
            {
                AssertTrue(sawFailureCount,
                    $"_consecutiveRecoverableRenderFailures incremented during the incident ({tag}) — backoff machinery is live");
                Marker($"RECOVERED tag={tag} rtReplaced=true polls={i}");
                yield break;
            }
            yield return null;
        }
        throw new HarnessAssertException($"render target was not replaced after injection ({tag})");
    }

    private static IEnumerable<object?> WaitForBackoffReset(HarnessWindow window)
    {
        for (int i = 0; i < 600; i++)
        {
            window.ForceRenderFrame(); // drive successful presents so the Ok branch can reset the counter
            if (GetIntField(window, "_consecutiveRecoverableRenderFailures") == 0)
            {
                Marker($"BACKOFF_RESET window={window.DisplayName} frames={i + 1}");
                yield break;
            }
            yield return null;
        }
        throw new HarnessAssertException("_consecutiveRecoverableRenderFailures never reset after recovery");
    }

    private static IEnumerable<object?> StableFrames(HarnessWindow window, Scene scene, int frames)
    {
        var rt = window.RenderTarget;
        for (int i = 0; i < frames; i++)
        {
            scene.Animated.Opacity = 0.85 + 0.1 * Math.Abs(Math.Sin(i * 0.4));
            window.ForceRenderFrame();
            yield return null;
        }
        AssertTrue(ReferenceEquals(window.RenderTarget, rt),
            $"no unexpected recovery during {frames} stability frames");
        Marker($"STABLE window={window.DisplayName} frames={frames}");
    }

    // ── assertions / reflection / markers ───────────────────────────────────

    private static void AssertBackendStillD3D12(Window window)
    {
        var rt = window.RenderTarget;
        AssertTrue(rt is not null && rt.Backend == RenderBackend.D3D12,
            $"backend stayed D3D12 after recovery (actual: {rt?.Backend.ToString() ?? "<none>"})");
        string overrideBackend = GetField<object>(window, "_renderBackendOverride")?.ToString() ?? "?";
        AssertTrue(overrideBackend == "Auto",
            $"_renderBackendOverride stayed Auto (actual: {overrideBackend}) — a single incident must not trigger the software fallback");
        Marker($"BACKEND_OK d3d12 override={overrideBackend}");
    }

    private static void ReadCounters(HarnessWindow window, out ulong orphaned, out ulong graveyard)
    {
        var rt = window.RenderTarget;
        AssertTrue(rt is not null, "render target available for counter query");
        int ok = DebugRetainedDestroyCounts(rt!.Handle, out orphaned, out graveyard);
        AssertTrue(ok == 1, "debug_retained_destroy_counts query succeeded");
    }

    private static nint GetCachedLayer(Visual visual) => (nint)CachedLayerField.GetValue(visual)!;

    private static T? GetField<T>(object target, string name)
    {
        // Private fields of base types (Window) are invisible to GetField on
        // the runtime type (HarnessWindow) — walk the hierarchy.
        for (Type? type = target.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null) return (T?)field.GetValue(target);
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static int GetIntField(object target, string name) => GetField<int>(target, name);

    internal static void Marker(string text)
    {
        Console.WriteLine("## MARKER " + text);
        Console.Out.Flush();
    }

    private static void AssertTrue(bool condition, string what)
    {
        if (!condition) throw new HarnessAssertException(what);
        Marker("OK " + what);
    }

    private static void Pass()
    {
        Console.WriteLine("## RESULT PASS");
        Console.Out.Flush();
        Environment.Exit(ExitPass);
    }

    private static void Fail(string reason)
    {
        Console.WriteLine("## RESULT FAIL " + reason);
        Console.Out.Flush();
        Environment.Exit(ExitFail);
    }
}

internal sealed class HarnessAssertException : Exception
{
    public HarnessAssertException(string message) : base(message) { }
}

/// <summary>
/// Window whose OnRender(RenderTarget) — which every render path calls between
/// BeginDraw and EndDraw, on the render thread when JALIUM_RENDER_THREAD=1 —
/// removes the GPU device when armed. That makes the injection genuinely
/// mid-frame: the EndDraw of the very same frame observes DEVICE_REMOVED.
/// </summary>
internal sealed class HarnessWindow : Window
{
    private int _arm;
    public int InjectionsFired;
    public volatile int LastInjectionResult = -1;
    public volatile int InjectionThreadId = -1; // managed thread the injection ran on (UI vs render thread)
    public string DisplayName = "A";

    public void ArmInjection() => Interlocked.Exchange(ref _arm, 1);

    protected override void OnRender(RenderTarget renderTarget)
    {
        base.OnRender(renderTarget);
        if (Interlocked.CompareExchange(ref _arm, 0, 1) == 1)
        {
            InjectionThreadId = Environment.CurrentManagedThreadId;
            LastInjectionResult = Program.DebugRemoveDevice(renderTarget.Handle);
            Interlocked.Increment(ref InjectionsFired);
            Program.Marker(
                $"INJECTED window={DisplayName} ok={LastInjectionResult} midframe=true " +
                $"thread={InjectionThreadId}");
        }
    }
}

/// <summary>
/// Element that removes the device from inside its OnRender — placed as the
/// child of a BlurEffect border, its draw executes within the effect's
/// offscreen-capture scope (BeginEffectCapture .. EndEffectCapture), so the
/// device dies while a capture is open. Opts out of the retained drawing
/// cache so the draw really re-executes (and re-fires) every frame instead of
/// being replayed from recorded commands.
/// </summary>
internal sealed class InjectionProbeElement : FrameworkElement
{
    private static readonly SolidColorBrush Fill = new(Color.FromRgb(200, 60, 60));
    private int _arm;
    public int InjectionsFired;
    public volatile int LastInjectionResult = -1;
    public volatile int WasInsideCapture = -1; // 1 if the device was removed while an offscreen capture was open
    public Func<nint> ResolveRenderTargetHandle = static () => 0;

    protected override bool ParticipatesInRenderCache => false;

    public void ArmInjection() => Interlocked.Exchange(ref _arm, 1);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Fill, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));
        if (Interlocked.CompareExchange(ref _arm, 0, 1) == 1)
        {
            nint handle = ResolveRenderTargetHandle();
            // Snapshot capture state BEFORE the removal: this element draws inside
            // its BlurEffect's BeginEffectCapture..EndEffectCapture scope, so a
            // healthy capture path reports 1 here. If a regression skipped the
            // capture (e.g. BlurEffect.HasEffect went false) the probe would draw
            // straight to the swap chain and this would be 0 — caught downstream.
            WasInsideCapture = handle != 0 ? Program.DebugInOffscreenCapture(handle) : -2;
            LastInjectionResult = handle != 0 ? Program.DebugRemoveDevice(handle) : -2;
            Interlocked.Increment(ref InjectionsFired);
            Program.Marker($"INJECTED probe=capture ok={LastInjectionResult} insideCapture={WasInsideCapture}");
        }
    }
}

internal sealed class Scene
{
    public required FrameworkElement Root { get; init; }
    public required Border Animated { get; init; }
    public InjectionProbeElement? Probe { get; init; }
}

internal static class SceneBuilder
{
    /// <summary>
    /// Effect-heavy row (OuterGlow / Blur / DropShadow / LiquidGlass) plus a
    /// retained-layer candidate. The effects sit in a SIBLING branch of the
    /// animated container: any active effect inside the animated subtree would
    /// disqualify it from layer promotion (SubtreeHasEffect).
    /// </summary>
    public static Scene Build(bool withProbe)
    {
        var effectRow = new StackPanel { Orientation = Orientation.Horizontal };
        effectRow.Children.Add(new Border
        {
            Width = 90,
            Height = 46,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(40, 90, 160)),
            Effect = new OuterGlowEffect { GlowSize = 14, GlowColor = Color.FromRgb(0, 200, 255) },
        });

        InjectionProbeElement? probe = null;
        var blurHost = new Border
        {
            Width = 90,
            Height = 46,
            Margin = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(60, 140, 80)),
            // Fully qualified: Jalium.UI.Media.BlurEffect is the BACKDROP blur,
            // the element effect lives in Jalium.UI.Media.Effects.
            Effect = new Jalium.UI.Media.Effects.BlurEffect(6.0),
        };
        if (withProbe)
        {
            probe = new InjectionProbeElement { Width = 60, Height = 30 };
            blurHost.Child = probe;
        }
        else
        {
            blurHost.Child = new TextBlock
            {
                Text = "blur",
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        effectRow.Children.Add(blurHost);

        effectRow.Children.Add(new Border
        {
            Width = 90,
            Height = 46,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(150, 70, 170)),
            Effect = new DropShadowEffect(),
        });

        effectRow.Children.Add(new Border
        {
            Width = 120,
            Height = 46,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            LiquidGlass = true,
            LiquidGlassBlurRadius = 10.0,
        });

        // Retained-layer candidate: content-clean Border with one child visual;
        // the scripts drive Opacity (composition-only) to make it eligible.
        var animated = new Border
        {
            Width = 200,
            Height = 90,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 34)),
            Child = new Border
            {
                Width = 180,
                Height = 70,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(220, 160, 40)),
            },
        };

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(effectRow);
        root.Children.Add(animated);

        return new Scene { Root = root, Animated = animated, Probe = probe };
    }
}
