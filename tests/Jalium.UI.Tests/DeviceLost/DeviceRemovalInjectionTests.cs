using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace Jalium.UI.Tests.DeviceLost;

/// <summary>
/// Real-GPU runtime verification for the GPU-switch (mid-frame DEVICE_REMOVED)
/// hardening. Each test drives tests/Jalium.UI.DeviceLostHarness as a child
/// process, with the device removed via the official debug trigger
/// (ID3D12Device5::RemoveDevice) behind jalium_render_target_debug_remove_device.
///
/// Child process, not in-proc, because:
///  - a hardening regression dies with an uncatchable AV/CSE (0xC000041D); in
///    this process that would kill the whole test host, in a child it becomes
///    a clean nonzero exit code with the harness transcript attached;
///  - JALIUM_RENDER_THREAD is latched at process start, so the render-thread
///    variant needs its own process and environment.
///
/// [Collection("Application")] serializes these with the other window/GPU test
/// classes so two GPU-heavy processes never overlap (the DP-metadata race
/// itself does not apply — no Window is constructed in-proc).
///
/// Needs a real GPU + desktop session; the harness reports exit code 2 when
/// the requested backend is unavailable and the test is skipped (reported as
/// passed with a note in the output).
///
/// Most scenarios drive D3D12 (retained-layer discrimination + #921 hooks are
/// D3D12-only machinery). The backend-agnostic 'devicelost' core (inject →
/// recover → fresh device → no downgrade) runs against BOTH D3D12 and Vulkan via
/// the harness's optional backend argument, so the Vulkan device-lost recovery
/// chain is regressible here instead of only via a physical GPU TDR.
/// </summary>
[Collection("Application")]
public sealed class DeviceRemovalInjectionTests
{
    private const int HarnessTimeoutMs = 180_000;
    private readonly ITestOutputHelper _output;

    public DeviceRemovalInjectionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void MidFrameRemoval_RecoversAndReRealizesRetainedLayers()
        => RunScenario(
            "midframe",
            renderThread: false,
            requiredMarkers:
            [
                "LAYER_REALIZED tag=initial",
                "INJECTED window=A ok=1 midframe=true",
                "RECOVERED tag=round1",
                "LAYER_REREALIZED",
                "BACKOFF_RESET",
                "BACKEND_OK d3d12",
                "RECOVERED tag=round2",
                "SCENARIO midframe complete",
            ]);

    [Fact]
    public void MidEffectCaptureRemoval_RecoversWithoutWedgingCaptureState()
        => RunScenario(
            "capture",
            renderThread: false,
            requiredMarkers:
            [
                "INJECTED probe=capture ok=1 insideCapture=1", // device really died inside an OPEN capture scope
                "RECOVERED tag=capture1",
                "RECOVERED tag=capture2",
                "BACKEND_OK d3d12",
                "SCENARIO capture complete",
            ]);

    [Fact]
    public void EscapedRetainedLayer_RefusedByGenerationGuard_ReleasedNotLeaked()
        => RunScenario(
            "escapedhandle",
            renderThread: false,
            requiredMarkers:
            [
                "LAYER_REALIZED tag=escaped-setup",
                "detached subtree kept its stale-device layer handle",
                "RECOVERED tag=escaped",
                "escaped layer survived the recovery sweep",
                "ESCAPED_HANDLE_RELEASED",
                "ESCAPED_HANDLE_ORPHANED",
                "SCENARIO escapedhandle complete",
            ]);

    [Fact]
    public void MultiWindow_StaggeredRecovery_OrphanVsGraveyardDiscrimination()
        => RunScenario(
            "multiwindow",
            renderThread: false,
            requiredMarkers:
            [
                "STAGGERED_A_RECOVERED",
                "STAGGERED_B_RECOVERED",
                "CROSS_HEALTHY_GRAVEYARD",
                "INJECTED window=A outOfFrame=true",
                "CROSS_REMOVED_ORPHAN",
                "A_RECOVERED_AGAIN",
                "SCENARIO multiwindow complete",
            ]);

    [Fact]
    public void RenderThread_SingleMarshaledRecovery_NoBackendDowngrade()
        => RunScenario(
            "renderthread",
            renderThread: true,
            requiredMarkers:
            [
                "INJECTED window=A ok=1 midframe=true",
                "SINGLE_RECOVERY round=1 instances=2",
                "SINGLE_RECOVERY round=2 instances=2",
                "BACKEND_OK d3d12",
                "SCENARIO renderthread complete",
            ]);

    /// <summary>
    /// Backend-agnostic device-lost core on D3D12: mid-frame device removal, then
    /// recovery replaced the render target, brought up a fresh device (pointer
    /// changed on the first incident), stayed on D3D12, and a second incident did
    /// not downgrade. This is the same scenario the Vulkan variant below runs — the
    /// portable subset that does not touch retained layers or the #921 hooks.
    /// </summary>
    [Fact]
    public void DeviceLost_D3D12_RecoversWithFreshDeviceNoDowngrade()
        => RunScenario(
            "devicelost",
            renderThread: false,
            backend: "d3d12",
            requiredMarkers:
            [
                "FIRST_FRAME window=A backend=D3D12",
                "INJECTED window=A ok=1 midframe=true",
                "DEVICELOST_RECOVERED round=1 before=", // ... changed=1 asserted in-harness
                "BACKEND_OK d3d12",
                "DEVICELOST_RECOVERED round=2 before=",
                "SCENARIO devicelost complete backend=d3d12",
            ]);

