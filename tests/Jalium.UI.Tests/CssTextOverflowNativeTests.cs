using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextOverflowNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PaintsEllipsisDirectAndRecorded()
        => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_PaintsEllipsisDirectAndRecorded()
        => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_PaintsEllipsisDirectAndRecorded()
        => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 130, height = 75;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var block = new TextBlock
        {
            Text = "MMMMMMMMMM", Width = 70, Height = 50,
            FontSize = 24, Foreground = Brushes.Red,
        };

        byte[] Paint(string overflow, bool recorded)
        {
            Css.SetStyle(block, $"white-space:nowrap; text-overflow:{overflow}");
            CssBorderRadiusTests.Layout(block, 70, 50);
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

        static byte[] RedMask(byte[] pixels)
        {
            var mask = new byte[width * height];
            for (var i = 0; i < mask.Length; i++)
            {
                var offset = i * 4;
                mask[i] = pixels[offset + 2] > 150 && pixels[offset + 1] < 80 &&
                    pixels[offset] < 80 ? (byte)1 : (byte)0;
            }
            return mask;
        }

        foreach (var recorded in new[] { false, true })
        {
            var ellipse = RedMask(Paint("ellipsis", recorded));
            var clipped = RedMask(Paint("clip", recorded));
            Assert.Contains((byte)1, ellipse);
            Assert.Contains((byte)1, clipped);
            var different = ellipse.Zip(clipped).Count(pair => pair.First != pair.Second);
            Assert.True(different > 20,
                $"{backend} recorded={recorded}: ellipsis and clip differ by {different} pixels");
        }
    }
}
