using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record InitialCase(string Head, double Size, double Sink);
    private const string InitialTail = "lpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron. The paragraph continues beside the initial letter.";
    private const string InitialBodyStyle = "display:block;font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;";
    private static readonly Color InitialInk = Color.FromRgb(0x14, 0x63, 0x42);

    // This reference uses an ordinary, empty CSS float for exclusion and paints
    // the initial directly from font metrics. It never sets initial-letter or
    // calls its used-size, alignment, scope or text-projection helpers.
    private sealed class InitialReference(FormattedText glyph, Point origin) : StackPanel
    {
        protected override void OnRender(DrawingContext context)
        { base.OnRender(context); context.DrawText(glyph, origin); }
    }

    private sealed class InitialReferenceTail : TextBlock
    {
        private readonly Dictionary<string, NativeTextParagraph> _paragraphs = [];
        protected override void OnRender(DrawingContext context)
        {
            foreach (var line in GetDocumentLines(RenderSize.Width))
            {
                var content = Text.Substring(line.Start, line.End - line.Start);
                if (content.Length == 0) continue;
                if (!_paragraphs.TryGetValue(content, out var paragraph))
                {
                    paragraph = NativeTextParagraph.TryCreate([new(content, "Georgia", 16, 400, 0, Color.FromRgb(0x23, 0x3c, 0x32))],
                        "Georgia", 16, 1000000, 24, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)
                        ?? throw new Exception("Reference body native line unavailable");
                    _paragraphs.Add(content, paragraph);
                }
                var first = DocumentTextBounds(line.Start, 0)[0];
                var glyphs = new FormattedText(content, "Georgia", 16) { PlatformTextLine = paragraph.Lines[0] };
                context.DrawText(glyphs, new(first.X, line.Top));
            }
        }
    }

    private static (StackPanel Root, TextBlock Body, Run Original) InitialActual(InitialCase row, int width, int dpi)
    {
        var root = new StackPanel { Width = width, Height = 180, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
        var body = new TextBlock { IsTextSelectionEnabled = true }; var original = new Run(row.Head + InitialTail); body.Inlines.Add(original);
        Css.SetClass(body, "initial-body"); root.Children.Add(body);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".initial-body{" + InitialBodyStyle + "}.initial-body::first-letter{initial-letter:" +
            row.Size.ToString("R", CultureInfo.InvariantCulture) + " " + row.Sink.ToString("R", CultureInfo.InvariantCulture) + ";color:#146342;}"));
        VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); FirstLetterLayout(root, width, 180); return (root, body, original);
    }

    private static (StackPanel Root, TextBlock Body, double Shift) InitialOracle(InitialCase row, int width, int dpi)
    {
        var units = TextMeasurement.GetFontUnitMetrics("Georgia", 16); var font = TextMeasurement.GetFontMetrics("Georgia", 16);
        var cap = (row.Size - 1) * 24 + units.CapHeight;
        var usedSize = cap / units.CapHeight * 16;
        var glyph = new FormattedText(row.Head, "Georgia", usedSize) { Foreground = new SolidColorBrush(InitialInk) };
        TextMeasurement.MeasureText(glyph);
        if (!TextMeasurement.TryGetFontInkBounds(glyph, out var ink)) throw new Exception("Reference glyph ink unavailable");
        // The primitive DrawText path snaps font sizes to device pixels. Keep
        // the exact fractional font size with an independently shaped native
        // paragraph, as required for initial-letter's metric-derived size.
        var paragraph = NativeTextParagraph.TryCreate([new(row.Head, "Georgia", usedSize, 400, 0, InitialInk)],
            "Georgia", usedSize, 1000000, 0, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)
            ?? throw new Exception("Reference native glyph unavailable");
        glyph = new(row.Head, "Georgia", usedSize) { Foreground = new SolidColorBrush(InitialInk),
            PlatformTextLine = paragraph.Lines[0], Baseline = paragraph.Lines[0].Metrics.Baseline };
        var height = Math.Max(cap, cap + ink.Bottom);
        var baseline = font.Ascent + (24 - font.Ascent - font.Descent) / 2;
        var relativeTop = row.Size >= row.Sink ? baseline + (row.Sink - 1) * 24 - cap : baseline - units.CapHeight;
        var shift = Math.Max(0, -relativeTop);
        var root = new InitialReference(glyph, new(-ink.Left, shift + relativeTop + cap - glyph.Baseline))
        { Width = width, Height = 180, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
        string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture) + "px";
        Css.SetStyle(root, InitialBodyStyle + "padding-top:" + Number(shift));
        var exclusion = new Border(); Css.SetStyle(exclusion, "display:block;float:left;width:" + Number(ink.Width) +
            ";height:" + Number(height) + ";margin-top:" + Number(relativeTop));
        var body = new InitialReferenceTail { Text = InitialTail }; Css.SetStyle(body, "display:inline;");
        root.Children.Add(exclusion); root.Children.Add(body); VisualTreeHelper.SetRootDpi(root, new(dpi, dpi));
        FirstLetterLayout(root, width, 180); return (root, body, shift);
    }

    internal static int RunInitialLetters()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        InitialCase[] rows = [new("A", 3, 3), new("A", 3, 1), new("A", 3, 2), new("A", 2.5, 3), new("j", 3, 3), new("e\u0301", 3, 3)];
        var records = new List<object>(); var failures = 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var row in rows) foreach (var width in new[] { 312, 672 }) foreach (var dpi in new[] { 1, 2 })
                {
                    var actual = InitialActual(row, width, dpi); var expected = InitialOracle(row, width, dpi);
                    foreach (var recorded in new[] { false, true })
                    {
                        var label = $"initial-letter-{backend}-{engine}-{Array.IndexOf(rows, row)}-{width}-{dpi}-{recorded}";
                        var errors = new List<string>(); var largest = 0;
                        try
                        {
                            if (!ReferenceEquals(actual.Original, actual.Body.Inlines[0]) || actual.Original.Text != row.Head + InitialTail || actual.Original.FontSize != 16)
                                throw new Exception("Authored source or computed font changed");
                            for (var index = row.Head.Length; index < row.Head.Length + InitialTail.Length; index++)
                            {
                                var a = actual.Body.DocumentTextBounds(index, 1); var b = expected.Body.DocumentTextBounds(index - row.Head.Length, 1);
                                if (a.Count != b.Count) throw new Exception($"Source {index} bounds count {a.Count} != {b.Count}");
                                var delta = expected.Body.TransformToAncestor(expected.Root);
                                for (var n = 0; n < a.Count; n++)
                                    if (Math.Abs(a[n].X - b[n].X - delta.X) > .001 || Math.Abs(a[n].Y - b[n].Y - delta.Y) > .001 ||
                                        Math.Abs(a[n].Width - b[n].Width) > .001 || Math.Abs(a[n].Height - b[n].Height) > .001)
                                        throw new Exception($"Source {index} geometry {a[n]} != {b[n]} offset {delta}");
                            }
                            byte[] Capture(StackPanel root) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 180 * dpi, dpi, dc =>
                            {
                                if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                                else root.Render(dc);
                            }, () => true, context) ?? throw new Exception("Initial capture unavailable"));
                            var pixels = Capture(actual.Root); var reference = Capture(expected.Root);
                            for (var index = 0; index < pixels.Length; index++)
                            {
                                var difference = Math.Abs(pixels[index] - reference[index]); largest = Math.Max(largest, difference);
                                if (difference > 3 && errors.Count < 4) errors.Add($"Pixel {index / 4} channel {index % 4}: {pixels[index]} != {reference[index]}");
                            }
                            if (errors.Count > 0) { Save(pixels, width * dpi, 180 * dpi, label + ".png"); Save(reference, width * dpi, 180 * dpi, label + "-expected.png"); }
                            actual.Body.SelectAll();
                            if (actual.Body.SelectedText != row.Head + InitialTail) throw new Exception("Selection source changed");
                            actual.Body.Select(0, 0);
                        }
                        catch (Exception error) { errors.Add(error.ToString()); }
                        if (errors.Count > 0) failures++;
                        records.Add(new { label, largest, errors }); Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
                    }
                }
            }
            File.WriteAllText(Path.Combine(Output, "initial-letter.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records,
                tolerance = 3, nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }

    internal static int RunInitialLetterGallery()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        (string Caption, string Style)[] cases =
        [
            ("三行下沉", "initial-letter:3;"),
            ("上提首字", "initial-letter:3 raise;"),
            ("轮廓绕排", "initial-letter:3;initial-letter-wrap:all;"),
            ("中文首字", "initial-letter:3;initial-letter-wrap:grid;"),
            ("边框对齐", "initial-letter:3;initial-letter-align:border-box;padding:3px;border:2px solid #146342;"),
            ("分数行数", "initial-letter:2.5 3;")
        ];
        try
        {
            foreach (var viewport in new[] { 360, 720 })
            {
                var columns = viewport < 600 ? 1 : 2; var height = 24 + cases.Length / columns * 222;
                var gallery = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                for (var index = 0; index < cases.Length; index++)
                {
                    var x = 24 + index % columns * 360; var y = 16 + index / columns * 222;
                    var caption = new TextBlock { Text = cases[index].Caption, FontSize = 15, Width = 312, Height = 24, Foreground = new SolidColorBrush(InitialInk) };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); gallery.Children.Add(caption);
                    var body = new TextBlock
                    {
                        Text = index == 3 ? "春风拂过树梢，文字沿着首字自然排版，窄窗口保持阅读顺序。阅读是一段安静的旅程。春风拂过树梢，文字沿着首字自然排版。" : "A" + InitialTail,
                        Width = 312, Height = 180,
                        Language = Jalium.UI.Markup.XmlLanguage.GetLanguage(index == 3 ? "zh-CN" : "en-US")
                    };
                    Css.SetClass(body, "initial-gallery-body");
                    Css.GetStyleSheets(body).Add(CssStyleSheet.Parse(".initial-gallery-body{" + InitialBodyStyle +
                        "}.initial-gallery-body::first-letter{color:#146342;" + cases[index].Style + "}"));
                    Jalium.UI.Controls.Canvas.SetLeft(body, x); Jalium.UI.Controls.Canvas.SetTop(body, y + 30); gallery.Children.Add(body);
                }
                FirstLetterLayout(gallery, viewport, height);
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => gallery.Render(dc), () => true, context)
                    ?? throw new Exception("Initial gallery capture unavailable")), viewport, height, $"initial-letter-gallery-{backend}-{viewport}.png");
            }
            return 0;
        }
        finally { PopPool(pool); }
    }

    internal static int RunInitialLetterFlowGallery()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var records = new List<object>(); var failures = 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var viewport in new[] { 360, 720 })
                {
                    var columns = viewport < 600 ? 1 : 2;
                    int[] rowHeights = columns == 1 ? [520, 220, 220, 220] : [520, 220];
                    var height = 32 + rowHeights.Sum();
                    var gallery = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                    string[] captions = ["长首字自然换行", "首行字体与首字对齐", "嵌入内容上对齐", "独立嵌入内容"];
                    for (var index = 0; index < captions.Length; index++)
                    {
                        var x = 24 + index % columns * 360; var y = 16 + rowHeights.Take(index / columns).Sum();
                        var caption = new TextBlock { Text = captions[index], FontSize = 15, Width = 312, Height = 24, Foreground = new SolidColorBrush(InitialInk) };
                        Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); gallery.Children.Add(caption);
                        FrameworkElement body;
                        if (index == 1)
                        {
                            body = new TextBlock { Text = "A" + InitialTail, IsTextSelectionEnabled = true };
                            Css.SetClass(body, "flow-styled-first-line");
                            Css.GetStyleSheets(body).Add(CssStyleSheet.Parse(".flow-styled-first-line{" + InitialBodyStyle + "}" +
                                ".flow-styled-first-line::first-line{font-family:Arial;font-size:22px;line-height:32px;}" +
                                ".flow-styled-first-line::first-letter{initial-letter:3;color:#146342;}"));
                        }
                        else
                        {
                            var paragraph = new StackPanel(); Css.SetStyle(paragraph, InitialBodyStyle);
                            FrameworkElement initial;
                            if (index == 0) initial = new TextBlock { Text = "Alpha beta gamma delta eta", IsTextSelectionEnabled = true };
                            else
                            {
                                var contents = new StackPanel(); Css.SetStyle(contents, "display:contents;");
                                if (index == 2) { var letter = new TextBlock { Text = "A" }; Css.SetStyle(letter, "display:inline;"); contents.Children.Add(letter); }
                                var atom = new Border { Width = 32, Height = 40, Background = new SolidColorBrush(Color.FromRgb(0x3f, 0x78, 0x9b)) };
                                Css.SetStyle(atom, "display:inline-block;margin-right:4px;" + (index == 2 ? "vertical-align:top;" : "")); contents.Children.Add(atom);
                                initial = new Border { Child = contents };
                            }
                            Css.SetStyle(initial, "display:inline;initial-letter:3;color:#146342;");
                            var tail = new TextBlock { Text = index == 0 ? " continues with ordinary text. The paragraph keeps its reading order when the window changes width." : InitialTail };
                            Css.SetStyle(tail, "display:inline;"); paragraph.Children.Add(initial); paragraph.Children.Add(tail); body = paragraph;
                        }
                        body.Width = 312; body.Height = rowHeights[index / columns] - 50;
                        Jalium.UI.Controls.Canvas.SetLeft(body, x); Jalium.UI.Controls.Canvas.SetTop(body, y + 32); gallery.Children.Add(body);
                    }
                    FirstLetterLayout(gallery, viewport, height);
                    byte[] Capture(bool recorded) => Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc =>
                    {
                        if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); gallery.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                        else gallery.Render(dc);
                    }, () => true, context) ?? throw new Exception("Initial flow gallery capture unavailable"));
                    var direct = Capture(false); var replay = Capture(true); var largest = 0;
                    for (var n = 0; n < direct.Length; n++) largest = Math.Max(largest, Math.Abs(direct[n] - replay[n]));
                    var label = $"initial-letter-flow-{backend}-{engine}-{viewport}";
                    Save(direct, viewport, height, label + ".png"); Save(replay, viewport, height, label + "-replay.png");
                    var atomPixels = new int[2];
                    for (var index = 2; index < 4; index++)
                    {
                        var left = 24 + index % columns * 360; var top = 48 + rowHeights.Take(index / columns).Sum();
                        for (var y = top; y < top + 100; y++) for (var x = left; x < left + 110; x++)
                        {
                            var pixel = (y * viewport + x) * 4;
                            if (direct[pixel] == 0x9b && direct[pixel + 1] == 0x78 && direct[pixel + 2] == 0x3f) atomPixels[index - 2]++;
                        }
                    }
                    if (largest > 3 || atomPixels.Any(count => count < 1000) || !gallery.IsMeasureValid || !gallery.IsArrangeValid) failures++;
                    records.Add(new { label, largest, atomPixels, measureValid = gallery.IsMeasureValid, arrangeValid = gallery.IsArrangeValid });
                    Console.WriteLine($"{(largest <= 3 ? "PASS" : "FAIL")} {label} replay delta {largest}");
                }
            }
            File.WriteAllText(Path.Combine(Output, "initial-letter-flow.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records,
                nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
