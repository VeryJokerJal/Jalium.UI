using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Pixel-level coverage for the native glyph-atlas paths. Both backends must
/// rasterize at the render-target DPI and keep fixed, axis-aligned text stable
/// within one physical-pixel rounding bucket.
/// </summary>
[Collection("Application")]
public sealed class GpuTextDpiRenderingTests
{
    private const int Width = 512;
    private const int Height = 160;
    private const string TestText = "HMWX 012345";

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_FixedAliasedText_TracksDpiAndSnapsToPhysicalPixels() =>
        AssertHighDpiTextContract(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_FixedAliasedText_TracksDpiAndSnapsToPhysicalPixels() =>
        AssertHighDpiTextContract(RenderBackend.Vulkan);

    private static void AssertHighDpiTextContract(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var brush = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var format = context.CreateTextFormat("Segoe UI", 18f);

        Assert.Equal(backend, context.Backend);
        Assert.True(target.IsValid);
        Assert.True(brush.IsValid);
        Assert.True(format.IsValid);

        // Use the discrete display-text mode so the test exercises the fixed
        // path rather than an animation-oriented continuously positioned run.
        // The exact coverage values remain backend/DirectWrite dependent.
        format.SetTextRenderingMode(1); // TextRenderingMode.Aliased
        format.SetTextHintingMode(1);   // TextHintingMode.Fixed

        var dpi96 = RenderCapture(target, brush, format, dpi: 96f, x: 8.25f, y: 8.25f);
        var dpi144A = RenderCapture(target, brush, format, dpi: 144f, x: 8.10f, y: 8.10f);
        var dpi144B = RenderCapture(target, brush, format, dpi: 144f, x: 8.20f, y: 8.10f);
        var dpi192A = RenderCapture(target, brush, format, dpi: 192f, x: 8.10f, y: 8.10f);
        var dpi192B = RenderCapture(target, brush, format, dpi: 192f, x: 8.20f, y: 8.10f);

        Assert.True(dpi96.Bounds.Width > 0 && dpi96.Bounds.Height > 0,
            $"{backend}: 96-DPI text produced no visible pixels.");
        AssertScaleNear(backend, dpi96.Bounds, dpi144A.Bounds, dpi: 144, expectedScale: 1.5);
        AssertScaleNear(backend, dpi96.Bounds, dpi192A.Bounds, dpi: 192, expectedScale: 2.0);

        // Both origins fall into the same physical-pixel rounding bucket at
        // 150% and 200% DPI. A fixed display run must therefore be byte-
        // identical instead of drifting continuously between pixels.
        Assert.Equal(dpi144A.Pixels, dpi144B.Pixels);
        Assert.Equal(dpi192A.Pixels, dpi192B.Pixels);
    }

    private static void AssertScaleNear(
        RenderBackend backend,
        PixelBounds dpi96,
        PixelBounds scaled,
        int dpi,
        double expectedScale)
    {
        const double tolerance = 0.25;
        Assert.True(scaled.Width > dpi96.Width * (expectedScale - tolerance) &&
                    scaled.Width < dpi96.Width * (expectedScale + tolerance),
            $"{backend}: physical text width did not follow 96->{dpi} DPI " +
            $"({dpi96.Width}->{scaled.Width}).");
        Assert.True(scaled.Height > dpi96.Height * (expectedScale - tolerance) &&
                    scaled.Height < dpi96.Height * (expectedScale + tolerance),
            $"{backend}: physical text height did not follow 96->{dpi} DPI " +
            $"({dpi96.Height}->{scaled.Height}).");
    }

    private static TextCapture RenderCapture(
        RenderTarget target,
        NativeBrush brush,
        NativeTextFormat format,
        float dpi,
        float x,
        float y)
    {
        target.SetDpi(dpi, dpi);
        for (var frame = 0; frame < 2; frame++)
        {
            // Direct render-target tests do not have Window's dirty-region
            // coordinator. Force the same full repaint a DPI change requests
            // in production so Vulkan does not seed this frame from its
            // retained pre-DPI image.
            target.SetFullInvalidation();
            Assert.True(target.TryBeginDraw());
            target.Clear(0f, 0f, 0f);
            target.DrawText(TestText, format, x, y, 230f, 60f, brush);
            if (frame == 1)
            {
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            }

            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }

        var pixels = new byte[Width * Height * 4];
        Assert.Equal(
            JaliumResult.Ok,
            target.FetchReadback(pixels, Width * 4u, out var capturedWidth, out var capturedHeight));
        Assert.Equal(Width, capturedWidth);
        Assert.Equal(Height, capturedHeight);

        var minX = Width;
        var minY = Height;
        var maxX = -1;
        var maxY = -1;
        for (var py = 0; py < Height; py++)
        {
            for (var px = 0; px < Width; px++)
            {
                var offset = (py * Width + px) * 4;
                var intensity = Math.Max(pixels[offset],
                    Math.Max(pixels[offset + 1], pixels[offset + 2]));
                if (intensity <= 2)
                {
                    continue;
                }

                minX = Math.Min(minX, px);
                minY = Math.Min(minY, py);
                maxX = Math.Max(maxX, px);
                maxY = Math.Max(maxY, py);
            }
        }

        var bounds = maxX >= minX && maxY >= minY
            ? new PixelBounds(minX, minY, maxX - minX + 1, maxY - minY + 1)
            : new PixelBounds(0, 0, 0, 0);
        return new TextCapture(pixels, bounds);
    }

    private readonly record struct PixelBounds(int X, int Y, int Width, int Height);

    private readonly record struct TextCapture(byte[] Pixels, PixelBounds Bounds);
}
