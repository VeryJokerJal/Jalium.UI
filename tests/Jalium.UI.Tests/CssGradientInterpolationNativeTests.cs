using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssGradientInterpolationNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_DefaultInterpolationMatchesWptReferenceSamples() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_DefaultInterpolationMatchesWptReferenceSamples() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_DefaultInterpolationMatchesWptReferenceSamples() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 480, height = 50;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.Equal(backend, context.Backend);

        byte[] Paint(string css)
        {
            var box = new Border { Width = width, Height = height };
            Css.SetStyle(box, "background:" + css);
            box.Measure(new(width, height)); box.Arrange(new(0, 0, width, height));
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                Assert.True(target.TryBeginDraw()); target.Clear(1, 1, 1, 1);
                using (var drawing = new RenderTargetDrawingContext(target, context)) box.Render(drawing);
                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        // Interior samples from the original WPT sRGB and Oklab 480x50 PNG
        // references. The HTML box is represented by an equal-size native Border.
        var cases = new (string Css, (int X, byte R, byte G, byte B)[] Samples)[]
        {
            ("linear-gradient(to right, rgb(255, 0, 0), rgb(0, 255, 0))",
                [(120, 191, 64, 0), (240, 127, 128, 0), (360, 63, 192, 0)]),
            ("linear-gradient(to right, rgb(255, 0, 0), color(srgb 0 1 0))",
                [(120, 237, 116, 0), (240, 208, 169, 0), (360, 160, 214, 0)]),
        };
        foreach (var (css, samples) in cases)
        {
            var pixels = Paint(css);
            foreach (var (x, red, green, blue) in samples)
            {
                var at = (25 * width + x) * 4;
                Assert.InRange(Math.Abs(pixels[at] - blue), 0, 4);
                Assert.InRange(Math.Abs(pixels[at + 1] - green), 0, 4);
                Assert.InRange(Math.Abs(pixels[at + 2] - red), 0, 4);
                Assert.Equal((byte)255, pixels[at + 3]);
            }
        }

        // WPT gradient-hue-direction: increasing/shorter and decreasing/longer
        // form two matching pairs, including their native interpolated pixels.
        foreach (var space in new[] { "hsl", "lch" })
        {
            byte[] Pair(string direction) => Paint($"linear-gradient(to right in {space} {direction} hue, red, orange)");
            var increasing = Pair("increasing"); var shorter = Pair("shorter");
            var decreasing = Pair("decreasing"); var longer = Pair("longer");
            foreach (var x in new[] { 120, 240, 360 })
            {
                var at = (25 * width + x) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    Assert.InRange(Math.Abs(increasing[at + channel] - shorter[at + channel]), 0, 2);
                    Assert.InRange(Math.Abs(decreasing[at + channel] - longer[at + channel]), 0, 2);
                }
            }
        }

        // CSS Images 4's 25% transition-hint example: the halfway blend is
        // violet at x=120 instead of at the geometric center.
        var hinted = Paint("linear-gradient(to right, red 0%, 25%, blue 100%)");
        foreach (var (x, red, blue) in new[] { (120, 128, 128), (240, 75, 180), (360, 34, 221) })
        {
            var at = (25 * width + x) * 4;
            Assert.InRange(Math.Abs(hinted[at] - blue), 0, 4);
            Assert.InRange(hinted[at + 1], (byte)0, (byte)3);
            Assert.InRange(Math.Abs(hinted[at + 2] - red), 0, 4);
            Assert.Equal((byte)255, hinted[at + 3]);
        }

        var single = Paint("linear-gradient(green)");
        foreach (var x in new[] { 40, 240, 440 })
        {
            var at = (25 * width + x) * 4;
            Assert.InRange(single[at], (byte)0, (byte)1);
            Assert.InRange(single[at + 1], (byte)127, (byte)129);
            Assert.InRange(single[at + 2], (byte)0, (byte)1);
            Assert.Equal((byte)255, single[at + 3]);
        }
    }
}