    /// <summary>
    /// The F8 headline: the SAME device-lost recovery, now automated for Vulkan.
    /// The device is removed mid-frame via the Vulkan debug injection hook
    /// (DebugRemoveDevice trips the sticky deviceLost latch), the managed recovery
    /// chain rebuilds the render target on a fresh VkDevice, the backend stays
    /// Vulkan, and a second incident does not downgrade — proving the Vulkan
    /// device-lost hardening (SURFACE_LOST vs DEVICE_LOST classification, generation
    /// latch, managed recovery) is regressible under the harness exactly like D3D12,
    /// instead of only via a physical GPU TDR. Skips (exit 2) when the Vulkan
    /// backend is unavailable in this environment (no Vulkan runtime / DLL).
    /// </summary>
    [Fact]
    public void DeviceLost_Vulkan_RecoversWithFreshDeviceNoDowngrade()
        => RunScenario(
            "devicelost",
            renderThread: false,
            backend: "vulkan",
            requiredMarkers:
            [
                "FIRST_FRAME window=A backend=Vulkan",
                "INJECTED window=A ok=1 midframe=true",
                "DEVICELOST_RECOVERED round=1 before=",
                "BACKEND_OK vulkan",
                "DEVICELOST_RECOVERED round=2 before=",
                "SCENARIO devicelost complete backend=vulkan",
            ]);

    /// <summary>
    /// #921 same-thread leaked-open command-list resize. The CROSS-thread half of
    /// #921 (native returns BUSY → managed defers, dimensions frozen) is covered by
    /// the managed seam test
    /// RenderTargetFailureTests.Resize_WhenNativeReportsBusy_DefersWithoutThrowingAndKeepsDimensions.
    /// This covers the SAME-thread branch, which is unreachable through the fake
    /// seam: it needs a real D3D12 device + HWND swap chain and the internal
    /// "command list open while isDrawing_==0" race. The native debug hook stages
    /// that race (opens the list the way BeginDraw does, without setting isDrawing_)
    /// and resizes; the guard must AbortFrame (Close the leaked list) BEFORE the
    /// back buffers it references are freed, then return JALIUM_OK. A regression
    /// that drops the AbortFrame leaves the list open (listClosed=0) and, under the
    /// D3D12 debug layer, trips OBJECT_DELETED_WHILE_STILL_IN_USE / a crash.
    /// </summary>
    [Fact]
    public void SameThreadLeakedCommandListResize_ClosesListBeforeFreeingBackBuffers()
        => RunScenario(
            "leakedresize",
            renderThread: false,
            requiredMarkers:
            [
                // result=0 (JALIUM_OK, not the cross-thread BUSY defer) AND
                // listClosed=1 (the leaked list was Closed before the back buffers
                // it referenced were freed — the #921 fix). A regression drops one
                // or both and this exact line is absent (and the harness exits != 0).
                "LEAKED_RESIZE result=0 listClosed=1",
                "BACKEND_OK d3d12",
                "SCENARIO leakedresize complete",
            ]);

    /// <summary>
    /// #921 Vello-output-texture orphan. The D3D12 Vello 'JaliumVelloOutput' texture
    /// was the one Vello GPU resource not covered by the per-frame fence-gated retire
    /// machinery: ForceNewOutputTexture() bare-Reset its only owning ComPtr, leaving
    /// the AddBitmap keep-alive in bitmapTextures_ as the sole reference, which a later
    /// mid-frame FlushGraphicsForCompute() cleared BEFORE the command list was Closed —
    /// freeing the texture while the open list still referenced it (#921
    /// OBJECT_DELETED_WHILE_STILL_IN_USE). The fix routes ForceNewOutputTexture through
    /// RetireOutputTexture, parking the texture on the fence-gated retired list. This
    /// is unreachable through the public API (it needs a real D3D12 device + the
    /// internal Vello dispatch/composite/clear sequence with the list open), so the
    /// env-gated native debug hook stages it and reports alive==1 when the texture
    /// survived the clear. A regression (bare Reset) frees it and alive reads 0 (and,
    /// under the debug layer, trips OBJECT_DELETED_WHILE_STILL_IN_USE). The survival
    /// signal is debug-layer-independent, the same way leakedresize asserts listClosed.
    /// </summary>
    [Fact]
    public void VelloOutputTextureOrphan_RetiredOnFenceGatedList_NotFreedWhileReferenced()
        => RunScenario(
            "vellooutputorphan",
            renderThread: false,
            requiredMarkers:
            [
                // result=0 (the orphan was staged) AND alive=1 (the composited
                // 'JaliumVelloOutput' survived the mid-frame bitmapTextures_.clear()
                // because RetireOutputTexture parked it on the fence-gated retired
                // list — the #921 fix). A regression drops the park and this exact
                // line is absent (and the harness exits != 0).
                "VELLO_ORPHAN result=0 alive=1",
                "BACKEND_OK d3d12",
                "SCENARIO vellooutputorphan complete",
            ]);

