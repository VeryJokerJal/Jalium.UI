using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class RenderingEngineFrameBoundaryTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_CrossThreadEngineRequestsApplyAtTheNextFrameBoundary()
    {
        using var window = new HiddenNativeWindow(64, 64);
        using var context = new RenderContext(RenderBackend.D3D12);
        using var target = context.CreateRenderTarget(window.Hwnd, 64, 64);
        using var brush = context.CreateSolidBrush(0, 0, 1);
        target.SetRenderingEngine(RenderingEngine.Impeller);
        foreach (var next in new[] { RenderingEngine.Vello, RenderingEngine.Impeller })
        {
            Begin();
            var current = target.RenderingEngine;
            Task.Run(() => target.SetRenderingEngine(next)).GetAwaiter().GetResult();
            Assert.Equal(current, target.RenderingEngine);
            target.Clear(0, 0, 0, 0);
            target.FillRectangle(8, 8, 32, 32, brush);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[64 * 64 * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 64 * 4u, out _, out _));
            Assert.Equal(255, pixels[(16 * 64 + 16) * 4 + 3]);
            Assert.Equal(0, pixels[(48 * 64 + 48) * 4 + 3]);
            Begin();
            Assert.Equal(next, target.RenderingEngine);
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }

        void Begin()
        {
            target.SetFullInvalidation();
            long deadline = Environment.TickCount64 + 5000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline);
                Thread.Sleep(1);
            }
        }
    }
}
