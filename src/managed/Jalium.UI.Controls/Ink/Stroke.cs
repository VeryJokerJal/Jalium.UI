using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ink;

/// <summary>
/// Represents a single ink stroke consisting of stylus points and drawing attributes.
/// </summary>
public sealed class Stroke : INotifyPropertyChanged
{
    private StylusPointCollection _stylusPoints;
    private DrawingAttributes _drawingAttributes;
    private StrokeTaperMode _taperMode = StrokeTaperMode.None;

    // Rendering cache: avoids expensive per-frame tessellation for committed strokes.
    // For path-based brushes: cached geometry + pen drawn in a single DrawGeometry call.
    // For particle brushes: cached raw ellipse batch data for native batch rendering.
    private Geometry? _cachedGeometry;
    private SolidColorBrush? _cachedBrush;
    private Pen? _cachedPen;
    private DrawingGroup? _cachedDrawing;
    private float[]? _cachedEllipseBatchData;
    private int _cachedEllipseBatchCount;
    private bool _renderCacheDirty = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="Stroke"/> class.
    /// </summary>
    /// <param name="stylusPoints">The collection of stylus points.</param>
    public Stroke(StylusPointCollection stylusPoints)
        : this(stylusPoints, new DrawingAttributes())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Stroke"/> class.
    /// </summary>
    /// <param name="stylusPoints">The collection of stylus points.</param>
    /// <param name="drawingAttributes">The drawing attributes for this stroke.</param>
    public Stroke(StylusPointCollection stylusPoints, DrawingAttributes drawingAttributes)
    {
        _stylusPoints = stylusPoints ?? throw new ArgumentNullException(nameof(stylusPoints));
        _drawingAttributes = drawingAttributes ?? throw new ArgumentNullException(nameof(drawingAttributes));

        _stylusPoints.Changed += OnStylusPointsChanged;
        _drawingAttributes.AttributeChanged += OnDrawingAttributesChanged;
    }

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs when the stroke needs to be redrawn.
    /// </summary>
    public event EventHandler? Invalidated;

    /// <summary>
    /// Occurs when the stylus points collection changes.
    /// </summary>
    public event EventHandler? StylusPointsChanged;

    /// <summary>
    /// Occurs when the drawing attributes change.
    /// </summary>
    public event EventHandler? DrawingAttributesChanged;

    /// <summary>
    /// Gets or sets the collection of stylus points that define this stroke.
    /// </summary>
    public StylusPointCollection StylusPoints
    {
        get => _stylusPoints;
        set
        {
            if (_stylusPoints != value)
            {
                if (_stylusPoints != null)
                    _stylusPoints.Changed -= OnStylusPointsChanged;

                _stylusPoints = value ?? throw new ArgumentNullException(nameof(value));
                _stylusPoints.Changed += OnStylusPointsChanged;

                OnPropertyChanged(nameof(StylusPoints));
                OnInvalidated();
            }
        }
    }

    /// <summary>
    /// Gets or sets the drawing attributes for this stroke.
    /// </summary>
    public DrawingAttributes DrawingAttributes
    {
        get => _drawingAttributes;
        set
        {
            if (_drawingAttributes != value)
            {
                if (_drawingAttributes != null)
                    _drawingAttributes.AttributeChanged -= OnDrawingAttributesChanged;

                _drawingAttributes = value ?? throw new ArgumentNullException(nameof(value));
                _drawingAttributes.AttributeChanged += OnDrawingAttributesChanged;

                OnPropertyChanged(nameof(DrawingAttributes));
                OnInvalidated();
            }
        }
    }

    /// <summary>
    /// Gets or sets the taper mode for this stroke.
    /// </summary>
    public StrokeTaperMode TaperMode
    {
        get => _taperMode;
        set
        {
            if (_taperMode != value)
            {
                _taperMode = value;
                OnPropertyChanged(nameof(TaperMode));
                OnInvalidated();
            }
        }
    }

    /// <summary>
    /// Creates a copy of this stroke.
    /// </summary>
    /// <returns>A new <see cref="Stroke"/> with cloned points and attributes.</returns>
    public Stroke Clone()
    {
        var clone = new Stroke(_stylusPoints.Clone(), _drawingAttributes.Clone());
        clone.TaperMode = TaperMode;
        return clone;
    }

    /// <summary>
    /// Gets the bounding rectangle of this stroke.
    /// </summary>
    /// <returns>A <see cref="Rect"/> that bounds this stroke.</returns>
    public Rect GetBounds()
    {
        var bounds = _stylusPoints.GetBounds();
        if (bounds.IsEmpty)
            return bounds;

        var halfWidth = _drawingAttributes.Width / 2;
        var halfHeight = _drawingAttributes.Height / 2;
        var inflate = Math.Max(halfWidth, halfHeight);

        return new Rect(
            bounds.X - inflate,
            bounds.Y - inflate,
            bounds.Width + inflate * 2,
            bounds.Height + inflate * 2);
    }

