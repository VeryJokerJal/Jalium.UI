using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using ShapePath = Jalium.UI.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class GeometrySerializationTests
{
    public const string ScreenshotPath = "M156,165.76Q223,217.76,317,163.76Q413,95.76,487,163.76";
    private const string ScreenshotMarkup = "M 156,165.76 Q 223,217.76 317,163.76 Q 413,95.76 487,163.76";

    [Fact]
    public void PathDataRoundTripPreservesTheTwoQuadraticCurves()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        var path = new ShapePath { Data = ScreenshotPath, Stroke = Brushes.Black, StrokeThickness = 2 };
        var original = Assert.IsType<PathGeometry>(path.Data);

        string text = path.Data!.ToString();

        Assert.Equal(ScreenshotMarkup, text);
        var restored = Assert.IsType<PathGeometry>(Geometry.Parse(text));
        AssertEquivalent(original, restored);
        Assert.Equal(2, Assert.Single(restored.Figures).Segments.Count);
        Assert.All(restored.Figures[0].Segments, segment => Assert.IsType<QuadraticBezierSegment>(segment));
    }

    [Theory]
    [InlineData("M1.25,2.5 Q12.5,-24.75 36.125,48.875 T70.25,15.5")]
    [InlineData("m10,20 q30,-40 50,60 t70,-80")]
    [InlineData("M1,2 C3.25,-4.5 5.75,6.125 7.5,8.25 S19.5,20.25 21.75,22.5")]
    [InlineData("m1,2 c3,4 5,6 7,8 s9,10 11,12")]
    [InlineData("M10,20 A40,25 35 0 0 80,70")]
    [InlineData("M10,20 A40,25 35 0 1 80,70")]
    [InlineData("M10,20 A40,25 35 1 0 80,70")]
    [InlineData("M10,20 A40,25 35 1 1 80,70")]
    [InlineData("F1 M0,0 Q20,40 40,0 C50,-10 70,10 80,0 A10,20 25 1 0 110,30 Z M150,20 l10,5")]
    public void CurveMarkupRoundTripsControlPointsArcParametersAndFigures(string source)
    {
        var original = Assert.IsType<PathGeometry>(Geometry.Parse(source));
        var restored = Assert.IsType<PathGeometry>(Geometry.Parse(original.ToString(CultureInfo.InvariantCulture)));

        AssertEquivalent(original, restored);
    }

    [Fact]
    public void SerializationReadsEditedControlPointsRatherThanTheOriginalText()
    {
        var geometry = Assert.IsType<PathGeometry>(Geometry.Parse(ScreenshotPath));
        var first = Assert.IsType<QuadraticBezierSegment>(geometry.Figures[0].Segments[0]);
        first.Point1 = new Point(245.125, 240.875);
        var clone = geometry.Clone();
        Assert.IsType<QuadraticBezierSegment>(clone.Figures[0].Segments[1]).Point2 = new Point(510.5, 175.25);

        var restored = Assert.IsType<PathGeometry>(Geometry.Parse(clone.ToString(CultureInfo.InvariantCulture)));

        AssertEquivalent(clone, restored);
        Assert.Equal(new Point(245.125, 240.875), Assert.IsType<QuadraticBezierSegment>(restored.Figures[0].Segments[0]).Point1);
        Assert.Equal(new Point(487, 163.76), Assert.IsType<QuadraticBezierSegment>(geometry.Figures[0].Segments[1]).Point2);
    }

    [Fact]
    public void ConstructedPolyCurvesKeepEveryControlPointInNativeCommands()
    {
        var figure = new PathFigure { StartPoint = new Point(1, 2), IsClosed = true };
        figure.Segments.Add(new PolyBezierSegment(new[]
        {
            new Point(3, 4), new Point(5, 6), new Point(7, 8),
            new Point(9, 10), new Point(11, 12), new Point(13, 14)
        }, true));
        figure.Segments.Add(new PolyQuadraticBezierSegment(new[]
        {
            new Point(15, 16), new Point(17, 18), new Point(19, 20), new Point(21, 22)
        }, true));
        var geometry = new PathGeometry(new[] { figure }, FillRule.Nonzero, null);

        var restored = Assert.IsType<PathGeometry>(Geometry.Parse(geometry.ToString(CultureInfo.InvariantCulture)));
        var restoredFigure = Assert.Single(restored.Figures);

        Assert.Equal(FillRule.Nonzero, restored.FillRule);
        Assert.True(restoredFigure.IsClosed);
        Assert.Collection(restoredFigure.Segments,
            segment => Assert.IsType<BezierSegment>(segment),
            segment => Assert.IsType<BezierSegment>(segment),
            segment => Assert.IsType<QuadraticBezierSegment>(segment),
            segment => Assert.IsType<QuadraticBezierSegment>(segment));
        Assert.Equal(NativeCommands(figure, true), NativeCommands(restoredFigure, true));
    }

    [Fact]
    public void ScreenshotRoundTripSendsTwoNativeQuadraticCommands()
    {
        var geometry = Geometry.Parse(ScreenshotPath);
        var restored = Assert.IsType<PathGeometry>(Geometry.Parse(geometry.ToString(CultureInfo.InvariantCulture)));

        Assert.Equal(new float[]
        {
            3, 223, 217.76f, 317, 163.76f,
            3, 413, 95.76f, 487, 163.76f
        }, NativeCommands(Assert.Single(restored.Figures), true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCurveCommandsSurviveStringRoundTrip(bool nativeArcs)
    {
        var original = Assert.IsType<PathGeometry>(Geometry.Parse(
            "M10,20 Q30,-40 50,60 C60,70 80,90 100,110 A40,25 35 1 0 160,140"));
        var restored = Assert.IsType<PathGeometry>(Geometry.Parse(original.ToString(CultureInfo.InvariantCulture)));

        Assert.Equal(NativeCommands(original.Figures[0], nativeArcs), NativeCommands(restored.Figures[0], nativeArcs));
    }

    [Theory]
    [InlineData(ScreenshotPath)]
    [InlineData("M40,140 C100,20 200,240 300,80 S430,30 510,150")]
    [InlineData("M70,170 A100,65 25 1 1 290,130")]
    public void CurveStringRoundTripRendersIdenticalNonemptyPixels(string source)
    {
        Geometry original = Geometry.Parse(source);
        Geometry restored = Geometry.Parse(original.ToString(CultureInfo.InvariantCulture));

        byte[] expected = RenderPixels(original);
        byte[] actual = RenderPixels(restored);

        Assert.True(expected.Where((_, index) => index % 4 != 3).Any(channel => channel < 128));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StreamGeometryRoundTripRetainsCurvesFillRuleAndClosure()
    {
        var stream = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = stream.Open())
        {
            context.BeginFigure(new Point(10, 20), true, true);
            context.QuadraticBezierTo(new Point(30, -40), new Point(50, 60), true, false);
            context.BezierTo(new Point(60, 70), new Point(80, 90), new Point(100, 110), true, false);
            context.ArcTo(new Point(160, 140), new Size(40, 25), 35, true, SweepDirection.Counterclockwise, true, false);
        }

        Geometry value = stream;
        var restored = Assert.IsType<PathGeometry>(Geometry.Parse(value.ToString(CultureInfo.InvariantCulture)));

        AssertEquivalent(stream.GetPathGeometry()!, restored);
        Assert.Equal(FillRule.Nonzero, restored.FillRule);
        Assert.Equal(NativeCommands(stream.GetPathGeometry()!.Figures[0], true), NativeCommands(restored.Figures[0], true));
    }

    [Fact]
    public void ReopenedStreamSerializesItsCurrentGeometryAndFillRule()
    {
        var stream = new StreamGeometry();
        using (var context = stream.Open())
        {
            context.BeginFigure(new Point(0, 0), true, false);
            context.QuadraticBezierTo(new Point(10, 20), new Point(30, 40), true, false);
        }
        using (var context = stream.Open())
        {
            context.BeginFigure(new Point(50, 60), true, false);
            context.BezierTo(new Point(70, 80), new Point(90, 100), new Point(110, 120), true, false);
        }
        stream.FillRule = FillRule.Nonzero;

        var restored = Assert.IsType<PathGeometry>(Geometry.Parse(stream.ToString(CultureInfo.InvariantCulture)));

        Assert.Equal(FillRule.Nonzero, restored.FillRule);
        AssertEquivalent(stream.GetPathGeometry()!, restored);
        Assert.IsType<BezierSegment>(Assert.Single(Assert.Single(restored.Figures).Segments));
    }

    [Theory]
    [InlineData(FillRule.EvenOdd)]
    [InlineData(FillRule.Nonzero)]
    public void EmptyPathsAndStreamsRetainTheirFillRule(FillRule fillRule)
    {
        foreach (Geometry geometry in new Geometry[]
        {
            new PathGeometry { FillRule = fillRule }, new StreamGeometry { FillRule = fillRule }
        })
        {
            var restored = Assert.IsType<PathGeometry>(Geometry.Parse(geometry.ToString(CultureInfo.InvariantCulture)));
            Assert.Empty(restored.Figures);
            Assert.Equal(fillRule, restored.FillRule);
        }
    }

    [Fact]
    public void StringConverterAndValueSerializerRetainCurves()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        Geometry geometry = Geometry.Parse(ScreenshotPath);
        var converter = TypeDescriptor.GetConverter(typeof(Geometry));
        var serializer = ValueSerializer.GetSerializerFor(typeof(Geometry));
        Assert.NotNull(serializer);

        foreach (string text in new[]
        {
            geometry.ToString(), geometry.ToString(CultureInfo.InvariantCulture),
            ((IFormattable)geometry).ToString(null, CultureInfo.InvariantCulture),
            converter.ConvertToInvariantString(geometry)!, serializer!.ConvertToString(geometry, null)
        })
        {
            Assert.Equal(ScreenshotMarkup, text);
            AssertEquivalent((PathGeometry)geometry, Assert.IsType<PathGeometry>(Geometry.Parse(text)));
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void XamlValueSerializerUsesInvariantPathMarkup(string cultureName)
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo(cultureName));
        var original = Assert.IsType<PathGeometry>(Geometry.Parse(ScreenshotPath));
        var serializer = ValueSerializer.GetSerializerFor(typeof(Geometry))!;

        string text = serializer.ConvertToString(original, null);

        Assert.Equal(ScreenshotMarkup, text);
        AssertEquivalent(original, Assert.IsType<PathGeometry>(serializer.ConvertFromString(text, null)));
    }

    [Fact]
    public async Task FrozenCurvesCanBeSerializedOnAnotherThread()
    {
        var geometry = Geometry.Parse(ScreenshotPath);
        geometry.Freeze();

        string text = await Task.Run(() => geometry.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(ScreenshotMarkup, text);
    }

    [Fact]
    public void ExplicitFlatteningStillProducesLinesWithoutChangingTheSource()
    {
        var original = Assert.IsType<PathGeometry>(Geometry.Parse(ScreenshotPath));
        var flattened = original.GetFlattenedPathGeometry(0.25, ToleranceType.Absolute);

        Assert.False(flattened.MayHaveCurves());
        Assert.True(flattened.Figures[0].Segments.Count > 2);
        Assert.True(original.MayHaveCurves());
        Assert.Equal(ScreenshotMarkup, original.ToString(CultureInfo.InvariantCulture));
    }

    private static List<float> NativeCommands(PathFigure figure, bool nativeArcs)
    {
        var commands = new List<float>();
        var method = typeof(RenderTargetDrawingContext).GetMethod("AppendFigureSegments", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(null, new object[] { commands, figure, figure.StartPoint, 0d, 0d, false, nativeArcs });
        return commands;
    }

    private static byte[] RenderPixels(Geometry geometry)
    {
        const int width = 600, height = 260;
        var path = new ShapePath
        {
            Data = geometry, Stroke = Brushes.Black, StrokeThickness = 2,
            Fill = null, Width = width, Height = height, Stretch = Stretch.None
        };
        path.Measure(new Size(width, height));
        path.Arrange(new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
        bitmap.Clear(Color.FromRgb(255, 255, 255));
        bitmap.Render(path);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return pixels;
    }

    private static void AssertEquivalent(PathGeometry expected, PathGeometry actual)
    {
        Assert.Equal(expected.FillRule, actual.FillRule);
        Assert.Equal(expected.Figures.Count, actual.Figures.Count);
        for (int i = 0; i < expected.Figures.Count; i++)
        {
            var left = expected.Figures[i];
            var right = actual.Figures[i];
            Assert.Equal(left.StartPoint, right.StartPoint);
            Assert.Equal(left.IsClosed, right.IsClosed);
            Assert.Equal(left.IsFilled, right.IsFilled);
            Assert.Equal(left.Segments.Count, right.Segments.Count);
            for (int j = 0; j < left.Segments.Count; j++)
            {
                var segment = left.Segments[j];
                var restored = right.Segments[j];
                Assert.Equal(segment.GetType(), restored.GetType());
                Assert.Equal(segment.GetPoints(), restored.GetPoints());
                if (segment is ArcSegment arc)
                {
                    var restoredArc = Assert.IsType<ArcSegment>(restored);
                    Assert.Equal(arc.Size, restoredArc.Size);
                    Assert.Equal(arc.RotationAngle, restoredArc.RotationAngle);
                    Assert.Equal(arc.IsLargeArc, restoredArc.IsLargeArc);
                    Assert.Equal(arc.SweepDirection, restoredArc.SweepDirection);
                }
            }
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous;

        public CultureScope(CultureInfo culture)
        {
            _previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = culture;
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
