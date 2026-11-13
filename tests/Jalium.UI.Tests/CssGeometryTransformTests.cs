using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssGeometryTransformTests
{
    private sealed record Scene(GeometryGroup Actual, PathGeometry Expected, bool HasInk = true);
    private static Point Map(Point point, Matrix matrix) => new(
        point.X * matrix.M11 + point.Y * matrix.M21 + matrix.OffsetX,
        point.X * matrix.M12 + point.Y * matrix.M22 + matrix.OffsetY);

    private static Scene Make(int index)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        var expected = new PathGeometry { FillRule = FillRule.Nonzero };
        void Rectangle(GeometryGroup target, Rect rect, params Matrix[] matrices)
        {
            var leaf = new RectangleGeometry(rect);
            if (matrices.Length > 0) leaf.Transform = new MatrixTransform(matrices[0]);
            target.Children.Add(leaf);
            var points = new[] { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft };
            foreach (var matrix in matrices) for (var i = 0; i < points.Length; i++) points[i] = Map(points[i], matrix);
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            foreach (var point in points.Skip(1)) figure.Segments.Add(new LineSegment(point, true));
            expected.Figures.Add(figure);
        }
        switch (index)
        {
            case 0: Rectangle(group, new Rect(0, 0, 28, 22), new Matrix(1, 0, 0, 1, 24, 18)); break;
            case 1:
                var outer = new Matrix(1.5, .2, -.1, .8, 16, 10);
                var nested = new GeometryGroup { Transform = new MatrixTransform(outer) };
                group.Children.Add(nested);
                Rectangle(nested, new Rect(0, 0, 24, 20), new Matrix(1, 0, 0, 1, 8, 6), outer);
                break;
            case 2:
                Rectangle(group, new Rect(4, 4, 40, 22), new Matrix(.9, .25, -.3, 1.1, 44, 16));
                group.Transform = new MatrixTransform(new Matrix(1, .05, 0, .95, 5, 3));
                expected.Transform = group.Transform.Clone();
                break;
            case 3:
                var c = Math.Cos(Math.PI / 6); var s = Math.Sin(Math.PI / 6);
                Rectangle(group, new Rect(0, 0, 40, 22), new Matrix(c, s, -s, c, 50, 12)); break;
            case 4:
                Rectangle(group, new Rect(16, 16, 44, 36));
                Rectangle(group, new Rect(16, 16, 44, 36), new Matrix(-1, 0, 0, 1, 76, 0)); break;
            case 5:
                group.FillRule = expected.FillRule = FillRule.EvenOdd;
                Rectangle(group, new Rect(12, 12, 90, 60));
                Rectangle(group, new Rect(0, 0, 30, 24), new Matrix(1, 0, 0, 1, 42, 30)); break;
            case 6:
                var path = PathMarkupParser.ParseSvgPathData("M0 0L40 0L20 30");
                var move = new Matrix(1, .1, -.1, 1, 28, 22);
                path.Transform = new MatrixTransform(move); group.Children.Add(path);
                var triangle = new PathFigure { StartPoint = Map(new Point(0, 0), move), IsFilled = true };
                triangle.Segments.Add(new LineSegment(Map(new Point(40, 0), move), true));
                triangle.Segments.Add(new LineSegment(Map(new Point(20, 30), move), true));
                expected.Figures.Add(triangle); break;
            case 7:
                var parent = new Matrix(1, 0, 0, 1, 30, 18);
                var duplicate = new GeometryGroup { FillRule = FillRule.EvenOdd, Transform = new MatrixTransform(parent) };
                group.Children.Add(duplicate);
                Rectangle(duplicate, new Rect(0, 0, 38, 32), Matrix.Identity, parent);
                Rectangle(duplicate, new Rect(0, 0, 38, 32), Matrix.Identity, parent); break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
        return new(group, expected, index != 4);
    }

    public static IEnumerable<object[]> DrawRoutes() =>
        from index in Enumerable.Range(0, 8) from route in Enumerable.Range(0, 7) select new object[] { index, route };

    [Theory, MemberData(nameof(DrawRoutes))]
    public void ChildTransformsSurviveSoftwareDrawingImagesBrushesClipsAndReplay(int index, int route)
    {
        var scene = Make(index);
        var reference = scene.Expected.CloneCurrentValue();
        if (reference.Transform is { } transform)
        {
            foreach (var figure in reference.Figures)
            {
                figure.StartPoint = Map(figure.StartPoint, transform.Value);
                foreach (var segment in figure.Segments)
                    if (segment is LineSegment line) line.Point = Map(line.Point, transform.Value);
            }
            reference.Transform = null;
        }
        var expected = Paint(reference, route); var actual = Paint(scene.Actual, route);
        Assert.Equal(scene.HasInk, expected.Where((_, i) => i % 4 == 3).Any(alpha => alpha != 0));
        PixelsEqual(expected, actual);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void BoundsIncludeChildTransformsAndExcludeTheRootTransform(int index)
    {
        var scene = Make(index); Assert.Equal(scene.Expected.Bounds, scene.Actual.Bounds);
        Assert.Equal(scene.Expected.Bounds, scene.Actual.GetFlattenedPathGeometry().Bounds);
    }

    [Theory]
    [InlineData(FillRule.EvenOdd)] [InlineData(FillRule.Nonzero)]
    public void GroupHitTestingUsesTheCompoundTransformedWinding(FillRule rule)
    {
        var scene = Make(4); scene.Actual.FillRule = scene.Expected.FillRule = rule;
        Assert.False(scene.Actual.FillContains(new Point(30, 30)));
    }

    [Theory]
    [InlineData(FillRule.EvenOdd)] [InlineData(FillRule.Nonzero)]
    public void OpenFiguresCloseForFillHitTestingWithoutClosingTheirStroke(FillRule rule)
    {
        var path = PathMarkupParser.ParseSvgPathData("M0 0L30 0L15 20"); path.FillRule = rule;
        Assert.False(path.FillContains(new Point(3, 19)));
        Assert.True(path.FillContains(new Point(15, 10)));
        Assert.False(Assert.Single(path.Figures).IsClosed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SourceEditsInvalidateBoundsAndDrawingImageCaches(bool replaceTransform)
    {
        var leaf = new RectangleGeometry(new Rect(0, 0, 28, 22)) { Transform = new TranslateTransform(12, 12) };
        var group = new GeometryGroup(); group.Children.Add(leaf);
        var drawing = new GeometryDrawing(Brushes.Red, null, group);
        var frame = new DrawingGroup(); frame.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 160, 90)))); frame.Children.Add(drawing);
        var image = new DrawingImage(frame);
        Assert.Equal(new Rect(12, 12, 28, 22), group.Bounds);
        _ = PaintImage(image); var before = image.ContentGeneration;
        if (replaceTransform) leaf.Transform = new TranslateTransform(42, 12);
        else ((TranslateTransform)leaf.Transform!).X = 42;
        Assert.True(image.ContentGeneration > before);
        Assert.Equal(new Rect(42, 12, 28, 22), group.Bounds);
        var expectedPixels = Paint(new RectangleGeometry(new Rect(42, 12, 28, 22)), 3, Brushes.Red);
        PixelsEqual(expectedPixels, PaintImage(image));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FrozenClonesRetainTransformsOnAWorker(bool currentValue)
    {
        var scene = Make(1); _ = scene.Actual.Bounds;
        var clone = currentValue ? scene.Actual.CloneCurrentValue() : scene.Actual.Clone(); clone.Freeze();
        Exception? failure = null; byte[]? actual = null;
        var worker = new Thread(() => { try { actual = Paint(clone, 0); } catch (Exception error) { failure = error; } });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(10))); Assert.Null(failure);
        PixelsEqual(Paint(scene.Expected, 0), actual!);
    }

    [Fact]
    public void FlatteningIsAnIndependentSnapshotAndKeepsSegmentFlags()
    {
        var path = new PathGeometry(); var figure = new PathFigure { StartPoint = new Point(1, 2), IsFilled = false };
        figure.Segments.Add(new PolyLineSegment([new Point(3, 4), new Point(5, 6)], false) { IsSmoothJoin = true });
        path.Figures.Add(figure); path.Transform = new TranslateTransform(10, 20);
        var group = new GeometryGroup(); group.Children.Add(path);
        var flat = group.GetFlattenedPathGeometry(); var result = Assert.Single(flat.Figures);
        Assert.Equal(new Point(11, 22), result.StartPoint); Assert.False(result.IsFilled); Assert.False(result.IsClosed);
        var poly = Assert.IsType<PolyLineSegment>(Assert.Single(result.Segments));
        Assert.False(poly.IsStroked); Assert.True(poly.IsSmoothJoin);
        poly.Points[0] = new Point(100, 100); result.StartPoint = new Point(100, 100);
        Assert.Equal(new Point(1, 2), figure.StartPoint); Assert.Equal(new Point(3, 4), ((PolyLineSegment)figure.Segments[0]).Points[0]);
    }

    [Fact]
    public void DeepGroupsFlattenInChildBeforeParentOrder()
    {
        Geometry leaf = new RectangleGeometry(new Rect(0, 0, 2, 3));
        for (var i = 0; i < 512; i++) { var group = new GeometryGroup(); leaf.Transform = new TranslateTransform(1, 0); group.Children.Add(leaf); leaf = group; }
        Assert.Equal(new Rect(512, 0, 2, 3), leaf.GetFlattenedPathGeometry().Bounds);
    }

    public static IEnumerable<object[]> InvalidTolerances() => new[] { double.NaN, double.PositiveInfinity, -1d }.Select(value => new object[] { value });
    [Theory, MemberData(nameof(InvalidTolerances))]
    public void InvalidFlatteningTolerancesAreRejected(double tolerance)
    {
        var group = Make(0).Actual;
        Assert.Throws<ArgumentOutOfRangeException>(() => group.GetFlattenedPathGeometry(tolerance, ToleranceType.Absolute));
    }

    [Fact]
    public void TransformedEllipsesHaveAnalyticBounds()
    {
        var matrix = new Matrix(1, .4, .8, 1, -4, 10);
        var ellipse = new EllipseGeometry(new Point(12, 14), 7, 3) { Transform = new MatrixTransform(matrix) };
        var group = new GeometryGroup(); group.Children.Add(ellipse);
        var center = Map(new Point(12, 14), matrix);
        var rx = Math.Sqrt(49 * matrix.M11 * matrix.M11 + 9 * matrix.M21 * matrix.M21);
        var ry = Math.Sqrt(49 * matrix.M12 * matrix.M12 + 9 * matrix.M22 * matrix.M22);
        Assert.Equal(center.X - rx, group.Bounds.X, 8); Assert.Equal(center.Y - ry, group.Bounds.Y, 8);
        Assert.Equal(2 * rx, group.Bounds.Width, 8); Assert.Equal(2 * ry, group.Bounds.Height, 8);
    }

    [Fact]
    public void NaturalDrawingImageBoundsIncludeTheTopLevelGeometryTransform()
    {
        var geometry = new RectangleGeometry(new Rect(24, 18, 28, 22)) { Transform = new MatrixTransform(new Matrix(0, 1, -1, 0, 50, 10)) };
        var drawing = new GeometryDrawing(Brushes.Red, null, geometry);
        Assert.Equal(new Rect(10, 34, 22, 28), drawing.Bounds);
        var actual = PaintImage(new DrawingImage(drawing));
        PixelsEqual(Paint(new RectangleGeometry(new Rect(0, 0, 160, 90)), 1, Brushes.Red), actual);
    }

    [Theory]
    [InlineData("path('M0 0L30 0L15 20')")]
    [InlineData("shape(from 0px 0px,line to 30px 0px,line to 15px 20px)")]
    public void CssOpenClipPathsHaveTheSamePaintAndInputRegion(string shape)
    {
        var box = new Border { Width = 30, Height = 20, Background = Brushes.Red };
        Css.SetStyle(box, "clip-path:" + shape);
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false); Dispatcher.PushFrame(frame);
        box.Measure(new(30, 20)); box.Arrange(new(0, 0, 30, 20)); box.UpdateLayout();
        var bitmap = new RenderTargetBitmap(30, 20, 96, 96, PixelFormats.Bgra32); bitmap.Render(box);
        Assert.Equal(0, bitmap.GetPixelBuffer()[(19 * 30 + 3) * 4 + 3]);
        Assert.True(bitmap.GetPixelBuffer()[(10 * 30 + 15) * 4 + 3] > 0);
        Assert.Same(box, box.InputHitTest(new Point(15, 10)));
        Assert.Null(box.InputHitTest(new Point(3, 19)));
    }

    [Theory]
    [InlineData(GeometryCombineMode.Union)] [InlineData(GeometryCombineMode.Intersect)]
    [InlineData(GeometryCombineMode.Exclude)] [InlineData(GeometryCombineMode.Xor)]
    public void BooleanOperandsApplyTheirTransformsBeforeCombining(GeometryCombineMode mode)
    {
        var first = new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new TranslateTransform(20, 15) };
        var second = new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new TranslateTransform(30, 15) };
        var combined = new CombinedGeometry(mode, first, second, new TranslateTransform(7, 8));
        var expected = new PathGeometry { FillRule = FillRule.Nonzero };
        var rectangles = mode switch
        {
            GeometryCombineMode.Union => new[] { new Rect(20, 15, 30, 20) },
            GeometryCombineMode.Intersect => [new Rect(30, 15, 10, 20)],
            GeometryCombineMode.Exclude => [new Rect(20, 15, 10, 20)],
            _ => [new Rect(20, 15, 10, 20), new Rect(40, 15, 10, 20)],
        };
        foreach (var rect in rectangles) expected.Figures.AddRange(new RectangleGeometry(rect).GetFlattenedPathGeometry().Figures);
        Assert.Equal(expected.Bounds, combined.Bounds);
        var flat = combined.GetFlattenedPathGeometry(.01, ToleranceType.Absolute);
        for (var x = 5; x < 60; x += 5)
        {
            var point = new Point(x + .25, 22);
            Assert.Equal(expected.FillContains(point), flat.FillContains(point));
            Assert.Equal(expected.FillContains(point), combined.FillContains(point));
        }
        var translated = expected.CloneCurrentValue();
        foreach (var figure in translated.Figures)
        {
            figure.StartPoint = new Point(figure.StartPoint.X + 7, figure.StartPoint.Y + 8);
            foreach (var segment in figure.Segments)
                if (segment is LineSegment line) line.Point = new Point(line.Point.X + 7, line.Point.Y + 8);
        }
        PixelsEqual(Paint(translated, 0), Paint(combined, 0));
    }

    [Theory]
    [InlineData(GeometryCombineMode.Union, false)] [InlineData(GeometryCombineMode.Union, true)]
    [InlineData(GeometryCombineMode.Intersect, false)] [InlineData(GeometryCombineMode.Intersect, true)]
    [InlineData(GeometryCombineMode.Exclude, false)] [InlineData(GeometryCombineMode.Exclude, true)]
    [InlineData(GeometryCombineMode.Xor, false)] [InlineData(GeometryCombineMode.Xor, true)]
    public void NullBooleanOperandsFollowSetSemantics(GeometryCombineMode mode, bool missingFirst)
    {
        var child = new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new TranslateTransform(20, 15) };
        var combined = new CombinedGeometry { GeometryCombineMode = mode };
        if (missingFirst) combined.Geometry2 = child; else combined.Geometry1 = child;
        var present = mode is GeometryCombineMode.Union or GeometryCombineMode.Xor || mode == GeometryCombineMode.Exclude && !missingFirst;
        Assert.Equal(present, combined.FillContains(new Point(25, 20)));
        Assert.Equal(present ? new Rect(20, 15, 20, 20) : Rect.Empty, combined.Bounds);
    }

    [Fact]
    public void ScaledCurvesUseToleranceInTheGroupsCoordinateSystem()
    {
        var matrix = new Matrix(9, 4, -3, 1.5, 48, 28);
        var group = new GeometryGroup(); group.Children.Add(new EllipseGeometry(new Point(0, 0), 2, 4) { Transform = new MatrixTransform(matrix) });
        var coarse = group.GetFlattenedPathGeometry(.3, ToleranceType.Absolute);
        var fine = group.GetFlattenedPathGeometry(.02, ToleranceType.Absolute);
        Assert.True(fine.Figures[0].Segments.Count > coarse.Figures[0].Segments.Count);
        var points = new List<Point> { fine.Figures[0].StartPoint };
        points.AddRange(fine.Figures[0].Segments.Cast<LineSegment>().Select(segment => segment.Point));
        for (var sample = 0; sample < 1024; sample++)
        {
            var angle = sample * Math.Tau / 1024;
            var point = Map(new Point(2 * Math.Cos(angle), 4 * Math.Sin(angle)), matrix);
            var distance = double.MaxValue;
            for (var index = 1; index < points.Count; index++)
            {
                var a = points[index - 1]; var b = points[index]; var x = b.X - a.X; var y = b.Y - a.Y;
                var length = x * x + y * y;
                var t = length > 0 ? Math.Clamp(((point.X - a.X) * x + (point.Y - a.Y) * y) / length, 0, 1) : 0;
                distance = Math.Min(distance, Math.Sqrt(Math.Pow(point.X - a.X - t * x, 2) + Math.Pow(point.Y - a.Y - t * y, 2)));
            }
            Assert.InRange(distance, 0, .020001);
        }
        var relative = group.GetFlattenedPathGeometry(.001, ToleranceType.Relative);
        var absolute = group.GetFlattenedPathGeometry(.001 * Math.Max(group.Bounds.Width, group.Bounds.Height), ToleranceType.Absolute);
        Assert.Equal(absolute.ToString(), relative.ToString());
    }

    [Theory]
    [InlineData(GeometryCombineMode.Union, 2900)] [InlineData(GeometryCombineMode.Intersect, 700)]
    [InlineData(GeometryCombineMode.Exclude, 1300)] [InlineData(GeometryCombineMode.Xor, 2200)]
    public void ConcaveBooleanRegionsAgreeWithIndependentMembershipAndArea(GeometryCombineMode mode, double area)
    {
        var first = PathMarkupParser.ParseSvgPathData("M0 0H60V20H20V60H0Z");
        first.Transform = new TranslateTransform(12, 10);
        var second = new RectangleGeometry(new Rect(10, 10, 40, 40)) { Transform = new TranslateTransform(12, 10) };
        var result = Geometry.Combine(first, second, mode, null);
        CheckMembership(result, mode, 60, (x, y) => x < 60 && y < 20 || x < 20 && y < 60,
            (x, y) => x > 10 && x < 50 && y > 10 && y < 50, 12, 10);
        Assert.Equal(area, result.GetArea(), 6);
    }

    [Theory]
    [InlineData(GeometryCombineMode.Union, 3600)] [InlineData(GeometryCombineMode.Intersect, 1200)]
    [InlineData(GeometryCombineMode.Exclude, 2000)] [InlineData(GeometryCombineMode.Xor, 2400)]
    public void MixedInputFillRulesKeepHolesAndIslands(GeometryCombineMode mode, double area)
    {
        var first = new GeometryGroup { FillRule = FillRule.EvenOdd };
        first.Children.Add(new RectangleGeometry(new Rect(0, 0, 60, 60)));
        first.Children.Add(new RectangleGeometry(new Rect(20, 20, 20, 20)));
        var second = new RectangleGeometry(new Rect(10, 10, 40, 40));
        var result = Geometry.Combine(first, second, mode, null);
        CheckMembership(result, mode, 60, (x, y) => !(x > 20 && x < 40 && y > 20 && y < 40),
            (x, y) => x > 10 && x < 50 && y > 10 && y < 50);
        Assert.Equal(area, result.GetArea(), 6);
    }

    [Theory]
    [InlineData(GeometryCombineMode.Union, 1200)] [InlineData(GeometryCombineMode.Intersect, 400)]
    [InlineData(GeometryCombineMode.Exclude, 400)] [InlineData(GeometryCombineMode.Xor, 800)]
    public void SelfIntersectionsAreResolvedAsFilledRegions(GeometryCombineMode mode, double area)
    {
        var first = PathMarkupParser.ParseSvgPathData("M0 0L40 40L0 40L40 0Z");
        var result = Geometry.Combine(first, new RectangleGeometry(new Rect(0, 0, 20, 40)), mode, null);
        CheckMembership(result, mode, 40, (x, y) => y < Math.Min(x, 40 - x) || y > Math.Max(x, 40 - x), (x, y) => x < 20);
        Assert.Equal(area, result.GetArea(), 6);
    }

    [Fact]
    public void AreaNormalizesOverlappingContoursBeforeAddingThem()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, 20, 20)));
        group.Children.Add(new RectangleGeometry(new Rect(10, 0, 20, 20)));
        Assert.Equal(600, group.GetArea(), 6);
        group.FillRule = FillRule.EvenOdd; Assert.Equal(400, group.GetArea(), 6);
    }

    [Fact]
    public void LargeWorldOffsetsDoNotDestroySmallRegionArea()
    {
        var first = new RectangleGeometry(new Rect(1e9, -1e9, 20, 20));
        var second = new RectangleGeometry(new Rect(1e9 + 10, -1e9, 20, 20));
        var result = Geometry.Combine(first, second, GeometryCombineMode.Intersect, null);
        Assert.Equal(new Rect(1e9 + 10, -1e9, 10, 20), result.Bounds);
        Assert.Equal(200, result.GetArea(), 6);
    }

    [Fact]
    public void SubpixelBooleanRegionsAreNotRoundedToWholePixels()
    {
        var first = new RectangleGeometry(new Rect(.00012, .00023, .000004, .000003));
        var second = new RectangleGeometry(new Rect(.000122, .00023, .000004, .000003));
        var result = Geometry.Combine(first, second, GeometryCombineMode.Intersect, null, .00000001, ToleranceType.Absolute);
        Assert.True(result.FillContains(new Point(.000123, .000231)));
        Assert.InRange(result.GetArea(), 5.999e-12, 6.001e-12);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public void StyledPathControlsKeepTheDataGeometrysTopLevelTransform(int kind, bool frozen)
    {
        Geometry geometry;
        if (kind == 0) { var group = new GeometryGroup(); group.Children.Add(new RectangleGeometry(new Rect(0, 0, 20, 16))); geometry = group; }
        else if (kind == 1) geometry = new RectangleGeometry(new Rect(0, 0, 20, 16));
        else geometry = new CombinedGeometry(GeometryCombineMode.Union,
            new RectangleGeometry(new Rect(0, 0, 10, 16)), new RectangleGeometry(new Rect(10, 0, 10, 16)));
        geometry.Transform = new TranslateTransform(24, 18); if (frozen) geometry.Freeze();
        var source = new Jalium.UI.Shapes.Path { Data = geometry, Width = 80, Height = 60, Fill = Brushes.Red };
        var reference = new Jalium.UI.Shapes.Path { Data = new RectangleGeometry(new Rect(24, 18, 20, 16)), Width = 80, Height = 60, Fill = Brushes.Red };
        foreach (var path in new[] { source, reference })
        {
            Css.SetStyle(path, "opacity:.5;transform:translate(3px,2px);transform-origin:0 0");
            path.Measure(new(80, 60)); path.Arrange(new(0, 0, 80, 60)); path.UpdateLayout();
        }
        byte[] Capture(Visual visual)
        {
            var bitmap = new RenderTargetBitmap(80, 60, 96, 96, PixelFormats.Bgra32); bitmap.Render(visual);
            return bitmap.GetPixelBuffer();
        }
        PixelsEqual(Capture(reference), Capture(source));
    }

    private static void CheckMembership(PathGeometry result, GeometryCombineMode mode, int size,
        Func<double, double, bool> first, Func<double, double, bool> second, double dx = 0, double dy = 0)
    {
        for (var x = .37; x < size; x += 2.31) for (var y = .61; y < size; y += 2.17)
        {
            var a = first(x, y); var b = second(x, y);
            var expected = mode switch { GeometryCombineMode.Union => a || b, GeometryCombineMode.Intersect => a && b,
                GeometryCombineMode.Exclude => a && !b, _ => a ^ b };
            Assert.Equal(expected, result.FillContains(new Point(x + dx, y + dy)));
        }
    }

    private static byte[] Paint(Geometry geometry, int route, Brush? brush = null)
    {
        brush ??= new SolidColorBrush(Colors.Red) { Opacity = .5 };
        var drawing = new GeometryDrawing(brush, null, geometry);
        if (route == 0) return SoftwareVectorRasterizer.Rasterize(drawing, 160, 90, new Rect(0, 0, 160, 90))!;
        var frame = new DrawingGroup(); frame.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 160, 90)))); frame.Children.Add(drawing);
        var bitmap = new RenderTargetBitmap(160, 90, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap);
        void Draw(DrawingContext target)
        {
            if (route is 1 or 2) target.DrawGeometry(brush, null, geometry);
            else if (route == 3) target.DrawImage(new DrawingImage(frame), new Rect(0, 0, 160, 90));
            else if (route == 4) target.DrawRectangle(new ImageBrush(new DrawingImage(frame)) { Stretch = Stretch.Fill }, null, new Rect(0, 0, 160, 90));
            else { target.PushClip(geometry); target.DrawRectangle(brush, null, new Rect(0, 0, 160, 90)); target.Pop(); }
        }
        if (route is 2 or 6) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); Draw(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
        else Draw(dc);
        dc.Close(); return bitmap.GetPixelBuffer();
    }
    private static byte[] PaintImage(DrawingImage image)
    {
        var bitmap = new RenderTargetBitmap(160, 90, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap);
        dc.DrawImage(image, new Rect(0, 0, 160, 90)); dc.Close(); return bitmap.GetPixelBuffer();
    }
    private static void PixelsEqual(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 3);
    }
}
