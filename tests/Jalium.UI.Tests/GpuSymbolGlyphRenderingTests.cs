using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class GpuSymbolGlyphRenderingTests
{
    private const int Width = 64;
    private const int Height = 64;
    private const string WebsiteGlyph = "\uEB41";

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Impeller_SmallWebsiteGlyph_PreservesInteriorDetail() =>
        AssertSmallWebsiteGlyphContract(RenderBackend.D3D12, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Vello_SmallWebsiteGlyph_PreservesInteriorDetail() =>
        AssertSmallWebsiteGlyphContract(RenderBackend.D3D12, RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Impeller_SmallWebsiteGlyph_PreservesInteriorDetail() =>
        AssertSmallWebsiteGlyphContract(RenderBackend.Vulkan, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Vello_SmallWebsiteGlyph_PreservesInteriorDetail() =>
        AssertSmallWebsiteGlyphContract(RenderBackend.Vulkan, RenderingEngine.Vello);

    private static void AssertSmallWebsiteGlyphContract(
        RenderBackend backend,
        RenderingEngine engine)
    {
        foreach (var dpi in new[] { 96f, 144f, 192f })
        {
            var pixels = Render(backend, engine, hintingMode: 0, dpi: dpi);
            var bounds = FindInkBounds(pixels);
            var minimumExtent = (int)Math.Floor(12 * dpi / 96f);
            Assert.True(
                bounds.Width >= minimumExtent && bounds.Height >= minimumExtent,
                $"{backend}/{engine} at {dpi:F0} DPI: 15-DIP Website glyph produced an " +
                $"incomplete {bounds.Width}x{bounds.Height} ink box.");

            // U+EB41 is a ring containing three tiny 'w' outlines. At 15ppem a
            // final-size strike used to quantize all three letters into one hard
            // horizontal row. Inspect only the central half-width/third-height so
            // the ring itself cannot satisfy the assertion: the interior artwork
            // must retain coverage on at least three distinct rows.
            var x0 = bounds.X + bounds.Width / 4;
            var x1 = bounds.X + (bounds.Width * 3 + 3) / 4;
            var y0 = bounds.Y + bounds.Height / 3;
            var y1 = bounds.Y + (bounds.Height * 2 + 2) / 3;
            var interiorInkRows = 0;
            for (var y = y0; y < y1; y++)
            {
                var rowHasInk = false;
                for (var x = x0; x < x1; x++)
                {
                    if (IntensityAt(pixels, x, y) <= 8)
                        continue;
                    rowHasInk = true;
                    break;
                }
                if (rowHasInk)
                    interiorInkRows++;
            }

            Assert.True(
                interiorInkRows >= 3,
                $"{backend}/{engine} at {dpi:F0} DPI: U+EB41 interior collapsed to " +
                $"{interiorInkRows} coverage row(s); the three 'w' shapes must remain distinguishable.");
        }
    }

    private static byte[] Render(
        RenderBackend backend,
        RenderingEngine engine,
        int hintingMode,
        float dpi)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var brush = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var format = context.CreateTextFormat("Segoe MDL2 Assets", 15f);

        Assert.Equal(engine, context.DefaultRenderingEngine);

        format.SetTextRenderingMode(2);
        format.SetTextHintingMode(hintingMode);
        target.SetDpi(dpi, dpi);

        for (var frame = 0; frame < 2; frame++)
        {
            target.SetFullInvalidation();
            Assert.True(TryBeginDrawWithRetry(target));
            target.Clear(0f, 0f, 0f);
            target.DrawText(WebsiteGlyph, format, 8f, 4f, 32f, 32f, brush);
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

    private static PixelBounds FindInkBounds(byte[] pixels)
    {
        var minX = Width;
        var minY = Height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (IntensityAt(pixels, x, y) <= 2)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX >= minX && maxY >= minY
            ? new PixelBounds(minX, minY, maxX - minX + 1, maxY - minY + 1)
            : default;
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

    private readonly record struct PixelBounds(int X, int Y, int Width, int Height);
}
