using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Interop;

/// <summary>
/// CPU software rasterizer that converts a Drawing tree into a BGRA8 pixel buffer.
/// Used to cache vector image (SVG) content as bitmaps to avoid per-frame tessellation.
///
/// <para>
/// Geometry is anti-aliased with the same analytic-coverage scanline the native
/// software backend uses (<c>software_backend.cpp::FillPolygon</c>): each output row is
/// sampled by 4 vertical sub-scanlines and, within each, every filled span contributes
/// fractional horizontal coverage. Fills honor <see cref="PathGeometry.FillRule"/>
/// (even-odd or nonzero) for both single- and multi-figure paths; strokes are widened to
/// source-space quads + round joins and composited with max-coverage so overlapping
/// segments never darken.
/// </para>
/// <para>
/// All point transforms go through the framework's row-vector <see cref="Matrix"/>
/// convention (<see cref="Matrix.Transform(Point)"/>), so rotate / skew / non-symmetric
/// matrices land geometry exactly where the GPU path would — previously the rasterizer
/// used a transposed (column-vector) convention that mirrored rotated/skewed content.
/// </para>
/// <para>
/// <see cref="DrawingGroup.ClipGeometry"/> is honored via an anti-aliased per-pixel clip
/// coverage mask, intersected down the group tree.
/// </para>
/// </summary>
internal static class SoftwareVectorRasterizer
{
    [ThreadStatic]
    private static int s_vectorImageBrushDepth;

    /// <summary>
    /// Rasterizes a Drawing into a BGRA8 pixel buffer at the specified size.
    /// Returns null if the drawing cannot be rasterized.
    /// </summary>
    /// <param name="drawing">The drawing tree to rasterize.</param>
    /// <param name="width">Target pixel width.</param>
    /// <param name="height">Target pixel height.</param>
    /// <param name="sourceBounds">
    /// The source viewport rectangle to map onto the target buffer. For
    /// <see cref="SvgImage"/> this must be <c>(0, 0, svg.Width, svg.Height)</c>
    /// so that SVG viewport spacing is preserved. When <see langword="null"/>,
    /// falls back to <see cref="Drawing.Bounds"/>, which only covers the actual
    /// geometry and will distort content that relies on viewport whitespace.
    /// </param>
    /// <param name="touchedSources">
    /// When supplied, receives every <see cref="ImageSource"/> this rasterization read pixels from
    /// — an <see cref="ImageDrawing"/>'s source, an <see cref="ImageBrush"/>'s source.
    /// </param>
    /// <remarks>
    /// <para><paramref name="touchedSources"/> is what makes the caller's raster cache
    /// INVALIDATABLE. The result is one flat buffer keyed on the OUTER vector source, but its
    /// content depends on inner bitmaps that publish asynchronously: the first rasterization of a
    /// <see cref="DrawingImage"/> wrapping a URI-backed <see cref="ImageDrawing"/> runs while that
    /// bitmap still has no pixels, and produces a perfectly valid all-transparent buffer. A cache
    /// that cannot map "this inner bitmap just published" back to "that outer raster is stale"
    /// therefore serves the blank frame for the life of the window. Recording the dependency here —
    /// where the sources are actually read — is the only place that cannot drift from the rendering
    /// itself.</para>
    /// </remarks>
    public static byte[]? Rasterize(
        Drawing drawing,
        int width,
        int height,
        Rect? sourceBounds = null,
        ICollection<ImageSource>? touchedSources = null)
    {
        if (drawing == null || width <= 0 || height <= 0)
            return null;

        var bounds = sourceBounds ?? drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        // Allocate BGRA8 pixel buffer (straight alpha, all transparent). The D3D12
        // upload path premultiplies straight-alpha BitmapImages on its way to the GPU.
        var stride = width * 4;
        var pixels = new byte[stride * height];

        // Source -> device transform (pure scale + translate; row-vector Matrix).
        var scaleX = width / bounds.Width;
        var scaleY = height / bounds.Height;
        var m = new Matrix(scaleX, 0, 0, scaleY, -bounds.X * scaleX, -bounds.Y * scaleY);

        var ctx = new SoftwareRenderContext(pixels, width, height, stride, m, touchedSources);
        RenderDrawing(drawing, ctx);

        return pixels;
    }

    private static void RenderDrawing(Drawing drawing, in SoftwareRenderContext ctx)
    {
        if (drawing is DrawingGroup group)
        {
            var childCtx = ctx;

            // Apply group transform (child transform applied first under row-vector composition).
            if (group.Transform != null && !group.Transform.Value.IsIdentity)
                childCtx = childCtx.WithTransform(group.Transform.Value);

            // Apply group opacity
            if (group.Opacity < 1.0)
                childCtx = childCtx.WithOpacity(childCtx.Opacity * group.Opacity);

            // Apply group clip (intersect the per-pixel coverage mask). The clip geometry
            // lives in the group's post-transform coordinate space, so it is resolved
            // through childCtx (which already carries the group transform).
            if (group.ClipGeometry != null)
                childCtx = ApplyClip(childCtx, group.ClipGeometry);

            foreach (var child in group.Children)
            {
                if (child != null)
                    RenderDrawing(child, childCtx);
            }
        }
        else if (drawing is GeometryDrawing geomDrawing)
        {
            RenderGeometryDrawing(geomDrawing, ctx);
        }
        else if (drawing is ImageDrawing imageDrawing)
        {
            RenderImageDrawing(imageDrawing, ctx);
        }
    }

    private static void RenderGeometryDrawing(GeometryDrawing drawing, in SoftwareRenderContext ctx)
    {
        if (drawing.Geometry == null) return;

        // Apply geometry transform if present (composed before the context transform).
        var geoCtx = ctx;
        if (drawing.Geometry.Transform != null && !drawing.Geometry.Transform.Value.IsIdentity)
            geoCtx = ctx.WithTransform(drawing.Geometry.Transform.Value);

        // Match the native drawing context: one compound fill, followed by
        // child strokes with their complete local transforms still in scope.
        if (drawing.Geometry is GeometryGroup group)
        {
            double scale = Math.Max(geoCtx.ScaleX, geoCtx.ScaleY);
            double groupTolerance = scale > 1e-6 ? 0.3 / scale : 0.3;
            if (drawing.Brush != null)
                RenderGeometryDrawing(new GeometryDrawing(drawing.Brush, null,
                    RenderTargetDrawingContext.FlattenGeometryGroup(group, groupTolerance)), geoCtx);
            if (drawing.Pen != null)
            {
                foreach (var child in group.Children)
                    RenderGeometryDrawing(new GeometryDrawing(null, drawing.Pen, child), geoCtx);
            }
            return;
        }

        // Flatten curves to line segments. Tolerance is expressed in device pixels
        // (~0.3px chord error) then converted to source units by the effective scale,
        // so high zoom keeps curves smooth instead of faceting.
        double effScale = Math.Max(geoCtx.ScaleX, geoCtx.ScaleY);
        double tolerance = effScale > 1e-6 ? 0.3 / effScale : 0.3;
        var flatGeometry = GetFlattenedGeometry(drawing.Geometry, tolerance);
        if (flatGeometry == null) return;

        // Fill — gradients get true per-pixel sampling; everything else extracts a
        // representative color.
        if (drawing.Brush != null)
        {
            var fillPaint = CreatePixelPaint(drawing.Brush, geoCtx, flatGeometry);
            if (fillPaint != null)
            {
                FillGeometry(flatGeometry, geoCtx, 0, 0, 0, 255, fillPaint);
            }
            else
            {
                var (fb, fg, fr, fa) = ExtractBrushColor(drawing.Brush, geoCtx, flatGeometry);
                if (fa > 0)
                    FillGeometry(flatGeometry, geoCtx, fb, fg, fr, fa);
            }
        }

        // Stroke
        if (drawing.Pen is { Brush: not null } pen && pen.Thickness > 0)
        {
            var contours = BuildStrokeContours(flatGeometry, geoCtx, pen);
            if (contours.Count == 0) return;
            var strokeBounds = StrokePaintBounds(flatGeometry, geoCtx, pen, contours);
            var strokePaint = CreatePixelPaint(pen.Brush, geoCtx, flatGeometry, strokeBounds);
            if (strokePaint != null)
            {
                RasterizeCoverage(geoCtx, contours, nonZero: false, isMax: true,
                    0, 0, 0, 255, strokePaint);
            }
            else
            {
                var (sb, sg, sr, sa) = ExtractBrushColor(pen.Brush, geoCtx, flatGeometry);
                if (sa > 0)
                    RasterizeCoverage(geoCtx, contours, nonZero: false, isMax: true,
                        sb, sg, sr, sa);
            }
        }
    }

