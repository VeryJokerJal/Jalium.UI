using System.Diagnostics;
using System.Globalization;
using Jalium.UI;
using Jalium.UI.Interop;

namespace Jalium.UI.ParityHarness;

/// <summary>
/// Cross-backend rendering parity harness.
///
/// Drives the SAME sequence of raw native draw calls (through the
/// Jalium.UI.Interop thin wrappers — no Application / layout / styling, so
/// both backends receive byte-identical command streams) against a hidden
/// native window, captures each frame via the two-phase readback ABI
/// (RequestReadback before EndDraw, FetchReadback after), and dumps every
/// scene as a 32bpp top-down BMP + a small JSON metadata sidecar.
///
/// Usage:  Jalium.UI.ParityHarness &lt;backend&gt; &lt;outDir&gt; [sceneFilter]
///   backend      d3d12 | vulkan | software
///   outDir       directory receiving &lt;scene&gt;.bmp / &lt;scene&gt;.json
///   sceneFilter  optional substring — only scenes whose name contains it run
///
/// Exit codes:
///   0  all selected scenes rendered and dumped
///   2  the backend reported readback NOT_SUPPORTED (e.g. Vulkan before its
///      capture path lands) — a SKIP marker file is written; the diff driver
///      treats this as "no comparison data", not a failure
///   1  hard failure (context/target creation, draw, or readback error)
/// </summary>
internal static class Program
{
    public const int Width = 512;
    public const int Height = 512;

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Jalium.UI.ParityHarness <d3d12|vulkan|software> <outDir> [sceneFilter]");
            return 1;
        }

        RenderBackend backend = args[0].ToLowerInvariant() switch
        {
            "d3d12" => RenderBackend.D3D12,
            "vulkan" => RenderBackend.Vulkan,
            "software" => RenderBackend.Software,
            _ => RenderBackend.Auto,
        };
        if (backend == RenderBackend.Auto)
        {
            Console.Error.WriteLine($"unknown backend '{args[0]}' (expected d3d12 | vulkan | software)");
            return 1;
        }

        string outDir = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(outDir);
        string? sceneFilter = args.Length >= 3 ? args[2] : null;

        // The explicit-backend ctor silently falls back (Auto→Software chain)
        // when the requested backend cannot materialize; a parity run against
        // the WRONG backend would poison the diff, so assert what we got.
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend);
        if (context.Backend != backend)
        {
            Console.Error.WriteLine($"backend mismatch: requested {backend}, got {context.Backend} — aborting.");
            return 1;
        }

        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        // Deterministic single-frame stepping: no vsync pacing, no dirty-rect
        // partial present (full invalidation each frame via full-screen Clear).
        target.SetVSyncEnabled(false);

        // Readback capability probe: the base-class default answers
        // NOT_SUPPORTED synchronously, so one Request tells us whether this
        // backend can capture at all — BEFORE rendering 14 scenes for nothing.
        // (On supporting backends this arms a pending capture; every scene
        // re-arms anyway, so the probe is harmless.)
        var probe = target.RequestReadback();
        if (probe == JaliumResult.NotSupported)
        {
            string skipPath = Path.Combine(outDir, "SKIP.readback-not-supported");
            File.WriteAllText(skipPath,
                $"backend={args[0]}\nreason=RequestReadback returned NOT_SUPPORTED\n" +
                "The two-phase readback ABI is not implemented for this backend yet " +
                "(Vulkan capture lands separately). No frames were captured.\n");
            // ASCII-only console output — GBK/legacy-codepage consoles mangle
            // em-dashes and arrows in redirected harness logs.
            Console.WriteLine($"SKIP backend={args[0]} readback NOT_SUPPORTED, marker: {skipPath}");
            return 2;
        }

        var scenes = Scenes.All;
        int rendered = 0;
        int selected = 0;
        var failed = new List<(string Scene, string Error)>();
        foreach (var (name, draw) in scenes)
        {
            if (sceneFilter != null && !name.Contains(sceneFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            selected++;

            var sw = Stopwatch.StartNew();
            try
            {
                RenderScene(context, target, name, draw, outDir);
                sw.Stop();
                Console.WriteLine($"OK   scene={name,-28} {sw.ElapsedMilliseconds,5} ms");
                rendered++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Per-scene resilience: a scene that faults on ONE backend (e.g.
                // a Vulkan GPU-offscreen-effect path that reports EndDraw
                // InvalidState, or a backend that can't compile a shader) must
                // NOT abort the whole run — the point of a parity BASELINE is to
                // capture every scene that DOES render on both sides and to
                // record the ones that don't. We drop a `<scene>.FAILED.txt`
                // marker (so the diff can distinguish "rendered but different"
                // from "never rendered here") and keep going. The BeginDraw
                // retry loop in RenderScene re-acquires the swapchain for the
                // next scene after the faulted frame is closed.
                Console.Error.WriteLine($"FAIL scene={name}: {ex.Message}");
                failed.Add((name, ex.Message));
                try
                {
                    File.WriteAllText(Path.Combine(outDir, name + ".FAILED.txt"),
                        $"backend={args[0]}\nscene={name}\nerror={ex.Message}\n");
                }
                catch { /* marker best-effort */ }
            }
        }

        if (selected == 0)
        {
            Console.Error.WriteLine(sceneFilter != null
                ? $"no scene matched filter '{sceneFilter}'"
                : "no scenes registered");
            return 1;
        }

        if (failed.Count > 0)
        {
            Console.WriteLine($"DONE backend={args[0]} scenes={rendered} FAILED={failed.Count} outDir={outDir}");
            foreach (var (scene, error) in failed)
            {
                Console.WriteLine($"  FAILED scene={scene}: {error}");
            }
            // Exit 3 = "the backend rendered SOME scenes but faulted on others".
            // Distinct from 1 (hard/setup failure) and 2 (readback NOT_SUPPORTED)
            // so run_parity.ps1 can still diff the scenes that DID render on both
            // halves while surfacing the per-scene faults. If NOTHING rendered,
            // fall through to the hard-failure path below.
            if (rendered == 0)
            {
                Console.Error.WriteLine("no scene rendered successfully");
                return 1;
            }
            return 3;
        }

        Console.WriteLine($"DONE backend={args[0]} scenes={rendered} outDir={outDir}");
        return 0;
    }

    private static void RenderScene(RenderContext context, RenderTarget target,
        string name, Action<SceneContext> draw, string outDir)
    {
        // BeginDraw retry loop: with MFL=1 the frame-latency waitable of a
        // hidden (never-shown) window can lag DWM's consumption of the prior
        // present; D3D12 surfaces that as a recoverable InvalidState. Retry
        // with small sleeps instead of failing the scene.
        bool began = false;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (target.TryBeginDraw()) { began = true; break; }
            Thread.Sleep(5);
        }
        if (!began) throw new InvalidOperationException("BeginDraw did not succeed within 1s (waitable starved).");

        long drawStart = Stopwatch.GetTimestamp();
        var scene = new SceneContext(context, target);
        try
        {
            try
            {
                // Every scene is a from-scratch frame: declare FULL invalidation
                // explicitly. Clear() alone is NOT enough on damage-retaining
                // backends — Vulkan's DrawReplayFrame only honors the frame
                // clear when fullInvalidation is set (a partial frame instead
                // seeds the swapchain image from the persistent retain image,
                // i.e. the PREVIOUS scene, and composites on top — first run
                // showed every scene ghosting all its predecessors). D3D12
                // ignores the flag for rendering (dirty rects only shape
                // Present1), so this is a no-op there.
                target.SetFullInvalidation();
                // Uniform background so scene diffs aren't polluted by whatever
                // the previous scene left in the (persistent FLIP_SEQUENTIAL)
                // buffer.
                target.Clear(0.10f, 0.10f, 0.12f);
                draw(scene);

                var reqResult = target.RequestReadback();
                if (reqResult != JaliumResult.Ok)
                {
                    throw new InvalidOperationException($"RequestReadback failed: {reqResult}");
                }
            }
            catch
            {
                _ = target.TryEndDraw(); // never leave the native frame open
                throw;
            }

            var endResult = target.TryEndDraw();
            if (endResult != JaliumResult.Ok)
            {
                throw new InvalidOperationException($"EndDraw failed: {endResult}");
            }
        }
        finally
        {
            // Release scene brushes/formats/bitmaps only AFTER the frame was
            // submitted: backends are free to defer-reference brush objects
            // until their EndFrame flush.
            scene.DisposeResources();
        }
        double drawMs = Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;

        long fetchStart = Stopwatch.GetTimestamp();
        uint stride = Width * 4;
        byte[] pixels = new byte[stride * Height];
        var fetchResult = target.FetchReadback(pixels, stride, out int w, out int h);
        if (fetchResult != JaliumResult.Ok)
        {
            throw new InvalidOperationException($"FetchReadback failed: {fetchResult}");
        }
        if (w != Width || h != Height)
        {
            throw new InvalidOperationException($"FetchReadback size mismatch: got {w}x{h}, expected {Width}x{Height}");
        }
        double fetchMs = Stopwatch.GetElapsedTime(fetchStart).TotalMilliseconds;

        // Guard against an all-zero frame (readback silently broken / frame
        // never drawn): every scene paints a non-black background, so a fully
        // zeroed buffer is always a bug, never a valid capture.
        bool anyNonZero = false;
        foreach (byte b in pixels)
        {
            if (b != 0) { anyNonZero = true; break; }
        }
        if (!anyNonZero)
        {
            throw new InvalidOperationException("readback returned an all-zero frame");
        }

        string bmpPath = Path.Combine(outDir, name + ".bmp");
        BmpWriter.WriteBgra32TopDown(bmpPath, pixels, w, h, (int)stride);

        string jsonPath = Path.Combine(outDir, name + ".json");
        File.WriteAllText(jsonPath,
            "{\n" +
            $"  \"backend\": \"{target.Backend}\",\n" +
            $"  \"scene\": \"{name}\",\n" +
            $"  \"width\": {w},\n" +
            $"  \"height\": {h},\n" +
            $"  \"drawMs\": {drawMs.ToString("0.###", CultureInfo.InvariantCulture)},\n" +
            $"  \"fetchMs\": {fetchMs.ToString("0.###", CultureInfo.InvariantCulture)}\n" +
            "}\n");
    }
}