    /// <summary>
    /// Determines whether this stroke intersects with the specified point.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <param name="diameter">The diameter of the hit test area.</param>
    /// <returns>True if the point hits this stroke; otherwise, false.</returns>
    public bool HitTest(Point point, double diameter)
    {
        if (_stylusPoints.Count == 0)
            return false;

        var hitRadius = diameter / 2 + Math.Max(_drawingAttributes.Width, _drawingAttributes.Height) / 2;

        if (_stylusPoints.Count == 1)
        {
            var sp = _stylusPoints[0];
            var dx = point.X - sp.X;
            var dy = point.Y - sp.Y;
            return dx * dx + dy * dy <= hitRadius * hitRadius;
        }

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            var p1 = _stylusPoints[i].ToPoint();
            var p2 = _stylusPoints[i + 1].ToPoint();
            var distance = DistanceToLineSegment(point, p1, p2);
            if (distance <= hitRadius)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether this stroke intersects with the specified rectangle.
    /// </summary>
    /// <param name="rect">The rectangle to test.</param>
    /// <returns>True if the rectangle intersects this stroke; otherwise, false.</returns>
    public bool HitTest(Rect rect)
    {
        var bounds = GetBounds();
        return rect.IntersectsWith(bounds);
    }

    /// <summary>
    /// Draws this stroke using the specified drawing context.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    public void Draw(DrawingContext dc)
    {
        DrawCore(dc);
    }

    /// <summary>
    /// Core drawing implementation. Uses cached path geometry for path-based brushes
    /// (single draw call) or cached DrawingGroup for particle brushes (replay).
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    private void DrawCore(DrawingContext dc)
    {
        if (_stylusPoints.Count == 0)
            return;

        if (_renderCacheDirty)
        {
            BuildRenderCache();
        }

        // Fast path: single DrawGeometry call for path-based brushes
        if (_cachedGeometry != null)
        {
            if (_cachedPen != null)
                dc.DrawGeometry(null, _cachedPen, _cachedGeometry);
            else
                dc.DrawGeometry(_cachedBrush, null, _cachedGeometry);
            return;
        }

        // Particle brushes: use native batch when available, otherwise replay DrawingGroup
        if (_cachedEllipseBatchData != null && _cachedEllipseBatchCount > 0)
        {
            if (dc is Jalium.UI.Interop.RenderTargetDrawingContext rtdc)
            {
                // Direct native batch: single P/Invoke for all particles.
                // Reuse a single SolidColorBrush to avoid per-particle allocation.
                rtdc.BeginEllipseBatch(_cachedEllipseBatchCount);
                var reusableBrush = new SolidColorBrush();
                for (int i = 0; i < _cachedEllipseBatchCount; i++)
                {
                    var off = i * 5;
                    var packedBits = BitConverter.SingleToInt32Bits(_cachedEllipseBatchData[off + 4]);
                    reusableBrush.Color = Color.FromArgb(
                        (byte)((packedBits >> 24) & 0xFF),
                        (byte)(packedBits & 0xFF),
                        (byte)((packedBits >> 8) & 0xFF),
                        (byte)((packedBits >> 16) & 0xFF));
                    dc.DrawEllipse(reusableBrush, null,
                        new Point(_cachedEllipseBatchData[off], _cachedEllipseBatchData[off + 1]),
                        _cachedEllipseBatchData[off + 2], _cachedEllipseBatchData[off + 3]);
                }
                rtdc.EndEllipseBatch();
                return;
            }

            // Fallback for non-RenderTarget contexts (e.g., DrawingGroup recording)
            if (_cachedDrawing != null)
            {
                _cachedDrawing.RenderTo(dc);
                return;
            }
        }

        // Fallback
        DrawRoundBrush(dc);
    }

    /// <summary>
    /// Builds the rendering cache appropriate for the brush type.
    /// </summary>
    private void BuildRenderCache()
    {
        _renderCacheDirty = false;
        _cachedGeometry = null;
        _cachedBrush = null;
        _cachedPen = null;
        _cachedDrawing = null;

        switch (_drawingAttributes.BrushType)
        {
            case BrushType.Round:
            case BrushType.Pen:
            case BrushType.Marker:
                BuildPathCache();
                break;
            case BrushType.Calligraphy:
                BuildCalligraphyPathCache();
                break;
            // Particle-based brushes: record into DrawingGroup
            case BrushType.Airbrush:
            case BrushType.Crayon:
            case BrushType.Pencil:
            case BrushType.Oil:
            case BrushType.Watercolor:
                BuildParticleCache();
                break;
            default:
                BuildPathCache();
                break;
        }
    }

    /// <summary>
    /// Builds a cached StreamGeometry path for round/pen/marker brushes.
    /// Produces a single DrawGeometry call instead of hundreds of DrawEllipse calls.
    /// </summary>
    private void BuildPathCache()
    {
        var color = _drawingAttributes.Color;
        if (_drawingAttributes.IsHighlighter || _drawingAttributes.BrushType == BrushType.Marker)
        {
            var alpha = _drawingAttributes.BrushType == BrushType.Marker
                ? (byte)Math.Min((int)200, (int)color.A)
                : (byte)128;
            color = Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        var hasPressureVariation = !_drawingAttributes.IgnorePressure && HasPressureVariation();
        var hasTaper = _taperMode != StrokeTaperMode.None;

        if (hasPressureVariation || hasTaper)
        {
            // Variable-width stroke: build filled outline geometry
            BuildVariableWidthCache(color);
            return;
        }

        // Uniform-width stroke: use thick Pen on a polyline path
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, _drawingAttributes.Width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(_stylusPoints[0].ToPoint(), false, false);

            if (_drawingAttributes.FitToCurve && _stylusPoints.Count > 2)
            {
                // Catmull-Rom → cubic Bézier curve fitting for smooth strokes
                EmitCatmullRomBeziers(ctx, i => _stylusPoints[i].ToPoint(), _stylusPoints.Count);
            }
            else
            {
                for (int i = 1; i < _stylusPoints.Count; i++)
                {
                    ctx.LineTo(_stylusPoints[i].ToPoint(), true, true);
                }
            }
        }

        _cachedGeometry = geometry;
        _cachedPen = pen;
    }

    /// <summary>
    /// Builds a variable-width stroke as a chain of overlapping circles.
    /// Each stylus point gets a filled circle whose radius is modulated by
    /// pressure and taper.  This approach naturally produces capsule-shaped
    /// endpoints, handles self-intersecting paths (circles, figure-eights),
    /// and avoids the polygon self-intersection issues of outline-based rendering.
    /// </summary>
    private void BuildVariableWidthCache(Color color)
    {
        var radiusX = _drawingAttributes.Width / 2;
        var radiusY = _drawingAttributes.Height / 2;
        var totalPoints = _stylusPoints.Count;

        if (totalPoints <= 1)
        {
            if (totalPoints == 1)
            {
                var point = _stylusPoints[0].ToPoint();
                var animatedRadius = ApplyAnimationScale(radiusX, radiusY, 0.0);
                var ellipse = new EllipseGeometry { Center = point, RadiusX = animatedRadius.X, RadiusY = animatedRadius.Y };
                _cachedGeometry = ellipse;
                _cachedBrush = new SolidColorBrush(color);
            }
            return;
        }

        // Build circle chain with interpolation between stylus points.
        // For each pair of adjacent points, if the gap exceeds half the local
        // radius we insert intermediate circles so there are no visible seams.
        var collector = new EllipseBatchCollector();

        for (int i = 0; i < totalPoints; i++)
        {
            var pt = _stylusPoints[i].ToPoint();
            var pressure = _drawingAttributes.IgnorePressure ? 1.0 : _stylusPoints[i].PressureFactor;
            var progress = (double)i / (totalPoints - 1);
            var animR = ApplyAnimationScale(radiusX * pressure, radiusY * pressure, progress);
            collector.Add((float)pt.X, (float)pt.Y, (float)animR.X, (float)animR.Y, color);

            // Interpolate to next point if gap is too large
            if (i < totalPoints - 1)
            {
                var nextPt = _stylusPoints[i + 1].ToPoint();
                var dx = nextPt.X - pt.X;
                var dy = nextPt.Y - pt.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var stepR = Math.Max(animR.X, animR.Y) * 0.3;
                if (stepR < 0.5) stepR = 0.5;
                if (dist > stepR)
                {
                    int steps = (int)Math.Ceiling(dist / stepR);
                    var nextPressure = _drawingAttributes.IgnorePressure ? 1.0 : _stylusPoints[i + 1].PressureFactor;
                    var nextProgress = (double)(i + 1) / (totalPoints - 1);
                    for (int s = 1; s < steps; s++)
                    {
                        var t = (double)s / steps;
                        var ix = pt.X + dx * t;
                        var iy = pt.Y + dy * t;
                        var ip = pressure + (nextPressure - pressure) * t;
                        var iprog = progress + (nextProgress - progress) * t;
                        var iR = ApplyAnimationScale(radiusX * ip, radiusY * ip, iprog);
                        collector.Add((float)ix, (float)iy, (float)iR.X, (float)iR.Y, color);
                    }
                }
            }
        }

        _cachedEllipseBatchData = collector.GetData();
        _cachedEllipseBatchCount = collector.Count;

        // Also build a DrawingGroup fallback for non-GPU contexts
        _cachedDrawing = new DrawingGroup();
        using var cacheDc = _cachedDrawing.Open();
        var brush = new SolidColorBrush(color);
        for (int i = 0; i < collector.Count; i++)
        {
            var off = i * 5;
            cacheDc.DrawEllipse(brush, null,
                new Point(_cachedEllipseBatchData[off], _cachedEllipseBatchData[off + 1]),
                _cachedEllipseBatchData[off + 2], _cachedEllipseBatchData[off + 3]);
        }
    }

    /// <summary>
    /// Builds a path cache for calligraphy brush (thin elliptical pen nib).
    /// </summary>
    private void BuildCalligraphyPathCache()
    {
        var color = _drawingAttributes.Color;
        var brush = new SolidColorBrush(color);

        // Calligraphy uses a flattened pen nib - thin in Y direction
        var pen = new Pen(brush, _drawingAttributes.Width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(_stylusPoints[0].ToPoint(), false, false);

            if (_drawingAttributes.FitToCurve && _stylusPoints.Count > 2)
            {
                EmitCatmullRomBeziers(ctx, i => _stylusPoints[i].ToPoint(), _stylusPoints.Count);
            }
            else
            {
                for (int i = 1; i < _stylusPoints.Count; i++)
                    ctx.LineTo(_stylusPoints[i].ToPoint(), true, true);
            }
        }

        _cachedGeometry = geometry;
        _cachedPen = pen;
    }

    /// <summary>
    /// Builds a raw ellipse batch cache for particle-based brushes (airbrush, crayon, etc.).
    /// Collects all ellipse data into a flat array for native batch rendering.
    /// Also records into a DrawingGroup as fallback for non-GPU contexts.
    /// </summary>
    private void BuildParticleCache()
    {
        // Record into DrawingGroup (fallback) and simultaneously collect raw ellipse data
        var collector = new EllipseBatchCollector();
        _cachedDrawing = new DrawingGroup();
        using var cacheDc = _cachedDrawing.Open();

        // Draw into a collecting wrapper that captures ellipse data
        var wrappedDc = new EllipseCollectingDrawingContext(cacheDc, collector);

        switch (_drawingAttributes.BrushType)
        {
            case BrushType.Airbrush:
                DrawAirbrush(wrappedDc);
                break;
            case BrushType.Crayon:
                DrawCrayonBrush(wrappedDc);
                break;
            case BrushType.Pencil:
                DrawPencilBrush(wrappedDc);
                break;
            case BrushType.Oil:
                DrawOilBrush(wrappedDc);
                break;
            case BrushType.Watercolor:
                DrawWatercolorBrush(wrappedDc);
                break;
        }

        cacheDc.Close();

        // Store the collected batch data
        _cachedEllipseBatchData = collector.GetData();
        _cachedEllipseBatchCount = collector.Count;
    }

    /// <summary>
    /// Collects ellipse draw calls into a flat array for native batching.
    /// </summary>
    private sealed class EllipseBatchCollector
    {
        private float[] _data = new float[256 * 5];
        private int _count;

        public int Count => _count;

        public void Add(float cx, float cy, float rx, float ry, Color color)
        {
            if ((_count + 1) * 5 > _data.Length)
                Array.Resize(ref _data, _data.Length * 2);

            var offset = _count * 5;
            _data[offset] = cx;
            _data[offset + 1] = cy;
            _data[offset + 2] = rx;
            _data[offset + 3] = ry;
            uint packed = (uint)color.R | ((uint)color.G << 8) | ((uint)color.B << 16) | ((uint)color.A << 24);
            _data[offset + 4] = BitConverter.Int32BitsToSingle((int)packed);
            _count++;
        }

        public float[] GetData() => _data;
    }

    /// <summary>
    /// A DrawingContext wrapper that delegates to an inner context while also
    /// collecting filled ellipse data for native batch rendering.
    /// </summary>
    private sealed class EllipseCollectingDrawingContext : DrawingContext
    {
        private readonly DrawingContext _inner;
        private readonly EllipseBatchCollector _collector;

        public EllipseCollectingDrawingContext(DrawingContext inner, EllipseBatchCollector collector)
        {
            _inner = inner;
            _collector = collector;
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
            _inner.DrawEllipse(brush, pen, center, radiusX, radiusY);

            if (brush is SolidColorBrush solidBrush && pen == null)
            {
                _collector.Add((float)center.X, (float)center.Y, (float)radiusX, (float)radiusY, solidBrush.Color);
            }
        }

        // Delegate remaining methods to inner
        public override void DrawLine(Pen pen, Point point0, Point point1) => _inner.DrawLine(pen, point0, point1);
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) => _inner.DrawRectangle(brush, pen, rectangle);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) => _inner.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        public override void DrawText(FormattedText formattedText, Point origin) => _inner.DrawText(formattedText, origin);
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => _inner.DrawGeometry(brush, pen, geometry);
        public override void DrawImage(ImageSource imageSource, Rect rectangle) => _inner.DrawImage(imageSource, rectangle);
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) => _inner.DrawBackdropEffect(rectangle, effect, cornerRadius);
        public override void PushTransform(Transform transform) => _inner.PushTransform(transform);
        public override void PushClip(Geometry clipGeometry) => _inner.PushClip(clipGeometry);
        public override void PushOpacity(double opacity) => _inner.PushOpacity(opacity);
        public override void Pop() => _inner.Pop();
        public override void Close() => _inner.Close();
    }

    /// <summary>
    /// Emits Catmull-Rom spline segments as cubic Bézier curves into a StreamGeometryContext.
    /// The first point (index 0) must already be set via BeginFigure.
    /// </summary>
    /// <param name="ctx">The geometry context to emit into.</param>
    /// <param name="getPoint">Function returning the point at the given index.</param>
    /// <param name="count">Total number of points.</param>
    private static void EmitCatmullRomBeziers(StreamGeometryContext ctx, Func<int, Point> getPoint, int count)
    {
        // For each segment between points[i] and points[i+1], compute cubic Bézier
        // control points from the Catmull-Rom tangents.
        // Catmull-Rom tangent at point i: T_i = (P_{i+1} - P_{i-1}) / 2
        // Bézier CP1 = P_i + T_i / 3
        // Bézier CP2 = P_{i+1} - T_{i+1} / 3
        for (int i = 0; i < count - 1; i++)
        {
            var p0 = getPoint(Math.Max(i - 1, 0));
            var p1 = getPoint(i);
            var p2 = getPoint(i + 1);
            var p3 = getPoint(Math.Min(i + 2, count - 1));

            // Tangents
            var t1x = (p2.X - p0.X) / 2.0;
            var t1y = (p2.Y - p0.Y) / 2.0;
            var t2x = (p3.X - p1.X) / 2.0;
            var t2y = (p3.Y - p1.Y) / 2.0;

            // Bézier control points
            var cp1 = new Point(p1.X + t1x / 3.0, p1.Y + t1y / 3.0);
            var cp2 = new Point(p2.X - t2x / 3.0, p2.Y - t2y / 3.0);

            ctx.BezierTo(cp1, cp2, p2, true, true);
        }
    }

    /// <summary>
    /// Catmull-Rom scalar interpolation: 0.5 * ((2*p1) + (-p0+p2)*t + (2*p0-5*p1+4*p2-p3)*t² + (-p0+3*p1-3*p2+p3)*t³)
    /// </summary>
    private static double CatmullRom(double t, double p0, double p1, double p2, double p3)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5 * (2.0 * p1
            + (-p0 + p2) * t
            + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
            + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3);
    }

    /// <summary>
    /// Iterates smoothed points along the stroke path using Catmull-Rom interpolation.
    /// Yields (x, y, pressure, segmentIndex, segmentT) for each interpolated sample.
    /// Used by particle brushes to place particles along smooth curves instead of straight lines.
    /// </summary>
    private IEnumerable<(double X, double Y, double Pressure, int SegmentIndex, double SegmentT)>
        EnumerateSmoothedPath(double stepSize)
    {
        if (_stylusPoints.Count < 2)
            yield break;

        var useCurve = _drawingAttributes.FitToCurve && _stylusPoints.Count > 2;
        var count = _stylusPoints.Count;

        for (int i = 0; i < count - 1; i++)
        {
            var p1 = _stylusPoints[i];
            var p2 = _stylusPoints[i + 1];

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var segLen = Math.Sqrt(dx * dx + dy * dy);
            var steps = Math.Max(1, (int)(segLen / stepSize));

            for (int j = 0; j <= steps; j++)
            {
                var t = (double)j / steps;
                double x, y;

                if (useCurve)
                {
                    var ip0 = Math.Max(i - 1, 0);
                    var ip3 = Math.Min(i + 2, count - 1);
                    x = CatmullRom(t, _stylusPoints[ip0].X, p1.X, p2.X, _stylusPoints[ip3].X);
                    y = CatmullRom(t, _stylusPoints[ip0].Y, p1.Y, p2.Y, _stylusPoints[ip3].Y);
                }
                else
                {
                    x = p1.X + dx * t;
                    y = p1.Y + dy * t;
                }

                var pressure = p1.PressureFactor + (p2.PressureFactor - p1.PressureFactor) * t;
                yield return (x, y, pressure, i, t);
            }
        }
    }

    private bool HasPressureVariation()
    {
        if (_stylusPoints.Count <= 1)
            return false;
        var first = _stylusPoints[0].PressureFactor;
        for (int i = 1; i < _stylusPoints.Count; i++)
        {
            if (Math.Abs(_stylusPoints[i].PressureFactor - first) > 0.01)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Draws stroke with round brush (default smooth circular stroke).
    /// </summary>
    private void DrawRoundBrush(DrawingContext dc)
    {
        var baseColor = _drawingAttributes.Color;
        var brush = new SolidColorBrush(_drawingAttributes.Color);
        if (_drawingAttributes.IsHighlighter)
            brush = new SolidColorBrush(Color.FromArgb(128, baseColor.R, baseColor.G, baseColor.B));

        var radiusX = _drawingAttributes.Width / 2;
        var radiusY = _drawingAttributes.Height / 2;

        if (_stylusPoints.Count == 1)
        {
            var point = _stylusPoints[0].ToPoint();
            var animatedRadius = ApplyAnimationScale(radiusX, radiusY, 0.0);
            dc.DrawEllipse(brush, null, point, animatedRadius.X, animatedRadius.Y);
            return;
        }

        var totalPoints = _stylusPoints.Count;
        var stepSize = Math.Max(0.5, Math.Min(radiusX, radiusY) * 0.25);

        foreach (var (x, y, pressure, segIdx, segT) in EnumerateSmoothedPath(stepSize))
        {
            var pointProgress = (segIdx + segT) / (totalPoints - 1);

            var currentRadiusX = radiusX;
            var currentRadiusY = radiusY;
            if (!_drawingAttributes.IgnorePressure)
            {
                currentRadiusX *= pressure;
                currentRadiusY *= pressure;
            }

            var animatedRadius = ApplyAnimationScale(currentRadiusX, currentRadiusY, pointProgress);
            dc.DrawEllipse(brush, null, new Point(x, y), animatedRadius.X, animatedRadius.Y);
        }
    }

    /// <summary>
    /// Applies taper scaling to the radius based on the taper mode and progress.
    /// Progress: 0 = oldest point (start), 1 = newest point (current pen tip).
    /// </summary>
    /// <param name="radiusX">The base X radius.</param>
    /// <param name="radiusY">The base Y radius.</param>
    /// <param name="progress">The progress along the stroke (0 = start, 1 = tip).</param>
    /// <returns>The scaled radius.</returns>
    private Point ApplyAnimationScale(double radiusX, double radiusY, double progress)
    {
        double scale = 1.0;

        switch (_taperMode)
        {
            case StrokeTaperMode.TaperedStart:
                // TaperedStart: stroke starts thin and grows to full width
                // Start (oldest points) = small, Tip (newest points) = large
                // Using ease-out curve for natural tapering effect
                scale = EaseOutQuad(progress);
                // Scale from 0.2 to 1.0
                scale = 0.2 + scale * 0.8;
                break;

            case StrokeTaperMode.TaperedEnd:
                // TaperedEnd: stroke starts at full width and tapers to thin
                // Start (oldest points) = large, Tip (newest points) = small
                // Using ease-out curve for natural tapering effect
                scale = EaseOutQuad(1.0 - progress);
                // Scale from 0.2 to 1.0
                scale = 0.2 + scale * 0.8;
                break;

            case StrokeTaperMode.None:
            default:
                scale = 1.0;
                break;
        }

        return new Point(radiusX * scale, radiusY * scale);
    }

    /// <summary>
    /// Quadratic ease-out function: fast start, slow end.
    /// </summary>
    private static double EaseOutQuad(double t)
    {
        return 1.0 - (1.0 - t) * (1.0 - t);
    }

    #region Brush Type Implementations

    /// <summary>
    /// Draws stroke with calligraphy brush (varied width with artistic effect).
    /// </summary>
    private void DrawCalligraphyBrush(DrawingContext dc)
    {
        var baseColor = _drawingAttributes.Color;
        var brush = new SolidColorBrush(baseColor);

        var radiusX = _drawingAttributes.Width / 2;
        var radiusY = _drawingAttributes.Height / 2;

        if (_stylusPoints.Count == 1)
        {
            var point = _stylusPoints[0].ToPoint();
            dc.DrawEllipse(brush, null, point, radiusX, radiusY * 0.3); // Thin ellipse for calligraphy
            return;
        }

        var totalPoints = _stylusPoints.Count;

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            var p1 = _stylusPoints[i].ToPoint();
            var p2 = _stylusPoints[i + 1].ToPoint();

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);

            // Calculate angle for calligraphy effect
            var angle = Math.Atan2(dy, dx);
            var angleVariation = Math.Sin(angle * 3) * 0.5 + 0.5; // Vary width based on direction

            var stepSize = Math.Max(0.5, Math.Min(radiusX, radiusY) * 0.25);
            var steps = Math.Max(1, (int)(segmentLength / stepSize));

            for (int j = 0; j <= steps; j++)
            {
                var t = (double)j / steps;
                var x = p1.X + dx * t;
                var y = p1.Y + dy * t;

                var pointProgress = (i + t) / (totalPoints - 1);
                var pressure1 = _stylusPoints[i].PressureFactor;
                var pressure2 = _stylusPoints[i + 1].PressureFactor;
                var pressure = pressure1 + (pressure2 - pressure1) * t;

                var currentRadiusX = radiusX * pressure;
                var currentRadiusY = radiusY * 0.3 * angleVariation; // Thin, angle-dependent

                var animatedRadius = ApplyAnimationScale(currentRadiusX, currentRadiusY, pointProgress);
                dc.DrawEllipse(brush, null, new Point(x, y), animatedRadius.X, animatedRadius.Y);
            }
        }
    }

    /// <summary>
    /// Draws stroke with airbrush (soft spray effect with particles).
    /// </summary>
    private void DrawAirbrush(DrawingContext dc)
    {
        var baseColor = _drawingAttributes.Color;
        var radiusX = _drawingAttributes.Width / 2;
        var random = new Random(_stylusPoints.GetHashCode());
        var stepSize = Math.Max(2.0, radiusX * 0.5);

        foreach (var (cx, cy, _, _, _) in EnumerateSmoothedPath(stepSize))
        {
            int particleCount = 15;
            for (int k = 0; k < particleCount; k++)
            {
                var angle = random.NextDouble() * Math.PI * 2;
                var distance = random.NextDouble() * radiusX * 1.5;
                var x = cx + Math.Cos(angle) * distance;
                var y = cy + Math.Sin(angle) * distance;

                var alpha = (byte)(20 + random.Next(40));
                var particleBrush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                var particleSize = random.NextDouble() * 1.5 + 0.5;

                dc.DrawEllipse(particleBrush, null, new Point(x, y), particleSize, particleSize);
            }
        }
    }

    /// <summary>
    /// Draws stroke with crayon brush (rough textured effect).
    /// </summary>
    private void DrawCrayonBrush(DrawingContext dc)
    {
        var baseColor = _drawingAttributes.Color;
        var radiusX = _drawingAttributes.Width / 2;
        var radiusY = _drawingAttributes.Height / 2;
        var random = new Random(_stylusPoints.GetHashCode());
        var stepSize = Math.Max(0.5, radiusX * 0.2);

        foreach (var (x, y, _, _, _) in EnumerateSmoothedPath(stepSize))
        {
            var offsetX = (random.NextDouble() - 0.5) * radiusX * 0.5;
            var offsetY = (random.NextDouble() - 0.5) * radiusY * 0.5;
            var alpha = (byte)(180 + random.Next(75));

            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            var size = radiusX * (0.8 + random.NextDouble() * 0.4);

            dc.DrawEllipse(brush, null, new Point(x + offsetX, y + offsetY), size, size);
        }
    }

    /// <summary>
    /// Draws stroke with marker brush (semi-transparent wide stroke).
    /// </summary>
    private void DrawMarkerBrush(DrawingContext dc)
    {
        var baseColor = _drawingAttributes.Color;
        var alpha = (byte)Math.Min(200, (int)baseColor.A);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));

        var radiusX = _drawingAttributes.Width / 2;
        var radiusY = _drawingAttributes.Height / 2;

        if (_stylusPoints.Count == 1)
        {
            var point = _stylusPoints[0].ToPoint();
            dc.DrawEllipse(brush, null, point, radiusX, radiusY);
            return;
        }

        var totalPoints = _stylusPoints.Count;
        var stepSize = Math.Max(0.5, Math.Min(radiusX, radiusY) * 0.3);

        foreach (var (x, y, _, segIdx, segT) in EnumerateSmoothedPath(stepSize))
        {
            var pointProgress = (segIdx + segT) / (totalPoints - 1);
            var animatedRadius = ApplyAnimationScale(radiusX, radiusY, pointProgress);
            dc.DrawEllipse(brush, null, new Point(x, y), animatedRadius.X, animatedRadius.Y);
        }
    }

    /// <summary>
    /// Draws stroke with pencil brush (grainy textured stroke).
    /// </summary>
    private void DrawPencilBrush(DrawingContext dc)
    {
        var baseColor = _drawingAttributes.Color;
        var radiusX = _drawingAttributes.Width / 2;
        var radiusY = _drawingAttributes.Height / 2;
        var random = new Random(_stylusPoints.GetHashCode());
        var stepSize = Math.Max(0.3, radiusX * 0.15);

        foreach (var (x, y, _, _, _) in EnumerateSmoothedPath(stepSize))
        {
            var offsetX = (random.NextDouble() - 0.5) * radiusX * 0.3;
            var offsetY = (random.NextDouble() - 0.5) * radiusY * 0.3;
            var alpha = (byte)(200 + random.Next(55));

            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            var size = radiusX * 0.5 * (0.9 + random.NextDouble() * 0.2);

            dc.DrawEllipse(brush, null, new Point(x + offsetX, y + offsetY), size, size);
        }
    }

    /// <summary>
    /// Draws stroke with oil brush (textured artistic stroke with thick paint effect).
    /// </summary>
    private void DrawOilBrush(DrawingContext dc)
    {
        var baseColor = _drawingAttributes.Color;
        var radiusX = _drawingAttributes.Width / 2;
        var radiusY = _drawingAttributes.Height / 2;
        var random = new Random(_stylusPoints.GetHashCode());
        var stepSize = Math.Max(0.5, radiusX * 0.2);

        foreach (var (x, y, _, _, _) in EnumerateSmoothedPath(stepSize))
        {
            int layers = 5;
            for (int layer = 0; layer < layers; layer++)
            {
                var offsetX = (random.NextDouble() - 0.5) * radiusX * 0.6;
                var offsetY = (random.NextDouble() - 0.5) * radiusY * 0.6;

                var colorVariation = (int)((random.NextDouble() - 0.5) * 20);
                var r = (byte)Math.Clamp(baseColor.R + colorVariation, 0, 255);
                var g = (byte)Math.Clamp(baseColor.G + colorVariation, 0, 255);
                var b = (byte)Math.Clamp(baseColor.B + colorVariation, 0, 255);

                var alpha = (byte)(150 + random.Next(50));
                var brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));

                var size = radiusX * 0.9 * (0.8 + random.NextDouble() * 0.4);
                dc.DrawEllipse(brush, null, new Point(x + offsetX, y + offsetY), size, size * 0.8);
            }
        }
    }

    /// <summary>
    /// Draws stroke with watercolor brush (soft blended edges with color diffusion).
    /// </summary>
    private void DrawWatercolorBrush(DrawingContext dc)
    {
        var baseColor = _drawingAttributes.Color;
        var radiusX = _drawingAttributes.Width / 2;
        var random = new Random(_stylusPoints.GetHashCode());
        var stepSize = Math.Max(1.0, radiusX * 0.4);

        foreach (var (x, y, _, _, _) in EnumerateSmoothedPath(stepSize))
        {
            int layers = 8;
            for (int layer = 0; layer < layers; layer++)
            {
                var layerRadius = radiusX * (1.0 + layer * 0.15);
                var angle = random.NextDouble() * Math.PI * 2;
                var distance = random.NextDouble() * radiusX * 0.4;

                var offsetX = Math.Cos(angle) * distance;
                var offsetY = Math.Sin(angle) * distance;

                var alpha = (byte)(15 + (layers - layer) * 8);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));

                var size = layerRadius * (0.7 + random.NextDouble() * 0.3);
                dc.DrawEllipse(brush, null, new Point(x + offsetX, y + offsetY), size, size);
            }
        }
    }

    #endregion

    /// <summary>
    /// Gets the geometry representation of this stroke.
    /// </summary>
    /// <returns>A <see cref="Geometry"/> representing this stroke.</returns>
    public Geometry GetGeometry()
    {
        return GetGeometry(_drawingAttributes);
    }

    /// <summary>
    /// Gets the geometry representation of this stroke with the specified drawing attributes.
    /// </summary>
    /// <param name="attributes">The drawing attributes to use.</param>
    /// <returns>A <see cref="Geometry"/> representing this stroke.</returns>
    public Geometry GetGeometry(DrawingAttributes attributes)
    {
        if (_stylusPoints.Count < 2)
            return new PathGeometry();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(_stylusPoints[0].ToPoint(), false, false);

            if (attributes.FitToCurve && _stylusPoints.Count > 2)
            {
                EmitCatmullRomBeziers(ctx, i => _stylusPoints[i].ToPoint(), _stylusPoints.Count);
            }
            else
            {
                for (int i = 1; i < _stylusPoints.Count; i++)
                {
                    ctx.LineTo(_stylusPoints[i].ToPoint(), true, true);
                }
            }
        }

        return geometry;
    }

    /// <summary>
    /// Calculates the distance from a point to a line segment.
    /// </summary>
    private static double DistanceToLineSegment(Point point, Point lineStart, Point lineEnd)
    {
        var dx = lineEnd.X - lineStart.X;
        var dy = lineEnd.Y - lineStart.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared == 0)
        {
            // Line segment is a point
            var pdx = point.X - lineStart.X;
            var pdy = point.Y - lineStart.Y;
            return Math.Sqrt(pdx * pdx + pdy * pdy);
        }

        // Calculate projection parameter
        var t = Math.Max(0, Math.Min(1, ((point.X - lineStart.X) * dx + (point.Y - lineStart.Y) * dy) / lengthSquared));

        // Find closest point on segment
        var closestX = lineStart.X + t * dx;
        var closestY = lineStart.Y + t * dy;

        var distX = point.X - closestX;
        var distY = point.Y - closestY;
        return Math.Sqrt(distX * distX + distY * distY);
    }

    private void OnStylusPointsChanged(object? sender, EventArgs e)
    {
        StylusPointsChanged?.Invoke(this, EventArgs.Empty);
        OnInvalidated();
    }

    private void OnDrawingAttributesChanged(object? sender, EventArgs e)
    {
        DrawingAttributesChanged?.Invoke(this, EventArgs.Empty);
        OnInvalidated();
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Raises the <see cref="Invalidated"/> event.
    /// </summary>
    private void OnInvalidated()
    {
        _renderCacheDirty = true;
        _cachedGeometry = null;
        _cachedBrush = null;
        _cachedPen = null;
        _cachedDrawing = null;
        _cachedEllipseBatchData = null;
        _cachedEllipseBatchCount = 0;
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