    /// <summary>
    /// Per-pixel gradient sampler for the software rasterizer. Device pixels are mapped
    /// back through the context (and gradientTransform) into brush space, projected to a
    /// gradient parameter t, and interpolated across the sorted stop ramp. Replaces the
    /// old behaviour of flattening every gradient to one offset-weighted average colour,
    /// which turned any gradient-filled SVG artwork into a flat slab.
    /// </summary>
    private abstract class PixelPaint
    {
        public abstract (byte B, byte G, byte R, byte A) Sample(float deviceX, float deviceY);
    }

    private static PixelPaint? CreatePixelPaint(Brush brush, in SoftwareRenderContext ctx,
        PathGeometry geometry, Rect? strokeBounds = null)
    {
        var paintBounds = strokeBounds ?? geometry.Bounds;
        if (brush.CssGradientLayout is { } layout &&
            paintBounds.Width > 0 && paintBounds.Height > 0)
        {
            if (layout.Resolve(paintBounds.Width, paintBounds.Height) is not { } resolved) return null;
            brush = resolved;
        }
        if (brush is not ImageBrush image) return GradientPaint.TryCreate(brush, ctx, geometry);
        if (image.ImageSource is { } source) ctx.TouchedSources?.Add(source);
        if (!ctx.Matrix.TryInvert(out var inverse)) return null;
        var opacity = Math.Clamp(ctx.Opacity * image.Opacity, 0, 1);
        ImageBrushSampler? sampler;
        if (TryRasterizeVectorBrushSource(image.ImageSource, paintBounds, ctx, out var vectorPixels))
        {
            sampler = vectorPixels is null
                ? null
                : ImageBrushSampler.Create(image, paintBounds, default, opacity, vectorPixels);
        }
        else
        {
            sampler = ImageBrushSampler.Create(image, paintBounds, default,
                opacity, Math.Max(ctx.ScaleX, ctx.ScaleY));
        }
        return new ImagePaint(sampler, inverse);
    }

    private static bool TryRasterizeVectorBrushSource(ImageSource? source, Rect paintBounds,
        in SoftwareRenderContext ctx, out BitmapPixelSnapshot? pixels)
    {
        Drawing? drawing;
        Rect viewport;
        switch (source)
        {
            case DrawingImage { Drawing: { } imageDrawing }:
                drawing = imageDrawing;
                viewport = imageDrawing.Bounds;
                break;
            case SvgImage { Drawing: { } svgDrawing } svg:
                drawing = svgDrawing;
                viewport = svg.Width > 0 && svg.Height > 0
                    ? new Rect(0, 0, svg.Width, svg.Height)
                    : svgDrawing.Bounds;
                break;
            default:
                pixels = null;
                return false;
        }

        pixels = null;
        if (viewport.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0 ||
            paintBounds.Width <= 0 || paintBounds.Height <= 0 || s_vectorImageBrushDepth >= 8)
            return true;

        var width = Math.Clamp((int)Math.Ceiling(paintBounds.Width * ctx.ScaleX), 1, 4096);
        var height = Math.Clamp((int)Math.Ceiling(paintBounds.Height * ctx.ScaleY), 1, 4096);
        s_vectorImageBrushDepth++;
        try
        {
            var buffer = Rasterize(drawing, width, height, viewport, ctx.TouchedSources);
            if (buffer is null) return true;
            pixels = BitmapPixelSnapshot.Create(buffer, width, height, width * 4,
                NativePixelFormat.Bgra8,
                Math.Clamp((int)Math.Ceiling(viewport.Width), 1, 16384),
                Math.Clamp((int)Math.Ceiling(viewport.Height), 1, 16384));
            return true;
        }
        finally
        {
            s_vectorImageBrushDepth--;
        }
    }

    private sealed class ImagePaint(ImageBrushSampler? sampler, Matrix inverse) : PixelPaint
    {
        public override (byte B, byte G, byte R, byte A) Sample(float x, float y)
        {
            if (sampler is null) return default;
            var point = inverse.Transform(new Point(x, y));
            var color = sampler.Sample(point.X, point.Y);
            static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
            return (Byte(color.B), Byte(color.G), Byte(color.R), Byte(color.A));
        }
    }

    private sealed class GradientPaint : PixelPaint
    {
        private readonly double[] _offsets;
        private readonly (byte B, byte G, byte R, byte A)[] _colors;
        private readonly GradientSpreadMethod _spread;
        private readonly double _opacity;
        private readonly Matrix _deviceToBrush;
        private readonly bool _isRadial;

        // Linear: projection onto start→end. Radial: focal ray solve in unit space.
        private readonly double _startX, _startY, _dirX, _dirY, _lengthSq;
        private readonly double _centerX, _centerY, _radiusX, _radiusY, _focusX, _focusY;

        private GradientPaint(
            GradientBrush brush, double opacity, Matrix deviceToBrush,
            bool isRadial,
            double startX, double startY, double dirX, double dirY,
            double centerX, double centerY, double radiusX, double radiusY,
            double focusX, double focusY)
        {
            var stops = brush.GradientStops.OrderBy(static s => s.Offset).ToArray();
            _offsets = new double[stops.Length];
            _colors = new (byte, byte, byte, byte)[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                _offsets[i] = stops[i].Offset;
                var c = stops[i].Color;
                _colors[i] = (c.B, c.G, c.R, c.A);
            }

            _spread = brush.SpreadMethod;
            _opacity = opacity;
            _deviceToBrush = deviceToBrush;
            _isRadial = isRadial;
            _startX = startX; _startY = startY;
            _dirX = dirX; _dirY = dirY;
            _lengthSq = dirX * dirX + dirY * dirY;
            _centerX = centerX; _centerY = centerY;
            _radiusX = radiusX; _radiusY = radiusY;
            _focusX = focusX; _focusY = focusY;
        }

        /// <summary>
        /// Returns a sampler for a linear/radial gradient with at least two stops, or null
        /// when per-pixel evaluation is not possible (caller falls back to the flat
        /// average). Geometry-relative coordinates resolve against the flattened
        /// geometry's bounds, absolute ones against source space.
        /// </summary>
        public static GradientPaint? TryCreate(Brush brush, in SoftwareRenderContext ctx, PathGeometry geometry)
        {
            if (brush.CssGradientLayout is { } cssLayout &&
                geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0)
            {
                if (cssLayout.Resolve(geometry.Bounds.Width, geometry.Bounds.Height) is not { } resolved) return null;
                brush = resolved;
            }
            if (brush is not GradientBrush gradient || gradient.GradientStops.Count < 2)
                return null;
            if (brush is not LinearGradientBrush && brush is not RadialGradientBrush)
                return null;

            double opacity = Math.Clamp(ctx.Opacity * gradient.Opacity, 0.0, 1.0);
            if (opacity <= 0) return null;

            // Device → source. The scanline hands us device pixels; brush geometry lives
            // in source space (optionally warped by gradientTransform).
            if (!ctx.Matrix.TryInvert(out var deviceToSource))
                return null;

            var deviceToBrush = deviceToSource;
            if (gradient.Transform is { } gt && !gt.Value.IsIdentity)
            {
                if (!gt.Value.TryInvert(out var brushInverse))
                    return null;
                // p_brush = p_source * inverse(gradientTransform)  (row vectors), so the
                // full chain is device → source → brush space.
                deviceToBrush = Matrix.Multiply(deviceToSource, brushInverse);
            }

            var bounds = geometry.Bounds;
            bool relative = gradient.MappingMode == BrushMappingMode.RelativeToBoundingBox;
            double bw = Math.Max(bounds.Width, 1e-9);
            double bh = Math.Max(bounds.Height, 1e-9);

            Point MapPoint(Point p) => relative
                ? new Point(bounds.X + p.X * bw, bounds.Y + p.Y * bh)
                : p;

            if (brush is LinearGradientBrush linear)
            {
                var s = MapPoint(linear.StartPoint);
                var e = MapPoint(linear.EndPoint);
                return new GradientPaint(
                    linear, opacity, deviceToBrush,
                    isRadial: false,
                    s.X, s.Y, e.X - s.X, e.Y - s.Y,
                    0, 0, 0, 0, 0, 0);
            }

            var radial = (RadialGradientBrush)brush;
            var center = MapPoint(radial.Center);
            var origin = MapPoint(radial.GradientOrigin);
            double rx = relative ? radial.RadiusX * bw : radial.RadiusX;
            double ry = relative ? radial.RadiusY * bh : radial.RadiusY;
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);

