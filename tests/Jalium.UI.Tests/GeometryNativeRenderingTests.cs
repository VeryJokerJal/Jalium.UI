using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class GeometryNativeRenderingTests
{
    [Theory]
    [InlineData("corners")]
    [InlineData("negativeRectangle")]
    [InlineData("negativeEllipse")]
    public void NativePrimitiveFillUsesTheSameRadiiAsItsGeometry(string kind)
    {
        Geometry geometry = kind switch
        {
            "corners" => new RectangleGeometry(new Rect(0, 0, 20, 20), new CornerRadius(10, 0, 5, 0)),
            "negativeRectangle" => new RectangleGeometry(new Rect(0, 0, 20, 20), -10, -10),
            _ => new EllipseGeometry(new Point(10, 10), -10, -10),
        };
        geometry.Transform = new TranslateTransform(3, 4);
        using var context = new RenderContext(RenderBackend.Software);
        var pixels = Render(context, geometry, null);
        Assert.True(Ink(pixels, new Point(10, 10)));
        Assert.False(Ink(pixels, new Point(1, 1)));
        Assert.Equal(kind == "corners", Ink(pixels, new Point(19, 1)));
    }

    [Fact]
    public void NativeDegeneratePrimitivesRetainTheirVisibleStroke()
    {
        using var context = new RenderContext(RenderBackend.Software);
        var pen = new Pen(Brushes.Black, 2);
        var rectangle = new RectangleGeometry(new Rect(0, 0, 0, 20)) { Transform = new TranslateTransform(3, 4) };
        Assert.True(Ink(Render(context, rectangle, pen), new Point(0, 10)));
        var ellipse = new EllipseGeometry(new Point(10, 10), 0, 10) { Transform = new TranslateTransform(3, 4) };
        Assert.True(Ink(Render(context, ellipse, pen), new Point(10, 5)));
    }

    [Fact]
    public void NativeStrokeRespectsDashGapsAndUnstrokedSegments()
    {
        using var context = new RenderContext(RenderBackend.Software);
        var line = new LineGeometry(new Point(0, 0), new Point(20, 0))
            { Transform = new TranslateTransform(3, 4) };
        var pen = new Pen(Brushes.Black, 2) { DashStyle = new DashStyle([2, 2]), DashCap = PenLineCap.Flat };
        var dashed = Render(context, line, pen);
        Assert.True(Ink(dashed, new Point(1, 0)));
        Assert.False(Ink(dashed, new Point(6, 0)));
        var path = new PathGeometry([new PathFigure(new Point(0, 0),
            [new LineSegment(new Point(10, 0), false), new LineSegment(new Point(10, 10))], false)])
            { Transform = new TranslateTransform(3, 4) };
        pen.DashStyle = DashStyles.Solid;
        pen.DashCap = PenLineCap.Square;
        var partial = Render(context, path, pen);
        Assert.False(Ink(partial, new Point(5, 0)));
        Assert.True(Ink(partial, new Point(10, 5)));
        Assert.True(Ink(partial, new Point(10, -0.5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeSvgTextRotationMatchesIndependentlyPositionedGlyphContours(bool rotationList)
    {
        var svg = rotationList
            ? "<svg width='240' height='150'><text x='20 50 80' y='60' rotate='0 90 180' font-size='30'>A<tspan rotate='-30'>B</tspan>C</text></svg>"
            : "<svg width='240' height='150'><defs><path id='other' d='M0 20 H180'/></defs><text font-size='30'><textPath href='#other' path='M20 70 H200' side='right'>O</textPath></text></svg>";
        var expected = new DrawingGroup();
        if (rotationList)
        {
            expected.Children.Add(Glyph("A", 20, 60, 0));
            expected.Children.Add(Glyph("B", 50, 60, -30));
            expected.Children.Add(Glyph("C", 80, 60, 180));
        }
        else expected.Children.Add(Glyph("O", 200, 70, 180));
        using var context = new RenderContext(RenderBackend.Software);
        var actual = Render(context, 240, 150, drawing => SvgImage.FromSvgString(svg).Drawing!.RenderTo(drawing));
        var reference = Render(context, 240, 150, drawing => expected.RenderTo(drawing));
        Assert.Contains(actual, value => value == 0);
        Assert.Equal(reference, actual);

        static GeometryDrawing Glyph(string text, double x, double y, double angle)
        {
            Assert.True(NativeTextOutline.TryGetPath(text, "Segoe UI", 30, 400, 0,
                out var data, out _, out var baseline));
            var source = PathMarkupParser.ParseSvgPathData(data).GetFlattenedPathGeometry(0.01, ToleranceType.Absolute);
            var result = new PathGeometry { FillRule = FillRule.Nonzero };
            var cosine = Math.Cos(angle * Math.PI / 180);
            var sine = Math.Sin(angle * Math.PI / 180);
            Point Map(Point point) => new(x + point.X * cosine - (point.Y - baseline) * sine,
                y + point.X * sine + (point.Y - baseline) * cosine);
            foreach (var figure in source.Figures)
            {
                var mapped = new PathFigure { StartPoint = Map(figure.StartPoint), IsClosed = true, IsFilled = true };
                foreach (var point in figure.Segments.SelectMany(segment => segment.GetPoints()))
                    mapped.Segments.Add(new LineSegment(Map(point)));
                result.Figures.Add(mapped);
            }
            return new GeometryDrawing(Brushes.Black, null, result);
        }
    }

    [Fact]
    public void NativeSvgTextPathAppliesTheReferencedPathsOwnTransform()
    {
        const string referenced = "<svg width='240' height='150'><defs><g transform='translate(0,50)'><path id='curve' d='M20 80 C80 20 140 140 200 80' transform='translate(0,-10)'/></g></defs><text font-size='25'><textPath href='#curve'>OOOOO</textPath></text></svg>";
        const string inline = "<svg width='240' height='150'><text font-size='25'><textPath path='M20 70 C80 10 140 130 200 70'>OOOOO</textPath></text></svg>";
        using var context = new RenderContext(RenderBackend.Software);
        var actual = Render(context, 240, 150, drawing => SvgImage.FromSvgString(referenced).Drawing!.RenderTo(drawing));
        var expected = Render(context, 240, 150, drawing => SvgImage.FromSvgString(inline).Drawing!.RenderTo(drawing));
        Assert.Contains(actual, value => value == 0);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(GeometryImplementationTests.GeometryKinds), MemberType = typeof(GeometryImplementationTests))]
    public void NativeFillAndStrokeAgreeWithGeometryQueriesAfterOneRootTransform(string kind)
    {
        Geometry geometry = kind switch
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
        geometry.Transform = new TranslateTransform(3, 4);
        using var context = new RenderContext(RenderBackend.Software);
        var pen = new Pen(Brushes.Black, 2);
        var fill = Render(context, geometry, null);
        var stroke = Render(context, geometry, pen);
        var inside = kind == "Combined" ? new Point(2, 2) : new Point(10, 5);
        var outside = kind is "Path" or "Stream" ? new Point(1, 14) :
            kind == "Combined" ? new Point(10, 10) : new Point(1, 1);
        var edge = kind is "Path" or "Stream" ? new Point(10, 15) :
            kind == "Line" ? new Point(10, 10) : new Point(0, 10);
        var strokeInterior = kind == "Line" ? new Point(1, 1) :
            kind == "Combined" ? new Point(10, 10) : new Point(10, 5);
        Assert.Equal(geometry.FillContains(inside), Ink(fill, inside));
        Assert.False(Ink(fill, outside));
        Assert.True(Ink(stroke, edge));
        Assert.False(Ink(stroke, strokeInterior));
    }

    private static StreamGeometry Stream()
    {
        var stream = new StreamGeometry();
        using var context = stream.Open();
        context.BeginFigure(new Point(0, 0), true, true);
        context.BezierTo(new Point(0, 20), new Point(20, 20), new Point(20, 0), true, false);
        return stream;
    }

    private static bool Ink(byte[] pixels, Point local)
    {
        var x = (int)(10 + 2 * (local.X + 3));
        var y = (int)(10 + 2 * (local.Y + 4));
        return pixels[(y * 80 + x) * 4] < 128;
    }

    private static byte[] Render(RenderContext context, Geometry geometry, Pen? pen)
        => Render(context, 80, 80, drawing =>
        {
            drawing.PushTransform(new MatrixTransform(new Matrix(2, 0, 0, 2, 10, 10)));
            drawing.DrawGeometry(pen is null ? Brushes.Black : null, pen, geometry);
            drawing.Pop();
        });

    private static byte[] Render(RenderContext context, int width, int height, Action<RenderTargetDrawingContext> render)
    {
        using var target = context.CreateOffscreenRenderTarget(width, height);
        var drawing = new RenderTargetDrawingContext(target, context);
        try
        {
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            target.BeginDraw();
            target.Clear(1, 1, 1, 1);
            render(drawing);
            target.EndDraw();
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, checked((uint)width * 4), out var actualWidth, out var actualHeight));
            Assert.Equal(width, actualWidth);
            Assert.Equal(height, actualHeight);
            return pixels;
        }
        finally { drawing.Close(); }
    }
}
