namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Samples bitmap and vector image brushes without replacing their source or retaining mutable
/// pixels across draws.
/// </summary>
internal sealed class ImageBrushSampler
{
    [ThreadStatic]
    private static int s_vectorRasterDepth;

    private readonly BitmapPixelSnapshot _pixels;
    private readonly Rect _viewport, _image;
    private readonly TileMode _tileMode;
    private readonly CssBackgroundTilePattern? _cssPattern;
    private readonly Matrix _inverse;
    private readonly double _opacity;
    private readonly BitmapScalingMode _scalingMode;
    private readonly double _deviceScale;

    private ImageBrushSampler(ImageBrush brush, Rect bounds, BitmapPixelSnapshot pixels, Matrix inverse,
        double opacity, double deviceScale)
    {
        _pixels = pixels; _inverse = inverse; _opacity = opacity;
        _scalingMode = brush.ScalingMode;
        _deviceScale = deviceScale;
        if (brush.CssBackgroundLayout is { } cssBackground)
        {
            _viewport = bounds;
            var source = brush.ImageSource;
            var intrinsicWidth = source?.Width > 0 ? source.Width : pixels.CanonicalWidth;
            var intrinsicHeight = source?.Height > 0 ? source.Height : pixels.CanonicalHeight;
            _cssPattern = cssBackground.TilePattern(bounds, intrinsicWidth, intrinsicHeight);
            _image = _cssPattern.Value.ImageRect;
            _tileMode = TileMode.None;
            return;
        }
        _viewport = TileBrushHelper.ComputeViewport(brush, bounds);
        var width = pixels.CanonicalWidth; var height = pixels.CanonicalHeight;
        var viewbox = TileBrushHelper.ComputeViewbox(brush, width, height);
        _image = TileBrushHelper.ComputeFullImageRect(TileBrushHelper.ComputeContentRect(brush, _viewport, viewbox), viewbox, width, height);
        _tileMode = brush.TileMode;
    }

