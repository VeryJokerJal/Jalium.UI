using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssBoxShadowOrderNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_FirstShadowPaintsOnTop() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_FirstShadowPaintsOnTop() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_FirstShadowPaintsOnTop() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var border = new Border { Background = Brushes.White };

        Css.SetStyle(border, "box-shadow:12px 0 0 red, 12px 0 0 blue, 12px 0 0 green");
        CssBorderRadiusTests.Layout(border, 80, 40);
        var group = Assert.IsType<EffectGroup>(border.Effect);
        Assert.Equal(3, group.Children.Count);
        Assert.Equal(Colors.Red.ToArgb(), Assert.IsType<DropShadowEffect>(group.Children[0]).Color.ToArgb());
        Assert.Equal(Colors.Blue.ToArgb(), Assert.IsType<DropShadowEffect>(group.Children[1]).Color.ToArgb());
        Assert.Equal(Colors.Green.ToArgb(), Assert.IsType<DropShadowEffect>(group.Children[2]).Color.ToArgb());

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

        var index = (50 * width + 126) * 4;
        var red = pixels[index + 2];
        var blue = pixels[index];
        Assert.True(red > 180 && blue < 50,
            $"{backend}: first CSS shadow should cover the second (red={red}, blue={blue})");
    }
}
