using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssImageBrushNativeTests
{
    private sealed class TwoFrameDecoder : INativeImageDecoder
    {
        private static DecodedImage Frame(int index, NativePixelFormat format)
        {
            var pixels = new byte[4 * 4 * 4];
            for (var at = 0; at < pixels.Length; at += 4)
            {
                if (format == NativePixelFormat.Rgba8)
                    pixels[at + (index == 0 ? 0 : 1)] = 255;
                else
                    pixels[at + (index == 0 ? 2 : 1)] = 255;
                pixels[at + 3] = 255;
            }
            return new DecodedImage(pixels, 4, 4, 16, format);
        }

        public DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
            => Frame(0, requestedFormat);
        public DecodedImage Decode(Stream stream, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
            => Frame(0, requestedFormat);
        public DecodedImage DecodeFile(string filePath, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
            => Frame(0, requestedFormat);
        public bool TryReadDimensions(ReadOnlySpan<byte> data, out int width, out int height)
        {
            width = height = 4;
            return true;
        }
        public int ReadFrameCount(ReadOnlySpan<byte> data) => 2;
        public DecodedImageFrame DecodeFrame(ReadOnlySpan<byte> data, int frameIndex,
            NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
            => new(Frame(frameIndex, requestedFormat), 1000);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ImageBrushRespectsEllipticalClipsAndNativeValues() => Verify(RenderBackend.Software);
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ImageBrushRespectsEllipticalClipsAndNativeValues() => Verify(RenderBackend.D3D12);
    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_ImageBrushRespectsEllipticalClipsAndNativeValues() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 128, height = 64;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.Equal(backend, context.Backend);
        target.SetFullInvalidation();
        var probeDeadline = Environment.TickCount64 + 2000;
        while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < probeDeadline); Thread.Sleep(1); }
        var probeRadii = new CssUsedBorderRadii(new(18, 7), new(3, 11), new(9, 4), new(5, 13));
        Assert.True(target.TryPushEllipticalRectClip(
            new NativeEllipticalClip(new Rect(0, 0, width, height), probeRadii, ClipEdges.All)),
            $"{backend} did not expose the current two-axis clip side interface");
        target.PopClip();
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        var source = new WriteableBitmap(8, 4, 96, 96, PixelFormat.Bgra32, null);
        byte[] pattern = new byte[8 * 4 * 4];
        for (var i = 0; i < 32; i++) { pattern[i * 4 + (i % 8 < 4 ? 2 : 1)] = 255; pattern[i * 4 + 3] = 255; }
        source.WritePixels(new Int32Rect(0, 0, 8, 4), pattern, 32, 0);
        var brush = new ImageBrush(source);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var border = new Border { Background = brush };
        Css.SetStyle(border, "border-radius:50%");
        CssBorderRadiusTests.Layout(border, width, height);

        byte[] Render(Action<RenderTargetDrawingContext>? paint = null)
        {
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            if (paint is null) border.Render(drawing);
            else paint(drawing);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback()); Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out var w, out var h));
            Assert.Equal(width, w); Assert.Equal(height, h);
            return pixels;
        }

        static byte Channel(byte[] pixels, int x, int y, int channel) => pixels[(y * width + x) * 4 + channel];
        var pixels = Render();
        Assert.Equal((byte)0, Channel(pixels, 1, 1, 2));
        Assert.Equal((byte)255, Channel(pixels, 20, 32, 2));
        Assert.Equal((byte)255, Channel(pixels, 108, 32, 1));

        // Brush transforms move the image, while the CSS contour stays fixed.
        brush.RelativeTransform = new TranslateTransform(.5, 0);
        pixels = Render();
        Assert.Equal((byte)0, Channel(pixels, 20, 32, 2));
        Assert.Equal((byte)255, Channel(pixels, 84, 32, 2));
        Assert.Equal((byte)0, Channel(pixels, 126, 1, 1));
        brush.RelativeTransform = Transform.Identity;

        // Each corner gets its own clip, including a square top-right corner.
        Css.SetStyle(border, "border-radius:50px 0 20px 0 / 25px 0 10px 0");
        pixels = Render();
        Assert.Equal((byte)0, Channel(pixels, 1, 1, 2));
        Assert.Equal((byte)255, Channel(pixels, 126, 1, 1));

        // Updating the original bitmap changes the existing brush's pixels.
        for (var i = 0; i < 32; i++) { pattern[i * 4] = 255; pattern[i * 4 + 1] = pattern[i * 4 + 2] = 0; }
        source.WritePixels(new Int32Rect(0, 0, 8, 4), pattern, 32, 0);
        pixels = Render();
        Assert.Equal((byte)255, Channel(pixels, 64, 32, 0));
        Assert.Same(source, brush.ImageSource); Assert.Same(brush, border.Background);

        // Explicit native radii remain authoritative; per-corner image clips
        // no longer collapse to the largest radius at every corner.
        border.CornerRadius = new CornerRadius(30, 0, 0, 0);
        pixels = Render();
        Assert.Equal((byte)0, Channel(pixels, 1, 1, 0));
        Assert.Equal((byte)255, Channel(pixels, 126, 1, 0));

        var path = (PathGeometry)Geometry.Parse("M0,0 L128,0 L0,64 Z");
        pixels = Render(dc => dc.DrawGeometry(brush, null, path));
        Assert.Equal((byte)255, Channel(pixels, 20, 20, 0));
        Assert.Equal((byte)0, Channel(pixels, 110, 50, 0));
        // Shape and brush edits invalidate the raster under the same identities.
        path.Figures = ((PathGeometry)Geometry.Parse("M0,0 L128,0 L128,64 L0,64 Z M32,16 L96,16 L96,48 L32,48 Z")).Figures;
        pixels = Render(dc => dc.DrawGeometry(brush, null, path));
        Assert.Equal((byte)0, Channel(pixels, 64, 32, 0));
        Assert.Equal((byte)255, Channel(pixels, 110, 50, 0));
        brush.Opacity = .5;
        pixels = Render(dc => dc.DrawGeometry(brush, null, path));
        var reference = Render(dc => dc.DrawRectangle(new SolidColorBrush(Colors.Blue) { Opacity = .5 }, null, new Rect(0, 0, width, height)));
        Assert.InRange(Math.Abs(Channel(pixels, 110, 50, 0) - Channel(reference, 110, 50, 0)), 0, 1);

        brush.Opacity = 1;
        pixels = Render(dc =>
        {
            for (var i = 0; i < 24; i++)
            {
                var x = i % 8 * 16; var y = i / 8 * 20;
                var triangle = new PathGeometry([new PathFigure
                {
                    StartPoint = new Point(x, y), IsClosed = true, IsFilled = true,
                    Segments = new PathSegmentCollection { new LineSegment(new Point(x + 14, y), true), new LineSegment(new Point(x, y + 18), true) }
                }]);
                dc.DrawGeometry(brush, null, triangle);
            }
        });
        for (var i = 0; i < 24; i++) Assert.Equal((byte)255, Channel(pixels, i % 8 * 16 + 3, i / 8 * 20 + 3, 0));
        Assert.InRange(drawing.CachedVectorDrawingCount, 0, 16);
        drawing.ClearBitmapCache();
        Assert.Equal(0, drawing.CachedVectorDrawingCount);

        // An arbitrary-path fill is a cached DrawingImage raster. Animated sources must change
        // that outer cache's generation even though no Image control subscribes to FrameChanged.
        BitmapImage.SetDecoder(new TwoFrameDecoder());
        using var animated = Assert.IsType<AnimatedBitmap>(ImageSourceLoader.FromBytes([1, 2, 3]));
        animated.Pause();
        animated.CurrentFrameIndex = 0;
        var animatedBrush = new ImageBrush(animated);
        var animatedPath = (PathGeometry)Geometry.Parse("M0,0 L80,0 L0,60 Z");
        pixels = Render(dc => dc.DrawGeometry(animatedBrush, null, animatedPath));
        Assert.Equal((byte)255, Channel(pixels, 16, 16, 2));
        animated.CurrentFrameIndex = 1;
        pixels = Render(dc => dc.DrawGeometry(animatedBrush, null, animatedPath));
        Assert.Equal((byte)255, Channel(pixels, 16, 16, 1));
        Assert.Equal((byte)0, Channel(pixels, 16, 16, 2));

        // ImageBrush accepts every ImageSource, not just BitmapSource. Both the direct rounded
        // fill and the arbitrary-path raster preserve a mutable DrawingImage's real pixels.
        var vectorColour = new SolidColorBrush(Colors.Red);
        var vectorDrawing = new GeometryDrawing(vectorColour, null,
            new RectangleGeometry(new Rect(0, 0, 4, 2)));
        var vectorSource = new DrawingImage(vectorDrawing);
        var vectorBrush = new ImageBrush(vectorSource);
        border.ClearValue(Border.CornerRadiusProperty);
        border.Background = vectorBrush;
        Css.SetStyle(border, "border-radius:50%");
        pixels = Render();
        Assert.Equal((byte)0, Channel(pixels, 1, 1, 2));
        Assert.Equal((byte)255, Channel(pixels, 64, 32, 2));
        vectorColour.Color = Color.FromRgb(0, 255, 0);
        pixels = Render(dc => dc.DrawGeometry(vectorBrush, null, animatedPath));
        Assert.Equal((byte)255, Channel(pixels, 16, 16, 1));
        Assert.Equal((byte)0, Channel(pixels, 100, 50, 1));

        var nestedBitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormat.Bgra32, null);
        nestedBitmap.WritePixels(new Int32Rect(0, 0, 1, 1), [0, 0, 255, 255], 4, 0);
        var nestedVector = new DrawingImage(new ImageDrawing(nestedBitmap, new Rect(0, 0, 4, 2)));
        var nestedBrush = new ImageBrush(nestedVector);
        pixels = Render(dc => dc.DrawGeometry(nestedBrush, null, animatedPath));
        Assert.Equal((byte)255, Channel(pixels, 16, 16, 2));
        nestedBitmap.WritePixels(new Int32Rect(0, 0, 1, 1), [0, 255, 0, 255], 4, 0);
        pixels = Render(dc => dc.DrawGeometry(nestedBrush, null, animatedPath));
        Assert.Equal((byte)255, Channel(pixels, 16, 16, 1));
        Assert.Equal((byte)0, Channel(pixels, 16, 16, 2));
    }
}
