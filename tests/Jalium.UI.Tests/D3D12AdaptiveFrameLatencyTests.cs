using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class D3D12AdaptiveFrameLatencyTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_DefaultSwapChainStartsWithOneFrame()
    {
        WithOverride(null, () =>
        {
            using var window = new HiddenNativeWindow(256, 128);
            using var context = new RenderContext(RenderBackend.D3D12);
            using var target = context.CreateRenderTarget(window.Hwnd, 256, 128);
            var info = target.GetPresentInfo();
            Assert.NotNull(info);
            Assert.Equal(1, info.Value.MaxFrameLatency);
        });
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_HostLatencyOverridesArePreserved()
    {
        foreach (int requested in new[] { 1, 2, 9 })
        {
            WithOverride(requested.ToString(), () =>
            {
                using var window = new HiddenNativeWindow(256, 128);
                using var context = new RenderContext(RenderBackend.D3D12);
                using var target = context.CreateRenderTarget(window.Hwnd, 256, 128);
                var info = target.GetPresentInfo();
                Assert.NotNull(info);
                Assert.Equal(Math.Min(requested, info.Value.BufferCount), info.Value.MaxFrameLatency);
            });
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ImpellerTwoQueuedFramesKeepTheirGpuResourcesAlive() => VerifyFrames(RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_VelloTwoQueuedFramesKeepTheirGpuResourcesAlive() => VerifyFrames(RenderingEngine.Vello);

    private static void VerifyFrames(RenderingEngine engine)
    {
        WithOverride("2", () =>
        {
            foreach (bool composition in new[] { false, true })
            {
                const int width = 256, height = 128;
                using var window = new HiddenNativeWindow(width, height);
                using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, engine);
                using var target = composition
                    ? context.CreateRenderTargetForComposition(window.Hwnd, width, height)
                    : context.CreateRenderTarget(window.Hwnd, width, height);
                using var red = context.CreateSolidBrush(1, 0, 0);
                using var white = context.CreateSolidBrush(1, 1, 1);
                Assert.Equal(2, target.GetPresentInfo()!.Value.MaxFrameLatency);
                for (int frame = 0; frame < 32; frame++)
                {
                    long deadline = Environment.TickCount64 + 5000;
                    while (!target.TryBeginDraw())
                    {
                        Assert.True(Environment.TickCount64 < deadline);
                        Thread.Sleep(1);
                    }
                    target.Clear(0, 0, 0, 1);
                    int extent = frame == 31 ? 196 : 180 + frame % 31;
                    float[] points = [8, 8, extent, 8, extent, 104, 8, 104];
                    // More than 160px wide keeps the Vello case on its compute
                    // path. Capacity changes and an overlapping SDF draw force
                    // multiple dispatches; prior frames are not read back/drained.
                    target.FillPolygon(points, white, 1);
                    target.FillRectangle(12, 12, 8, 8, red);
                    target.FillPolygon(points, red, 1);
                    if (frame == 31) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                    Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                }
                var pixels = new byte[width * height * 4];
                Assert.Equal(JaliumResult.Ok,
                    target.FetchReadback(pixels, width * 4u, out _, out _));
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width + x) * 4;
                    bool inside = x >= 8 && x < 196 && y >= 8 && y < 104;
                    Assert.Equal((byte)0, pixels[index]);
                    Assert.Equal((byte)0, pixels[index + 1]);
                    Assert.Equal(inside ? (byte)255 : (byte)0, pixels[index + 2]);
                    Assert.Equal((byte)255, pixels[index + 3]);
                }
            }
        });
    }

    private static void WithOverride(string? value, Action action)
    {
        const string name = "JALIUM_MAX_FRAME_LATENCY";
        string? previous = Environment.GetEnvironmentVariable(name);
        try { Environment.SetEnvironmentVariable(name, value); action(); }
        finally { Environment.SetEnvironmentVariable(name, previous); }
    }
}
