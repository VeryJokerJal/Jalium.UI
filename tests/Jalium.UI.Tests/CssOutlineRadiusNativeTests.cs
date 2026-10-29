using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssOutlineRadiusNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_OutlineKeepsBothCssRadiusAxes() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_OutlineKeepsBothCssRadiusAxes() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_OutlineKeepsBothCssRadiusAxes() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 140, height = 80;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var border = new Border();
        Css.SetStyle(border,
            "border-radius:50px / 10px; outline:2px solid lime; outline-offset:4px");
        CssBorderRadiusTests.Layout(border, 100, 40);

        byte[] Render()
        {
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline);
                Thread.Sleep(1);
            }

            target.Clear(0, 0, 0, 1);
            drawing.Offset = new Point(20, 20);
            border.Render(drawing);
            drawing.Offset = default;
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out var w, out var h));
            Assert.Equal(width, w);
            Assert.Equal(height, h);
            return pixels;
        }

        static byte Green(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4 + 1];

        var cssPixels = Render();
        // Local coordinates (9,0) lie on the 55x15 outline ellipse but more than two
        // pixels inside the old 25px circular projection. (20,-5) is the converse.
        Assert.True(Green(cssPixels, 29, 20) > 180, $"{backend}: elliptical contour pixel was absent");
        Assert.True(Green(cssPixels, 40, 15) < 40, $"{backend}: outline used the horizontal radius for Y");

        border.CornerRadius = new CornerRadius(50);
        var nativePixels = Render();
        Assert.True(Green(nativePixels, 29, 20) < 40, $"{backend}: native CornerRadius did not take over");
        Assert.True(Green(nativePixels, 40, 15) > 180, $"{backend}: native circular outline was not restored");

        border.ClearValue(Border.CornerRadiusProperty);
        Css.SetStyle(border, "border-radius:50px / 10px; outline:9px double lime");
        var doublePixels = Render();
        Assert.True(Green(doublePixels, 70, 12) > 180, $"{backend}: double outline outer band missing");
        Assert.True(Green(doublePixels, 70, 15) < 40, $"{backend}: double outline gap filled");
        Assert.True(Green(doublePixels, 70, 18) > 180, $"{backend}: double outline inner band missing");

        foreach (var (style, topLight) in new[]
                 {
                     ("inset", false), ("outset", true),
                 })
        {
            Css.SetStyle(border, $"border-radius:50px / 10px;outline:10px {style} #808080");
            var relief = Render();
            var top = Green(relief, 70, 15);
            var bottom = Green(relief, 70, 65);
            Assert.Equal(topLight, top > bottom);
            Assert.True(Green(relief, 11, 11) < 8, $"{backend}: {style} escaped its elliptical corner");
        }

        foreach (var (style, outerLight) in new[]
                 {
                     ("groove", false), ("ridge", true),
                 })
        {
            Css.SetStyle(border, $"border-radius:50px / 10px;outline:10px {style} #808080");
            var relief = Render();
            var topOuter = Green(relief, 70, 12);
            var topInner = Green(relief, 70, 17);
            var bottomOuter = Green(relief, 70, 68);
            var bottomInner = Green(relief, 70, 63);
            Assert.Equal(outerLight, topOuter > topInner);
            Assert.Equal(outerLight, bottomOuter < bottomInner);
            Assert.True(Green(relief, 11, 11) < 8, $"{backend}: {style} escaped its elliptical corner");
        }

        Css.SetStyle(border,
            "border-radius:50px / 10px;outline:10px groove #808080;outline-offset:-100px");
        var clamped = Render();
        Assert.True(Green(clamped, 70, 32) > 60, $"{backend}: negative offset lost the clamped outline");
        Assert.True(Green(clamped, 70, 25) < 8, $"{backend}: negative offset escaped the clamped outline");

        Css.SetStyle(border, "border-radius:0;outline:10px inset rgba(128,128,128,.5)");
        var translucent = Render();
        var joint = Green(translucent, 15, 15);
        var topMidpoint = Green(translucent, 70, 15);
        Assert.InRange(joint, (byte)35, (byte)48);
        Assert.True(Math.Abs(joint - topMidpoint) <= 8,
            $"{backend}: translucent side joint {joint} differs from side center {topMidpoint}");
    }
}
