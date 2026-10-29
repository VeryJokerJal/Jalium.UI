using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssRadialGeometryNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_CircularAndEllipticalExtentsUseTheirPaintedBox() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_CircularAndEllipticalExtentsUseTheirPaintedBox() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_CircularAndEllipticalExtentsUseTheirPaintedBox() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 200, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.Equal(backend, context.Backend);

        byte[] Paint(string css)
        {
            var box = new Border { Width = width, Height = height };
            Css.SetStyle(box, "background-image:" + css);
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

        static (int R, int G, int B) Pixel(byte[] pixels, int x, int y)
        {
            var at = (y * width + x) * 4;
            return (pixels[at + 2], pixels[at + 1], pixels[at]);
        }

        var circle = Paint("radial-gradient(circle 100px, red, blue)");
        var horizontal = Pixel(circle, 150, 50);
        var vertical = Pixel(circle, 100, 0);
        Assert.InRange(Math.Abs(horizontal.R - vertical.R), 0, 5);
        Assert.InRange(Math.Abs(horizontal.B - vertical.B), 0, 5);
        Assert.InRange(horizontal.R, 118, 140);
        Assert.InRange(horizontal.B, 118, 140);

        // Default farthest-corner ellipse: its 100% contour passes through
        // both opposite corners of the non-square gradient box.
        var ellipse = Paint("radial-gradient(red, blue)");
        var center = Pixel(ellipse, 100, 50);
        var corner = Pixel(ellipse, 1, 1);
        Assert.InRange(center.R, 245, 255); Assert.InRange(center.B, 0, 10);
        Assert.InRange(corner.R, 0, 16); Assert.InRange(corner.B, 238, 255);

        var closest = Paint("radial-gradient(closest-side at 20px 30px, red, yellow, green)");
        var origin = Pixel(closest, 20, 30);
        var edgeX = Pixel(closest, 40, 30);
        var edgeY = Pixel(closest, 20, 60);
        Assert.InRange(origin.R, 245, 255); Assert.InRange(origin.B, 0, 10);
        Assert.InRange(edgeX.R, 0, 12); Assert.InRange(edgeX.G, 120, 136); Assert.InRange(edgeX.B, 0, 12);
        Assert.InRange(edgeY.R, 0, 12); Assert.InRange(edgeY.G, 120, 136); Assert.InRange(edgeY.B, 0, 12);
    }
}
