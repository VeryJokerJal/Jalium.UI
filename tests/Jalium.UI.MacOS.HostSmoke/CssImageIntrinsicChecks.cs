using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record IntrinsicCase(string Caption, string Attributes, string Style, Size Box, Rect Ink);
    private static string IntrinsicXml(string attributes, string? body = null) => "<svg xmlns='http://www.w3.org/2000/svg' " + attributes + ">" +
        (body ?? "<rect width='100%' height='100%' fill='#146342'/>") + "</svg>";
    private static string IntrinsicUri(string xml) => "data:image/svg+xml," + Uri.EscapeDataString(xml);
    private static void IntrinsicFlush()
    { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void IntrinsicLayout(FrameworkElement owner)
    { owner.Measure(new(600, 400)); owner.Arrange(new(0, 0, owner.DesiredSize.Width, owner.DesiredSize.Height)); owner.UpdateLayout(); }
    private static void IntrinsicReady(FrameworkElement owner)
    {
        IntrinsicFlush(); IntrinsicLayout(owner);
        var resources = new List<CssBackgroundImageResource>();
        if (CssReplacedContent.Image(owner) is { } generated) resources.AddRange(generated.ActiveResources);
        if (owner.GetValue(CssBackgroundImageResource.ActiveProperty) is CssBackgroundImageResource[] backgrounds) resources.AddRange(backgrounds);
        if (owner.GetValue(CssBorderImageProperties.SourceProperty) is CssBorderImageSource border) resources.AddRange(border.Resources);
        if (owner.GetValue(CssMaskBorderProperties.SourceProperty) is CssMaskBorderSource mask) resources.AddRange(mask.Resources);
        var end = DateTime.UtcNow.AddSeconds(10);
        while (resources.Any(resource => !resource.Ready.IsCompleted) && DateTime.UtcNow < end) { IntrinsicFlush(); Thread.Sleep(1); }
        if (resources.Any(resource => !resource.Ready.IsCompletedSuccessfully)) throw new Exception("CSS image readiness timed out");
        IntrinsicFlush(); IntrinsicLayout(owner);
    }
    private static FrameworkElement IntrinsicOwner(string attributes, string style, string route, List<SvgImage> sources, string? body = null)
    {
        if (route == "native")
        {
            var source = SvgImage.FromSvgString(IntrinsicXml(attributes, body)); sources.Add(source);
            var owner = new Image { Source = source };
            Css.SetStyle(owner, "object-fit:fill;image-rendering:pixelated;" + style); IntrinsicFlush(); IntrinsicLayout(owner); return owner;
        }
        var uri = IntrinsicUri(IntrinsicXml(attributes, body)); var value = "url(\"" + uri + "\")";
        if (route == "annotation") value = "image(" + value + ")";
        if (route == "annotation-rtl") value = "image(rtl " + value + ")";
        var content = new TextBlock { Text = "正文" }; Css.SetClass(content, "intrinsic");
        var sheet = CssStyleSheet.Parse(".intrinsic{content:" + value + ";image-rendering:pixelated;" + style + "}");
        if (sheet.Diagnostics.Count != 0) throw new Exception("CSS image fixture did not compile");
        Css.GetStyleSheets(content).Add(sheet); IntrinsicReady(content); return content;
    }

    internal static int RunImageIntrinsic()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<object>(); var failures = 0;
        var cases = new IntrinsicCase[]
        {
            new("仅有比例 · 默认图片尺寸", "viewBox='0 0 24 24'", "", new(150, 150), new(0, 0, 150, 150)),
            new("单轴自然宽度 · 补齐高度", "width='40'", "", new(40, 150), new(0, 0, 40, 150)),
            new("单轴自然高度 · 补齐宽度", "height='20'", "", new(300, 20), new(0, 0, 300, 20)),
            new("没有自然尺寸 · 使用默认尺寸", "", "", new(300, 150), new(0, 0, 300, 150)),
            new("自然比例 · 跟随指定宽度", "viewBox='0 0 24 12'", "width:100px;", new(100, 50), new(0, 0, 100, 50)),
            new("none · 仅有比例的图片", "viewBox='0 0 24 12'", "width:200px;height:100px;object-fit:none;object-position:left top;", new(200, 100), new(0, 0, 200, 100)),
            new("none · 保留自然宽度", "width='40'", "width:200px;height:100px;object-fit:none;object-position:left top;", new(200, 100), new(0, 0, 40, 100)),
            new("scale-down · 不放大固定图片", "width='40' height='20'", "width:200px;height:100px;object-fit:scale-down;object-position:left top;", new(200, 100), new(0, 0, 40, 20)),
            new("零宽度 · CSS 明确分配尺寸", "width='0' height='20'", "width:200px;height:100px;object-fit:fill;", new(200, 100), new(0, 0, 200, 100)),
            new("比例与边框 · 使用内容尺寸", "viewBox='0 0 24 12'", "box-sizing:border-box;width:100px;padding:5px;border:2px solid transparent;", new(100, 57), new(7, 7, 86, 43)),
            new("自然高度与首选比例 · 推导宽度", "height='20'", "aspect-ratio:3;", new(60, 20), new(0, 0, 60, 20)),
            new("首选比例 · 重新计算默认矩形", "viewBox='0 0 24 24'", "aspect-ratio:3;", new(300, 100), new(100, 0, 100, 100))
        };
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                void Check(string label, FrameworkElement owner, Size box, Rect ink, bool recorded, int dpi, FrameworkElement? cssReference = null)
                {
                    var errors = new List<string>(); var largest = 0;
                    try
                    {
                        VisualTreeHelper.SetRootDpi(owner, new(dpi, dpi)); IntrinsicLayout(owner);
                        if (cssReference is not null) { VisualTreeHelper.SetRootDpi(cssReference, new(dpi, dpi)); IntrinsicLayout(cssReference); }
                        if (Math.Abs(owner.ActualWidth - box.Width) > .001 || Math.Abs(owner.ActualHeight - box.Height) > .001)
                            throw new Exception($"Used size {owner.ActualWidth},{owner.ActualHeight} != {box}");
                        var w = (int)Math.Ceiling(box.Width + 24); var h = (int)Math.Ceiling(box.Height + 24);
                        byte[] Capture(bool reference)
                        {
                            using var svg = SvgImage.FromSvgString(IntrinsicXml($"width='{box.Width}' height='{box.Height}'",
                                $"<rect x='{ink.X}' y='{ink.Y}' width='{ink.Width}' height='{ink.Height}' fill='#146342'/>"));
                            return Pixels(MacOSWritingToolsBitmapCapture.Capture(w * dpi, h * dpi, dpi, dc =>
                            {
                                void Paint(DrawingContext c)
                                {
                                    c.PushTransform(new TranslateTransform(12, 12));
                                    if (reference && cssReference is not null) cssReference.Render(c);
                                    else if (reference) c.DrawImage(svg, new Rect(box), BitmapScalingMode.NearestNeighbor); else owner.Render(c);
                                    c.Pop();
                                }
                                if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); Paint(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); } else Paint(dc);
                            }, () => true, context) ?? throw new Exception("Intrinsic image capture unavailable"));
                        }
                        var actual = Capture(false); var expected = Capture(true);
                        for (var i = 0; i < actual.Length; i += 4) for (var channel = 0; channel < 4; channel++)
                        {
                            var a = channel == 3 ? actual[i + 3] : (actual[i + channel] * actual[i + 3] + 127) / 255;
                            var b = channel == 3 ? expected[i + 3] : (expected[i + channel] * expected[i + 3] + 127) / 255;
                            var error = Math.Abs(a - b); largest = Math.Max(largest, error);
                            if (error > 3 && errors.Count < 4) errors.Add($"pixel {i / 4} channel {channel}: {a} != {b}");
                        }
                        if (errors.Count > 0) { Save(actual, w * dpi, h * dpi, label + ".png"); Save(expected, w * dpi, h * dpi, label + "-expected.png"); }
                    }
                    catch (Exception error) { errors.Add(error.ToString()); }
                    if (errors.Count > 0) failures++; records.Add(new { label, largest, errors });
                    Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
                }
                foreach (var row in cases) foreach (var route in new[] { "content", "native", "annotation", "annotation-rtl" })
                foreach (var recorded in new[] { false, true }) foreach (var dpi in new[] { 1, 2 })
                {
                    var sources = new List<SvgImage>();
                    try
                    {
                        var owner = IntrinsicOwner(row.Attributes, row.Style, route, sources);
                        // Keep clips and image sampling identical while the
                        // reference SVG supplies fixed, independent dimensions.
                        // Pixelated and nearest-neighbor differ at 2x GPU edges.
                        var reference = row.Style.Contains("padding:") ? IntrinsicOwner("width='86' height='43'",
                            "box-sizing:border-box;width:100px;height:57px;padding:5px;border:2px solid transparent;", route, sources) : null;
                        if (Array.IndexOf(cases, row) == 11)
                            reference = IntrinsicOwner("width='300' height='100'", "width:300px;height:100px;", route, sources,
                                "<rect x='100' width='100' height='100' fill='#146342'/>");
                        Check($"intrinsic-{backend}-{engine}-{Array.IndexOf(cases, row)}-{route}-{recorded}-{dpi}", owner, row.Box, row.Ink, recorded, dpi, reference);
                    }
                    finally { foreach (var source in sources) source.Dispose(); }
                }
                foreach (var attributes in new[] { "viewBox='0 0 24 24'", "width='40'", "height='20'", "width='40' height='20'" })
                foreach (var recorded in new[] { false, true }) foreach (var dpi in new[] { 1, 2 })
                {
                    var owner = new Border(); Css.SetClass(owner, "intrinsic");
                    var uri = IntrinsicUri(IntrinsicXml(attributes, ""));
                    Css.GetStyleSheets(owner).Add(CssStyleSheet.Parse(".intrinsic{box-sizing:border-box;width:200px;height:100px;border:10px solid transparent;background:#146342;image-rendering:pixelated;mask-border-source:url(\"" + uri + "\");mask-border-slice:5;mask-border-width:auto;}"));
                    IntrinsicReady(owner);
                    var left = attributes.Contains("width='40'") ? 5 : 10; var top = attributes.Contains("height='20'") ? 5 : 10;
                    Check($"intrinsic-mask-border-{backend}-{engine}-{attributes}-{recorded}-{dpi}", owner, new(200, 100), new(left, top, 200 - 2 * left, 100 - 2 * top), recorded, dpi);
                }
            }
            foreach (var viewport in new[] { 360, 720 })
            {
                var columns = viewport < 600 ? 1 : 2; var height = 24 + (int)Math.Ceiling(6d / columns) * 206; var sources = new List<SvgImage>();
                var canvas = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                for (var i = 0; i < 6; i++)
                {
                    var x = 24 + i % columns * 360; var y = 16 + i / columns * 206;
                    var caption = new TextBlock { Text = cases[i].Caption, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), Width = 312, Height = 22 };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); canvas.Children.Add(caption);
                    var content = IntrinsicOwner(cases[i].Attributes, cases[i].Style, "content", sources);
                    Jalium.UI.Controls.Canvas.SetLeft(content, x); Jalium.UI.Controls.Canvas.SetTop(content, y + 30); canvas.Children.Add(content);
                }
                canvas.Measure(new(viewport, height)); canvas.Arrange(new(0, 0, viewport, height)); canvas.UpdateLayout();
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => canvas.Render(dc), () => true, context)!), viewport, height, $"image-intrinsic-gallery-{backend}-{viewport}.png");
                foreach (var source in sources) source.Dispose();
            }
            File.WriteAllText(Path.Combine(Output, "image-intrinsic.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, tolerance = 3, nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
