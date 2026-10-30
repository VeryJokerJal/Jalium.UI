using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssFilterChainNativeTests
{
    [Fact]
    public void MatrixChain_RetainsItsOrderedStagesWhenCloned()
    {
        var border = new Border();
        Css.SetStyle(border, "filter:brightness(2) contrast(.5)");
        var chain = Assert.IsType<CssColorMatrixChainEffect>(border.Effect);
        Assert.Equal(2, chain.Matrices.Length);
        var clone = Assert.IsType<CssColorMatrixChainEffect>(chain.Clone());
        Assert.Equal(chain.Matrices, clone.Matrices);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ColorFiltersApplyInOrder() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ColorFiltersApplyInOrder() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_ColorFiltersApplyInOrder() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var border = new Border { Background = Brushes.Red };

        (byte R, byte G, byte B) Render(string filter)
        {
            Css.SetStyle(border, "filter:" + filter);
            CssBorderRadiusTests.Layout(border, 80, 40);
            Assert.IsType<CssColorMatrixChainEffect>(border.Effect);

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
            var index = (50 * width + 80) * 4;
            return (pixels[index + 2], pixels[index + 1], pixels[index]);
        }

        var brightnessThenInvert = Render("brightness(.5) invert(1)");
        Assert.InRange(brightnessThenInvert.R, (byte)120, (byte)136);
        Assert.InRange(brightnessThenInvert.G, (byte)245, byte.MaxValue);
        Assert.InRange(brightnessThenInvert.B, (byte)245, byte.MaxValue);

        var invertThenBrightness = Render("invert(1) brightness(.5)");
        Assert.InRange(invertThenBrightness.R, (byte)0, (byte)8);
        Assert.InRange(invertThenBrightness.G, (byte)120, (byte)136);
        Assert.InRange(invertThenBrightness.B, (byte)120, (byte)136);

        var twoOpacities = Render("opacity(.5) opacity(.5)");
        Assert.InRange(twoOpacities.R, (byte)58, (byte)70);
        Assert.InRange(twoOpacities.G, (byte)0, (byte)8);
        Assert.InRange(twoOpacities.B, (byte)0, (byte)8);

        border.Background = new SolidColorBrush(Color.FromRgb(180, 0, 0));
        var clipped = Render("brightness(2) contrast(.5)");
        Assert.InRange(clipped.R, (byte)185, (byte)197);
        Assert.InRange(clipped.G, (byte)58, (byte)70);
        Assert.InRange(clipped.B, (byte)58, (byte)70);

        var reverseClipped = Render("contrast(.5) brightness(2)");
        Assert.InRange(reverseClipped.R, (byte)247, byte.MaxValue);
        Assert.InRange(reverseClipped.G, (byte)121, (byte)135);
        Assert.InRange(reverseClipped.B, (byte)121, (byte)135);

        border.Background = Brushes.Red;
        var longChain = Render(string.Join(' ', Enumerable.Repeat("opacity(.9)", 20)));
        Assert.InRange(longChain.R, (byte)24, (byte)38);
        Assert.InRange(longChain.G, (byte)0, (byte)8);
        Assert.InRange(longChain.B, (byte)0, (byte)8);

        var boundaryChain = Render(string.Join(' ', Enumerable.Repeat("opacity(.99)", 64)));
        Assert.InRange(boundaryChain.R, (byte)126, (byte)142);
        var fallbackChain = Render(string.Join(' ', Enumerable.Repeat("opacity(.99)", 65)));
        Assert.InRange(fallbackChain.R, (byte)124, (byte)140);
    }
}
