using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// CPU software rasterizer that converts a Drawing tree into a BGRA8 pixel buffer.
/// Used to cache vector image (SVG) content as bitmaps to avoid per-frame tessellation.
/// </summary>
internal static class SoftwareVectorRasterizer
{
    /// <summary>
    /// Rasterizes a Drawing into a BGRA8 pixel buffer at the specified size.
    /// Returns null if the drawing cannot be rasterized.
    /// </summary>
    public static byte[]? Rasterize(Drawing drawing, int width, int height)
    {
        if (drawing == null || width <= 0 || height <= 0)
            return null;

        var bounds = drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        // Allocate BGRA8 pixel buffer (pre-multiplied alpha, all transparent)
        var stride = width * 4;
        var pixels = new byte[stride * height];

        // Calculate transform from drawing space to pixel space
        var scaleX = width / bounds.Width;
        var scaleY = height / bounds.Height;
        var offsetX = -bounds.X * scaleX;
        var offsetY = -bounds.Y * scaleY;

        // Render the drawing tree
        var ctx = new SoftwareRenderContext(pixels, width, height, stride, scaleX, scaleY, offsetX, offsetY);
        RenderDrawing(drawing, ctx);

        return pixels;
    }

    private static void RenderDrawing(Drawing drawing, SoftwareRenderContext ctx)
    {
        if (drawing is DrawingGroup group)
        {
            var savedCtx = ctx;

            // Apply group transform
            if (group.Transform != null)
            {
                var m = group.Transform.Value;
                ctx = ctx.WithTransform(m);
            }

            // Apply group opacity
            if (group.Opacity < 1.0)
                ctx = ctx.WithOpacity(ctx.Opacity * group.Opacity);

            foreach (var child in group.Children)
            {
                if (child != null)
                    RenderDrawing(child, ctx);
            }
        }
        else if (drawing is GeometryDrawing geomDrawing)
        {
            RenderGeometryDrawing(geomDrawing, ctx);
        }
    }

    private static void RenderGeometryDrawing(GeometryDrawing drawing, SoftwareRenderContext ctx)
    {
        if (drawing.Geometry == null) return;

        // Get flattened path geometry (all curves → line segments)
        var flatGeometry = GetFlattenedGeometry(drawing.Geometry);
        if (flatGeometry == null) return;

        // Apply geometry transform if present
        var geoCtx = ctx;
        if (drawing.Geometry.Transform != null && !drawing.Geometry.Transform.Value.IsIdentity)
        {
            geoCtx = ctx.WithTransform(drawing.Geometry.Transform.Value);
        }

        // Fill — extract a representative color from any brush type
        if (drawing.Brush != null)
        {
            var (fb, fg, fr, fa) = ExtractBrushColor(drawing.Brush, geoCtx.Opacity);
            if (fa > 0)
            {
                FillGeometry(flatGeometry, geoCtx, fb, fg, fr, fa);
            }
        }

        // Stroke
        if (drawing.Pen?.Brush != null && drawing.Pen.Thickness > 0)
        {
            var (sb, sg, sr, sa) = ExtractBrushColor(drawing.Pen.Brush, geoCtx.Opacity);
            if (sa > 0)
            {
                var strokeWidth = drawing.Pen.Thickness;
                StrokeGeometry(flatGeometry, geoCtx, sb, sg, sr, sa, strokeWidth);
            }
        }
    }

