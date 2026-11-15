using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSvgTextPaintTests
{
    public static IEnumerable<object[]> PaintOrders()
    {
        yield return ["normal", false];
        foreach (var first in new[] { "fill", "stroke", "markers" })
        {
            yield return [first, first == "stroke"];
            foreach (var second in new[] { "fill", "stroke", "markers" }.Where(value => value != first))
            {
                var strokeFirst = first == "stroke" || first == "markers" && second == "stroke";
                yield return [$"{first} {second}", strokeFirst];
                yield return [$"{first} {second} {new[] { "fill", "stroke", "markers" }.Single(value => value != first && value != second)}", strokeFirst];
            }
        }
    }

    [Theory, MemberData(nameof(PaintOrders))]
    public void PaintOrderPaintsFillAndStrokeInSpecifiedOrder(string order, bool strokeFirst)
    {
        using var image = Parse($"rect{{fill:red;stroke:blue;stroke-width:8;paint-order:{order}}}",
            "<rect x='16' y='16' width='40' height='28'/>");
        foreach (var route in new[] { 0, 1, 2 })
        {
            var pixels = Render(image.Drawing!, route);
            AssertPixel(pixels, 18, 30, strokeFirst ? Colors.Red : Colors.Blue);
            AssertPixel(pixels, 13, 30, Colors.Blue);
            AssertPixel(pixels, 32, 30, Colors.Red);
        }
    }

    [Theory]
    [InlineData("fill fill")] [InlineData("stroke stroke")]
    [InlineData("normal stroke")] [InlineData("fill,stroke")]
    [InlineData("stroke fill markers stroke")] [InlineData("none")]
    [InlineData("filler")] [InlineData("fill/markers")]
    public void InvalidPaintOrderKeepsEarlierValidDeclaration(string invalid)
    {
        using var image = Parse($"rect{{fill:red;stroke:blue;stroke-width:8;paint-order:stroke;paint-order:{invalid}}}",
            "<rect x='16' y='16' width='40' height='28'/>");
        AssertPixel(Render(image.Drawing!, 0), 18, 30, Colors.Red);
    }

    [Theory]
    [InlineData("g{paint-order:stroke}", "")]
    [InlineData("g{--order:stroke}rect{paint-order:var(--order)}", "")]
    [InlineData("rect{paint-order:stroke}rect{paint-order:initial}", "paint-order='stroke'")]
    [InlineData("rect{paint-order:fill!important}rect{paint-order:stroke}", "paint-order='stroke'")]
    [InlineData("@supports(paint-order:stroke fill){rect{paint-order:stroke}}", "")]
    public void PaintOrderUsesSvgCascade(string css, string attributes)
    {
        using var image = Parse(css + "rect{fill:red;stroke:blue;stroke-width:8}",
            $"<g><rect x='16' y='16' width='40' height='28' {attributes}/></g>");
        var strokeFirst = !css.Contains("initial") && !css.Contains("!important");
        AssertPixel(Render(image.Drawing!, 0), 18, 30, strokeFirst ? Colors.Red : Colors.Blue);
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)]
    public void TextCanPaintOnlyItsStroke(bool rasterParse, int route)
    {
        var drawing = TextDrawing("text{fill:none;stroke:blue;stroke-width:4}", "<text x='12' y='54'>H</text>", rasterParse);
        var pixels = Render(drawing, route);
        Assert.True(CountColor(pixels, Colors.Blue) > 100, "fill:none must retain the glyph stroke");
        Assert.Equal(0, CountColor(pixels, Colors.Black));
    }

    [Theory]
    [InlineData("normal", 0)] [InlineData("stroke", 0)]
    [InlineData("normal", 1)] [InlineData("stroke", 1)]
    [InlineData("normal", 2)] [InlineData("stroke", 2)]
    public void TextStrokeMatchesTheIndependentFontOutline(string order, int route)
    {
        var drawing = TextDrawing($"text{{fill:red;stroke:blue;stroke-width:4;paint-order:{order}}}", "<text x='12' y='54'>H</text>");
        var geometry = Outline("H", 12, 54);
        var reference = new DrawingGroup();
        var fill = new GeometryDrawing(Brushes.Red, null, geometry);
        var stroke = new GeometryDrawing(null, new Pen(Brushes.Blue, 4) { DashCap = PenLineCap.Flat }, geometry);
        reference.Children.Add(order == "stroke" ? stroke : fill);
        reference.Children.Add(order == "stroke" ? fill : stroke);
        AssertPixels(Render(reference, route), Render(drawing, route));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void AdjacentSpansRetainDifferentStrokes(int route)
    {
        var drawing = TextDrawing("text{fill:none;stroke-width:3} .first{stroke:red}.second{stroke:blue}",
            "<text x='6' y='54'><tspan class='first'>H</tspan><tspan class='second'>H</tspan></text>");
        var pixels = Render(drawing, route);
        Assert.True(CountColor(pixels, Colors.Red) > 50);
        Assert.True(CountColor(pixels, Colors.Blue) > 50);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TextOpacityCompositesFillAndStrokeAsOneGroup(int route)
    {
        var drawing = TextDrawing("text{fill:red;stroke:blue;stroke-width:8;opacity:.5}", "<text x='12' y='54'>H</text>");
        var pixels = Render(drawing, route);
        Assert.True(CountColor(pixels, Colors.Blue, 127, 128) > 50);
        Assert.InRange(pixels.Where((_, index) => index % 4 == 3).Max(), 127, 128);
    }

    [Theory]
    [InlineData("stroke:blue;stroke-dasharray:4 4;stroke-dashoffset:2", "stroke-dasharray")]
    [InlineData("stroke:currentColor;color:blue;stroke-width:.125em", "stroke-width")]
    [InlineData("stroke:blue;stroke-opacity:50%", "stroke-opacity")]
    public void TextStrokeResolvesTheSharedSvgPaintProperties(string css, string property)
    {
        var drawing = TextDrawing($"text{{fill:none;stroke-width:4;{css}}}", "<text x='12' y='54'>H</text>");
        var ink = Geometries(drawing).Single(value => value.Pen is not null);
        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(ink.Pen!.Brush).Color);
        if (property == "stroke-width") Assert.Equal(6, ink.Pen.Thickness);
        else if (property == "stroke-opacity") Assert.Equal(.5, ink.Pen.Brush!.Opacity);
        else
        {
            Assert.Equal(new double[] { 1, 1 }, ink.Pen.DashStyle!.Dashes);
            Assert.Equal(.5, ink.Pen.DashStyle.Offset);
        }
    }

    [Theory]
    [InlineData("normal", 0)] [InlineData("stroke", 0)]
    [InlineData("normal", 1)] [InlineData("stroke", 1)]
    [InlineData("normal", 2)] [InlineData("stroke", 2)]
    public void PositionedGlyphsShareOnePaintPathWithinTheChunk(string order, int route)
    {
        var drawing = TextDrawing($"text{{fill:red;fill-opacity:.5;stroke:blue;stroke-opacity:.5;stroke-width:6;paint-order:{order}}}",
            "<text x='12' y='54' dx='0 -24'>HH</text>");
        Assert.True(NativeTextOutline.TryGetPath("H", "Helvetica", 48, 400, 0, out _, out var advance, out _));
        var path = new GeometryGroup { FillRule = FillRule.Nonzero };
        path.Children.Add(Outline("H", 12, 54));
        path.Children.Add(Outline("H", 12 + advance - 24, 54));
        var fill = new GeometryDrawing(new SolidColorBrush(Colors.Red) { Opacity = .5 }, null, path);
        var stroke = new GeometryDrawing(null, new Pen(new SolidColorBrush(Colors.Blue) { Opacity = .5 }, 6), path);
        var expected = new DrawingGroup();
        expected.Children.Add(order == "stroke" ? stroke : fill);
        expected.Children.Add(order == "stroke" ? fill : stroke);
        AssertPixels(Render(expected, route), Render(drawing, route));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void AbsolutePositionStartsAnotherPaintChunk(int route)
    {
        var drawing = TextDrawing("text{fill:none;stroke:blue;stroke-opacity:.5;stroke-width:4}",
            "<text x='12 12' y='54'>HH</text>");
        var expected = new DrawingGroup();
        for (var i = 0; i < 2; i++) expected.Children.Add(new GeometryDrawing(null,
            new Pen(new SolidColorBrush(Colors.Blue) { Opacity = .5 }, 4), Outline("H", 12, 54)));
        AssertPixels(Render(expected, route), Render(drawing, route));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TextLengthScalesGlyphAndStrokeAlongTheInlineAxis(int route)
    {
        Assert.True(NativeTextOutline.TryGetPath("H", "Helvetica", 48, 400, 0, out _, out var advance, out _));
        var drawing = TextDrawing("text{fill:none;stroke:blue;stroke-width:4}",
            FormattableString.Invariant($"<text x='6' y='54' textLength='{advance * 1.5:R}' lengthAdjust='spacingAndGlyphs'>H</text>"));
        var expected = new DrawingGroup { Transform = new ScaleTransform(1.5, 1, 6, 54) };
        expected.Children.Add(new GeometryDrawing(null, new Pen(Brushes.Blue, 4), Outline("H", 6, 54)));
        AssertPixels(Render(expected, route), Render(drawing, route));
    }

    [Theory]
    [InlineData("mask='url(#m)'")] [InlineData("style='mask:url(#m)'")]
    public void MaskedSpanDoesNotMergeIntoUnmaskedText(string attribute)
    {
        var drawing = TextDrawing("text{fill:red}", $"<defs><mask id='m'><rect width='80' height='70' fill='black'/></mask></defs><text x='6' y='54'>H<tspan {attribute}>H</tspan></text>");
        var reference = TextDrawing("text{fill:red}", "<text x='6' y='54'>H</text>");
        AssertPixels(Render(reference, 0), Render(drawing, 0));
    }

    [Theory]
    [InlineData("stroke:blue;stroke-dasharray:4 4", "stroke:blue;stroke-dasharray:8 8")]
    [InlineData("stroke:blue;stroke-opacity:.25", "stroke:blue;stroke-opacity:.75")]
    [InlineData("stroke:blue;stroke-width:2", "stroke:blue;stroke-width:6")]
    [InlineData("stroke:blue;paint-order:normal", "stroke:blue;paint-order:stroke")]
    public void SpanPaintChangesRemainIndependent(string first, string second)
    {
        var drawing = TextDrawing($"text{{fill:red;stroke-width:4}}.first{{{first}}}.second{{{second}}}",
            "<text x='6' y='54'><tspan class='first'>H</tspan><tspan class='second'>H</tspan></text>");
        var strokes = Geometries(drawing).Where(value => value.Pen is not null).ToArray();
        Assert.Equal(2, strokes.Length);
    }

    [Fact]
    public void PaintOrderSurvivesUseInstancesAndDrawingClones()
    {
        using var image = Parse("use{fill:red;stroke:blue;stroke-width:8}.first{paint-order:stroke}.second{paint-order:fill}",
            "<defs><rect id='r' x='12' y='16' width='20' height='28'/></defs><use class='first' href='#r'/><use class='second' href='#r' x='36'/>");
        foreach (var drawing in new[] { image.Drawing!, image.Drawing!.Clone(), image.Drawing.CloneCurrentValue() })
        {
            AssertPixel(Render(drawing, 0), 14, 30, Colors.Red);
            AssertPixel(Render(drawing, 0), 50, 30, Colors.Blue);
        }
    }

    [Theory]
    [InlineData("pathLength='1'")]
    [InlineData("style='path-length:1px'")]
    public void GeometryOnlyPathLengthDoesNotCalibrateTextStrokes(string attribute)
    {
        const string css = "text{fill:none;stroke:blue;stroke-width:4;stroke-dasharray:4 4}";
        var drawing = TextDrawing(css, $"<text x='12' y='54' {attribute}>H</text>");
        var reference = TextDrawing(css, "<text x='12' y='54'>H</text>");
        AssertPixels(Render(reference, 0), Render(drawing, 0));
    }

    [Theory]
    [InlineData("O", 0)] [InlineData("O", 1)] [InlineData("O", 2)]
    [InlineData("轮廓", 0)] [InlineData("轮廓", 1)] [InlineData("轮廓", 2)]
    public void CurvedAndFallbackGlyphContoursKeepStrokeCoverageUnified(string text, int route)
    {
        var drawing = TextDrawing("text{fill:none;stroke:blue;stroke-width:4;stroke-opacity:.5}", $"<text x='0' y='54'>{text}</text>");
        var reference = new GeometryDrawing(null, new Pen(new SolidColorBrush(Colors.Blue) { Opacity = .5 }, 4), Outline(text, 0, 54));
        AssertPixels(Render(reference, route), Render(drawing, route));
    }

    [Theory]
    [InlineData("AV", "stroke-width:100px", false)] [InlineData("AV", "stroke-width:100px", true)]
    [InlineData("AV", "paint-order:stroke", false)] [InlineData("AV", "paint-order:stroke", true)]
    [InlineData("fi", "stroke-width:100px", false)] [InlineData("fi", "stroke-width:100px", true)]
    [InlineData("fi", "paint-order:stroke", false)] [InlineData("fi", "paint-order:stroke", true)]
    public void InactiveStrokeStylesDoNotBreakKerningOrLigatures(string text, string style, bool raster)
    {
        var reference = TextDrawing("text{fill:red}", $"<text x='12' y='54'>{text}</text>", raster);
        var drawing = TextDrawing("text{fill:red}", $"<text x='12' y='54'>{text[0]}<tspan style='{style}'>{text[1]}</tspan></text>", raster);
        Assert.Equal(reference.Bounds, drawing.Bounds);
        if (raster) AssertPixels(Render(reference, 0), Render(drawing, 0));
    }

    private static IEnumerable<GeometryDrawing> Geometries(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometry) yield return geometry;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var item in Geometries(child)) yield return item;
    }

    private static SvgImage Parse(string css, string body) => SvgImage.FromSvgString(
        $"<svg xmlns='http://www.w3.org/2000/svg' width='80' height='70'><style>{css}</style>{body}</svg>");

    private static DrawingGroup TextDrawing(string css, string body, bool raster = true)
    {
        var xml = $"<svg xmlns='http://www.w3.org/2000/svg' width='80' height='70'><style>text{{font:48px Helvetica}}{css}</style>{body}</svg>";
        return raster ? SvgParser.ParseWithTextOutlines(xml).Drawing : SvgParser.Parse(xml).Drawing;
    }

    private static Geometry Outline(string text, double x, double baseline)
    {
        Assert.True(NativeTextOutline.TryGetPath(text, "Helvetica", 48, 400, 0, out var data, out _, out var ascent));
        var path = PathMarkupParser.ParseSvgPathData(data).GetFlattenedPathGeometry(.01, ToleranceType.Absolute);
        foreach (var figure in path.Figures) figure.IsClosed = true;
        path.FillRule = FillRule.Nonzero;
        Point Map(Point point) => new(point.X + x, point.Y + baseline - ascent);
        foreach (var figure in path.Figures)
        {
            figure.StartPoint = Map(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line) line.Point = Map(line.Point);
                else if (segment is PolyLineSegment lines)
                    for (var i = 0; i < lines.Points.Count; i++) lines.Points[i] = Map(lines.Points[i]);
            }
        }
        return path;
    }

    private static byte[] Render(Drawing drawing, int route)
    {
        if (route == 0) return SoftwareVectorRasterizer.Rasterize(drawing, 80, 70, new Rect(0, 0, 80, 70))!;
        var bitmap = new RenderTargetBitmap(80, 70, 96, 96, PixelFormats.Bgra32);
        var context = new SoftwareDrawingContext(bitmap);
        if (route == 1) drawing.RenderTo(context);
        else
        {
            var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); drawing.RenderTo(recorder);
            DrawingReplayer.Replay(recorder.Commit(), context);
        }
        context.Close(); return bitmap.GetPixelBuffer();
    }

    private static int CountColor(byte[] pixels, Color color, int minAlpha = 255, int maxAlpha = 255)
    {
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] == color.B && pixels[i + 1] == color.G && pixels[i + 2] == color.R && pixels[i + 3] >= minAlpha && pixels[i + 3] <= maxAlpha) count++;
        return count;
    }

    private static void AssertPixel(byte[] pixels, int x, int y, Color color)
    {
        var offset = (y * 80 + x) * 4;
        Assert.Equal(new byte[] { color.B, color.G, color.R, 255 }, pixels.AsSpan(offset, 4).ToArray());
    }

    private static void AssertPixels(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 2);
    }
}
