using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssFilterDropShadowNativeTests
{
    private sealed class SmallShape : FrameworkElement
    {
        internal Brush Fill = Brushes.White;

        protected override void OnRender(DrawingContext drawingContext)
            => drawingContext.DrawRectangle(Fill, null, new Rect(20, 10, 20, 20));
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_DropShadowFollowsInputAlpha() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_DropShadowFollowsInputAlpha() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_DropShadowFollowsInputAlpha() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var shape = new SmallShape();
        Css.SetStyle(shape, "filter:drop-shadow(12px 0 1px red)");
        CssBorderRadiusTests.Layout(shape, 80, 40);
        Assert.True(Assert.IsType<DropShadowEffect>(shape.Effect).CssUsesAlphaMask);

        byte[] Render()
        {
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            drawing.Offset = new Point(40, 30);
            try { shape.Render(drawing); }
            finally { drawing.Offset = default; }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Red(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4 + 2];
        var opaque = Render();
        Assert.True(Red(opaque, 90, 50) > 100,
            $"{backend}: drop-shadow() did not paint the shifted input silhouette");
        Assert.True(Red(opaque, 125, 50) < 8,
            $"{backend}: drop-shadow() shaded the element box beyond its input alpha");

        shape.Fill = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
        shape.InvalidateVisual();
        var translucent = Render();
        Assert.True(Red(translucent, 90, 50) < Red(opaque, 90, 50) - 35 &&
                    Red(translucent, 90, 50) > Red(opaque, 90, 50) / 3,
            $"{backend}: shadow opacity did not follow source alpha " +
            $"(opaque={Red(opaque, 90, 50)}, translucent={Red(translucent, 90, 50)})");

        shape.Fill = Brushes.White;
        shape.InvalidateVisual();
        Css.SetStyle(shape, "box-shadow:12px 0 2px red;filter:none");
        CssBorderRadiusTests.Layout(shape, 80, 40);
        Assert.False(Assert.IsType<DropShadowEffect>(shape.Effect).CssUsesAlphaMask);
        var boxShadow = Render();
        Assert.True(Red(boxShadow, 125, 50) > 100,
            $"{backend}: box-shadow did not shade the element's full border box");

        Css.SetStyle(shape, "filter:drop-shadow(12px 0 6px red);box-shadow:none");
        CssBorderRadiusTests.Layout(shape, 80, 40);
        var blurred = Render();
        Assert.InRange(Red(blurred, 96, 50), (byte)15, (byte)120);
        Assert.True(Red(blurred, 125, 50) < 8,
            $"{backend}: blurred filter shadow escaped far beyond the input silhouette");

        Css.SetStyle(shape, "filter:drop-shadow(-12px 0 1px red);box-shadow:none");
        CssBorderRadiusTests.Layout(shape, 80, 40);
        var leftShadow = Render();
        Assert.True(Red(leftShadow, 50, 50) > 100 && Red(leftShadow, 125, 50) < 8,
            $"{backend}: negative offset sampled the wrong capture region");
    }
}
