using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSvgMarkerTests
{
    private const string Arrow = "<path d='M0,-4 L8,0 L0,4 Z' fill='red'/>";
    private const string Marker = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='8' markerHeight='8' overflow='visible' orient='auto'>" + Arrow + "</marker></defs>";
    private static string At(double x, double y, double angle = 0) =>
        FormattableString.Invariant($"<g transform='translate({x},{y}) rotate({angle})'>{Arrow}</g>");

    public static IEnumerable<object[]> Scenes()
    {
        var cases = new (string Name, string Source, string Reference)[]
        {
            ("end", Marker + "<line x1='20' y1='30' x2='80' y2='30' marker-end='url(#m)'/>", At(80, 30)),
            ("start-reverse", Marker.Replace("orient='auto'", "orient='auto-start-reverse'") + "<line x1='20' y1='30' x2='80' y2='30' marker-start='url(#m)' marker-end='url(#m)'/>", At(20, 30, 180) + At(80, 30)),
            ("middle", Marker + "<polyline fill='none' points='20,30 80,30 80,70' marker-mid='url(#m)'/>", At(80, 30, 45)),
            ("cubic", Marker + "<path fill='none' d='M20,30 C20,50 60,50 80,30' marker-start='url(#m)' marker-end='url(#m)'/>", At(20, 30, 90) + At(80, 30, -45)),
            ("quadratic", Marker + "<path fill='none' d='M20,30 Q20,70 80,30' marker-end='url(#m)'/>", At(80, 30, -33.690067525979785)),
            ("arc", Marker + "<path fill='none' d='M20,30 A20,20 0 0 1 40,50' marker-start='url(#m)' marker-end='url(#m)'/>", At(20, 30) + At(40, 50, 90)),
            ("subpaths", Marker + "<path fill='none' d='M20,30 H50 M80,50 H110' marker='url(#m)'/>", At(20, 30) + At(50, 30) + At(80, 50) + At(110, 50)),
            ("stylesheet", Marker + "<style>@supports (marker-end:url(#m)){g{--tip:url(#m);marker-end:var(--tip)}} .off{marker-end:none}</style><g><line x1='20' y1='30' x2='80' y2='30'/><line class='off' x1='20' y1='60' x2='80' y2='60'/></g>", At(80, 30)),
            ("stroke-units", Marker.Replace("markerUnits='userSpaceOnUse'", "markerUnits='strokeWidth'") + "<path fill='none' stroke-width='2' d='M20,30 H80' marker-end='url(#m)'/>", "<g transform='translate(80,30) scale(2)'>" + Arrow + "</g>"),
            ("context-stroke", Marker.Replace("fill='red'", "fill='context-stroke'") + "<path fill='none' stroke='blue' stroke-width='0' d='M20,30 H80' marker-end='url(#m)'/>", At(80, 30).Replace("fill='red'", "fill='blue'")),
            ("definition-inheritance", Marker.Replace(" fill='red'", "") + "<g fill='blue'><path fill='none' d='M20,30 H80' marker-end='url(#m)'/></g>", At(80, 30).Replace("fill='red'", "fill='black'")),
            ("opacity", Marker + "<g opacity='.5'><path fill='none' opacity='.5' d='M20,30 H80' marker-end='url(#m)'/></g>", "<g opacity='.25'>" + At(80, 30) + "</g>"),
            ("marker-ellipse", "<marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='20' refX='10' refY='10' orient='90' overflow='visible'><ellipse cx='10' cy='10' rx='8' ry='4' fill='red'/></marker><path fill='none' d='M20,30H80' marker-end='url(#m)'/>", "<ellipse cx='80' cy='30' rx='4' ry='8' fill='red'/>"),
        };
        foreach (var scene in cases)
            for (var route = 0; route < 5; route++) yield return [scene.Name, scene.Source, scene.Reference, route];
    }

    [Theory, MemberData(nameof(Scenes))]
    public void MarkersMatchExplicitlyPlacedReferenceGraphics(string name, string source, string reference, int route)
    {
        using var actual = Parse(source);
        using var expected = Parse(reference);
        var pixels = Paint(expected, route);
        Assert.Contains(pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        AssertPixels(pixels, Paint(actual, route), name);
    }

    [Theory]
    [InlineData("hidden", 10)] [InlineData("visible", 18)] [InlineData("initial", 18)]
    [InlineData("unset", 18)] [InlineData("revert", 10)] [InlineData("clip", 10)]
    public void MarkerViewportClippingUsesUaDefaultsAndCssWideValues(string overflow, double size)
    {
        var definition = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='10' markerHeight='10' refX='5' refY='5' style='overflow:" + overflow + "'><rect x='-4' y='-4' width='18' height='18' fill='red'/></marker></defs>";
        using var actual = Parse(definition + "<line x1='20' y1='30' x2='80' y2='30' marker-end='url(#m)'/>");
        using var expected = Parse(FormattableString.Invariant($"<rect x='{80 - size / 2}' y='{30 - size / 2}' width='{size}' height='{size}' fill='red'/>"));
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(actual, route), overflow);
    }

    [Theory]
    [InlineData("normal", "fsm")] [InlineData("markers", "mfs")]
    [InlineData("stroke markers", "smf")] [InlineData("markers stroke fill", "msf")]
    [InlineData("fill markers stroke", "fms")] [InlineData("stroke", "sfm")]
    public void PaintOrderPositionsMarkersRelativeToFillAndStroke(string order, string expectedOrder)
    {
        const string fill = "<rect x='40' y='30' width='40' height='30' fill='blue'/>";
        const string stroke = "<rect x='40' y='30' width='40' height='30' fill='none' stroke='green' stroke-width='8'/>";
        const string marker = "<rect x='35' y='25' width='10' height='10' fill='red'/>";
        var definition = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='10' markerHeight='10' refX='5' refY='5'><rect width='10' height='10' fill='red'/></marker></defs>";
        using var actual = Parse(definition + "<rect x='40' y='30' width='40' height='30' fill='blue' stroke='green' stroke-width='8' marker-start='url(#m)' paint-order='" + order + "'/>");
        using var expected = Parse(string.Concat(expectedOrder.Select(operation => operation == 'f' ? fill : operation == 's' ? stroke : marker)));
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(actual, route), order);
    }

    [Theory]
    [InlineData("0.25turn")] [InlineData("90deg")] [InlineData("100grad")] [InlineData("1.5707963267948966rad")]
    public void ExplicitOrientAnglesUseTheirUnits(string angle)
    {
        using var actual = Parse(Marker.Replace("orient='auto'", "orient='" + angle + "'") + "<path fill='none' d='M20,30H80' marker-end='url(#m)'/>");
        using var expected = Parse(At(80, 30, 90));
        AssertPixels(Paint(expected, 0), Paint(actual, 0), angle);
    }

    [Theory]
    [InlineData("50%", "50%")] [InlineData("center", "center")] [InlineData("5", "5")]
    public void ViewBoxReferencePointIsAppliedAfterAnisotropicMapping(string x, string y)
    {
        var definition = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='10' viewBox='0 0 10 10' preserveAspectRatio='none' refX='" + x + "' refY='" + y + "'><rect width='10' height='10' fill='red'/></marker></defs>";
        using var actual = Parse(definition + "<line x1='20' y1='30' x2='80' y2='30' marker-end='url(#m)'/>");
        using var expected = Parse("<rect x='70' y='25' width='20' height='10' fill='red'/>");
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(actual, route), x);
    }

    [Theory]
    [InlineData("xMinYMin meet", 80, 15)] [InlineData("xMidYMid meet", 75, 20)] [InlineData("xMaxYMax meet", 75, 15)]
    public void ViewBoxAlignmentChangesTheClippedViewport(string aspect, int x, int width)
    {
        var definition = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='10' viewBox='0 0 10 10' preserveAspectRatio='" + aspect + "'><rect x='-5' width='20' height='10' fill='red'/></marker></defs>";
        using var actual = Parse(definition + "<line x1='20' y1='30' x2='80' y2='30' marker-end='url(#m)'/>");
        using var expected = Parse($"<rect x='{x}' y='30' width='{width}' height='10' fill='red'/>");
        AssertPixels(Paint(expected, 0), Paint(actual, 0), aspect);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ContextGradientsKeepTheSourceBoundingBoxAndUserSpace(bool radial)
    {
        var gradient = radial
            ? "<radialGradient id='g' gradientUnits='userSpaceOnUse' cx='60' cy='40' r='40'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>"
            : "<linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        var definition = "<defs>" + gradient + "<marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='20' refX='10' refY='10'><rect width='20' height='20' fill='context-fill'/></marker></defs>";
        using var actual = Parse(definition + "<rect x='40' y='30' width='40' height='30' fill='url(#g)' marker-start='url(#m)'/>");
        var expectedGradient = radial ? gradient : "<linearGradient id='g' gradientUnits='userSpaceOnUse' x1='40' y1='30' x2='80' y2='30'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        using var expected = Parse("<defs>" + expectedGradient + "</defs><rect x='40' y='30' width='40' height='30' fill='url(#g)'/><rect x='30' y='20' width='20' height='20' fill='url(#g)'/>");
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(actual, route), "context gradient " + radial);
    }

    [Fact]
    public void MarkerInkDoesNotChangeObjectBoundingBoxClips()
    {
        const string definition = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='30' markerHeight='30' refX='15' refY='15'><rect width='30' height='30' fill='red'/></marker><clipPath id='c' clipPathUnits='objectBoundingBox'><rect width='.5' height='1'/></clipPath></defs>";
        using var actual = Parse(definition + "<rect x='40' y='30' width='40' height='30' fill='blue' marker-start='url(#m)' clip-path='url(#c)'/>");
        using var expected = Parse("<defs><clipPath id='c'><rect x='40' y='30' width='20' height='30'/></clipPath></defs><g clip-path='url(#c)'><rect x='40' y='30' width='40' height='30' fill='blue'/><rect x='25' y='15' width='30' height='30' fill='red'/></g>");
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(actual, route), "object bounds");
    }

    [Theory]
    [InlineData("markerWidth='0'")] [InlineData("markerHeight='-1'")] [InlineData("viewBox='0 0 0 10'")]
    public void EmptyOrInvalidMarkerViewportsDoNotPaint(string attribute)
    {
        using var actual = Parse("<marker id='m' " + attribute + "><rect width='100' height='100' fill='red'/></marker><rect x='40' y='30' width='40' height='30' fill='blue' marker-start='url(#m)'/>");
        using var expected = Parse("<rect x='40' y='30' width='40' height='30' fill='blue'/>");
        AssertPixels(Paint(expected, 0), Paint(actual, 0), attribute);
    }

    [Fact]
    public void RecursiveMarkersStopAtTheActiveDefinitionAndLeaveOtherPaintIntact()
    {
        using var actual = Parse("<marker id='m' markerUnits='userSpaceOnUse' markerWidth='10' markerHeight='10'><rect width='10' height='10' fill='red' marker-start='url(#m)'/></marker><path fill='none' d='M20,30H80' marker-end='url(#m)'/>");
        using var expected = Parse("<rect x='80' y='30' width='10' height='10' fill='red'/>");
        AssertPixels(Paint(expected, 0), Paint(actual, 0), "recursive marker");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DeferredSvgMasksRenderMarkerContent(bool contextual)
    {
        var marker = Marker.Replace("fill='red'", contextual ? "fill='context-stroke'" : "fill='white'");
        var mask = "<mask id='mask' maskUnits='userSpaceOnUse' x='0' y='0' width='160' height='90' mask-type='alpha'>";
        using var actual = Parse(marker + "<defs>" + mask + "<path fill='none' stroke='white' stroke-width='0' d='M20,30H80' marker-end='url(#m)'/></mask></defs><rect width='160' height='90' fill='blue' mask='url(#mask)'/>");
        using var expected = Parse("<defs>" + mask + At(80, 30) + "</mask></defs><rect width='160' height='90' fill='blue' mask='url(#mask)'/>");
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(actual, route), "SVG mask " + contextual);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UseInstancesSupplyTheirOwnContextPaintAndRestoreItAfterward(bool gradient)
    {
        const string definition = "<linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        var fill = gradient ? "url(#g)" : "blue";
        using var actual = Parse("<defs>" + definition + "<rect id='r' width='40' height='30' fill='context-fill'/></defs><use href='#r' x='20' y='20' fill='" + fill + "'/><rect x='100' y='20' width='20' height='30' fill='context-fill'/>");
        using var expected = Parse("<defs>" + definition + "</defs><rect x='20' y='20' width='40' height='30' fill='" + fill + "'/>");
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(actual, route), "use context " + gradient);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MarkerDefinitionsApplyTheirOwnClipAndMask(bool mask)
    {
        var effect = mask ? "<mask id='c' maskUnits='userSpaceOnUse' x='0' y='0' width='10' height='10' mask-type='alpha'><rect width='5' height='10' fill='white'/></mask>"
            : "<clipPath id='c'><rect width='5' height='10'/></clipPath>";
        var property = mask ? "mask" : "clip-path";
        using var actual = Parse("<defs>" + effect + "<marker id='m' markerUnits='userSpaceOnUse' markerWidth='10' markerHeight='10' " + property + "='url(#c)'><rect width='10' height='10' fill='red'/></marker></defs><path fill='none' d='M20,30H80' marker-end='url(#m)'/>");
        using var expected = Parse("<rect x='80' y='30' width='5' height='10' fill='red'/>");
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(actual, route), property);
    }

    [Fact]
    public void ContextGradientCanPaintTheMarkerStroke()
    {
        const string gradient = "<linearGradient id='g' gradientUnits='userSpaceOnUse' x1='40' y1='30' x2='80' y2='30'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        using var actual = Parse("<defs>" + gradient + "<marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='10' refX='10' refY='5' overflow='visible'><line x1='0' y1='5' x2='20' y2='5' stroke='context-fill' stroke-width='6'/></marker></defs><rect x='40' y='30' width='40' height='30' fill='url(#g)' marker-start='url(#m)'/>");
        using var expected = Parse("<defs>" + gradient + "</defs><rect x='40' y='30' width='40' height='30' fill='url(#g)'/><path d='M30,30H50' fill='none' stroke='url(#g)' stroke-width='6'/>");
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(actual, route), "context gradient stroke");
    }

    [Theory]
    [InlineData(0,1)] [InlineData(1,1)] [InlineData(2,1)] [InlineData(3,1)]
    [InlineData(0,2)] [InlineData(1,2)] [InlineData(2,2)] [InlineData(3,2)]
    [InlineData(0,3)] [InlineData(1,3)] [InlineData(2,3)] [InlineData(3,3)]
    public void QuarterTurnsKeepEllipseRasterCoverageAtEveryScale(int turns, int dpi)
    {
        using var actual = Parse($"<marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='20' refX='10' refY='10' orient='{turns * 90}' overflow='visible'><ellipse cx='10' cy='10' rx='8' ry='4' fill='red'/></marker><path fill='none' d='M20,30H80' marker-end='url(#m)'/>");
        using var expected = Parse($"<ellipse cx='80' cy='30' rx='{(turns % 2 == 0 ? 8 : 4)}' ry='{(turns % 2 == 0 ? 4 : 8)}' fill='red'/>");
        var a = SoftwareVectorRasterizer.Rasterize(actual.Drawing!, 160*dpi, 90*dpi, new Rect(0,0,160,90))!;
        var b = SoftwareVectorRasterizer.Rasterize(expected.Drawing!, 160*dpi, 90*dpi, new Rect(0,0,160,90))!;
        AssertPixels(b, a, $"quarter turn {turns}, DPI{dpi}");
    }

    [Fact]
    public void EditingMarkerDrawingInvalidatesTheSvgImageCache()
    {
        using var image = Parse(Marker + "<path fill='none' d='M20,30H80' marker-end='url(#m)'/>");
        _ = Paint(image, 3); var generation = image.ContentGeneration;
        var marker = Assert.Single(Geometries(image.Drawing!), drawing => drawing.Brush is SolidColorBrush { Color: var color } && color == Colors.Red);
        marker.Brush = Brushes.Green;
        Assert.True(image.ContentGeneration > generation);
        using var expected = Parse(At(80, 30).Replace("fill='red'", "fill='green'"));
        for (var route = 0; route < 5; route++) AssertPixels(Paint(expected, route), Paint(image, route), "edited marker");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FrozenMarkerContextPaintCanBeClonedAndReadOnAnotherThread(bool currentValue)
    {
        using var image = Parse("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='20'><rect width='20' height='20' fill='context-fill'/></marker></defs><rect x='40' y='30' width='40' height='30' fill='url(#g)' marker-start='url(#m)'/>");
        var drawing = currentValue ? image.Drawing!.CloneCurrentValue() : image.Drawing!.Clone(); drawing.Freeze();
        var expected = Paint(image, 0); byte[]? actual = null; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { actual = SoftwareVectorRasterizer.Rasterize(drawing, 160, 90, new Rect(0, 0, 160, 90)); }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(10))); Assert.Null(failure);
        AssertPixels(expected, actual!, "frozen marker context paint");
    }

    private static IEnumerable<GeometryDrawing> Geometries(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometry) yield return geometry;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var item in Geometries(child)) yield return item;
    }

    [Theory]
    [InlineData("M20,20L20,20V60", "90,90,90")]
    [InlineData("M20,20H50L50,20V50", "0,0,90,90")]
    [InlineData("M20,20C20,20 20,20 60,20", "0,0")]
    [InlineData("M20,20Q20,20 20,60", "90,90")]
    [InlineData("M20,20H60V60Z", "-67.5,45,157.5,-67.5")]
    public void VertexDirectionsPreserveDegenerateAndClosedPathSemantics(string path, string expected)
    {
        var positions = SvgMarkerPositions.Get(PathMarkupParser.Parse(path));
        var angles = expected.Split(',').Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(angles.Length, positions.Count);
        for (var index = 0; index < angles.Length; index++) Assert.InRange(Math.Abs(positions[index].Angle - angles[index]), 0, 1e-9);
        Assert.Equal(SvgMarkerPositions.Kind.Start, positions[0].Kind);
        Assert.Equal(SvgMarkerPositions.Kind.End, positions[^1].Kind);
    }

    private static SvgImage Parse(string body) => SvgImage.FromSvgString(
        "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='90'>" + body + "</svg>");

    private static byte[] Paint(SvgImage image, int route)
    {
        if (route == 0) return SoftwareVectorRasterizer.Rasterize(image.Drawing!, 160, 90, new Rect(0, 0, 160, 90))!;
        var bitmap = new RenderTargetBitmap(160, 90, 96, 96, PixelFormats.Bgra32);
        var context = new SoftwareDrawingContext(bitmap);
        if (route == 1) image.Drawing!.RenderTo(context);
        else if (route == 2)
        {
            var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); image.Drawing!.RenderTo(recorder);
            DrawingReplayer.Replay(recorder.Commit(), context);
        }
        else if (route == 3) context.DrawImage(image, new Rect(0, 0, 160, 90));
        else context.DrawRectangle(new ImageBrush(image) { Stretch = Stretch.Fill }, null, new Rect(0, 0, 160, 90));
        context.Close(); return bitmap.GetPixelBuffer();
    }

    private static void AssertPixels(byte[] expected, byte[] actual, string name)
    {
        Assert.Equal(expected.Length, actual.Length);
        var worst = 0; var differences = 0;
        for (var index = 0; index < expected.Length; index++)
        {
            var delta = Math.Abs(expected[index] - actual[index]); worst = Math.Max(worst, delta);
            if (delta > 3) differences++;
        }
        Assert.True(differences == 0, $"{name}: {differences} channels differ, maximum {worst}");
    }
}
