using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// C7 correctness: the Vulkan retained-layer (composited-animation) fast path must be
/// a pure PERFORMANCE optimization — compositing a captured layer has to produce output
/// pixel-identical to drawing the same subtree content directly (full re-emission).
///
/// Each test drives the native retained-layer C ABI directly through a hidden-window
/// Vulkan render target and the two-phase readback harness:
///   Pass A (fast path): RealizeLayerBegin -> draw content -> RealizeLayerEnd ->
///                       CompositeLayer(...).
///   Pass B (baseline):  draw the SAME content directly, no capture.
/// The readbacks must match. Capture happens on the GPU replay path (default ON), so this
/// exercises RetainedLayerCaptureBegin/End, the per-layer GPU image blit, and the Bitmap
/// composite sampling that image.
/// </summary>
/// <remarks>
/// Retained layers default ON (JALIUM_VK_RETAINED_LAYERS); the tests SKIP (not fail) when
/// the Vulkan backend is unavailable. The GPU capture path additionally needs the shared
/// effect offscreen RT (JALIUM_VK_EFFECT_GPU_RT, latched at process start); when it is off
/// RealizeLayerBegin falls back to a CPU snapshot and the same parity assertion still holds.
/// </remarks>
[Collection("Application")]
public sealed class VulkanRetainedLayerParityTests
{
    private const int Width = 256;
    private const int Height = 256;

    // Subtree content drawn either INTO a layer or directly. Two overlapping solid rects
    // inside the capture region so the comparison covers coverage, overlap, and the region
    // interior (not just a flat fill).
    private static void DrawSubtreeContent(RenderTarget rt, NativeBrush a, NativeBrush b)
    {
        rt.FillRectangle(70f, 70f, 90f, 70f, a);
        rt.FillRectangle(110f, 100f, 80f, 80f, b);
    }

    private static byte[] RenderBaseline(RenderContext context, nint hwnd,
        NativeBrush a, NativeBrush b, float opacity)
    {
        using var rt = context.CreateRenderTarget(hwnd, Width, Height);
        Assert.True(rt.IsValid);
        var buffer = new byte[Width * Height * 4];
        for (int frame = 0; frame < 2; frame++)
        {
            Assert.True(rt.TryBeginDraw());
            rt.Clear(0.12f, 0.12f, 0.18f);
            if (opacity < 1.0f) rt.PushOpacity(opacity);
            DrawSubtreeContent(rt, a, b);
            if (opacity < 1.0f) rt.PopOpacity();
            if (frame == 1) Assert.Equal(JaliumResult.Ok, rt.RequestReadback());
            Assert.Equal(JaliumResult.Ok, rt.TryEndDraw());
        }
        Assert.Equal(JaliumResult.Ok, rt.FetchReadback(buffer, (uint)(Width * 4), out _, out _));
        return buffer;
    }

    private static byte[] RenderViaLayer(RenderContext context, nint hwnd,
        NativeBrush a, NativeBrush b, float opacity, bool captureEachFrame, out bool realized)
    {
        using var rt = context.CreateRenderTarget(hwnd, Width, Height);
        Assert.True(rt.IsValid);
        var buffer = new byte[Width * Height * 4];
        realized = false;
        nint layer = nint.Zero;
        try
        {
            for (int frame = 0; frame < 2; frame++)
            {
                Assert.True(rt.TryBeginDraw());
                rt.Clear(0.12f, 0.12f, 0.18f);

                // Capture the subtree content into the layer. When captureEachFrame is false
                // the second frame skips capture and composites the CACHED layer — this is the
                // cross-frame-persistence path the managed side takes on content-clean frames.
                bool doCapture = captureEachFrame || frame == 0;
                if (doCapture)
                {
                    nint newLayer = rt.RealizeLayerBegin(layer, 60f, 60f, 140f, 130f);
                    if (newLayer != nint.Zero)
                    {
                        realized = true;
                        DrawSubtreeContent(rt, a, b);
                        rt.RealizeLayerEnd(newLayer);
                        layer = newLayer;
                    }
                    else if (frame == 0)
                    {
                        // Could not realize (e.g. backend refused) — draw directly so the
                        // frame is still valid; the test's realized==false guard handles it.
                        DrawSubtreeContent(rt, a, b);
                    }
                }

                // Composite the layer (fast path) with the given opacity at identity transform.
                if (layer != nint.Zero)
                {
                    if (opacity < 1.0f) rt.PushOpacity(opacity);
                    rt.CompositeLayer(layer, 60f, 60f, 140f, 130f, 1.0f);
                    if (opacity < 1.0f) rt.PopOpacity();
                }

                if (frame == 1) Assert.Equal(JaliumResult.Ok, rt.RequestReadback());
                Assert.Equal(JaliumResult.Ok, rt.TryEndDraw());
            }
            Assert.Equal(JaliumResult.Ok, rt.FetchReadback(buffer, (uint)(Width * 4), out _, out _));
        }
        finally
        {
            if (layer != nint.Zero) rt.DestroyRetainedLayer(layer);
        }
        return buffer;
    }

