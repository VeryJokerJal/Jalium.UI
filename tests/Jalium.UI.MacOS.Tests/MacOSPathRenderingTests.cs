using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using ShapePath = Jalium.UI.Shapes.Path;

namespace Jalium.UI.Tests;

public sealed class MacOSPathRenderingTests : MacOSGeometryTestBase
{
    [Fact]
    public void RecordingPreservesEdgeModeAndCapturedGeometry()
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(1, 2) };
        figure.Segments.Add(new LineSegment(new Point(30, 40)));
        geometry.Figures.Add(figure);
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder();
        recorder.DrawGeometry(Brushes.Teal, null, geometry, EdgeMode.Aliased);
        var recorded = host.FinishRecord(recorder);
        figure.StartPoint = new Point(90, 90);
        var sink = new GeometrySink();
        host.Replay(recorded, sink);
        Assert.Equal(EdgeMode.Aliased, sink.Edge);
        Assert.Equal(new Point(1, 2), Assert.IsType<PathGeometry>(sink.Geometry).Figures[0].StartPoint);
    }

    [Fact]
    public void PathResolvesInheritedAndLocalEdgeMode()
    {
        var path = new ShapePath { Data = "M0,0 L8,0 L8,8 Z", Fill = Brushes.Teal, Width = 8, Height = 8 };
        var parent = new Border { Child = path };
        parent.Measure(new Size(8, 8)); parent.Arrange(new Rect(0, 0, 8, 8));
        var sink = new GeometrySink();
        path.Render(sink);
        Assert.Equal(RenderOptions.DefaultEdgeMode, sink.Edge);
        RenderOptions.SetEdgeMode(parent, EdgeMode.Aliased);
        path.Render(sink);
        Assert.Equal(EdgeMode.Aliased, sink.Edge);
        RenderOptions.SetEdgeMode(path, EdgeMode.Antialiased);
        path.Render(sink);
        Assert.Equal(EdgeMode.Antialiased, sink.Edge);
    }

    [Theory]
    [InlineData(0, 1, -1, 0)]
    [InlineData(2, 0.3, 0.6, 1)]
    [InlineData(-2, 0, 0, 1)]
    public void AffineArcBakePreservesEllipseAndSegmentFlags(double m11, double m12, double m21, double m22)
    {
        var matrix = new Matrix(m11, m12, m21, m22, 7, 11);
        var arc = new ArcSegment(new Point(16, 15), new Size(12, 5), 30, false, SweepDirection.Clockwise, false)
            { IsSmoothJoin = true };
        var source = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(2, 3), IsFilled = false };
        figure.Segments.Add(arc); source.Figures.Add(figure);
        var clone = (PathGeometry)typeof(ShapePath).GetMethod("ClonePathGeometry", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(PathGeometry), typeof(Matrix) }, null)!
            .Invoke(null, new object[] { source, matrix })!;
        var actual = Assert.IsType<ArcSegment>(Assert.Single(Assert.Single(clone.Figures).Segments));
        Assert.False(actual.IsStroked); Assert.True(actual.IsSmoothJoin);
        Assert.Equal(matrix.Transform(arc.Point), actual.Point);
        Assert.Equal(new Size(12, 5), arc.Size);
        Assert.Equal(matrix.Determinant < 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise,
            actual.SweepDirection);
        var angle = Math.PI / 6;
        var u = matrix.Transform(new Point(12 * Math.Cos(angle), 12 * Math.Sin(angle))) - new Point(7, 11);
        var v = matrix.Transform(new Point(-5 * Math.Sin(angle), 5 * Math.Cos(angle))) - new Point(7, 11);
        var phi = actual.RotationAngle * Math.PI / 180;
        double ux = actual.Size.Width*Math.Cos(phi), uy = actual.Size.Width*Math.Sin(phi);
        double vx = -actual.Size.Height*Math.Sin(phi), vy = actual.Size.Height*Math.Cos(phi);
        Assert.Equal(u.X*u.X+v.X*v.X, ux*ux+vx*vx, 8);
        Assert.Equal(u.X*u.Y+v.X*v.Y, ux*uy+vx*vy, 8);
        Assert.Equal(u.Y*u.Y+v.Y*v.Y, uy*uy+vy*vy, 8);
    }

    [Fact]
    public void CompoundGroupBakesChildTransformsAndPreservesUnfilledFigures()
    {
        var path = new PathGeometry { Transform = new TranslateTransform(3, 4) };
        var figure = new PathFigure { StartPoint = new Point(1, 2), IsFilled = false, IsClosed = false };
        figure.Segments.Add(new LineSegment(new Point(5, 6), false) { IsSmoothJoin = true });
        path.Figures.Add(figure);
        var child = new GeometryGroup { Transform = new ScaleTransform(2, 3) };
        child.Children.Add(path);
        var root = new GeometryGroup { FillRule = FillRule.Nonzero, Transform = new TranslateTransform(100, 100) };
        root.Children.Add(child);
        var result = RenderTargetDrawingContext.FlattenGeometryGroup(root);
        Assert.Equal(FillRule.Nonzero, result.FillRule);
        var actual = Assert.Single(result.Figures);
        Assert.Equal(new Point(8, 18), actual.StartPoint);
        Assert.False(actual.IsFilled);
        var line = Assert.IsType<LineSegment>(Assert.Single(actual.Segments));
        Assert.Equal(new Point(16, 30), line.Point);
        Assert.False(line.IsStroked); Assert.True(line.IsSmoothJoin);
    }

    [Fact]
    public void CurveFlatteningKeepsSmoothInternalJoinsAndUnstrokedEdges()
    {
        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(0, 0) };
        figure.Segments.Add(new BezierSegment(new Point(0, 100), new Point(100, 100), new Point(100, 0), false));
        path.Figures.Add(figure);
        var flat = path.GetFlattenedPathGeometry(0.125, ToleranceType.Absolute);
        Assert.True(flat.Figures[0].Segments.Count > 4);
        Assert.All(flat.Figures[0].Segments, s => Assert.False(s.IsStroked));
        Assert.False(flat.Figures[0].Segments[0].IsSmoothJoin);
        Assert.All(flat.Figures[0].Segments.Skip(1), s => Assert.True(s.IsSmoothJoin));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DegenerateArcOmitsIdenticalEndpointsAndPreservesZeroRadiusLine(bool identical)
    {
        var start=new Point(10,10);
        var end=identical ? start : new Point(60,30);
        var figure=new PathFigure { StartPoint=start };
        figure.Segments.Add(new ArcSegment(end,new Size(identical?20:0,12),30,true,SweepDirection.Clockwise,true));
        var geometry=new PathGeometry(); geometry.Figures.Add(figure);
        var flat=geometry.GetFlattenedPathGeometry(0.125,ToleranceType.Absolute);
        if(identical) Assert.Empty(flat.Figures[0].Segments);
        else Assert.Equal(end,Assert.IsType<LineSegment>(Assert.Single(flat.Figures[0].Segments)).Point);
    }

    [Fact]
    public void LargeEllipseAndArcSubdivisionRespectSagittaTolerance()
    {
        const double radius=10000,tolerance=0.125;
        var ellipse=new EllipseGeometry(new Point(0,0),radius,radius);
        var flat=ellipse.GetFlattenedPathGeometry(tolerance,ToleranceType.Absolute);
        var points=flat.Figures[0].Segments.Cast<LineSegment>().Select(s=>s.Point).Prepend(flat.Figures[0].StartPoint).ToArray();
        Assert.InRange(points.Length, 257, 4096);
        for(int i=1;i<points.Length;++i)
        {
            var midpoint=new Point((points[i-1].X+points[i].X)/2,(points[i-1].Y+points[i].Y)/2);
            Assert.InRange(radius-Math.Sqrt(midpoint.X*midpoint.X+midpoint.Y*midpoint.Y),0,tolerance*1.001);
        }
        var arcGeometry=new PathGeometry();
        var figure=new PathFigure {StartPoint=new Point(radius,0)};
        figure.Segments.Add(new ArcSegment(new Point(-radius,0),new Size(radius,radius),0,false,SweepDirection.Clockwise,true));
        arcGeometry.Figures.Add(figure);
        var arc=arcGeometry.GetFlattenedPathGeometry(tolerance,ToleranceType.Absolute).Figures[0];
        var previous=arc.StartPoint;
        foreach(var line in arc.Segments.Cast<LineSegment>())
        {
            var midpoint=new Point((previous.X+line.Point.X)/2,(previous.Y+line.Point.Y)/2);
            Assert.InRange(radius-Math.Sqrt(midpoint.X*midpoint.X+midpoint.Y*midpoint.Y),0,tolerance*1.001);
            previous=line.Point;
        }
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    public void PrimitivePathDataKeepsCurvesAndGeometryTransform(int kind, int arcCount)
    {
        Geometry geometry = kind switch
        {
            0 => new EllipseGeometry(new Point(30, 30), 20, 12),
            1 => new RectangleGeometry(new Rect(10, 10, 40, 24), 8, 5),
            2 => new RectangleGeometry(new Rect(10, 10, 40, 24)),
            _ => new LineGeometry(new Point(10, 10), new Point(50, 34)),
        };
        var transform = new MatrixTransform(new Matrix(1.2, 0.3, -0.2, 0.8, 4, 7));
        geometry.Transform = transform;
        var path = new ShapePath { Data = geometry, Fill = Brushes.Teal, Stroke = Brushes.Black,
            StrokeThickness = 2, Width = 100, Height = 100, Stretch = Stretch.None };
        RenderOptions.SetEdgeMode(path, EdgeMode.Aliased);
        path.Measure(new Size(100, 100)); path.Arrange(new Rect(0, 0, 100, 100));
        var sink = new GeometrySink(); path.Render(sink);
        var rendered = Assert.IsType<PathGeometry>(sink.Geometry);
        Assert.Equal(transform.Value, rendered.Transform!.Value);
        Assert.Equal(EdgeMode.Aliased, sink.Edge);
        var figure = Assert.Single(rendered.Figures);
        Assert.Equal(kind != 3, figure.IsFilled);
        Assert.Equal(arcCount, figure.Segments.OfType<ArcSegment>().Count());
        Assert.All(figure.Segments.OfType<ArcSegment>(), arc => Assert.True(arc.IsSmoothJoin));
        Assert.Same(geometry, path.Data);
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(0, 8)]
    public void SingleZeroRoundedRadiusProducesFiniteSquareCorners(double rx, double ry)
    {
        var rectangle = new RectangleGeometry(new Rect(0, 0, 40, 24), rx, ry);
        var flat = rectangle.GetFlattenedPathGeometry(0.125, ToleranceType.Absolute);
        var figure = Assert.Single(flat.Figures);
        Assert.Equal(3, figure.Segments.Count);
        Assert.All(figure.Segments.Cast<LineSegment>(), line =>
        {
            Assert.True(double.IsFinite(line.Point.X) && double.IsFinite(line.Point.Y));
            Assert.False(line.IsSmoothJoin);
        });
        Assert.Equal(rectangle.Rect, flat.Bounds);
    }

    [Fact]
    public void LargeRoundedRectangleUsesBoundedSmoothSagittaSubdivision()
    {
        const double radius = 10000, tolerance = 0.125;
        var flat = new RectangleGeometry(new Rect(-10010, -10010, 20020, 20020), radius, radius)
            .GetFlattenedPathGeometry(tolerance, ToleranceType.Absolute);
        var figure = Assert.Single(flat.Figures);
        Assert.InRange(figure.Segments.Count, 257, 4096);
        Assert.All(figure.Segments, segment => Assert.True(segment.IsSmoothJoin));
        Assert.Equal(figure.StartPoint, Assert.IsType<LineSegment>(figure.Segments[^1]).Point);
        var previous = figure.StartPoint;
        foreach (var line in figure.Segments.Cast<LineSegment>())
        {
            if (Math.Abs(previous.X) > 10 && Math.Abs(previous.Y) > 10 &&
                Math.Abs(line.Point.X) > 10 && Math.Abs(line.Point.Y) > 10)
            {
                var midpoint = new Point((previous.X + line.Point.X) / 2, (previous.Y + line.Point.Y) / 2);
                var cx = Math.CopySign(10, midpoint.X); var cy = Math.CopySign(10, midpoint.Y);
                var distance = Math.Sqrt(Math.Pow(midpoint.X - cx, 2) + Math.Pow(midpoint.Y - cy, 2));
                Assert.InRange(radius - distance, 0, tolerance * 1.001);
            }
            previous = line.Point;
        }
    }

    private sealed class GeometrySink : DrawingContextAdapter
    {
        public Geometry? Geometry;
        public EdgeMode Edge;
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => Geometry = geometry;
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry, EdgeMode edgeMode)
        { Geometry = geometry; Edge = edgeMode; }
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
