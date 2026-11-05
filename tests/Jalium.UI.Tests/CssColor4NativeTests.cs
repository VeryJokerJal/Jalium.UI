using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssColor4NativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsWptGreenColorFunctions() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsWptGreenColorFunctions() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsWptGreenColorFunctions() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 40, height = 40;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.Equal(backend, context.Backend);

        foreach (var css in new[]
        {
            "hwb(120 0% 49.8039%)",
            "lab(46.2775% -47.5621 48.5837)",
            "lch(46.2775% 67.9892 134.3912)",
            "oklab(51.975% -0.1403 0.10768)",
            "oklch(51.975% 0.17686 142.495)",
        })
        {
            var box = new Border { Width = width, Height = height };
            Css.SetStyle(box, "background-color:" + css);
            box.Measure(new(width, height)); box.Arrange(new(0, 0, width, height));
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                Assert.True(target.TryBeginDraw()); target.Clear(1, 0, 0, 1);
                using (var drawing = new RenderTargetDrawingContext(target, context)) box.Render(drawing);
                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            var at = (20 * width + 20) * 4;
            Assert.InRange(pixels[at], (byte)0, (byte)1); // B
            Assert.InRange(pixels[at + 1], (byte)127, (byte)129); // G
            Assert.InRange(pixels[at + 2], (byte)0, (byte)1); // R
            Assert.Equal((byte)255, pixels[at + 3]);
        }
    }
}
