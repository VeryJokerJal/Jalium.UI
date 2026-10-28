using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class D3D12GlyphDirtyRowUploadTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void Vello_InterleavedDirtyRowsMatchTheCachedTextFrame() => Verify(RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void Impeller_InterleavedDirtyRowsMatchTheCachedTextFrame() => Verify(RenderingEngine.Impeller);

    private static void Verify(RenderingEngine engine)
    {
        const int width = 512, height = 256;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var white = context.CreateSolidBrush(1, 1, 1);
        using var green = context.CreateSolidBrush(0, 1, 0);
        var formats = Enumerable.Range(0, 12)
            .Select(i => context.CreateTextFormat("Segoe UI", 16 + i * 10)).ToArray();
        try
        {
            target.SetDpi(96, 96);
            // Grow and populate the atlas. Subsequent additions start well past
            // row zero; a source/destination Y mix-up cannot pass as an initial
            // full-atlas upload. Revisit after growth invalidates memoized UVs.
            for (int frame = 0; frame < 7; frame++)
            {
                Begin();
                foreach (var format in formats)
                    target.DrawText("Atlas warmup HMWX 0123456789", format, 0, 0, width, height, white);
                FetchFrame();
            }
            Assert.True(target.TryQueryGpuStats(out var stats));
            Assert.True(stats.GlyphSlotsTotal > 256);

            var uploaded = DrawInterleavedFrame();
            var cached = DrawInterleavedFrame();
            Assert.Equal(uploaded, cached);
            for (int group = 0; group < 3; group++)
            {
                int ink = 0;
                for (int y = group * 80; y < (group + 1) * 80; y++)
                for (int x = 0; x < 450; x++)
                    if (uploaded[(y * width + x) * 4 + 3] > 10) ink++;
                Assert.True(ink > 40, $"Missing freshly uploaded text in row group {group}.");
            }

            byte[] DrawInterleavedFrame()
            {
                Begin();
                float[] square = [0, 12, 1, 0, 12, 12, 0, 1, 12, 5];
                for (int group = 0; group < 3; group++)
                {
                    target.DrawText($"Dirty rows {group}: alpha beta W国", formats[group],
                        8, 4 + group * 80, 430, 72, white);
                    target.PushTransformTranslation(480, 8 + group * 80);
                    target.FillPath(1, 1, square, white, fillRule: 0);
                    target.PopTransform();
                    // A graphics draw overlapping the pending Vello path forces
                    // a text upload before the next group adds new atlas rows.
                    target.FillRectangle(480, 8 + group * 80, 14, 14, green);
                }
                return FetchFrame();
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

            byte[] FetchFrame()
            {
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
                var pixels = new byte[width * height * 4];
                Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
                return pixels;
            }
        }
        finally
        {
            foreach (var format in formats) format.Dispose();
        }
    }
}
