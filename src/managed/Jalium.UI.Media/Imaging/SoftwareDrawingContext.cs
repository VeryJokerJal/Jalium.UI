using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// CPU rasterizer behind <see cref="RenderTargetBitmap.Render(Visual)"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the offscreen twin of <c>Jalium.UI.Interop.RenderTargetDrawingContext</c> (the live
/// GPU-backed context) and it must speak the same ambient-state protocol, because
/// <c>Visual.RenderChildVisualInline</c> only applies a child's arrange offset, opacity and
/// render transform when the context advertises them through <see cref="IOffsetDrawingContext"/> /
/// <see cref="IOpacityDrawingContext"/> / <see cref="ITransformDrawingContext"/>. Implementing
/// none of them — the previous state of this class — silently fell through to the bare
/// <c>child.Render(dc)</c> tail of that method, so every descendant painted at its own local
/// origin: template parts with <c>VerticalAlignment="Center"</c> landed at the top of the image
/// and a <c>Margin</c>-positioned thumb landed at x=0.
/// </para>
/// <para>
/// Coordinate protocol (identical to the live context): draw calls take LOCAL coordinates, the
/// context adds <see cref="Offset"/>, and the accumulated matrix maps that offset space to device
/// pixels. <see cref="PushTransform(Transform)"/> folds pure translations into <see cref="Offset"/>
/// and conjugates anything else as <c>T(-Offset) * M * T(+Offset)</c> so it applies about the
/// element's own origin.
/// </para>
/// <para>
/// Shapes rasterize from a signed distance function evaluated at pixel centres mapped back through
/// the inverse matrix, which gives antialiased edges and keeps rotation / scale correct without a
/// separate transformed-geometry path. Compositing is straight-alpha source-over into the target's
/// BGRA buffer — the old code wrote brush bytes in verbatim, which turned <c>#28FFFFFF</c> and
/// <c>Transparent</c> into opaque white.
/// </para>
/// <para>
/// Known limitations, all deliberate: text and glyph runs need the native glyph rasterizer and are
/// skipped; <see cref="DrawImage"/> and backdrop effects are skipped; tile brushes (image / drawing
/// / visual) paint nothing; a square clip degrades to its device-space bounding box under a
/// rotation, which is what the GPU scissor path does too.
/// </para>
/// </remarks>
internal sealed class SoftwareDrawingContext : DrawingContextAdapter,
    IOffsetDrawingContext, IClipDrawingContext, IOpacityDrawingContext, ITransformDrawingContext
{
    private enum StateKind
    {
        Clip,
        Opacity,
        Offset,
        Transform,
    }

    private readonly record struct StateEntry(
        StateKind Kind,
        Point SavedOffset,
        double SavedOpacity,
        Rect SavedClipBounds,
        int SavedClipCount);

    private enum ShapeKind
    {
        RoundedRect,
        Ellipse,
        Segment,
    }

    /// <summary>A signed-distance shape expressed in offset space (local + <see cref="Offset"/>).</summary>
    private readonly struct ShapeDesc
    {
        public ShapeDesc(Rect rect, double topLeft, double topRight, double bottomRight, double bottomLeft)
        {
            Kind = ShapeKind.RoundedRect;
            Rect = rect;
            var limit = Math.Max(0, Math.Min(rect.Width, rect.Height) / 2.0);
            TopLeft = Math.Clamp(topLeft, 0, limit);
            TopRight = Math.Clamp(topRight, 0, limit);
            BottomRight = Math.Clamp(bottomRight, 0, limit);
            BottomLeft = Math.Clamp(bottomLeft, 0, limit);
            P0 = default;
            P1 = default;
        }

        private ShapeDesc(ShapeKind kind, Rect rect, Point p0, Point p1)
        {
            Kind = kind;
            Rect = rect;
            TopLeft = TopRight = BottomRight = BottomLeft = 0;
            P0 = p0;
            P1 = p1;
        }

        public ShapeKind Kind { get; }
        public Rect Rect { get; }
        public double TopLeft { get; }
        public double TopRight { get; }
        public double BottomRight { get; }
        public double BottomLeft { get; }
        public Point P0 { get; }
        public Point P1 { get; }

        public static ShapeDesc FromEllipse(Point center, double radiusX, double radiusY) =>
            new(ShapeKind.Ellipse,
                new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2),
                center,
                default);

        public static ShapeDesc FromSegment(Point p0, Point p1) =>
            new(ShapeKind.Segment,
                new Rect(
                    Math.Min(p0.X, p1.X),
                    Math.Min(p0.Y, p1.Y),
                    Math.Abs(p1.X - p0.X),
                    Math.Abs(p1.Y - p0.Y)),
                p0,
                p1);

        /// <summary>Signed distance from the shape boundary; negative inside.</summary>
        public double SignedDistance(double x, double y)
        {
            switch (Kind)
            {
                case ShapeKind.Ellipse:
                {
                    var radiusX = Rect.Width / 2.0;
                    var radiusY = Rect.Height / 2.0;
                    if (radiusX <= 0 || radiusY <= 0) return double.MaxValue;

                    var qx = x - P0.X;
                    var qy = y - P0.Y;
                    var normalized = Math.Sqrt(((qx * qx) / (radiusX * radiusX)) + ((qy * qy) / (radiusY * radiusY)));
                    var gradientX = qx / (radiusX * radiusX);
                    var gradientY = qy / (radiusY * radiusY);
                    var gradient = Math.Sqrt((gradientX * gradientX) + (gradientY * gradientY));
                    return gradient > 1e-12
                        ? (normalized * (normalized - 1.0)) / gradient
                        : -Math.Min(radiusX, radiusY);
                }

                case ShapeKind.Segment:
                {
                    var dx = P1.X - P0.X;
                    var dy = P1.Y - P0.Y;
                    var lengthSquared = (dx * dx) + (dy * dy);
                    var px = x - P0.X;
                    var py = y - P0.Y;
                    var t = lengthSquared > 1e-12
                        ? Math.Clamp(((px * dx) + (py * dy)) / lengthSquared, 0.0, 1.0)
                        : 0.0;
                    var ox = px - (t * dx);
                    var oy = py - (t * dy);
                    return Math.Sqrt((ox * ox) + (oy * oy));
                }

                default:
                {
                    var halfWidth = Rect.Width / 2.0;
                    var halfHeight = Rect.Height / 2.0;
                    if (halfWidth <= 0 || halfHeight <= 0) return double.MaxValue;

                    var qx = x - (Rect.X + halfWidth);
                    var qy = y - (Rect.Y + halfHeight);
                    var radius = qx > 0
                        ? (qy > 0 ? BottomRight : TopRight)
                        : (qy > 0 ? BottomLeft : TopLeft);

                    var dx = Math.Abs(qx) - halfWidth + radius;
                    var dy = Math.Abs(qy) - halfHeight + radius;
                    var outsideX = Math.Max(dx, 0.0);
                    var outsideY = Math.Max(dy, 0.0);
                    return Math.Sqrt((outsideX * outsideX) + (outsideY * outsideY))
                           + Math.Min(Math.Max(dx, dy), 0.0)
                           - radius;
                }
            }
        }
    }

    /// <summary>A rounded clip kept in the offset space it was pushed in.</summary>
    private readonly record struct ClipEntry(ShapeDesc Shape, Matrix Inverse, double DeviceScale);

    private readonly RenderTargetBitmap _target;
    private readonly Stack<StateEntry> _stateStack = new();
    private readonly List<ClipEntry> _clips = new();
    private readonly Stack<Matrix> _matrixStack = new();

    private Matrix _matrix = Matrix.Identity;
    private Rect _clipBounds;
    private double _opacity = 1.0;
    private bool _closed;

    public SoftwareDrawingContext(RenderTargetBitmap target)
    {
        _target = target;
        _clipBounds = new Rect(0, 0, target.PixelWidth, target.PixelHeight);
    }

    /// <inheritdoc />
    public Point Offset { get; set; }

    #region Shape primitives

    /// <inheritdoc />
    public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) =>
        DrawRoundedRectangle(brush, pen, rect, 0, 0);

    /// <inheritdoc />
    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double radiusX, double radiusY)
    {
        if (_closed || rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return;

        var radius = ResolveCornerRadius(radiusX, radiusY);
        var shape = new ShapeDesc(Translate(rect), radius, radius, radius, radius);
        FillShape(shape, brush, 0);
        StrokeShape(shape, pen);
    }

    /// <inheritdoc />
    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
    {
        if (_closed || radiusX <= 0 || radiusY <= 0) return;

        var shape = ShapeDesc.FromEllipse(
            new Point(center.X + Offset.X, center.Y + Offset.Y),
            radiusX,
            radiusY);
        FillShape(shape, brush, 0);
        StrokeShape(shape, pen);
    }

    /// <inheritdoc />
    public override void DrawLine(Pen pen, Point point0, Point point1)
    {
        if (_closed || pen?.Brush == null || pen.Thickness <= 0) return;

        var shape = ShapeDesc.FromSegment(
            new Point(point0.X + Offset.X, point0.Y + Offset.Y),
            new Point(point1.X + Offset.X, point1.Y + Offset.Y));
        FillShape(shape, pen.Brush, pen.Thickness / 2.0);
    }

    /// <inheritdoc />
    public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
    {
        if (_closed || geometry == null) return;

        // Analytic primitives keep their SDF: it antialiases better than a flattened
        // polygon and covers the overwhelmingly common case (layout clips, Border
        // decoration, Shape parts).
        var geometryTransform = geometry.Transform;
        if (geometryTransform == null || geometryTransform.Value.IsIdentity)
        {
            switch (geometry)
            {
                case RectangleGeometry { HasPerCornerRadii: true } perCorner:
                {
                    var corners = perCorner.CornerRadius;
                    var rounded = new ShapeDesc(
                        Translate(perCorner.Rect),
                        corners.TopLeft, corners.TopRight, corners.BottomRight, corners.BottomLeft);
                    FillShape(rounded, brush, 0);
                    StrokeShape(rounded, pen);
                    return;
                }

                case RectangleGeometry rectangleGeometry:
                    DrawRoundedRectangle(brush, pen, rectangleGeometry.Rect,
                        rectangleGeometry.RadiusX, rectangleGeometry.RadiusY);
                    return;

                case EllipseGeometry ellipseGeometry:
                    DrawEllipse(brush, pen, ellipseGeometry.Center,
                        ellipseGeometry.RadiusX, ellipseGeometry.RadiusY);
                    return;

                case LineGeometry lineGeometry:
                    if (pen != null) DrawLine(pen, lineGeometry.StartPoint, lineGeometry.EndPoint);
                    return;
            }
        }

        var flattened = geometry.GetFlattenedPathGeometry();
        var figures = BuildFigurePolylines(flattened, geometryTransform);
        if (figures.Count == 0) return;

        if (brush != null)
        {
            // Filling always treats a figure as closed, exactly like WPF; the scanline
            // walks adjacent point pairs, so the closing edge must be an explicit point.
            var fillPolygons = new List<List<Point>>(figures.Count);
            foreach (var figure in figures)
            {
                var points = figure.Points;
                if (points.Count < 3) continue;

                var polygon = new List<Point>(points.Count + 1);
                polygon.AddRange(points);
                if (polygon[0] != polygon[^1]) polygon.Add(polygon[0]);
                fillPolygons.Add(polygon);
            }

            if (fillPolygons.Count > 0)
            {
                FillPolygons(fillPolygons, flattened.FillRule, brush, geometry.Bounds);
            }
        }

        if (pen?.Brush != null && pen.Thickness > 0)
        {
            // No joins or caps: every flattened edge is stroked as its own capsule. At the
            // hairline widths icons actually use, the difference is invisible. Unlike the
            // fill above, the stroke must respect the figure's own topology: a two-point
            // line segment is a legitimate stroke (lucide icons are full of them), and an
            // OPEN figure must not grow a phantom closing edge back to its start point.
            foreach (var figure in figures)
            {
                var points = figure.Points;
                if (points.Count < 2) continue;

                for (var i = 0; i + 1 < points.Count; i++)
                {
                    FillShape(ShapeDesc.FromSegment(points[i], points[i + 1]), pen.Brush, pen.Thickness / 2.0);
                }

                if (figure.IsClosed && points.Count >= 3 && points[0] != points[^1])
                {
                    FillShape(ShapeDesc.FromSegment(points[^1], points[0]), pen.Brush, pen.Thickness / 2.0);
                }
            }
        }
    }

    /// <summary>
    /// Collapses an elliptical corner onto the isotropic SDF radius, matching
    /// <see cref="RectangleGeometry"/>'s own flattening rule: a corner is square only when
    /// BOTH radii are zero, otherwise the set radius wins.
    /// </summary>
    private static double ResolveCornerRadius(double radiusX, double radiusY)
    {
        var rx = Math.Abs(radiusX);
        var ry = Math.Abs(radiusY);
        if (rx <= 0) return ry;
        if (ry <= 0) return rx;
        return Math.Min(rx, ry);
    }

    #endregion

    #region Unsupported primitives

    /// <inheritdoc />
    /// <remarks>
    /// Glyph rasterization lives in the native text engine and there is no managed outline
    /// source: <c>FormattedText.BuildGeometry</c> returns highlight rectangles, so routing text
    /// through <see cref="DrawGeometry"/> would paint solid blocks instead of letters.
    /// </remarks>
    public override void DrawText(FormattedText formattedText, Point origin)
    {
    }

    /// <inheritdoc />
    /// <remarks>See <see cref="DrawText"/>: <c>GlyphRun.BuildGeometry</c> is also rectangles.</remarks>
    public override void DrawGlyphRun(Brush? foregroundBrush, GlyphRun glyphRun)
    {
    }

    /// <inheritdoc />
    /// <remarks>Bitmap compositing is not implemented; image content is skipped.</remarks>
    public override void DrawImage(ImageSource imageSource, Rect rect)
    {
    }

    /// <inheritdoc />
    /// <remarks>Backdrop materials sample the composited window and have no offscreen meaning.</remarks>
    public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
    {
    }

    #endregion

    #region Ambient state

    /// <inheritdoc />
    public override void PushClip(Geometry clipGeometry)
    {
        if (_closed || clipGeometry == null) return;

        var bounds = clipGeometry.Bounds;
        var topLeft = 0.0;
        var topRight = 0.0;
        var bottomRight = 0.0;
        var bottomLeft = 0.0;

        if (clipGeometry is RectangleGeometry rectangleGeometry)
        {
            bounds = rectangleGeometry.Rect;
            if (rectangleGeometry.HasPerCornerRadii)
            {
                var corners = rectangleGeometry.CornerRadius;
                topLeft = corners.TopLeft;
                topRight = corners.TopRight;
                bottomRight = corners.BottomRight;
                bottomLeft = corners.BottomLeft;
            }
            else
            {
                topLeft = topRight = bottomRight = bottomLeft =
                    ResolveCornerRadius(rectangleGeometry.RadiusX, rectangleGeometry.RadiusY);
            }
        }

        PushClipCore(bounds, topLeft, topRight, bottomRight, bottomLeft);
    }

    /// <summary>Pushes a uniform rounded-rect clip.</summary>
    public void PushRoundedRectClip(Rect bounds, CornerRadius cornerRadius) =>
        PushPerCornerRoundedRectClip(bounds, cornerRadius);

    /// <summary>Pushes a rounded-rect clip with independent corner radii.</summary>
    public void PushPerCornerRoundedRectClip(Rect bounds, CornerRadius cornerRadius) =>
        PushClipCore(bounds, cornerRadius.TopLeft, cornerRadius.TopRight,
            cornerRadius.BottomRight, cornerRadius.BottomLeft);

    private void PushClipCore(Rect bounds, double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        _stateStack.Push(new StateEntry(StateKind.Clip, Offset, _opacity, _clipBounds, _clips.Count));

        if (bounds.IsEmpty) return;

        var offsetBounds = Translate(bounds);
        _clipBounds = Rect.Intersect(_clipBounds, TransformBounds(offsetBounds));

        // A square clip is fully described by its device bounds. Only rounded corners need a
        // per-pixel coverage term — and only they are safe to evaluate, because a layout clip
        // with open edges carries ±1e9 extents that would wreck the SDF's precision.
        if ((topLeft > 0 || topRight > 0 || bottomRight > 0 || bottomLeft > 0) &&
            _matrix.TryInvert(out var inverse))
        {
            _clips.Add(new ClipEntry(
                new ShapeDesc(offsetBounds, topLeft, topRight, bottomRight, bottomLeft),
                inverse,
                DeviceScale));
        }
    }

    /// <inheritdoc />
    public override void PushOpacity(double opacity)
    {
        if (_closed) return;

        _stateStack.Push(new StateEntry(StateKind.Opacity, Offset, _opacity, _clipBounds, _clips.Count));
        _opacity *= Math.Clamp(opacity, 0.0, 1.0);
    }

    /// <inheritdoc />
    public void PopOpacity() => Pop();

    /// <inheritdoc />
    public override void PushTransform(Transform transform)
    {
        if (_closed || transform == null) return;

        if (transform is TranslateTransform translate)
        {
            _stateStack.Push(new StateEntry(StateKind.Offset, Offset, _opacity, _clipBounds, _clips.Count));
            Offset = new Point(Offset.X + translate.X, Offset.Y + translate.Y);
            return;
        }

        // Draw coordinates already carry Offset, so the transform is conjugated to apply about
        // the element's own origin: T(-Offset) * M * T(+Offset).
        var m = transform.Value;
        var ox = Offset.X;
        var oy = Offset.Y;
        var incoming = new Matrix(
            m.M11, m.M12, m.M21, m.M22,
            (-ox * m.M11) + (-oy * m.M21) + m.OffsetX + ox,
            (-ox * m.M12) + (-oy * m.M22) + m.OffsetY + oy);

        _stateStack.Push(new StateEntry(StateKind.Transform, Offset, _opacity, _clipBounds, _clips.Count));
        _matrixStack.Push(_matrix);

        // Inner-first, outer-last: a nested push pre-multiplies rather than appends.
        _matrix = incoming * _matrix;
    }

    /// <inheritdoc />
    void ITransformDrawingContext.PushTransform(Transform transform, double originX, double originY)
    {
        if (originX == 0 && originY == 0)
        {
            PushTransform(transform);
            return;
        }

        var combined = Matrix.Multiply(
            Matrix.Multiply(new Matrix(1, 0, 0, 1, -originX, -originY), transform.Value),
            new Matrix(1, 0, 0, 1, originX, originY));
        PushTransform(new MatrixTransform(combined));
    }

    /// <inheritdoc />
    public void PopTransform() => Pop();

    /// <inheritdoc />
    public override void Pop()
    {
        if (_closed || _stateStack.Count == 0) return;

        var state = _stateStack.Pop();
        switch (state.Kind)
        {
            case StateKind.Clip:
                _clipBounds = state.SavedClipBounds;
                if (_clips.Count > state.SavedClipCount)
                {
                    _clips.RemoveRange(state.SavedClipCount, _clips.Count - state.SavedClipCount);
                }
                break;

            case StateKind.Opacity:
                _opacity = state.SavedOpacity;
                break;

            case StateKind.Offset:
                Offset = state.SavedOffset;
                break;

            case StateKind.Transform:
                if (_matrixStack.Count > 0) _matrix = _matrixStack.Pop();
                Offset = state.SavedOffset;
                break;
        }
    }

    /// <inheritdoc />
    public override void Close() => _closed = true;

    #endregion

    #region Rasterization

    /// <summary>Local-to-device length scale, keeping the antialiasing band one device pixel wide.</summary>
    private double DeviceScale
    {
        get
        {
            var determinant = Math.Abs(_matrix.Determinant);
            return determinant > 1e-12 ? Math.Sqrt(determinant) : 1.0;
        }
    }

    private Rect Translate(Rect rect) => new(rect.X + Offset.X, rect.Y + Offset.Y, rect.Width, rect.Height);

    private Rect TransformBounds(Rect rect)
    {
        if (_matrix.IsIdentity || rect.IsEmpty) return rect;

        var p0 = _matrix.Transform(new Point(rect.X, rect.Y));
        var p1 = _matrix.Transform(new Point(rect.Right, rect.Y));
        var p2 = _matrix.Transform(new Point(rect.Right, rect.Bottom));
        var p3 = _matrix.Transform(new Point(rect.X, rect.Bottom));

        var left = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        var top = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        var right = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        var bottom = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private void StrokeShape(in ShapeDesc shape, Pen? pen)
    {
        if (pen?.Brush == null || pen.Thickness <= 0) return;
        FillShape(shape, pen.Brush, pen.Thickness / 2.0);
    }

    /// <summary>
    /// Rasterizes <paramref name="shape"/>. A positive <paramref name="strokeHalfWidth"/> turns the
    /// fill into a stroke centred on the shape boundary (WPF pen semantics).
    /// </summary>
    private void FillShape(in ShapeDesc shape, Brush? brush, double strokeHalfWidth)
    {
        if (brush == null || _opacity <= 0) return;

        var sampler = BrushSampler.Create(brush, shape.Rect, Offset, _opacity);
        if (sampler == null) return;
        if (!_matrix.TryInvert(out var inverse)) return;

        var scale = DeviceScale;

        // Grow by the antialiasing band (and the stroke half width) before mapping to device space.
        var margin = strokeHalfWidth + (0.5 / Math.Max(scale, 1e-6));
        var localBounds = new Rect(
            shape.Rect.X - margin,
            shape.Rect.Y - margin,
            shape.Rect.Width + (margin * 2),
            shape.Rect.Height + (margin * 2));

        var device = Rect.Intersect(TransformBounds(localBounds), _clipBounds);
        if (device.IsEmpty) return;

        var x0 = Math.Max(0, (int)Math.Floor(device.X));
        var y0 = Math.Max(0, (int)Math.Floor(device.Y));
        var x1 = Math.Min(_target.PixelWidth, (int)Math.Ceiling(device.Right));
        var y1 = Math.Min(_target.PixelHeight, (int)Math.Ceiling(device.Bottom));

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var local = inverse.Transform(new Point(x + 0.5, y + 0.5));
                var distance = shape.SignedDistance(local.X, local.Y);
                if (strokeHalfWidth > 0) distance = Math.Abs(distance) - strokeHalfWidth;

                var coverage = Math.Clamp(0.5 - (distance * scale), 0.0, 1.0);
                if (coverage <= 0) continue;

                coverage *= ClipCoverage(x, y);
                if (coverage <= 0) continue;

                var sample = sampler.Sample(local.X, local.Y);
                BlendPixel(x, y, sample.R, sample.G, sample.B, sample.A * coverage);
            }
        }
    }

    /// <summary>
    /// Scanline-fills flattened figures with 4x vertical supersampling and analytic horizontal
    /// coverage. Input polygons are in offset space; they are mapped to device space here.
    /// </summary>
    private void FillPolygons(List<List<Point>> polygons, FillRule fillRule, Brush brush, Rect brushBounds)
    {
        if (_opacity <= 0) return;

        var sampler = BrushSampler.Create(brush, Translate(brushBounds), Offset, _opacity);
        if (sampler == null) return;
        if (!_matrix.TryInvert(out var inverse)) return;

        var deviceBounds = Rect.Empty;
        var devicePolygons = new List<List<Point>>(polygons.Count);
        foreach (var polygon in polygons)
        {
            var mapped = new List<Point>(polygon.Count);
            foreach (var point in polygon)
            {
                var device = _matrix.Transform(point);
                mapped.Add(device);
                deviceBounds = Rect.Union(deviceBounds, device);
            }

            devicePolygons.Add(mapped);
        }

        deviceBounds = Rect.Intersect(deviceBounds, _clipBounds);
        if (deviceBounds.IsEmpty) return;

        var x0 = Math.Max(0, (int)Math.Floor(deviceBounds.X));
        var y0 = Math.Max(0, (int)Math.Floor(deviceBounds.Y));
        var x1 = Math.Min(_target.PixelWidth, (int)Math.Ceiling(deviceBounds.Right));
        var y1 = Math.Min(_target.PixelHeight, (int)Math.Ceiling(deviceBounds.Bottom));
        if (x1 <= x0 || y1 <= y0) return;

        const int SubSamples = 4;
        const double SubWeight = 1.0 / SubSamples;

        var coverage = new double[x1 - x0];
        var crossings = new List<(double X, int Direction)>();

        for (var y = y0; y < y1; y++)
        {
            Array.Clear(coverage);

            for (var sub = 0; sub < SubSamples; sub++)
            {
                var sampleY = y + ((sub + 0.5) * SubWeight);
                crossings.Clear();

                foreach (var polygon in devicePolygons)
                {
                    for (var i = 0; i + 1 < polygon.Count; i++)
                    {
                        var a = polygon[i];
                        var b = polygon[i + 1];
                        if (a.Y == b.Y) continue;
                        if (sampleY < Math.Min(a.Y, b.Y) || sampleY >= Math.Max(a.Y, b.Y)) continue;

                        var t = (sampleY - a.Y) / (b.Y - a.Y);
                        crossings.Add((a.X + (t * (b.X - a.X)), b.Y > a.Y ? 1 : -1));
                    }
                }

                if (crossings.Count < 2) continue;
                crossings.Sort(static (left, right) => left.X.CompareTo(right.X));

                var winding = 0;
                for (var i = 0; i + 1 < crossings.Count; i++)
                {
                    winding += fillRule == FillRule.EvenOdd ? 1 : crossings[i].Direction;
                    var inside = fillRule == FillRule.EvenOdd ? (winding % 2) != 0 : winding != 0;
                    if (!inside) continue;

                    AccumulateSpan(coverage, x0, x1, crossings[i].X, crossings[i + 1].X, SubWeight);
                }
            }

            for (var x = x0; x < x1; x++)
            {
                var value = coverage[x - x0];
                if (value <= 0.0005) continue;

                value = Math.Min(1.0, value) * ClipCoverage(x, y);
                if (value <= 0) continue;

                var local = inverse.Transform(new Point(x + 0.5, y + 0.5));
                var sample = sampler.Sample(local.X, local.Y);
                BlendPixel(x, y, sample.R, sample.G, sample.B, sample.A * value);
            }
        }
    }

    private static void AccumulateSpan(double[] coverage, int x0, int x1, double startX, double endX, double weight)
    {
        var start = Math.Max(startX, x0);
        var end = Math.Min(endX, x1);
        if (end <= start) return;

        var first = (int)Math.Floor(start);
        var last = (int)Math.Floor(end);

        if (first == last)
        {
            coverage[first - x0] += (end - start) * weight;
            return;
        }

        coverage[first - x0] += (first + 1 - start) * weight;
        for (var i = first + 1; i < last; i++)
        {
            coverage[i - x0] += weight;
        }

        if (last < x1)
        {
            coverage[last - x0] += (end - last) * weight;
        }
    }

    /// <summary>Flattens a path geometry into closed offset-space polylines.</summary>
    private readonly record struct FigurePolyline(List<Point> Points, bool IsClosed);

    /// <summary>
    /// Flattens each figure into its raw open polyline plus its closed flag. Callers apply
    /// their own topology: fill closes every ≥3-point figure, stroke keeps two-point line
    /// segments and only closes figures that are actually marked closed.
    /// </summary>
    private List<FigurePolyline> BuildFigurePolylines(PathGeometry geometry, Transform? geometryTransform)
    {
        var result = new List<FigurePolyline>();
        var extra = geometryTransform?.Value;

        foreach (var figure in geometry.Figures)
        {
            if (figure.Segments.Count == 0) continue;

            var polyline = new List<Point> { MapToOffsetSpace(figure.StartPoint, extra) };
            foreach (var segment in figure.Segments)
            {
                foreach (var point in segment.GetPoints())
                {
                    polyline.Add(MapToOffsetSpace(point, extra));
                }
            }

            if (polyline.Count < 2) continue;
            result.Add(new FigurePolyline(polyline, figure.IsClosed));
        }

        return result;
    }

    private Point MapToOffsetSpace(Point point, Matrix? geometryTransform)
    {
        if (geometryTransform is { } m) point = m.Transform(point);
        return new Point(point.X + Offset.X, point.Y + Offset.Y);
    }

    private double ClipCoverage(int x, int y)
    {
        if (_clips.Count == 0) return 1.0;

        var coverage = 1.0;
        var device = new Point(x + 0.5, y + 0.5);
        foreach (var clip in _clips)
        {
            var local = clip.Inverse.Transform(device);
            coverage *= Math.Clamp(0.5 - (clip.Shape.SignedDistance(local.X, local.Y) * clip.DeviceScale), 0.0, 1.0);
            if (coverage <= 0) return 0.0;
        }

        return coverage;
    }

    /// <summary>Straight-alpha source-over compositing into the target's BGRA buffer.</summary>
    private void BlendPixel(int x, int y, double sourceR, double sourceG, double sourceB, double sourceA)
    {
        if (sourceA <= 0) return;
        sourceA = Math.Min(sourceA, 1.0);

        var buffer = _target.GetPixelBuffer();
        var offset = (y * _target.Stride) + (x * 4);

        var destinationA = buffer[offset + 3] / 255.0;
        var outAlpha = sourceA + (destinationA * (1.0 - sourceA));
        if (outAlpha <= 0)
        {
            buffer[offset] = 0;
            buffer[offset + 1] = 0;
            buffer[offset + 2] = 0;
            buffer[offset + 3] = 0;
            return;
        }

        var keep = destinationA * (1.0 - sourceA);
        buffer[offset] = ToByte(((buffer[offset] / 255.0 * keep) + (sourceB * sourceA)) / outAlpha);
        buffer[offset + 1] = ToByte(((buffer[offset + 1] / 255.0 * keep) + (sourceG * sourceA)) / outAlpha);
        buffer[offset + 2] = ToByte(((buffer[offset + 2] / 255.0 * keep) + (sourceR * sourceA)) / outAlpha);
        buffer[offset + 3] = ToByte(outAlpha);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);

    #endregion

    #region Brushes

    /// <summary>
    /// Evaluates a brush in offset space. Solid colours, linear gradients and radial gradients
    /// are supported; tile brushes (image / drawing / visual) return <see langword="null"/> and
    /// paint nothing rather than degenerating into a flat colour.
    /// </summary>
    private abstract class BrushSampler
    {
        protected BrushSampler(double opacity) => Opacity = opacity;

        protected double Opacity { get; }

        public abstract (double R, double G, double B, double A) Sample(double x, double y);

        public static BrushSampler? Create(Brush brush, Rect bounds, Point offset, double ambientOpacity)
        {
            var opacity = ambientOpacity * Math.Clamp(brush.Opacity, 0.0, 1.0);
            if (opacity <= 0) return null;

            switch (brush)
            {
                case SolidColorBrush solid:
                {
                    var color = solid.Color;
                    return color.A == 0 ? null : new SolidSampler(color, opacity);
                }

                case LinearGradientBrush linear:
                {
                    var stops = GradientRamp.Build(linear);
                    if (stops == null) return null;

                    var start = MapPoint(linear.StartPoint, linear.MappingMode, bounds, offset);
                    var end = MapPoint(linear.EndPoint, linear.MappingMode, bounds, offset);
                    return new LinearGradientSampler(stops, start, end, opacity);
                }

                case RadialGradientBrush radial:
                {
                    var stops = GradientRamp.Build(radial);
                    if (stops == null) return null;

                    var center = MapPoint(radial.Center, radial.MappingMode, bounds, offset);
                    var origin = MapPoint(radial.GradientOrigin, radial.MappingMode, bounds, offset);
                    var radiusX = radial.MappingMode == BrushMappingMode.RelativeToBoundingBox
                        ? radial.RadiusX * Math.Max(bounds.Width, 1e-6)
                        : radial.RadiusX;
                    var radiusY = radial.MappingMode == BrushMappingMode.RelativeToBoundingBox
                        ? radial.RadiusY * Math.Max(bounds.Height, 1e-6)
                        : radial.RadiusY;
                    return new RadialGradientSampler(stops, center, origin, radiusX, radiusY, opacity);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Relative points scale with the fill bounds (offset cancels out); absolute points are in
        /// the element's own local space, so the ambient offset has to be added back.
        /// </summary>
        private static Point MapPoint(Point point, BrushMappingMode mode, Rect bounds, Point offset) =>
            mode == BrushMappingMode.RelativeToBoundingBox
                ? new Point(bounds.X + (point.X * bounds.Width), bounds.Y + (point.Y * bounds.Height))
                : new Point(point.X + offset.X, point.Y + offset.Y);

        private sealed class SolidSampler : BrushSampler
        {
            private readonly double _r;
            private readonly double _g;
            private readonly double _b;
            private readonly double _a;

            public SolidSampler(Color color, double opacity)
                : base(opacity)
            {
                _r = color.R / 255.0;
                _g = color.G / 255.0;
                _b = color.B / 255.0;
                _a = color.A / 255.0 * opacity;
            }

            public override (double R, double G, double B, double A) Sample(double x, double y) =>
                (_r, _g, _b, _a);
        }

        private sealed class LinearGradientSampler : BrushSampler
        {
            private readonly GradientRamp _ramp;
            private readonly Point _start;
            private readonly double _dx;
            private readonly double _dy;
            private readonly double _lengthSquared;

            public LinearGradientSampler(GradientRamp ramp, Point start, Point end, double opacity)
                : base(opacity)
            {
                _ramp = ramp;
                _start = start;
                _dx = end.X - start.X;
                _dy = end.Y - start.Y;
                _lengthSquared = (_dx * _dx) + (_dy * _dy);
            }

            public override (double R, double G, double B, double A) Sample(double x, double y)
            {
                var t = _lengthSquared > 1e-12
                    ? (((x - _start.X) * _dx) + ((y - _start.Y) * _dy)) / _lengthSquared
                    : 0.0;
                return _ramp.Evaluate(t, Opacity);
            }
        }

        private sealed class RadialGradientSampler : BrushSampler
        {
            private readonly GradientRamp _ramp;
            private readonly Point _center;
            private readonly double _radiusX;
            private readonly double _radiusY;
            private readonly double _focusX;
            private readonly double _focusY;

            public RadialGradientSampler(
                GradientRamp ramp, Point center, Point origin, double radiusX, double radiusY, double opacity)
                : base(opacity)
            {
                _ramp = ramp;
                _center = center;
                _radiusX = Math.Abs(radiusX);
                _radiusY = Math.Abs(radiusY);

                // Work in the unit circle the ellipse maps to, and keep the focus strictly inside
                // it so the ray/circle solve always has a real root.
                var fx = _radiusX > 1e-12 ? (origin.X - center.X) / _radiusX : 0.0;
                var fy = _radiusY > 1e-12 ? (origin.Y - center.Y) / _radiusY : 0.0;
                var length = Math.Sqrt((fx * fx) + (fy * fy));
                if (length > 0.99)
                {
                    fx *= 0.99 / length;
                    fy *= 0.99 / length;
                }

                _focusX = fx;
                _focusY = fy;
            }

            public override (double R, double G, double B, double A) Sample(double x, double y)
            {
                if (_radiusX <= 1e-12 || _radiusY <= 1e-12) return _ramp.Evaluate(1.0, Opacity);

                var px = (x - _center.X) / _radiusX;
                var py = (y - _center.Y) / _radiusY;
                var dx = px - _focusX;
                var dy = py - _focusY;

                // Solve |focus + s*d| = 1; t = 1/s puts the boundary at 1.
                var a = (dx * dx) + (dy * dy);
                if (a <= 1e-12) return _ramp.Evaluate(0.0, Opacity);

                var b = 2.0 * ((_focusX * dx) + (_focusY * dy));
                var c = (_focusX * _focusX) + (_focusY * _focusY) - 1.0;
                var discriminant = Math.Max((b * b) - (4.0 * a * c), 0.0);
                var s = (-b + Math.Sqrt(discriminant)) / (2.0 * a);
                return _ramp.Evaluate(s > 1e-12 ? 1.0 / s : 0.0, Opacity);
            }
        }
    }

    /// <summary>A gradient's stops, sorted once and evaluated per pixel.</summary>
    private sealed class GradientRamp
    {
        private readonly double[] _offsets;
        private readonly Color[] _colors;
        private readonly GradientSpreadMethod _spread;

        private GradientRamp(double[] offsets, Color[] colors, GradientSpreadMethod spread)
        {
            _offsets = offsets;
            _colors = colors;
            _spread = spread;
        }

        public static GradientRamp? Build(GradientBrush brush)
        {
            var stops = brush.GradientStops;
            if (stops == null || stops.Count == 0) return null;

            var sorted = stops.OrderBy(static stop => stop.Offset).ToArray();
            var offsets = new double[sorted.Length];
            var colors = new Color[sorted.Length];
            for (var i = 0; i < sorted.Length; i++)
            {
                offsets[i] = sorted[i].Offset;
                colors[i] = sorted[i].Color;
            }

            return new GradientRamp(offsets, colors, brush.SpreadMethod);
        }

        public (double R, double G, double B, double A) Evaluate(double t, double opacity)
        {
            t = _spread switch
            {
                GradientSpreadMethod.Repeat => t - Math.Floor(t),
                GradientSpreadMethod.Reflect => ReflectT(t),
                _ => Math.Clamp(t, 0.0, 1.0),
            };

            if (t <= _offsets[0]) return Pack(_colors[0], opacity);
            if (t >= _offsets[^1]) return Pack(_colors[^1], opacity);

            for (var i = 1; i < _offsets.Length; i++)
            {
                if (t > _offsets[i]) continue;

                var span = _offsets[i] - _offsets[i - 1];
                var local = span > 1e-12 ? (t - _offsets[i - 1]) / span : 0.0;
                var from = _colors[i - 1];
                var to = _colors[i];
                return (
                    Lerp(from.R, to.R, local),
                    Lerp(from.G, to.G, local),
                    Lerp(from.B, to.B, local),
                    Lerp(from.A, to.A, local) * opacity);
            }

            return Pack(_colors[^1], opacity);
        }

        private static double ReflectT(double t)
        {
            var wrapped = Math.Abs(t) % 2.0;
            return wrapped > 1.0 ? 2.0 - wrapped : wrapped;
        }

        private static (double R, double G, double B, double A) Pack(Color color, double opacity) =>
            (color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0 * opacity);

        private static double Lerp(byte from, byte to, double t) => (from + ((to - from) * t)) / 255.0;
    }

    #endregion
}
