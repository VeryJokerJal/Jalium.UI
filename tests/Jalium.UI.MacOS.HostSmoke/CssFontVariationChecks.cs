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
    private sealed record VariationCase(string Name, string Css, FontVariation[] Axes);
    private sealed class VariationReference : FrameworkElement, IDisposable
    {
        private readonly List<(NativeTextParagraph Text, double Y, int Split)> _lines = [];
        private readonly bool _fragmented;
        internal VariationReference(string family, FontVariation[] axes, int width, bool fragmented)
        {
            Width = width; Height = 280; _fragmented = fragmented;
            string key = FontFeatureRenderingSource.Wrap(family, [], CssCapsVariant.Normal, new(Variations: axes, OpticalSizing: 1));
            var metrics = TextMeasurement.GetFontMetrics(key, 32, 400, 0);
            double baseline = metrics.Ascent + (44 - metrics.Ascent - metrics.Descent) / 2;
            using var format = TextMeasurement.CreateTextFormatFromFamilyList(RenderContext.Current!, key, 32, 400, 0);
            const string source = "ABMABM"; int start = 0;
            while (start < source.Length)
            {
                int count = 1;
                while (start + count < source.Length && format.MeasureText(source.Substring(start, count + 1), 10000, 1000).Width <= width + .0001) count++;
                string text = source.Substring(start, count);
                var units = Enumerable.Range(0, count).Select(i => new NativeMethods.ParagraphAdvanceUnit { TextPosition = (uint)i, Length = 1 }).ToArray();
                var paragraph = NativeTextParagraph.TryCreateAdvanced([new(text, key, 32, 400, 0, Colors.Black)], family, 32, 10000, 44, FlowDirection.LeftToRight, units, contextPaint: true) ?? throw new Exception("Variation reference unavailable");
                _lines.Add((paragraph, _lines.Count * 44 + baseline, Math.Clamp(3 - start, 0, count))); start += count;
            }
        }
        protected override Size MeasureOverride(Size available) => new(Width, Height);
        protected override void OnRender(DrawingContext context)
        {
            foreach (var (paragraph, y, split) in _lines)
            {
                void Paint(int start, int count)
                {
                    var line = paragraph.Lines[0].ForSource(start, count, Color.FromRgb(0x14, 0x63, 0x42), 1, [], 0);
                    context.DrawText(new FormattedText(paragraph.Text.Substring(start, count), "Arial", 32) { PlatformTextLine = line, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), MaxTextWidth = 10000, MaxTextHeight = 280 }, new(0, y - line.Metrics.Baseline));
                }
                if (_fragmented && split > 0 && split < paragraph.Text.Length) { Paint(0, split); Paint(split, paragraph.Text.Length - split); }
                else Paint(0, paragraph.Text.Length);
            }
        }
        public void Dispose() { foreach (var line in _lines) line.Text.Dispose(); }
    }
    internal static int RunFontVariations()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication")); Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string root = Environment.GetEnvironmentVariable("JALIUM_REPO_ROOT") ?? Directory.GetCurrentDirectory();
        using var font = CssNativeFontResource.Create(File.ReadAllBytes(Path.Combine(root, "tests/Jalium.UI.MacOS.Tests/Assets/FontVariations/variabletest_box.ttf"))) ?? throw new Exception("Variable font unavailable");
        var rows = new[] {
            new VariationCase("normal", "normal", []), new VariationCase("fraction", "'UPWD' 75.5", [new(0x55505744,75.5)]),
            new VariationCase("maximum", "'UPWD' 350", [new(0x55505744,350)]), new VariationCase("negative", "'UPWD' -100", [new(0x55505744,0)]),
            new VariationCase("clamped", "'UPWD' 600", [new(0x55505744,350)]), new VariationCase("duplicate", "'UPWD' 50,'UPWD' 175", [new(0x55505744,175)]),
            new VariationCase("optical", "'opsz' 128", [new(0x6f70737a,128)]),
            new VariationCase("combined", "'UPWD' 150,'FVTT' 10,'VM2B' 700,'opsz' 128", [new(0x55505744,150),new(0x46565454,10),new(0x564d3242,700),new(0x6f70737a,128)]) };
        var records = new List<object>(); int failed = 0;
        try
        {
            foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello }) foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 24, 80, 240 }) foreach (var row in rows) foreach (string path in new[] { "text", "run", "span", "shared" })
            {
                context.DefaultRenderingEngine = engine; string label = $"variation-{engine}-{dpi}-{width}-{row.Name}-{path}"; var errors = new List<string>(); int differentBytes = -1;
                try
                {
                    var runs = new List<(Run Run, DependencyObject? Parent)>(); var texts = new List<TextBlock>(); FrameworkElement actual;
                    if (path == "shared") { var flow = new StackPanel(); foreach (string text in new[] { "ABM", "ABM" }) { var child = new TextBlock { Text = text, IsTextSelectionEnabled = true }; texts.Add(child); flow.Children.Add(child); } Css.SetStyleSheet(flow, "TextBlock{display:inline}"); actual = flow; }
                    else { var block = new TextBlock { IsTextSelectionEnabled = true }; texts.Add(block); actual = block; if (path == "text") block.Text = "ABMABM"; else { var a = new Run("ABM"); var b = new Run("ABM"); block.Inlines.Add(a); if (path == "span") block.Inlines.Add(new Span(b)); else block.Inlines.Add(b); runs.Add((a, a.Parent)); runs.Add((b, b.Parent)); } }
                    Css.SetStyle(actual, $"display:block;font-family:'{font.Family}';font-size:32px;line-height:44px;color:#146342;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-variation-settings:{row.Css};");
                    byte[] Capture(FrameworkElement view, int size)
                    {
                        view.SetRootDpi(new(dpi, dpi)); for (int i = 0; i < 3; i++) { view.Measure(new(size, 280)); view.Arrange(new(0, 0, size, 280)); view.UpdateLayout(); }
                        var bitmap = MacOSWritingToolsBitmapCapture.Capture(size * dpi, 280 * dpi, dpi, dc => view.Render(dc), () => true) ?? throw new Exception("Variable readback failed");
                        if (context.Backend != RenderBackend.Metal || context.DefaultRenderingEngine != engine || !ReferenceEquals(RenderContext.Current, context)) throw new Exception("Variable backend changed"); return Pixels(bitmap);
                    }
                    using var reference = new VariationReference(font.Family, row.Axes, width, path == "shared");
                    var changed = Capture(actual, width); var expected = Capture(reference, width); differentBytes = changed.Zip(expected).Count(pair => pair.First != pair.Second);
                    Save(changed, width * dpi, 280 * dpi, label + "-changed.png"); Save(expected, width * dpi, 280 * dpi, label + "-expected.png");
                    if (differentBytes != 0) throw new Exception($"Variable CSS differs from raw-coordinate reference ({differentBytes} bytes)");
                    foreach (var text in texts) { text.SelectAll(); if (text.SelectedText != (path == "shared" ? "ABM" : "ABMABM")) throw new Exception("Variable source selection changed"); text.Select(0, 0); }
                    foreach (var (run, parent) in runs) if (run.Text != "ABM" || !ReferenceEquals(run.Parent, parent)) throw new Exception("Variable run ownership changed");
                    Capture(actual, 400); if (!Capture(actual, width).SequenceEqual(changed)) throw new Exception("Variable resize retained stale metrics");
                    string css = Css.GetStyle(actual); Css.SetStyle(actual, css + "font-variation-settings:normal;"); Capture(actual, width); Css.SetStyle(actual, css); if (!Capture(actual, width).SequenceEqual(changed)) throw new Exception("Variable reset retained old coordinates");
                }
                catch (Exception error) { errors.Add(error.ToString()); failed++; }
                records.Add(new { label, backend = context.Backend.ToString(), engine = engine.ToString(), differentBytes, errors }); Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            }
            File.WriteAllText(Path.Combine(Output, "font-variations.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records, nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false })); return failed == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
