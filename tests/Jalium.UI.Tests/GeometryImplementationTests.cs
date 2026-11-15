using System.Globalization;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class GeometryImplementationTests
{
    public static TheoryData<string> GeometryKinds => new()
    {
        "Rectangle", "Ellipse", "Line", "Path", "Stream", "Group", "Combined", "CssRoundedRectangle"
    };

    private static Geometry Create(string kind) => kind switch
    {
        "Rectangle" => new RectangleGeometry(new Rect(0, 0, 20, 20), 10, 10),
        "Ellipse" => new EllipseGeometry(new Point(10, 10), 10, 10),
        "Line" => new LineGeometry(new Point(0, 20), new Point(20, 0)),
        "Path" => Geometry.Parse("M0 0 C0 20 20 20 20 0 Z"),
        "Stream" => Stream(),
        "Group" => new GeometryGroup { Children = { new EllipseGeometry(new Point(10, 10), 10, 10) } },
        "Combined" => new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, 20, 20)), new RectangleGeometry(new Rect(5, 5, 10, 10))),
        "CssRoundedRectangle" => new CssRoundedRectangleGeometry(new Rect(0, 0, 20, 20),
            new CssUsedBorderRadii(new Size(10, 10), new Size(10, 10), new Size(10, 10), new Size(10, 10))),
        _ => throw new ArgumentException(kind)
    };

    private static StreamGeometry Stream()
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new Point(0, 0), true, true);
        context.BezierTo(new Point(0, 20), new Point(20, 20), new Point(20, 0), true, false);
        return geometry;
    }

    [Theory]
    [MemberData(nameof(GeometryKinds))]
    public void EveryGeometryUsesItsActualFilledRegion(string kind)
    {
        var geometry = Create(kind);
        var inside = kind == "Combined" ? new Point(2, 2) : new Point(10, 5);
        var outside = kind is "Path" or "Stream" ? new Point(1, 14) :
            kind == "Combined" ? new Point(10, 10) : new Point(1, 1);
        Assert.Equal(kind != "Line", geometry.FillContains(inside));
        Assert.False(geometry.FillContains(outside));
        Assert.Equal(kind != "Line", geometry.FillContains(inside, 0.001, ToleranceType.Absolute));
        Assert.False(geometry.FillContains(outside, 0.001, ToleranceType.Absolute));
        Assert.False(geometry.FillContains(new Point(double.NaN, 5)));
    }

    [Theory]
    [MemberData(nameof(GeometryKinds))]
    public void EveryGeometryUsesTheActualStrokeAndKeepsTheInteriorEmpty(string kind)
    {
        var geometry = Create(kind);
        var pen = new Pen(Brushes.Black, 2);
        var edge = kind is "Path" or "Stream" ? new Point(10, 15) : kind == "Line" ? new Point(10, 10) : new Point(0, 10);
        var empty = kind == "Line" ? new Point(1, 1) : new Point(10, 5);
        if (kind == "Combined") empty = new Point(10, 10);
        Assert.True(geometry.StrokeContains(pen, edge));
        Assert.False(geometry.StrokeContains(pen, empty));
        Assert.True(geometry.StrokeContains(pen, edge, 0.001, ToleranceType.Absolute));
        Assert.False(geometry.StrokeContains(pen, empty, 0.001, ToleranceType.Absolute));
    }

    [Theory]
    [MemberData(nameof(GeometryKinds))]
    public void EveryGeometrySupportsIndependentClonesFreezeAndPathConversion(string kind)
    {
        var geometry = Create(kind);
        geometry.Transform = new TranslateTransform(100, 200);
        var clone = geometry.Clone();
        Assert.Equal(geometry.GetType(), clone.GetType());
        Assert.NotSame(geometry.Transform, clone.Transform);
        Assert.Equal(geometry.Bounds, clone.Bounds);
        var path = PathGeometry.CreateFromGeometry(geometry);
        Assert.Equal(geometry.Transform.Value, path.Transform!.Value);
        Assert.NotSame(geometry.Transform, path.Transform);
        Assert.Equal(geometry.Bounds.X, path.Bounds.X, 6);
        Assert.Equal(geometry.Bounds.Y, path.Bounds.Y, 6);
        var serialized = Geometry.Parse(geometry.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(geometry.Bounds.X, serialized.Bounds.X, 6);
        Assert.Equal(geometry.Bounds.Height, serialized.Bounds.Height, 6);
        geometry.Freeze();
        Assert.True(geometry.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => geometry.Transform = null);
        Assert.False(clone.IsFrozen);
        clone.Transform = null;
    }

    [Theory]
    [MemberData(nameof(GeometryKinds))]
    public void EveryGeometryValidatesToleranceAcrossOperations(string kind)
    {
        var geometry = Create(kind);
        var pen = new Pen(Brushes.Black, 2);
        foreach (var tolerance in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => geometry.GetFlattenedPathGeometry(tolerance, ToleranceType.Absolute));
            Assert.Throws<ArgumentOutOfRangeException>(() => geometry.FillContains(new Point(5, 5), tolerance, ToleranceType.Absolute));
            Assert.Throws<ArgumentOutOfRangeException>(() => geometry.GetWidenedPathGeometry(pen, tolerance, ToleranceType.Absolute));
            Assert.Throws<ArgumentOutOfRangeException>(() => geometry.GetOutlinedPathGeometry(tolerance, ToleranceType.Absolute));
            Assert.Throws<ArgumentOutOfRangeException>(() => geometry.GetArea(tolerance, ToleranceType.Absolute));
        }
    }

    [Theory]
    [MemberData(nameof(GeometryKinds))]
    public void CurveDetectionReportsTheUnderlyingSegments(string kind)
    {
        Assert.Equal(kind is not "Line" and not "Combined", Create(kind).MayHaveCurves());
    }

    [Fact]
    public void StreamCachesAnalyticCurveBoundsAndInvalidatesAfterChildMutation()
    {
        var stream = Stream();
        Assert.Equal(new Rect(0, 0, 20, 15), stream.Bounds);
        Assert.True(stream.TryGetComposedCommands(out _, out _, out _, out _, out var bounds, out _, out _, out _));
        Assert.Equal(stream.Bounds, bounds);
        var curve = Assert.IsType<BezierSegment>(stream.GetPathGeometry()!.Figures[0].Segments[0]);
        curve.Point1 = new Point(0, 40);
        curve.Point2 = new Point(20, 40);
        Assert.Equal(30, stream.Bounds.Height, 7);
        Assert.False(stream.TryGetComposedCommands(out _, out _, out _, out _, out _, out _, out _, out _));
        var clone = stream.Clone();
        stream.Freeze();
        Assert.True(stream.GetPathGeometry()!.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => curve.Point1 = default);
        Assert.False(clone.GetPathGeometry()!.IsFrozen);
        clone.Clear();
        Assert.True(clone.IsEmpty());
        using (var context = clone.Open())
        {
            context.BeginFigure(new Point(0, 0), true, false);
            context.QuadraticBezierTo(new Point(10, 20), new Point(20, 0), true, false);
        }
        Assert.Equal(10, clone.Bounds.Height, 7);
        Assert.IsType<QuadraticBezierSegment>(PathGeometry.CreateFromGeometry(clone).Figures[0].Segments[0]);
    }

    [Fact]
    public void FilledIntersectionDetailsRespectHolesConcavityAndOperandTransforms()
    {
        var donut = Create("Combined");
        var hole = new RectangleGeometry(new Rect(7, 7, 3, 3));
        Assert.Equal(IntersectionDetail.Empty, donut.FillContainsWithDetail(hole));
        Assert.False(donut.FillContains(hole));
        var a = Geometry.Parse("M0 0 L20 0 0 20 Z");
        var b = Geometry.Parse("M20 20 L20 2 2 20 Z");
        Assert.Equal(IntersectionDetail.Empty, a.FillContainsWithDetail(b));
        var outer = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var inner = new RectangleGeometry(new Rect(1, 1, 2, 2));
        Assert.Equal(IntersectionDetail.FullyContains, outer.FillContainsWithDetail(inner));
        Assert.Equal(IntersectionDetail.FullyInside, inner.FillContainsWithDetail(outer));
        inner.Transform = new TranslateTransform(20, 0);
        Assert.Equal(IntersectionDetail.Empty, outer.FillContainsWithDetail(inner));
        inner.Transform = new TranslateTransform(7, 0);
        Assert.Equal(IntersectionDetail.FullyContains, outer.FillContainsWithDetail(inner));
        inner.Transform = new TranslateTransform(8, 0);
        Assert.Equal(IntersectionDetail.Intersects, outer.FillContainsWithDetail(inner));
    }

    [Fact]
    public void StrokeIntersectionDoesNotContainTheBoundingRectangleInterior()
    {
        var line = new LineGeometry(new Point(0, 20), new Point(20, 0));
        var pen = new Pen(Brushes.Black, 4);
        Assert.Equal(IntersectionDetail.Empty,
            line.StrokeContainsWithDetail(pen, new RectangleGeometry(new Rect(1, 1, 2, 2))));
        Assert.Equal(IntersectionDetail.FullyContains,
            line.StrokeContainsWithDetail(pen, new RectangleGeometry(new Rect(9, 9, 2, 2))));
    }

    [Fact]
    public void OutliningResolvesSelfIntersectionsAndPreservesTheFilledArea()
    {
        var bow = Geometry.Parse("M0 0 L20 20 0 20 20 0 Z");
        var outlined = bow.GetOutlinedPathGeometry();
        Assert.Equal(2, outlined.Figures.Count);
        Assert.Equal(200, outlined.GetArea(), 7);
        Assert.All(outlined.Figures, figure => Assert.True(figure.IsClosed && figure.IsFilled));
        var donut = Create("Combined").GetOutlinedPathGeometry();
        Assert.False(donut.FillContains(new Point(10, 10)));
        Assert.Equal(300, donut.GetArea(), 7);
    }

    [Fact]
    public void RoundedRectanglesUseTheSamePerCornerContourForAreaHitTestingAndSerialization()
    {
        var geometry = new RectangleGeometry(new Rect(0, 0, 100, 60), new CornerRadius(20, 0, 10, 5));
        var expectedArea = 6000 - (1 - Math.PI / 4) * (400 + 100 + 25);
        Assert.Equal(expectedArea, geometry.GetArea(), 8);
        Assert.Equal(expectedArea, geometry.GetArea(0.01, ToleranceType.Absolute), 8);
        var parsed = Geometry.Parse(geometry.ToString(CultureInfo.InvariantCulture));
        Assert.True(parsed.FillContains(new Point(99.9, 0.1)));
        Assert.False(parsed.FillContains(new Point(0.1, 0.1)));
        Assert.InRange(Math.Abs(expectedArea - parsed.GetArea(0.001, ToleranceType.Absolute)), 0, 0.1);
        var negative = new RectangleGeometry(new Rect(0, 0, 20, 20), -10, -10);
        Assert.False(negative.FillContains(new Point(1, 1)));
        Assert.Equal(100 * Math.PI, negative.GetArea(), 8);
    }

    [Theory]
    [InlineData("M0 0 C0 100 100 100 100 0")]
    [InlineData("M0 0 A100 50 25 0 1 150 20")]
    public void RelativeFlatteningToleranceMatchesItsAbsoluteEquivalent(string data)
    {
        var path = Geometry.Parse(data);
        var absolute = 0.01 * Math.Max(path.Bounds.Width, path.Bounds.Height);
        Assert.Equal(path.GetFlattenedPathGeometry(absolute, ToleranceType.Absolute).ToString(CultureInfo.InvariantCulture),
            path.GetFlattenedPathGeometry(0.01, ToleranceType.Relative).ToString(CultureInfo.InvariantCulture));
        var ellipse = new EllipseGeometry(new Point(0, 0), 100, 50);
        Assert.Equal(ellipse.GetFlattenedPathGeometry(2, ToleranceType.Absolute).ToString(CultureInfo.InvariantCulture),
            ellipse.GetFlattenedPathGeometry(0.01, ToleranceType.Relative).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FractionLengthMeasuresDistanceInsteadOfTheBezierParameter()
    {
        var cubic = (PathGeometry)Geometry.Parse("M0 0 C0 0 0 0 100 0");
        cubic.GetPointAtFractionLength(0.5, out var point, out var tangent);
        Assert.Equal(new Point(50, 0), point);
        Assert.Equal(new Point(1, 0), tangent);
        var poly = new PathGeometry([new PathFigure(new Point(0, 0),
            [new PolyLineSegment([new Point(10, 0), new Point(10, 30)], true)], false)]);
        poly.GetPointAtFractionLength(0.5, out point, out tangent);
        Assert.Equal(new Point(10, 10), point);
        Assert.Equal(new Point(0, 1), tangent);
        Assert.Equal(40, poly.GetTotalLength());
        var repeated = (PathGeometry)Geometry.Parse("M0 0 L10 0 0 0 10 0");
        repeated.GetPointAtFractionLength(0.5, out point, out _);
        Assert.Equal(new Point(5, 0), point);
    }

    [Fact]
    public void FilledBoundariesAreIncludedWithoutResurrectingCancelledContours()
    {
        var path = Geometry.Parse("M0 0 L20 0 20 20 0 20 Z");
        Assert.True(path.FillContains(new Point(20, 20)));
        Assert.True(path.FillContains(new Point(20, 10)));
        var cancelled = Geometry.Parse("M0 0 L20 0 20 20 0 20 Z M0 0 L20 0 20 20 0 20 Z");
        Assert.False(cancelled.FillContains(new Point(20, 20)));
        Assert.False(cancelled.FillContains(new Point(10, 10)));
        var empty = new RectangleGeometry(new Rect(0, 0, 0, 20));
        Assert.False(empty.IsEmpty());
        Assert.False(empty.FillContains(new Point(0, 10)));
    }

    [Fact]
    public void ToleranceOverloadKeepsDirectionalRectangleClipSemantics()
    {
        var rectangle = new RectangleGeometry(new Rect(0, 0, 20, 20))
            { BoundsClipEdges = ClipEdges.Left | ClipEdges.Top | ClipEdges.Right };
        Assert.True(rectangle.FillContains(new Point(10, 30)));
        Assert.True(rectangle.FillContains(new Point(10, 30), 0.1, ToleranceType.Absolute));
        Assert.False(rectangle.FillContains(new Point(-1, 30), 0.1, ToleranceType.Relative));
    }

    [Fact]
    public void DegeneratePrimitivesKeepTheirStrokeWhileHavingNoFilledArea()
    {
        var pen = new Pen(Brushes.Black, 2);
        var rectangle = new RectangleGeometry(new Rect(0, 0, 0, 20));
        Assert.False(rectangle.IsEmpty());
        Assert.Equal(0, rectangle.GetArea());
        Assert.True(rectangle.StrokeContains(pen, new Point(0, 10)));
        var ellipse = new EllipseGeometry(new Point(10, 10), 0, 10);
        Assert.False(ellipse.IsEmpty());
        Assert.False(ellipse.FillContains(new Point(10, 5)));
        Assert.Equal(0, ellipse.GetArea());
        Assert.True(ellipse.StrokeContains(pen, new Point(10, 5)));
    }

    [Fact]
    public void StreamAllowsClearingAndChangingFillRuleAfterItsExposedPathIsFrozen()
    {
        var stream = Stream();
        var original = stream.GetPathGeometry()!;
        original.Freeze();
        stream.FillRule = FillRule.Nonzero;
        Assert.Equal(FillRule.Nonzero, stream.GetPathGeometry()!.FillRule);
        Assert.False(stream.GetPathGeometry()!.IsFrozen);
        Assert.True(original.IsFrozen);
        stream.GetPathGeometry()!.Freeze();
        stream.Clear();
        Assert.True(stream.IsEmpty());
        Assert.False(stream.GetPathGeometry()!.IsFrozen);
    }

    [Fact]
    public void EmptyStreamReopenInvalidatesItsParentsCachedGeometry()
    {
        var stream = Stream();
        var group = new GeometryGroup { Children = { stream } };
        Assert.Equal(15, group.Bounds.Height);
        Assert.True(group.FillContains(new Point(10, 5)));
        using (stream.Open()) { }
        Assert.True(group.Bounds.IsEmpty);
        Assert.False(group.FillContains(new Point(10, 5)));
    }

    [Fact]
    public void FlatteningRetainsCollinearCurveReversalsAndLength()
    {
        var curve = Geometry.Parse("M0 0 C100 0 -100 0 10 0");
        var flat = curve.GetFlattenedPathGeometry(0.001, ToleranceType.Absolute);
        Assert.InRange(Math.Abs(curve.Bounds.Left - flat.Bounds.Left), 0, 0.001);
        Assert.InRange(Math.Abs(curve.Bounds.Right - flat.Bounds.Right), 0, 0.001);
        Assert.InRange(flat.GetTotalLength(), 110, 120);
    }

    [Fact]
    public void LargeCounterclockwiseArcsIncludeEverySweptExtremum()
    {
        var arc = Geometry.Parse("M10 0 A10 10 0 1 0 0 10");
        Assert.Equal(new Rect(-10, -10, 20, 20), arc.Bounds);
        var flat = arc.GetFlattenedPathGeometry(0.0001, ToleranceType.Absolute);
        Assert.InRange(Math.Abs(flat.Bounds.Left + 10), 0, 0.0001);
        Assert.InRange(Math.Abs(flat.Bounds.Top + 10), 0, 0.0001);
    }

    [Theory]
    [InlineData(PenLineCap.Flat, false, false)]
    [InlineData(PenLineCap.Square, true, true)]
    [InlineData(PenLineCap.Round, true, false)]
    [InlineData(PenLineCap.Triangle, true, false)]
    public void WideningRespectsEachCapShape(PenLineCap cap, bool axial, bool corner)
    {
        var line = new LineGeometry(new Point(0, 0), new Point(20, 0));
        var pen = new Pen(Brushes.Black, 4) { StartLineCap = cap, EndLineCap = cap };
        Assert.Equal(axial, line.StrokeContains(pen, new Point(-1, 0)));
        Assert.Equal(corner, line.StrokeContains(pen, new Point(-1.8, 1.8)));
        Assert.Equal(axial, line.StrokeContains(pen, new Point(21, 0)));
    }

    [Theory]
    [InlineData(PenLineJoin.Miter, 10, true)]
    [InlineData(PenLineJoin.Miter, 1, false)]
    [InlineData(PenLineJoin.Bevel, 10, false)]
    [InlineData(PenLineJoin.Round, 10, false)]
    public void WideningRespectsJoinsAndMiterLimits(PenLineJoin join, double limit, bool corner)
    {
        var path = Geometry.Parse("M0 10 L0 0 10 0");
        var pen = new Pen(Brushes.Black, 4) { LineJoin = join, MiterLimit = limit };
        Assert.Equal(corner, path.StrokeContains(pen, new Point(-1.8, -1.8)));
    }

    [Fact]
    public void WideningRespectsDashGapsOffsetsOddPatternsAndZeroLengthDots()
    {
        var line = new LineGeometry(new Point(0, 0), new Point(30, 0));
        var pen = new Pen(Brushes.Black, 2) { DashStyle = new DashStyle([2, 2]), DashCap = PenLineCap.Flat };
        Assert.True(line.StrokeContains(pen, new Point(1, 0)));
        Assert.False(line.StrokeContains(pen, new Point(6, 0)));
        pen.DashStyle.Offset = 1;
        Assert.False(line.StrokeContains(pen, new Point(3, 0)));
        Assert.True(line.StrokeContains(pen, new Point(7, 0)));
        pen.DashStyle = new DashStyle([2]);
        Assert.False(line.StrokeContains(pen, new Point(6, 0)));
        pen.DashStyle = new DashStyle([0, 2]);
        pen.DashCap = PenLineCap.Round;
        Assert.True(line.StrokeContains(pen, new Point(4, 0.5)));
        Assert.False(line.StrokeContains(pen, new Point(2, 0)));
    }

    [Fact]
    public void WideningDoesNotStrokeSegmentsMarkedAsGaps()
    {
        var path = new PathGeometry([new PathFigure(new Point(0, 0),
            [new LineSegment(new Point(10, 0), false), new LineSegment(new Point(10, 10), true)], false)]);
        var pen = new Pen(Brushes.Black, 2);
        Assert.False(path.StrokeContains(pen, new Point(5, 0)));
        Assert.True(path.StrokeContains(pen, new Point(10, 5)));
    }

    [Fact]
    public void DashPhaseContinuesAcrossUnstrokedSegments()
    {
        var path = new PathGeometry([new PathFigure(new Point(0, 0),
            [new LineSegment(new Point(3, 0), false), new LineSegment(new Point(12, 0))], false)]);
        var pen = new Pen(Brushes.Black, 1) { DashStyle = new DashStyle([2, 2]), DashCap = PenLineCap.Flat };
        Assert.False(path.StrokeContains(pen, new Point(3.5, 0)));
        Assert.True(path.StrokeContains(pen, new Point(5.5, 0)));
        Assert.False(path.StrokeContains(pen, new Point(7, 0)));
    }

    [Fact]
    public void SmoothJoinsUseRoundConnectionsRegardlessOfThePensCornerStyle()
    {
        var path = new PathGeometry([new PathFigure(new Point(0, 10),
            [new LineSegment(new Point(0, 0)), new LineSegment(new Point(10, 0)) { IsSmoothJoin = true }], false)]);
        var pen = new Pen(Brushes.Black, 4) { LineJoin = PenLineJoin.Miter };
        Assert.False(path.StrokeContains(pen, new Point(-1.8, -1.8)));
        Assert.True(path.StrokeContains(pen, new Point(-1.3, -1.3), 0.001, ToleranceType.Absolute));
    }

    [Fact]
    public void ZeroDashGapsKeepJoinsContinuousAndTinyPatternsFailWithoutHanging()
    {
        var path = Geometry.Parse("M0 0 L4 0 4 4");
        var pen = new Pen(Brushes.Black, 2) { DashStyle = new DashStyle([2, 0]), DashCap = PenLineCap.Flat };
        Assert.True(path.StrokeContains(pen, new Point(4.9, -0.9)));
        pen.DashStyle = new DashStyle([1e-12, 1e-12]);
        Assert.Throws<ArgumentException>(() => path.GetWidenedPathGeometry(pen));
    }

    [Fact]
    public void SingularArcTransformsRetainProjectedExtrema()
    {
        var source = (PathGeometry)Geometry.Parse("M10 0 A10 10 0 1 0 0 10");
        source.Transform = new MatrixTransform(new Matrix(1, 0, 0, 0, 0, 7));
        var result = new PathGeometry();
        result.AddGeometry(source);
        Assert.False(result.MayHaveCurves());
        Assert.InRange(Math.Abs(result.Bounds.Left + 10), 0, 0.001);
        Assert.Equal(10, result.Bounds.Right, 8);
        Assert.Equal(7, result.Bounds.Y, 8);
        Assert.Equal(0, result.Bounds.Height);
    }

    [Theory]
    [InlineData(1, 0.3, 0.7, 1.4)]
    [InlineData(-2, 0, 0.4, 0.5)]
    [InlineData(0, 1, -1, 0)]
    public void AddingTransformedArcsPreservesAnEllipseUnderAffineMaps(double m11, double m12, double m21, double m22)
    {
        var source = (PathGeometry)Geometry.Parse("M0 0 A30 12 35 1 1 45 10");
        var matrix = new Matrix(m11, m12, m21, m22, 13, -7);
        source.Transform = new MatrixTransform(matrix);
        var target = new PathGeometry();
        target.AddGeometry(source);
        Assert.IsType<ArcSegment>(target.Figures[0].Segments[0]);
        Assert.Null(target.Transform);
        var expectedBounds = PathGeometry.GetFigureBounds(source.Figures[0], matrix);
        Assert.Equal(expectedBounds.X, target.Bounds.X, 7);
        Assert.Equal(expectedBounds.Y, target.Bounds.Y, 7);
        Assert.Equal(expectedBounds.Width, target.Bounds.Width, 7);
        Assert.Equal(expectedBounds.Height, target.Bounds.Height, 7);
        var expected = source.GetFlattenedPathGeometry(0.001, ToleranceType.Absolute);
        var actual = target.GetFlattenedPathGeometry(0.001, ToleranceType.Absolute);
        foreach (var point in expected.Figures[0].Segments.SelectMany(segment => segment.GetPoints()).Where((_, index) => index % 7 == 0))
        {
            var mapped = matrix.Transform(point);
            var cursor = actual.Figures[0].StartPoint;
            var distance = double.PositiveInfinity;
            foreach (var end in actual.Figures[0].Segments.SelectMany(segment => segment.GetPoints()))
            {
                var dx = end.X - cursor.X; var dy = end.Y - cursor.Y;
                var t = Math.Clamp(((mapped.X - cursor.X) * dx + (mapped.Y - cursor.Y) * dy) / (dx * dx + dy * dy), 0, 1);
                distance = Math.Min(distance, Math.Sqrt(Math.Pow(mapped.X - cursor.X - t * dx, 2) + Math.Pow(mapped.Y - cursor.Y - t * dy, 2)));
                cursor = end;
            }
            Assert.InRange(distance, 0, 0.002);
        }
    }
}
