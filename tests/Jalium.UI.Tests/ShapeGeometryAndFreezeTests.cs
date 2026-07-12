using Jalium.UI;
using Jalium.UI.Controls.Shapes;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Xunit;
using ShapePath = Jalium.UI.Controls.Shapes.Path;
using ShapePointCollection = Jalium.UI.Controls.Shapes.PointCollection;

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
            Points = new ShapePointCollection { new Point(0, 0), new Point(10, 0), new Point(5, 10) }
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
            Points = new ShapePointCollection { new Point(0, 0), new Point(10, 10), new Point(20, 0) }
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
