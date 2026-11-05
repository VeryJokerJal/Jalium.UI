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
    internal static int RunPercentageSpacing()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 76, 320 })
        foreach (string kind in new[] { "plain", "runs", "runs-color", "split", "nested", "mixed-runs", "small-frame", "calc-inherit",
            "negative", "calc", "auto", "rtl", "bidi", "cursive", "emoji", "combining", "wrapped", "columns", "tab", "ellipsis", "atomic",
            "transition", "keyframe", "reader", "editor", "writing-tools" })
        {
            context.DefaultRenderingEngine = engine; string label = $"percent-{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            bool oracle = kind is "plain" or "runs" or "split" or "negative" or "calc" or "rtl" or "bidi" or "cursive" or "emoji" or "combining" or "transition" or "keyframe";
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested renderer unavailable");
                byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => view.Render(dc), () => true) ?? throw new Exception("Percentage spacing readback failed"));
                using var scene = new PercentageScene(kind, width, dpi, false); scene.Layout(); var normal = Capture(scene.View);
                scene.Change(true); scene.Layout(); var changed = Capture(scene.View); var metrics = NumericMetrics(scene.View);
                using var reference = new PercentageScene(kind, width, dpi, true, pixels: true); reference.Layout(); var expected = Capture(reference.View);
                Check(changed.SequenceEqual(expected), "Percentage tracking differs from independently resolved pixel tracking");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(NumericMetrics(reference.View)), "Percentage tracking retained stale layout geometry");
                Check(changed.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0), "Percentage glyph ink disappeared");
                if (oracle)
                    foreach (bool resized in new[] { false, true })
                    {
                        using var old = new PercentageOracle(kind, resized ? 48 : 32); old.SetRootDpi(new(dpi, dpi));
                        old.Measure(new(width, 240)); old.Arrange(new(0, 0, width, 240)); var pixels = Capture(old);
                        Check(PercentageGlyphsEqual(resized ? changed : normal, pixels), "Percentage tracking differs from independently translated whole CoreText glyphs (" + resized + ")");
                        Save(pixels, width * dpi, 240 * dpi, label + (resized ? "-oracle.png" : "-oracle-normal.png"));
                    }
                if (kind == "writing-tools")
                {
                    var editor = (RichTextBox)scene.View;
                    var source = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                    int start = source.Text.IndexOf("ffi", StringComparison.Ordinal); Check(start >= 0, "Percentage tracking lost Writing Tools source");
                    try
                    {
                        MacOSWritingToolsPresentation.Prepare(editor, source, "percentage-ligature", start, 1, 0, () => true);
                        var hidden = Capture(editor); Check(hidden.Where((_, index) => index % 4 == 3).All(alpha => alpha == 0), "Hidden percentage ligature left glyph ink");
                        var preview = MacOSWritingToolsPresentation.CreatePreview(editor, source, start, 1, Matrix.Identity, dpi, null, () => true);
                        Check(preview is not null && Canvas(preview, width * dpi, 240 * dpi, dpi).SequenceEqual(changed), "Percentage preview changed positioned glyphs");
                        Save(hidden, width * dpi, 240 * dpi, label + "-hidden.png");
                    }
                    finally { MacOSWritingToolsPresentation.Clear(editor); }
                    Check(Capture(editor).SequenceEqual(changed), "Percentage glyph mask leaked after clear");
                }
                scene.AssertSources(); scene.Change(false); scene.Layout(); var restored = Capture(scene.View);
                Check(normal.SequenceEqual(restored), "Percentage tracking reset retained stale glyphs"); scene.AssertSources();
                foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                    Save(item.Item2, width * dpi, 240 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, wholeGlyphOracle = oracle, metrics });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count != 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool value, string message) { if (!value) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "percentage-spacing.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private static string[] PercentageParts(string kind) => kind switch
    {
        "writing-tools" => ["", "f", "fi"], "auto" => ["中", "AV f", "fi文"], "rtl" => ["של", "ום ", "עולם"],
        "bidi" => ["אב ", "AV ", "אב"], "cursive" => ["مر", "حبا ", "مرحبا"], "emoji" => ["👩\u200d", "💻🙂", "文"],
        "combining" => ["a", "\u0301V ", "a\u0301V"], "tab" => ["AV", "\t", "AV"], "atomic" => ["AV", "AV", "AV"],
        "wrapped" or "columns" or "ellipsis" => ["AV f", "fi AV ffi AV ffi ", "AV ffi AV ffi AV ffi"], _ => ["A", "V ff", "i AV"],
    };

    private static bool PercentageGlyphsEqual(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;
        // Paragraph cell advances use floats; the independent draw translates
        // through a second float API. At a fractional bidi edge CoreText can
        // differ by two coverage levels. Compare premultiplied color so tiny
        // alpha does not amplify an otherwise negligible RGB difference.
        for (int i = 0; i < first.Length; i += 4)
        {
            int a = first[i + 3], b = second[i + 3];
            if (Math.Abs(a - b) > 2) return false;
            for (int channel = 0; channel < 3; channel++)
                if (Math.Abs(first[i + channel] * a - second[i + channel] * b) > 2 * 255) return false;
        }
        return true;
    }
    private static double PercentagePixels(string kind, double size, double? used = null)
        => kind == "negative" ? -.05 * (used ?? size) : kind is "calc" or "calc-inherit" ? .1 * size + .05 * (used ?? size)
            : kind is "transition" or "keyframe" ? (.1 * size + 8) / 2 : .1 * (used ?? size);

    private sealed class PercentageScene : IDisposable
    {
        internal readonly FrameworkElement View;
        private readonly string _kind, _content; private readonly int _width; private readonly bool _pixels;
        private readonly DependencyObject _style; private readonly FlowDocumentPresentation? _reader; private readonly StackPanel? _frame;
        private readonly TextBlock[] _text = []; private readonly Run[] _runs = []; private readonly object?[] _parents;
        internal PercentageScene(string kind, int width, int dpi, bool changed, bool pixels = false)
        {
            _kind = kind; _width = width; _pixels = pixels; var parts = PercentageParts(kind); _content = string.Concat(parts);
            if (kind is "reader" or "editor" or "writing-tools")
            {
                _runs = parts.Select(value => new Run(value)).ToArray(); var paragraph = new Paragraph();
                foreach (var run in _runs) paragraph.Inlines.Add(run);
                var document = new FlowDocument(paragraph) { PagePadding = new(0) }; _style = document;
                Css.SetStyleSheet(document, "Paragraph,Section{display:block;margin:0;padding:0;border:0}Run{display:inline}");
                if (kind == "reader") { _reader = new(); _reader.SetDocument(document); View = _reader; }
                else View = new RichTextBox(document) { Template = null, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent, IsReadOnly = true };
            }
            else if (kind is "split" or "nested" or "small-frame" or "calc-inherit" or "atomic")
            {
                var root = new StackPanel(); View = root; _style = root;
                _text = parts.Select(value => new TextBlock { Text = value, IsTextSelectionEnabled = true }).ToArray();
                if (kind is "nested" or "small-frame" or "calc-inherit")
                { _frame = new(); _frame.Children.Add(_text[0]); _frame.Children.Add(_text[1]); root.Children.Add(_frame); root.Children.Add(_text[2]); }
                else
                    for (int i = 0; i < _text.Length; i++)
                    {
                        root.Children.Add(_text[i]);
                        if (i == 0 && kind == "atomic") { var icon = new Border(); root.Children.Add(icon); Css.SetStyle(icon, "display:inline-block;width:16px;height:16px;background:#e9f6ee"); }
                    }
                Css.SetStyleSheet(root, "TextBlock{display:inline}");
            }
            else
            {
                var text = new TextBlock { IsTextSelectionEnabled = true }; View = text; _style = text; _text = [text];
                if (kind is not ("runs" or "runs-color" or "mixed-runs")) text.Text = _content;
                else
                {
                    _runs = parts.Select(value => new Run(value)).ToArray(); foreach (var run in _runs) text.Inlines.Add(run);
                    if (kind == "runs-color") { _runs[0].Foreground = Brushes.Red; _runs[1].Foreground = Brushes.Red; _runs[2].Foreground = Brushes.Blue; }
                }
                if (kind == "keyframe") Css.SetStyleSheet(text, "@keyframes tracking{from{letter-spacing:10%}to{letter-spacing:8px}}");
            }
            _parents = _text.Select(text => (object?)text.Parent).Concat(_runs.Select(run => (object?)run.Parent)).ToArray();
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }
        internal void Change(bool changed)
        {
            double size = changed ? 48 : 32; string Px(double value) => value.ToString("R", CultureInfo.InvariantCulture) + "px";
            string spacing = _pixels ? Px(PercentagePixels(_kind, size)) : _kind == "negative" ? "-5%" : _kind is "calc" or "calc-inherit" ? "calc(.1em + 5%)" : "10%";
            string body = $"display:block;width:{_width}px;font-family:Seravek;font-size:{size}px;line-height:64px;color:#146342;white-space:pre;font-kerning:normal;font-feature-settings:'liga' 1;word-spacing:0;letter-spacing:{spacing};text-autospace:{(_kind == "auto" ? "normal" : "no-autospace")};";
            if (_kind is "wrapped" or "columns") body = body.Replace("white-space:pre", "white-space:normal");
            if (_kind == "columns") body += "columns:2;column-gap:16px;height:192px;column-fill:auto;";
            if (_kind == "rtl" || _kind == "cursive") body += "direction:rtl;text-align:left;";
            if (_kind == "ellipsis") body += "white-space:nowrap;text-overflow:ellipsis;overflow:hidden;";
            Css.SetStyle(_style, body);
            if (_frame is not null) Css.SetStyle(_frame, "display:inline;background:#e9f6ee;" +
                (_kind is "small-frame" or "calc-inherit" ? "font-size:.75em;" + (_pixels ? "letter-spacing:" + Px(PercentagePixels(_kind, size, size * .75)) : "") : ""));
            if (_kind == "mixed-runs") Css.SetStyle(_runs[0], "font-size:.5em;" + (_pixels ? "letter-spacing:" + Px(PercentagePixels(_kind, size, size / 2)) : ""));
            CssEvaluationScheduler.FlushIfPending(View.Dispatcher);
            if (!_pixels && _kind is "transition" or "keyframe")
            {
                object? value;
                if (_kind == "keyframe")
                {
                    var node = CssNode.Get(View); var frames = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "tracking")!)[CssFlowProperties.LetterSpacingProperty];
                    value = new CssPropertySampler(CssFlowProperties.LetterSpacingProperty, frames).Sample(.5, new("linear"));
                }
                else value = CssTextSpacing.Interpolate(CssLayoutLength.Percent(.1), CssLayoutLength.Px(8), .5);
                View.SetCssAnimatedValue(CssFlowProperties.LetterSpacingProperty, value);
            }
        }
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(View.Dispatcher); _reader?.SetViewportHeight(240);
              View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout(); }
        }
        internal void AssertSources()
        {
            if (_runs.Length > 0 && string.Concat(_runs.Select(run => run.Text)) != _content) throw new Exception("Percentage tracking edited original runs");
            if (_runs.Length == 0 && string.Concat(_text.Select(text => text.Text)) != _content) throw new Exception("Percentage tracking edited original source");
            if (!_parents.SequenceEqual(_text.Select(text => (object?)text.Parent).Concat(_runs.Select(run => (object?)run.Parent)))) throw new Exception("Percentage tracking changed source ownership");
            foreach (var text in _text)
            { text.SelectAll(); if (text.SelectedText != text.Text) throw new Exception("Percentage copy differs from source"); text.Select(0, 0);
              foreach (var segment in text.GetSourceRangeSegments(0, text.Text.Length))
                if (segment.EndX > segment.StartX && text.GetPositionFromPoint(new((segment.StartX + segment.EndX) / 2, segment.Baseline - 2), false) is null)
                    throw new Exception("Percentage source midpoint lost its caret"); }
        }
        public void Dispose() { View.ClearCssAnimatedValue(CssFlowProperties.LetterSpacingProperty); _reader?.Dispose(); Css.SetStyleSheets(_style, null); }
    }

    private sealed class PercentageOracle : FrameworkElement, IDisposable
    {
        private readonly NativeTextParagraph _paragraph; private readonly List<(int Start, int Length, double Shift)> _pieces = [];
        private readonly double _baseline;
        internal PercentageOracle(string kind, double size)
        {
            string text = string.Concat(PercentageParts(kind)); double spacing = PercentagePixels(kind, size);
            string family = FontFeatureRenderingSource.Wrap("Seravek", [new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), 1),
                new(FontFeature.TagOf("clig"), 0), new(FontFeature.TagOf("dlig"), 0), new(FontFeature.TagOf("hlig"), 0)]);
            var metrics = TextMeasurement.GetFontMetrics(family, size, 400, 0); _baseline = metrics.Ascent + (64 - metrics.Ascent - metrics.Descent) / 2;
            _paragraph = NativeTextParagraph.TryCreate([new(text, family, size, 400, 0, Color.FromRgb(20, 99, 66))], family, size, 100000, 64,
                TextAlignment.Left, kind is "rtl" or "cursive" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, noWrap: true)
                ?? throw new Exception("Percentage old paragraph reference unavailable");
            var boundaries = GraphemeClusters.GetBoundaries(text); var shifts = new double[text.Length];
            var cells = Enumerable.Range(0, boundaries.Length - 1).Select(i => (Start: boundaries[i], End: boundaries[i + 1],
                Left: Math.Min(_paragraph.Caret(0, boundaries[i], false).X, _paragraph.Caret(0, boundaries[i + 1], true).X))).OrderBy(cell => cell.Left).ToArray();
            double shift = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0 && !(kind == "cursive" && text[cells[i - 1].Start] != ' ' && text[cells[i].Start] != ' ')) shift += spacing;
                for (int k = cells[i].Start; k < cells[i].End; k++) shifts[k] = shift;
            }
            for (int start = 0; start < text.Length;)
            {
                int length = kind == "cursive" && text.AsSpan(start).StartsWith("مرحبا") ? 5 : text.AsSpan(start).StartsWith("ffi") ? 3 : boundaries[Array.IndexOf(boundaries, start) + 1] - start;
                if (text[start] != ' ') _pieces.Add((start, length, shifts.Skip(start).Take(length).Average())); start += length;
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
