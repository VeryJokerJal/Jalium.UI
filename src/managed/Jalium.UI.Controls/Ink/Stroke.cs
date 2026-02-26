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
    /// Core drawing implementation using ellipse stamping for smooth strokes.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    protected void DrawCore(DrawingContext dc)
    {
        if (_stylusPoints.Count == 0)
            return;

        // Dispatch to appropriate brush type renderer
        switch (_drawingAttributes.BrushType)
        {
            case BrushType.Round:
            case BrushType.Pen:
                DrawRoundBrush(dc);
                break;
            case BrushType.Calligraphy:
                DrawCalligraphyBrush(dc);
                break;
            case BrushType.Airbrush:
                DrawAirbrush(dc);
                break;
            case BrushType.Crayon:
                DrawCrayonBrush(dc);
                break;
            case BrushType.Marker:
                DrawMarkerBrush(dc);
                break;
            case BrushType.Pencil:
                DrawPencilBrush(dc);
                break;
            case BrushType.Oil:
                DrawOilBrush(dc);
                break;
            case BrushType.Watercolor:
                DrawWatercolorBrush(dc);
                break;
            default:
                DrawRoundBrush(dc);
                break;
        }
    }

    /// <summary>
    /// Draws stroke with round brush (default smooth circular stroke).
    /// </summary>
    private void DrawRoundBrush(DrawingContext dc)
    {
        // Create brush
        var baseColor = _drawingAttributes.Color;
        var brush = new SolidColorBrush(_drawingAttributes.Color);
        if (_drawingAttributes.IsHighlighter)
            brush = new SolidColorBrush(Color.FromArgb(128, _drawingAttributes.Color.R, _drawingAttributes.Color.G, _drawingAttributes.Color.B));

        var radiusX = _drawingAttributes.Width / 2;
        var radiusY = _drawingAttributes.Height / 2;

        // For single point, just draw one ellipse
        if (_stylusPoints.Count == 1)
        {
            var point = _stylusPoints[0].ToPoint();
            var animatedRadius = ApplyAnimationScale(radiusX, radiusY, 0.0);
            dc.DrawEllipse(brush, null, point, animatedRadius.X, animatedRadius.Y);
            return;
        }

        var totalPoints = _stylusPoints.Count;

        // Draw ellipses along the stroke path with interpolation for smooth appearance
        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            var p1 = _stylusPoints[i].ToPoint();
            var p2 = _stylusPoints[i + 1].ToPoint();

            // Calculate distance between points
            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);

            // Determine step size based on radius (smaller steps = smoother but slower)
            // Use 0.25 multiplier to ensure ellipses overlap and create smooth continuous stroke
            var stepSize = Math.Max(0.5, Math.Min(radiusX, radiusY) * 0.25);
            var steps = Math.Max(1, (int)(segmentLength / stepSize));

            // Draw interpolated ellipses along the segment
            for (int j = 0; j <= steps; j++)
            {
                var t = (double)j / steps;
                var x = p1.X + dx * t;
                var y = p1.Y + dy * t;

                // Calculate progress based on point index (0 = oldest/start, 1 = newest/tip)
                // This creates real-time animation effect during drawing
                var pointProgress = (i + t) / (totalPoints - 1);

                // Get pressure factor if available (for variable width)
                var pressure1 = _stylusPoints[i].PressureFactor;
                var pressure2 = _stylusPoints[i + 1].PressureFactor;
                var pressure = pressure1 + (pressure2 - pressure1) * t;

                // Apply pressure to radius if not ignoring pressure
                var currentRadiusX = radiusX;
                var currentRadiusY = radiusY;
                if (!_drawingAttributes.IgnorePressure)
                {
                    currentRadiusX *= pressure;
                    currentRadiusY *= pressure;
                }

                // Apply animation scaling based on point progress
                var animatedRadius = ApplyAnimationScale(currentRadiusX, currentRadiusY, pointProgress);

                dc.DrawEllipse(brush, null, new Point(x, y), animatedRadius.X, animatedRadius.Y);
            }
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
        var radiusY = _drawingAttributes.Height / 2;

        var random = new Random(_stylusPoints.GetHashCode());

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            var p1 = _stylusPoints[i].ToPoint();
            var p2 = _stylusPoints[i + 1].ToPoint();

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);

            var stepSize = Math.Max(2.0, radiusX * 0.5);
            var steps = Math.Max(1, (int)(segmentLength / stepSize));

            for (int j = 0; j <= steps; j++)
            {
                var t = (double)j / steps;
                var centerX = p1.X + dx * t;
                var centerY = p1.Y + dy * t;

                // Draw multiple particles for spray effect
                int particleCount = 15;
                for (int k = 0; k < particleCount; k++)
                {
                    var angle = random.NextDouble() * Math.PI * 2;
                    var distance = random.NextDouble() * radiusX * 1.5;
                    var x = centerX + Math.Cos(angle) * distance;
                    var y = centerY + Math.Sin(angle) * distance;

                    var alpha = (byte)(20 + random.Next(40)); // Varied transparency
                    var particleBrush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                    var particleSize = random.NextDouble() * 1.5 + 0.5;

                    dc.DrawEllipse(particleBrush, null, new Point(x, y), particleSize, particleSize);
                }
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

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            var p1 = _stylusPoints[i].ToPoint();
            var p2 = _stylusPoints[i + 1].ToPoint();

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);

            var stepSize = Math.Max(0.5, radiusX * 0.2);
            var steps = Math.Max(1, (int)(segmentLength / stepSize));

            for (int j = 0; j <= steps; j++)
            {
                var t = (double)j / steps;
                var x = p1.X + dx * t;
                var y = p1.Y + dy * t;

                // Add randomness for crayon texture
                var offsetX = (random.NextDouble() - 0.5) * radiusX * 0.5;
                var offsetY = (random.NextDouble() - 0.5) * radiusY * 0.5;
                var alpha = (byte)(180 + random.Next(75)); // Varied opacity

                var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                var size = radiusX * (0.8 + random.NextDouble() * 0.4);

                dc.DrawEllipse(brush, null, new Point(x + offsetX, y + offsetY), size, size);
            }
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

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            var p1 = _stylusPoints[i].ToPoint();
            var p2 = _stylusPoints[i + 1].ToPoint();

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);

            var stepSize = Math.Max(0.5, Math.Min(radiusX, radiusY) * 0.3);
            var steps = Math.Max(1, (int)(segmentLength / stepSize));

            for (int j = 0; j <= steps; j++)
            {
                var t = (double)j / steps;
                var x = p1.X + dx * t;
                var y = p1.Y + dy * t;

                var pointProgress = (i + t) / (totalPoints - 1);
                var animatedRadius = ApplyAnimationScale(radiusX, radiusY, pointProgress);

                dc.DrawEllipse(brush, null, new Point(x, y), animatedRadius.X, animatedRadius.Y);
            }
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

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            var p1 = _stylusPoints[i].ToPoint();
            var p2 = _stylusPoints[i + 1].ToPoint();

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);

            var stepSize = Math.Max(0.3, radiusX * 0.15);
            var steps = Math.Max(1, (int)(segmentLength / stepSize));

            for (int j = 0; j <= steps; j++)
            {
                var t = (double)j / steps;
                var x = p1.X + dx * t;
                var y = p1.Y + dy * t;

                // Add slight randomness for pencil grain
                var offsetX = (random.NextDouble() - 0.5) * radiusX * 0.3;
                var offsetY = (random.NextDouble() - 0.5) * radiusY * 0.3;
                var alpha = (byte)(200 + random.Next(55)); // Varied opacity for grain

                var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                var size = radiusX * 0.5 * (0.9 + random.NextDouble() * 0.2);

                dc.DrawEllipse(brush, null, new Point(x + offsetX, y + offsetY), size, size);
            }
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

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            var p1 = _stylusPoints[i].ToPoint();
            var p2 = _stylusPoints[i + 1].ToPoint();

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);

            var stepSize = Math.Max(0.5, radiusX * 0.2);
            var steps = Math.Max(1, (int)(segmentLength / stepSize));

            for (int j = 0; j <= steps; j++)
            {
                var t = (double)j / steps;
                var x = p1.X + dx * t;
                var y = p1.Y + dy * t;

                // Draw multiple overlapping strokes for thick paint texture
                int layers = 5;
                for (int layer = 0; layer < layers; layer++)
                {
                    var offsetX = (random.NextDouble() - 0.5) * radiusX * 0.6;
                    var offsetY = (random.NextDouble() - 0.5) * radiusY * 0.6;

                    // Vary color slightly for oil paint mixing effect
                    var colorVariation = (int)((random.NextDouble() - 0.5) * 20);
                    var r = (byte)Math.Clamp(baseColor.R + colorVariation, 0, 255);
                    var g = (byte)Math.Clamp(baseColor.G + colorVariation, 0, 255);
                    var b = (byte)Math.Clamp(baseColor.B + colorVariation, 0, 255);

                    var alpha = (byte)(150 + random.Next(50)); // Thick, semi-opaque
                    var brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));

                    var size = radiusX * 0.9 * (0.8 + random.NextDouble() * 0.4);
                    dc.DrawEllipse(brush, null, new Point(x + offsetX, y + offsetY), size, size * 0.8);
                }
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
        var radiusY = _drawingAttributes.Height / 2;

        var random = new Random(_stylusPoints.GetHashCode());

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            var p1 = _stylusPoints[i].ToPoint();
            var p2 = _stylusPoints[i + 1].ToPoint();

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);

            var stepSize = Math.Max(1.0, radiusX * 0.4);
            var steps = Math.Max(1, (int)(segmentLength / stepSize));

            for (int j = 0; j <= steps; j++)
            {
                var t = (double)j / steps;
                var x = p1.X + dx * t;
                var y = p1.Y + dy * t;

                // Draw multiple layers with decreasing opacity for watercolor effect
                int layers = 8;
                for (int layer = 0; layer < layers; layer++)
                {
                    var layerRadius = radiusX * (1.0 + layer * 0.15); // Expand outward
                    var angle = random.NextDouble() * Math.PI * 2;
                    var distance = random.NextDouble() * radiusX * 0.4;

                    var offsetX = Math.Cos(angle) * distance;
                    var offsetY = Math.Sin(angle) * distance;

                    // Fade out as we go outward (watercolor diffusion)
                    var alpha = (byte)(15 + (layers - layer) * 8);
                    var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));

                    var size = layerRadius * (0.7 + random.NextDouble() * 0.3);
                    dc.DrawEllipse(brush, null, new Point(x + offsetX, y + offsetY), size, size);
                }
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

        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = _stylusPoints[0].ToPoint(),
            IsClosed = false,
            IsFilled = false
        };

        for (int i = 1; i < _stylusPoints.Count; i++)
        {
            figure.Segments.Add(new LineSegment(_stylusPoints[i].ToPoint()));
        }

        geometry.Figures.Add(figure);
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
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Raises the <see cref="Invalidated"/> event.
    /// </summary>
    protected void OnInvalidated()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
