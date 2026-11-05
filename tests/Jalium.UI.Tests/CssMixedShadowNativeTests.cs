using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMixedShadowNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_OuterAndInsetUseTheirOwnPaintLayers() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_OuterAndInsetUseTheirOwnPaintLayers() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_OuterAndInsetUseTheirOwnPaintLayers() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var border = new Border { Background = Brushes.Blue };

        byte[] Render(string shadow)
        {
            Css.SetStyle(border, "box-shadow:" + shadow);
            CssBorderRadiusTests.Layout(border, 80, 40);
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            drawing.Offset = new Point(40, 30);
            try { border.Render(drawing); }
            finally { drawing.Offset = default; }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Channel(byte[] pixels, int x, int y, int channel)
            => pixels[(y * width + x) * 4 + channel];

        foreach (var shadow in new[]
        {
            "12px 0 0 red, inset 0 0 8px green",
            "inset 0 0 8px green, 12px 0 0 red",
        })
        {
            var pixels = Render(shadow);
            Assert.True(Channel(pixels, 125, 50, 2) > 150,
                $"{backend}: outer shadow disappeared for '{shadow}'");
            Assert.True(Channel(pixels, 80, 32, 1) > 25,
                $"{backend}: inset shadow was covered by content for '{shadow}' " +
                $"(green={Channel(pixels, 80, 32, 1)})");
        }

        var layeredInset = Render("inset 0 0 8px rgb(255 0 0 / .5), inset 0 0 8px green");
        Assert.True(Channel(layeredInset, 80, 32, 2) > 20 &&
                    Channel(layeredInset, 80, 32, 1) > 20,
            $"{backend}: front inset shadow erased the rear inset shadow");

        var reversedInset = Render("inset 0 0 8px green, inset 0 0 8px rgb(255 0 0 / .5)");
        Assert.True(Channel(layeredInset, 80, 32, 2) >
                    Channel(reversedInset, 80, 32, 2) + 5,
            $"{backend}: first authored inset shadow did not paint on top");

        border.Child = new Border { Background = Brushes.Cyan };
        var contentAboveInset = Render("inset 0 0 8px red, 12px 0 0 red");
        Assert.True(Channel(contentAboveInset, 80, 32, 2) < 10,
            $"{backend}: inset shadow painted above an opaque child " +
            $"(red={Channel(contentAboveInset, 80, 32, 2)})");
        Assert.True(Channel(contentAboveInset, 125, 50, 2) > 150,
            $"{backend}: outer shadow disappeared behind child content");

        border.ClipToBounds = true;
        var clippedChild = Render("inset 0 0 8px red, 12px 0 0 red");
        Assert.True(Channel(clippedChild, 80, 32, 2) < 10 &&
                    Channel(clippedChild, 125, 50, 2) > 150,
            $"{backend}: the element layout clip changed child or outer-shadow paint order");
        border.ClipToBounds = false;

        var contentAboveSingleInset = Render("inset 0 0 8px red");
        Assert.True(Channel(contentAboveSingleInset, 80, 32, 2) < 10,
            $"{backend}: single inset shadow painted above an opaque child");

        border.Background = null;
        var transparentBackgroundChild = Render("inset 0 0 8px red");
        Assert.True(Channel(transparentBackgroundChild, 80, 32, 2) < 10 &&
                    Channel(transparentBackgroundChild, 80, 32, 1) > 200,
            $"{backend}: transparent parent lost or shadowed its child");

        border.Child = null;
        var uncoveredInset = Render("inset 0 0 8px red");
        Assert.True(Channel(uncoveredInset, 80, 32, 2) > 25,
            $"{backend}: removing the child also removed the inset shadow");
    }
}
