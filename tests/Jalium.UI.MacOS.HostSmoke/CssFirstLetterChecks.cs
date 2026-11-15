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
    private sealed record FirstLetterCase(string Caption, string Source, int Length, string Style,
        string Language = "en-US", bool Legacy = false, bool Ancestor = false);
    private const string FirstLetterTail = " 文字沿着首字自然排版，窄窗口保持阅读顺序。The paragraph continues across several lines, preserving the original selection and source offsets.";
    private static FirstLetterCase[] FirstLetterCases() =>
    [
        new("中文引号 · 完整首字", "「中」文排版" + FirstLetterTail, 3, "font-size:42px;color:#146342;"),
        new("组合字素 · 附加符", "e\u0301cole française" + FirstLetterTail, 2, "font-size:42px;color:#146342;"),
        new("Emoji · 肤色与连接符", "👩🏽‍💻 works here" + FirstLetterTail, "👩🏽‍💻".Length, "font-size:42px;color:#146342;"),
        new("荷兰语 ij · 大写映射", "ijsselmeer verder" + FirstLetterTail, 2, "font-size:42px;text-transform:uppercase;color:#146342;", "nl-NL"),
        new("左侧浮动 · 行盒避让", "Jalium typography" + FirstLetterTail, 1, "float:left;font-size:64px;line-height:1;margin-right:8px;color:#146342;"),
        new("右侧浮动 · 边框与内边距", "Alpha beta" + FirstLetterTail, 1, "float:right;font-size:56px;line-height:1;margin-left:8px;padding:3px;border:2px solid #146342;color:#146342;"),
        new("背景与透明度 · 行内装饰", "Alpha beta" + FirstLetterTail, 1, "font-size:40px;padding:3px;border:2px solid #146342;border-radius:6px;background:#bee8d1;opacity:.7;color:#146342;"),
        new("字体特性 · 小型大写", "alphabet typography" + FirstLetterTail, 1, "font-size:42px;font-variant-caps:small-caps;font-family:serif;color:#146342;"),
        new("法文窄空格 · 引号组合", "«\u202FA\u202F» encore" + FirstLetterTail, 5, "font-size:36px;color:#146342;"),
        new("大小写展开 · 原始索引", "ßeta typography" + FirstLetterTail, 1, "font-size:42px;text-transform:uppercase;color:#146342;"),
        new("兼容语法 · 单冒号", "Alpha beta" + FirstLetterTail, 1, "font-size:42px;letter-spacing:1px;color:#146342;", Legacy: true),
        new("祖先与段落 · 嵌套继承", "Alpha beta" + FirstLetterTail, 1, "font-size:48px;color:#146342;", Ancestor: true)
    ];

    private static (StackPanel Root, TextBlock Body, Run Original) FirstLetterRoot(FirstLetterCase row, int width, bool reference, int dpi)
    {
        var root = new StackPanel { Width = width, Height = 220, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
        Css.SetClass(root, "first-letter-root");
        var body = new TextBlock { IsTextSelectionEnabled = true, Language = Jalium.UI.Markup.XmlLanguage.GetLanguage(row.Language) };
        Css.SetClass(body, "first-letter-body");
        var run = new Run(row.Source);
        if (reference)
        {
            body.Inlines.Add(new Run(row.Source[..row.Length]));
            body.Inlines.Add(new Run(row.Source[row.Length..]));
            Css.SetStyle(body.Inlines[0], "display:inline;" + row.Style);
        }
        else body.Inlines.Add(run);
        root.Children.Add(body);
        var rules = ".first-letter-root {display:block;} .first-letter-body {display:block;font-size:16px;line-height:24px;white-space:normal;color:#233c32;}";
        if (!reference)
            rules += row.Ancestor
                ? ".first-letter-root::first-letter {font-size:2em;color:#146342;} .first-letter-body::first-letter {font-size:1.5em;}"
                : ".first-letter-body" + (row.Legacy ? ":first-letter" : "::first-letter") + " {" + row.Style + "}";
        var sheet = CssStyleSheet.Parse(rules);
        if (sheet.Diagnostics.Count > 0) throw new Exception(string.Join("; ", sheet.Diagnostics));
        Css.GetStyleSheets(root).Add(sheet); VisualTreeHelper.SetRootDpi(root, new(dpi, dpi));
        FirstLetterLayout(root, width, 220);
        body.Select(0, Math.Min(Math.Max(6, row.Length), row.Source.Length));
        return (root, body, run);
    }

    private static void FirstLetterLayout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout();
        }
    }

    internal static int RunFirstLetters()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var rows = FirstLetterCases(); var records = new List<object>(); var failures = 0;
        var pairs = new Dictionary<(RenderingEngine Engine, int Scene, int Width, int Dpi),
            ((StackPanel Root, TextBlock Body, Run Original) Actual, (StackPanel Root, TextBlock Body, Run Original) Reference)>();
        var limited = int.TryParse(Environment.GetEnvironmentVariable("JALIUM_CSS_FIRST_LETTER_LIMIT"), out var limit) && limit > 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var row in rows) foreach (var width in new[] { 312, 672 }) foreach (var recorded in new[] { false, true }) foreach (var dpi in new[] { 1, 2 })
                {
                    if (limited && records.Count >= limit) goto Complete;
                    var label = $"first-letter-{backend}-{engine}-{Array.IndexOf(rows, row)}-{width}-{recorded}-{dpi}";
                    var errors = new List<string>(); var largest = 0; var timing = new Dictionary<string, long>();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var key = (engine, Array.IndexOf(rows, row), width, dpi);
                        if (!pairs.TryGetValue(key, out var pair))
                        {
                            var a = FirstLetterRoot(row, width, false, dpi); timing["actualLayoutMs"] = watch.ElapsedMilliseconds; watch.Restart();
                            var b = FirstLetterRoot(row, width, true, dpi); timing["referenceLayoutMs"] = watch.ElapsedMilliseconds; watch.Restart();
                            pair = (a, b); pairs[key] = pair;
                        }
                        else timing["reusedForRecording"] = 1;
                        var actual = pair.Actual; var expected = pair.Reference;
                        if (recorded) pairs.Remove(key);
                        for (var index = 0; index < row.Source.Length; index++)
                        {
                            var a = actual.Body.DocumentTextBounds(index, 1); var b = expected.Body.DocumentTextBounds(index, 1);
                            if (a.Count != b.Count) throw new Exception($"Source {index} bounds count {a.Count} != {b.Count}");
                            for (var n = 0; n < a.Count; n++)
                                if (Math.Abs(a[n].X - b[n].X) > .001 || Math.Abs(a[n].Y - b[n].Y) > .001 ||
                                    Math.Abs(a[n].Width - b[n].Width) > .001 || Math.Abs(a[n].Height - b[n].Height) > .001)
                                    throw new Exception($"Source {index} bounds {a[n]} != {b[n]}");
                        }
                        timing["sourceBoundsMs"] = watch.ElapsedMilliseconds; watch.Restart();
                        if (!ReferenceEquals(actual.Original, actual.Body.Inlines[0]) || actual.Original.Text != row.Source ||
                            actual.Body.Text != row.Source || actual.Body.SelectedText != row.Source[..Math.Min(Math.Max(6, row.Length), row.Source.Length)])
                            throw new Exception("Source Run, text or selection changed");
                        foreach (var index in new[] { 0, row.Length, Math.Min(row.Source.Length - 1, row.Length + 8) })
                            foreach (var bounds in actual.Body.DocumentTextBounds(index, 1))
                            {
                                var point = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                                var wanted = expected.Body.DocumentCharacterFromPoint(point); var found = actual.Body.DocumentCharacterFromPoint(point);
                                if (wanted != found) throw new Exception($"Point hit {found} != {wanted}");
                                var offset = actual.Body.TransformToAncestor(actual.Root);
                                if (!ReferenceEquals(actual.Root.InputHitTest(new(point.X + offset.X, point.Y + offset.Y)), actual.Body))
                                    throw new Exception("Private pseudo box leaked through input hit testing");
                            }
                        timing["hitMs"] = watch.ElapsedMilliseconds; watch.Restart();
                        byte[] Capture(StackPanel root) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 220 * dpi, dpi, dc =>
                        {
                            if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                            else root.Render(dc);
                        }, () => true, context) ?? throw new Exception("First-letter capture unavailable"));
                        var pixels = Capture(actual.Root); var reference = Capture(expected.Root);
                        timing["captureMs"] = watch.ElapsedMilliseconds; watch.Restart();
                        for (var index = 0; index < pixels.Length; index++)
                        {
                            var delta = Math.Abs(pixels[index] - reference[index]); largest = Math.Max(largest, delta);
                            if (delta > 3 && errors.Count < 4) errors.Add($"Pixel {index / 4}, channel {index % 4}: {pixels[index]} != {reference[index]}");
                        }
                        if (errors.Count > 0) { Save(pixels, width * dpi, 220 * dpi, label + ".png"); Save(reference, width * dpi, 220 * dpi, label + "-expected.png"); }
                    }
                    catch (Exception error) { errors.Add(error.ToString()); }
                    if (errors.Count > 0) failures++;
                    records.Add(new { label, largest, errors, timing }); Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {JsonSerializer.Serialize(timing)} {string.Join(" | ", errors)}");
                }
            }
            Complete:
            if (!limited)
            foreach (var viewport in new[] { 360, 720 })
            {
                var columns = viewport < 600 ? 1 : 2; var height = 24 + 6 / columns * 252;
                var gallery = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                for (var index = 0; index < 6; index++)
                {
                    var x = 24 + index % columns * 360; var y = 16 + index / columns * 252;
                    var row = rows[new[] { 0, 1, 4, 5, 6, 3 }[index]];
                    var caption = new TextBlock { Text = row.Caption, FontSize = 15, Width = 312, Height = 22, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)) };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); gallery.Children.Add(caption);
                    var content = FirstLetterRoot(row, 312, false, 1).Root;
                    Jalium.UI.Controls.Canvas.SetLeft(content, x); Jalium.UI.Controls.Canvas.SetTop(content, y + 30); gallery.Children.Add(content);
                }
                FirstLetterLayout(gallery, viewport, height);
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => gallery.Render(dc), () => true, context)!), viewport, height,
                    $"first-letter-gallery-{backend}-{viewport}.png");
            }
            File.WriteAllText(Path.Combine(Output, "first-letter.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, tolerance = 3, limited,
                nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
