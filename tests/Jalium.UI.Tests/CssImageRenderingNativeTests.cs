using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Shapes;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssImageRenderingNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ImageAndBackgroundSamplingSurviveDirectAndRecordedDrawing()
        => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ImageAndBackgroundSamplingSurviveDirectAndRecordedDrawing()
        => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_ImageAndBackgroundSamplingSurviveDirectAndRecordedDrawing()
        => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int viewport = 20;
        using var window = new HiddenNativeWindow(viewport, viewport);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, viewport, viewport);
        using var drawing = new RenderTargetDrawingContext(target, context);
        Assert.Equal(backend, context.Backend);

        var source = new WriteableBitmap(2, 1, 96, 96, PixelFormat.Bgra32, null);
        source.WritePixels(new Int32Rect(0, 0, 2, 1),
            new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, 8, 0);
        var image = new Image { Source = source };
        var background = new Border();
        Css.SetStyle(background,
            "width:5px;height:5px;background-image:url('tile.png');" +
            "background-size:5px 5px;background-repeat:no-repeat;image-rendering:pixelated");
        CssBorderRadiusTests.Layout(background, 5, 5);
        Assert.IsType<ImageBrush>(background.Background).ImageSource = source;
        var localBackground = new Border { Background = new ImageBrush(source) };
        Css.SetStyle(localBackground, "width:5px;height:5px;image-rendering:pixelated");
        FrameworkElement[] shapeFills =
        [
            new Rectangle { Fill = new ImageBrush(source), StrokeThickness = 0 },
            new Ellipse { Fill = new ImageBrush(source), StrokeThickness = 0 },
            new Jalium.UI.Shapes.Path
            {
                Data = Geometry.Parse("M0,0 L5,0 L5,5 L0,5 Z"),
                Fill = new ImageBrush(source),
                StrokeThickness = 0,
            },
        ];
        foreach (var shape in shapeFills)
            Css.SetStyle(shape, "width:5px;height:5px;image-rendering:pixelated");
        var strokeBrush = new ImageBrush(source);
        var strokedLine = new Line
        {
            X1 = 0, X2 = 5, Y1 = 2.5, Y2 = 2.5,
            Stroke = strokeBrush, StrokeThickness = 5,
        };
        Css.SetStyle(strokedLine, "width:5px;height:5px;image-rendering:pixelated");

        byte[] Paint(FrameworkElement element, bool recorded)
        {
            CssBorderRadiusTests.Layout(element, 5, 5);
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
                element.Render(recorder);
                DrawingReplayer.Replay(recorder.Commit(), drawing);
            }
            else element.Render(drawing);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[viewport * viewport * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, viewport * 4u, out _, out _));
            return pixels;
        }

        static byte Red(byte[] pixels, int x) => pixels[(2 * viewport + x) * 4 + 2];
        static string Row(byte[] pixels) => string.Join(",", Enumerable.Range(0, 5).Select(x => Red(pixels, x)));

        foreach (var recorded in new[] { false, true })
        {
            Css.SetStyle(image, "width:5px;height:5px;object-fit:fill;image-rendering:pixelated");
            var pixelated = Paint(image, recorded);
            Assert.True(Red(pixelated, 1) >= 245,
                $"{backend} recorded={recorded} pixelated row={Row(pixelated)}");
            Assert.InRange(Red(pixelated, 2), (byte)110, (byte)145);
            Assert.InRange(Red(pixelated, 3), byte.MinValue, (byte)10);

            Css.SetStyle(image, "width:5px;height:5px;object-fit:fill;image-rendering:crisp-edges");
            var crisp = Paint(image, recorded);
            Assert.True(Red(crisp, 2) <= 10 || Red(crisp, 2) >= 245,
                $"{backend} recorded={recorded} crisp row={Row(crisp)}");

            var tile = Paint(background, recorded);
            Assert.True(Red(tile, 1) >= 245,
                $"{backend} recorded={recorded} background row={Row(tile)}");
            Assert.InRange(Red(tile, 2), (byte)110, (byte)145);

            var localTile = Paint(localBackground, recorded);
            Assert.True(Red(localTile, 1) >= 245,
                $"{backend} recorded={recorded} local background row={Row(localTile)}");
            Assert.InRange(Red(localTile, 2), (byte)110, (byte)145);

            foreach (var shape in shapeFills)
            {
                var fill = Paint(shape, recorded);
                Assert.True(Red(fill, 1) >= 245,
                    $"{backend} {shape.GetType().Name} recorded={recorded} fill row={Row(fill)}");
                Assert.InRange(Red(fill, 2), (byte)110, (byte)145);
            }

            var stroke = Paint(strokedLine, recorded);
            Assert.True(Red(stroke, 1) >= 245,
                $"{backend} recorded={recorded} shape stroke row={Row(stroke)}");
            Assert.InRange(Red(stroke, 2), (byte)110, (byte)145);
            Assert.Equal(BitmapScalingMode.Unspecified, strokeBrush.ScalingMode);

            strokeBrush.ScalingMode = BitmapScalingMode.NearestNeighbor;
            var nativeStroke = Paint(strokedLine, recorded);
            Assert.True(Red(nativeStroke, 2) <= 10 || Red(nativeStroke, 2) >= 245,
                $"{backend} recorded={recorded} native stroke row={Row(nativeStroke)}");
            strokeBrush.ScalingMode = BitmapScalingMode.Unspecified;

            strokedLine.StrokeDashArray = new DoubleCollection { 1, 2 };
            var dashedStroke = Paint(strokedLine, recorded);
            Assert.True(Red(dashedStroke, 1) >= 245,
                $"{backend} recorded={recorded} dashed shape stroke row={Row(dashedStroke)}");
            Assert.InRange(Red(dashedStroke, 2), (byte)110, (byte)145);
            strokedLine.StrokeDashArray = null;
        }
    }
}
