using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssRelativeColorNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsRelativeColors() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsRelativeColors() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsRelativeColors() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 40, height = 40;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        var box = new Border { Width = width, Height = height };

        byte[] Render(string css)
        {
            Css.SetStyle(box, css);
            CssBorderRadiusTests.Layout(box, width, height);
            for (var frame = 0; frame < 2; frame++)
            {
                target.SetFullInvalidation();
                var deadline = Environment.TickCount64 + 2000;
                while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
                target.Clear(0, 0, 0, 1);
                using (var drawing = new RenderTargetDrawingContext(target, context))
                    box.Render(drawing);
                if (frame == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Channel(byte[] pixels, int x, int channel)
            => pixels[(20 * width + x) * 4 + channel];

        const string relative = "background-color:rgb(from currentColor r g calc(b / 2))";
        var blue = Render("color:blue;" + relative);
        Assert.InRange(Channel(blue, 20, 0), (byte)127, (byte)129);
        Assert.True(Channel(blue, 20, 1) < 3 && Channel(blue, 20, 2) < 3,
            $"{backend}: relative currentColor blue painted wrong channels");

        var red = Render("color:red;" + relative);
        Assert.True(Channel(red, 20, 2) > 250 && Channel(red, 20, 0) < 3,
            $"{backend}: relative currentColor did not update the painted brush");

        var gradient = Render("background-image:linear-gradient(to right in srgb," +
            "rgb(from red r g b),rgb(from blue r g b))");
        Assert.True(Channel(gradient, 2, 2) > 220 && Channel(gradient, 2, 0) < 35 &&
                    Channel(gradient, 37, 0) > 220 && Channel(gradient, 37, 2) < 35,
            $"{backend}: relative gradient stops did not reach native painting");
    }
}
