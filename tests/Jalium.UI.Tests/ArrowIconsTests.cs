using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers <see cref="ArrowIcons"/> after its path parser was switched to delegate to the
/// framework's canonical <see cref="PathMarkupParser"/>. Two things are verified:
/// <list type="bullet">
///   <item>The four built-in arrow glyphs still parse into the same closed, filled
///   figures they did with the old hand-rolled parser (no rendering regression).</item>
///   <item>The previously-unsupported commands — smooth cubic <c>S</c>, quadratic
///   <c>Q</c>, smooth quadratic <c>T</c> and elliptical arc <c>A</c> — now parse, and an
///   <see cref="ArcSegment"/> survives <see cref="ArrowIcons.ScaleGeometry"/> instead of
///   being silently dropped at draw time.</item>
/// </list>
/// </summary>
public class ArrowIconsTests
{
    [Theory]
    [InlineData(ArrowIcons.Direction.Up)]
    [InlineData(ArrowIcons.Direction.Down)]
    [InlineData(ArrowIcons.Direction.Left)]
    [InlineData(ArrowIcons.Direction.Right)]
    public void GetGeometry_BuiltInArrow_IsSingleClosedFilledFigure(ArrowIcons.Direction direction)
    {
        var geometry = ArrowIcons.GetGeometry(direction);

        Assert.True(geometry.IsFrozen, "Cached built-in arrows must be frozen for cross-thread use.");
        var figure = Assert.Single(geometry.Figures);
        Assert.True(figure.IsClosed, "Arrow outline must be a closed figure.");
        Assert.True(figure.IsFilled, "Arrow outline must be filled.");
        Assert.NotEmpty(figure.Segments);
        Assert.False(geometry.Bounds.IsEmpty, "Arrow must have a non-empty bounding box.");
    }

    [Fact]
    public void GetGeometry_UpArrow_HasExpectedSegmentSequence()
    {
        // "M1000.011 690.586 l… c… l… c… h… c… Z" → one closed figure whose implicit/explicit
        // commands produce Line, Bezier, Line, Bezier, Line, Bezier in order.
        var figure = Assert.Single(ArrowIcons.GetGeometry(ArrowIcons.Direction.Up).Figures);

        Assert.Equal(new Point(1000.011, 690.586), figure.StartPoint);
        Assert.Collection(figure.Segments,
            s => Assert.IsType<LineSegment>(s),
            s => Assert.IsType<BezierSegment>(s),
            s => Assert.IsType<LineSegment>(s),
            s => Assert.IsType<BezierSegment>(s),
            s => Assert.IsType<LineSegment>(s),
            s => Assert.IsType<BezierSegment>(s));
    }

    [Fact]
    public void ParseSvgPath_SmoothCubic_ReflectsPreviousControlPoint()
    {
        // 'S' was silently dropped by the old parser; it must now produce a cubic whose
        // first control point is the reflection of the previous one about the current point.
        var figure = Assert.Single(ArrowIcons.ParseSvgPath("M0,0 C1,1 2,2 3,3 S4,4 5,5").Figures);

        Assert.IsType<BezierSegment>(figure.Segments[0]);
        var smooth = Assert.IsType<BezierSegment>(figure.Segments[1]);
        Assert.Equal(new Point(4, 4), smooth.Point1); // reflect((2,2) about (3,3))
        Assert.Equal(new Point(4, 4), smooth.Point2);
        Assert.Equal(new Point(5, 5), smooth.Point3);
    }

    [Fact]
    public void ParseSvgPath_Quadratic_AndSmoothQuadratic()
    {
        // 'Q' and 'T' were both silently dropped by the old parser.
        var figure = Assert.Single(ArrowIcons.ParseSvgPath("M0,0 Q1,2 3,4 T5,6").Figures);

        var quad = Assert.IsType<QuadraticBezierSegment>(figure.Segments[0]);
        Assert.Equal(new Point(1, 2), quad.Point1);
        Assert.Equal(new Point(3, 4), quad.Point2);

        var smooth = Assert.IsType<QuadraticBezierSegment>(figure.Segments[1]);
        Assert.Equal(new Point(5, 6), smooth.Point1); // reflect((1,2) about (3,4))
        Assert.Equal(new Point(5, 6), smooth.Point2);
    }

    [Fact]
    public void ParseSvgPath_EllipticalArc()
    {
        // 'A' was silently dropped by the old parser.
        var figure = Assert.Single(ArrowIcons.ParseSvgPath("M0,0 A5,7 30 1 0 10,10").Figures);

        var arc = Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));
        Assert.Equal(new Size(5, 7), arc.Size);
        Assert.Equal(30, arc.RotationAngle);
        Assert.True(arc.IsLargeArc);
        Assert.Equal(SweepDirection.Counterclockwise, arc.SweepDirection);
        Assert.Equal(new Point(10, 10), arc.Point);
    }

    [Fact]
    public void ScaleGeometry_PreservesAndScalesEverySegmentKind()
    {
        // A figure containing each segment kind the mini-language can emit. The arc case is
        // the regression guard: before the fix ScaleGeometry had no ArcSegment branch and
        // dropped it, so an 'A'-command arrow would lose part of its outline when drawn.
        var source = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(1, 1)));
        figure.Segments.Add(new BezierSegment(new Point(2, 2), new Point(3, 3), new Point(4, 4)));
        figure.Segments.Add(new QuadraticBezierSegment(new Point(5, 5), new Point(6, 6)));
        figure.Segments.Add(new ArcSegment(new Point(7, 7), new Size(8, 9), 45, true, SweepDirection.Clockwise, true));
        source.Figures.Add(figure);

        // Uniform scale ×2 with a (10, 20) translation: p' = p*2 + (10, 20).
        var scaled = ArrowIcons.ScaleGeometry(source, 2.0, 10, 20);

        var result = Assert.Single(scaled.Figures);
        Assert.True(result.IsClosed);
        Assert.True(result.IsFilled);
        Assert.Equal(new Point(10, 20), result.StartPoint);

        var line = Assert.IsType<LineSegment>(result.Segments[0]);
        Assert.Equal(new Point(12, 22), line.Point);

        var bezier = Assert.IsType<BezierSegment>(result.Segments[1]);
        Assert.Equal(new Point(14, 24), bezier.Point1);
        Assert.Equal(new Point(16, 26), bezier.Point2);
        Assert.Equal(new Point(18, 28), bezier.Point3);

        var quad = Assert.IsType<QuadraticBezierSegment>(result.Segments[2]);
        Assert.Equal(new Point(20, 30), quad.Point1);
        Assert.Equal(new Point(22, 32), quad.Point2);

        var arc = Assert.IsType<ArcSegment>(result.Segments[3]);
        Assert.Equal(new Point(24, 34), arc.Point);          // 7*2+10, 7*2+20
        Assert.Equal(new Size(16, 18), arc.Size);            // radii scaled by the same factor
        Assert.Equal(45, arc.RotationAngle);                 // rotation preserved
        Assert.True(arc.IsLargeArc);                         // flags preserved
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
    }
}
