using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    internal static int RunContextualShaping()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 76, 320 })
        foreach (string kind in new[] { "plain", "runs", "runs-color", "split", "nested", "tracking", "replace", "replace-split",
            "french", "french-split", "rtl", "bidi", "controlled", "controlled-split", "wrapped", "columns", "reader", "editor", "writing-tools" })
        {
            context.DefaultRenderingEngine = engine; string label = $"auto-{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            bool oracle = kind is "plain" or "runs" or "split" or "tracking" or "replace" or "replace-split" or "french" or "french-split" or "rtl" or "bidi" or "controlled" or "controlled-split";
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested renderer unavailable");
                byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => view.Render(dc), () => true) ?? throw new Exception("Autospace readback failed"));
                using var scene = new AutoScene(kind, width, dpi, false); scene.Layout(); var normal = Capture(scene.View);
                scene.Change(true); scene.Layout(); var changed = Capture(scene.View); var metrics = NumericMetrics(scene.View);
                using var fresh = new AutoScene(kind, width, dpi, true); fresh.Layout(); var expected = Capture(fresh.View);
                Check(changed.SequenceEqual(expected), "Autospace differs from a fresh view");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(NumericMetrics(fresh.View)), "Autospace retained stale geometry");
                Check(changed.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0), "Autospace glyph ink disappeared");
                if (oracle)
                {
                    foreach (bool spaced in new[] { false, true })
                    {
                        using var reference = new AutoOracle(kind, spaced); reference.SetRootDpi(new(dpi, dpi));
                        reference.Measure(new(width, 240)); reference.Arrange(new(0, 0, width, 240)); var pixels = Capture(reference);
                        Check((spaced ? changed : normal).SequenceEqual(pixels), "Autospace differs from independently translated whole CoreText glyphs (" + spaced + ")");
                        Save(pixels, width * dpi, 240 * dpi, label + (spaced ? "-oracle.png" : "-oracle-normal.png"));
                    }
                }
                if (kind == "writing-tools")
                {
                    var editor = (RichTextBox)scene.View;
                    var source = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                    int start = source.Text.IndexOf("ffi中", StringComparison.Ordinal); Check(start >= 0, "Autospace lost Writing Tools source text");
                    try
                    {
                        MacOSWritingToolsPresentation.Prepare(editor, source, "auto-line", start, 4, 0, () => true);
                        var hidden = Capture(editor); Check(hidden.Where((_, index) => index % 4 == 3).All(alpha => alpha == 0), "Hidden autospace line left glyph ink");
                        var preview = MacOSWritingToolsPresentation.CreatePreview(editor, source, start, 4, Matrix.Identity, dpi, null, () => true);
                        Check(preview is not null && Canvas(preview, width * dpi, 240 * dpi, dpi).SequenceEqual(changed), "Autospace preview changed positioned glyphs");
                        Save(hidden, width * dpi, 240 * dpi, label + "-hidden.png");
                    }
                    finally { MacOSWritingToolsPresentation.Clear(editor); }
                    Check(Capture(editor).SequenceEqual(changed), "Autospace glyph mask leaked after clear");
                }
                scene.AssertSources(); scene.Change(false); scene.Layout(); var restored = Capture(scene.View);
                Check(normal.SequenceEqual(restored), "Autospace reset retained stale glyphs"); scene.AssertSources();
                foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                    Save(item.Item2, width * dpi, 240 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, wholeGlyphOracle = oracle, metrics });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count != 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool value, string message) { if (!value) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "contextual-shaping.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private static string[] AutoParts(string kind) => kind switch
    {
        "replace" or "replace-split" => ["中", " ", "AVffi"],
        "french" or "french-split" => ["«A", "Vffi", "!»"],
        "rtl" or "bidi" => ["אב", "中AV", "文"],
        "controlled" or "controlled-split" => ["中", "\u202eA \u202c", "B"],
        "writing-tools" => ["", "f", "fi中"],
        "wrapped" or "columns" => ["中A", "Vffi文中AVffi文", "中AVffi文"],
        _ => ["中A", "Vf", "fi文"],
    };

    private sealed class AutoScene : IDisposable
    {
        internal readonly FrameworkElement View;
        private readonly string _kind, _body; private readonly int _width;
        private readonly DependencyObject _style; private readonly FlowDocumentPresentation? _reader;
        private readonly TextBlock[] _text = []; private readonly Run[] _runs = [];
        private readonly object?[] _parents;
        internal AutoScene(string kind, int width, int dpi, bool changed)
        {
            _kind = kind; _width = width; var parts = AutoParts(kind);
            _body = $"display:block;width:{width}px;font-family:Seravek;font-size:32px;line-height:44px;color:#146342;white-space:pre;font-kerning:normal;font-feature-settings:'liga' 1;word-spacing:{(kind.StartsWith("replace") ? 3 : 0)}px;letter-spacing:{(kind == "tracking" ? 2 : 0)}px;";
            if (kind is "wrapped" or "columns") _body = _body.Replace("white-space:pre", "white-space:normal");
            if (kind == "columns") _body += "columns:2;column-gap:16px;height:132px;column-fill:auto;";
            if (kind == "rtl") _body += "direction:rtl;text-align:left;";
            if (kind is "reader" or "editor" or "writing-tools")
            {
                _runs = parts.Select(value => new Run(value)).ToArray(); var paragraph = new Paragraph();
                foreach (var run in _runs) paragraph.Inlines.Add(run);
                var document = new FlowDocument(paragraph) { PagePadding = new(0) }; _style = document;
                Css.SetStyleSheet(document, "Paragraph,Section{display:block;margin:0;padding:0;border:0}Run{display:inline}");
                if (kind == "reader") { _reader = new(); _reader.SetDocument(document); View = _reader; }
                else View = new RichTextBox(document) { Template = null, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent, IsReadOnly = true };
            }
            else if (kind is "plain" or "runs" or "runs-color" or "tracking" or "replace" or "french" or "rtl" or "controlled")
            {
                var text = new TextBlock { IsTextSelectionEnabled = true }; View = text; _style = text; _text = [text];
                if (kind is not ("runs" or "runs-color")) text.Text = string.Concat(parts);
                else
                {
                    _runs = parts.Select(value => new Run(value)).ToArray(); foreach (var run in _runs) text.Inlines.Add(run);
                    if (kind == "runs-color") { _runs[0].Foreground = Brushes.Red; _runs[1].Foreground = Brushes.Red; _runs[2].Foreground = Brushes.Blue; }
                }
            }
            else
            {
                var root = new StackPanel(); View = root; _style = root;
                _text = parts.Select(value => new TextBlock { Text = value, IsTextSelectionEnabled = true }).ToArray();
                if (kind == "nested")
                { var frame = new StackPanel(); frame.Children.Add(_text[0]); frame.Children.Add(_text[1]); root.Children.Add(frame); Css.SetStyle(frame, "display:inline;background:#e9f6ee;"); root.Children.Add(_text[2]); }
                else foreach (var text in _text) root.Children.Add(text);
                Css.SetStyleSheet(root, "TextBlock{display:inline}");
            }
            if (kind.StartsWith("french")) View.Language = XmlLanguage.GetLanguage("fr-FR");
            _parents = _text.Select(text => (object?)text.Parent).Concat(_runs.Select(run => (object?)run.Parent)).ToArray();
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }
        internal void Change(bool changed) => Css.SetStyle(_style, _body + "text-autospace:" +
            (!changed ? "no-autospace" : _kind.StartsWith("french") ? "punctuation" : _kind.StartsWith("replace") || _kind.StartsWith("controlled") ? "ideograph-alpha replace" : "normal") + ";");
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(View.Dispatcher); _reader?.SetViewportHeight(240);
              View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout(); }
        }
        internal void AssertSources()
        {
            string content = string.Concat(AutoParts(_kind));
            if (_runs.Length > 0 && string.Concat(_runs.Select(run => run.Text)) != content) throw new Exception("Autospace changed original runs");
            if (_runs.Length == 0 && string.Concat(_text.Select(text => text.Text)) != content) throw new Exception("Autospace changed original text");
            if (!_parents.SequenceEqual(_text.Select(text => (object?)text.Parent).Concat(_runs.Select(run => (object?)run.Parent)))) throw new Exception("Autospace changed source ownership");
            foreach (var text in _text)
            {
                text.SelectAll(); if (text.SelectedText != (_kind is "runs" or "runs-color" ? content : text.Text)) throw new Exception("Autospace selection changed source text");
                text.Select(0, 0);
                foreach (var segment in text.GetSourceRangeSegments(0, text.Text.Length))
                    if (segment.EndX > segment.StartX && text.GetPositionFromPoint(new((segment.StartX + segment.EndX) / 2, segment.Baseline - 2), false) is null)
                        throw new Exception("Autospace source midpoint lost its caret");
            }
        }
        public void Dispose() { _reader?.Dispose(); Css.SetStyleSheets(_style, null); }
    }

    // Shape the whole text with the old API, then translate its original glyph
    // masks. This oracle does not call the advanced factory or flow formatter.
    private sealed class AutoOracle : FrameworkElement, IDisposable
    {
        private readonly NativeTextParagraph _paragraph;
        private readonly List<(int Start, int Length, double Shift)> _pieces = [];
        private readonly double _baseline;
        internal AutoOracle(string kind, bool changed)
        {
            string text = string.Concat(AutoParts(kind)); bool french = kind.StartsWith("french"), replace = kind.StartsWith("replace") || kind.StartsWith("controlled");
            double tracking = kind == "tracking" ? 2 : 0, word = kind.StartsWith("replace") ? 3 : 0;
            string family = FontFeatureRenderingSource.Wrap("Seravek", [new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), 1)]);
            var metrics = TextMeasurement.GetFontMetrics(family, 32, 400, 0); _baseline = metrics.Ascent + (44 - metrics.Ascent - metrics.Descent) / 2;
            double ic = TextMeasurement.GetFontUnitMetrics(family, 32, 400, 0).IdeographicAdvance;
            _paragraph = NativeTextParagraph.TryCreate([new(text, family, 32, 400, 0, Color.FromRgb(20, 99, 66))], family, 32, 100000, 44,
                TextAlignment.Left, kind == "rtl" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, noWrap: true) ?? throw new Exception("Autospace reference unavailable");
            var cells = Enumerable.Range(0, text.Length).Where(i => char.GetUnicodeCategory(text[i]) != System.Globalization.UnicodeCategory.Format).Select(i => (Start: i, Left: Math.Min(_paragraph.Caret(0, i, false).X, _paragraph.Caret(0, i + 1, true).X),
                Width: Math.Abs(_paragraph.Caret(0, i + 1, true).X - _paragraph.Caret(0, i, false).X), Rtl: _paragraph.Caret(0, i + 1, true).X < _paragraph.Caret(0, i, false).X)).OrderBy(cell => cell.Left).ToArray();
            double Space(char value)
            {
                using var font = RenderContext.Current!.CreateTextFormat("Seravek", 32, 400, 0);
                return font.MeasureText(value.ToString(), 100000, 1000).WidthIncludingTrailingWhitespace;
            }
            double Auto(char left, char right)
            {
                if (!changed) return 0;
                if (french) return right is '!' or '?' or ';' ? Space('\u202f') : right is ':' or '»' || left == '«' ? Space('\u00a0') : 0;
                bool a = left is '中' or '文', b = right is '中' or '文';
                return a != b && char.IsLetterOrDigit(a ? right : left) ? ic / 8 : 0;
            }
            char Visual(int i) => cells[i].Rtl ? text[cells[i].Start] switch { '«' => '»', '»' => '«', var c => c } : text[cells[i].Start];
            var gaps = Enumerable.Range(0, Math.Max(0, cells.Length - 1)).Select(i => Auto(Visual(i), Visual(i + 1))).ToArray();
            var shifts = new double[text.Length]; double extra = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i]; double own = text[cell.Start] == ' ' ? word : 0;
                if (replace && changed && text[cell.Start] == ' ' && i > 0 && i + 1 < cells.Length) own += Auto(Visual(i - 1), Visual(i + 1)) - cell.Width;
                double leading = i == 0 ? 0 : gaps[i - 1] / 2, trailing = i == gaps.Length ? 0 : gaps[i] / 2;
                shifts[cell.Start] = extra + own / 2 + leading + i * tracking; extra += own + leading + trailing;
            }
            for (int start = 0; start < text.Length;)
            {
                if (text[start] == ' ') { start++; continue; }
                int length = text.AsSpan(start).StartsWith("ffi") ? 3 : 1;
                _pieces.Add((start, length, shifts.Skip(start).Take(length).Average())); start += length;
            }
        }
        protected override void OnRender(DrawingContext context)
        {
            foreach (var piece in _pieces)
            {
                var text = new FormattedText(_paragraph.Text, "Seravek", 32) { PlatformTextLine = _paragraph.Lines[0], Foreground = Brushes.Black,
                    MaxTextWidth = 100000, MaxTextHeight = 240, GlyphVisibility = 1, GlyphRanges = [(uint)piece.Start, (uint)piece.Length] };
                context.DrawText(text, new(piece.Shift - _paragraph.Lines[0].Metrics.Line.X, _baseline - _paragraph.Lines[0].Metrics.Baseline));
            }
        }
        public void Dispose() => _paragraph.Dispose();
    }
}
