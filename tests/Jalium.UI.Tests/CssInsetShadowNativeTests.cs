using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssInsetShadowNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_InsetUsesPaddingContourAndSpread() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_InsetUsesPaddingContourAndSpread() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_InsetUsesPaddingContourAndSpread() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);

        // The feature gate is a real C ABI call, so a stale native payload cannot make this test
        // pass through the scalar fallback while the side interface is absent.
        target.SetFullInvalidation();
        var probeDeadline = Environment.TickCount64 + 2000;
        while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < probeDeadline); Thread.Sleep(1); }
        target.Clear(0, 0, 0, 1);
        var probe = new NativeEllipticalClip(new Rect(30, 30, 100, 40),
            new CssUsedBorderRadii(new(50, 10), new(50, 10), new(50, 10), new(50, 10)),
            ClipEdges.All);
        Assert.True(target.TryDrawInnerShadowEffectElliptical(
            30, 30, 100, 40, 8, 0, 0, 3, 1, 0, 0, 1, 0, 0, probe),
            $"{backend} did not expose the inset-shadow side interface");
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 255, 0)),
            BorderThickness = new Thickness(6),
        };
        void Style(int spread)
        {
            Css.SetStyle(border,
                $"border-radius:50px / 10px; box-shadow:inset 0 0 8px {spread}px red");
            CssBorderRadiusTests.Layout(border, 100, 40);
            Assert.Equal(spread, Assert.IsType<InnerShadowEffect>(border.Effect).SpreadRadius);
        }

        byte[] Render(bool roundedAncestor = false)
        {
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            if (roundedAncestor)
                drawing.PushClip(new RectangleGeometry(new Rect(55, 35, 50, 30), 12, 12));
            try
            {
                drawing.Offset = new Point(30, 30);
                border.Render(drawing);
            }
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

        Style(0);
        var ordinary = Render();
        Assert.True(Red(ordinary, 80, 38) > 25,
            $"{backend}: zero-offset inset shadow painted no inner edge");
        Assert.True(Red(ordinary, 80, 34) < 8,
            $"{backend}: inset shadow covered the green border outside the padding edge");
        Assert.True(Red(ordinary, 80, 50) < 8,
            $"{backend}: inset shadow leaked across the center");

        var clipped = Render(roundedAncestor: true);
        var unmaskedRed = 0;
        var maskedRed = 0;
        for (var y = 36; y < 64; ++y)
        for (var x = 36; x < 55; ++x)
        {
            unmaskedRed = Math.Max(unmaskedRed, Red(ordinary, x, y));
            maskedRed = Math.Max(maskedRed, Red(clipped, x, y));
        }
        Assert.True(unmaskedRed > 20 && maskedRed < 8,
            $"{backend}: parent rounded clip lost its mask (before={unmaskedRed}, after={maskedRed})");
        Assert.True(Red(clipped, 80, 38) > 25,
            $"{backend}: parent rounded clip erased the inset shadow inside its own bounds");

        Style(6);
        var expanded = Render();
        Assert.True(Red(expanded, 80, 43) > Red(ordinary, 80, 43) + 35,
            $"{backend}: positive spread did not widen the inset band");
        Assert.True(Red(expanded, 80, 34) < 8,
            $"{backend}: positive spread escaped the padding edge");

        Style(-6);
        var contracted = Render();
        Assert.True(Red(contracted, 80, 38) + 25 < Red(ordinary, 80, 38),
            $"{backend}: negative spread did not narrow the inset band");

        Style(0);
        var elliptical = Render();
        border.CornerRadius = new CornerRadius(50);
        var circular = Render();
        var ellipseAdvantage = 0;
        var circleAdvantage = 0;
        for (var y = 36; y < 64; ++y)
        for (var x = 36; x < 124; ++x)
        {
            var difference = Red(elliptical, x, y) - Red(circular, x, y);
            ellipseAdvantage = Math.Max(ellipseAdvantage, difference);
            circleAdvantage = Math.Max(circleAdvantage, -difference);
        }
        Assert.True(ellipseAdvantage > 20 && circleAdvantage > 20,
            $"{backend}: CSS/native corner takeover failed to change both inset arcs " +
            $"(ellipse={ellipseAdvantage}, circle={circleAdvantage})");

        border.ClearValue(Border.CornerRadiusProperty);
        border.Background = null;
        border.BorderBrush = null;
        border.BorderThickness = new Thickness(0);
        Style(4);
        var transparent = Render();
        Assert.True(Red(transparent, 80, 34) > 40,
            $"{backend}: inset shadow incorrectly depended on background alpha");
    }
}
