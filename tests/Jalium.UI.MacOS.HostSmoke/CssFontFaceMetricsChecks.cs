using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;
internal static partial class CssWritingToolsChecks
{
    private sealed record FaceMetricCase(string Name, string First, string Second, double FirstSize, double SecondSize,
        double Ascent, double Descent, double Gap, double SecondAscent, double SecondDescent, double SecondGap,
        string Adjustment = "none", double? LineHeight = null);
    private sealed class FaceMetricReference : FrameworkElement, IDisposable
    {
        private readonly List<(NativeTextParagraph Paragraph, double Baseline, int Split)> _lines = [];
        private readonly bool _fragmented;
        internal FaceMetricReference(string first, string second, FaceMetricCase row, int width, bool fragmented)
        {
            Width = width; Height = 192; _fragmented = fragmented;
            if (row.FirstSize <= 0) return;
            using var a = TextMeasurement.CreateTextFormatFromFamilyList(RenderContext.Current!, first, (float)row.FirstSize, 400, 0);
            using var b = TextMeasurement.CreateTextFormatFromFamilyList(RenderContext.Current!, second, (float)row.SecondSize, 400, 0);
            double advance = a.MeasureText("x", 10000, 1000).Width + b.MeasureText("A", 10000, 1000).Width;
            double y = 0;
            foreach (string text in advance > width ? new[] { "x", "A" } : new[] { "xA" })
            {
                double ascent = row.Ascent, descent = row.Descent, gap = row.Gap;
                if (row.LineHeight is null && text.Contains('A')) { ascent = Math.Max(ascent, row.SecondAscent); descent = Math.Max(descent, row.SecondDescent); gap = Math.Max(gap, row.SecondGap); }
                double height = row.LineHeight ?? ascent + descent + gap;
                double baseline = ascent + (height - ascent - descent) / 2;
                var spans = text.Select(c => new NativeTextParagraph.Span(c.ToString(), c == 'x' ? first : second,
                    c == 'x' ? row.FirstSize : row.SecondSize, 400, 0, Colors.Black)).ToArray();
                var units = Enumerable.Range(0, text.Length).Select(i => new NativeMethods.ParagraphAdvanceUnit { TextPosition = (uint)i, Length = 1 }).ToArray();
                var paragraph = NativeTextParagraph.TryCreateAdvanced(spans, first, row.FirstSize, 10000, 1, FlowDirection.LeftToRight, units, contextPaint: true) ?? throw new Exception("Independent metric reference unavailable");
                _lines.Add((paragraph, y + baseline, text == "xA" ? 1 : 0)); y += height;
            }
        }
        protected override Size MeasureOverride(Size available) => new(Width, Height);
        protected override void OnRender(DrawingContext context)
        {
            foreach (var (paragraph, baseline, split) in _lines)
            {
                void Paint(int start, int count)
                {
                    var line = paragraph.Lines[0].ForSource(start, count, Color.FromRgb(0x14, 0x63, 0x42), 1, [], 0);
                    context.DrawText(new FormattedText(paragraph.Text.Substring(start, count), "Arial", 32) { PlatformTextLine = line, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), MaxTextWidth = 10000, MaxTextHeight = 192 }, new(0, baseline - line.Metrics.Baseline));
                }
                if (_fragmented && split > 0) { Paint(0, split); Paint(split, paragraph.Text.Length - split); } else Paint(0, paragraph.Text.Length);
            }
        }
        public void Dispose() { foreach (var line in _lines) line.Paragraph.Dispose(); }
    }
    internal static int RunFontFaceMetrics()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string root = Environment.GetEnvironmentVariable("JALIUM_REPO_ROOT") ?? Directory.GetCurrentDirectory();
        string folder = Path.Combine(root, "tests/Jalium.UI.MacOS.Tests/Assets/FontSizeAdjust");
        byte[] primary = File.ReadAllBytes(Path.Combine(folder, "Primary.ttf")), secondary = File.ReadAllBytes(Path.Combine(folder, "Secondary.ttf"));
        using var first = CssNativeFontResource.Create(primary)!; using var second = CssNativeFontResource.Create(secondary)!;
        const string large = "size-adjust:150%;ascent-override:100%;descent-override:50%;line-gap-override:20%;";
        const string other = "size-adjust:200%;ascent-override:50%;descent-override:25%;line-gap-override:10%;";
        FaceMetricCase[] rows = [
            new("scaled", "size-adjust:150%", "size-adjust:200%", 48, 64, 36, 12, 0, 24, 8, 0),
            new("override", large, other, 48, 64, 48, 24, 9.6, 32, 16, 6.4),
            new("fixed", large, other, 48, 64, 48, 24, 9.6, 32, 16, 6.4, LineHeight:48),
            new("property", large, other, 48, 96, 48, 24, 9.6, 48, 24, 9.6, ".75"),
            new("compact", "size-adjust:150%;ascent-override:20%;descent-override:10%;line-gap-override:0%", "size-adjust:200%;ascent-override:10%;descent-override:5%;line-gap-override:0%", 48, 64, 9.6, 4.8, 0, 6.4, 3.2, 0),
            new("zero-metrics", "ascent-override:0%;descent-override:0%;line-gap-override:0%", "ascent-override:0%;descent-override:0%;line-gap-override:0%", 32, 32, 0, 0, 0, 0, 0, 0),
            new("zero-size", "size-adjust:0%", "size-adjust:0%", 0, 0, 0, 0, 0, 0, 0, 0),
            new("mixed", "ascent-override:50%;descent-override:25%;line-gap-override:0%", "ascent-override:150%;descent-override:50%;line-gap-override:0%", 32, 32, 16, 8, 0, 48, 16, 0),
        ];
        var records = new List<object>(); int failed = 0;
        try
        {
            foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello }) foreach (int dpi in new[] { 1, 2 })
            foreach (int width in new[] { 40, 160, 400 }) foreach (var row in rows) foreach (string path in new[] { "text", "run", "span", "shared" })
            {
                context.DefaultRenderingEngine = engine; string label = $"face-metrics-{engine}-{dpi}-{width}-{row.Name}-{path}";
                string? filter=Environment.GetEnvironmentVariable("JALIUM_CSS_FONT_CASE_FILTER");
                if (!string.IsNullOrEmpty(filter) && !label.Contains(filter, StringComparison.Ordinal)) continue;
                void Trace(string phase) { if (!string.IsNullOrEmpty(filter)) Console.WriteLine("TRACE " + label + " " + phase); }
                Trace("begin"); var errors = new List<string>(); int differentBytes = -1;
                try
                {
                    FrameworkElement actual; var sources = new List<TextBlock>(); var runs = new List<(Run Run, DependencyObject? Parent)>();
                    if (path == "shared") { var flow = new StackPanel(); foreach (string text in new[] { "x", "A" }) { var child = new TextBlock { Text = text, IsTextSelectionEnabled = true }; sources.Add(child); flow.Children.Add(child); } actual = flow; }
                    else { var block = new TextBlock { IsTextSelectionEnabled = true }; sources.Add(block); actual = block; if (path == "text") block.Text = "xA"; else { var x = new Run("x"); var a = new Run("A"); block.Inlines.Add(x); if (path == "span") block.Inlines.Add(new Span(a)); else block.Inlines.Add(a); runs.Add((x, x.Parent)); runs.Add((a, a.Parent)); } }
                    string sheet = (path == "shared" ? "TextBlock{display:inline}" : "") + "@font-face{font-family:MetricPrimary;src:url(\"data:font/ttf;base64," + Convert.ToBase64String(primary) + "\");" + row.First + "}@font-face{font-family:MetricSecondary;src:url(\"data:font/ttf;base64," + Convert.ToBase64String(secondary) + "\");" + row.Second + "}";
                    Css.SetStyleSheet(actual, sheet);
                    Css.SetStyle(actual, "display:block;font-family:MetricPrimary,MetricSecondary;font-size:32px;line-height:" + (row.LineHeight is { } h ? h.ToString(CultureInfo.InvariantCulture) + "px" : "normal") + ";color:#146342;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-size-adjust:" + row.Adjustment);
                    byte[] Capture(FrameworkElement view, int w)
                    {
                        view.SetRootDpi(new(dpi, dpi)); for (int i = 0; i < 3; ++i) { view.Measure(new(w, 192)); view.Arrange(new(0, 0, w, 192)); view.UpdateLayout(); }
                        var image = MacOSWritingToolsBitmapCapture.Capture(w*dpi, 384*dpi, dpi, dc => { dc.PushTransform(new TranslateTransform(0, 96)); view.Render(dc); dc.Pop(); }, () => true) ?? throw new Exception("Metric readback failed");
                        if (context.Backend != RenderBackend.Metal || context.DefaultRenderingEngine != engine || !ReferenceEquals(RenderContext.Current, context)) throw new Exception("Metric backend changed"); return Pixels(image);
                    }
                    Trace("initial-layout"); Capture(actual, width); Trace("load-fonts"); Css.WaitForFontsAsync(actual).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); CssFontFaces.FlushNotifications(actual.Dispatcher);
                    Trace("reference"); using var reference = new FaceMetricReference(first.Family, second.Family, row, width, path == "shared");
                    Trace("actual-pixels"); var changed = Capture(actual, width); Trace("reference-pixels"); var expected = Capture(reference, width); differentBytes = changed.Zip(expected).Count(pair => pair.First != pair.Second);
                    Save(changed, width*dpi, 384*dpi, label + "-changed.png"); Save(expected, width*dpi, 384*dpi, label + "-expected.png");
                    if (differentBytes != 0) throw new Exception($"Metric pixels differ from literal per-face sizes and line metrics ({differentBytes} bytes)");
                    Trace("selection"); foreach (var text in sources) { text.SelectAll(); if (text.SelectedText != (path == "shared" ? (ReferenceEquals(text, sources[0]) ? "x" : "A") : "xA")) throw new Exception("Source selection changed"); text.Select(0, 0); }
                    foreach (var (run, parent) in runs) if (!ReferenceEquals(run.Parent, parent)) throw new Exception("Run ownership changed");
                    Trace("resize"); Capture(actual, 500); if (!Capture(actual, width).SequenceEqual(changed)) throw new Exception("Resize retained stale metrics");
                    void AwaitFonts()
                    {
                        Css.WaitForFontsAsync(actual).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                        CssFontFaces.FlushNotifications(actual.Dispatcher);
                    }
                    Trace("sheet-reset");
                    Css.SetStyleSheet(actual, sheet.Replace("size-adjust:150%", "size-adjust:50%"));
                    Capture(actual, width); AwaitFonts(); Capture(actual, width);
                    Css.SetStyleSheet(actual, sheet);
                    Capture(actual, width); AwaitFonts();
                    if (!Capture(actual, width).SequenceEqual(changed)) throw new Exception("Sheet reset retained stale metrics");
                }
                catch (Exception error) { errors.Add(error.ToString()); ++failed; }
                records.Add(new { label, backend = context.Backend.ToString(), engine = engine.ToString(), differentBytes, errors }); Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            }
            File.WriteAllText(Path.Combine(Output, "font-face-metrics.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records, nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false })); return failed == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