    private static PathGeometry? GetFlattenedGeometry(Geometry geometry)
    {
        if (geometry is PathGeometry pg)
            return pg.GetFlattenedPathGeometry();
        if (geometry is RectangleGeometry rg)
        {
            var r = rg.Rect;
            var fig = new PathFigure { StartPoint = new Point(r.X, r.Y), IsClosed = true, IsFilled = true };
            fig.Segments.Add(new LineSegment { Point = new Point(r.X + r.Width, r.Y) });
            fig.Segments.Add(new LineSegment { Point = new Point(r.X + r.Width, r.Y + r.Height) });
            fig.Segments.Add(new LineSegment { Point = new Point(r.X, r.Y + r.Height) });
            var pg2 = new PathGeometry();
            pg2.Figures.Add(fig);
            return pg2;
        }
        if (geometry is EllipseGeometry eg)
        {
            // Approximate ellipse as polygon
            const int segments = 32;
            var fig = new PathFigure
            {
                StartPoint = new Point(eg.Center.X + eg.RadiusX, eg.Center.Y),
                IsClosed = true,
                IsFilled = true
            };
            for (int i = 1; i <= segments; i++)
            {
                var angle = 2 * Math.PI * i / segments;
                fig.Segments.Add(new LineSegment
                {
                    Point = new Point(
                        eg.Center.X + eg.RadiusX * Math.Cos(angle),
                        eg.Center.Y + eg.RadiusY * Math.Sin(angle))
                });
            }
            var pg2 = new PathGeometry();
            pg2.Figures.Add(fig);
            return pg2;
        }
        if (geometry is LineGeometry lg)
        {
            var fig = new PathFigure { StartPoint = lg.StartPoint, IsClosed = false, IsFilled = false };
            fig.Segments.Add(new LineSegment { Point = lg.EndPoint });
            var pg2 = new PathGeometry();
            pg2.Figures.Add(fig);
            return pg2;
        }
        if (geometry is GeometryGroup gg)
        {
            var pg2 = new PathGeometry { FillRule = gg.FillRule };
            foreach (var child in gg.Children)
            {
                var flat = GetFlattenedGeometry(child);
                if (flat != null)
                {
                    foreach (var fig in flat.Figures)
                        pg2.Figures.Add(fig);
                }
            }
            return pg2;
        }
        // Try generic flattening
        try { return geometry.GetFlattenedPathGeometry(); }
        catch { return null; }
    }

    /// <summary>
    /// Extracts a representative BGRA color from any brush type.
    /// For SolidColorBrush: exact color. For gradients: first stop color.
    /// This ensures gradient-filled SVG elements render with an approximate
    /// color instead of being completely invisible.
    /// </summary>
    private static (byte B, byte G, byte R, byte A) ExtractBrushColor(Brush brush, double opacity)
    {
        if (brush is SolidColorBrush solid)
        {
            var c = solid.Color;
            var a = (byte)(c.A * opacity * solid.Opacity);
            return (c.B, c.G, c.R, a);
        }

        if (brush is LinearGradientBrush lgb && lgb.GradientStops.Count > 0)
        {
            // Use the first gradient stop as approximate color
            var stop = lgb.GradientStops[0];
            var c = stop.Color;
            var a = (byte)(c.A * opacity * lgb.Opacity);
            return (c.B, c.G, c.R, a);
        }

        if (brush is RadialGradientBrush rgb && rgb.GradientStops.Count > 0)
        {
            var stop = rgb.GradientStops[0];
            var c = stop.Color;
            var a = (byte)(c.A * opacity * rgb.Opacity);
            return (c.B, c.G, c.R, a);
        }

        // Unknown brush type — render as opaque black as last resort
        return (0, 0, 0, (byte)(255 * opacity));
    }

    #region Scanline Fill

    private static void FillGeometry(PathGeometry geometry, SoftwareRenderContext ctx,
        byte b, byte g, byte r, byte a)
    {
        // Collect ALL edges from ALL figures into one list so that compound paths
        // (e.g. a triangle with an exclamation-mark hole) are filled correctly.
        // Each figure's edges are stored as a contiguous sub-list of (start, end) pairs.
        var allContours = new List<List<(float X, float Y)>>();
        foreach (var figure in geometry.Figures)
        {
            if (!figure.IsFilled) continue;

            var points = GetTransformedPoints(figure, ctx);
            if (points.Count < 3) continue;
            allContours.Add(points);
        }

        if (allContours.Count == 0) return;

        // Single figure: simple scanline fill (no compound path interaction)
        if (allContours.Count == 1)
        {
            ScanlineFill(ctx.Pixels, ctx.Width, ctx.Height, ctx.Stride, allContours[0], b, g, r, a);
            return;
        }

        // Multiple figures: compound scanline fill with fill-rule support
        bool useNonZero = geometry.FillRule == FillRule.Nonzero;
        CompoundScanlineFill(ctx.Pixels, ctx.Width, ctx.Height, ctx.Stride,
            allContours, useNonZero, b, g, r, a);
    }

