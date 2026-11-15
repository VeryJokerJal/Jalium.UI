using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private static string ViewportSvg(string attributes, string body) => "<svg xmlns='http://www.w3.org/2000/svg' " + attributes + ">" + body + "</svg>";

    internal static int RunSvgViewports()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<object>(); var failures = 0;
        const string rect = "<rect x='10' y='20' width='20' height='20' fill='#146342'/>";
        var cases = new (string Caption, string Attributes, string Body, string Expected)[]
        {
            ("viewBox · 居中保持比例", "width='100' height='100' viewBox='0 0 100 100'", rect, "<rect x='60' y='20' width='20' height='20' fill='#146342'/>"),
            ("preserveAspectRatio · 填满", "width='100' height='100' viewBox='0 0 100 100' preserveAspectRatio='none'", rect, "<rect x='20' y='20' width='40' height='20' fill='#146342'/>"),
            ("slice · 居中裁切", "width='100' height='100' viewBox='0 0 100 100' preserveAspectRatio='xMidYMid slice'", "<rect x='10' y='40' width='20' height='20' fill='#146342'/>", "<rect x='20' y='30' width='40' height='40' fill='#146342'/>"),
            ("固定根尺寸 · 独立缩放", "width='100' height='100'", rect, "<rect x='20' y='20' width='40' height='20' fill='#146342'/>"),
            ("百分比根尺寸 · 目标视口", "width='100%' height='100%'", rect, rect),
            ("媒体条件 · 跟随图片尺寸", "width='100' height='100'", "<style>rect{fill:#146342}@media(min-width:150px){rect{fill:#1260b0}}</style><rect width='100%' height='100%'/>", "<rect width='200' height='100' fill='#1260b0'/>"),
            ("视口单位 · 使用逻辑尺寸", "width='100' height='100' viewBox='0 0 100 100' preserveAspectRatio='none'", "<rect width='10vw' height='20vh' fill='#146342'/>", "<rect width='40' height='20' fill='#146342'/>"),
            ("描边 · 缩放后仍为 4px", "width='100' height='100' viewBox='0 0 100 100' preserveAspectRatio='none'", "<path d='M10,50H90' fill='none' stroke='#146342' stroke-width='4' vector-effect='non-scaling-stroke'/>", "<path d='M20,50H180' fill='none' stroke='#146342' stroke-width='4'/>"),
            ("负原点 viewBox · 正确平移", "width='100' height='100' viewBox='-50 -50 100 100'", "<rect x='-40' y='-30' width='20' height='20' fill='#146342'/>", "<rect x='60' y='20' width='20' height='20' fill='#146342'/>"),
            ("嵌套 SVG · 百分比坐标", "width='100' height='100' viewBox='0 0 100 100' preserveAspectRatio='none'", "<svg x='10%' y='20%' width='50%' height='50%' viewBox='0 0 50 50'><rect width='20' height='20' fill='#146342'/></svg>", "<rect x='20' y='20' width='40' height='20' fill='#146342'/>"),
            ("根尺寸缺省 · 保留用户坐标", "", rect, rect),
            ("局部根尺寸 · 补齐另一轴", "width='50%' height='100%'", rect, "<rect x='20' y='20' width='40' height='20' fill='#146342'/>")
        };
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (var row in cases) foreach (var route in new[] { "element", "css-brush", "css-background" })
            foreach (var recorded in new[] { false, true }) foreach (var dpi in new[] { 1, 2 })
            {
                context.DefaultRenderingEngine = engine; var label = $"viewport-{backend}-{engine}-{Array.IndexOf(cases, row)}-{route}-{recorded}-{dpi}";
                var errors = new List<string>(); var largest = 0;
                try
                {
                    byte[] Capture(bool reference)
                    {
                        using var svg = reference ? SvgImage.FromSvgString(ViewportSvg("width='200' height='100'", row.Expected))
                            : route == "element" ? SvgImage.FromSvgString(ViewportSvg(row.Attributes, row.Body))
                            : SvgImage.FromCssSvgString(ViewportSvg(row.Attributes, row.Body));
                        var image = new Image { Source = svg, Width = 200, Height = 100, Stretch = Stretch.Fill };
                        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                        image.Measure(new(200, 100)); image.Arrange(new(0, 0, 200, 100)); image.UpdateLayout();
                        return Pixels(MacOSWritingToolsBitmapCapture.Capture(224 * dpi, 124 * dpi, dpi, dc =>
                        {
                            void Paint(DrawingContext c)
                            {
                                c.PushTransform(new TranslateTransform(12, 12));
                                if (reference) c.DrawImage(svg, new(0, 0, 200, 100), BitmapScalingMode.NearestNeighbor);
                                else if (route == "element") image.Render(c);
                                else
                                {
                                    var brush = new ImageBrush(svg) { Stretch = Stretch.Fill, ScalingMode = BitmapScalingMode.NearestNeighbor };
                                    if (route == "css-background") brush.CssBackgroundLayout = new(CssBackgroundSizeMode.Fill, new(false, false));
                                    c.DrawRectangle(brush, null, new(0, 0, 200, 100));
                                }
                                c.Pop();
                            }
                            if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); Paint(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); } else Paint(dc);
                        }, () => true, context) ?? throw new Exception("SVG viewport capture unavailable"));
                    }
                    var actual = Capture(false); var expected = Capture(true);
                    for (var i = 0; i < actual.Length; i += 4) for (var channel = 0; channel < 4; channel++)
                    {
                        var a = channel == 3 ? actual[i + 3] : (actual[i + channel] * actual[i + 3] + 127) / 255;
                        var b = channel == 3 ? expected[i + 3] : (expected[i + channel] * expected[i + 3] + 127) / 255;
                        var error = Math.Abs(a - b); largest = Math.Max(largest, error);
                        if (error > 3 && errors.Count < 4) errors.Add($"pixel {i / 4} channel {channel}: {a} != {b}");
                    }
                    if (errors.Count > 0) { Save(actual, 224 * dpi, 124 * dpi, label + ".png"); Save(expected, 224 * dpi, 124 * dpi, label + "-expected.png"); }
                }
                catch (Exception error) { errors.Add(error.ToString()); }
                if (errors.Count > 0) failures++; records.Add(new { label, largest, errors });
                Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
            }
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var css in new[] { false, true }) foreach (var nested in new[] { false, true })
                foreach (var recorded in new[] { false, true }) foreach (var dpi in new[] { 1, 2 })
                {
                    var cropLabel = $"viewport-fraction-{backend}-{engine}-{css}-{nested}-{recorded}-{dpi}"; var cropErrors = new List<string>();
                    try
                    {
                        var xml = ViewportSvg("width='20.5' height='10.5'", "<rect x='10.25' width='10.25' height='10.5' fill='#146342'/>");
                        using var svg = css ? SvgImage.FromCssSvgString(xml) : SvgImage.FromSvgString(xml);
                        var brush = new ImageBrush(svg) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
                            Viewbox = new(10.25, 0, 10.25, 10.5), ScalingMode = BitmapScalingMode.NearestNeighbor };
                        var group = new DrawingGroup(); group.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new(0, 0, 100, 50))));
                        var source = new DrawingImage(group);
                        using var referenceSvg = SvgImage.FromSvgString(ViewportSvg("width='100' height='50'", "<rect width='100' height='50' fill='#146342'/>"));
                        byte[] CaptureCrop(bool reference) => Pixels(MacOSWritingToolsBitmapCapture.Capture(124 * dpi, 74 * dpi, dpi, dc =>
                        {
                            void Paint(DrawingContext c)
                            {
                                c.PushTransform(new TranslateTransform(12, 12));
                                if (reference) c.DrawImage(referenceSvg, new(0, 0, 100, 50), BitmapScalingMode.NearestNeighbor);
                                else if (nested) c.DrawImage(source, new(0, 0, 100, 50), BitmapScalingMode.NearestNeighbor);
                                else c.DrawRectangle(brush, null, new(0, 0, 100, 50));
                                c.Pop();
                            }
                            if (recorded) { var recorder = new DrawingRecorder(); Paint(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); } else Paint(dc);
                        }, () => true, context) ?? throw new Exception("Fractional viewbox capture unavailable"));
                        var actual = CaptureCrop(false); var expected = CaptureCrop(true);
                        for (var i = 0; i < actual.Length; i++) if (Math.Abs(actual[i] - expected[i]) > 3)
                        { cropErrors.Add($"byte {i}: {actual[i]} != {expected[i]}"); break; }
                    }
                    catch (Exception error) { cropErrors.Add(error.ToString()); }
                    if (cropErrors.Count > 0) failures++; records.Add(new { label = cropLabel, errors = cropErrors });
                    Console.WriteLine($"{(cropErrors.Count == 0 ? "PASS" : "FAIL")} {cropLabel} {string.Join(" | ", cropErrors)}");
                }
                var errors = new List<string>(); var largestCache = 0;
                var label = $"viewport-cache-{backend}-{engine}";
                try
                {
                    using var svg = SvgImage.FromSvgString(ViewportSvg("width='100' height='100' viewBox='0 0 100 100'", rect));
                    _ = MacOSWritingToolsBitmapCapture.Capture(224, 124, 1, dc =>
                    {
                        var native = (Jalium.UI.Interop.RenderTargetDrawingContext)dc;
                        for (var i = 0; i < 24; i++)
                        {
                            var size = new Size(200 + i, 100);
                            dc.DrawImage(svg.ImageForViewport(size), new Rect(1000, 1000, size.Width, size.Height));
                            largestCache = Math.Max(largestCache, native.CachedVectorDrawingCount);
                        }
                        dc.DrawImage(svg, new(1000, 1000, 200, 100));
                        dc.DrawImage(svg.ImageForViewport(new(200, 100)), new(12, 12, 200, 100));
                        largestCache = Math.Max(largestCache, native.CachedVectorDrawingCount);
                        if (largestCache != 1) errors.Add($"One SVG retained {largestCache} native vector slots after viewport changes");
                        native.ClearCache();
                        if (native.CachedVectorDrawingCount != 0) errors.Add("Cache clear retained a concrete viewport");
                    }, () => true, context) ?? throw new Exception("Viewport cache capture unavailable");
                }
                catch (Exception error) { errors.Add(error.ToString()); }
                if (errors.Count > 0) failures++; records.Add(new { label, largestCache, errors });
                Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
            }
            foreach (var viewport in new[] { 360, 720 })
            {
                var columns = viewport < 600 ? 1 : 2; var height = 24 + (int)Math.Ceiling(6d / columns) * 162;
                var canvas = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                var sources = new List<SvgImage>();
                for (var i = 0; i < 6; i++)
                {
                    var x = 24 + i % columns * 360; var y = 16 + i / columns * 162;
                    var caption = new TextBlock { Text = cases[i].Caption, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), Width = 312, Height = 22 };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); canvas.Children.Add(caption);
                    var svg = SvgImage.FromSvgString(ViewportSvg(cases[i].Attributes, cases[i].Body)); sources.Add(svg);
                    var image = new Image { Source = svg, Width = 200, Height = 100, Stretch = Stretch.Fill };
                    Jalium.UI.Controls.Canvas.SetLeft(image, x); Jalium.UI.Controls.Canvas.SetTop(image, y + 30); canvas.Children.Add(image);
                }
                canvas.Measure(new(viewport, height)); canvas.Arrange(new(0, 0, viewport, height)); canvas.UpdateLayout();
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => canvas.Render(dc), () => true, context)!), viewport, height, $"svg-viewport-gallery-{backend}-{viewport}.png");
                foreach (var source in sources) source.Dispose();
            }
            File.WriteAllText(Path.Combine(Output, "svg-viewports.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, tolerance = 3, reference = "Independent SVG paths at the concrete image size", nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
