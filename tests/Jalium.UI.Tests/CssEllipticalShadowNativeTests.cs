using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssEllipticalShadowNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_DropShadowKeepsBothCssRadiusAxes() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_DropShadowKeepsBothCssRadiusAxes() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_DropShadowKeepsBothCssRadiusAxes() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);

        // Pin the optional native side interface independently of the visual result.
        target.SetFullInvalidation();
        var deadline = Environment.TickCount64 + 2000;
        while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
        var probe = new NativeEllipticalClip(new Rect(30, 30, 100, 40),
            new CssUsedBorderRadii(new(50, 10), new(50, 10), new(50, 10), new(50, 10)),
            ClipEdges.All);
        Assert.True(target.TryDrawDropShadowEffectElliptical(
            30, 30, 100, 40, 1, 0, 0, 1, 0, 0, 1, 30, 30, probe),
            $"{backend} did not expose the elliptical effect side interface");
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());

        var border = new Border();
        Css.SetStyle(border,
            "background-color:blue; border-radius:50px / 10px; box-shadow:0 0 1px rgb(255 0 0)");
        CssBorderRadiusTests.Layout(border, 100, 40);
        Assert.IsType<DropShadowEffect>(border.Effect);

        byte[] Render()
        {
            target.SetFullInvalidation();
            var drawDeadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < drawDeadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            drawing.Offset = new Point(30, 30);
            border.Render(drawing);
            drawing.Offset = default;
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Red(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4 + 2];

        var css = Render();
        border.CornerRadius = new CornerRadius(50);
        var native = Render();

        // The two contours cross in each corner. Find the strongest red-only shadow difference in
        // each direction; the blue element contributes no red, so this cannot confuse content with
        // the shadow. Requiring both signs proves the result changed shape rather than opacity.
        var cssAdvantage = 0; var nativeAdvantage = 0;
        var cssPoint = default(Point); var nativePoint = default(Point);
        for (var py = 20; py < 80; py++)
        for (var px = 20; px < 140; px++)
        {
            var delta = Red(css, px, py) - Red(native, px, py);
            if (delta > cssAdvantage) { cssAdvantage = delta; cssPoint = new(px, py); }
            if (-delta > nativeAdvantage) { nativeAdvantage = -delta; nativePoint = new(px, py); }
        }
        Assert.True(cssAdvantage > 35,
            $"{backend}: ellipse never exceeded circle (max={cssAdvantage} at {cssPoint})");
        Assert.True(nativeAdvantage > 35,
            $"{backend}: circle never exceeded ellipse (max={nativeAdvantage} at {nativePoint})");
    }
}
