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
    private sealed record AdjustmentCase(string Name, string Value, double FirstSize, double SecondSize, bool Face = false);
    private sealed class AdjustmentReference : FrameworkElement, IDisposable
    {
        private readonly List<(NativeTextParagraph Paragraph, double Baseline, int Split)> _lines = [];
        private readonly bool _fragmented;
        internal AdjustmentReference(string first, string second, AdjustmentCase row, int width, bool fragmented)
        {
            Width = width; Height = 192; _fragmented = fragmented;
            if (row.FirstSize < 1e-10) return;
            var metrics = TextMeasurement.GetFontMetrics(first, row.FirstSize, 400, 0);
            double baseline = metrics.Ascent + (48 - metrics.Ascent - metrics.Descent) / 2;
            using var a = TextMeasurement.CreateTextFormatFromFamilyList(RenderContext.Current!, first, (float)row.FirstSize, 400, 0);
            using var b = TextMeasurement.CreateTextFormatFromFamilyList(RenderContext.Current!, second, (float)row.SecondSize, 400, 0);
            double advance = a.MeasureText("x", 10000, 1000).Width + b.MeasureText("A", 10000, 1000).Width;
            bool wrap = advance > width;
            foreach (string text in wrap ? new[] { "x", "A" } : new[] { "xA" })
            {
                var spans = text.Select(character => new NativeTextParagraph.Span(character.ToString(), character == 'x' ? first : second,
                    character == 'x' ? row.FirstSize : row.SecondSize, 400, 0, Colors.Black)).ToArray();
                var units = Enumerable.Range(0, text.Length).Select(i => new NativeMethods.ParagraphAdvanceUnit { TextPosition = (uint)i, Length = 1 }).ToArray();
                var paragraph = NativeTextParagraph.TryCreateAdvanced(spans, first, row.FirstSize, 10000, 48, FlowDirection.LeftToRight, units, contextPaint: true)
                    ?? throw new Exception("Size adjustment reference unavailable");
                _lines.Add((paragraph, _lines.Count * 48 + baseline, text == "xA" ? 1 : 0));
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
                    context.DrawText(new FormattedText(paragraph.Text.Substring(start, count), "Arial", 32)
                    {
                        PlatformTextLine = line, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), MaxTextWidth = 10000, MaxTextHeight = 192,
                    }, new(0, baseline - line.Metrics.Baseline));
                }
                if (_fragmented && split > 0) { Paint(0, split); Paint(split, paragraph.Text.Length - split); }
                else Paint(0, paragraph.Text.Length);
            }
        }
        public void Dispose() { foreach (var line in _lines) line.Paragraph.Dispose(); }
    }
    internal static int RunFontSizeAdjust()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string root = Environment.GetEnvironmentVariable("JALIUM_REPO_ROOT") ?? Directory.GetCurrentDirectory();
        string folder = Path.Combine(root, "tests/Jalium.UI.MacOS.Tests/Assets/FontSizeAdjust");
        byte[] primary = File.ReadAllBytes(Path.Combine(folder, "Primary.ttf")), secondary = File.ReadAllBytes(Path.Combine(folder, "Secondary.ttf"));
        using var first = CssNativeFontResource.Create(primary) ?? throw new Exception("Primary fixture unavailable");
        using var second = CssNativeFontResource.Create(secondary) ?? throw new Exception("Secondary fixture unavailable");
        var rows = new List<AdjustmentCase> { new("none", "none", 32, 32), new("zero", "0", 1e-20, 1e-20) };
        foreach (var (metric, ratio) in new[] { ("ex-height", .5), ("cap-height", .75), ("ch-width", .6), ("ic-width", 1d), ("ic-height", 1.2) })
        {
            rows.Add(new(metric, metric + " " + (ratio * 1.5).ToString("R", CultureInfo.InvariantCulture), 48, 96));
            rows.Add(new(metric + "-from-font", metric + " from-font", 32, 64));
        }
        rows.Add(new("loaded-face", "from-font", 32, 64, true));
        var records = new List<object>(); int failed = 0;
        try
        {
            foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello }) foreach (int dpi in new[] { 1, 2 })
            foreach (int width in new[] { 40, 160, 400 }) foreach (var row in rows) foreach (string path in new[] { "text", "run", "span", "shared" })
            {
                context.DefaultRenderingEngine = engine; string label = $"adjust-{engine}-{dpi}-{width}-{row.Name}-{path}";
                var errors = new List<string>(); int differentBytes = -1;
                try
                {
                    FrameworkElement actual; var sources = new List<TextBlock>(); var runs = new List<(Run Run, DependencyObject? Parent)>();
                    if (path == "shared")
                    {
                        var flow = new StackPanel(); foreach (string text in new[] { "x", "A" }) { var child = new TextBlock { Text = text, IsTextSelectionEnabled = true }; sources.Add(child); flow.Children.Add(child); }
                        Css.SetStyleSheet(flow, "TextBlock{display:inline}"); actual = flow;
                    }
                    else
                    {
                        var block = new TextBlock { IsTextSelectionEnabled = true }; sources.Add(block); actual = block;
                        if (path == "text") block.Text = "xA";
                        else { var x = new Run("x"); var a = new Run("A"); block.Inlines.Add(x); if (path == "span") block.Inlines.Add(new Span(a)); else block.Inlines.Add(a); runs.Add((x, x.Parent)); runs.Add((a, a.Parent)); }
                    }
                    string family = first.Family;
                    if (row.Face)
                    {
                        family = "AdjustmentFace";
                        Css.SetStyleSheet(actual, (path == "shared" ? "TextBlock{display:inline}" : "") + "@font-face{font-family:AdjustmentFace;src:url(\"data:font/ttf;base64," + Convert.ToBase64String(primary) + "\")}");
                    }
                    Css.SetStyle(actual, $"display:block;font-family:'{family}','{second.Family}';font-size:32px;line-height:1.5;color:#146342;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-size-adjust:{row.Value};");
                    byte[] Capture(FrameworkElement view, int w)
                    {
                        view.SetRootDpi(new(dpi, dpi)); for (int i = 0; i < 3; ++i) { view.Measure(new(w, 192)); view.Arrange(new(0, 0, w, 192)); view.UpdateLayout(); }
                        var image = MacOSWritingToolsBitmapCapture.Capture(w*dpi, 192*dpi, dpi, dc => view.Render(dc), () => true) ?? throw new Exception("Size adjustment readback failed");
                        if (context.Backend != RenderBackend.Metal || context.DefaultRenderingEngine != engine || !ReferenceEquals(RenderContext.Current, context)) throw new Exception("Size adjustment backend changed"); return Pixels(image);
                    }
                    if (row.Face) { Capture(actual, width); Css.WaitForFontsAsync(actual).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
                    using var reference = new AdjustmentReference(first.Family, second.Family, row, width, path == "shared");
                    var changed = Capture(actual, width); var expected = Capture(reference, width); differentBytes = changed.Zip(expected).Count(pair => pair.First != pair.Second);
                    Save(changed, width*dpi, 192*dpi, label + "-changed.png"); Save(expected, width*dpi, 192*dpi, label + "-expected.png");
                    if (differentBytes != 0) throw new Exception($"Size adjustment differs from independent per-face sizes ({differentBytes} bytes)");
                    foreach (var text in sources) { text.SelectAll(); if (text.SelectedText != (path == "shared" ? (ReferenceEquals(text, sources[0]) ? "x" : "A") : "xA")) throw new Exception("Source selection changed"); text.Select(0, 0); }
                    foreach (var (run, parent) in runs) if (!ReferenceEquals(run.Parent, parent)) throw new Exception("Run ownership changed");
                    Capture(actual, 500); if (!Capture(actual, width).SequenceEqual(changed)) throw new Exception("Resize retained stale sizing");
                    string style = Css.GetStyle(actual); Css.SetStyle(actual, style + "font-size-adjust:ch-width 1.2;"); Capture(actual, width);
                    Css.SetStyle(actual, style); if (!Capture(actual, width).SequenceEqual(changed)) throw new Exception("Reset retained stale sizing");
                }
                catch (Exception error) { errors.Add(error.ToString()); ++failed; }
                records.Add(new { label, backend = context.Backend.ToString(), engine = engine.ToString(), differentBytes, errors });
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            }
            File.WriteAllText(Path.Combine(Output, "font-size-adjust.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records, nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
            return failed == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