    private static void StrokeGeometry(PathGeometry geometry, SoftwareRenderContext ctx,
        byte b, byte g, byte r, byte a, double strokeWidth)
    {
        var halfWidth = strokeWidth * Math.Max(ctx.ScaleX, ctx.ScaleY) * 0.5;
        if (halfWidth < 0.5) halfWidth = 0.5;

        foreach (var figure in geometry.Figures)
        {
            var points = GetTransformedPoints(figure, ctx);
            if (points.Count < 2) continue;

            for (int i = 0; i < points.Count - 1; i++)
            {
                DrawThickLine(ctx.Pixels, ctx.Width, ctx.Height, ctx.Stride,
                    points[i], points[i + 1], halfWidth, b, g, r, a);
            }
            if (figure.IsClosed && points.Count > 2)
            {
                DrawThickLine(ctx.Pixels, ctx.Width, ctx.Height, ctx.Stride,
                    points[^1], points[0], halfWidth, b, g, r, a);
            }
        }
    }

    private static List<(float X, float Y)> GetTransformedPoints(PathFigure figure, SoftwareRenderContext ctx)
    {
        var points = new List<(float X, float Y)>();
        var start = ctx.TransformPoint(figure.StartPoint);
        points.Add(start);

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
                // Flatten cubic Bézier to line segments (should have been
                // flattened by GetFlattenedGeometry, but handle as safety net)
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
                // Approximate arc with line to endpoint (arcs should be
                // flattened by GetFlattenedGeometry before reaching here)
                points.Add(ctx.TransformPoint(arc.Point));
                current = arc.Point;
            }
        }

        return points;
    }

    private static void FlattenCubicBezier(List<(float X, float Y)> points, SoftwareRenderContext ctx,
        Point p0, Point p1, Point p2, Point p3, int depth = 0)
    {
        const int maxDepth = 8;
        const double tolerance = 0.5;

        if (depth >= maxDepth)
        {
            points.Add(ctx.TransformPoint(p3));
            return;
        }

        // Flatness check: measure control point deviation from chord
        double dx = p3.X - p0.X, dy = p3.Y - p0.Y;
        double d1 = Math.Abs((p1.X - p3.X) * dy - (p1.Y - p3.Y) * dx);
        double d2 = Math.Abs((p2.X - p3.X) * dy - (p2.Y - p3.Y) * dx);
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6 || (d1 + d2) / len <= tolerance)
        {
            points.Add(ctx.TransformPoint(p3));
            return;
        }

        // De Casteljau subdivision at t=0.5
        var m01 = Mid(p0, p1); var m12 = Mid(p1, p2); var m23 = Mid(p2, p3);
        var m012 = Mid(m01, m12); var m123 = Mid(m12, m23);
        var mid = Mid(m012, m123);

        FlattenCubicBezier(points, ctx, p0, m01, m012, mid, depth + 1);
        FlattenCubicBezier(points, ctx, mid, m123, m23, p3, depth + 1);
    }

    private static void FlattenQuadBezier(List<(float X, float Y)> points, SoftwareRenderContext ctx,
        Point p0, Point p1, Point p2, int depth = 0)
    {
        const int maxDepth = 8;
        const double tolerance = 0.5;

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

    /// <summary>
    /// Scanline fill using the even-odd rule (sufficient for most SVG content).
    /// </summary>
    private static void ScanlineFill(byte[] pixels, int width, int height, int stride,
        List<(float X, float Y)> points, byte b, byte g, byte r, byte a)
    {
        // Find Y bounds
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in points)
        {
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        int yStart = Math.Max(0, (int)minY);
        int yEnd = Math.Min(height - 1, (int)maxY);

        var intersections = new List<float>();

        for (int y = yStart; y <= yEnd; y++)
        {
            float scanY = y + 0.5f;
            intersections.Clear();

            // Find intersections with all edges
            for (int i = 0; i < points.Count; i++)
            {
                var p0 = points[i];
                var p1 = points[(i + 1) % points.Count];

                if ((p0.Y <= scanY && p1.Y > scanY) || (p1.Y <= scanY && p0.Y > scanY))
                {
                    float t = (scanY - p0.Y) / (p1.Y - p0.Y);
                    intersections.Add(p0.X + t * (p1.X - p0.X));
                }
            }

            intersections.Sort();

            // Fill between pairs (even-odd rule)
            for (int i = 0; i + 1 < intersections.Count; i += 2)
            {
                int xStart = Math.Max(0, (int)intersections[i]);
                int xEnd = Math.Min(width - 1, (int)intersections[i + 1]);

                for (int x = xStart; x <= xEnd; x++)
                {
                    BlendPixel(pixels, stride, x, y, b, g, r, a);
                }
            }
        }
    }

    /// <summary>
    /// Compound scanline fill for multiple contours with fill-rule support.
    /// All edges from all contours are tested against each scanline together,
    /// so inner contours act as holes (via even-odd or nonzero winding).
    /// </summary>
    private static void CompoundScanlineFill(byte[] pixels, int width, int height, int stride,
        List<List<(float X, float Y)>> contours, bool nonZero,
        byte b, byte g, byte r, byte a)
    {
        // Compute global Y bounds
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var pts in contours)
        {
            foreach (var p in pts)
            {
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        int yStart = Math.Max(0, (int)minY);
        int yEnd = Math.Min(height - 1, (int)maxY);

        // For nonzero fill rule, store (x, direction) pairs
        var intersections = new List<(float x, int dir)>();

        for (int y = yStart; y <= yEnd; y++)
        {
            float scanY = y + 0.5f;
            intersections.Clear();

            // Collect intersections from ALL contours
            foreach (var pts in contours)
            {
                int count = pts.Count;
                for (int i = 0; i < count; i++)
                {
                    var p0 = pts[i];
                    var p1 = pts[(i + 1) % count];

                    if ((p0.Y <= scanY && p1.Y > scanY) || (p1.Y <= scanY && p0.Y > scanY))
                    {
                        float t = (scanY - p0.Y) / (p1.Y - p0.Y);
                        float x = p0.X + t * (p1.X - p0.X);
                        int dir = (p1.Y > p0.Y) ? 1 : -1; // upward or downward crossing
                        intersections.Add((x, dir));
                    }
                }
            }

            // Sort by X
            intersections.Sort((a2, b2) => a2.x.CompareTo(b2.x));

            if (nonZero)
            {
                // NonZero winding rule: track winding number across intersections
                int winding = 0;
                float spanStartX = 0;
                bool inFill = false;
                for (int i = 0; i < intersections.Count; i++)
                {
                    int prevWinding = winding;
                    winding += intersections[i].dir;

                    if (prevWinding == 0 && winding != 0)
                    {
                        // Entering filled region
                        spanStartX = intersections[i].x;
                        inFill = true;
                    }
                    else if (prevWinding != 0 && winding == 0 && inFill)
                    {
                        // Leaving filled region — fill the span
                        int xStart2 = Math.Max(0, (int)spanStartX);
                        int xEnd2 = Math.Min(width - 1, (int)intersections[i].x);
                        for (int x = xStart2; x <= xEnd2; x++)
                        {
                            BlendPixel(pixels, stride, x, y, b, g, r, a);
                        }
                        inFill = false;
                    }
                }
            }
            else
            {
                // Even-odd rule: fill between pairs
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    int xStart2 = Math.Max(0, (int)intersections[i].x);
                    int xEnd2 = Math.Min(width - 1, (int)intersections[i + 1].x);

                    for (int x = xStart2; x <= xEnd2; x++)
                    {
                        BlendPixel(pixels, stride, x, y, b, g, r, a);
                    }
                }
            }
        }
    }

    private static void DrawThickLine(byte[] pixels, int width, int height, int stride,
        (float X, float Y) p0, (float X, float Y) p1, double halfWidth,
        byte b, byte g, byte r, byte a)
    {
        // Compute perpendicular offset
        var dx = p1.X - p0.X;
        var dy = p1.Y - p0.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) return;

        var nx = (float)(-dy / len * halfWidth);
        var ny = (float)(dx / len * halfWidth);

        // Build quad polygon
        var quad = new List<(float X, float Y)>(4)
        {
            (p0.X + nx, p0.Y + ny),
            (p1.X + nx, p1.Y + ny),
            (p1.X - nx, p1.Y - ny),
            (p0.X - nx, p0.Y - ny)
        };

        ScanlineFill(pixels, width, height, stride, quad, b, g, r, a);
    }

    private static void BlendPixel(byte[] pixels, int stride, int x, int y, byte b, byte g, byte r, byte a)
    {
        var offset = y * stride + x * 4;
        if (offset + 3 >= pixels.Length) return;

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
            // Alpha blend (premultiplied) — clamp to [0,255] to prevent overflow
            var srcA = a / 255f;
            var invA = 1f - srcA;
            pixels[offset] = (byte)Math.Min(255, (int)(b * srcA + pixels[offset] * invA));
            pixels[offset + 1] = (byte)Math.Min(255, (int)(g * srcA + pixels[offset + 1] * invA));
            pixels[offset + 2] = (byte)Math.Min(255, (int)(r * srcA + pixels[offset + 2] * invA));
            pixels[offset + 3] = (byte)Math.Min(255, (int)(a + pixels[offset + 3] * invA));
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
        public readonly double ScaleX;
        public readonly double ScaleY;
        public readonly double OffsetX;
        public readonly double OffsetY;
        public readonly double Opacity;
        // Combined transform matrix (2D affine)
        public readonly double M11, M12, M21, M22, Dx, Dy;

        public SoftwareRenderContext(byte[] pixels, int width, int height, int stride,
            double scaleX, double scaleY, double offsetX, double offsetY)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            Stride = stride;
            ScaleX = scaleX;
            ScaleY = scaleY;
            OffsetX = offsetX;
            OffsetY = offsetY;
            Opacity = 1.0;
            M11 = scaleX; M12 = 0;
            M21 = 0; M22 = scaleY;
            Dx = offsetX; Dy = offsetY;
        }

        private SoftwareRenderContext(byte[] pixels, int width, int height, int stride,
            double scaleX, double scaleY, double offsetX, double offsetY, double opacity,
            double m11, double m12, double m21, double m22, double dx, double dy)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            Stride = stride;
            ScaleX = scaleX;
            ScaleY = scaleY;
            OffsetX = offsetX;
            OffsetY = offsetY;
            Opacity = opacity;
            M11 = m11; M12 = m12;
            M21 = m21; M22 = m22;
            Dx = dx; Dy = dy;
        }

        public SoftwareRenderContext WithTransform(Matrix m)
        {
            // Compose: current * m
            var nm11 = M11 * m.M11 + M12 * m.M21;
            var nm12 = M11 * m.M12 + M12 * m.M22;
            var nm21 = M21 * m.M11 + M22 * m.M21;
            var nm22 = M21 * m.M12 + M22 * m.M22;
            var ndx = Dx + M11 * m.OffsetX + M12 * m.OffsetY;
            var ndy = Dy + M21 * m.OffsetX + M22 * m.OffsetY;

            return new SoftwareRenderContext(Pixels, Width, Height, Stride,
                ScaleX, ScaleY, OffsetX, OffsetY, Opacity,
                nm11, nm12, nm21, nm22, ndx, ndy);
        }

        public SoftwareRenderContext WithOpacity(double opacity)
        {
            return new SoftwareRenderContext(Pixels, Width, Height, Stride,
                ScaleX, ScaleY, OffsetX, OffsetY, opacity,
                M11, M12, M21, M22, Dx, Dy);
        }

        public (float X, float Y) TransformPoint(Point p)
        {
            return (
                (float)(M11 * p.X + M12 * p.Y + Dx),
                (float)(M21 * p.X + M22 * p.Y + Dy)
            );
        }
    }

    #endregion
}
