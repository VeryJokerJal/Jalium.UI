using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class VelloOutputCapacityRenderingTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ReusedOutputCapacityPreservesTheLogicalRegionAndLayerOrder() => Verify(96);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ReusedOutputCapacityPreservesTheLogicalRegionAtFractionalDpi() => Verify(144);

    private static void Verify(float dpi)
    {
        float scale = dpi / 96;
        int width = (int)(640 * scale), height = (int)(256 * scale);
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, RenderingEngine.Vello);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var white = context.CreateSolidBrush(1, 1, 1);
        using var barrier = context.CreateSolidBrush(0, 0, 0, 0);
        using var red = context.CreateSolidBrush(1, 0, 0);
        target.SetDpi(dpi, dpi);

        // Cross capacity boundaries, then shrink and revisit nearby sizes on
        // both swap-chain slots. White texels left by a larger scene must never
        // stretch or bleed into the smaller scene's transparent area.
        (int W, int H)[] sizes = [(580, 220), (577, 217), (580, 220), (561, 201),
            (24, 28), (29, 31), (145, 93), (140, 89), (580, 220), (27, 30)];
        foreach (var size in sizes)
        {
            // Even DIP edges at 150% remain integral physical edges, retaining
            // the exact rectangle oracle while exercising fractional DPI UVs.
            int w = dpi == 96 ? size.W : size.W & ~1;
            int h = dpi == 96 ? size.H : size.H & ~1;
            target.SetFullInvalidation();
            long deadline = Environment.TickCount64 + 5000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline);
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0, 0);
            RectanglePath(8, 8, w, h, white);
            // An overlapping transparent graphic flushes the first output.
            // Keep the exact pixel oracle entirely in the path renderer: the
            // SDF rectangle has its own corner antialiasing at fractional DPI.
            target.FillRectangle(12, 12, 8, 8, barrier);
            // Cross the first path's right edge so the oracle also verifies
            // the draw order and independent lifetime of both output textures.
            RectanglePath(w + 2, 8, 12, 24, red);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok,
                target.FetchReadback(pixels, checked((uint)width * 4u), out _, out _));
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool first = x >= 8 * scale && x < (8 + w) * scale &&
                    y >= 8 * scale && y < (8 + h) * scale;
                bool second = x >= (w + 2) * scale && x < (w + 14) * scale &&
                    y >= 8 * scale && y < 32 * scale;
                int offset = (y * width + x) * 4;
                int b = first && !second ? 255 : 0;
                int g = first && !second ? 255 : 0;
                int r = first || second ? 255 : 0;
                int a = first || second ? 255 : 0;
                if (Math.Abs(pixels[offset] - b) > 1 || Math.Abs(pixels[offset + 1] - g) > 1 ||
                    Math.Abs(pixels[offset + 2] - r) > 1 || Math.Abs(pixels[offset + 3] - a) > 1)
                    Assert.Fail($"DPI {dpi}, scene {w}x{h}, pixel ({x},{y}): " +
                        $"expected BGRA ({b},{g},{r},{a}), got " +
                        $"({pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]}).");
            }
        }

        void RectanglePath(int x, int y, int w, int h, NativeBrush brush)
        {
            float[] commands = [0, x + w, y, 0, x + w, y + h, 0, x, y + h, 5];
            target.FillPath(x, y, commands, brush, fillRule: 0);
        }

    }
}