    private void RunScenario(string scenario, bool renderThread, string[] requiredMarkers, string backend = "d3d12")
    {
        string exe = LocateHarness();
        // The harness takes an optional 2nd positional backend token
        // (harness <scenario> [d3d12|vulkan]); default d3d12 keeps the original
        // single-argument invocations unchanged.
        string args = backend == "d3d12" ? scenario : $"{scenario} {backend}";
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.Environment["JALIUM_DEBUG_DEVICE_REMOVE"] = "1";
        psi.Environment["JALIUM_RENDER_THREAD"] = renderThread ? "1" : "0";
        // The layer assertions need the retained-layer path live, and a WARP
        // override left over from an earlier downgrade would change what
        // "recovery" exercises.
        psi.Environment.Remove("JALIUM_DISABLE_RETAINED_LAYERS");
        psi.Environment.Remove("JALIUM_D3D12_FORCE_WARP");

        var transcript = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (transcript) transcript.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (transcript) transcript.AppendLine("[stderr] " + e.Data); };

        Assert.True(process.Start(), $"failed to start harness: {exe}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(HarnessTimeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            process.WaitForExit(10_000);
            Assert.Fail($"harness '{scenario}' timed out after {HarnessTimeoutMs / 1000}s.\n{Snapshot()}");
        }
        process.WaitForExit(); // drain the async output readers

        int exitCode = process.ExitCode;
        string all = Snapshot();
        _output.WriteLine(all);

        if (exitCode == 2)
        {
            _output.WriteLine($"SKIP: harness reported no usable D3D12 device — '{scenario}' needs a real GPU.");
            return;
        }

        Assert.True(exitCode == 0,
            $"harness '{scenario}' exited with {exitCode} (0x{exitCode:x8}) — a crash exit here is exactly the " +
            $"unhardened GPU-switch failure mode.\n{all}");
        foreach (string marker in requiredMarkers)
        {
            Assert.True(all.Contains(marker, StringComparison.Ordinal),
                $"harness '{scenario}' passed but the transcript is missing marker '{marker}' — " +
                $"the scenario did not exercise what this test claims.\n{all}");
        }

        string Snapshot() { lock (transcript) return transcript.ToString(); }
    }

    private static string LocateHarness()
    {
        // AppContext.BaseDirectory = tests/Jalium.UI.Tests/bin/<Config>/<tfm>/
        // The harness (a ProjectReference with ReferenceOutputAssembly=false, so
        // it is built but not copied here) lives in the sibling project's
        // bin/<Config>/<tfm>/.
        var tfmDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        DirectoryInfo? configDir = tfmDir.Parent;          // bin/<Config>

        // Directory.Build.props can redirect every project into one shared
        // JaliumBuildRoot, yielding
        //   <root>/bin/<Project>/<Config>/<TFM>/
        // rather than the SDK's default
        //   <Project>/bin/<Config>/<TFM>/.
        // The harness is still a build-order-only ProjectReference, so probe
        // its sibling project directory before interpreting the tree as the
        // default repository layout.
        DirectoryInfo? projectOutputDir = configDir?.Parent;
        DirectoryInfo? sharedBinDir = projectOutputDir?.Parent;
        if (configDir is not null &&
            sharedBinDir is not null &&
            string.Equals(projectOutputDir?.Name, "Jalium.UI.Tests", StringComparison.OrdinalIgnoreCase))
        {
            string sharedCandidate = Path.Combine(
                sharedBinDir.FullName, "Jalium.UI.DeviceLostHarness",
                configDir.Name, tfmDir.Name, "Jalium.UI.DeviceLostHarness.exe");
            if (File.Exists(sharedCandidate)) return sharedCandidate;
        }

        DirectoryInfo? testsProjectDir = configDir?.Parent?.Parent; // tests/Jalium.UI.Tests
        DirectoryInfo? testsRoot = testsProjectDir?.Parent;          // tests/

        if (testsRoot is not null && configDir is not null)
        {
            string candidate = Path.Combine(
                testsRoot.FullName, "Jalium.UI.DeviceLostHarness", "bin",
                configDir.Name, tfmDir.Name, "Jalium.UI.DeviceLostHarness.exe");
            if (File.Exists(candidate)) return candidate;

            // Mixed-config fallback (e.g. tests built Debug while the harness
            // was last built Release by hand).
            foreach (string config in new[] { "Debug", "Release" })
            {
                candidate = Path.Combine(
                    testsRoot.FullName, "Jalium.UI.DeviceLostHarness", "bin",
                    config, tfmDir.Name, "Jalium.UI.DeviceLostHarness.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException(
            "Jalium.UI.DeviceLostHarness.exe not found next to the test tree — build " +
            "tests/Jalium.UI.DeviceLostHarness (it is a ReferenceOutputAssembly=false " +
            $"ProjectReference of this test project). Probed from: {AppContext.BaseDirectory}");
    }
}
