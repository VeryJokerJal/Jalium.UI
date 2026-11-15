using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssOuterShadowNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_SpreadChangesThePaintedPerimeter() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_SpreadChangesThePaintedPerimeter() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_SpreadChangesThePaintedPerimeter() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);

        target.SetFullInvalidation();
        var probeDeadline = Environment.TickCount64 + 2000;
        while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < probeDeadline); Thread.Sleep(1); }
        target.Clear(0, 0, 0, 1);
        var probe = new NativeEllipticalClip(new Rect(32, 22, 96, 56),
            new CssUsedBorderRadii(default, default, default, default), ClipEdges.All);
        Assert.True(target.TryDrawDropShadowEffectSpreadElliptical(
            40, 30, 80, 40, 0, 0, 0, 1, 0, 0, 1, 0, 0, probe),
            $"{backend} did not expose the outer-spread side interface");
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());

        var border = new Border { Background = Brushes.White };
        byte[] Render(string shadow, string radius = "0", bool asFilter = false,
            bool asFilterBlur = false, double cssFontSize = 0, bool roundedAncestor = false)
        {
            var font = cssFontSize > 0 ? $"font-size:{cssFontSize}px;" : "";
            Css.SetStyle(border, font + (asFilterBlur
                ? $"border-radius:{radius};box-shadow:none;filter:blur({shadow})"
                : asFilter
                    ? $"border-radius:{radius};box-shadow:none;filter:drop-shadow({shadow})"
                    : $"border-radius:{radius};filter:none;box-shadow:{shadow}"));
            CssBorderRadiusTests.Layout(border, 80, 40);
            if (asFilterBlur) Assert.IsType<BlurEffect>(border.Effect);
            else Assert.IsType<DropShadowEffect>(border.Effect);

            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            if (roundedAncestor)
                drawing.PushClip(new RectangleGeometry(new Rect(32, 35, 100, 30), 12, 12));
            drawing.Offset = new Point(40, 30);
            try { border.Render(drawing); }
            finally
            {
                drawing.Offset = default;
                if (roundedAncestor) drawing.Pop();
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Red(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4 + 2];

        var ordinary = Render("0 0 0 red");
        var expanded = Render("0 0 0 8px red");
        Assert.True(Red(ordinary, 35, 50) < 8 && Red(expanded, 35, 50) > 150,
            $"{backend}: positive spread did not expand the outer perimeter");
        Assert.True(Red(expanded, 30, 50) < 8,
            $"{backend}: positive spread painted beyond its perimeter");

        var offset = Render("20px 0 0 red");
        var contracted = Render("20px 0 0 -8px red");
        Assert.True(Red(offset, 135, 50) > 150 && Red(contracted, 135, 50) < 8,
            $"{backend}: negative spread did not contract the outer perimeter " +
            $"(ordinary={Red(offset, 135, 50)}, contracted={Red(contracted, 135, 50)})");
        Assert.True(Red(contracted, 125, 50) > 150,
            $"{backend}: negative spread removed the entire shifted shadow");

        var rounded = Render("0 0 0 8px red", "20px 10px");
        Assert.True(Red(rounded, 35, 50) > 150 && Red(rounded, 33, 23) < 8,
            $"{backend}: spread lost the rounded corner contour");

        var elliptical = Render("0 0 0 8px red", "20px / 10px");
        var circular = Render("0 0 0 8px red", "20px / 20px");
        Assert.True(Red(elliptical, 35, 35) > Red(circular, 35, 35) + 50,
            $"{backend}: spread lost the independent vertical corner radius");

        border.Background = null;
        var transparent = Render("0 0 0 8px red", "20px / 10px");
        Assert.True(Red(transparent, 80, 50) < 8 && Red(transparent, 35, 50) > 150,
            $"{backend}: an outer shadow leaked inside a transparent border box " +
            $"(inside={Red(transparent, 80, 50)}, outside={Red(transparent, 35, 50)})");
        Assert.True(Red(transparent, 50, 35) < 8 && Red(transparent, 45, 32) > 100,
            $"{backend}: outer-shadow knockout lost the elliptical corner");
        var transparentOffset = Render("12px 0 0 red");
        Assert.True(Red(transparentOffset, 80, 50) < 8 &&
                    Red(transparentOffset, 125, 50) > 150,
            $"{backend}: offset outer shadow crossed the original border-box interior");
        var ancestorClipped = Render("0 0 0 8px red", "20px / 10px", roundedAncestor: true);
        Assert.True(Red(ancestorClipped, 35, 50) > 150 &&
                    Red(ancestorClipped, 30, 50) < 8 &&
                    Red(ancestorClipped, 80, 50) < 8,
            $"{backend}: outer knockout lost the ancestor rounded clip");
        border.Background = Brushes.White;

        // 12px box-shadow blur radius and 6px filter standard deviation should
        // produce the same long straight-edge falloff on all three backends.
        var boxBlur = Render("0 0 12px red");
        var filterBlur = Render("0 0 6px red", asFilter: true);
        var boxEdge = Red(boxBlur, 125, 50);
        var filterEdge = Red(filterBlur, 125, 50);
        Assert.True(boxEdge is >= 25 and <= 75,
            $"{backend}: unexpected box blur edge {boxEdge}; " +
            $"samples={string.Join(',', Enumerable.Range(120, 11).Select(x => Red(boxBlur, x, 50)))}");
        Assert.True(Math.Abs(boxEdge - filterEdge) <= 18,
            $"{backend}: box radius and filter sigma disagree (box={boxEdge}, filter={filterEdge})");

        var blurPixels = Render("6px", asFilterBlur: true);
        var blurEdge = Red(blurPixels, 125, 50);
        Assert.True(blurEdge is >= 25 and <= 75,
            $"{backend}: unexpected filter blur edge {blurEdge}; " +
            $"samples={string.Join(',', Enumerable.Range(120, 21).Select(x => Red(blurPixels, x, 50)))}");
        Assert.True(Math.Abs(blurEdge - boxEdge) <= 20,
            $"{backend}: filter blur sigma disagrees with the box shadow (blur={blurEdge}, shadow={boxEdge})");

        var roundedBlur = Render("6px", "20px", asFilterBlur: true);
        Assert.True(Red(roundedBlur, 38, 35) > 8,
            $"{backend}: filter blur was clipped to the rounded element border");

        border.Background = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
        var translucentBlur = Render("6px", asFilterBlur: true);
        var translucentEdge = Red(translucentBlur, 125, 50);
        Assert.True(translucentEdge >= 8 && translucentEdge < blurEdge - 10,
            $"{backend}: translucent blur fringe is not alpha-scaled " +
            $"(opaque={blurEdge}, translucent={translucentEdge})");

        border.Background = Brushes.White;
        var smallEmSpread = Render("0 0 0 .4em red", cssFontSize: 20);
        var largeEmSpread = Render("0 0 0 .4em red", cssFontSize: 30);
        Assert.True(Red(smallEmSpread, 30, 50) < 8 && Red(largeEmSpread, 30, 50) > 150,
            $"{backend}: em-based outer spread did not follow font size");
    }
}
