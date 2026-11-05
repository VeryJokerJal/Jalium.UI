using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Drawing = Jalium.UI.Media.Drawing;

namespace Jalium.UI.Tests;

public sealed class CssGradientTransformTests
{
    private static readonly Rect Canvas = new(0, 0, 200, 96);
    private static readonly Rect Box = new(20, 12, 160, 64);
    private static readonly Point[] Samples = [new(32.5, 24.5), new(68.5, 48.5), new(100.5, 32.5), new(140.5, 56.5)];

    public static IEnumerable<object[]> BrushCases()
    {
        foreach (var radial in new[] { false, true })
        foreach (var absolute in new[] { false, true })
        for (var variant = 0; variant < 5; variant++)
        for (var route = 0; route < 5; route++) yield return [radial, absolute, variant, route];
    }

    [Theory, MemberData(nameof(BrushCases))]
    public void BrushTransformsMapPaintIndependentlyOfGeometry(bool radial, bool absolute, int variant, int route)
    {
        var brush = Brush(radial, absolute);
        var (transform, relative) = Transforms(variant);
        brush.Transform = new MatrixTransform(transform); brush.RelativeTransform = new MatrixTransform(relative);
        var pixels = Paint(new GeometryDrawing(brush, null, new RectangleGeometry(Box)), route);
        Check(pixels, p =>
        {
            p = Inverse(p, transform);
            p = new((p.X - Box.X) / Box.Width, (p.Y - Box.Y) / Box.Height);
            p = Inverse(p, relative);
            return Parameter(new(Box.X + p.X * Box.Width, Box.Y + p.Y * Box.Height), radial, false);
        });
    }

    public static IEnumerable<object[]> SvgCases()
    {
        foreach (var radial in new[] { false, true })
        foreach (var absolute in new[] { false, true })
        for (var variant = 0; variant < 3; variant++)
        for (var route = 0; route < 5; route++) yield return [radial, absolute, variant, route];
    }

