using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class GlyphMotionRenderingTests
{
    private const int Width = 1600;
    private const int Height = 200;
    private const string Separator = "       ";

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void IdealText_SubpixelScroll_MovesEveryGlyphContinuously() =>
        CheckMotion(zoom: false, longRun: false);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void IdealText_HoverZoom_KeepsIndividualGlyphSpacingContinuous() =>
        CheckMotion(zoom: true, longRun: false);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void LongText_AtlasTiles_PreserveGlyphsDuringScrolling() =>
        CheckMotion(zoom: false, longRun: true);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void GradientText_AfterSolidTextCache_KeepsItsColourVariation()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.D3D12);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        target.SetDpi(96, 96);
        using var format = CreateFormat(context);
        using var white = context.CreateSolidBrush(1, 1, 1, 1);
        const string text = "MMMMMMMMMMMMMMMMMMMM";
        var textWidth = format.MeasureText(text, Width - 100, 100).Width;
        using var gradient = context.CreateLinearGradientBrush(40, 40, 40 + textWidth, 40,
            [0, 1, 0, 0, 1, 1, 0, 0, 1, 1], 2);

        // Reusing a cached glyph mask must not flatten the gradient:
        // brush colouring is independent of the mask's rasterization.
        Render(target, format, white, text, 40, 40, 1);
        var pixels = Render(target, format, gradient, text, 40, 40, 1);
        double leftRed = 0, leftBlue = 0, rightRed = 0, rightBlue = 0;
        for (var y = 30; y < 90; y++)
        for (var x = 40; x < 40 + textWidth; x++)
        {
            var i = (y * Width + x) * 4;
            // D3D12 readback is BGRA.
            if (x < 40 + textWidth / 3) { leftRed += pixels[i + 2]; leftBlue += pixels[i]; }
            if (x > 40 + textWidth * 2 / 3) { rightRed += pixels[i + 2]; rightBlue += pixels[i]; }
        }
        Assert.True(leftRed > 2 * leftBlue && rightBlue > 2 * rightRed,
            $"The gradient collapsed into one colour after reusing the text layout cache: left=({leftRed},{leftBlue}), right=({rightRed},{rightBlue}).");
    }

    private static void CheckMotion(bool zoom, bool longRun)
    {
        string[] samples = longRun
            ? [string.Join(Separator, Enumerable.Repeat("H", 18))]
            : [string.Join(Separator, Enumerable.Repeat("i", 5)),
               string.Join(Separator, "H", "é", "国", "A", "文"),
               string.Join(Separator, "H", "e\u0301", "国", "ش", "🙂")];

        foreach (var dpi in new[] { 96f, 120f, 144f, 192f })
        {
            // A fresh atlas also exercises long-run tiling at the initial size.
            using var window = new HiddenNativeWindow(Width, Height);
            using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, RenderingEngine.Vello);
            using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
            target.SetDpi(dpi, dpi);
            using var format = CreateFormat(context);
            using var brush = context.CreateSolidBrush(1, 1, 1, 1);
            var dpiScale = dpi / 96d;

            foreach (var text in samples)
            foreach (var horizontal in zoom ? new[] { true } : new[] { true, false })
            {
                var expectedCount = text.Split(Separator).Length;
                List<Point>? rest = null;
                double[]? previousErrors = null;
                for (var step = 0; step <= 16; step++)
                {
                    var shift = zoom ? 0 : step * .125 / dpiScale;
                    var scale = zoom ? 1f + step * .03f / 16 : 1f;
                    var x = (float)(40 + (horizontal ? shift : 0));
                    var y = (float)(40 + (horizontal ? 0 : shift));
                    var pixels = Render(target, format, brush, text, x, y, scale);
                    var current = GlyphCenters(pixels);
                    Assert.Equal(expectedCount, current.Count);
                    if (rest == null) { rest = current; continue; }
                    var errors = new double[current.Count];
                    for (var glyph = 0; glyph < current.Count; glyph++)
                    {
                        errors[glyph] = zoom
                            ? current[glyph].X - rest[glyph].X * scale
                            : horizontal
                                ? current[glyph].X - rest[glyph].X - shift * dpiScale
                                : current[glyph].Y - rest[glyph].Y - shift * dpiScale;
                        Assert.True(Math.Abs(errors[glyph]) < .32,
                            $"DPI {dpi}, zoom={zoom}, horizontal={horizontal}, step {step}, glyph {glyph}: " +
                            $"position error {errors[glyph]:F3}px in '{text}'.");
                        if (previousErrors != null)
                        {
                            var jump = errors[glyph] - previousErrors[glyph];
                            Assert.True(Math.Abs(jump) < .28,
                                $"DPI {dpi}, step {step}, glyph {glyph}: relative jump {jump:F3}px in '{text}'.");
                        }
                    }
                    previousErrors = errors;
                }
            }
        }
    }

    private static NativeTextFormat CreateFormat(RenderContext context)
    {
        var format = context.CreateTextFormat("Segoe UI", 14);
        format.SetTextFormattingMode((int)TextFormattingMode.Ideal);
        format.SetTextRenderingMode((int)TextRenderingMode.Grayscale);
        format.SetTextHintingMode((int)TextHintingMode.Auto);
        format.SetSubpixelPositioning(true);
        return format;
    }

    private static byte[] Render(RenderTarget target, NativeTextFormat format, NativeBrush brush,
        string text, float x, float y, float scale)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            target.SetFullInvalidation();
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!target.TryBeginDraw())
            {
                Assert.True(wait.ElapsedMilliseconds < 3000, "Render target stayed busy.");
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0);
            if (scale != 1) target.PushTransform([scale, 0, 0, scale, 0, 0]);
            target.DrawText(text, format, x, y, Width - 100, 100, brush);
            if (scale != 1) target.PopTransform();
            if (pass == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }
        var pixels = new byte[Width * Height * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, Width * 4u, out _, out _));
        return pixels;
    }

    private static List<Point> GlyphCenters(byte[] pixels)
    {
        var columns = new double[Width];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var i = (y * Width + x) * 4;
            columns[x] += Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
        }
        var groups = new List<(int Left, int Right)>();
        var start = -1;
        var last = -1;
        for (var x = 0; x < Width; x++)
        {
            if (columns[x] > 1) { if (start < 0) start = x; last = x; }
            else if (start >= 0 && x - last > 7) { groups.Add((start, last + 1)); start = -1; }
        }
        if (start >= 0) groups.Add((start, last + 1));
        var result = new List<Point>();
        foreach (var group in groups)
        {
            double ink = 0, momentX = 0, momentY = 0;
            for (var x = group.Left; x < group.Right; x++)
            for (var y = 0; y < Height; y++)
            {
                var i = (y * Width + x) * 4;
                var coverage = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
                ink += coverage;
                momentX += (x + .5) * coverage;
                momentY += (y + .5) * coverage;
            }
            Assert.True(ink > 0);
            result.Add(new Point(momentX / ink, momentY / ink));
        }
        return result;
    }
}
