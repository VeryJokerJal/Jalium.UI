using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssPanelBorderNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsPanelBorder() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsPanelBorder() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsPanelBorder() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 80, height = 60;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.Equal(backend, context.Backend);

        byte[] Paint(FrameworkElement element, string style)
        {
            Css.SetStyle(element, style);
            CssBorderRadiusTests.Layout(element, width, height);
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                Assert.True(target.TryBeginDraw());
                target.Clear(0, 0, 0, 1);
                using (var drawing = new RenderTargetDrawingContext(target, context))
                    element.Render(drawing);
                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok,
                target.FetchReadback(pixels, width * 4u, out var readWidth, out var readHeight));
            Assert.Equal(width, readWidth);
            Assert.Equal(height, readHeight);
            return pixels;
        }

        var sides = Paint(new Grid(),
            "display:grid; border:10px solid; border-color:red green blue yellow; border-radius:20px");
        static (byte Blue, byte Green, byte Red) ColorAt(byte[] pixels, int x, int y)
        {
            var offset = (y * width + x) * 4;
            return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
        }
        Assert.Equal(((byte)0, (byte)0, (byte)255), ColorAt(sides, 40, 5));
        Assert.Equal(((byte)0, (byte)128, (byte)0), ColorAt(sides, 75, 30));
        Assert.Equal(((byte)255, (byte)0, (byte)0), ColorAt(sides, 40, 55));
        Assert.Equal(((byte)0, (byte)255, (byte)255), ColorAt(sides, 5, 30));
        Assert.Equal(((byte)0, (byte)0, (byte)0), ColorAt(sides, 0, 0));

        var doubleBorder = Paint(new Grid(),
            "display:grid; border:9px double red; border-radius:20px");
        Assert.Equal((byte)255, ColorAt(doubleBorder, 40, 1).Red);
        Assert.Equal((byte)0, ColorAt(doubleBorder, 40, 4).Red);
        Assert.Equal((byte)255, ColorAt(doubleBorder, 40, 7).Red);

        var text = Paint(new TextBlock { Text = "X" },
            "border:8px solid red; padding:4px; background-color:blue; " +
            "background-clip:content-box");
        Assert.Equal((byte)255, ColorAt(text, 40, 4).Red);
        Assert.Equal(((byte)0, (byte)0, (byte)0), ColorAt(text, 40, 10));
        Assert.Equal((byte)255, ColorAt(text, 40, 20).Blue);
    }
}