            // Normalize the focal point into the unit circle so the ray solve below
            // always has a real root.
            double fx = rx > 1e-12 ? (origin.X - center.X) / rx : 0.0;
            double fy = ry > 1e-12 ? (origin.Y - center.Y) / ry : 0.0;
            double focusLen = Math.Sqrt(fx * fx + fy * fy);
            if (focusLen > 0.99)
            {
                fx *= 0.99 / focusLen;
                fy *= 0.99 / focusLen;
            }

            return new GradientPaint(
                radial, opacity, deviceToBrush,
                isRadial: true,
                0, 0, 0, 0,
                center.X, center.Y, rx, ry, fx, fy);
        }

        public override (byte B, byte G, byte R, byte A) Sample(float deviceX, float deviceY)
        {
            var m = _deviceToBrush;
            double x = deviceX * m.M11 + deviceY * m.M21 + m.OffsetX;
            double y = deviceX * m.M12 + deviceY * m.M22 + m.OffsetY;

            double t;
            if (!_isRadial)
            {
                t = _lengthSq > 1e-12
                    ? ((x - _startX) * _dirX + (y - _startY) * _dirY) / _lengthSq
                    : 0.0;
            }
            else if (_radiusX <= 1e-12 || _radiusY <= 1e-12)
            {
                t = 1.0;
            }
            else
            {
                double px = (x - _centerX) / _radiusX;
                double py = (y - _centerY) / _radiusY;
                double dx = px - _focusX;
                double dy = py - _focusY;
                double a = dx * dx + dy * dy;
                if (a <= 1e-12)
                {
                    t = 0.0;
                }
                else
                {
                    double b = 2.0 * (_focusX * dx + _focusY * dy);
                    double c = _focusX * _focusX + _focusY * _focusY - 1.0;
                    double disc = Math.Max(b * b - 4.0 * a * c, 0.0);
                    double s = (-b + Math.Sqrt(disc)) / (2.0 * a);
                    t = s > 1e-12 ? 1.0 / s : 0.0;
                }
            }

            t = _spread switch
            {
                GradientSpreadMethod.Repeat => t - Math.Floor(t),
                GradientSpreadMethod.Reflect => Reflect(t),
                _ => Math.Clamp(t, 0.0, 1.0),
            };

            var offsets = _offsets;
            var colors = _colors;
            if (t <= offsets[0]) return Apply(colors[0]);
            if (t >= offsets[^1]) return Apply(colors[^1]);

            for (int i = 1; i < offsets.Length; i++)
            {
                if (t > offsets[i]) continue;
                double span = offsets[i] - offsets[i - 1];
                double local = span > 1e-12 ? (t - offsets[i - 1]) / span : 0.0;
                var from = colors[i - 1];
                var to = colors[i];
                return (
                    (byte)(from.B + (to.B - from.B) * local + 0.5),
                    (byte)(from.G + (to.G - from.G) * local + 0.5),
                    (byte)(from.R + (to.R - from.R) * local + 0.5),
                    (byte)((from.A + (to.A - from.A) * local) * _opacity + 0.5));
            }

            return Apply(colors[^1]);
        }

        private (byte B, byte G, byte R, byte A) Apply((byte B, byte G, byte R, byte A) c)
            => (c.B, c.G, c.R, (byte)(c.A * _opacity + 0.5));

