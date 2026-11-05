using System.Globalization;
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
    internal static int RunJustifiedShaping()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 76, 320 })
        foreach (string kind in new[] { "plain", "runs", "runs-color", "split", "nested", "inter-character", "forced", "split-forced", "selection", "split-selection",
            "tracking", "word", "auto", "rtl", "bidi", "cursive", "emoji", "combining", "wrapped", "columns", "reader", "editor", "writing-tools" })
        {
            context.DefaultRenderingEngine = engine; string label = $"justify-{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            bool oracle = kind is not ("runs-color" or "selection" or "split-selection" or "wrapped" or "columns" or "reader" or "editor" or "writing-tools");
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested renderer unavailable");
                byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => view.Render(dc), () => true) ?? throw new Exception("Justification readback failed"));
                using var scene = new JustifiedScene(kind, width, dpi, false); scene.Layout(); var normal = Capture(scene.View);
                scene.Change(true); scene.Layout(); var changed = Capture(scene.View); var metrics = NumericMetrics(scene.View);
                using var fresh = new JustifiedScene(kind, width, dpi, true); fresh.Layout(); var expected = Capture(fresh.View);
                Check(changed.SequenceEqual(expected), "Justification differs from a fresh view");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(NumericMetrics(fresh.View)), "Justification retained stale geometry");
                Check(changed.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0), "Justified glyph ink disappeared");
                if (oracle)
                    foreach (bool justified in new[] { false, true })
                    {
                        using var reference = new JustifiedOracle(kind, width, justified); reference.SetRootDpi(new(dpi, dpi));
                        reference.Measure(new(width, 240)); reference.Arrange(new(0, 0, width, 240)); var pixels = Capture(reference);
                        Check((justified ? changed : normal).SequenceEqual(pixels), "Justification differs from independently translated whole CoreText glyphs (" + justified + ")");
                        Save(pixels, width * dpi, 240 * dpi, label + (justified ? "-oracle.png" : "-oracle-normal.png"));
                    }
                if (kind == "writing-tools")
                {
                    var editor = (RichTextBox)scene.View;
                    var source = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                    int start = source.Text.IndexOf("ffi", StringComparison.Ordinal); Check(start >= 0, "Justification lost Writing Tools source text");
                    try
                    {
                        MacOSWritingToolsPresentation.Prepare(editor, source, "justified-ligature", start, 1, 0, () => true);
                        var hidden = Capture(editor); Check(hidden.Where((_, index) => index % 4 == 3).All(alpha => alpha == 0), "Hidden justified ligature left glyph ink");
                        var preview = MacOSWritingToolsPresentation.CreatePreview(editor, source, start, 1, Matrix.Identity, dpi, null, () => true);
                        Check(preview is not null && Canvas(preview, width * dpi, 240 * dpi, dpi).SequenceEqual(changed), "Justified preview changed positioned glyphs");
                        Save(hidden, width * dpi, 240 * dpi, label + "-hidden.png");
                    }
                    finally { MacOSWritingToolsPresentation.Clear(editor); }
                    Check(Capture(editor).SequenceEqual(changed), "Justified glyph mask leaked after clear");
                }
                scene.AssertSources(); scene.Change(false); scene.Layout(); var restored = Capture(scene.View);
                Check(normal.SequenceEqual(restored), "Justification reset retained stale glyphs"); scene.AssertSources();
                foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                    Save(item.Item2, width * dpi, 240 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, wholeGlyphOracle = oracle, metrics });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count != 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool value, string message) { if (!value) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "justified-shaping.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private static string[] JustifiedParts(string kind) => kind switch
    {
        "inter-character" or "forced" or "split-forced" or "selection" or "split-selection" or "writing-tools" => ["f", "f", "i"],
        "auto" => ["中A", "V f", "fi文"], "rtl" => ["של", "ום ", "עולם"], "bidi" => ["אב ", "AV ", "אב"],
        "cursive" => ["س", "لا", "م"], "emoji" => ["👩\u200d", "💻🙂", "文"], "combining" => ["a", "\u0301V ", "a\u0301V"],
        "wrapped" or "columns" => ["AV f", "fi AV ffi AV ffi ", "AV ffi AV ffi AV ffi"], _ => ["A", "V ff", "i AV"],
    };

    private static bool JustifiedCharacters(string kind) => kind is "inter-character" or "forced" or "split-forced" or "selection" or "split-selection" or "writing-tools" or "cursive" or "emoji" or "combining";

    private sealed class JustifiedScene : IDisposable
    {
        internal readonly FrameworkElement View;
        private readonly string _body, _content; private readonly int _width;
        private readonly string _kind;
        private readonly DependencyObject _style; private readonly FlowDocumentPresentation? _reader;
        private readonly TextBlock[] _text = []; private readonly Run[] _runs = [];
        private readonly object?[] _parents;
        internal JustifiedScene(string kind, int width, int dpi, bool changed)
        {
            _kind = kind; _width = width; var parts = JustifiedParts(kind); _content = string.Concat(parts);
            bool characters = JustifiedCharacters(kind), optional = kind is "inter-character" or "emoji" or "combining";
            _body = $"display:block;width:{width}px;font-family:Seravek;font-size:32px;line-height:44px;color:#146342;white-space:pre;font-kerning:normal;" +
                $"font-feature-settings:{(optional ? "'kern' 1" : "'liga' 1")};text-justify:{(characters ? "inter-character" : kind == "auto" ? "auto" : "inter-word")};" +
                $"word-spacing:{(kind == "word" ? 5 : 0)}px;letter-spacing:{(kind == "tracking" ? 2 : 0)}px;text-autospace:{(kind == "auto" ? "normal" : "no-autospace")};";
            if (kind is "wrapped" or "columns") _body = _body.Replace("white-space:pre", "white-space:normal");
            if (kind == "columns") _body += "columns:2;column-gap:16px;height:132px;column-fill:auto;";
            if (kind is "rtl" or "cursive") _body += "direction:rtl;";
            if (kind is "reader" or "editor" or "writing-tools")
            {
                _runs = parts.Select(value => new Run(value)).ToArray(); var paragraph = new Paragraph();
                foreach (var run in _runs) paragraph.Inlines.Add(run);
                var document = new FlowDocument(paragraph) { PagePadding = new(0) }; _style = document;
                Css.SetStyleSheet(document, "Paragraph,Section{display:block;margin:0;padding:0;border:0}Run{display:inline}");
                if (kind == "reader") { _reader = new(); _reader.SetDocument(document); View = _reader; }
                else View = new RichTextBox(document) { Template = null, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent, IsReadOnly = true };
            }
            else if (kind is "split" or "nested" or "split-forced" or "split-selection")
            {
                var root = new StackPanel(); View = root; _style = root;
                _text = parts.Select(value => new TextBlock { Text = value, IsTextSelectionEnabled = true }).ToArray();
                if (kind == "nested")
                { var frame = new StackPanel(); frame.Children.Add(_text[0]); frame.Children.Add(_text[1]); root.Children.Add(frame); Css.SetStyle(frame, "display:inline;background:#e9f6ee;"); root.Children.Add(_text[2]); }
                else foreach (var text in _text) root.Children.Add(text);
                Css.SetStyleSheet(root, "TextBlock{display:inline}");
            }
            else
            {
                var text = new TextBlock { IsTextSelectionEnabled = true }; View = text; _style = text; _text = [text];
                if (kind is not ("runs" or "runs-color")) text.Text = _content;
                else
                {
                    _runs = parts.Select(value => new Run(value)).ToArray(); foreach (var run in _runs) text.Inlines.Add(run);
                    if (kind == "runs-color") { _runs[0].Foreground = Brushes.Red; _runs[1].Foreground = Brushes.Red; _runs[2].Foreground = Brushes.Blue; }
                }
            }
            _parents = _text.Select(text => (object?)text.Parent).Concat(_runs.Select(run => (object?)run.Parent)).ToArray();
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }
        internal void Change(bool changed)
        {
            Css.SetStyle(_style, _body + "text-align:" + (changed ? "justify-all" : "left") + ";");
            if (_kind == "selection") _text[0].Select(1, 1);
            if (_kind == "split-selection") _text[1].Select(0, 1);
        }
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(View.Dispatcher); _reader?.SetViewportHeight(240);
              View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout(); }
        }
        internal void AssertSources()
        {
            if (_runs.Length > 0 && string.Concat(_runs.Select(run => run.Text)) != _content) throw new Exception("Justification changed original runs");
            if (_runs.Length == 0 && string.Concat(_text.Select(text => text.Text)) != _content) throw new Exception("Justification changed source text");
            if (!_parents.SequenceEqual(_text.Select(text => (object?)text.Parent).Concat(_runs.Select(run => (object?)run.Parent)))) throw new Exception("Justification changed source ownership");
            foreach (var text in _text)
            {
                text.SelectAll(); if (text.SelectedText != text.Text) throw new Exception("Justification copy differs from source text"); text.Select(0, 0);
                foreach (var segment in text.GetSourceRangeSegments(0, text.Text.Length))
                    if (segment.EndX > segment.StartX && text.GetPositionFromPoint(new((segment.StartX + segment.EndX) / 2, segment.Baseline - 2), false) is null)
                        throw new Exception("Justified source midpoint lost its caret");
            }
        }
        public void Dispose() { _reader?.Dispose(); Css.SetStyleSheets(_style, null); }
    }

    // Independent reference: retain glyph masks from the old whole-paragraph
    // API, then translate clusters using the hand-computed physical expansions.
    private sealed class JustifiedOracle : FrameworkElement, IDisposable
    {
        private readonly NativeTextParagraph _paragraph;
        private readonly List<(int Start, int Length, double Shift)> _pieces = [];
        private readonly double _baseline;
        private readonly double? _frameWidth;
        internal JustifiedOracle(string kind, int width, bool justified)
        {
            string text = string.Concat(JustifiedParts(kind)); bool characters = JustifiedCharacters(kind), auto = kind == "auto", cursive = kind == "cursive";
            bool ligature = !justified || kind is not ("inter-character" or "emoji" or "combining");
            double tracking = kind == "tracking" ? 2 : 0, word = kind == "word" ? 5 : 0;
            string family = FontFeatureRenderingSource.Wrap("Seravek", [new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), ligature ? 1u : 0u),
                new(FontFeature.TagOf("clig"), 0), new(FontFeature.TagOf("dlig"), 0), new(FontFeature.TagOf("hlig"), 0)]);
            var metrics = TextMeasurement.GetFontMetrics(family, 32, 400, 0); _baseline = metrics.Ascent + (44 - metrics.Ascent - metrics.Descent) / 2;
            double ic = TextMeasurement.GetFontUnitMetrics(family, 32, 400, 0).IdeographicAdvance;
            _paragraph = NativeTextParagraph.TryCreate([new(text, family, 32, 400, 0, Color.FromRgb(20, 99, 66))], family, 32, 100000, 44,
                TextAlignment.Left, kind is "rtl" or "cursive" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, noWrap: true)
                ?? throw new Exception("Justification whole-font reference unavailable");
            var boundaries = GraphemeClusters.GetBoundaries(text);
            var cells = Enumerable.Range(0, boundaries.Length - 1).Select(i =>
                (Start: boundaries[i], End: boundaries[i + 1], Left: Math.Min(_paragraph.Caret(0, boundaries[i], false).X,
                    _paragraph.Caret(0, boundaries[i + 1], true).X))).OrderBy(cell => cell.Left).ToArray();
            bool Space(int i) => text[cells[i].Start] == ' ';
            bool Cjk(int i) => text[cells[i].Start] is '中' or '文';
            double[] autoGaps = Enumerable.Range(0, cells.Length - 1).Select(i => auto && Cjk(i) != Cjk(i + 1) && !Space(i) && !Space(i + 1) ? ic / 8 : 0).ToArray();
            bool[] between = Enumerable.Range(0, cells.Length - 1).Select(i => characters && !cursive || auto && !Space(i) && !Space(i + 1) && (Cjk(i) || Cjk(i + 1))).ToArray();
            bool[] separator = Enumerable.Range(0, cells.Length).Select(i => i > 0 && i + 1 < cells.Length && Space(i) && !characters).ToArray();
            int count = between.Count(value => value) + separator.Count(value => value);
            double natural = _paragraph.Lines[0].Metrics.Line.Width + autoGaps.Sum() + word * cells.Count(cell => text[cell.Start] == ' ') + (cells.Length - 1) * tracking;
            double extra = justified && count > 0 ? Math.Max(0, width - natural) / count : 0;
            double offset = justified && count == 0 ? Math.Max(0, width - natural) / 2 : 0;
            // Overflowing justified RTL lines fall back to their physical
            // right start edge. The visible tail differs from text-align:left.
            if (justified && kind == "rtl" && natural > width) offset = width - natural;
            double accumulated = offset; var shifts = new double[text.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                double own = Space(i) ? word : 0, before = i == 0 ? 0 : autoGaps[i - 1] / 2, after = i == autoGaps.Length ? 0 : autoGaps[i] / 2;
                double leading = i > 0 && between[i - 1] ? extra / 2 : 0, trailing = i < between.Length && between[i] ? extra / 2 : 0;
                if (separator[i]) { leading += extra / 2; trailing += extra / 2; }
                for (int k = cells[i].Start; k < cells[i].End; k++) shifts[k] = accumulated + own / 2 + before + leading + i * tracking;
                accumulated += own + before + after + leading + trailing;
            }
            for (int start = 0; start < text.Length;)
            {
                int length = cursive ? text.Length : ligature && text.AsSpan(start).StartsWith("ffi") ? 3 : boundaries[Array.IndexOf(boundaries, start) + 1] - start;
                if (text[start] != ' ') _pieces.Add((start, length, shifts.Skip(start).Take(length).Average())); start += length;
            }
            if (kind == "nested") _frameWidth = _paragraph.Caret(0, 5, false).X + shifts[5];
        }
        protected override void OnRender(DrawingContext context)
        {
            if (_frameWidth is { } frameWidth)
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(233, 246, 238)), null, new(0, 0, frameWidth, 44));
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