    internal static ImageBrushSampler? Create(ImageBrush brush, Rect bounds, Point offset, double opacity, double deviceScale = 1)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || brush.ImageSource is not { } source || opacity <= 0) return null;
        var transform = TileBrushHelper.ComputeBrushTransform(brush, bounds, offset);
        if (!transform.TryInvert(out var inverse)) return null;
        var requestWidth = bounds.Width;
        var requestHeight = bounds.Height;
        if (brush.CssBackgroundLayout is { } layout && source.Width > 0 && source.Height > 0)
        {
            var image = layout.ImageRect(bounds, source.Width, source.Height);
            if (!image.IsEmpty)
            {
                requestWidth = Math.Max(requestWidth, image.Width);
                requestHeight = Math.Max(requestHeight, image.Height);
            }
        }
        var pixels = GetPixels(source, Hint(requestWidth * deviceScale), Hint(requestHeight * deviceScale),
            brush.Stretch == Stretch.UniformToFill);
        return pixels is null ? null : new(brush, bounds, pixels, inverse, opacity, deviceScale);
    }

    internal static ImageBrushSampler? Create(ImageBrush brush, Rect bounds, Point offset,
        double opacity, BitmapPixelSnapshot pixels)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || opacity <= 0) return null;
        var transform = TileBrushHelper.ComputeBrushTransform(brush, bounds, offset);
        return transform.TryInvert(out var inverse) ? new(brush, bounds, pixels, inverse, opacity, 1) : null;
    }

    private static int Hint(double value) => double.IsFinite(value) ? (int)Math.Clamp(Math.Ceiling(value), 1, 16384) : 1;

    internal static BitmapPixelSnapshot? GetPixels(ImageSource source, int widthHint, int heightHint, bool cover)
    {
        source.MarkDrawn();
        if (source is AnimatedBitmap animation)
            return animation.CurrentFrame is { } frame ? GetPixels(frame, widthHint, heightHint, cover) : null;
        if (source is BitmapImage image)
        {
            image.RequestDecode(widthHint, heightHint, cover);
            if (image.AnimatedSubstitute is { } substitute) return GetPixels(substitute, widthHint, heightHint, cover);
            if (!image.TryGetPixelSnapshot(out var pixels))
            {
                image.TryRestorePixelData();
                image.TryGetPixelSnapshot(out pixels);
            }
            return pixels;
        }
        if (source is BitmapSource bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
        {
            var pixels = bitmap.Format == PixelFormat.Bgra32 ? BitmapPixelOperations.Read(bitmap)
                : BitmapPixelOperations.Convert(bitmap, PixelFormat.Bgra32, null, 0);
            return BitmapPixelSnapshot.Create(pixels.Pixels, pixels.Width, pixels.Height, pixels.Stride,
                NativePixelFormat.Bgra8, pixels.Width, pixels.Height);
        }
        if (source is DrawingImage { Drawing: { } drawing })
            return RasterizeVector(drawing, drawing.Bounds, widthHint, heightHint);
        if (source is SvgImage { Drawing: { } svgDrawing } svg)
        {
            var viewport = svg.Width > 0 && svg.Height > 0
                ? new Rect(0, 0, svg.Width, svg.Height)
                : svgDrawing.Bounds;
            return RasterizeVector(svgDrawing, viewport, widthHint, heightHint);
        }
        return null;
    }

    private static BitmapPixelSnapshot? RasterizeVector(Drawing drawing, Rect viewport,
        int widthHint, int heightHint)
    {
        if (viewport.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0 || s_vectorRasterDepth >= 8)
            return null;

        var width = Math.Clamp(widthHint, 1, 4096);
        var height = Math.Clamp(heightHint, 1, 4096);
        s_vectorRasterDepth++;
        try
        {
            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
            var context = new SoftwareDrawingContext(target);
            var transform = new Matrix(
                width / viewport.Width, 0, 0, height / viewport.Height,
                -viewport.X * width / viewport.Width,
                -viewport.Y * height / viewport.Height);
            context.PushTransform(new MatrixTransform(transform));
            drawing.RenderTo(context);
            context.Pop();
            context.Close();
            return BitmapPixelSnapshot.Create(target.GetPixelBuffer(), width, height, target.Stride,
                NativePixelFormat.Bgra8,
                Math.Clamp((int)Math.Ceiling(viewport.Width), 1, 16384),
                Math.Clamp((int)Math.Ceiling(viewport.Height), 1, 16384));
        }
        finally
        {
            s_vectorRasterDepth--;
        }
    }

    internal (double R, double G, double B, double A) Sample(double x, double y)
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0 || _image.Width <= 0 || _image.Height <= 0) return default;
        var point = _inverse.Transform(new Point(x, y));
        if (_cssPattern is { } cssPattern)
        {
            if (!Inside(_viewport, point)) return default;
            if (!cssPattern.X.TryMap(point.X, out var px) ||
                !cssPattern.Y.TryMap(point.Y, out var py)) return default;
            point = new(_image.X + px, _image.Y + py);
        }
        else if (_tileMode == TileMode.None)
        {
            if (!Inside(_viewport, point)) return default;
        }
        else
        {
            var column = Math.Floor((point.X - _viewport.X) / _viewport.Width);
            var row = Math.Floor((point.Y - _viewport.Y) / _viewport.Height);
            point = new(point.X - column * _viewport.Width, point.Y - row * _viewport.Height);
            if (_tileMode is TileMode.FlipX or TileMode.FlipXY && Math.Abs(column % 2) == 1)
                point = new(2 * _viewport.X + _viewport.Width - point.X, point.Y);
            if (_tileMode is TileMode.FlipY or TileMode.FlipXY && Math.Abs(row % 2) == 1)
                point = new(point.X, 2 * _viewport.Y + _viewport.Height - point.Y);
        }
        if (!Inside(_image, point)) return default;
        var u = (point.X - _image.X) / _image.Width * _pixels.Width - .5;
        var v = (point.Y - _image.Y) / _image.Height * _pixels.Height - .5;
        var result = SamplePixels(_pixels, u, v, _scalingMode,
            _image.Width * _deviceScale, _image.Height * _deviceScale);
        return (result.R, result.G, result.B, result.A * _opacity);
    }

    private static bool Inside(Rect rect, Point point)
        => point.X >= rect.X && point.X < rect.Right && point.Y >= rect.Y && point.Y < rect.Bottom;

    internal static (double R, double G, double B, double A) SamplePixels(
        BitmapPixelSnapshot pixels, double u, double v,
        BitmapScalingMode mode = BitmapScalingMode.Unspecified,
        double targetWidth = 0, double targetHeight = 0)
    {
        if (mode == BitmapScalingMode.NearestNeighbor)
        {
            var ix = (int)Math.Clamp(Math.Floor(u + .5), 0, pixels.Width - 1);
            var iy = (int)Math.Clamp(Math.Floor(v + .5), 0, pixels.Height - 1);
            var at = iy * pixels.Stride + ix * 4;
            var rgba = pixels.Format == NativePixelFormat.Rgba8;
            return (pixels.Pixels[at + (rgba ? 0 : 2)] / 255d,
                pixels.Pixels[at + 1] / 255d,
                pixels.Pixels[at + (rgba ? 2 : 0)] / 255d,
                pixels.Pixels[at + 3] / 255d);
        }

        // CSS pixelated first makes a nearest-neighbor raster at the closest
        // positive integer scale, then smoothly resamples that raster.
        var stepX = mode == BitmapScalingMode.Pixelated
            ? Math.Max(1, Math.Floor(targetWidth / pixels.Width + .5)) : 1d;
        var stepY = mode == BitmapScalingMode.Pixelated
            ? Math.Max(1, Math.Floor(targetHeight / pixels.Height + .5)) : 1d;
        if (mode == BitmapScalingMode.Pixelated)
        {
            u = (u + .5) * stepX - .5;
            v = (v + .5) * stepY - .5;
        }
        var x = Math.Floor(u); var y = Math.Floor(v);
        var fx = u - x; var fy = v - y;
        var r = 0d; var g = 0d; var b = 0d; var a = 0d;
        void Add(double sx, double sy, double weight)
        {
            var px = (int)Math.Clamp(Math.Floor(sx / stepX), 0, pixels.Width - 1);
            var py = (int)Math.Clamp(Math.Floor(sy / stepY), 0, pixels.Height - 1);
            var at = py * pixels.Stride + px * 4;
            var alpha = pixels.Pixels[at + 3] / 255d * weight;
            var rgba = pixels.Format == NativePixelFormat.Rgba8;
            r += pixels.Pixels[at + (rgba ? 0 : 2)] / 255d * alpha;
            g += pixels.Pixels[at + 1] / 255d * alpha;
            b += pixels.Pixels[at + (rgba ? 2 : 0)] / 255d * alpha;
            a += alpha;
        }
        Add(x, y, (1 - fx) * (1 - fy)); Add(x + 1, y, fx * (1 - fy));
        Add(x, y + 1, (1 - fx) * fy); Add(x + 1, y + 1, fx * fy);
        // Interpolate premultiplied colours; hidden RGB in transparent texels
        // must not bleed into a visible neighbour.
        return a > 0 ? (r / a, g / a, b / a, a) : default;
    }
}
