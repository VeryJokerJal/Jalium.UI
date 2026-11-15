using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMediaObjectNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_VideoFitAndPositionPaintDirectAndRecordedPixels()
        => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_VideoFitAndPositionPaintDirectAndRecordedPixels()
        => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_VideoFitAndPositionPaintDirectAndRecordedPixels()
        => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 100, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        using var media = CssMediaObjectTests.CreateMedia();
        Assert.Equal(backend, context.Backend);

        byte[] Paint(string style, bool recorded)
        {
            Css.SetStyle(media, "width:100px;height:100px;" + style);
            CssBorderRadiusTests.Layout(media, width, height);
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
                media.Render(recorder);
                DrawingReplayer.Replay(recorder.Commit(), drawing);
            }
            else media.Render(drawing);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static bool Red(byte[] pixels, int x, int y)
        {
            var index = (y * width + x) * 4;
            return pixels[index + 2] > 220 && pixels[index + 1] < 20 && pixels[index] < 20;
        }

        foreach (var recorded in new[] { false, true })
        {
            var contain = Paint("object-fit:contain", recorded);
            Assert.False(Red(contain, 50, 10));
            Assert.True(Red(contain, 50, 50));

            var cover = Paint("object-fit:cover", recorded);
            Assert.True(Red(cover, 50, 10));

            var positioned = Paint("object-fit:none;object-position:right bottom", recorded);
            Assert.False(Red(positioned, 50, 50));
            Assert.True(Red(positioned, 98, 98));

            var padded = Paint("padding:10px;overflow:visible;object-fit:cover;object-position:right bottom", recorded);
            Assert.False(Red(padded, 5, 50));
            Assert.True(Red(padded, 15, 50));
        }
    }
}
