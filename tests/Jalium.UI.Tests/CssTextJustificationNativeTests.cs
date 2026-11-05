using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextJustificationNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_JustifiesInlineTextDirectAndRecorded()
        => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_JustifiesInlineTextDirectAndRecorded()
        => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_JustifiesInlineTextDirectAndRecorded()
        => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 170, height = 120;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var first = new Run("one ");
        var second = new Run("two ") { Foreground = Brushes.Blue };
        var third = new Run("three four five");
        var block = new TextBlock(first)
        {
            Width = 90, Height = 100, FontSize = 20,
            Foreground = Brushes.White,
        };
        block.Inlines.Add(second);
        block.Inlines.Add(third);

        byte[] Paint(string alignment, bool recorded)
        {
            Css.SetStyle(block, $"white-space:normal; text-align:{alignment}");
            CssBorderRadiusTests.Layout(block, 90, 100);
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
                ((IOffsetDrawingContext)recorder).Offset = new Point(10, 10);
                block.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(10, 10);
                try { block.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok,
                target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static int MinBlueX(byte[] pixels)
        {
            var minX = width;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width + x) * 4;
                    if (pixels[index] > 150 && pixels[index + 1] < 80 &&
                        pixels[index + 2] < 80)
                        minX = Math.Min(minX, x);
                }
            return minX;
        }

        foreach (var recorded in new[] { false, true })
        {
            var left = MinBlueX(Paint("left", recorded));
            var justified = MinBlueX(Paint("justify", recorded));
            Assert.True(left < width, $"{backend} recorded={recorded}: no blue glyphs");
            Assert.True(justified > left + 8,
                $"{backend} recorded={recorded}: blue glyph x {left} -> {justified}");
        }
    }
}
