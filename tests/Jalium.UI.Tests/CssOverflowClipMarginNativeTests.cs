using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssOverflowClipMarginNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_DirectAndRecordedClipMarginPixels() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_DirectAndRecordedClipMarginPixels() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_DirectAndRecordedClipMarginPixels() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 100, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var panel = new Canvas { Width = 40, Height = 30 };
        panel.Children.Add(new Border { Width = 80, Height = 80, Background = Brushes.Red });

        byte[] Render(string style, bool recorded)
        {
            Css.SetStyle(panel, style);
            CssBorderRadiusTests.Layout(panel, 40, 30);
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                ((IOffsetDrawingContext)recorder).Offset = new Point(20, 20);
                panel.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(20, 20);
                try { panel.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Red(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4 + 2];
        static byte Blue(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4];

        foreach (var recorded in new[] { false, true })
        {
            var expanded = Render("overflow:clip;overflow-clip-margin:12px", recorded);
            Assert.True(Red(expanded, 70, 35) > 245, $"{backend} recorded={recorded}: expanded clip lost overflow");
            Assert.True(Red(expanded, 75, 35) < 8, $"{backend} recorded={recorded}: overflow passed the clip edge");

            var defaultEdge = Render("overflow:clip", recorded);
            Assert.True(Red(defaultEdge, 70, 35) < 8, $"{backend} recorded={recorded}: default clip edge expanded");

            var hidden = Render("overflow:hidden;overflow-clip-margin:12px", recorded);
            Assert.True(Red(hidden, 70, 35) < 8, $"{backend} recorded={recorded}: hidden used clip margin");

            var oneAxis = Render("overflow:clip visible;overflow-clip-margin:12px", recorded);
            Assert.True(Red(oneAxis, 70, 35) > 245, $"{backend} recorded={recorded}: x clip margin lost overflow");
            Assert.True(Red(oneAxis, 75, 35) < 8, $"{backend} recorded={recorded}: x clip margin passed the edge");
            Assert.True(Red(oneAxis, 35, 70) > 245, $"{backend} recorded={recorded}: open y axis was clipped");

            panel.Children.Clear();
            panel.Background = Brushes.Blue;
            var decorated = Render("padding:10px;overflow:clip", recorded);
            Assert.True(Blue(decorated, 22, 22) > 245,
                $"{backend} recorded={recorded}: clipping descendants also clipped the panel decoration");
            panel.Background = null;
            panel.Children.Add(new Border { Width = 80, Height = 80, Background = Brushes.Red });
        }
    }
}
