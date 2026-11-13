using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssShadowFilterCompositionNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_FilterConsumesPaintedShadowAndContent() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_FilterConsumesPaintedShadowAndContent() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_FilterConsumesPaintedShadowAndContent() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var border = new Border { Background = Brushes.Blue };

        byte[] Render(string css, bool recorded = false, bool roundedAncestor = false)
        {
            Css.SetStyle(border, css);
            CssBorderRadiusTests.Layout(border, 80, 40);
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(1, 1, 1, 1);
            if (roundedAncestor)
                drawing.PushClip(new RectangleGeometry(new Rect(32, 35, 100, 30), 12, 12));
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                ((IOffsetDrawingContext)recorder).Offset = new Point(40, 30);
                border.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(40, 30);
                try { border.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            if (roundedAncestor) drawing.Pop();
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Channel(byte[] pixels, int x, int y, int channel)
            => pixels[(y * width + x) * 4 + channel];

        var ordinary = Render("box-shadow:12px 0 0 red;filter:none");
        Assert.True(Channel(ordinary, 125, 50, 2) > 150,
            $"{backend}: unfiltered outer shadow was not red");

        var filtered = Render("box-shadow:12px 0 0 red;filter:brightness(0)");
        Assert.True(Channel(filtered, 125, 50, 2) < 20 &&
                    Channel(filtered, 125, 50, 1) < 20 &&
                    Channel(filtered, 125, 50, 0) < 20,
            $"{backend}: filter did not consume the outer shadow " +
            $"(red={Channel(filtered, 125, 50, 2)})");
        Assert.True(Channel(filtered, 80, 50, 0) < 20,
            $"{backend}: filter did not consume the element background");

        var clipped = Render("box-shadow:12px 0 0 red;filter:brightness(0)",
            roundedAncestor: true);
        Assert.True(Channel(clipped, 125, 50, 2) < 20 &&
                    Channel(clipped, 135, 50, 2) > 245,
            $"{backend}: combined shadow/filter lost ancestor rounded clipping");

        var replayed = Render("box-shadow:12px 0 0 red;filter:brightness(0)", recorded: true);
        Assert.True(Channel(replayed, 125, 50, 2) < 20 &&
                    Channel(replayed, 80, 50, 0) < 20,
            $"{backend}: recorded shadow/filter paint order changed during replay");

        var grayscale = Render("box-shadow:12px 0 0 red;filter:grayscale(1)");
        var grayR = Channel(grayscale, 125, 50, 2);
        var grayG = Channel(grayscale, 125, 50, 1);
        var grayB = Channel(grayscale, 125, 50, 0);
        Assert.True(grayG is > 25 and < 160 &&
                    Math.Abs(grayR - grayG) < 12 && Math.Abs(grayB - grayG) < 12,
            $"{backend}: grayscale did not transform the outer shadow " +
            $"(R={grayR}, G={grayG}, B={grayB})");

        var matrixChain = Render(
            "box-shadow:12px 0 0 red;filter:grayscale(1) brightness(.5)");
        var chainR = Channel(matrixChain, 125, 50, 2);
        var chainG = Channel(matrixChain, 125, 50, 1);
        Assert.True(chainG > 10 && chainG + 10 < grayG &&
                    Math.Abs(chainR - chainG) < 12,
            $"{backend}: ordered color-matrix chain did not consume the outer shadow");

        var blurred = Render("box-shadow:12px 0 0 red;filter:blur(4px)");
        Assert.True(Channel(blurred, 134, 50, 1) + 10 < Channel(ordinary, 134, 50, 1),
            $"{backend}: blur did not extend the painted shadow apron");

        var secondShadow = Render(
            "box-shadow:12px 0 0 red;filter:drop-shadow(12px 0 0 green)");
        Assert.True(Channel(secondShadow, 140, 50, 2) < 30 &&
                    Channel(secondShadow, 140, 50, 1) > 50,
            $"{backend}: filter drop-shadow did not sample the box-shadow alpha");

        border.Child = new Border { Background = Brushes.Cyan };
        var inset = Render("box-shadow:inset 0 0 8px red;filter:brightness(0)");
        Assert.True(Channel(inset, 80, 32, 2) < 20 &&
                    Channel(inset, 80, 32, 1) < 20,
            $"{backend}: inset shadow or child bypassed the filter");
    }
}
