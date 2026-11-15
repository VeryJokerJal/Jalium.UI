using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class D3D12IdleCacheReclamationTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ActiveReclamationPreservesGlyphs_AndIdleReclamationRebuildsThem()
    {
        const int width = 192, height = 64;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.D3D12);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var white = context.CreateSolidBrush(1, 1, 1);
        using var format = context.CreateTextFormat("Segoe UI", 20);
        target.SetDpi(96, 96);

        var original = DrawTextFrame();
        Assert.True(target.TryQueryGpuStats(out var warm));
        Assert.True(warm.GlyphSlotsUsed > 0);

        // A scan can land between two continuously rendered frames. Keep the
        // atlas even if the next frame draws no text to repopulate its entries.
        target.ReclaimIdleResources();
        Begin();
        AssertGlyphCount(warm.GlyphSlotsUsed);
        target.ReclaimIdleResources(); // An open frame is also active.
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        Begin();
        AssertGlyphCount(warm.GlyphSlotsUsed);
        Assert.Equal(JaliumResult.Ok, target.RequestReadback());
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        Fetch(); // Wait for the actual frame fence before crossing the idle grace period.

        Thread.Sleep(2100);
        target.ReclaimIdleResources();
        target.ReclaimIdleResources();
        Begin();
        AssertGlyphCount(0);
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        Assert.Equal(original, DrawTextFrame());

        void AssertGlyphCount(int expected)
        {
            Assert.True(target.TryQueryGpuStats(out var stats));
            Assert.Equal(expected, stats.GlyphSlotsUsed);
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
            target.Clear(0, 0, 0, 0);
        }

        byte[] Fetch()
        {
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok,
                target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        byte[] DrawTextFrame()
        {
            Begin();
            target.DrawText("Cache lifetime", format, 4, 4, width - 8, height - 8, white);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            return Fetch();
        }
    }
}
