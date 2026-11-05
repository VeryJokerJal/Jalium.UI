using Jalium.UI;
using Jalium.UI.Shapes;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Interop;
using Xunit;
using ShapePath = Jalium.UI.Shapes.Path;
using MediaPointCollection = Jalium.UI.Media.PointCollection;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers the WPF Shape geometry contract (DefiningGeometry / RenderedGeometry /
/// GeometryTransform) added across the Shape hierarchy, and the Geometry Freezable
/// semantics (recursive Freeze / CanFreeze + true deep Clone) added to the Geometry tree.
/// </summary>
public class ShapeGeometryAndFreezeTests
{
    // ── Feature 1: Shape.RenderedGeometry / DefiningGeometry ──

    [Fact]
    public void Line_rendered_geometry_is_line_geometry()
    {
        var line = new Line { X1 = 1, Y1 = 2, X2 = 11, Y2 = 22 };
        var lg = Assert.IsType<LineGeometry>(line.RenderedGeometry);
        Assert.Equal(new Point(1, 2), lg.StartPoint);
        Assert.Equal(new Point(11, 22), lg.EndPoint);
    }

    [Fact]
    public void Polygon_rendered_geometry_is_closed_path()
    {
        var poly = new Polygon
        {
            Points = new MediaPointCollection { new Point(0, 0), new Point(10, 0), new Point(5, 10) }
        };
        var pg = Assert.IsType<PathGeometry>(poly.RenderedGeometry);
        Assert.Single(pg.Figures);
        Assert.True(pg.Figures[0].IsClosed);
    }

    [Fact]
    public void Polyline_rendered_geometry_is_open_path()
    {
        var pl = new Polyline
        {
            Points = new MediaPointCollection { new Point(0, 0), new Point(10, 10), new Point(20, 0) }
        };
        var pg = Assert.IsType<PathGeometry>(pl.RenderedGeometry);
        Assert.Single(pg.Figures);
        Assert.False(pg.Figures[0].IsClosed);
    }

    [Fact]
    public void Path_rendered_geometry_exposes_parsed_data()
    {
        var path = new ShapePath { Data = "M 0,0 L 10,10" };
        var rendered = path.RenderedGeometry;
        Assert.IsType<PathGeometry>(rendered);
        Assert.False(rendered.Bounds.IsEmpty);
    }

    [Fact]
    public void Shape_without_geometry_returns_empty_rendered_geometry()
    {
        var poly = new Polygon(); // no Points
        Assert.Same(Geometry.Empty, poly.RenderedGeometry);
    }

    // ── Feature 5 / Tier A: Geometry deep Clone + recursive Freeze ──

    [Fact]
    public void PathGeometry_clone_is_deep_preserves_transform_and_curves()
    {
        var pg = (PathGeometry)Geometry.Parse("M0,0 C10,0 10,10 0,10");
        pg.Transform = new TranslateTransform { X = 5, Y = 7 };
        var clone = (PathGeometry)pg.Clone();

        Assert.NotSame(pg, clone);
        Assert.NotNull(clone.Transform);
        Assert.Equal(5, clone.Transform!.Value.OffsetX);
        // Curves are preserved — NOT flattened to line segments (the old base Clone bug).
        Assert.Contains(clone.Figures[0].Segments, s => s is BezierSegment);
    }

    [Fact]
    public void GeometryGroup_freeze_is_recursive()
    {
        var child1 = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var child2 = new EllipseGeometry { Center = new Point(5, 5), RadiusX = 3, RadiusY = 3 };
        var group = new GeometryGroup();
        group.Children.Add(child1);
        group.Children.Add(child2);

        Assert.True(group.CanFreeze);
        group.Freeze();

        Assert.True(group.IsFrozen);
        Assert.True(child1.IsFrozen); // recursive
        Assert.True(child2.IsFrozen);
    }

    [Fact]
    public void CombinedGeometry_freeze_is_recursive()
    {
        var g1 = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var g2 = new RectangleGeometry(new Rect(5, 5, 10, 10));
        var combined = new CombinedGeometry { Geometry1 = g1, Geometry2 = g2, GeometryCombineMode = GeometryCombineMode.Union };

        combined.Freeze();

        Assert.True(combined.IsFrozen);
        Assert.True(g1.IsFrozen);
        Assert.True(g2.IsFrozen);
    }

    // ── Feature 2: GeometryTypeConverter — Geometry-typed properties accept path strings ──

    [Fact]
    public void GeometryConverter_parses_path_string()
    {
        var g = TypeConverterRegistry.ConvertValue("M 0,0 L 10,10", typeof(Geometry));
        var pg = Assert.IsType<PathGeometry>(g);
        Assert.False(pg.Bounds.IsEmpty);
    }

    [Fact]
    public void GeometryConverter_resolves_for_pathgeometry_typed_property_via_base()
    {
        // GetConverter's IsAssignableFrom fallback routes PathGeometry-typed properties
        // to the Geometry converter too.
        var g = TypeConverterRegistry.ConvertValue("M 0,0 L 10,10", typeof(PathGeometry));
        Assert.IsType<PathGeometry>(g);
    }

    [Fact]
    public void GeometryConverter_invalid_input_returns_null_not_throw()
    {
        var g = TypeConverterRegistry.ConvertValue("definitely not a path", typeof(Geometry));
        Assert.Null(g);
    }

    // ── Feature 5 / Tier B: Pen.Clone deep copy ──

    [Fact]
    public void Pen_clone_is_deep_with_independent_dashstyle()
    {
        var brush = new SolidColorBrush(Color.FromRgb(255, 0, 0));
        var pen = new Pen(brush, 3)
        {
            LineJoin = PenLineJoin.Round,
            DashCap = PenLineCap.Round,
            DashStyle = new DashStyle(new[] { 1.0, 2.0 }, 0.5),
        };

        var clone = pen.Clone();

        Assert.NotSame(pen, clone);
        Assert.Equal(3, clone.Thickness);
        Assert.Equal(PenLineJoin.Round, clone.LineJoin);
        Assert.Equal(PenLineCap.Round, clone.DashCap);
        Assert.NotSame(pen.Brush, clone.Brush); // nested Freezables are deep-cloned
        Assert.Equal(
            Assert.IsType<SolidColorBrush>(pen.Brush).Color,
            Assert.IsType<SolidColorBrush>(clone.Brush).Color);
        Assert.NotSame(pen.DashStyle, clone.DashStyle); // dash style independent
        Assert.Equal(2, clone.DashStyle!.Dashes.Count);
        Assert.Equal(0.5, clone.DashStyle.Offset);
    }

    // ── Feature 3: Geometry.Combine boolean result uses nonzero fill rule ──

    [Fact]
    public void Combine_result_uses_nonzero_fill_rule()
    {
        var a = new RectangleGeometry(new Rect(0, 0, 20, 20));
        var b = new RectangleGeometry(new Rect(5, 5, 10, 10));
        Assert.Equal(FillRule.Nonzero, Geometry.Combine(a, b, GeometryCombineMode.Exclude, null).FillRule);
        Assert.Equal(FillRule.Nonzero, Geometry.Combine(a, b, GeometryCombineMode.Union, null).FillRule);
    }

    [Fact]
    public void Combine_exclude_hollows_contained_region()
    {
        // B (5,5,10,10) is fully inside A (0,0,20,20); Exclude must leave a hole where B is.
        var a = new RectangleGeometry(new Rect(0, 0, 20, 20));
        var b = new RectangleGeometry(new Rect(5, 5, 10, 10));
        var result = Geometry.Combine(a, b, GeometryCombineMode.Exclude, null);
        Assert.False(result.FillContains(new Point(10, 10))); // inside B → excluded (hole)
        Assert.True(result.FillContains(new Point(2, 2)));     // A only → kept
    }

    [Fact]
    public void Combine_union_keeps_overlap_filled()
    {
        // Overlapping rects: union must fill the overlap (even-odd used to hollow it).
        var a = new RectangleGeometry(new Rect(0, 0, 12, 12));
        var b = new RectangleGeometry(new Rect(6, 6, 12, 12));
        var result = Geometry.Combine(a, b, GeometryCombineMode.Union, null);
        Assert.True(result.FillContains(new Point(9, 9)));    // overlap → filled
        Assert.True(result.FillContains(new Point(2, 2)));    // A only → filled
        Assert.True(result.FillContains(new Point(16, 16)));  // B only → filled
    }

    // ── Feature 4 (A1): SVG <text> parses into a GlyphRunDrawing ──

    private static List<GlyphRunDrawing> CollectGlyphRuns(Drawing? d)
    {
        var result = new List<GlyphRunDrawing>();
        Walk(d, result);
        return result;

        static void Walk(Drawing? node, List<GlyphRunDrawing> sink)
        {
            if (node is GlyphRunDrawing g) sink.Add(g);
            else if (node is DrawingGroup grp)
                foreach (var c in grp.Children) Walk(c, sink);
        }
    }

    [Fact]
    public void Svg_text_is_parsed_into_glyph_run_drawing()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\">" +
                  "<text x=\"10\" y=\"30\" font-size=\"20\" font-weight=\"bold\" fill=\"#FF0000\">Hi</text></svg>";
        var image = SvgImage.FromSvgString(svg);
        var grd = Assert.Single(CollectGlyphRuns(image.Drawing));
        Assert.NotNull(grd.FormattedText);
        Assert.Equal("Hi", grd.FormattedText!.Text);
        Assert.Equal(20, grd.FormattedText.FontSize);
        Assert.Equal(700, grd.FormattedText.FontWeight);
    }

    [Fact]
    public void Svg_text_with_tspan_concatenates_content()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\">" +
                  "<text x=\"0\" y=\"20\">Ab<tspan>Cd</tspan></text></svg>";
        var grd = Assert.Single(CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing));
        Assert.Equal("AbCd", grd.FormattedText!.Text);
    }

    [Fact]
    public void Svg_text_anchor_middle_shifts_origin_left_of_start()
    {
        static string Svg(string anchor) =>
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\">" +
            $"<text x=\"50\" y=\"30\" font-size=\"20\" text-anchor=\"{anchor}\">Hello</text></svg>";
        var startRun = Assert.Single(CollectGlyphRuns(SvgImage.FromSvgString(Svg("start")).Drawing));
        var middleRun = Assert.Single(CollectGlyphRuns(SvgImage.FromSvgString(Svg("middle")).Drawing));
        Assert.True(middleRun.Origin.X < startRun.Origin.X);
    }

    [Fact]
    public void Svg_tspan_changes_font_fill_and_relative_position()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='120'>" +
                  "<text x='20' y='75' font-size='48' fill='red'>O" +
                  "<tspan dx='30' dy='15' font-size='0.5em' fill='blue'>O</tspan>" +
                  "</text></svg>";
        var runs = CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing);
        Assert.Equal(2, runs.Count);
        Assert.Equal(48, runs[0].FormattedText!.FontSize);
        Assert.Equal(24, runs[1].FormattedText!.FontSize);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(runs[0].ForegroundBrush).Color);
        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(runs[1].ForegroundBrush).Color);
        Assert.True(runs[1].Origin.X > runs[0].Origin.X + 40);
        Assert.True(runs[1].Origin.Y > runs[0].Origin.Y);
    }

    [Fact]
    public void Svg_text_rem_uses_the_computed_root_font_size()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='100' " +
                  "font-size='2rem'><text x='20' y='70' font-size='1rem'>A" +
                  "<tspan font-size='50%'>B</tspan></text></svg>";
        var runs = CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing);
        Assert.Equal(2, runs.Count);
        Assert.Equal(32, runs[0].FormattedText!.FontSize);
        Assert.Equal(16, runs[1].FormattedText!.FontSize);
    }

    [Fact]
    public void Svg_nested_text_lengths_adjust_child_before_parent_spacing()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        static float Width(string value)
        {
            Assert.True(NativeTextOutline.TryGetPath(value, "Segoe UI", 40, 400, 0,
                out _, out var width, out _));
            return width;
        }
        var a = Width("A");
        var b = Width("B");
        var parentLength = (a + 80 + b + 30).ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='300' height='120'>" +
                  $"<text x='20' y='80' font-size='40' textLength='{parentLength}'>" +
                  "A<tspan textLength='80' lengthAdjust='spacingAndGlyphs'>OO</tspan>B" +
                  "</text></svg>";
        var runs = CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing);
        Assert.Equal(new[] { "A", "O", "O", "B" },
            runs.Select(run => run.FormattedText!.Text));
        Assert.Equal(20 + a + 15, runs[1].Origin.X, 1);
        Assert.Equal(40, runs[2].Origin.X - runs[1].Origin.X, 1);
        Assert.Equal(20 + a + 80 + 30, runs[3].Origin.X, 1);

        var scaledLength = (2 * a + 80 + 2 * b).ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var scaledSvg = "<svg xmlns='http://www.w3.org/2000/svg' width='300' height='120'>" +
                        $"<text x='20' y='80' font-size='40' textLength='{scaledLength}' lengthAdjust='spacingAndGlyphs'>" +
                        "A<tspan textLength='80' lengthAdjust='spacingAndGlyphs'>OO</tspan>B" +
                        "</text></svg>";
        var scaledRuns = CollectGlyphRuns(SvgImage.FromSvgString(scaledSvg).Drawing);
        Assert.Equal(4, scaledRuns.Count);
        Assert.Equal(20 + 2 * a, scaledRuns[1].Origin.X, 1);
        Assert.Equal(40, scaledRuns[2].Origin.X - scaledRuns[1].Origin.X, 1);
        Assert.Equal(20 + 2 * a + 80, scaledRuns[3].Origin.X, 1);
    }

    [Fact]
    public void Svg_text_path_uses_path_distance_and_continues_from_the_endpoint()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 30, 400, 0,
            out _, out var width, out var baseline));
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='120'>" +
                  "<defs><path id='line' d='M20 70 H200'/></defs>" +
                  "<text font-size='30'><textPath href='#line' startOffset='25%'>OO</textPath>" +
                  "<tspan>O</tspan></text></svg>";
        var runs = CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing);
        Assert.Equal(new[] { "O", "O", "O" },
            runs.Select(run => run.FormattedText!.Text));
        Assert.Equal(65, runs[0].Origin.X, 1);
        Assert.Equal(65 + width, runs[1].Origin.X, 1);
        Assert.Equal(200, runs[2].Origin.X, 1);
        Assert.Equal(70 - baseline, runs[0].Origin.Y, 1);
        Assert.Equal(70 - baseline, runs[2].Origin.Y, 1);
    }

    [Fact]
    public void Svg_text_path_prefers_inline_path_and_reverses_the_right_side()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='120'>" +
                  "<defs><path id='other' d='M0 20 H180'/></defs>" +
                  "<text font-size='30'><textPath href='#other' path='M20 70 H200' " +
                  "side='right'>O</textPath></text></svg>";
        var run = Assert.Single(CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing));
        Assert.Equal(200, run.Origin.X, 1);
        var rotations = new List<RotateTransform>();
        FindRotations(SvgImage.FromSvgString(svg).Drawing, rotations);
        Assert.Contains(rotations, rotation => Math.Abs(rotation.Angle) == 180);

        static void FindRotations(Drawing? drawing, List<RotateTransform> found)
        {
            if (drawing is not DrawingGroup group) return;
            if (group.Transform is RotateTransform rotation) found.Add(rotation);
            foreach (var child in group.Children) FindRotations(child, found);
        }
    }

    [Fact]
    public void Svg_text_path_falls_back_to_href_when_inline_path_is_invalid()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='120'>" +
                  "<defs><path id='line' d='M20 70 H200'/></defs>" +
                  "<text font-size='30'><textPath href='#line' path='M0 0 L'>" +
                  "O</textPath></text></svg>";
        var run = Assert.Single(CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing));
        Assert.Equal(20, run.Origin.X, 1);
    }

    [Fact]
    public void Svg_text_path_samples_curves_and_uses_the_referenced_shapes_own_transform()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='150'>" +
                  "<defs><g transform='translate(0,50)'>" +
                  "<path id='curve' d='M20 80 C80 20 140 140 200 80' " +
                  "transform='translate(0,-10)'/></g></defs>" +
                  "<text font-size='25'><textPath href='#curve'>OOOOO</textPath></text>" +
                  "<text font-size='25'><textPath path='M20 70 C80 10 140 130 200 70'>" +
                  "OOOOO</textPath></text></svg>";
        var image = SvgImage.FromSvgString(svg);
        var runs = CollectGlyphRuns(image.Drawing);
        Assert.Equal(10, runs.Count);
        for (var index = 0; index < 5; index++)
        {
            Assert.Equal(runs[index + 5].Origin.X, runs[index].Origin.X, 1);
            Assert.Equal(runs[index + 5].Origin.Y, runs[index].Origin.Y, 1);
        }
        var rotations = new List<double>();
        FindRotations(image.Drawing, rotations);
        Assert.Equal(10, rotations.Count);
        Assert.True(rotations[0] < 0);
        Assert.True(rotations[4] > rotations[0]);
        for (var index = 0; index < 5; index++)
            Assert.Equal(rotations[index + 5], rotations[index], 1);

        static void FindRotations(Drawing? drawing, List<double> found)
        {
            if (drawing is not DrawingGroup group) return;
            if (group.Transform is RotateTransform rotation) found.Add(rotation.Angle);
            foreach (var child in group.Children) FindRotations(child, found);
        }
    }

    [Fact]
    public void Svg_text_path_hides_open_path_overflow_and_keeps_a_closed_path_to_one_circuit()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("A", "Segoe UI", 30, 400, 0,
            out _, out var width, out _));
        var lineEnd = (20 + 1.2 * width).ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='200'>" +
                  "<defs><circle id='ring' cx='80' cy='80' r='50'/></defs>" +
                  $"<text font-size='30'><textPath path='M20 70 H{lineEnd}'>AAAA</textPath></text>" +
                  "<text font-size='30'><textPath href='#ring' startOffset='75%'>" +
                  "OOOOOOOOOOOOOOOOOOOO</textPath></text></svg>";
        var runs = CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing);
        Assert.Equal(1, runs.Count(run => run.FormattedText!.Text == "A"));
        Assert.InRange(runs.Count(run => run.FormattedText!.Text == "O"), 10, 19);
    }

    [Fact]
    public void Svg_tspan_on_text_path_applies_normal_offset_and_supplemental_rotation()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 30, 400, 0,
            out _, out _, out var baseline));
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='100'>" +
                  "<text font-size='30'><textPath path='M20 70 H180'>" +
                  "<tspan dy='10' rotate='30'>O</tspan></textPath></text></svg>";
        var image = SvgImage.FromSvgString(svg);
        var run = Assert.Single(CollectGlyphRuns(image.Drawing));
        Assert.Equal(80 - baseline, run.Origin.Y, 1);
        var rotations = new List<double>();
        Collect(image.Drawing);
        Assert.Contains(rotations, value => Math.Abs(value - 30) < 0.01);

        void Collect(Drawing? drawing)
        {
            if (drawing is not DrawingGroup group) return;
            if (group.Transform is RotateTransform rotation) rotations.Add(rotation.Angle);
            foreach (var child in group.Children) Collect(child);
        }
    }

    [Fact]
    public void Svg_text_path_ignores_absolute_y_but_keeps_relative_dy()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 30, 400, 0,
            out _, out _, out var baseline));
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='160'>" +
                  "<text y='50' font-size='30'><textPath path='M20 70 H180'>" +
                  "<tspan y='100' dy='10'>O</tspan></textPath></text></svg>";
        var run = Assert.Single(CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing));
        Assert.Equal(80 - baseline, run.Origin.Y, 1);
    }

    [Fact]
    public void Svg_text_path_uses_adjusted_text_length_for_glyph_spacing()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 30, 400, 0,
            out _, out var width, out _));
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='220' height='100'>" +
                  "<text font-size='30'><textPath path='M20 70 H200' " +
                  "textLength='80'>OO</textPath></text></svg>";
        var runs = CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing);
        Assert.Equal(2, runs.Count);
        Assert.Equal(20, runs[0].Origin.X, 1);
        Assert.Equal(20 + 80 - width, runs[1].Origin.X, 1);
    }

    [Fact]
    public void Svg_text_path_anchors_each_absolute_x_chunk_separately()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return;
        Assert.True(NativeTextOutline.TryGetPath("O", "Segoe UI", 30, 400, 0,
            out _, out var width, out _));
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='100'>" +
                  "<text font-size='30'><textPath path='M20 70 H220' " +
                  "text-anchor='middle'><tspan x='50 120'>OO</tspan>" +
                  "</textPath></text></svg>";
        var runs = CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing);
        Assert.Equal(2, runs.Count);
        Assert.Equal(20 + 50 - width / 2, runs[0].Origin.X, 1);
        Assert.Equal(20 + 120 - width / 2, runs[1].Origin.X, 1);
    }

    [Fact]
    public void Svg_text_path_calibrates_numeric_offsets_but_not_percentages_with_path_length()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='120'>" +
                  "<defs><path id='guide' d='M20 70 H220' pathLength='400'/>" +
                  "<line id='shape' x1='20' y1='70' x2='220' y2='70' pathLength='400'/>" +
                  "<path id='zero' d='M20 70 H220' pathLength='0'/></defs>" +
                  "<text font-size='30'><textPath href='#guide' startOffset='100'>O</textPath></text>" +
                  "<text font-size='30'><textPath href='#guide' startOffset='50%'>O</textPath></text>" +
                  "<text font-size='30'><textPath href='#shape' startOffset='100'>O</textPath></text>" +
                  "<text font-size='30'><textPath href='#zero' startOffset='0'>O</textPath></text>" +
                  "<text font-size='30'><textPath href='#zero' startOffset='10'>O</textPath></text>" +
                  "<text font-size='30'><textPath href='#zero' startOffset='50%'>O</textPath></text>" +
                  "</svg>";
        var runs = CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing);
        Assert.Equal(5, runs.Count);
        Assert.Equal(70, runs[0].Origin.X, 1);
        Assert.Equal(120, runs[1].Origin.X, 1);
        Assert.Equal(70, runs[2].Origin.X, 1);
        Assert.Equal(20, runs[3].Origin.X, 1);
        Assert.Equal(120, runs[4].Origin.X, 1);
    }

    [Fact]
    public void Svg_closed_text_path_limits_wrap_using_each_anchor_range()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='140' height='100'>" +
                  "<defs><path id='loop' d='M20 20 H80 V60 H20 Z'/></defs>" +
                  "<text font-size='25'><textPath href='#loop' text-anchor='start'>" +
                  "<tspan x='0'>A</tspan></textPath></text>" +
                  "<text font-size='25'><textPath href='#loop' text-anchor='start'>" +
                  "<tspan x='-50'>B</tspan></textPath></text>" +
                  "<text font-size='25'><textPath href='#loop' startOffset='75%' text-anchor='middle'>" +
                  "<tspan x='90'>C</tspan></textPath></text>" +
                  "<text font-size='25'><textPath href='#loop' startOffset='75%' text-anchor='middle'>" +
                  "<tspan x='110'>D</tspan></textPath></text>" +
                  "<text font-size='25'><textPath href='#loop' text-anchor='end'>" +
                  "<tspan x='-90'>E</tspan></textPath></text>" +
                  "<text font-size='25'><textPath href='#loop' text-anchor='end'>" +
                  "<tspan x='50'>F</tspan></textPath></text></svg>";
        var runs = CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing);
        Assert.Equal(new[] { "A", "C", "E" },
            runs.Select(run => run.FormattedText!.Text));
    }

    [Fact]
    public void Svg_tspan_keeps_group_opacity_and_transform_for_nested_text()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='100'>" +
                  "<text x='20' y='70' font-size='30'>A" +
                  "<tspan opacity='0.4' transform='translate(10,5)'>B<tspan>C</tspan></tspan>" +
                  "D</text></svg>";
        var image = SvgImage.FromSvgString(svg);
        var runs = CollectGlyphRuns(image.Drawing);
        Assert.Equal(4, runs.Count);
        Assert.Equal(new[] { "A", "B", "C", "D" },
            runs.Select(run => run.FormattedText!.Text));

        var effect = FindSpanEffect(image.Drawing);
        Assert.NotNull(effect);
        Assert.Equal(0.4, effect.Opacity);
        var translation = Assert.IsType<TranslateTransform>(effect.Transform);
        Assert.Equal(10, translation.X);
        Assert.Equal(5, translation.Y);
        Assert.Equal(2, CollectGlyphRuns(effect).Count);

        static DrawingGroup? FindSpanEffect(Drawing? drawing)
        {
            if (drawing is not DrawingGroup group) return null;
            if (group.Opacity == 0.4) return group;
            foreach (var child in group.Children)
                if (FindSpanEffect(child) is { } found) return found;
            return null;
        }
    }

    [Fact]
    public void Svg_text_position_lists_count_supplementary_characters_as_two_utf16_units()
    {
        static List<GlyphRunDrawing> Runs(string attributes) => CollectGlyphRuns(
            SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' " +
                "width='200' height='100'><text y='60' font-size='30' " +
                $"{attributes}>\U0001F600O</text></svg>").Drawing);

        var absolute = Runs("x='20 50 80'");
        Assert.Equal(2, absolute.Count);
        Assert.Equal("O", absolute[1].FormattedText!.Text);
        Assert.Equal(80, absolute[1].Origin.X);

        var plain = Runs("x='20 50 80' dx='0 0 0'");
        var offset = Runs("x='20 50 80' dx='0 5 10'");
        Assert.Equal(2, plain.Count);
        Assert.Equal(2, offset.Count);
        Assert.Equal(15, offset[1].Origin.X - plain[1].Origin.X, 2);
    }

    [Fact]
    public void Svg_text_rotation_list_continues_through_tspan_with_child_override()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='100'>" +
                  "<text x='20 50 80' y='60' rotate='0 90 180' font-size='30'>" +
                  "A<tspan rotate='-30'>B</tspan>C</text></svg>";
        var image = SvgImage.FromSvgString(svg);
        Assert.Equal(new[] { "A", "B", "C" },
            CollectGlyphRuns(image.Drawing).Select(run => run.FormattedText!.Text));
        var rotations = new List<RotateTransform>();
        CollectRotations(image.Drawing, rotations);
        Assert.Equal(2, rotations.Count);
        Assert.Equal(-30, rotations[0].Angle);
        Assert.Equal(50, rotations[0].CenterX);
        Assert.Equal(60, rotations[0].CenterY);
        Assert.Equal(180, rotations[1].Angle);
        Assert.Equal(80, rotations[1].CenterX);
        Assert.Equal(60, rotations[1].CenterY);

        static void CollectRotations(Drawing? drawing, List<RotateTransform> found)
        {
            if (drawing is not DrawingGroup group) return;
            if (group.Transform is RotateTransform rotation) found.Add(rotation);
            foreach (var child in group.Children) CollectRotations(child, found);
        }
    }

    [Fact]
    public void Svg_visible_tspan_inside_hidden_text_still_draws()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='100'>" +
                  "<text x='20' y='70' visibility='hidden' font-size='30'>Hidden" +
                  "<tspan visibility='visible'>Shown</tspan></text></svg>";
        var run = Assert.Single(CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing));
        Assert.Equal("Shown", run.FormattedText!.Text);
        Assert.True(run.Origin.X > 20);
    }

    [Fact]
    public void Svg_text_with_fill_none_is_not_rendered()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\">" +
                  "<text x=\"10\" y=\"30\" fill=\"none\">Hidden</text></svg>";
        Assert.Empty(CollectGlyphRuns(SvgImage.FromSvgString(svg).Drawing));
    }

    // ── Review fix H1: CombinedGeometry.GetFlattenedPathGeometry must not bake its own Transform ──

    [Fact]
    public void Combined_geometry_flatten_does_not_bake_own_transform()
    {
        // The transform is applied ONCE by the consumer (DrawGeometry's PushTransform), so the
        // flattened result stays in local space — its bounds are NOT shifted by +100. Baking it
        // here (the old behavior) double-applied the transform on the GPU path.
        var a = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var b = new RectangleGeometry(new Rect(2, 2, 6, 6));
        var combined = new CombinedGeometry
        {
            Geometry1 = a,
            Geometry2 = b,
            GeometryCombineMode = GeometryCombineMode.Union,
            Transform = new TranslateTransform { X = 100, Y = 0 },
        };
        var flat = combined.GetFlattenedPathGeometry();
        Assert.True(flat.Bounds.X < 50, $"flatten must not bake Transform; bounds.X={flat.Bounds.X}");
    }
}
