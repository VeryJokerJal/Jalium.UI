using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Pixel-level guard for the non-font half of icon rendering. A 15px compound
/// cubic ring exercises curve flattening, multi-figure EvenOdd fill, and edge
/// coverage through both rendering engines on both Windows GPU backends.
/// </summary>
[Collection("Application")]
public sealed class GpuSmallIconPathRenderingTests
{
    private const int Width = 40;
    private const int Height = 32;

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Impeller_SmallCubicIcon_IsCompleteAndAntialiased() =>
        AssertSmallCubicIconContract(RenderBackend.D3D12, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Vello_SmallCubicIcon_IsCompleteAndAntialiased() =>
        AssertSmallCubicIconContract(RenderBackend.D3D12, RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Impeller_SmallCubicIcon_IsCompleteAndAntialiased() =>
        AssertSmallCubicIconContract(RenderBackend.Vulkan, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Vello_SmallCubicIcon_IsCompleteAndAntialiased() =>
        AssertSmallCubicIconContract(RenderBackend.Vulkan, RenderingEngine.Vello);

    private static void AssertSmallCubicIconContract(
        RenderBackend backend,
        RenderingEngine engine)
    {
        var pixels = Render(backend, engine);
        var solid = 0;
        var partial = 0;
        var visible = 0;
        for (var y = 2; y < 23; y++)
        {
            for (var x = 5; x < 27; x++)
            {
                var intensity = IntensityAt(pixels, x, y);
                if (intensity > 4)
                    visible++;
                if (intensity >= 245)
                    solid++;
                else if (intensity >= 8)
                    partial++;
            }
        }

        Assert.True(
            visible >= 55,
            $"{backend}/{engine}: the 15px compound path is incomplete ({visible} visible pixels).");
        Assert.True(
            solid >= 14,
            $"{backend}/{engine}: the ring lost its solid core ({solid} solid pixels).");
        Assert.True(
            partial >= 18,
            $"{backend}/{engine}: curved icon edges are aliased ({partial} partial-coverage pixels).");
        Assert.True(
            IntensityAt(pixels, 16, 11) <= 4,
            $"{backend}/{engine}: EvenOdd inner hole was filled unexpectedly.");
    }

    private static byte[] Render(RenderBackend backend, RenderingEngine engine)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var white = context.CreateSolidBrush(1f, 1f, 1f, 1f);

        Assert.Equal(engine, context.DefaultRenderingEngine);
        target.SetDpi(96f, 96f);
        target.SetPathMsaaSampleCount(8);

        // Outer r=7.25 and inner r=4.65 cubic circles, both clockwise. EvenOdd
        // makes the inner contour a hole independent of winding direction.
        const float cx = 16f;
        const float cy = 11.5f;
        const float outer = 7.25f;
        const float inner = 4.65f;
        const float k = 0.55228475f;
        float[] commands =
        [
            1f, cx + outer, cy + k * outer, cx + k * outer, cy + outer, cx, cy + outer,
            1f, cx - k * outer, cy + outer, cx - outer, cy + k * outer, cx - outer, cy,
            1f, cx - outer, cy - k * outer, cx - k * outer, cy - outer, cx, cy - outer,
            1f, cx + k * outer, cy - outer, cx + outer, cy - k * outer, cx + outer, cy,
            5f,
            2f, cx + inner, cy,
            1f, cx + inner, cy + k * inner, cx + k * inner, cy + inner, cx, cy + inner,
            1f, cx - k * inner, cy + inner, cx - inner, cy + k * inner, cx - inner, cy,
            1f, cx - inner, cy - k * inner, cx - k * inner, cy - inner, cx, cy - inner,
            1f, cx + k * inner, cy - inner, cx + inner, cy - k * inner, cx + inner, cy,
            5f,
        ];

        for (var frame = 0; frame < 2; frame++)
        {
            target.SetFullInvalidation();
            Assert.True(TryBeginDrawWithRetry(target));
            target.Clear(0f, 0f, 0f);
            target.FillPath(cx + outer, cy, commands, white, fillRule: 0, edgeMode: 2);
            if (frame == 1)
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }

        var pixels = new byte[Width * Height * 4];
        Assert.Equal(
            JaliumResult.Ok,
            target.FetchReadback(pixels, Width * 4u, out var capturedWidth, out var capturedHeight));
        Assert.Equal(Width, capturedWidth);
        Assert.Equal(Height, capturedHeight);
        return pixels;
    }

    private static int IntensityAt(byte[] pixels, int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2]));
    }

    private static bool TryBeginDrawWithRetry(RenderTarget target)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            if (target.TryBeginDraw())
                return true;
            Thread.Sleep(1);
        }
        while (stopwatch.ElapsedMilliseconds < 250);

        return false;
    }
}
