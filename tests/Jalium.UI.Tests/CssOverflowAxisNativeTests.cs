using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssOverflowAxisNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_AxisClipsPaintOutsideOnlyTheSelectedEdges() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_AxisClipsPaintOutsideOnlyTheSelectedEdges() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_AxisClipsPaintOutsideOnlyTheSelectedEdges() => Verify(RenderBackend.Vulkan);

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

        foreach (var recorded in new[] { false, true })
        {
            var horizontal = Render("overflow:clip visible", recorded);
            Assert.True(Red(horizontal, 70, 35) < 8, $"{backend} recorded={recorded}: x overflow escaped");
            Assert.True(Red(horizontal, 35, 60) > 245, $"{backend} recorded={recorded}: y overflow was clipped");

            var vertical = Render("overflow:visible clip", recorded);
            Assert.True(Red(vertical, 70, 35) > 245, $"{backend} recorded={recorded}: x overflow was clipped");
            Assert.True(Red(vertical, 35, 60) < 8, $"{backend} recorded={recorded}: y overflow escaped");

            var both = Render("overflow-x:visible;overflow-y:hidden", recorded);
            Assert.True(Red(both, 70, 35) < 8, $"{backend} recorded={recorded}: visible x did not compute to auto");
            Assert.True(Red(both, 35, 60) < 8, $"{backend} recorded={recorded}: y overflow escaped");
            Assert.True(Red(both, 35, 35) > 245);

            var squareCorner = Render("border-radius:50%;overflow:clip visible", recorded);
            Assert.True(Red(squareCorner, 21, 21) > 245,
                $"{backend} recorded={recorded}: one-axis clip rounded its corner");
            var roundedCorner = Render("border-radius:50%;overflow:hidden", recorded);
            Assert.True(Red(roundedCorner, 21, 21) < 8,
                $"{backend} recorded={recorded}: two-axis clip lost its rounded corner");
        }
    }
}
