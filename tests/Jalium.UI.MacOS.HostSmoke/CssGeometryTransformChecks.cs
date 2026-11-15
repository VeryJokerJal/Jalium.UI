using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record GeometryCase(string Name, string Caption, Func<(Geometry Source, PathGeometry Reference)> Create,
        bool Empty = false, bool Stroke = false, bool Gradient = false, bool Dashed = false);

    private static GeometryCase[] GeometryCases()
    {
        static Point Map(Point point, Matrix matrix) => new(point.X * matrix.M11 + point.Y * matrix.M21 + matrix.OffsetX,
            point.X * matrix.M12 + point.Y * matrix.M22 + matrix.OffsetY);
        static (Geometry, PathGeometry) Rectangles(int kind)
        {
            var source = new GeometryGroup { FillRule = kind == 4 ? FillRule.EvenOdd : FillRule.Nonzero };
            var reference = new PathGeometry { FillRule = source.FillRule };
            void Add(GeometryGroup parent, Rect rect, params Matrix[] matrices)
            {
                var child = new RectangleGeometry(rect);
                if (matrices.Length > 0) child.Transform = new MatrixTransform(matrices[0]);
                parent.Children.Add(child);
                var points = new[] { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft };
                foreach (var matrix in matrices) for (var i = 0; i < points.Length; i++) points[i] = Map(points[i], matrix);
                reference.Figures.Add(Polygon(points));
            }
            switch (kind)
            {
                case 0: Add(source, new Rect(0, 0, 52, 36), new Matrix(1, 0, 0, 1, 24, 18)); break;
                case 1:
                    var matrix = new Matrix(1.5, .25, -.125, .75, 20, 12);
                    var inner = new GeometryGroup { Transform = new MatrixTransform(matrix) }; source.Children.Add(inner);
                    Add(inner, new Rect(0, 0, 40, 28), new Matrix(1, 0, 0, 1, 8, 6), matrix); break;
                case 2:
                    var c = Math.Cos(Math.PI / 6); var s = Math.Sin(Math.PI / 6);
                    Add(source, new Rect(0, 0, 60, 32), new Matrix(c, s, -s, c, 54, 12)); break;
                case 3: Add(source, new Rect(0, 0, 52, 32), new Matrix(1, .25, -.5, 1, 40, 14)); break;
                case 4:
                    Add(source, new Rect(12, 12, 110, 66)); Add(source, new Rect(0, 0, 40, 28), new Matrix(1, 0, 0, 1, 46, 30)); break;
                case 5:
                    Add(source, new Rect(20, 18, 60, 40)); Add(source, new Rect(20, 18, 60, 40), new Matrix(-1, 0, 0, 1, 100, 0)); break;
                case 6:
                    var move = new Matrix(1, 0, 0, 1, 30, 18);
                    var nested = new GeometryGroup { FillRule = FillRule.EvenOdd, Transform = new MatrixTransform(move) }; source.Children.Add(nested);
                    Add(nested, new Rect(0, 0, 60, 40), Matrix.Identity, move); Add(nested, new Rect(0, 0, 60, 40), Matrix.Identity, move); break;
                case 7:
                    Add(source, new Rect(0, 0, 52, 36), new Matrix(1, 0, 0, 1, 24, 18));
                    source.Transform = new TranslateTransform(8, 4); reference.Transform = source.Transform.Clone(); break;
                case 8:
                    var changed = new RectangleGeometry(new Rect(0, 0, 52, 36)) { Transform = new TranslateTransform(4, 18) };
                    source.Children.Add(changed); _ = source.Bounds; ((TranslateTransform)changed.Transform).X = 48;
                    reference.Figures.Add(Polygon([new(48, 18), new(100, 18), new(100, 54), new(48, 54)])); break;
                case 9:
                    var turn = new Matrix(0, 1, -1, 0, 112, 10);
                    var rotated = new GeometryGroup { Transform = new MatrixTransform(turn) }; source.Children.Add(rotated);
                    Add(rotated, new Rect(0, 0, 48, 28), new Matrix(-1, 0, 0, 1, 64, 12), turn); break;
            }
            return (source, reference);
        }
        static PathFigure Polygon(Point[] points, bool closed = true, bool filled = true)
        {
            var figure = new PathFigure { StartPoint = points[0], IsClosed = closed, IsFilled = filled };
            foreach (var point in points.Skip(1)) figure.Segments.Add(new LineSegment(point, true));
            return figure;
        }
        static (Geometry, PathGeometry) Lines(bool filled, bool poly, bool gap)
        {
            var matrix = new Matrix(1, .125, -.25, 1, 32, 20);
            Point[] points = [new(0, 0), new(64, 0), new(36, 42), new(8, 24)];
            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = filled };
            if (poly) figure.Segments.Add(new PolyLineSegment(points.Skip(1), true) { IsSmoothJoin = true });
            else for (var i = 1; i < points.Length; i++) figure.Segments.Add(new LineSegment(points[i], !gap || i != 2));
            var path = new PathGeometry(); path.Figures.Add(figure); path.Transform = new MatrixTransform(matrix);
            var group = new GeometryGroup { FillRule = FillRule.Nonzero }; group.Children.Add(path);
            var expected = new PathGeometry { FillRule = FillRule.Nonzero };
            var mapped = Polygon(points.Select(point => Map(point, matrix)).ToArray(), false, filled);
            if (poly)
            {
                mapped.Segments.Clear(); mapped.Segments.Add(new PolyLineSegment(points.Skip(1).Select(point => Map(point, matrix)), true) { IsSmoothJoin = true });
            }
            else if (gap) mapped.Segments[1].IsStroked = false;
            expected.Figures.Add(mapped); return (group, expected);
        }
        static (Geometry, PathGeometry) Boolean(GeometryCombineMode mode)
        {
            var first = new RectangleGeometry(new Rect(0, 0, 64, 42)) { Transform = new TranslateTransform(18, 20) };
            var second = new RectangleGeometry(new Rect(0, 0, 64, 42)) { Transform = new TranslateTransform(50, 20) };
            var source = new CombinedGeometry(mode, first, second);
            var expected = new PathGeometry { FillRule = FillRule.Nonzero };
            Rect[] rects = mode switch
            {
                GeometryCombineMode.Union => [new(18, 20, 96, 42)],
                GeometryCombineMode.Intersect => [new(50, 20, 32, 42)],
                GeometryCombineMode.Exclude => [new(18, 20, 32, 42)],
                _ => [new(18, 20, 32, 42), new(82, 20, 32, 42)],
            };
            foreach (var rect in rects) expected.Figures.Add(Polygon([rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft]));
            return (source, expected);
        }
        return
        [
            new("translate", "子几何平移", () => Rectangles(0)),
            new("nested", "先子变换，再父变换", () => Rectangles(1)),
            new("rotate", "旋转保留真实位置", () => Rectangles(2)),
            new("skew", "非对称错切矩阵", () => Rectangles(3)),
            new("hole", "奇偶规则保留镂空", () => Rectangles(4)),
            new("cancel", "镜像轮廓相消，无填充", () => Rectangles(5), Empty: true),
            new("winding", "外层规则统一轮廓覆盖", () => Rectangles(6)),
            new("root", "顶层变换只应用一次", () => Rectangles(7)),
            new("changed", "修改平移后更新缓存", () => Rectangles(8)),
            new("reflect-rotate", "嵌套镜像与旋转", () => Rectangles(9)),
            new("open-fill", "开放路径的隐式填充闭合", () => Lines(true, false, false)),
            new("open-stroke", "开放描边保留端点", () => Lines(false, false, false), Stroke: true),
            new("polyline", "折线与平滑连接标记", () => Lines(false, true, false), Stroke: true),
            new("stroke-gap", "非描边段保留间隙", () => Lines(false, false, true), Stroke: true),
            new("dashes", "变换后统一虚线节奏", () => Lines(false, false, false), Stroke: true, Dashed: true),
            new("gradient", "变换后轮廓的渐变边界", () => Rectangles(1), Gradient: true),
            new("union", "布尔并集使用变换后的区域", () => Boolean(GeometryCombineMode.Union)),
            new("intersect", "布尔交集使用变换后的区域", () => Boolean(GeometryCombineMode.Intersect)),
            new("exclude", "布尔差集使用变换后的区域", () => Boolean(GeometryCombineMode.Exclude)),
            new("xor", "布尔异或保留两侧区域", () => Boolean(GeometryCombineMode.Xor)),
        ];
    }

    internal static int RunGeometryTransforms()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<object>(); var failures = 0;
        Brush Fill(GeometryCase row) => row.Gradient ? new LinearGradientBrush(Colors.Red, Colors.Blue, 0) { Opacity = .5 } : new SolidColorBrush(Colors.Red) { Opacity = .5 };
        Pen? Stroke(GeometryCase row) => row.Stroke ? new Pen(new SolidColorBrush(Colors.Blue) { Opacity = .5 }, 3)
            { DashStyle = row.Dashed ? new DashStyle([2d, 1d], .5) : DashStyles.Solid, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, DashCap = PenLineCap.Flat } : null;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (var dpi in new[] { 1, 2 }) foreach (var recorded in new[] { false, true }) foreach (var affine in new[] { false, true })
            foreach (var route in new[] { "drawing", "image", "brush", "clip" }) foreach (var row in GeometryCases())
            {
                context.DefaultRenderingEngine = engine;
                var label = $"geometry-{backend}-{engine}-{dpi}-{recorded}-{affine}-{route}-{row.Name}";
                var errors = new List<string>(); var largest = 0; var differing = 0;
                try
                {
                    var scene = row.Create(); var fill = Fill(row); var pen = Stroke(row);
                    var actual = new GeometryDrawing(fill, pen, scene.Source); var expected = new GeometryDrawing(fill, pen, scene.Reference);
                    DrawingImage Image(GeometryDrawing drawing)
                    {
                        var frame = new DrawingGroup(); frame.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 160, 90)))); frame.Children.Add(drawing);
                        return new DrawingImage(frame);
                    }
                    var actualImage = Image(actual); var expectedImage = Image(expected);
                    var matrix = affine ? new Matrix(.86, .12, -.08, .92, 24, 16) : new Matrix(1, 0, 0, 1, 16, 12);
                    const int width = 208, height = 128;
                    byte[] Paint(bool source) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, height * dpi, dpi, dc =>
                    {
                        void Draw(DrawingContext target)
                        {
                            if (route == "drawing") (source ? actual : expected).RenderTo(target);
                            else if (route == "image") target.DrawImage(source ? actualImage : expectedImage, new Rect(0, 0, 160, 90));
                            else if (route == "brush") target.DrawRectangle(new ImageBrush(source ? actualImage : expectedImage) { Stretch = Stretch.Fill }, null, new Rect(0, 0, 160, 90));
                            else { target.PushClip(source ? scene.Source : scene.Reference); target.DrawRectangle(fill, null, new Rect(0, 0, 160, 90)); target.Pop(); }
                        }
                        if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); recorder.PushTransform(new MatrixTransform(matrix)); Draw(recorder); recorder.Pop(); DrawingReplayer.Replay(recorder.Commit(), dc); }
                        else { dc.PushTransform(new MatrixTransform(matrix)); Draw(dc); dc.Pop(); }
                    }, () => true, context) ?? throw new Exception("Geometry capture unavailable"));
                    var left = Paint(true); var right = Paint(false);
                    var hasInk = Enumerable.Range(0, right.Length / 4).Any(i => right[i * 4 + 3] > 0);
                    if (hasInk != (!row.Empty && !(route == "clip" && row.Stroke))) throw new Exception("Reference has unexpected empty/nonempty coverage");
                    for (var i = 0; i < left.Length; i++) { var delta = Math.Abs(left[i] - right[i]); largest = Math.Max(largest, delta); if (delta > 3) differing++; }
                    Save(left, width * dpi, height * dpi, label + ".png"); Save(right, width * dpi, height * dpi, label + "-expected.png");
                    if (differing > 0) throw new Exception($"Manual-coordinate reference: {differing} channels exceed tolerance3, maximum{largest}");
                }
                catch (Exception error) { failures++; errors.Add(error.ToString()); }
                records.Add(new { label, largest, differing, errors }); Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            }
            foreach (var viewport in new[] { 360, 720 })
            {
                var rows = GeometryCases(); var columns = viewport < 600 ? 1 : 2; var height = 24 + (int)Math.Ceiling(rows.Length / (double)columns) * 112;
                var canvas = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                for (var index = 0; index < rows.Length; index++)
                {
                    var row = rows[index]; var x = 24 + index % columns * 360; var y = 16 + index / columns * 112;
                    var caption = new TextBlock { Text = row.Caption, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), Width = 312, Height = 22 };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); canvas.Children.Add(caption);
                    var scene = row.Create(); var frame = new DrawingGroup(); frame.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 160, 90)))); frame.Children.Add(new GeometryDrawing(Fill(row), Stroke(row), scene.Source));
                    var image = new Image { Source = new DrawingImage(frame), Width = 160, Height = 90 };
                    Jalium.UI.Controls.Canvas.SetLeft(image, x); Jalium.UI.Controls.Canvas.SetTop(image, y + 22); canvas.Children.Add(image);
                }
                canvas.Measure(new(viewport, height)); canvas.Arrange(new(0, 0, viewport, height)); canvas.UpdateLayout();
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => canvas.Render(dc), () => true, context)!), viewport, height, $"geometry-gallery-{backend}-{viewport}.png");
            }
            File.WriteAllText(Path.Combine(Output, "geometry.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, tolerance = 3, nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false, liveWindow = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
