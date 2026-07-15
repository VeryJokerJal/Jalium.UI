using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// D3D12 retained-layer capture must isolate parent clips for both the direct
/// renderer and the Impeller path-batch mirror.
/// </summary>
[Collection("Application")]
public sealed class D3D12RetainedLayerParityTests
{
    private const int Width = 256;
    private const int Height = 256;

    private static readonly float[] s_pathCommands =
    [
        0f, 190f, 70f,
        0f, 190f, 180f,
        0f, 70f, 180f,
        5f
    ];

    private static void DrawPathContent(RenderTarget rt, NativeBrush brush) =>
        rt.FillPath(70f, 70f, s_pathCommands, brush, fillRule: 1);

    private static byte[] RenderBaseline(RenderContext context, nint hwnd, NativeBrush brush)
    {
        using var rt = context.CreateRenderTarget(hwnd, Width, Height);
        Assert.True(rt.IsValid);
        Assert.Equal(RenderingEngine.Impeller, rt.RenderingEngine);
        // Analytic mode bypasses the solid-color stencil fast path, ensuring
        // FillPath exercises Impeller's independently mirrored clip state.
        rt.SetPathMsaaSampleCount(0);

        var pixels = new byte[Width * Height * 4];
        Assert.True(rt.TryBeginDraw());
        rt.Clear(0.12f, 0.12f, 0.18f);
        DrawPathContent(rt, brush);
        Assert.Equal(JaliumResult.Ok, rt.RequestReadback());
        Assert.Equal(JaliumResult.Ok, rt.TryEndDraw());
        Assert.Equal(JaliumResult.Ok, rt.FetchReadback(pixels, (uint)(Width * 4), out _, out _));
        return pixels;
    }

    private static void AssertBuffersClose(byte[] expected, byte[] actual, int tolerance, string scenario)
    {
        Assert.Equal(expected.Length, actual.Length);
        long maxDiff = 0;
        long diffCount = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            int difference = Math.Abs(expected[i] - actual[i]);
            if (difference == 0) continue;
            diffCount++;
            maxDiff = Math.Max(maxDiff, difference);
        }

        Assert.True(maxDiff <= tolerance,
            $"{scenario}: maxDiff={maxDiff}, diffPixels~={diffCount / 4}");
    }

    [RequiresBackendFact(RenderBackend.D3D12)]
    public void RealizeLayerBegin_AncestorClip_ShouldNotTruncateImpellerContent()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(
            RenderBackend.D3D12,
            GpuPreference.Auto,
            RenderingEngine.Impeller);
        Assert.Equal(RenderBackend.D3D12, context.Backend);
        using var brush = context.CreateSolidBrush(0.90f, 0.25f, 0.15f, 1f);
        Assert.True(brush.IsValid);

        byte[] baseline = RenderBaseline(context, window.Hwnd, brush);
        int centerPixel = ((120 * Width) + 120) * 4;
        int backgroundPixel = ((12 * Width) + 12) * 4;
        Assert.True(
            Enumerable.Range(0, 3).Any(channel =>
                Math.Abs(baseline[centerPixel + channel] - baseline[backgroundPixel + channel]) > 20),
            "The direct Impeller baseline must contain visible path pixels.");

        byte[] viaLayer = new byte[Width * Height * 4];
        using var rt = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(rt.IsValid);
        Assert.Equal(RenderingEngine.Impeller, rt.RenderingEngine);
        rt.SetPathMsaaSampleCount(0);
        Assert.True(rt.SupportsRetainedLayers());

        nint layer = nint.Zero;
        try
        {
            Assert.True(rt.TryBeginDraw());
            rt.Clear(0.12f, 0.12f, 0.18f);

            // Only the top 18 px is initially revealed. A stale Impeller clip
            // mirror would cache almost the entire path as transparent.
            rt.PushClip(60f, 60f, 140f, 18f);
            layer = rt.RealizeLayerBegin(nint.Zero, 60f, 60f, 140f, 130f);
            Assert.NotEqual(nint.Zero, layer);
            DrawPathContent(rt, brush);
            rt.RealizeLayerEnd(layer);
            rt.PopClip();

            // Composite after the reveal clip is gone. This is deliberately in
            // the same frame: an invisible HWND may report occluded after its
            // first Present, while clip isolation itself is frame-independent.
            rt.CompositeLayer(layer, 60f, 60f, 140f, 130f, 1.0f);
            Assert.Equal(JaliumResult.Ok, rt.RequestReadback());
            Assert.Equal(JaliumResult.Ok, rt.TryEndDraw());
            Assert.Equal(JaliumResult.Ok, rt.FetchReadback(viaLayer, (uint)(Width * 4), out _, out _));
        }
        finally
        {
            if (layer != nint.Zero) rt.DestroyRetainedLayer(layer);
        }

        AssertBuffersClose(
            baseline,
            viaLayer,
            tolerance: 3,
            scenario: "Ancestor-clipped D3D12 retained capture differs from direct Impeller draw");
    }

    [RequiresBackendFact(RenderBackend.D3D12)]
    public void RealizeLayerBegin_AncestorRoundedClip_ShouldNotBakeMaskIntoLayer()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(
            RenderBackend.D3D12,
            GpuPreference.Auto,
            RenderingEngine.Impeller);
        using var brush = context.CreateSolidBrush(0.15f, 0.70f, 0.35f, 1f);
        Assert.True(brush.IsValid);

        byte[] baseline = new byte[Width * Height * 4];
        using (var direct = context.CreateRenderTarget(window.Hwnd, Width, Height))
        {
            Assert.True(direct.TryBeginDraw());
            direct.Clear(0.12f, 0.12f, 0.18f);
            direct.FillRectangle(60f, 60f, 140f, 130f, brush);
            Assert.Equal(JaliumResult.Ok, direct.RequestReadback());
            Assert.Equal(JaliumResult.Ok, direct.TryEndDraw());
            Assert.Equal(JaliumResult.Ok, direct.FetchReadback(baseline, (uint)(Width * 4), out _, out _));
        }

        byte[] viaLayer = new byte[Width * Height * 4];
        using var rt = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(rt.IsValid);
        Assert.True(rt.TryBeginDraw());
        rt.Clear(0.12f, 0.12f, 0.18f);

        nint layer = nint.Zero;
        try
        {
            // The rounded parent cuts the layer corners only while it is being
            // captured. After PopClip, the cached texture must still be complete.
            rt.PushRoundedRectClip(60f, 60f, 140f, 130f, 42f, 42f);
            layer = rt.RealizeLayerBegin(nint.Zero, 60f, 60f, 140f, 130f);
            Assert.NotEqual(nint.Zero, layer);
            rt.FillRectangle(60f, 60f, 140f, 130f, brush);
            rt.RealizeLayerEnd(layer);
            rt.PopClip();

            rt.CompositeLayer(layer, 60f, 60f, 140f, 130f, 1.0f);
            Assert.Equal(JaliumResult.Ok, rt.RequestReadback());
            Assert.Equal(JaliumResult.Ok, rt.TryEndDraw());
            Assert.Equal(JaliumResult.Ok, rt.FetchReadback(viaLayer, (uint)(Width * 4), out _, out _));
        }
        finally
        {
            if (layer != nint.Zero) rt.DestroyRetainedLayer(layer);
        }

        AssertBuffersClose(
            baseline,
            viaLayer,
            tolerance: 4,
            scenario: "Ancestor rounded clip was baked into the D3D12 retained layer");
    }
}