    [Theory, MemberData(nameof(SvgCases))]
    public void SvgGradientsTransformBeforeObjectBoundingBoxMapping(bool radial, bool absolute, int variant, int route)
    {
        var transform = variant switch
        {
            1 => absolute ? new Matrix(1.05, .22, -.31, .85, 8, -5) : new Matrix(1.05, .22, -.31, .85, .08, -.05),
            2 => absolute ? new Matrix(.8, -.25, .12, 1.1, 18, 4) : new Matrix(.8, -.25, .12, 1.1, .18, .04),
            _ => Matrix.Identity,
        };
        using var image = SvgImage.FromSvgString(Svg(radial, absolute, MatrixText(transform)));
        var pixels = Paint(image.Drawing!, route);
        Check(pixels, p =>
        {
            if (!absolute) p = new((p.X - Box.X) / Box.Width, (p.Y - Box.Y) / Box.Height);
            p = Inverse(p, transform);
            if (!radial) return Parameter(p, false, !absolute);
            var radius = absolute ? 72 : .45;
            var x = (p.X - (absolute ? 92 : .45)) / radius;
            var y = (p.Y - (absolute ? 44 : .5)) / radius;
            return FocalParameter(x, y);
        });
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)] [InlineData(false, 4)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)] [InlineData(true, 4)]
    public void UserSpaceDefaultsResolveAgainstTheViewport(bool radial, int route)
    {
        var tag = radial ? "radialGradient" : "linearGradient";
        using var image = SvgImage.FromSvgString($"<svg xmlns='http://www.w3.org/2000/svg' width='200' height='96'><{tag} id='g' gradientUnits='userSpaceOnUse'><stop stop-color='black'/><stop offset='1' stop-color='white'/></{tag}><rect x='20' y='12' width='160' height='64' fill='url(#g)'/></svg>");
        Check(Paint(image.Drawing!, route), p => radial
            ? Math.Sqrt(Math.Pow(p.X - 100, 2) + Math.Pow(p.Y - 48, 2)) / Math.Sqrt((200 * 200 + 96 * 96) / 2.0) * 2
            : p.X / 200);
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)] [InlineData(false, 4)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)] [InlineData(true, 4)]
    public void SvgStrokePaintUsesObjectBoundsExcludingStrokeWidth(bool radial, int route)
    {
        var text = Svg(radial, false, "matrix(1.05 .22 -.31 .85 .08 -.4)")
            .Replace("<rect x='20' y='12' width='160' height='64' fill='url(#g)'/>", "<path d='M20,12H180V76H20Z' fill='none' stroke='url(#g)' stroke-width='12'/>");
        using var image = SvgImage.FromSvgString(text);
        Check(Paint(image.Drawing!, route), p =>
        {
            p = Inverse(new((p.X - 20) / 160, (p.Y - 12) / 64), new(1.05, .22, -.31, .85, .08, -.4));
            return radial ? FocalParameter((p.X - .45) / .45, (p.Y - .5) / .45) : Parameter(p, false, true);
        }, [new(32.5, 12.5), new(68.5, 12.5), new(100.5, 12.5), new(140.5, 12.5)]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GradientTemplatesFollowChainsAndCssTransformWins(bool radial)
    {
        var tag = radial ? "radialGradient" : "linearGradient";
        var attrs = radial ? "cx='.45' cy='.5' r='.45'" : "x1='.1' y1='.2' x2='.9' y2='.75'";
        using var image = SvgImage.FromSvgString($"<svg xmlns='http://www.w3.org/2000/svg' width='200' height='96'><style>#g{{--shift:.08;transform:matrix(1.05,.22,-.31,.85,var(--shift),-.05)}}</style><defs><{tag} id='base' {attrs} gradientTransform='translate(.4 .4)'><stop stop-color='black'/><stop offset='1' stop-color='white'/></{tag}><{tag} id='middle' href='#base'/><{tag} id='g' href='#middle'/></defs><rect x='20' y='12' width='160' height='64' fill='url(#g)'/></svg>");
        var m = new Matrix(1.05, .22, -.31, .85, .08, -.05);
        Check(Paint(image.Drawing!, 1), p =>
        {
            p = Inverse(new((p.X - 20) / 160, (p.Y - 12) / 64), m);
            return radial ? Math.Sqrt(Math.Pow(p.X - .45, 2) + Math.Pow(p.Y - .5, 2)) / .45 : Parameter(p, false, true);
        });
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void NoninvertibleBrushTransformProducesNoPaint(int route)
    {
        var brush = Brush(false, false); brush.Transform = new MatrixTransform(new(0, 0, 0, 1, 0, 0));
        var pixels = Paint(new GeometryDrawing(brush, null, new RectangleGeometry(Box)), route);
        foreach (var p in Samples) Assert.Equal(0, pixels[((int)p.Y * 200 + (int)p.X) * 4 + 3]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NestedTransformMutationChangesHashAndSnapshot(bool relative)
    {
        var brush = Brush(false, false); var transform = new TranslateTransform();
        if (relative) brush.RelativeTransform = transform; else brush.Transform = transform;
        var hash = brush.ComputeContentHash(); var snapshot = Assert.IsAssignableFrom<GradientBrush>(DrawInputSnapshotter.SnapshotBrush(brush));
        transform.X = relative ? .2 : 32;
        Assert.NotEqual(hash, brush.ComputeContentHash());
        var next = Assert.IsAssignableFrom<GradientBrush>(DrawInputSnapshotter.SnapshotBrush(brush)); Assert.NotSame(snapshot, next);
        var frozen = brush.CloneCurrentValue(); frozen.Freeze();
        Assert.Equal(Paint(new GeometryDrawing(brush, null, new RectangleGeometry(Box)), 0), Paint(new GeometryDrawing(frozen, null, new RectangleGeometry(Box)), 4));
        Assert.Equal(relative ? .2 : 32, (relative ? next.RelativeTransform : next.Transform)!.Value.OffsetX);
        Assert.Equal(0, (relative ? snapshot.RelativeTransform : snapshot.Transform)!.Value.OffsetX);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void CssAnglesKeepTheirPhysicalDirectionOnWidePaintBounds(int route)
    {
        var reader = new Jalium.UI.Styling.CssTokenReader("35deg, black, white");
        Assert.True(Jalium.UI.Styling.CssGradientParser.TryParseGradientFunction("linear-gradient", ref reader, out var brush));
        var radians = 35 * Math.PI / 180; var dx = Math.Sin(radians); var dy = -Math.Cos(radians);
        var length = Math.Abs(160 * dx) + Math.Abs(64 * dy);
        Check(Paint(new GeometryDrawing(brush, null, new RectangleGeometry(Box)), route), p => .5 + ((p.X - 100) * dx + (p.Y - 44) * dy) / length);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void CssUsedSizeResolutionRetainsBrushTransformsAndOpacity(int route)
    {
        var reader = new Jalium.UI.Styling.CssTokenReader("35deg, black, white");
        Assert.True(Jalium.UI.Styling.CssGradientParser.TryParseGradientFunction("linear-gradient", ref reader, out var brush));
        var transform = new Matrix(1, 0, .3, .85, -12, 7); var relative = new Matrix(1.1, .2, -.3, .9, .05, -.08);
        brush!.Transform = new MatrixTransform(transform); brush.RelativeTransform = new MatrixTransform(relative); brush.Opacity = .6;
        var radians = 35 * Math.PI / 180; var dx = Math.Sin(radians); var dy = -Math.Cos(radians); var length = Math.Abs(160 * dx) + Math.Abs(64 * dy);
        Check(Paint(new GeometryDrawing(brush, null, new RectangleGeometry(Box)), route), p =>
        {
            p = Inverse(p, transform); p = Inverse(new((p.X - 20) / 160, (p.Y - 12) / 64), relative);
            return .5 + ((20 + p.X * 160 - 100) * dx + (12 + p.Y * 64 - 44) * dy) / length;
        }, alpha: 153);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void SvgLinePaintUsesItsGeometricBoundingBox(int route)
    {
        var text = Svg(false, false, "translate(.1 .05)")
            .Replace("<rect x='20' y='12' width='160' height='64' fill='url(#g)'/>", "<line x1='20' y1='12' x2='180' y2='76' stroke='url(#g)' stroke-width='12'/>");
        using var image = SvgImage.FromSvgString(text);
        var samples = new[] { 40, 80, 120, 160 }.Select(x => new Point(x + .5, 12 + (x - 20) * .4 + .5)).ToArray();
        Check(Paint(image.Drawing!, route), p => Parameter(new((p.X - 20) / 160 - .1, (p.Y - 12) / 64 - .05), false, true), samples);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void CssStrokeAndPointRecordingPreservesUsedImageSize(int shape)
    {
        var reader = new Jalium.UI.Styling.CssTokenReader("35deg in srgb,black 4px,white 68px");
        Assert.True(Jalium.UI.Styling.CssGradientParser.TryParseGradientFunction("linear-gradient", ref reader, out var brush));
        brush!.Transform = new MatrixTransform(new(1, 0, .3, .85, -12, 7));
        brush.RelativeTransform = new MatrixTransform(new(1.1, .2, -.3, .9, .05, -.08)); brush.Opacity = .6;
        byte[] Capture(bool recorded)
        {
            var bitmap = new RenderTargetBitmap(200, 96, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap);
            void Draw(DrawingContext target)
            {
                var pen = new Pen(brush, shape is 4 or 5 ? 12 : 20);
                switch (shape)
                {
                    case 0: target.DrawRectangle(null, pen, Box); break;
                    case 1: target.DrawRoundedRectangle(null, pen, Box, 8, 8); break;
                    case 2: target.DrawEllipse(null, pen, new(100, 44), 80, 32); break;
                    case 3: target.DrawGeometry(null, pen, PathMarkupParser.Parse("M20 12H180V76H20Z")); break;
                    case 4: target.DrawLine(pen, new(20, 12), new(180, 76)); break;
                    case 5: target.DrawLines(pen, [new(20, 12), new(180, 48), new(30, 76), new(100, 18)]); break;
                    case 6: target.DrawPoints(brush, [new(40, 32), new(100, 48), new(160, 64)], 12); break;
                }
            }
            if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); Draw(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
            else Draw(dc);
            dc.Close(); var pixels = new byte[200 * 96 * 4]; bitmap.CopyPixels(pixels, 200 * 4, 0); return pixels;
        }
        var direct = Capture(false); var replay = Capture(true);
        Assert.True(Enumerable.Range(0, direct.Length / 4).Any(i => direct[i * 4 + 3] > 0));
        Assert.True(direct.Zip(replay, (a, b) => Math.Abs(a - b)).Max() <= 1, "Recording changed CSS gradient used-size coordinates");
    }

    [Fact]
    public void CssResolvedPaintKeepsItsOwnTransformValue()
    {
        var brush = CssPaint(); var transform = new TranslateTransform(20, 0); brush.Transform = transform;
        var before = Jalium.UI.Styling.CssImageLayout.ResolveBrush(brush, 160, 64)!;
        transform.X = 40;
        var after = Jalium.UI.Styling.CssImageLayout.ResolveBrush(brush, 160, 64)!;
        Assert.Equal(20, before.Transform!.Value.OffsetX); Assert.Equal(40, after.Transform!.Value.OffsetX);
        Assert.True(before.IsFrozen); Assert.True(after.IsFrozen);
    }

    [Fact]
    public void CssResolvedPaintReusesUnchangedUsedValues()
    {
        var brush = CssPaint(); brush.Opacity = .6;
        var before = Jalium.UI.Styling.CssImageLayout.ResolveBrush(brush, 160, 64)!;
        var same = Jalium.UI.Styling.CssImageLayout.ResolveBrush(brush, 160, 64)!;
        Assert.Same(before, same);
        brush.Opacity = .8;
        var after = Jalium.UI.Styling.CssImageLayout.ResolveBrush(brush, 160, 64)!;
        Assert.NotSame(before, after); Assert.Equal(.6, before.Opacity); Assert.Equal(.8, after.Opacity);
    }

    [Fact]
    public void CssOriginalTransformDoesNotRetainObsoleteUsedSizePaint()
    {
        var brush = CssPaint(); brush.RelativeTransform = new TranslateTransform(.1, 0);
        var stale = ObsoleteCssPaint(brush);
        for (var i = 0; i < 3 && stale.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(stale.IsAlive); GC.KeepAlive(brush);
    }

    private static GradientBrush CssPaint()
    {
        var reader = new Jalium.UI.Styling.CssTokenReader("35deg, black, white");
        Assert.True(Jalium.UI.Styling.CssGradientParser.TryParseGradientFunction("linear-gradient", ref reader, out var brush));
        return Assert.IsAssignableFrom<GradientBrush>(brush);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ObsoleteCssPaint(GradientBrush brush)
    {
        var before = Jalium.UI.Styling.CssImageLayout.ResolveBrush(brush, 160, 64)!;
        _ = Jalium.UI.Styling.CssImageLayout.ResolveBrush(brush, 200, 80);
        return new WeakReference(before);
    }

    private static GradientBrush Brush(bool radial, bool absolute)
    {
        GradientBrush brush = radial
            ? new RadialGradientBrush { Center = absolute ? new(92, 44) : new(.45, .5), GradientOrigin = absolute ? new(76, 44) : new(.35, .5), RadiusX = absolute ? 72 : .45, RadiusY = absolute ? 38.4 : .6 }
            : new LinearGradientBrush { StartPoint = absolute ? new(36, 24.8) : new(.1, .2), EndPoint = absolute ? new(164, 60) : new(.9, .75) };
        brush.MappingMode = absolute ? BrushMappingMode.Absolute : BrushMappingMode.RelativeToBoundingBox;
        brush.ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation;
        brush.GradientStops.Add(new(Colors.Black, 0)); brush.GradientStops.Add(new(Colors.White, 1)); return brush;
    }

    private static (Matrix Absolute, Matrix Relative) Transforms(int variant) => variant switch
    {
        1 => (new(1.05, .22, -.31, .85, 8, -5), Matrix.Identity),
        2 => (Matrix.Identity, new(.8, -.25, .12, 1.1, .18, .04)),
        3 => (new(1, 0, .3, .85, -12, 7), new(1.1, .2, -.3, .9, .05, -.08)),
        4 => (new(-1, .2, .12, 1, 185, -4), new(.9, .1, -.2, 1, .1, -.02)),
        _ => (Matrix.Identity, Matrix.Identity),
    };

    // Independent scalar inverse and focal-ray oracle; it never uses production paint mapping.
    private static Point Inverse(Point p, Matrix m)
    {
        var x = p.X - m.OffsetX; var y = p.Y - m.OffsetY; var det = m.M11 * m.M22 - m.M12 * m.M21;
        return new((x * m.M22 - y * m.M21) / det, (y * m.M11 - x * m.M12) / det);
    }
    private static double Parameter(Point p, bool radial, bool normalized)
    {
        if (!radial)
        {
            var sx = normalized ? .1 : 36; var sy = normalized ? .2 : 24.8;
            var dx = normalized ? .8 : 128; var dy = normalized ? .55 : 35.2;
            return ((p.X - sx) * dx + (p.Y - sy) * dy) / (dx * dx + dy * dy);
        }
        var px = (p.X - (normalized ? .45 : 92)) / (normalized ? .45 : 72);
        var py = (p.Y - (normalized ? .5 : 44)) / (normalized ? .6 : 38.4);
        return FocalParameter(px, py);
    }
    private static double FocalParameter(double px, double py)
    {
        const double fx = -2.0 / 9; var x = px - fx; var a = x * x + py * py;
        if (a < 1e-20) return 0;
        var b = 2 * fx * x; var c = fx * fx - 1;
        return 2 * a / (-b + Math.Sqrt(b * b - 4 * a * c));
    }
    private static void Check(byte[] pixels, Func<Point, double> parameter, Point[]? samples = null, byte alpha = 255)
    {
        foreach (var p in samples ?? Samples)
        {
            var expected = (int)Math.Round(Math.Clamp(parameter(p), 0, 1) * 255); var at = ((int)p.Y * 200 + (int)p.X) * 4;
            for (var c = 0; c < 3; c++) Assert.True(Math.Abs(pixels[at + c] - expected) <= 1, $"({p.X},{p.Y}) channel{c} actual {pixels[at + c]} expected {expected}");
            Assert.Equal(alpha, pixels[at + 3]);
        }
    }
    private static byte[] Paint(Drawing drawing, int route)
    {
        if (route == 1) return SoftwareVectorRasterizer.Rasterize(drawing, 200, 96, Canvas)!;
        var bitmap = new RenderTargetBitmap(200, 96, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap);
        if (route == 4)
        {
            var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); drawing.RenderTo(recorder); DrawingReplayer.Replay(recorder.Commit(), dc);
        }
        else if (route is 2 or 3)
        {
            var scene = new DrawingGroup(); scene.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(Canvas))); scene.Children.Add(drawing);
            if (route == 2) dc.DrawImage(new DrawingImage(scene), Canvas);
            else dc.DrawRectangle(new ImageBrush(new DrawingImage(scene)) { Stretch = Stretch.Fill }, null, Canvas);
        }
        else drawing.RenderTo(dc);
        dc.Close(); var pixels = new byte[200 * 96 * 4]; bitmap.CopyPixels(pixels, 200 * 4, 0); return pixels;
    }
    private static string MatrixText(Matrix m) => string.Create(CultureInfo.InvariantCulture, $"matrix({m.M11} {m.M12} {m.M21} {m.M22} {m.OffsetX} {m.OffsetY})");
    private static string Svg(bool radial, bool absolute, string transform)
    {
        var tag = radial ? "radialGradient" : "linearGradient";
        var attrs = radial
            ? absolute ? "cx='92' cy='44' r='72' fx='76' fy='44'" : "cx='.45' cy='.5' r='.45' fx='.35' fy='.5'"
            : absolute ? "x1='36' y1='24.8' x2='164' y2='60'" : "x1='.1' y1='.2' x2='.9' y2='.75'";
        return $"<svg xmlns='http://www.w3.org/2000/svg' width='200' height='96'><{tag} id='g' gradientUnits='{(absolute ? "userSpaceOnUse" : "objectBoundingBox")}' {attrs} gradientTransform='{transform}'><stop stop-color='black'/><stop offset='1' stop-color='white'/></{tag}><rect x='20' y='12' width='160' height='64' fill='url(#g)'/></svg>";
    }
}
