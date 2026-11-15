using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssCalendarBackgroundNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_BoxClipsAndPositionedGradient() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_BoxClipsAndPositionedGradient() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_BoxClipsAndPositionedGradient() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 500, height = 400;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);

        byte[] Render(string style, bool recorded)
        {
            var calendar = new Calendar();
            Css.SetStyle(calendar, style);
            CssBorderRadiusTests.Layout(calendar, 400, 350);
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline);
                Thread.Sleep(1);
            }

            target.Clear(0, 0, 0, 1);
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                ((IOffsetDrawingContext)recorder).Offset = new Point(40, 30);
                calendar.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(40, 30);
                try { calendar.Render(drawing); }
                finally { drawing.Offset = default; }
            }

            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Channel(byte[] pixels, int x, int y, int channel)
            => pixels[(y * width + x) * 4 + channel];

        const string layeredStyle =
            "border:8px solid transparent;padding:4px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box";
        foreach (var recorded in new[] { false, true })
        {
            var layered = Render(layeredStyle, recorded);
            Assert.True(Channel(layered, 50, 230, 0) < 8,
                $"{backend} recorded={recorded}: Calendar background escaped content box");
            Assert.True(Channel(layered, 390, 230, 0) > 245);
            Assert.True(Channel(layered, 412, 362, 2) > 100);
            Assert.True(Channel(layered, 425, 362, 1) > 100);

            var rounded = Render("background-color:blue;border-radius:50%", recorded);
            Assert.True(Channel(rounded, 439, 31, 0) < 8,
                $"{backend} recorded={recorded}: Calendar background escaped radius");
            Assert.True(Channel(rounded, 390, 205, 0) > 245);
        }
    }
}
