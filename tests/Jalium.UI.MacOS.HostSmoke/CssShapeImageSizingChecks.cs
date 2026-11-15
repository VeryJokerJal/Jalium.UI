using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record ShapeSizingCase(string Caption, string Shape, bool Image, string? Display,
        string Fit, Size Box, Rect Ink, string Extra = "");
    private const string ShapeSizingText = "图像轮廓决定文字经过的位置。尺寸、裁剪和图片候选更新以后，原始文字与选择范围仍然保留。窄窗口自然换行，宽窗口保持清楚的阅读顺序。";
    private static string ShapeSizingSvg(string attrs, string? body = null) => "<svg xmlns='http://www.w3.org/2000/svg' " + attrs + ">" +
        (body ?? "<rect width='100%' height='100%' fill='#146342'/>") + "</svg>";
    private static string ShapeSizingUri(string xml) => "data:image/svg+xml," + Uri.EscapeDataString(xml);
    private static string ShapeSizingUrl(string xml) => "url(\"" + ShapeSizingUri(xml) + "\")";
    private static void ShapeSizingFlush()
    { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }

    private static string ShapeSizingPng(int width, int height, bool right)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4; bytes[at] = 66; bytes[at + 1] = 99; bytes[at + 2] = 20;
            bytes[at + 3] = (x >= width / 2) == right ? (byte)255 : (byte)0;
        }
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4)));
        using var stream = new MemoryStream(); png.Save(stream); return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static ShapeSizingCase[] ShapeSizingCases()
    {
        var square = ShapeSizingSvg("viewBox='0 0 24 24'");
        var width = ShapeSizingSvg("width='40'"); var height = ShapeSizingSvg("height='20'");
        var zero = ShapeSizingSvg("width='0' height='20'");
        var media = ShapeSizingSvg("viewBox='0 0 24 24'", "<style>rect{x:0}@media(min-width:120px){rect{x:12}}</style><rect width='12' height='24' fill='#146342'/>");
        var sprite = ShapeSizingSvg("width='100' height='100' viewBox='0 0 100 100'", "<rect x='50' width='50' height='100' fill='#146342'/>");
        var small = ShapeSizingSvg("width='20' height='20'");
        var half = ShapeSizingSvg("width='100' height='100' viewBox='0 0 100 100'", "<rect x='75' width='25' height='100' fill='#146342'/>");
        var png = ShapeSizingPng(40, 20, true);
        return
        [
            new("SVG 比例 · 只拟合一次", ShapeSizingUrl(square), true, square, "contain", new(160, 80), new(40, 0, 80, 80)),
            new("没有显示源 · 独立轮廓", ShapeSizingUrl(square), true, null, "contain", new(160, 80), new(40, 0, 80, 80)),
            new("图片媒体条件 · 实际视口", ShapeSizingUrl(media), true, media, "contain", new(160, 80), new(40, 0, 40, 80)),
            new("零自然长度 · RTL 镜像", "image(rtl " + ShapeSizingUrl(zero) + ")", true, zero, "fill", new(160, 80), new(0, 0, 160, 80)),
            new("自然宽度 · none", ShapeSizingUrl(width), true, width, "none", new(160, 80), new(0, 0, 40, 80)),
            new("自然高度 · contain", ShapeSizingUrl(height), true, null, "contain", new(160, 80), new(0, 0, 160, 80)),
            new("源坐标裁剪 · 再拉伸", "image(url(\"" + ShapeSizingUri(sprite) + "#xywh=50,0,50,100\"))", false, null, "fill", new(160, 80), new(0, 0, 160, 80)),
            new("透明片段 · 空环绕区域", "image(url(\"" + ShapeSizingUri(sprite) + "#xywh=0,0,50,100\"))", false, null, "fill", new(160, 80), default),
            new("像素片段 · 候选密度", "image-set(image(url(\"" + png + "#xywh=20,0,20,10\")) 2x)", true, null, "none", new(160, 80), new(0, 0, 10, 5)),
            new("交叉渐变 · 部分自然尺寸", "cross-fade(" + ShapeSizingUrl(width) + "," + ShapeSizingUrl(small) + ")", true, null, "none", new(160, 80), new(0, 0, 30, 50)),
            new("加载回退 · 颜色不提供尺寸", "cross-fade(image(url('data:image/svg+xml,%3Csvg'),#146342)," + ShapeSizingUrl(small) + ")", true, null, "none", new(160, 80), new(0, 0, 20, 20)),
            new("裁剪与方向 · 保留透明半边", "image(rtl url(\"" + ShapeSizingUri(half) + "#xywh=50,0,50,100\"))", false, null, "fill", new(160, 80), new(0, 0, 80, 80)),
            new("内边距与边框 · 内容坐标", ShapeSizingUrl(square), true, square, "contain", new(160, 110), new(32, 7, 96, 96), "padding:5px;border:2px solid transparent;"),
            new("SVG 候选 · 忽略像素密度", "image-set(" + ShapeSizingUrl(small) + " 2x)", true, null, "none", new(160, 80), new(0, 0, 20, 20)),
            new("取样预算 · 保留逻辑高度", ShapeSizingUrl(width), true, null, "none", new(5000, 200), new(0, 0, 40, 200))
        ];
    }

    private static (StackPanel Root, FrameworkElement Float, TextBlock Body, Run Original) ShapeSizingRoot(
        ShapeSizingCase row, int width, bool right, bool reference, int dpi, List<SvgImage> sources)
    {
        var root = new StackPanel { Width = width, Height = 220, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
        Css.SetStyle(root, "display:flow-root;font-size:16px;line-height:20px;color:#233c32;");
        FrameworkElement floating;
        if (row.Image)
        {
            var image = new Image();
            if (row.Display is { } xml) { var source = SvgImage.FromSvgString(xml); sources.Add(source); image.Source = source; }
            floating = image;
        }
        else floating = new Border();
        var rect = row.Ink;
        var shape = reference ? rect.Width <= 0 || rect.Height <= 0 ? "inset(50%) border-box" :
            $"polygon({rect.X}px {rect.Y}px,{rect.Right}px {rect.Y}px,{rect.Right}px {rect.Bottom}px,{rect.X}px {rect.Bottom}px) border-box" : row.Shape;
        Css.SetStyle(floating, $"float:{(right ? "right" : "left")};box-sizing:border-box;width:{row.Box.Width}px;height:{row.Box.Height}px;object-fit:{row.Fit};object-position:{(row.Fit == "contain" ? "center" : "left top")};shape-outside:{shape};" + row.Extra);
        var body = new TextBlock { IsTextSelectionEnabled = true };
        var run = new Run(ShapeSizingText + (row.Box.Width > 2048 ? ShapeSizingText : ""));
        body.Inlines.Add(run); Css.SetStyle(body, "display:inline;");
        root.Children.Add(floating); root.Children.Add(body); VisualTreeHelper.SetRootDpi(root, new(dpi, dpi));
        void Layout() { ShapeSizingFlush(); root.Measure(new(width, 220)); root.Arrange(new(0, 0, width, 220)); root.UpdateLayout(); }
        Layout();
        if (floating.GetValue(CssShapeOutsideProperties.ValueProperty) is CssShapeImageValue value)
        {
            foreach (var resource in value.Resources)
                if (!resource.Ready.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Shape image readiness timeout");
        }
        Layout(); body.Select(2, 6); return (root, floating, body, run);
    }

    internal static int RunShapeImageSizing()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<object>(); var failures = 0; var rows = ShapeSizingCases();
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var row in rows) foreach (var width in new[] { 312, 672 }) foreach (var right in new[] { false, true })
                foreach (var recorded in new[] { false, true }) foreach (var dpi in new[] { 1, 2 })
                {
                    var label = $"shape-sizing-{backend}-{engine}-{Array.IndexOf(rows, row)}-{width}-{right}-{recorded}-{dpi}";
                    var sources = new List<SvgImage>(); var errors = new List<string>(); var largest = 0;
                    try
                    {
                        var actual = ShapeSizingRoot(row, width, right, false, dpi, sources);
                        var expected = ShapeSizingRoot(row, width, right, true, dpi, sources);
                        var a = actual.Body.GetSourceRangeSegments(0, actual.Body.Text.Length); var b = expected.Body.GetSourceRangeSegments(0, expected.Body.Text.Length);
                        if (a.Count != b.Count) throw new Exception($"Line count {a.Count} != {b.Count}");
                        for (var i = 0; i < a.Count; i++)
                        {
                            if (Math.Abs(a[i].StartX - b[i].StartX) > .001 || Math.Abs(a[i].EndX - b[i].EndX) > .001 || Math.Abs(a[i].Baseline - b[i].Baseline) > .001)
                                throw new Exception($"Line {i}: {a[i]} != {b[i]}");
                            var at = actual.Body.TransformToAncestor(actual.Root);
                            if (!ReferenceEquals(actual.Root.InputHitTest(new(at.X + (a[i].StartX + a[i].EndX) / 2, at.Y + a[i].Baseline - 2)), actual.Body)) throw new Exception("Text hit changed");
                        }
                        if (!ReferenceEquals(actual.Body.Inlines[0], actual.Original) || actual.Body.SelectedText != actual.Body.Text.Substring(2, 6)) throw new Exception("Source run/selection changed");
                        byte[] Capture(StackPanel root) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 220 * dpi, dpi, dc =>
                        {
                            if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                            else root.Render(dc);
                        }, () => true, context) ?? throw new Exception("Shape image capture unavailable"));
                        var pixels = Capture(actual.Root); var reference = Capture(expected.Root);
                        for (var i = 0; i < pixels.Length; i++)
                        {
                            var difference = Math.Abs(pixels[i] - reference[i]); largest = Math.Max(largest, difference);
                            if (difference > 3 && errors.Count < 4) errors.Add($"pixel {i / 4}, channel {i % 4}: {pixels[i]} != {reference[i]}");
                        }
                        if (errors.Count > 0) { Save(pixels, width * dpi, 220 * dpi, label + ".png"); Save(reference, width * dpi, 220 * dpi, label + "-expected.png"); }
                    }
                    catch (Exception error) { errors.Add(error.ToString()); }
                    finally { foreach (var source in sources) source.Dispose(); }
                    if (errors.Count > 0) failures++;
                    records.Add(new { label, largest, errors }); Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
                }
            }
            foreach (var viewport in new[] { 360, 720 })
            {
                var columns = viewport < 600 ? 1 : 2; var height = 24 + (int)Math.Ceiling(6d / columns) * 252; var sources = new List<SvgImage>();
                var gallery = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                for (var i = 0; i < 6; i++)
                {
                    var x = 24 + i % columns * 360; var y = 16 + i / columns * 252;
                    var caption = new TextBlock { Text = rows[i].Caption, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), Width = 312, Height = 22 };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); gallery.Children.Add(caption);
                    var content = ShapeSizingRoot(rows[i], 312, false, false, 1, sources).Root;
                    Jalium.UI.Controls.Canvas.SetLeft(content, x); Jalium.UI.Controls.Canvas.SetTop(content, y + 30); gallery.Children.Add(content);
                }
                gallery.Measure(new(viewport, height)); gallery.Arrange(new(0, 0, viewport, height)); gallery.UpdateLayout();
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => gallery.Render(dc), () => true, context)!), viewport, height, $"shape-image-sizing-gallery-{backend}-{viewport}.png");
                foreach (var source in sources) source.Dispose();
            }
            File.WriteAllText(Path.Combine(Output, "shape-image-sizing.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, tolerance = 3, nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
