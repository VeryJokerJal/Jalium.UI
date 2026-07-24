using System.Reflection;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class GeometryPathParityTests
{
    [Fact]
    public void GeometryExposesWpfToleranceContainmentAndRenderBoundsSurface()
    {
        Assert.Equal(0.25, Geometry.StandardFlatteningTolerance);
        Assert.NotNull(Geometry.TransformProperty);

        var outer = new RectangleGeometry(new Rect(0, 0, 20, 20));
        var inner = new RectangleGeometry(new Rect(5, 5, 4, 4));
        var pen = new Pen(new SolidColorBrush(Color.Black), 4);

        Assert.True(outer.FillContains(inner));
        Assert.True(outer.FillContains(inner, 0.1, ToleranceType.Absolute));
        Assert.Equal(IntersectionDetail.FullyContains,
            outer.FillContainsWithDetail(inner, 0.1, ToleranceType.Relative));
        Assert.True(outer.StrokeContains(pen, new Point(0, 10), 0.1, ToleranceType.Absolute));
        Assert.Equal(new Rect(-2, -2, 24, 24), outer.GetRenderBounds(pen));
        Assert.Throws<ArgumentOutOfRangeException>(() => outer.GetArea(-1, ToleranceType.Absolute));

        PathGeometry combined = Geometry.Combine(
            outer, inner, GeometryCombineMode.Exclude, null, 0.05, ToleranceType.Absolute);
        Assert.Equal(FillRule.Nonzero, combined.FillRule);
    }

    [Fact]
    public void ShapeConstructorsDependencyPropertiesAndTypedClonesAreFunctional()
    {
        var transform = new MatrixTransform(new Matrix(1, 0, 0, 1, 3, 4));
        var rectangle = new RectangleGeometry(new Rect(0, 0, 12, 8), 2, 2, transform);
        var ellipse = new EllipseGeometry(new Point(4, 5), 3, 2, transform);
        var ellipseFromRect = new EllipseGeometry(new Rect(0, 0, 8, 6));
        var line = new LineGeometry(new Point(1, 2), new Point(8, 9), transform);

        Assert.NotNull(RectangleGeometry.RectProperty);
        Assert.NotNull(EllipseGeometry.CenterProperty);
        Assert.NotNull(LineGeometry.StartPointProperty);
        Assert.Equal(12 * 8 - (4 - Math.PI) * 4,
            rectangle.GetArea(0.1, ToleranceType.Absolute), 8);
        Assert.Equal(Math.PI * 6, ellipse.GetArea(0.1, ToleranceType.Absolute), 8);
        Assert.Equal(new Point(4, 3), ellipseFromRect.Center);
        Assert.Equal(0, line.GetArea(0.1, ToleranceType.Absolute));

        Assert.IsType<RectangleGeometry>(rectangle.Clone());
        Assert.IsType<EllipseGeometry>(ellipse.CloneCurrentValue());
        Assert.IsType<LineGeometry>(line.Clone());

        rectangle.Freeze();
        Assert.Throws<InvalidOperationException>(() => rectangle.Rect = new Rect(1, 1, 1, 1));
    }

    [Fact]
    public void GeometryCombineModeValuesMatchWpf()
    {
        Assert.Equal(0, (int)GeometryCombineMode.Union);
        Assert.Equal(1, (int)GeometryCombineMode.Intersect);
        Assert.Equal(2, (int)GeometryCombineMode.Xor);
        Assert.Equal(3, (int)GeometryCombineMode.Exclude);
    }

    [Fact]
    public void RoundedRectangleFillContainsUsesAnalyticAllocationFreeCornerTests()
    {
        var elliptical = new RectangleGeometry(new Rect(0, 0, 100, 60), 20, 10);
        Assert.False(elliptical.FillContains(new Point(1, 1)));
        Assert.True(elliptical.FillContains(new Point(20, 0)));
        Assert.True(elliptical.FillContains(new Point(50, 30)));
        Assert.False(elliptical.FillContains(new Point(-1, 30)));

        var perCorner = new RectangleGeometry(
            new Rect(0, 0, 100, 100),
            new CornerRadius(20, 0, 10, 5));
        Assert.False(perCorner.FillContains(new Point(0, 0)));
        Assert.True(perCorner.FillContains(new Point(100, 0)));
        Assert.False(perCorner.FillContains(new Point(100, 100)));
        Assert.False(perCorner.FillContains(new Point(0, 100)));
        Assert.True(perCorner.FillContains(new Point(50, 50)));

        // Adjacent oversized radii are normalized to the available bounds.
        var normalized = new RectangleGeometry(
            new Rect(0, 0, 100, 40),
            new CornerRadius(80));
        Assert.False(normalized.FillContains(new Point(0, 0)));
        Assert.True(normalized.FillContains(new Point(20, 0)));

        _ = perCorner.FillContains(new Point(7, 7));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            _ = perCorner.FillContains(new Point(i % 101, (i * 7) % 101));
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void PathCollectionsAreAnimatableDeepCloneAndFreezeChildren()
    {
        var segment = new BezierSegment(
            new Point(2, 0), new Point(8, 10), new Point(10, 10), true)
        {
            IsSmoothJoin = true,
        };
        var figure = new PathFigure(new Point(0, 0), new PathSegment[] { segment }, true);
        var geometry = new PathGeometry(new[] { figure }, FillRule.Nonzero, null);

        Assert.IsAssignableFrom<Jalium.UI.Media.Animation.Animatable>(geometry.Figures);
        Assert.IsType<PathSegmentCollection>(figure.Segments);
        Assert.True(figure.MayHaveCurves());

        PathGeometry clone = geometry.Clone();
        Assert.NotSame(geometry.Figures, clone.Figures);
        Assert.NotSame(geometry.Figures[0], clone.Figures[0]);
        Assert.NotSame(geometry.Figures[0].Segments[0], clone.Figures[0].Segments[0]);
        Assert.True(((BezierSegment)clone.Figures[0].Segments[0]).IsSmoothJoin);

        geometry.Freeze();
        Assert.True(geometry.Figures.IsFrozen);
        Assert.True(figure.IsFrozen);
        Assert.True(segment.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => geometry.Figures.Add(new PathFigure()));
    }

    [Fact]
    public void PathFigureFormattingParsingAndFlatteningRoundTrip()
    {
        var figures = PathFigureCollection.Parse("M 0,0 C 2,0 8,10 10,10 Z");
        PathFigure figure = Assert.Single(figures);

        Assert.True(figure.MayHaveCurves());
        PathFigure flattened = figure.GetFlattenedPathFigure(0.05, ToleranceType.Absolute);
        Assert.DoesNotContain(flattened.Segments, static segment => segment is BezierSegment);

        string text = figures.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PathFigureCollection reparsed = PathFigureCollection.Parse(text);
        Assert.Single(reparsed);
        Assert.True(reparsed[0].IsClosed);

        var converter = new PathFigureCollectionConverter();
        Assert.True(converter.CanConvertFrom(null, typeof(string)));
        Assert.IsType<PathFigureCollection>(converter.ConvertFromInvariantString("M0,0 L1,1"));
    }

    [Fact]
    public void SegmentDependencyPropertiesPointCollectionsAndTypedClonesMatchWpfShape()
    {
        var points = new PointCollection(new[] { new Point(1, 2), new Point(3, 4) });
        var polyLine = new PolyLineSegment(points, true) { IsSmoothJoin = true };
        var polyBezier = new PolyBezierSegment(
            new[] { new Point(1, 0), new Point(2, 1), new Point(3, 0) }, true);
        var polyQuadratic = new PolyQuadraticBezierSegment(
            new[] { new Point(2, 2), new Point(4, 0) }, true);
        var arc = new ArcSegment(
            new Point(10, 10), new Size(5, 6), 30, true, SweepDirection.Clockwise, true);

        Assert.IsType<PointCollection>(polyLine.Points);
        Assert.NotNull(PolyLineSegment.PointsProperty);
        Assert.NotNull(ArcSegment.SweepDirectionProperty);
        Assert.IsType<PolyLineSegment>(polyLine.Clone());
        Assert.IsType<PolyBezierSegment>(polyBezier.CloneCurrentValue());
        Assert.IsType<PolyQuadraticBezierSegment>(polyQuadratic.Clone());
        Assert.IsType<ArcSegment>(arc.CloneCurrentValue());
        Assert.True(polyLine.Clone().IsSmoothJoin);

        PointCollection clonedPoints = points.Clone();
        clonedPoints[0] = new Point(50, 50);
        Assert.Equal(new Point(1, 2), points[0]);
    }

    [Fact]
    public void GeometryGroupCombinedAndStreamGeometryUseTypedFreezableContracts()
    {
        var first = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var second = new EllipseGeometry(new Rect(2, 2, 4, 4));
        var group = new GeometryGroup
        {
            Children = new GeometryCollection(2) { first, second },
            FillRule = FillRule.Nonzero,
        };
        var combined = new CombinedGeometry(GeometryCombineMode.Intersect, first, second);

        Assert.IsType<GeometryGroup>(group.Clone());
        Assert.IsType<CombinedGeometry>(combined.CloneCurrentValue());
        Assert.NotNull(GeometryGroup.ChildrenProperty);
        Assert.NotNull(CombinedGeometry.GeometryCombineModeProperty);

        var stream = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext context = stream.Open())
        {
            context.BeginFigure(new Point(0, 0), true, true);
            context.LineTo(new Point(10, 0), true, false);
            context.LineTo(new Point(10, 10), true, false);
        }

        StreamGeometry streamClone = stream.Clone();
        Assert.False(streamClone.IsEmpty());
        Assert.Equal(FillRule.Nonzero, streamClone.FillRule);
        Assert.NotNull(StreamGeometry.FillRuleProperty);

        MethodInfo createInstance = typeof(StreamGeometry).GetMethod(
            "CreateInstanceCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(typeof(StreamGeometry), createInstance.DeclaringType);

        stream.Freeze();
        Assert.Throws<InvalidOperationException>(() => stream.Open());
    }
}
