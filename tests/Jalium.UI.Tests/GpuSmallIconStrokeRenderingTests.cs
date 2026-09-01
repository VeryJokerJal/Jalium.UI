using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class GpuSmallIconStrokeRenderingTests
{
    private const int Width = 32;
    private const int Height = 32;

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Impeller_BackArrow_MatchesBrowserCoverageQuality() =>
        AssertBackArrowContract(RenderBackend.D3D12, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Vello_BackArrow_MatchesBrowserCoverageQuality() =>
        AssertBackArrowContract(RenderBackend.D3D12, RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Impeller_BackArrow_MatchesBrowserCoverageQuality() =>
        AssertBackArrowContract(RenderBackend.Vulkan, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Vello_BackArrow_MatchesBrowserCoverageQuality() =>
        AssertBackArrowContract(RenderBackend.Vulkan, RenderingEngine.Vello);

    private static void AssertBackArrowContract(
        RenderBackend backend,
        RenderingEngine engine)
    {
        var pixels = Render(backend, engine);
        var visible = 0;
        var partial = 0;
        var solid = 0;
        for (var y = 4; y < 28; y++)
        {
            for (var x = 4; x < 28; x++)
            {
                var value = IntensityAt(pixels, x, y);
                if (value > 4)
                    visible++;
                if (value is > 4 and < 251)
                    partial++;
                else if (value >= 251)
                    solid++;
            }
        }

        // The identical 18px SVG rendered by Chromium has 72 partial-coverage
        // pixels. The old Vulkan Impeller MSAA-stencil route produced only 38,
        // visibly quantizing each diagonal into a staircase. Analytic/Vello
        // output lands at 78-84 on this geometry.
        Assert.True(
            partial >= 70,
            $"{backend}/{engine}: back-arrow coverage is still quantized " +
            $"({partial} partial pixels; browser reference=72).");
        Assert.True(
            visible >= 90 && solid >= 18,
            $"{backend}/{engine}: back-arrow stroke is incomplete " +
            $"(visible={visible}, solid={solid}).");
        Assert.True(
            IntensityAt(pixels, 15, 16) >= 160,
            $"{backend}/{engine}: horizontal shaft lost its covered core.");
    }

    private static byte[] Render(RenderBackend backend, RenderingEngine engine)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var white = context.CreateSolidBrush(1f, 1f, 1f, 1f);

        Assert.Equal(engine, context.DefaultRenderingEngine);
        target.SetDpi(96f, 96f);
        target.SetPathMsaaSampleCount(4);

        // Exact Gallery DesktopBackButton path after Path.Stretch=Uniform fits
        // its source bounds (8,4)-(22,20), StrokeThickness=1.8, into 18x18,
        // then translates the slot to (7,7) in this capture.
        float[] transform = [1.0125f, 0f, 0f, 1.0125f, 0.8125f, 3.85f];
        float[] commands =
        [
            0f, 8f, 12f,
            0f, 18f, 20f,
            2f, 8f, 12f,
            0f, 22f, 12f,
        ];

        for (var frame = 0; frame < 2; frame++)
        {
            target.SetFullInvalidation();
            Assert.True(TryBeginDrawWithRetry(target));
            target.Clear(0f, 0f, 0f);
            target.PushTransform(transform);
            target.StrokePath(
                18f,
                4f,
                commands,
                white,
                strokeWidth: 1.8f,
                closed: false,
                lineJoin: 0,
                miterLimit: 10f,
                lineCap: 0,
                edgeMode: -1);
            target.PopTransform();
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
