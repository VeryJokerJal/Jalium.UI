using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    internal static int RunWordShaping()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 76, 320 })
        foreach (string kind in new[] { "plain", "runs", "runs-color", "split", "nested", "color", "tracking", "negative", "rtl", "bidi", "wrapped", "columns", "reader", "editor", "writing-tools" })
        {
            context.DefaultRenderingEngine = engine; string label = $"words-{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested renderer unavailable");
                byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => view.Render(dc), () => true) ?? throw new Exception("Word readback failed"));
                using var scene = new WordScene(kind, width, dpi, false); scene.Layout(); var normal = Capture(scene.View);
                scene.Change(true); scene.Layout(); var changed = Capture(scene.View); var metrics = NumericMetrics(scene.View);
                using var fresh = new WordScene(kind, width, dpi, true); fresh.Layout(); var expected = Capture(fresh.View);
                Check(changed.SequenceEqual(expected), "Word spacing differs from a fresh view");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(NumericMetrics(fresh.View)), "Word source geometry retained stale spacing");
                Check(changed.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0), "Word glyph ink disappeared");
                // These columns are narrower than a word. Separators collapse
                // at the wraps, so none of their word advances remain visible.
                Check(normal.SequenceEqual(changed) == (kind == "columns" && width == 76), "Word spacing changed the wrong glyph positions");
                bool oracle = kind is "plain" or "runs" or "runs-color" or "split" or "tracking" or "negative" or "rtl" or "bidi";
                if (oracle)
                {
                    using var reference = new WordOracle(kind); reference.SetRootDpi(new(dpi, dpi));
                    reference.Measure(new(width, 240)); reference.Arrange(new(0, 0, width, 240)); var pixels = Capture(reference);
                    Check(changed.SequenceEqual(pixels), "Word spacing differs from independently translated whole CoreText glyphs");
                    Save(pixels, width * dpi, 240 * dpi, label + "-oracle.png");
                    using var zero = new WordOracle(kind, changed: false); zero.SetRootDpi(new(dpi, dpi));
                    zero.Measure(new(width, 240)); zero.Arrange(new(0, 0, width, 240)); var zeroPixels = Capture(zero);
                    Check(normal.SequenceEqual(zeroPixels), "Zero word spacing differs from whole CoreText shaping");
                    Save(zeroPixels, width * dpi, 240 * dpi, label + "-oracle-normal.png");
                }
                if (kind == "writing-tools")
                {
                    var editor = (RichTextBox)scene.View;
                    var source = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                    int start = source.Text.IndexOf("ffi", StringComparison.Ordinal); Check(start >= 0, "Writing Tools lost source text");
                    try
                    {
                        MacOSWritingToolsPresentation.Prepare(editor, source, "word-cluster", start, 1, 0, () => true);
                        var hidden = Capture(editor); Check(hidden.Where((_, index) => index % 4 == 3).All(alpha => alpha == 0), "Spaced ffi left partial hidden ink");
                        var preview = MacOSWritingToolsPresentation.CreatePreview(editor, source, start, 1, Matrix.Identity, dpi, null, () => true);
                        Check(preview is not null && Canvas(preview, width * dpi, 240 * dpi, dpi).SequenceEqual(changed), "Spaced glyph preview lost or duplicated ink");
                        Save(hidden, width * dpi, 240 * dpi, label + "-hidden.png");
                    }
                    finally { MacOSWritingToolsPresentation.Clear(editor); }
                    Check(Capture(editor).SequenceEqual(changed), "Word glyph mask leaked after clear");
                }
                scene.AssertSources(); scene.Change(false); scene.Layout(); var restored = Capture(scene.View);
                Check(normal.SequenceEqual(restored), "Word spacing reset retained stale glyphs"); scene.AssertSources();
                foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                    Save(item.Item2, width * dpi, 240 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, wholeGlyphOracle = oracle, metrics });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count != 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool value, string message) { if (!value) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "word-shaping.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private static string[] WordParts(string kind) => kind switch
    {
        "rtl" => ["مر", "حبا ", "مرحبا"], "bidi" => ["אב ", "AV", " אב"],
        "writing-tools" => [" ", "f", "fi"],
        "wrapped" or "columns" => ["A", "V ffi AV ffi AV ", "ffi AV ffi AV ffi"],
        _ => ["A", "V f", "fi AV"],
    };

    private sealed class WordScene : IDisposable
    {
        internal readonly FrameworkElement View;
        private readonly string _kind, _body; private readonly int _width;
        private readonly DependencyObject _style; private readonly FlowDocumentPresentation? _reader;
        private readonly TextBlock[] _text = []; private readonly Run[] _runs = [];
        private readonly object?[] _parents;
        internal WordScene(string kind, int width, int dpi, bool changed)
        {
            _kind = kind; _width = width; var parts = WordParts(kind);
            _body = $"display:block;width:{width}px;font-family:Seravek;font-size:32px;line-height:44px;color:#146342;white-space:pre;font-kerning:normal;font-feature-settings:'liga' 1;text-autospace:no-autospace;";
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
            else if (kind is "plain" or "runs" or "runs-color")
            {
                var text = new TextBlock { IsTextSelectionEnabled = true }; View = text; _style = text; _text = [text];
                if (kind == "plain") text.Text = string.Concat(parts);
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
                Css.SetStyleSheet(root, "TextBlock{display:inline}"); if (kind == "color") Css.SetStyle(_text[1], "color:#b63342;text-decoration:underline");
            }
            _parents = _text.Select(text => (object?)text.Parent).Concat(_runs.Select(run => (object?)run.Parent)).ToArray();
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }
        internal void Change(bool changed) => Css.SetStyle(_style, _body + "letter-spacing:" + (_kind == "tracking" ? "2" : "0") + "px;word-spacing:" +
            (changed ? _kind == "negative" ? "-100" : "6" : "0") + "px;");
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(View.Dispatcher); _reader?.SetViewportHeight(240);
              View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout(); }
        }
        internal void AssertSources()
        {
            var parts = WordParts(_kind); string content = string.Concat(parts);
            if (_runs.Length > 0 && string.Concat(_runs.Select(run => run.Text)) != content) throw new Exception("Word spacing changed original runs");
            if (_runs.Length == 0 && string.Concat(_text.Select(text => text.Text)) != content) throw new Exception("Word spacing changed original text");
            if (!_parents.SequenceEqual(_text.Select(text => (object?)text.Parent).Concat(_runs.Select(run => (object?)run.Parent)))) throw new Exception("Word spacing changed source ownership");
            foreach (var text in _text)
            {
                text.SelectAll(); if (text.SelectedText != (_kind is "runs" or "runs-color" ? content : text.Text)) throw new Exception("Word selection changed source text");
                text.Select(0, 0);
                foreach (var segment in text.GetSourceRangeSegments(0, text.Text.Length))
                    if (segment.EndX > segment.StartX && text.GetPositionFromPoint(new((segment.StartX + segment.EndX) / 2, segment.Baseline - 2), false) is null)
                        throw new Exception("Word source midpoint lost its caret");
            }
        }
        public void Dispose() { _reader?.Dispose(); Css.SetStyleSheets(_style, null); }
    }

    // Independent oracle: the old paragraph API shapes the complete text. Word
    // glyph groups are translated using its original cells, without the new
    // advance factory or any shared-flow fragment geometry.
    private sealed class WordOracle : FrameworkElement, IDisposable
    {
        private readonly NativeTextParagraph _paragraph;
        private readonly NativeTextParagraph? _red, _blue;
        private readonly List<(int Start, int Length, double Shift)> _pieces = [];
        private readonly double _baseline;
        internal WordOracle(string kind, bool changed = true)
        {
            string text = string.Concat(WordParts(kind)); double tracking = kind == "tracking" ? 2 : 0, word = changed ? kind == "negative" ? -100 : 6 : 0;
            string family = FontFeatureRenderingSource.Wrap("Seravek", [new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), 1)]);
            var font = TextMeasurement.GetFontMetrics(family, 32, 400, 0); _baseline = font.Ascent + (44 - font.Ascent - font.Descent) / 2;
            _paragraph = NativeTextParagraph.TryCreate([new(text, family, 32, 400, 0, Color.FromRgb(20, 99, 66))], family, 32, 100000, 44,
                TextAlignment.Left, kind == "rtl" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, noWrap: true) ?? throw new Exception("Word reference unavailable");
            if (kind == "runs-color")
            {
                _red = NativeTextParagraph.TryCreate([new(text, family, 32, 400, 0, Colors.Red)], family, 32, 100000, 44, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
                _blue = NativeTextParagraph.TryCreate([new(text, family, 32, 400, 0, Colors.Blue)], family, 32, 100000, 44, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
                if (_red is null || _blue is null) throw new Exception("Colored whole-line reference unavailable");
            }
            var cells = Enumerable.Range(0, text.Length).Select(i => (Start: i, Left: Math.Min(_paragraph.Caret(0, i, false).X, _paragraph.Caret(0, i + 1, true).X),
                Width: Math.Abs(_paragraph.Caret(0, i + 1, true).X - _paragraph.Caret(0, i, false).X))).OrderBy(cell => cell.Left).ToArray();
            var shifts = new double[text.Length]; double extra = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i]; double own = text[cell.Start] == ' ' ? Math.Max(-cell.Width, word) : 0;
                shifts[cell.Start] = extra + own / 2 + i * tracking; extra += own;
            }
            for (int start = 0; start < text.Length;)
            {
                if (text[start] == ' ') { start++; continue; }
                int end = start + 1; while (end < text.Length && text[end] != ' ') end++;
                // Only ffi has an optional multi-character glyph in these
                // fixtures. Its retained glyph is centered in the tracked cell.
                if (text.Substring(start, end - start) == "ffi") _pieces.Add((start, end - start, shifts.Skip(start).Take(end - start).Average()));
                else if (tracking == 0) _pieces.Add((start, end - start, shifts[start]));
                else for (int i = start; i < end; i++) _pieces.Add((i, 1, shifts[i]));
                start = end;
            }
        }
        protected override void OnRender(DrawingContext context)
        {
            foreach (var piece in _pieces)
            {
                var paragraph = _red is null ? _paragraph : piece.Start < 3 ? _red : _blue!;
                var text = new FormattedText(paragraph.Text, "Seravek", 32) { PlatformTextLine = paragraph.Lines[0], Foreground = Brushes.Black,
                    MaxTextWidth = 100000, MaxTextHeight = 240, GlyphVisibility = 1, GlyphRanges = [(uint)piece.Start, (uint)piece.Length] };
                context.DrawText(text, new(piece.Shift - _paragraph.Lines[0].Metrics.Line.X, _baseline - _paragraph.Lines[0].Metrics.Baseline));
            }
        }
        public void Dispose() { _paragraph.Dispose(); _red?.Dispose(); _blue?.Dispose(); }
    }
}