        private static double Reflect(double t)
        {
            var wrapped = Math.Abs(t) % 2.0;
            return wrapped > 1.0 ? 2.0 - wrapped : wrapped;
        }
    }

    /// <summary>
    /// Blits an <see cref="ImageDrawing"/> (e.g. an SVG <c>&lt;image&gt;</c> carrying a
    /// base64-embedded raster) into the pixel buffer. Uses inverse texture mapping:
    /// every destination pixel inside the transformed rect's bounding box is mapped
    /// back through the context transform to image-local space, then to a source
    /// pixel via bilinear sampling. This correctly handles the scale and translate of
    /// the SVG viewport as well as element-level rotate / skew.
    /// </summary>
    private static void RenderImageDrawing(ImageDrawing drawing, in SoftwareRenderContext ctx)
    {
        if (drawing.ImageSource is not { } imageSource) return;

        // Recorded before any early return: the caller's raster cache has to learn about the
        // dependency even on the frame where nothing could be blitted, because THAT is the frame
        // whose blank output would otherwise be cached forever.
        ctx.TouchedSources?.Add(imageSource);

        var rect = drawing.Rect;
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return;

        double opacity = Math.Clamp(ctx.Opacity, 0.0, 1.0);
        if (opacity <= 0) return;

        // Transform the rect's four corners into pixel space to get the affected
        // bounding box (a rotated/skewed rect still has an axis-aligned cover).
        var c0 = ctx.TransformPoint(new Point(rect.X, rect.Y));
        var c1 = ctx.TransformPoint(new Point(rect.X + rect.Width, rect.Y));
        var c2 = ctx.TransformPoint(new Point(rect.X + rect.Width, rect.Y + rect.Height));
        var c3 = ctx.TransformPoint(new Point(rect.X, rect.Y + rect.Height));

        float minXf = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X));
        float maxXf = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X));
        float minYf = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y));
        float maxYf = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y));

        int xStart = Math.Max(0, (int)Math.Floor(minXf));
        int xEnd = Math.Min(ctx.Width - 1, (int)Math.Ceiling(maxXf));
        int yStart = Math.Max(0, (int)Math.Floor(minYf));
        int yEnd = Math.Min(ctx.Height - 1, (int)Math.Ceiling(maxYf));
        if (xStart > xEnd || yStart > yEnd) return;

        // This drawing path reads pixels directly and never reaches the GPU bitmap choke point.
        // Resolve every raster ImageSource through the shared sampler so deferred BitmapImages,
        // WriteableBitmaps, animated frames and RGBA decoders all use one snapshot contract.
        var snapshot = ImageBrushSampler.GetPixels(
            imageSource,
            Math.Clamp((int)Math.Ceiling(maxXf - minXf), 1, 16384),
            Math.Clamp((int)Math.Ceiling(maxYf - minYf), 1, 16384),
            cover: false);
        if (snapshot is null) return;

        int srcW = snapshot.Width;
        int srcH = snapshot.Height;
        if (srcW <= 0 || srcH <= 0 || snapshot.Pixels.Length < 4) return;

        // Invert the device transform so a destination pixel maps back to drawing-local space.
        if (!ctx.Matrix.TryInvert(out var inv)) return;

        var clip = ctx.ClipMask;
        for (int y = yStart; y <= yEnd; y++)
        {
            for (int x = xStart; x <= xEnd; x++)
            {
                // Pixel center -> drawing-local space.
                var local = inv.Transform(new Point(x + 0.5, y + 0.5));

                // Drawing-local -> normalized [0,1) within the image rect.
                double u = (local.X - rect.X) / rect.Width;
                double v = (local.Y - rect.Y) / rect.Height;
                if (u < 0.0 || u >= 1.0 || v < 0.0 || v >= 1.0) continue;

                // Bilinear sample of the source texels.
                double fx = u * srcW - 0.5;
                double fy = v * srcH - 0.5;
                var sampled = ImageBrushSampler.SamplePixels(snapshot, fx, fy);
                if (sampled.A <= 0) continue;
                byte outA = (byte)Math.Clamp(Math.Round(sampled.A * opacity * 255), 0, 255);

                // Apply clip coverage.
                if (clip != null)
                {
                    byte cm = clip[y * ctx.Width + x];
                    if (cm == 0) continue;
                    if (cm != 255) outA = (byte)(outA * cm / 255);
                }
                if (outA == 0) continue;

                BlendPixel(ctx.Pixels, ctx.Stride, x, y,
                    (byte)Math.Clamp(Math.Round(sampled.B * 255), 0, 255),
                    (byte)Math.Clamp(Math.Round(sampled.G * 255), 0, 255),
                    (byte)Math.Clamp(Math.Round(sampled.R * 255), 0, 255),
                    outA);
            }
        }
    }

    private static PathGeometry? GetFlattenedGeometry(Geometry geometry, double tolerance)
    {
        // Delegate to the geometry's own flattener: RectangleGeometry (incl. rx/ry
        // rounded corners), EllipseGeometry (tolerance-adaptive), LineGeometry,
        // GeometryGroup and PathGeometry all override GetFlattenedPathGeometry with the
        // correct, tolerance-aware tessellation. Doing our own low-poly approximations
        // here (the old 32-gon ellipse / corner-dropping rectangle) was both jaggy and
        // wrong (it lost rounded-rect corners entirely).
        try
        {
            if (tolerance <= 0 || double.IsNaN(tolerance)) tolerance = 0.25;
            return geometry.GetFlattenedPathGeometry(tolerance, ToleranceType.Absolute);
        }
        catch
        {
            try { return geometry.GetFlattenedPathGeometry(); }
            catch { return null; }
        }
    }

    /// <summary>
    /// Extracts a representative BGRA color from any brush type.
    /// For SolidColorBrush: exact color. For gradients: offset-weighted average of stops.
    /// For ImageBrush: an average sampled from the source's pixel buffer.
    /// This ensures filled SVG elements render with at least an approximate
    /// color instead of being completely invisible when the brush type is
    /// not natively supported by the software rasterizer.
    /// </summary>
    /// <param name="paintedGeometry">
    /// The flattened geometry this brush is about to paint, in source units. Only read for an
    /// <see cref="ImageBrush"/>, whose decode request needs an honest size hint.
    /// </param>
    private static (byte B, byte G, byte R, byte A) ExtractBrushColor(
        Brush brush, in SoftwareRenderContext ctx, PathGeometry paintedGeometry)
    {
        var opacity = ctx.Opacity;

        if (brush is SolidColorBrush solid)
        {
            var c = solid.Color;
            var a = (byte)(c.A * opacity * solid.Opacity);
            return (c.B, c.G, c.R, a);
        }

        if (brush is LinearGradientBrush lgb && lgb.GradientStops.Count > 0)
        {
            var c = AverageGradient(lgb.GradientStops);
            var a = (byte)(c.A * opacity * lgb.Opacity);
            return (c.B, c.G, c.R, a);
        }

        if (brush is RadialGradientBrush rgb && rgb.GradientStops.Count > 0)
        {
            var c = AverageGradient(rgb.GradientStops);
            var a = (byte)(c.A * opacity * rgb.Opacity);
            return (c.B, c.G, c.R, a);
        }

        if (brush is ImageBrush imageBrush)
        {
            if (imageBrush.ImageSource is BitmapImage bitmap)
            {
                ctx.TouchedSources?.Add(bitmap);

                // The other half of RC2, and the one the choke-point fix cannot reach: a brush used
                // as a vector FILL never goes near GetNativeBitmap, so before this line nothing in
                // the process ever asked a URI-backed brush source for its pixels — it sampled an
                // empty buffer on the first frame and on every frame after it, and fell through to
                // the opaque-black last resort permanently. (At HEAD the decode was eager, so the
                // average colour was always available; this leg is a regression from that, not a
                // pre-existing gap.)
                //
                // The hint is the geometry's own device-space extent, floored at 1 for the same
                // reason the ImageDrawing leg floors its own: an all-zero request means "unbounded"
                // to the deferred decoder and resolves to the natural size, which a growth-only
                // bucket ladder can never walk back. Over-asking is the expensive mistake here, and
                // this is a FLAT-AVERAGE fill — the 64-sample average below cannot use more
                // resolution than the shape it paints — so the shape's extent is both honest and
                // generous.
                var extent = paintedGeometry.Bounds;
                bitmap.RequestDecode(
                    Math.Clamp((int)Math.Ceiling(extent.Width * ctx.ScaleX), 1, 16384),
                    Math.Clamp((int)Math.Ceiling(extent.Height * ctx.ScaleY), 1, 16384),
                    cover: false);

                if (bitmap.IsDeferredDecodePending)
                {
                    // Paint NOTHING this frame rather than the black silhouette below. The decode
                    // requested above lands within a frame or two and RasterChanged then drops the
                    // caller's cached raster, so the shape appears at its real average colour; a
                    // black rectangle burned into that cached raster is a WRONG image, which is
                    // worse than a shape that is briefly absent. A source that genuinely cannot
                    // decode still ends up on the diagnostics channel through its own failure path.
                    return (0, 0, 0, 0);
                }
            }

            var sampled = SampleImageAverage(imageBrush.ImageSource);
            if (sampled.HasValue)
            {
                var c = sampled.Value;
                var a = (byte)(c.A * opacity * imageBrush.Opacity);
                return (c.B, c.G, c.R, a);
            }
            // No pixel data available (a vector source, or a raster whose decode failed) —
            // fall through to the opaque-black last resort below.
        }

        // Unknown brush type — render as opaque black as last resort
        return (0, 0, 0, (byte)(255 * opacity));
    }

    /// <summary>
    /// Averages a gradient's stops (offset-weighted) into a single representative color.
    /// The software path fills with one flat color, so the weighted average reads closer
    /// to the gradient's overall tone than just the first stop did.
    /// </summary>
    private static Color AverageGradient(IList<GradientStop> stops)
    {
        if (stops.Count == 1) return stops[0].Color;

        double sumB = 0, sumG = 0, sumR = 0, sumA = 0, wsum = 0;
        for (int i = 0; i < stops.Count; i++)
        {
            double prev = i > 0 ? stops[i - 1].Offset : stops[i].Offset;
            double next = i < stops.Count - 1 ? stops[i + 1].Offset : stops[i].Offset;
            double w = Math.Max(1e-3, (next - prev) * 0.5 + 1e-3);
            var c = stops[i].Color;
            sumB += c.B * w; sumG += c.G * w; sumR += c.R * w; sumA += c.A * w; wsum += w;
        }
        if (wsum <= 0) return stops[0].Color;
        return Color.FromArgb((byte)(sumA / wsum), (byte)(sumR / wsum), (byte)(sumG / wsum), (byte)(sumB / wsum));
    }

    /// <summary>
    /// Samples a coarse-grid average BGRA color from <paramref name="source"/>
    /// when its raw pixel buffer is reachable. Returns <see langword="null"/>
    /// for sources that have not been decoded yet or do not expose pixels
    /// (e.g. <see cref="SvgImage"/> / <see cref="DrawingImage"/>).
    /// </summary>
    private static Color? SampleImageAverage(ImageSource? source)
    {
        // Read through the snapshot so the buffer cannot be swapped for a differently-sized one
        // between the length check and the loop. Deliberately does not request a decode of its own:
        // the caller does that, once, with the size hint only it can compute — the extent of the
        // geometry the brush is about to fill.
        if (source is BitmapImage bitmap &&
            bitmap.TryGetPixelSnapshot(out var snapshot) &&
            snapshot is { Pixels.Length: >= 4 })
        {
            var pixels = snapshot.Pixels;
            const int MaxSamples = 64;
            int totalPixels = pixels.Length / 4;
            int step = Math.Max(1, totalPixels / MaxSamples);

            long sumB = 0, sumG = 0, sumR = 0, sumA = 0;
            int count = 0;
            for (int i = 0; i < totalPixels; i += step)
            {
                int off = i * 4;
                sumB += pixels[off];
                sumG += pixels[off + 1];
                sumR += pixels[off + 2];
                sumA += pixels[off + 3];
                count++;
            }

            if (count == 0) return null;
            return Color.FromArgb(
                (byte)(sumA / count),
                (byte)(sumR / count),
                (byte)(sumG / count),
                (byte)(sumB / count));
        }

        return null;
    }

    #region Coverage-AA Fill / Stroke / Clip

    private static void FillGeometry(PathGeometry geometry, in SoftwareRenderContext ctx,
        byte b, byte g, byte r, byte a, PixelPaint? paint = null)
    {
        if (a == 0) return;

        // Collect every filled figure's device-space contour. Compound paths (a shape
        // with holes) and single self-intersecting paths are both handled by feeding
        // all contours to one fill-rule pass.
        var contours = new List<List<(float X, float Y)>>();
        foreach (var figure in geometry.Figures)
        {
            if (!figure.IsFilled) continue;
            var points = GetTransformedPoints(figure, ctx);
            if (points.Count < 3) continue;
            contours.Add(points);
        }

        if (contours.Count == 0) return;

        // Honor the geometry's fill rule for single AND multiple figures. SVG defaults
        // to nonzero; the previous code hard-coded even-odd for the single-figure case,
        // which hollowed out self-intersecting single paths (stars, knots, ...).
        bool nonZero = geometry.FillRule == FillRule.Nonzero;
        RasterizeCoverage(ctx, contours, nonZero, isMax: false, b, g, r, a, paint);
    }

    private static Rect StrokePaintBounds(PathGeometry geometry,
        in SoftwareRenderContext ctx, Pen pen,
        List<List<(float X, float Y)>> contours)
    {
        var centerline = geometry.Bounds;
        var halfWidth = pen.Thickness / 2;
        var left = centerline.X - halfWidth;
        var top = centerline.Y - halfWidth;
        var right = centerline.Right + halfWidth;
        var bottom = centerline.Bottom + halfWidth;
        if (ctx.Matrix.TryInvert(out var inverse))
        {
            foreach (var contour in contours)
            foreach (var vertex in contour)
            {
                var point = inverse.Transform(new Point(vertex.X, vertex.Y));
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) continue;
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
        }
        return new Rect(left, top, right - left, bottom - top);
    }

    private static List<List<(float X, float Y)>> BuildStrokeContours(
        PathGeometry geometry, in SoftwareRenderContext ctx, Pen pen)
    {
        var contours = new List<List<(float X, float Y)>>();

        double strokeWidth = pen.Thickness;
        double halfW = strokeWidth * 0.5;

        // DashStyle stores dash lengths in multiples of pen thickness. Splitting the
        // source path before its affine transform also preserves dash lengths under
        // nonuniform scale and shear.
        // Ignoring the pattern painted every dashed SVG stroke as a solid line.
        double[]? sourceDashes = null;
        double sourceDashOffset = 0;
        if (pen.DashStyle?.Dashes is { Count: > 0 } dashes)
        {
            double unit = strokeWidth;
            double minDashInSource = 0.05 /
                Math.Max(Math.Sqrt(ctx.ScaleX * ctx.ScaleX +
                    ctx.ScaleY * ctx.ScaleY), 1e-6);
            // SVG: an odd-length dash array repeats itself once so on/off alternation
            // stays consistent ("4 2 1" ≡ "4 2 1 4 2 1").
            int patternLength = dashes.Count % 2 == 0 ? dashes.Count : dashes.Count * 2;
            double total = 0;
            sourceDashes = new double[patternLength];
            for (int i = 0; i < patternLength; i++)
            {
                sourceDashes[i] = Math.Max(0, dashes[i % dashes.Count]) * unit;
                total += sourceDashes[i];
            }
            if (!double.IsFinite(total)) return contours;
            if (total <= 1e-6)
            {
                sourceDashes = null;   // all-zero pattern = solid per SVG
            }
            else
            {
                // Preserve zero-length "on" entries as tiny centerlines so
                // round and square dash caps can paint dots at their positions.
                for (int i = 0; i < sourceDashes.Length; i++)
                    sourceDashes[i] = sourceDashes[i] == 0
                        ? Math.Min(1e-4, minDashInSource * 0.5)
                        : Math.Max(sourceDashes[i], minDashInSource);
                sourceDashOffset = pen.DashStyle.Offset * unit;
                if (!double.IsFinite(sourceDashOffset)) sourceDashes = null;
            }
        }

        // Widen the path in source space and then transform the resulting contours.
        // This makes the pen follow both axes of an arbitrary affine transform.
        var sourceCtx = new SoftwareRenderContext(ctx.Pixels, ctx.Width, ctx.Height,
            ctx.Stride, Matrix.Identity);
        double diskScale = Math.Max(ctx.ScaleX, ctx.ScaleY);

        void AddCap((float X, float Y) endpoint, (float X, float Y) adjacent, PenLineCap cap)
        {
            if (cap == PenLineCap.Flat) return;
            if (cap == PenLineCap.Round)
            {
                AddDisk(contours, endpoint, halfW, diskScale);
                return;
            }

            double dx = endpoint.X - adjacent.X;
            double dy = endpoint.Y - adjacent.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 1e-6) return;
            double ux = dx / length, uy = dy / length;
            var tip = ((float)(endpoint.X + ux * halfW),
                (float)(endpoint.Y + uy * halfW));
            if (cap == PenLineCap.Square)
            {
                AddSegmentQuad(contours, endpoint, tip, halfW);
            }
            else if (cap == PenLineCap.Triangle)
            {
                float nx = (float)(-uy * halfW), ny = (float)(ux * halfW);
                contours.Add(new List<(float X, float Y)>
                {
                    (endpoint.X + nx, endpoint.Y + ny), tip,
                    (endpoint.X - nx, endpoint.Y - ny),
                });
            }
        }

        static bool SamePoint((float X, float Y) left, (float X, float Y) right)
            => Math.Abs(left.X - right.X) <= 1e-3f &&
               Math.Abs(left.Y - right.Y) <= 1e-3f;

        void AddJoin((float X, float Y) previous, (float X, float Y) vertex,
            (float X, float Y) next)
        {
            double dx0 = vertex.X - previous.X, dy0 = vertex.Y - previous.Y;
            double dx1 = next.X - vertex.X, dy1 = next.Y - vertex.Y;
            double len0 = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
            double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            if (len0 <= 1e-6 || len1 <= 1e-6) return;
            dx0 /= len0;
            dy0 /= len0;
            dx1 /= len1;
            dy1 /= len1;
            double cross = dx0 * dy1 - dy0 * dx1;
            if (Math.Abs(cross) <= 1e-9) return;

            if (pen.LineJoin == PenLineJoin.Round)
            {
                AddDisk(contours, vertex, halfW, diskScale);
                return;
            }

            double side = cross > 0 ? -halfW : halfW;
            var outer0 = ((float)(vertex.X - dy0 * side),
                (float)(vertex.Y + dx0 * side));
            var outer1 = ((float)(vertex.X - dy1 * side),
                (float)(vertex.Y + dx1 * side));
            if (pen.LineJoin == PenLineJoin.Miter &&
                double.IsFinite(pen.MiterLimit) && pen.MiterLimit >= 1)
            {
                double betweenX = outer1.Item1 - outer0.Item1;
                double betweenY = outer1.Item2 - outer0.Item2;
                double t = (betweenX * dy1 - betweenY * dx1) / cross;
                double tipX = outer0.Item1 + t * dx0;
                double tipY = outer0.Item2 + t * dy0;
                double reachX = tipX - vertex.X;
                double reachY = tipY - vertex.Y;
                if (double.IsFinite(tipX) && double.IsFinite(tipY) &&
                    reachX * reachX + reachY * reachY <=
                    halfW * halfW * pen.MiterLimit * pen.MiterLimit)
                {
                    contours.Add(new List<(float X, float Y)>
                    {
                        vertex, outer0, ((float)tipX, (float)tipY), outer1,
                    });
                    return;
                }
            }
            contours.Add(new List<(float X, float Y)> { vertex, outer0, outer1 });
        }

        void EmitPolyline(List<(float X, float Y)> pts, bool isClosed,
            PenLineCap startCap, PenLineCap endCap)
        {
            if (pts.Count < 2)
            {
                if (pts.Count == 1 && startCap == PenLineCap.Round)
                    AddDisk(contours, pts[0], halfW, diskScale);
                return;
            }

            int segCount = isClosed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % pts.Count];
                AddSegmentQuad(contours, p0, p1, halfW);
            }

            if (isClosed)
            {
                for (int i = 0; i < pts.Count; i++)
                    AddJoin(pts[(i - 1 + pts.Count) % pts.Count], pts[i],
                        pts[(i + 1) % pts.Count]);
            }
            else
            {
                for (int i = 1; i < pts.Count - 1; i++)
                    AddJoin(pts[i - 1], pts[i], pts[i + 1]);
                AddCap(pts[0], pts[1], startCap);
                AddCap(pts[^1], pts[^2], endCap);
            }
        }

        foreach (var figure in geometry.Figures)
        {
            var pts = GetTransformedPoints(figure, sourceCtx);
            // A repeated vertex has no incoming or outgoing tangent. Remove it
            // before joins and dash traversal so it cannot erase a real corner.
            for (int i = pts.Count - 1; i > 0; i--)
            {
                double dx = pts[i].X - pts[i - 1].X;
                double dy = pts[i].Y - pts[i - 1].Y;
                if (dx * dx + dy * dy <= 1e-12)
                    pts.RemoveAt(i);
            }
            if (figure.IsClosed && pts.Count > 2 && SamePoint(pts[0], pts[^1]))
                pts.RemoveAt(pts.Count - 1);
            if (sourceDashes == null)
            {
                EmitPolyline(pts, figure.IsClosed, pen.StartLineCap, pen.EndLineCap);
                continue;
            }

            // Dashed: walk the (closed → wrapped) polyline by arc length and emit each
            // "on" run as its own open sub-polyline, capped like any open stroke end.
            var dashRuns = SplitByDashes(pts, figure.IsClosed, sourceDashes, sourceDashOffset);
            if (figure.IsClosed && dashRuns.Count > 1 &&
                SamePoint(dashRuns[0][0], pts[0]) &&
                SamePoint(dashRuns[^1][^1], pts[0]))
            {
                dashRuns[^1].AddRange(dashRuns[0].Skip(1));
                dashRuns.RemoveAt(0);
            }
            foreach (var sub in dashRuns)
            {
                var closedRun = figure.IsClosed && sub.Count > 2 &&
                    SamePoint(sub[0], sub[^1]);
                if (closedRun) sub.RemoveAt(sub.Count - 1);
                EmitPolyline(sub, closedRun, pen.DashCap, pen.DashCap);
            }
        }

        foreach (var contour in contours)
        for (int i = 0; i < contour.Count; i++)
        {
            var vertex = contour[i];
            contour[i] = ctx.TransformPoint(new Point(vertex.X, vertex.Y));
        }
        return contours;
    }

    /// <summary>
    /// Splits a source-space polyline into the "on" runs of a dash pattern (source
    /// units). A closed figure is walked with its wrap-around edge included.
    /// </summary>
    private static List<List<(float X, float Y)>> SplitByDashes(
        List<(float X, float Y)> pts, bool isClosed, double[] dashes, double dashOffset)
    {
        var result = new List<List<(float X, float Y)>>();
        if (pts.Count < 2) return result;

        double total = 0;
        foreach (var d in dashes) total += d;

        // Normalize the starting offset into the pattern.
        double offset = dashOffset % total;
        if (offset < 0) offset += total;
        int dashIndex = 0;
        double remaining = dashes[0];
        while (offset > 0)
        {
            if (offset < remaining) { remaining -= offset; break; }
            offset -= remaining;
            dashIndex = (dashIndex + 1) % dashes.Length;
            remaining = dashes[dashIndex];
        }
        bool isOn = dashIndex % 2 == 0;

        List<(float X, float Y)>? current = isOn ? new List<(float, float)> { pts[0] } : null;

        int edgeCount = isClosed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < edgeCount; i++)
        {
            var p0 = pts[i];
            var p1 = pts[(i + 1) % pts.Count];
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            double segLen = Math.Sqrt(dx * dx + dy * dy);
            if (segLen <= 1e-9) continue;

            double consumed = 0;
            while (consumed < segLen)
            {
                double step = Math.Min(segLen - consumed, remaining);
                double t1 = (consumed + step) / segLen;
                var end = ((float)(p0.X + dx * t1), (float)(p0.Y + dy * t1));

                if (isOn)
                {
                    current ??= new List<(float, float)>
                    {
                        ((float)(p0.X + dx * (consumed / segLen)), (float)(p0.Y + dy * (consumed / segLen)))
                    };
                    current.Add(end);
                }

                consumed += step;
                remaining -= step;
                if (remaining <= 1e-9)
                {
                    if (isOn && current is { Count: >= 2 }) result.Add(current);
                    current = null;
                    dashIndex = (dashIndex + 1) % dashes.Length;
                    remaining = dashes[dashIndex];
                    isOn = dashIndex % 2 == 0;
                }
            }
        }

        if (isOn && current is { Count: >= 2 }) result.Add(current);
        return result;
    }

    /// <summary>Appends the 4-point quad spanning <paramref name="p0"/>→<paramref name="p1"/>
    /// offset by ±halfWidth along the segment's source-space normal.</summary>
    private static void AddSegmentQuad(List<List<(float X, float Y)>> contours,
        (float X, float Y) p0, (float X, float Y) p1, double halfWidth)
    {
        double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;
        float nx = (float)(-dy / len * halfWidth);
        float ny = (float)(dx / len * halfWidth);
        contours.Add(new List<(float, float)>(4)
        {
            (p0.X + nx, p0.Y + ny),
            (p1.X + nx, p1.Y + ny),
            (p1.X - nx, p1.Y - ny),
            (p0.X - nx, p0.Y - ny),
        });
    }

    /// <summary>Appends a polygon approximation of a filled disk (round join/cap).</summary>
    private static void AddDisk(List<List<(float X, float Y)>> contours,
        (float X, float Y) center, double radius, double deviceScale)
    {
        if (radius <= 0) return;
        int sides = Math.Clamp((int)(radius * deviceScale * 1.5) + 6, 8, 128);
        var poly = new List<(float, float)>(sides);
        for (int i = 0; i < sides; i++)
        {
            double ang = 2 * Math.PI * i / sides;
            poly.Add((center.X + (float)(radius * Math.Cos(ang)), center.Y + (float)(radius * Math.Sin(ang))));
        }
        contours.Add(poly);
    }

    /// <summary>
    /// Analytic-coverage scanline rasterizer shared by fill and stroke. Each output row
    /// is sampled by 4 vertical sub-scanlines; within each, filled spans contribute
    /// fractional horizontal coverage. <paramref name="isMax"/> selects per-contour
    /// max-coverage (strokes — overlap must not darken) vs. combined fill-rule coverage
    /// across all contours (fills). The context's clip mask, if any, attenuates coverage.
    /// </summary>
    private static void RasterizeCoverage(
        in SoftwareRenderContext ctx, List<List<(float X, float Y)>> contours,
        bool nonZero, bool isMax, byte b, byte g, byte r, byte a,
        PixelPaint? paint = null)
    {
        byte[] pixels = ctx.Pixels;
        int width = ctx.Width, height = ctx.Height, stride = ctx.Stride;
        byte[]? clip = ctx.ClipMask;

        if (!ComputeBounds(contours, width, height, out int ix0, out int iy0, out int ix1, out int iy1))
            return;

        int rowW = ix1 - ix0;
        var cov = new float[rowW];
        float[]? tmp = isMax ? new float[rowW] : null;
        const int kSub = 4;
        const float kInv = 1.0f / kSub;
        var xs = new List<(float x, int dir)>();
        var single = isMax ? new List<List<(float X, float Y)>>(1) { null! } : null;

        for (int row = iy0; row < iy1; row++)
        {
            Array.Clear(cov, 0, rowW);

            if (isMax)
            {
                foreach (var contour in contours)
                {
                    Array.Clear(tmp!, 0, rowW);
                    single![0] = contour;
                    for (int k = 0; k < kSub; k++)
                    {
                        float sy = row + (k + 0.5f) * kInv;
                        AccumulateScanline(single!, sy, nonZero: false, ix0, ix1, kInv, tmp!, xs);
                    }
                    for (int t = 0; t < rowW; t++)
                        if (tmp![t] > cov[t]) cov[t] = tmp[t];
                }
            }
            else
            {
                for (int k = 0; k < kSub; k++)
                {
                    float sy = row + (k + 0.5f) * kInv;
                    AccumulateScanline(contours, sy, nonZero, ix0, ix1, kInv, cov, xs);
                }
            }

            int rowBase = row * width;
            for (int px = ix0; px < ix1; px++)
            {
                float c = cov[px - ix0];
                if (c <= 0f) continue;
                if (c > 1f) c = 1f;
                if (clip != null)
                {
                    byte cm = clip[rowBase + px];
                    if (cm == 0) continue;
                    if (cm != 255) c *= cm / 255f;
                }

                byte pb = b, pg = g, pr = r, pa = a;
                if (paint != null)
                {
                    // Per-pixel gradient color, sampled at the pixel center; the sampled
                    // alpha already folds in stop alpha, brush opacity and group opacity.
                    (pb, pg, pr, pa) = paint.Sample(px + 0.5f, row + 0.5f);
                }

                byte aa = c >= 0.999f ? pa : (byte)(pa * c + 0.5f);
                if (aa == 0) continue;
                BlendPixel(pixels, stride, px, row, pb, pg, pr, aa);
            }
        }
    }

    /// <summary>
    /// Rasterizes <paramref name="contours"/> into an 8-bit coverage <paramref name="mask"/>
    /// (0..255) for use as a clip mask. Same analytic-coverage scanline as the fill path.
    /// </summary>
    private static void RasterizeMask(
        List<List<(float X, float Y)>> contours, bool nonZero, int width, int height, byte[] mask)
    {
        if (!ComputeBounds(contours, width, height, out int ix0, out int iy0, out int ix1, out int iy1))
            return;

        int rowW = ix1 - ix0;
        var cov = new float[rowW];
        const int kSub = 4;
        const float kInv = 1.0f / kSub;
        var xs = new List<(float x, int dir)>();

        for (int row = iy0; row < iy1; row++)
        {
            Array.Clear(cov, 0, rowW);
            for (int k = 0; k < kSub; k++)
            {
                float sy = row + (k + 0.5f) * kInv;
                AccumulateScanline(contours, sy, nonZero, ix0, ix1, kInv, cov, xs);
            }
            int rowBase = row * width;
            for (int px = ix0; px < ix1; px++)
            {
                float c = cov[px - ix0];
                if (c <= 0f) continue;
                if (c > 1f) c = 1f;
                mask[rowBase + px] = (byte)(c * 255f + 0.5f);
            }
        }
    }

    private static bool ComputeBounds(
        List<List<(float X, float Y)>> contours, int width, int height,
        out int ix0, out int iy0, out int ix1, out int iy1)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in contours)
        {
            foreach (var p in c)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
        ix0 = iy0 = ix1 = iy1 = 0;
        if (maxX < minX || maxY < minY) return false;

        ix0 = Math.Max(0, (int)Math.Floor(minX));
        ix1 = Math.Min(width, (int)Math.Ceiling(maxX));
        iy0 = Math.Max(0, (int)Math.Floor(minY));
        iy1 = Math.Min(height, (int)Math.Ceiling(maxY));
        return ix1 > ix0 && iy1 > iy0;
    }

    /// <summary>
    /// For one sub-scanline <paramref name="sy"/>, finds edge crossings across all
    /// <paramref name="contours"/>, resolves filled spans by even-odd or nonzero winding,
    /// and accumulates each span's analytic horizontal coverage (×<paramref name="weight"/>)
    /// into <paramref name="cov"/>.
    /// </summary>
    private static void AccumulateScanline(
        List<List<(float X, float Y)>> contours, float sy, bool nonZero,
        int ix0, int ix1, float weight, float[] cov, List<(float x, int dir)> xs)
    {
        xs.Clear();
        foreach (var pts in contours)
        {
            int n = pts.Count;
            if (n < 2) continue;
            for (int i = 0; i < n; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % n];
                float y0 = p0.Y, y1 = p1.Y;
                // Half-open rule: counts an edge if it straddles sy, robust at vertices,
                // skips horizontal edges (y0==y1).
                if ((y0 <= sy && y1 > sy) || (y1 <= sy && y0 > sy))
                {
                    float t = (sy - y0) / (y1 - y0);
                    float x = p0.X + t * (p1.X - p0.X);
                    xs.Add((x, y1 > y0 ? 1 : -1));
                }
            }
        }
        if (xs.Count < 2) return;
        xs.Sort(static (u, v) => u.x.CompareTo(v.x));

        if (nonZero)
        {
            int winding = 0;
            float spanStart = 0f;
            for (int i = 0; i < xs.Count; i++)
            {
                int prev = winding;
                winding += xs[i].dir;
                if (prev == 0 && winding != 0) spanStart = xs[i].x;
                else if (prev != 0 && winding == 0) AccumulateSpan(spanStart, xs[i].x, ix0, ix1, weight, cov);
            }
        }
        else
        {
            for (int i = 0; i + 1 < xs.Count; i += 2)
                AccumulateSpan(xs[i].x, xs[i + 1].x, ix0, ix1, weight, cov);
        }
    }

    private static void AccumulateSpan(float xL, float xR, int ix0, int ix1, float weight, float[] cov)
    {
        if (xR <= xL) return;
        int cx0 = Math.Max(ix0, (int)Math.Floor(xL));
        int cx1 = Math.Min(ix1, (int)Math.Ceiling(xR));
        for (int px = cx0; px < cx1; px++)
        {
            float c = Math.Min(px + 1f, xR) - Math.Max((float)px, xL);
            if (c <= 0f) continue;
            if (c > 1f) c = 1f;
            cov[px - ix0] += c * weight;
        }
    }

    /// <summary>
    /// Builds a clip coverage mask from <paramref name="clipGeometry"/> (resolved through
    /// the current device transform), intersects it with any existing clip mask, and
    /// returns a context that attenuates all subsequent drawing by it.
    /// </summary>
    private static SoftwareRenderContext ApplyClip(in SoftwareRenderContext ctx, Geometry clipGeometry)
    {
        double effScale = Math.Max(ctx.ScaleX, ctx.ScaleY);
        double tol = effScale > 1e-6 ? 0.3 / effScale : 0.3;
        var flat = GetFlattenedGeometry(clipGeometry, tol);
        if (flat == null) return ctx;
        if (flat.Figures.Count == 0)
            return ctx.WithClipMask(new byte[ctx.Width * ctx.Height]);

        // Honor a transform set directly on the clip geometry (composed before ctx,
        // like fill/stroke do for geometry.Transform).
        var clipCtx = ctx;
        if (clipGeometry.Transform != null && !clipGeometry.Transform.Value.IsIdentity)
            clipCtx = ctx.WithTransform(clipGeometry.Transform.Value);

        var contours = new List<List<(float X, float Y)>>();
        foreach (var figure in flat.Figures)
        {
            var pts = GetTransformedPoints(figure, clipCtx);
            if (pts.Count >= 3) contours.Add(pts);
        }
        if (contours.Count == 0)
            return ctx.WithClipMask(new byte[ctx.Width * ctx.Height]);

        var mask = new byte[ctx.Width * ctx.Height];
        bool nonZero = flat.FillRule == FillRule.Nonzero;
        RasterizeMask(contours, nonZero, ctx.Width, ctx.Height, mask);

        // Intersect with the inherited clip (min of coverages).
        if (ctx.ClipMask is { } prev)
        {
            for (int i = 0; i < mask.Length; i++)
                if (prev[i] < mask[i]) mask[i] = prev[i];
        }
        return ctx.WithClipMask(mask);
    }

    private static List<(float X, float Y)> GetTransformedPoints(PathFigure figure, in SoftwareRenderContext ctx)
    {
        var points = new List<(float X, float Y)>();
        points.Add(ctx.TransformPoint(figure.StartPoint));

        var current = figure.StartPoint;
        foreach (var segment in figure.Segments)
        {
            if (segment is LineSegment ls)
            {
                points.Add(ctx.TransformPoint(ls.Point));
                current = ls.Point;
            }
            else if (segment is PolyLineSegment pls)
            {
                foreach (var pt in pls.Points)
                {
                    points.Add(ctx.TransformPoint(pt));
                    current = pt;
                }
            }
            else if (segment is BezierSegment bs)
            {
                // Safety net: should already be flattened by GetFlattenedGeometry.
                FlattenCubicBezier(points, ctx, current, bs.Point1, bs.Point2, bs.Point3);
                current = bs.Point3;
            }
            else if (segment is PolyBezierSegment pbs)
            {
                var bpts = pbs.Points;
                for (int pi = 0; pi + 2 < bpts.Count; pi += 3)
                {
                    FlattenCubicBezier(points, ctx, current, bpts[pi], bpts[pi + 1], bpts[pi + 2]);
                    current = bpts[pi + 2];
                }
            }
            else if (segment is QuadraticBezierSegment qs)
            {
                FlattenQuadBezier(points, ctx, current, qs.Point1, qs.Point2);
                current = qs.Point2;
            }
            else if (segment is PolyQuadraticBezierSegment pqs)
            {
                var qpts = pqs.Points;
                for (int pi = 0; pi + 1 < qpts.Count; pi += 2)
                {
                    FlattenQuadBezier(points, ctx, current, qpts[pi], qpts[pi + 1]);
                    current = qpts[pi + 1];
                }
            }
            else if (segment is ArcSegment arc)
            {
                // Arcs should be flattened by GetFlattenedGeometry before reaching here.
                points.Add(ctx.TransformPoint(arc.Point));
                current = arc.Point;
            }
        }

        return points;
    }

    private static void FlattenCubicBezier(List<(float X, float Y)> points, in SoftwareRenderContext ctx,
        Point p0, Point p1, Point p2, Point p3, int depth = 0)
    {
        const int maxDepth = 10;
        const double tolerance = 0.3;

        if (depth >= maxDepth)
        {
            points.Add(ctx.TransformPoint(p3));
            return;
        }

        double dx = p3.X - p0.X, dy = p3.Y - p0.Y;
        double d1 = Math.Abs((p1.X - p3.X) * dy - (p1.Y - p3.Y) * dx);
        double d2 = Math.Abs((p2.X - p3.X) * dy - (p2.Y - p3.Y) * dx);
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6 || (d1 + d2) / len <= tolerance)
        {
            points.Add(ctx.TransformPoint(p3));
            return;
        }

        var m01 = Mid(p0, p1); var m12 = Mid(p1, p2); var m23 = Mid(p2, p3);
        var m012 = Mid(m01, m12); var m123 = Mid(m12, m23);
        var mid = Mid(m012, m123);

        FlattenCubicBezier(points, ctx, p0, m01, m012, mid, depth + 1);
        FlattenCubicBezier(points, ctx, mid, m123, m23, p3, depth + 1);
    }

    private static void FlattenQuadBezier(List<(float X, float Y)> points, in SoftwareRenderContext ctx,
        Point p0, Point p1, Point p2, int depth = 0)
    {
        const int maxDepth = 10;
        const double tolerance = 0.3;

        if (depth >= maxDepth)
        {
            points.Add(ctx.TransformPoint(p2));
            return;
        }

        double dx = p2.X - p0.X, dy = p2.Y - p0.Y;
        double d = Math.Abs((p1.X - p2.X) * dy - (p1.Y - p2.Y) * dx);
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6 || d / len <= tolerance)
        {
            points.Add(ctx.TransformPoint(p2));
            return;
        }

        var m01 = Mid(p0, p1); var m12 = Mid(p1, p2);
        var mid = Mid(m01, m12);

        FlattenQuadBezier(points, ctx, p0, m01, mid, depth + 1);
        FlattenQuadBezier(points, ctx, mid, m12, p2, depth + 1);
    }

    private static Point Mid(Point a, Point b) =>
        new Point((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

    private static void BlendPixel(byte[] pixels, int stride, int x, int y, byte b, byte g, byte r, byte a)
    {
        var offset = y * stride + x * 4;
        if (offset < 0 || offset + 3 >= pixels.Length) return;

        if (a == 255)
        {
            // Opaque: overwrite
            pixels[offset] = b;
            pixels[offset + 1] = g;
            pixels[offset + 2] = r;
            pixels[offset + 3] = a;
        }
        else
        {
            // Straight-alpha source-over (Porter-Duff): outRGB = (src·srcA + dst·dstA·(1-srcA)) / outA.
            // The buffer holds STRAIGHT (non-premultiplied) color; the D3D12 upload premultiplies it
            // later. The old formula omitted the dst·dstA weight and the ÷outA un-premultiply, so a
            // partial-alpha pixel (every AA edge / translucent fill) was left half-premultiplied and
            // darkened a second time downstream (~2× too dark). When dst is opaque (dstA=1) this
            // reduces exactly to the old formula, so opaque-on-opaque content is byte-identical.
            float srcA = a / 255f;
            float dstA = pixels[offset + 3] / 255f;
            float outA = srcA + dstA * (1f - srcA);
            if (outA <= 0f)
            {
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = pixels[offset + 3] = 0;
                return;
            }
            float dstW = dstA * (1f - srcA);
            float invOutA = 1f / outA;
            pixels[offset] = (byte)Math.Clamp((b * srcA + pixels[offset] * dstW) * invOutA + 0.5f, 0f, 255f);
            pixels[offset + 1] = (byte)Math.Clamp((g * srcA + pixels[offset + 1] * dstW) * invOutA + 0.5f, 0f, 255f);
            pixels[offset + 2] = (byte)Math.Clamp((r * srcA + pixels[offset + 2] * dstW) * invOutA + 0.5f, 0f, 255f);
            pixels[offset + 3] = (byte)(outA * 255f + 0.5f);
        }
    }

    #endregion

    #region Render Context

    private readonly struct SoftwareRenderContext
    {
        public readonly byte[] Pixels;
        public readonly int Width;
        public readonly int Height;
        public readonly int Stride;
        public readonly double Opacity;

        /// <summary>Source → device affine transform (framework row-vector convention).</summary>
        public readonly Matrix Matrix;

        /// <summary>Effective per-axis device scale (row norms = transformed X/Y basis-vector
        /// lengths), for curve-flatten tolerance and brush sampling.</summary>
        public readonly double ScaleX;
        public readonly double ScaleY;

        /// <summary>Per-pixel clip coverage (0..255, length Width*Height) intersected down
        /// the DrawingGroup tree from each group's ClipGeometry; null when unclipped.</summary>
        public readonly byte[]? ClipMask;

        /// <summary>
        /// Collects every <see cref="ImageSource"/> read while rendering, so the caller's raster
        /// cache knows which publications invalidate the buffer. Null when nobody is caching.
        /// </summary>
        /// <remarks>
        /// A reference shared by every derived context rather than a copied value: the whole
        /// drawing tree contributes to ONE set, which is what the cache is keyed against.
        /// </remarks>
        public readonly ICollection<ImageSource>? TouchedSources;

        public SoftwareRenderContext(byte[] pixels, int width, int height, int stride, Matrix matrix,
            ICollection<ImageSource>? touchedSources = null)
            : this(pixels, width, height, stride, matrix, 1.0, null, touchedSources)
        {
        }

        private SoftwareRenderContext(byte[] pixels, int width, int height, int stride, Matrix matrix,
            double opacity, byte[]? clipMask, ICollection<ImageSource>? touchedSources)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            Stride = stride;
            Matrix = matrix;
            Opacity = opacity;
            ClipMask = clipMask;
            TouchedSources = touchedSources;
            ScaleX = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
            ScaleY = Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22);
        }

        /// <summary>Composes a child transform (applied to points BEFORE the current one,
        /// matching DrawingGroup/Geometry nesting): combined = child * current (row-vector).</summary>
        public SoftwareRenderContext WithTransform(Matrix child)
            => new(Pixels, Width, Height, Stride, Matrix.Multiply(child, Matrix), Opacity, ClipMask,
                TouchedSources);

        public SoftwareRenderContext WithOpacity(double opacity)
            => new(Pixels, Width, Height, Stride, Matrix, opacity, ClipMask, TouchedSources);

        public SoftwareRenderContext WithClipMask(byte[] clipMask)
            => new(Pixels, Width, Height, Stride, Matrix, Opacity, clipMask, TouchedSources);

        public (float X, float Y) TransformPoint(Point p)
        {
            var t = Matrix.Transform(p);
            return ((float)t.X, (float)t.Y);
        }
    }

    #endregion
}
