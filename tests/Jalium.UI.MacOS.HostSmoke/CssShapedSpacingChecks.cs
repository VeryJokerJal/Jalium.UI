using System.Text.Json;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private static (string Text, double Spacing, bool Forced, bool Cursive) SpacingCase(string kind) => kind switch
    {
        "negative" => ("AV", -1, false, false), "combining" => ("a\u0301b", 3, false, false),
        "emoji" => ("👩‍💻🙂", 3, false, false), "forced" => ("ffi", 2, true, false),
        "ligatures" => ("ffi", 2, false, false), "cursive" => ("مرحبا", 3, false, true),
        "required" => ("لا", 3, false, true), "bidi" => ("אבAB", 3, false, false),
        "wrapped" => ("AV AV AV AV AV AV AV AV", 2, false, false),
        "formatting" => ("A\u200BV", 2, false, false), _ => ("AV", 2, false, false),
    };

    internal static int RunShapedSpacing()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string[] kinds = ["pair", "negative", "combining", "emoji", "forced", "ligatures", "cursive", "required", "bidi", "formatting",
            "wrapped", "inline-run", "inline-span", "reader-root", "reader-run", "reader-span", "editor-root", "editor-span", "marker", "reader-marker", "editor-marker"];
        var records = new List<object>(); int failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 170, 360 }) foreach (string kind in kinds)
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_SPACING_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; string label = $"{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested spacing engine unavailable");
                using var actual = new SpacingScene(kind, width, dpi, false); actual.Layout();
                byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => view.Render(dc), () => true) ?? throw new Exception("Spacing readback failed"));
                string source = actual.Source; var parent = actual.Run?.Parent; var normal = Capture(actual.View);
                if (kind == "bidi")
                {
                    using var unspacedReference = new SpacingOracle(kind, normal: true); unspacedReference.SetRootDpi(new(dpi, dpi));
                    unspacedReference.Measure(new(width, 240)); unspacedReference.Arrange(new(0, 0, width, 240));
                    Check(normal.SequenceEqual(Capture(unspacedReference)), "Zero tracking ignored the CSS paragraph base direction");
                }
                actual.Change(true); actual.Layout(); var changed = Capture(actual.View); var metrics = NumericMetrics(actual.View);
                using var fresh = new SpacingScene(kind, width, dpi, true); fresh.Layout(); var expected = Capture(fresh.View);
                var expectedMetrics = NumericMetrics(fresh.View);
                Check(changed.SequenceEqual(expected), "Spacing drawing differs from a fresh view");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(expectedMetrics), "Spacing geometry differs from a fresh view");
                bool control = kind is "cursive" or "required";
                Check(normal.SequenceEqual(changed) == control, control ? "Tracking broke a cursive word" : "Tracking did not change visible glyph positions");
                byte[]? oracle = null;
                if (!kind.Contains('-') && kind is not ("marker" or "wrapped"))
                {
                    using var reference = new SpacingOracle(kind); reference.SetRootDpi(new(dpi, dpi));
                    reference.Measure(new(width, 240)); reference.Arrange(new(0, 0, width, 240)); oracle = Capture(reference);
                    Check(changed.SequenceEqual(oracle), "Spacing glyphs differ from independently translated, unspaced CoreText glyph ranges");
                    Save(oracle, width * dpi, 240 * dpi, label + "-oracle.png");
                }
                actual.Change(false); actual.Layout(); var restored = Capture(actual.View);
                Check(normal.SequenceEqual(restored), "Spacing reset did not restore pixels");
                Check(actual.Source == source && ReferenceEquals(parent, actual.Run?.Parent), "Spacing changed source text or ownership");
                foreach (var image in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                    Save(image.Item2, width * dpi, 240 * dpi, label + "-" + image.Item1 + ".png");
                records.Add(new { label, errors, control, rawGlyphOracle = oracle is not null, metrics, expectedMetrics });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count != 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool value, string message) { if (!value) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "spacing.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private sealed class SpacingScene : IDisposable
    {
        internal readonly FrameworkElement View;
        internal readonly Run? Run;
        private readonly TextBlock? _text;
        private readonly FlowDocument? _document;
        private readonly FlowDocumentPresentation? _reader;
        private readonly DependencyObject _target;
        private readonly string _kind, _body;
        private readonly int _width;
        internal string Source => _document?.GetText() ?? Run?.Text ?? _text?.Text ?? "";
        internal SpacingScene(string kind, int width, int dpi, bool changed)
        {
            _kind = kind; _width = width; var sample = SpacingCase(kind);
            _body = "font-family:Seravek;font-size:32px;line-height:44px;color:#146342;white-space:nowrap;font-kerning:normal;text-autospace:no-autospace;";
            if (kind == "wrapped") _body = _body.Replace("white-space:nowrap", "white-space:normal");
            if (kind.StartsWith("reader-", StringComparison.Ordinal) || kind.StartsWith("editor-", StringComparison.Ordinal))
            {
                Run = new(sample.Text); var span = new Span(Run); var paragraph = new Paragraph(span); _document = new(paragraph) { PagePadding = new(0) };
                Css.SetStyle(_document, _body); Css.SetStyleSheet(_document, "Paragraph,Section,List,ListItem{margin:0;padding:0;border:0}");
                _target = kind.EndsWith("run", StringComparison.Ordinal) ? Run : kind.EndsWith("span", StringComparison.Ordinal) ? span : _document;
                if (kind.EndsWith("marker", StringComparison.Ordinal))
                { Run.Text = "正文"; _document.Blocks.Remove(paragraph); _document.Blocks.Add(new Jalium.UI.Documents.List(new ListItem(paragraph))); }
                if (kind.StartsWith("editor-", StringComparison.Ordinal)) View = new RichTextBox(_document) { Template = null, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent };
                else { _reader = new(); _reader.SetDocument(_document); View = _reader; }
            }
            else
            {
                _text = new() { Text = sample.Text }; View = _text; _target = _text; Css.SetStyle(_text, _body);
                if (kind is "inline-run" or "inline-span")
                { _text.Text = ""; Run = new(sample.Text); var span = new Span(Run); _text.Inlines.Add(span); _target = kind == "inline-run" ? Run : span; }
                if (sample.Cursive) _text.FlowDirection = FlowDirection.RightToLeft;
                if (kind == "marker") _text.Text = "正文";
            }
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }
        internal void Change(bool changed)
        {
            var sample = SpacingCase(_kind); string value = changed ? sample.Spacing.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px" : "0px";
            string declaration = "letter-spacing:" + value + ";" + (sample.Forced ? "font-feature-settings:'liga' 1;" : "");
            if (_kind is "marker" or "reader-marker" or "editor-marker")
            {
                string selector = _kind == "marker" ? "TextBlock" : "ListItem";
                Css.SetStyleSheet(_document ?? (DependencyObject)_text!, $"{selector}::marker{{content:'AV ';{declaration}}}Paragraph,Section,List,ListItem{{margin:0;padding:0;border:0}}");
                if (_kind == "marker") Css.SetStyle(_text!, _body + "display:list-item;list-style-position:inside");
            }
            else Css.SetStyle(_target, (ReferenceEquals(_target, _document) || ReferenceEquals(_target, _text) ? _body : "") + declaration);
        }
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(View.Dispatcher); _reader?.SetViewportHeight(240);
              View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout(); }
        }
        public void Dispose() { _reader?.Dispose(); Css.SetStyleSheets(_document ?? (DependencyObject)_text!, null); }
    }

    private sealed class SpacingOracle : FrameworkElement, IDisposable
    {
        private readonly NativeTextParagraph _natural;
        private readonly double _baseline;
        private readonly List<(int Start, int Length, double Shift)> _pieces = [];
        internal SpacingOracle(string kind, bool normal = false)
        {
            var test = SpacingCase(kind);
            if (normal) test.Spacing = 0;
            // CSS ignores formatting-only characters for tracking. Use the
            // visible reference string rather than requiring an insertion edge
            // that CoreText does not enumerate for U+200B.
            string referenceText = kind == "formatting" ? test.Text.Replace("\u200B", "") : test.Text;
            FontFeature[] features = [new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), test.Forced ? 1u : 0u),
                new(FontFeature.TagOf("clig"), 0), new(FontFeature.TagOf("dlig"), 0), new(FontFeature.TagOf("hlig"), 0)];
            string family = FontFeatureRenderingSource.Wrap("Seravek", features);
            var fontMetrics = TextMeasurement.GetFontMetrics(family, 32, 400, 0);
            _baseline = fontMetrics.Ascent + (44 - fontMetrics.Ascent - fontMetrics.Descent) / 2;
            _natural = NativeTextParagraph.TryCreate([new(referenceText, family, 32, 400, 0, Color.FromRgb(20, 99, 66))], family,
                32, 100000, 44, TextAlignment.Left, test.Cursive ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, noWrap: true)
                ?? throw new Exception("Independent unspaced reference failed");
            if (test.Cursive) { _pieces.Add((0, test.Text.Length, 0)); return; }
            if (test.Forced) { _pieces.Add((0, test.Text.Length, test.Spacing)); return; }
            var boundaries = GraphemeClusters.GetBoundaries(referenceText);
            var visual = new List<(int Start, int Length, double X)>();
            for (int i = 0; i + 1 < boundaries.Length; i++)
            {
                int start = boundaries[i], end = boundaries[i + 1];
                if (referenceText.AsSpan(start, end - start).ToString().EnumerateRunes().All(r => Rune.GetUnicodeCategory(r) == System.Globalization.UnicodeCategory.Format)) continue;
                visual.Add((start, end - start, Math.Min(_natural.Caret(0, start, false).X, _natural.Caret(0, end, true).X)));
            }
            visual.Sort((a, b) => a.X.CompareTo(b.X));
            for (int i = 0; i < visual.Count; i++) _pieces.Add((visual[i].Start, visual[i].Length, i * test.Spacing));
        }
        protected override void OnRender(DrawingContext context)
        {
            foreach (var piece in _pieces)
            {
                var text = new FormattedText(_natural.Text, "Seravek", 32) { PlatformTextLine = _natural.Lines[0],
                    Foreground = new SolidColorBrush(Color.FromRgb(20, 99, 66)), MaxTextWidth = 100000, MaxTextHeight = 240,
                    GlyphRanges = [(uint)piece.Start, (uint)piece.Length], GlyphVisibility = 1 };
                context.DrawText(text, new(piece.Shift - _natural.Lines[0].Metrics.Line.X,
                    _baseline - _natural.Lines[0].Metrics.Baseline));
            }
        }
        public void Dispose() => _natural.Dispose();
    }
}
