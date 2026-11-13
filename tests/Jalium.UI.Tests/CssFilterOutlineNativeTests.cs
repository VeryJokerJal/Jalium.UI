using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssFilterOutlineNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_FilterIncludesOutline() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_FilterIncludesOutline() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_FilterIncludesOutline() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var border = new Border { Background = Brushes.Blue };

        byte[] Render(string css)
        {
            Css.SetStyle(border, css);
            CssBorderRadiusTests.Layout(border, 80, 40);
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(1, 1, 1, 1);
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

        const string outline = "outline:4px solid red;outline-offset:4px;";
        var ordinary = Render(outline + "filter:none");
        Assert.True(Channel(ordinary, 34, 50, 2) > 150 &&
                    Channel(ordinary, 34, 50, 1) < 40,
            $"{backend}: unfiltered outline was not red");

        var filtered = Render(outline + "filter:brightness(0)");
        Assert.True(Channel(filtered, 34, 50, 2) < 20 &&
                    Channel(filtered, 34, 50, 1) < 20,
            $"{backend}: CSS outline bypassed brightness(0) " +
            $"(red={Channel(filtered, 34, 50, 2)})");
        Assert.True(Channel(filtered, 80, 50, 0) < 20,
            $"{backend}: filter stopped affecting the background");

        var blurred = Render(outline + "border-radius:20px / 10px;filter:blur(4px)");
        Assert.True(Channel(blurred, 29, 50, 1) + 10 <
                    Channel(ordinary, 29, 50, 1),
            $"{backend}: blur clipped the CSS outline apron");

        var combined = Render(outline +
            "box-shadow:12px 0 0 green;filter:brightness(0)");
        Assert.True(Channel(combined, 34, 50, 2) < 20 &&
                    Channel(combined, 125, 50, 1) < 20,
            $"{backend}: combined box-shadow and outline escaped the filter");
    }
}