    private static (long maxDiff, long diffCount) Compare(byte[] a, byte[] b)
    {
        Assert.Equal(a.Length, b.Length);
        long maxDiff = 0, diffCount = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d = Math.Abs(a[i] - b[i]);
            if (d != 0) { diffCount++; if (d > maxDiff) maxDiff = d; }
        }
        return (maxDiff, diffCount);
    }

    /// <summary>
    /// Fast path (capture + composite at identity, opacity 1) vs. direct draw must be
    /// pixel-identical. The per-layer image is element-sized and composited 1:1, so a
    /// NEAREST blit introduces no resampling — the result should be byte-exact.
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void CompositeLayer_Identity_MatchesDirectDraw()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        Assert.Equal(RenderBackend.Vulkan, context.Backend);
        using var a = context.CreateSolidBrush(0.90f, 0.25f, 0.15f, 1f);
        using var b = context.CreateSolidBrush(0.15f, 0.55f, 0.90f, 1f);
        Assert.True(a.IsValid && b.IsValid);

        byte[] baseline = RenderBaseline(context, window.Hwnd, a, b, 1.0f);
        byte[] viaLayer = RenderViaLayer(context, window.Hwnd, a, b, 1.0f, captureEachFrame: true, out bool realized);

        if (!realized)
        {
            // Retained layers unavailable at runtime (kill switch / refused): the fast path
            // fell back to direct draw, which is trivially identical — nothing to assert.
            return;
        }

        var (maxDiff, diffCount) = Compare(baseline, viaLayer);
        Assert.True(maxDiff <= 1,
            $"retained-layer composite differs from direct draw: maxDiff={maxDiff}, diffPixels~={diffCount / 4}");
    }

    /// <summary>
    /// Cross-frame persistence: capture ONCE on frame 0, then composite the CACHED layer on
    /// frame 1 with NO re-capture (the managed content-clean path). The composited frame must
    /// still match the direct draw — proving the per-layer GPU image survives across frames
    /// and is sampled without a capture marker in that frame's stream.
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void CompositeLayer_CachedAcrossFrames_MatchesDirectDraw()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var a = context.CreateSolidBrush(0.20f, 0.80f, 0.35f, 1f);
        using var b = context.CreateSolidBrush(0.85f, 0.35f, 0.70f, 1f);
        Assert.True(a.IsValid && b.IsValid);

        byte[] baseline = RenderBaseline(context, window.Hwnd, a, b, 1.0f);
        byte[] viaLayer = RenderViaLayer(context, window.Hwnd, a, b, 1.0f, captureEachFrame: false, out bool realized);
        if (!realized) return;

        var (maxDiff, diffCount) = Compare(baseline, viaLayer);
        Assert.True(maxDiff <= 1,
            $"cached-layer composite differs from direct draw: maxDiff={maxDiff}, diffPixels~={diffCount / 4}");
    }

    /// <summary>
    /// Vulkan currently captures retained layers from its surface-sized shared offscreen
    /// image. A layer crossing any surface edge cannot be captured completely and must be
    /// refused so the managed renderer falls back to direct subtree emission. Accepting a
    /// clipped capture would later stretch that fragment over the full composite bounds.
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void RealizeLayerBegin_PartiallyOutsideSurface_ShouldFallBack()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var rt = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(rt.IsValid);

        if (!rt.SupportsRetainedLayers())
        {
            return;
        }

        Assert.True(rt.TryBeginDraw());
        rt.Clear(0.12f, 0.12f, 0.18f);

        Assert.Equal(nint.Zero, rt.RealizeLayerBegin(nint.Zero, -1f, 20f, 40f, 40f));
        Assert.Equal(nint.Zero, rt.RealizeLayerBegin(nint.Zero, 220f, 20f, 40f, 40f));
        Assert.Equal(nint.Zero, rt.RealizeLayerBegin(nint.Zero, 20f, -1f, 40f, 40f));
        Assert.Equal(nint.Zero, rt.RealizeLayerBegin(nint.Zero, 20f, 220f, 40f, 40f));
        Assert.Equal(nint.Zero, rt.RealizeLayerBegin(nint.Zero, 0f, 0f, Width, Height + 1f));

        Assert.Equal(JaliumResult.Ok, rt.TryEndDraw());
    }

    /// <summary>
    /// An ancestor clip controls where the cached layer is composited, but must not be
    /// baked into the layer texture itself. NavigationViewItem exercises this contract
    /// while its children panel reveals moving child items: the first capture can occur
    /// while only a thin strip is visible, then the same layer is moved into full view.
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void RealizeLayerBegin_AncestorClip_ShouldNotTruncateCachedContent()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var a = context.CreateSolidBrush(0.90f, 0.25f, 0.15f, 1f);
        using var b = context.CreateSolidBrush(0.15f, 0.55f, 0.90f, 1f);
        Assert.True(a.IsValid && b.IsValid);

        byte[] baseline = RenderBaseline(context, window.Hwnd, a, b, 1.0f);
        byte[] viaLayer = new byte[Width * Height * 4];
        using var rt = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(rt.IsValid);
        if (!rt.SupportsRetainedLayers()) return;

        nint layer = nint.Zero;
        try
        {
            for (int frame = 0; frame < 2; frame++)
            {
                Assert.True(rt.TryBeginDraw());
                rt.Clear(0.12f, 0.12f, 0.18f);

                if (frame == 0)
                {
                    // Simulate an expanding panel whose current viewport exposes only
                    // the first 18 pixels of a 130-pixel-tall child layer.
                    rt.PushClip(60f, 60f, 140f, 18f);
                    layer = rt.RealizeLayerBegin(nint.Zero, 60f, 60f, 140f, 130f);
                    Assert.NotEqual(nint.Zero, layer);
                    DrawSubtreeContent(rt, a, b);
                    rt.RealizeLayerEnd(layer);
                    rt.PopClip();
                }

                if (layer != nint.Zero)
                {
                    // The panel has finished expanding, so composite without its former
                    // reveal clip. The full cached child must now be available.
                    rt.CompositeLayer(layer, 60f, 60f, 140f, 130f, 1.0f);
                }
                else
                {
                    DrawSubtreeContent(rt, a, b);
                }

                if (frame == 1) Assert.Equal(JaliumResult.Ok, rt.RequestReadback());
                Assert.Equal(JaliumResult.Ok, rt.TryEndDraw());
            }
            Assert.Equal(JaliumResult.Ok, rt.FetchReadback(viaLayer, (uint)(Width * 4), out _, out _));
        }
        finally
        {
            if (layer != nint.Zero) rt.DestroyRetainedLayer(layer);
        }

        var (maxDiff, diffCount) = Compare(baseline, viaLayer);
        Assert.True(maxDiff <= 1,
            $"ancestor-clipped retained capture differs from direct draw: maxDiff={maxDiff}, diffPixels~={diffCount / 4}");
    }

    /// <summary>
    /// Composite opacity path: a layer captured at FULL opacity then composited at 0.6 must
    /// match the same content drawn directly at 0.6.
    ///
    /// IMPORTANT: this holds pixel-for-pixel only when the content does NOT self-overlap —
    /// group opacity (blend the opaque group once at 0.6, the layer path) and per-element
    /// opacity (blend each element at 0.6, the direct path) DIVERGE on overlap regions, and
    /// that divergence is correct WPF semantics (it is precisely why CompositeLayer applies
    /// group opacity). So this test uses two DISJOINT rects, where the two models coincide,
    /// to validate the composite opacity math without conflating it with grouping semantics.
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void CompositeLayer_WithOpacity_MatchesDirectDraw()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var a = context.CreateSolidBrush(0.9f, 0.9f, 0.2f, 1f);
        using var b = context.CreateSolidBrush(0.2f, 0.9f, 0.9f, 1f);
        Assert.True(a.IsValid && b.IsValid);

        // Disjoint content: per-element and group opacity coincide.
        static void DrawDisjoint(RenderTarget rt, NativeBrush x, NativeBrush y)
        {
            rt.FillRectangle(70f, 70f, 50f, 50f, x);
            rt.FillRectangle(140f, 130f, 50f, 50f, y);
        }

        const float opacity = 0.6f;
        byte[] baseline;
        {
            using var rt = context.CreateRenderTarget(window.Hwnd, Width, Height);
            Assert.True(rt.IsValid);
            baseline = new byte[Width * Height * 4];
            for (int frame = 0; frame < 2; frame++)
            {
                Assert.True(rt.TryBeginDraw());
                rt.Clear(0.12f, 0.12f, 0.18f);
                rt.PushOpacity(opacity);
                DrawDisjoint(rt, a, b);
                rt.PopOpacity();
                if (frame == 1) Assert.Equal(JaliumResult.Ok, rt.RequestReadback());
                Assert.Equal(JaliumResult.Ok, rt.TryEndDraw());
            }
            Assert.Equal(JaliumResult.Ok, rt.FetchReadback(baseline, (uint)(Width * 4), out _, out _));
        }

        byte[] viaLayer;
        bool realized = false;
        {
            using var rt = context.CreateRenderTarget(window.Hwnd, Width, Height);
            Assert.True(rt.IsValid);
            viaLayer = new byte[Width * Height * 4];
            nint layer = nint.Zero;
            try
            {
                for (int frame = 0; frame < 2; frame++)
                {
                    Assert.True(rt.TryBeginDraw());
                    rt.Clear(0.12f, 0.12f, 0.18f);
                    if (frame == 0)
                    {
                        nint newLayer = rt.RealizeLayerBegin(nint.Zero, 60f, 60f, 140f, 130f);
                        if (newLayer != nint.Zero)
                        {
                            realized = true;
                            DrawDisjoint(rt, a, b);   // content captured at FULL opacity
                            rt.RealizeLayerEnd(newLayer);
                            layer = newLayer;
                        }
                        else
                        {
                            // Not realized — draw directly at the target opacity so the frame is valid.
                            rt.PushOpacity(opacity);
                            DrawDisjoint(rt, a, b);
                            rt.PopOpacity();
                        }
                    }
                    if (layer != nint.Zero)
                    {
                        // Group opacity applied at composite time.
                        rt.CompositeLayer(layer, 60f, 60f, 140f, 130f, opacity);
                    }
                    if (frame == 1) Assert.Equal(JaliumResult.Ok, rt.RequestReadback());
                    Assert.Equal(JaliumResult.Ok, rt.TryEndDraw());
                }
                Assert.Equal(JaliumResult.Ok, rt.FetchReadback(viaLayer, (uint)(Width * 4), out _, out _));
            }
            finally
            {
                if (layer != nint.Zero) rt.DestroyRetainedLayer(layer);
            }
        }

        if (!realized) return;

        var (maxDiff, _) = Compare(baseline, viaLayer);
        // Premultiplied opacity blend on both paths; allow a small rounding tolerance for the
        // extra premul round-trip through the element-sized layer image.
        Assert.True(maxDiff <= 3,
            $"retained-layer composite with opacity differs from direct draw: maxDiff={maxDiff}");
    }
}
