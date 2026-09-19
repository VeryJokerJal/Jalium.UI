using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Pixel-level contract of text under a live native transform on the Software
/// backend. The CPU glyph blitter used to apply the current matrix to the run
/// ORIGIN only: a pushed scale left glyph size and advances at 1x (the designer
/// zoom bug — text overflowing its scaled-down container) and a rotation drew
/// upright glyphs. Runs are now laid out in local space, rasterized at the
/// matrix's resolution and mapped through it.
/// </summary>
[Collection("Application")]
public sealed class SoftwareTextTransformTests
{
    private const int Width = 320;
    private const int Height = 220;

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ScaledText_ScalesInkExtent()
    {
        byte[] identity = RenderText(matrix: null);
        var (w1, h1) = InkExtent(identity);
        Assert.True(w1 > 8 && h1 > 4, $"identity run should leave ink (w={w1}, h={h1})");

        byte[] scaled = RenderText(new float[] { 3f, 0f, 0f, 3f, 0f, 0f });
        var (w3, h3) = InkExtent(scaled);

        Assert.True(w3 > w1 * 2.2 && w3 < w1 * 3.8,
            $"3x scale must scale the run width ~3x (identity={w1}px, scaled={w3}px)");
        Assert.True(h3 > h1 * 2.2 && h3 < h1 * 3.8,
            $"3x scale must scale the run height ~3x (identity={h1}px, scaled={h3}px)");
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_RotatedText_RunsVertically()
    {
        // 90° rotation about the origin, shifted right so the run stays on the
        // surface: x' = -y + 200, y' = x.
        byte[] rotated = RenderText(new float[] { 0f, 1f, -1f, 0f, 200f, 0f });
        var (w, h) = InkExtent(rotated);

        Assert.True(h > 8, $"rotated run should leave ink (w={w}, h={h})");
        Assert.True(h > w * 2,
            $"a 90°-rotated wide run must be tall, not upright (w={w}px, h={h}px)");
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_GdiMaskCache_IsByteIdenticalToFirstRender()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        using var renderTarget = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var white = context.CreateSolidBrush(0.85f, 0.92f, 1f, 0.8f);
        using var format = context.CreateTextFormat("Segoe UI", 18f);

        byte[] first = RenderAndRead();
        byte[] cached = RenderAndRead();
        Assert.Equal(first, cached);

        byte[] RenderAndRead()
        {
            Assert.True(renderTarget.TryBeginDraw());
            renderTarget.Clear(0.02f, 0.03f, 0.05f, 1f);
            renderTarget.DrawText(
                "Cached text — 中文 123",
                format,
                17.25f,
                21.5f,
                280f,
                160f,
                white);
            Assert.Equal(JaliumResult.Ok, renderTarget.RequestReadback());
            Assert.Equal(JaliumResult.Ok, renderTarget.TryEndDraw());
            var pixels = new byte[Width * Height * 4];
            Assert.Equal(
                JaliumResult.Ok,
                renderTarget.FetchReadback(
                    pixels, Width * 4u, out int capturedWidth, out int capturedHeight));
            Assert.Equal(Width, capturedWidth);
            Assert.Equal(Height, capturedHeight);
            return pixels;
        }
    }

    private static byte[] RenderText(float[]? matrix)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        Assert.Equal(RenderBackend.Software, context.Backend);

        using var renderTarget = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(renderTarget.IsValid);
        using var white = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var format = context.CreateTextFormat("Segoe UI", 20f);
        Assert.True(white.IsValid && format.IsValid);

        for (int frame = 0; frame < 2; frame++)
        {
            Assert.True(renderTarget.TryBeginDraw());
            renderTarget.Clear(0f, 0f, 0f, 1f);
            if (matrix != null) renderTarget.PushTransform(matrix);
            renderTarget.DrawText("MMMM", format, 10f, 10f, 300f, 100f, white);
            if (matrix != null) renderTarget.PopTransform();
            if (frame == 1)
            {
                Assert.Equal(JaliumResult.Ok, renderTarget.RequestReadback());
            }
            Assert.Equal(JaliumResult.Ok, renderTarget.TryEndDraw());
        }

        var pixels = new byte[Width * Height * 4];
        Assert.Equal(JaliumResult.Ok,
            renderTarget.FetchReadback(pixels, Width * 4u, out int capturedWidth, out int capturedHeight));
        Assert.True(capturedWidth >= Width && capturedHeight >= Height);
        return pixels;
    }

    private static (int w, int h) InkExtent(byte[] pixels)
    {
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int offset = (y * Width + x) * 4;
                int luma = (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3;
                if (luma > 40)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        return maxX < 0 ? (0, 0) : (maxX - minX + 1, maxY - minY + 1);
    }
}
