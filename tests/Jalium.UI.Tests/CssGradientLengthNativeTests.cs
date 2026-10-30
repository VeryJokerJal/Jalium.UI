using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssGradientLengthNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ResolvesUsedGradientLengthsAndCornerGeometry() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ResolvesUsedGradientLengthsAndCornerGeometry() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_ResolvesUsedGradientLengthsAndCornerGeometry() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int targetWidth = 200, targetHeight = 100;
        using var window = new HiddenNativeWindow(targetWidth, targetHeight);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, targetWidth, targetHeight);
        Assert.Equal(backend, context.Backend);

        byte[] Paint(FrameworkElement element, int width, int height)
        {
            element.Measure(new(width, height));
            element.Arrange(new(0, 0, width, height)); element.UpdateLayout();
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                Assert.True(target.TryBeginDraw()); target.Clear(1, 1, 1, 1);
                using (var drawing = new RenderTargetDrawingContext(target, context)) element.Render(drawing);
                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var pixels = new byte[targetWidth * targetHeight * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, targetWidth * 4u, out _, out _));
            return pixels;
        }

        static (int R, int G, int B) Pixel(byte[] pixels, int x, int y)
        {
            var at = (y * targetWidth + x) * 4;
            return (pixels[at + 2], pixels[at + 1], pixels[at]);
        }

        var length = new Border { Width = 100, Height = 40 };
        Css.SetStyle(length, "background-image:linear-gradient(to right, red 0px, blue 100px)");
        var narrow = Paint(length, 100, 40);
        length.Width = 200;
        var wide = Paint(length, 200, 40);
        Assert.Equal(200, length.RenderSize.Width);
        var sourceBrush = Assert.IsType<LinearGradientBrush>(length.Background);
        Assert.NotNull(sourceBrush.CssGradientLayout);
        Assert.Equal(.5, Assert.IsType<LinearGradientBrush>(sourceBrush.CssGradientLayout.Resolve(200, 40))
            .GradientStops[^1].Offset, 8);
        var midpointNarrow = Pixel(narrow, 50, 20);
        var midpointWide = Pixel(wide, 50, 20);
        Assert.True(Math.Abs(midpointNarrow.R - midpointWide.R) <= 3,
            $"narrow={midpointNarrow}, wide={midpointWide}, after={Pixel(wide, 150, 20)}");
        Assert.InRange(Math.Abs(midpointNarrow.B - midpointWide.B), 0, 3);
        Assert.InRange(midpointWide.R, 120, 136);
        Assert.InRange(midpointWide.B, 120, 136);
        var afterBlue = Pixel(wide, 150, 20);
        Assert.InRange(afterBlue.R, 0, 2); Assert.InRange(afterBlue.B, 253, 255);

        // WPT linear-gradient-non-square: the 50% boundary passes through
        // top-right and bottom-left on a 200 x 100 box.
        var diagonal = new Border { Width = 200, Height = 100 };
        Css.SetStyle(diagonal,
            "background-image:linear-gradient(to right bottom, black 50%, lightgray 50%)");
        var corner = Paint(diagonal, 200, 100);
        var black = Pixel(corner, 120, 35);
        var gray = Pixel(corner, 80, 65);
        Assert.InRange(black.R, 0, 2); Assert.InRange(black.G, 0, 2);
        Assert.InRange(gray.R, 209, 213); Assert.InRange(gray.G, 209, 213);

        // WPT linear-gradient-calc-em-units: at y=40 the 30px-font gradient
        // is still blue, while the 10px-font gradient has started blending.
        byte[] FontGradient(double fontSize)
        {
            var text = new TextBlock { FontSize = fontSize, Width = 100, Height = 100 };
            Css.SetStyle(text, "background-image:linear-gradient(blue calc(2em), yellow)");
            return Paint(text, 100, 100);
        }
        var smallFont = Pixel(FontGradient(10), 50, 40);
        var largeFont = Pixel(FontGradient(30), 50, 40);
        Assert.True(smallFont.R > 20 && smallFont.G > 20);
        Assert.InRange(largeFont.R, 0, 3); Assert.InRange(largeFont.G, 0, 3);
        Assert.InRange(largeFont.B, 252, 255);
    }
}
