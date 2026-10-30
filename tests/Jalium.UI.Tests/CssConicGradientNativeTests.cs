using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssConicGradientNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsConicQuartersAndRoundedClip() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsConicQuartersAndRoundedClip() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsConicQuartersAndRoundedClip() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 101, height = 101;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.Equal(backend, context.Backend);

        byte[] Paint(string gradient, bool rounded)
        {
            var border = new Border { Width = width, Height = height };
            Css.SetStyle(border, "background-image:" + gradient + (rounded ? ";border-radius:50%" : ""));
            border.Measure(new(width, height));
            border.Arrange(new(0, 0, width, height));
            border.UpdateLayout();
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                Assert.True(target.TryBeginDraw());
                target.Clear(1, 1, 1, 1);
                using (var drawing = new RenderTargetDrawingContext(target, context)) border.Render(drawing);
                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static (int R, int G, int B) Pixel(byte[] pixels, int x, int y)
        {
            var at = (y * width + x) * 4;
            return (pixels[at + 2], pixels[at + 1], pixels[at]);
        }

        const string quarters = "conic-gradient(red 0deg 90deg, lime 90deg 180deg, " +
            "blue 180deg 270deg, yellow 270deg 360deg)";
        var image = Paint(quarters, rounded: false);
        Assert.Equal((255, 0, 0), Pixel(image, 75, 25));
        Assert.Equal((0, 255, 0), Pixel(image, 75, 75));
        Assert.Equal((0, 0, 255), Pixel(image, 25, 75));
        Assert.Equal((255, 255, 0), Pixel(image, 25, 25));

        var clipped = Paint(quarters, rounded: true);
        Assert.Equal((255, 255, 255), Pixel(clipped, 2, 2));
        Assert.Equal((255, 0, 0), Pixel(clipped, 75, 25));

        var repeated = Paint("repeating-conic-gradient(red 0deg, blue 90deg)", rounded: false);
        var first = Pixel(repeated, 75, 25);
        foreach (var (x, y) in new[] { (75, 75), (25, 75), (25, 25) })
        {
            var samePhase = Pixel(repeated, x, y);
            Assert.InRange(Math.Abs(first.R - samePhase.R), 0, 4);
            Assert.InRange(Math.Abs(first.B - samePhase.B), 0, 4);
        }
    }
}
