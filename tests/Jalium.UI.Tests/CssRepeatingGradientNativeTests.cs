using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssRepeatingGradientNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_RendersRepeatedAndAverageColors() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_RendersRepeatedAndAverageColors() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_RendersRepeatedAndAverageColors() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 100, height = 20;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.Equal(backend, context.Backend);

        byte[] Paint(string gradient)
        {
            var border = new Border { Width = width, Height = height };
            Css.SetStyle(border, "background-image:" + gradient);
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
            var result = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(result, width * 4u, out _, out _));
            return result;
        }

        static (int R, int G, int B) Pixel(byte[] data, int x, int y = 10)
        {
            var at = (y * width + x) * 4;
            return (data[at + 2], data[at + 1], data[at]);
        }

        var pixels = Paint("repeating-linear-gradient(to right, red 0px, blue 20px)");
        var first = Pixel(pixels, 5);
        foreach (var x in new[] { 25, 45, 65, 85 })
        {
            var repeated = Pixel(pixels, x);
            Assert.InRange(Math.Abs(first.R - repeated.R), 0, 4);
            Assert.InRange(Math.Abs(first.B - repeated.B), 0, 4);
        }
        Assert.True(Pixel(pixels, 18).B > 210);
        Assert.True(Pixel(pixels, 22).R > 210);

        var radial = Paint("repeating-radial-gradient(circle closest-side, red 0px, blue 20px)");
        var corner = Pixel(radial, 95, 18);
        Assert.True(corner.R > 130 && corner.B < 130, $"radial corner={corner}");

        var average = Pixel(Paint("repeating-linear-gradient(red 0px, white 0px, blue 0px)"), 50);
        Assert.InRange(average.R, 188, 194);
        Assert.InRange(average.G, 125, 131);
        Assert.InRange(average.B, 188, 194);
    }
}
