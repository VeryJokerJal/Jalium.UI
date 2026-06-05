using System;
using Jalium.UI;
using Jalium.UI.Media;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers the full SVG / XAML path mini-language grammar handled by
/// <see cref="PathMarkupParser"/>: every command in absolute and relative form, implicit
/// command repetition, smooth-curve control-point reflection, elliptical arcs (including
/// packed flags), subpath closing semantics, number packing, the fill-rule extension, and
/// the robustness contract (malformed input throws <see cref="FormatException"/> instead of
/// silently skipping or looping forever).
/// </summary>
public class PathMarkupParserTests
{
    private static PathFigure SingleFigure(string data)
    {
        var g = PathMarkupParser.Parse(data);
        return Assert.Single(g.Figures);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Parse_EmptyOrWhitespace_ReturnsEmptyGeometry(string data)
    {
        var g = PathMarkupParser.Parse(data);
        Assert.Empty(g.Figures);
    }

    [Fact]
    public void Parse_MoveAndLine_Absolute()
    {
        var fig = SingleFigure("M1,2 L3,4");
        Assert.Equal(new Point(1, 2), fig.StartPoint);
        var line = Assert.IsType<LineSegment>(Assert.Single(fig.Segments));
        Assert.Equal(new Point(3, 4), line.Point);
        Assert.False(fig.IsClosed);
    }

    [Fact]
    public void Parse_MoveAndLine_Relative()
    {
        var fig = SingleFigure("m1,2 l3,4");
        Assert.Equal(new Point(1, 2), fig.StartPoint);
        var line = Assert.IsType<LineSegment>(Assert.Single(fig.Segments));
        Assert.Equal(new Point(4, 6), line.Point); // (1+3, 2+4)
    }

    [Fact]
    public void Parse_ImplicitLineToAfterMove()
    {
        var fig = SingleFigure("M0,0 1,1 2,2");
        Assert.Equal(2, fig.Segments.Count);
        Assert.Equal(new Point(1, 1), Assert.IsType<LineSegment>(fig.Segments[0]).Point);
        Assert.Equal(new Point(2, 2), Assert.IsType<LineSegment>(fig.Segments[1]).Point);
    }

    [Fact]
    public void Parse_HorizontalVertical_AbsoluteAndRelative()
    {
        var fig = SingleFigure("M0,0 H10 V5 h-2 v-1");
        Assert.Equal(new Point(10, 0), Assert.IsType<LineSegment>(fig.Segments[0]).Point);
        Assert.Equal(new Point(10, 5), Assert.IsType<LineSegment>(fig.Segments[1]).Point);
        Assert.Equal(new Point(8, 5), Assert.IsType<LineSegment>(fig.Segments[2]).Point);
        Assert.Equal(new Point(8, 4), Assert.IsType<LineSegment>(fig.Segments[3]).Point);
    }

    [Fact]
    public void Parse_CubicBezier_AndSmoothReflectsPreviousControl()
    {
        var fig = SingleFigure("M0,0 C1,1 2,2 3,3 S4,4 5,5");
        var c = Assert.IsType<BezierSegment>(fig.Segments[0]);
        Assert.Equal(new Point(1, 1), c.Point1);
        Assert.Equal(new Point(2, 2), c.Point2);
        Assert.Equal(new Point(3, 3), c.Point3);

        var s = Assert.IsType<BezierSegment>(fig.Segments[1]);
        Assert.Equal(new Point(4, 4), s.Point1); // reflect((2,2) about (3,3)) = (4,4)
        Assert.Equal(new Point(4, 4), s.Point2);
        Assert.Equal(new Point(5, 5), s.Point3);
    }

    [Fact]
    public void Parse_SmoothCubicWithoutPriorCurve_UsesCurrentPointAsControl()
    {
        var fig = SingleFigure("M2,2 S4,4 6,6");
        var s = Assert.IsType<BezierSegment>(Assert.Single(fig.Segments));
        Assert.Equal(new Point(2, 2), s.Point1); // no prior C/S → first control is current point
    }

    [Fact]
    public void Parse_QuadraticBezier_AndSmoothReflectsPreviousControl()
    {
        var fig = SingleFigure("M0,0 Q1,2 3,4 T5,6");
        var q = Assert.IsType<QuadraticBezierSegment>(fig.Segments[0]);
        Assert.Equal(new Point(1, 2), q.Point1);
        Assert.Equal(new Point(3, 4), q.Point2);

        var t = Assert.IsType<QuadraticBezierSegment>(fig.Segments[1]);
        Assert.Equal(new Point(5, 6), t.Point1); // reflect((1,2) about (3,4)) = (5,6)
        Assert.Equal(new Point(5, 6), t.Point2);
    }

    [Fact]
    public void Parse_Arc_Absolute()
    {
        var fig = SingleFigure("M0,0 A5,7 30 1 0 10,10");
        var arc = Assert.IsType<ArcSegment>(Assert.Single(fig.Segments));
        Assert.Equal(new Size(5, 7), arc.Size);
        Assert.Equal(30, arc.RotationAngle);
        Assert.True(arc.IsLargeArc);
        Assert.Equal(SweepDirection.Counterclockwise, arc.SweepDirection);
        Assert.Equal(new Point(10, 10), arc.Point);
    }

    [Fact]
    public void Parse_Arc_PackedFlags()
    {
        // "0110,0" => largeArc=0, sweep=1, then endpoint 10,0 (flags are single chars).
        var fig = SingleFigure("M0,0A5,5 0 0110,0");
        var arc = Assert.IsType<ArcSegment>(Assert.Single(fig.Segments));
        Assert.False(arc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
        Assert.Equal(new Point(10, 0), arc.Point);
    }

    [Fact]
    public void Parse_CloseThenDrawCommand_OpensNewSubpathAtStartPoint()
    {
        var g = PathMarkupParser.Parse("M1,1 L5,1 Z L9,9");
        Assert.Equal(2, g.Figures.Count);

        Assert.True(g.Figures[0].IsClosed);
        Assert.Equal(new Point(1, 1), g.Figures[0].StartPoint);

        // After Z the pen is back at the subpath start (1,1); the following L opens a new
        // subpath rooted there rather than drawing into the closed figure.
        Assert.False(g.Figures[1].IsClosed);
        Assert.Equal(new Point(1, 1), g.Figures[1].StartPoint);
        Assert.Equal(new Point(9, 9), Assert.IsType<LineSegment>(Assert.Single(g.Figures[1].Segments)).Point);
    }

    [Fact]
    public void Parse_MultipleSubpaths_ViaMove()
    {
        var g = PathMarkupParser.Parse("M0,0 L1,1 M5,5 L6,6");
        Assert.Equal(2, g.Figures.Count);
        Assert.Equal(new Point(0, 0), g.Figures[0].StartPoint);
        Assert.Equal(new Point(5, 5), g.Figures[1].StartPoint);
    }

    [Theory]
    [InlineData("F0 M0,0 L1,1", FillRule.EvenOdd)]
    [InlineData("F1 M0,0 L1,1", FillRule.Nonzero)]
    public void Parse_FillRule(string data, FillRule expected)
    {
        var g = PathMarkupParser.Parse(data);
        Assert.Equal(expected, g.FillRule);
    }

    [Fact]
    public void Parse_NegativeSignActsAsSeparator()
    {
        var fig = SingleFigure("M1-2L3-4");
        Assert.Equal(new Point(1, -2), fig.StartPoint);
        Assert.Equal(new Point(3, -4), Assert.IsType<LineSegment>(Assert.Single(fig.Segments)).Point);
    }

    [Fact]
    public void Parse_PackedDecimals()
    {
        var fig = SingleFigure("M1.5.5 L.5.25");
        Assert.Equal(new Point(1.5, 0.5), fig.StartPoint);
        Assert.Equal(new Point(0.5, 0.25), Assert.IsType<LineSegment>(Assert.Single(fig.Segments)).Point);
    }

    [Fact]
    public void Parse_ExponentNotation()
    {
        var fig = SingleFigure("M1e2,3");
        Assert.Equal(new Point(100, 3), fig.StartPoint);
    }

    [Theory]
    [InlineData("M0,0 B5,5")]       // unknown command followed by numbers (old infinite-loop case)
    [InlineData("5,5 L1,1")]         // coordinate before any command
    [InlineData("M0,0 A5,5 0 2 1 10,10")] // invalid arc flag '2'
    [InlineData("F M0,0")]            // fill-rule command without 0/1
    public void Parse_MalformedInput_ThrowsFormatException_WithoutHanging(string data)
    {
        Assert.Throws<FormatException>(() => PathMarkupParser.Parse(data));
    }

    [Fact]
    public void Parse_NullData_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PathMarkupParser.Parse(null!));
    }
}
