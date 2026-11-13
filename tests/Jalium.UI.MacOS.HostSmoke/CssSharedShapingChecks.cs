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
    private static (string First, string Last, double Spacing, bool Forced) SharedCase(string kind) => kind switch
    {
        "forced" or "writing-tools" => ("f", "fi", 2, true), "ligatures" => ("f", "fi", 2, false),
        "combining" => ("a", "\u0301b", 2, false), "emoji" => ("👩\u200d", "💻🙂", 3, false),
        "cursive" => ("مر", "حبا", 3, false), "bidi" => ("אב", "AB", 3, false),
        "negative" => ("A", "V", -1, false),
        "decoration" => ("AB", "אב", 2, false),
        "wrapped" or "columns" => ("AV ", "AV AV AV AV AV AV AV AV AV", 2, false),
        _ => ("A", "V", 2, false),
    };

    internal static int RunSharedShaping()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failed = 0;
        string[] kinds = ["pair", "negative", "ligatures", "forced", "combining", "emoji", "cursive", "bidi", "color",
            "nested", "nested-background", "decoration", "selection", "wrapped", "columns", "reader", "editor", "writing-tools"];
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 68, 320 }) foreach (string kind in kinds)
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_SHARED_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; string label = $"shared-{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested renderer unavailable");
                byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => view.Render(dc), () => true) ?? throw new Exception("Shared readback failed"));
                using var scene = new SharedScene(kind, width, dpi, false); scene.Layout(); var normal = Capture(scene.View);
                scene.Change(true); scene.Layout(); var changed = Capture(scene.View); var metrics = NumericMetrics(scene.View);
                using var fresh = new SharedScene(kind, width, dpi, true); fresh.Layout(); var expected = Capture(fresh.View);
                Check(changed.SequenceEqual(expected), "Shared shaping differs from a fresh view");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(NumericMetrics(fresh.View)), "Shared source geometry differs from a fresh view");
                Check(changed.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0), "Shared glyph ink disappeared");
                Check(normal.SequenceEqual(changed) == (kind == "cursive"), "Tracking changed the wrong glyph positions");
                if (kind == "decoration") Check(scene.DecorationHasInk(changed, dpi), "The RTL Run lost its underline");
                if (kind == "writing-tools")
                {
                    var editor = (RichTextBox)scene.View;
                    var source = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                    int first = source.Text.IndexOf("ffi", StringComparison.Ordinal);
                    Check(first >= 0, "Writing Tools source lost the original ligature text");
                    try
                    {
                        MacOSWritingToolsPresentation.Prepare(editor, source, "shared-cluster", first, 1, 0, () => true);
                        var hidden = Capture(editor);
                        Check(hidden.Where((_, index) => index % 4 == 3).All(alpha => alpha == 0), "A cross-source ligature left partial hidden glyph ink");
                        var preview = MacOSWritingToolsPresentation.CreatePreview(editor, source, first, 1, Matrix.Identity, dpi, null, () => true);
                        Check(preview is not null && Canvas(preview, width * dpi, 240 * dpi, dpi).SequenceEqual(changed),
                            "A cross-source ligature preview lost or duplicated glyph ink");
                        Save(hidden, width * dpi, 240 * dpi, label + "-hidden.png");
                    }
                    finally { MacOSWritingToolsPresentation.Clear(editor); }
                    Check(Capture(editor).SequenceEqual(changed), "A shared glyph mask leaked into the next frame");
                }
                byte[]? oracle = null;
                if (kind is "pair" or "negative" or "ligatures" or "forced" or "combining" or "emoji" or "cursive" or "bidi" or "color" or "nested")
                {
                    using var reference = new SharedOracle(kind); reference.SetRootDpi(new(dpi, dpi));
                    reference.Measure(new(width, 240)); reference.Arrange(new(0, 0, width, 240)); oracle = Capture(reference);
                    Check(changed.SequenceEqual(oracle), "Source seams differ from independently translated whole CoreText glyphs");
                    Save(oracle, width * dpi, 240 * dpi, label + "-oracle.png");
                }
                scene.AssertSources();
                scene.Change(false); scene.Layout(); var restored = Capture(scene.View);
                Check(normal.SequenceEqual(restored), "Shared tracking reset retained stale glyphs");
                scene.AssertSources();
                foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                    Save(item.Item2, width * dpi, 240 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, wholeGlyphOracle = oracle is not null, metrics });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count != 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool value, string message) { if (!value) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "shared-shaping.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private sealed class SharedScene : IDisposable
    {
        internal readonly FrameworkElement View;
        private readonly TextBlock? _first, _last;
        private readonly Run? _firstRun, _lastRun;
        private readonly FlowDocument? _document;
        private readonly FlowDocumentPresentation? _reader;
        private readonly DependencyObject _style;
        private readonly object? _firstParent, _lastParent;
        private readonly string _kind, _body, _firstText, _lastText;
        private readonly int _width;
        internal SharedScene(string kind, int width, int dpi, bool changed)
        {
            _kind = kind; _width = width; var sample = SharedCase(kind); _firstText = sample.First; _lastText = sample.Last;
            _body = $"display:block;width:{width}px;font-family:Seravek;font-size:32px;line-height:44px;color:#146342;white-space:nowrap;font-kerning:normal;text-autospace:no-autospace;";
            if (kind is "wrapped" or "columns") _body = _body.Replace("white-space:nowrap", "white-space:normal");
            if (kind == "columns") _body += "columns:2;column-gap:16px;height:88px;column-fill:auto;";
            if (kind == "cursive") _body += "direction:rtl;text-align:left;";
            if (kind is "reader" or "editor" or "writing-tools")
            {
                _firstRun = new(sample.First); _lastRun = new(sample.Last);
                var paragraph = new Paragraph(_firstRun); paragraph.Inlines.Add(_lastRun);
                _document = new(paragraph) { PagePadding = new(0) }; _style = _document;
                Css.SetStyleSheet(_document, "Paragraph,Section{display:block;margin:0;padding:0;border:0}Run{display:inline}");
                if (kind is "editor" or "writing-tools") View = new RichTextBox(_document) { Template = null, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent, IsReadOnly = true };
                else { _reader = new(); _reader.SetDocument(_document); View = _reader; }
                _firstParent = _firstRun.Parent; _lastParent = _lastRun.Parent;
            }
            else
            {
                var root = new StackPanel(); View = root; _style = root;
                _first = new() { Text = sample.First, IsTextSelectionEnabled = true }; _last = new() { Text = sample.Last, IsTextSelectionEnabled = true };
                if (kind is "nested" or "nested-background")
                { var frame = new StackPanel(); frame.Children.Add(_first); root.Children.Add(frame); Css.SetStyle(frame, "display:inline;" + (kind == "nested-background" ? "background:#e9f6ee;" : "")); }
                else root.Children.Add(_first);
                root.Children.Add(_last); Css.SetStyleSheet(root, "TextBlock{display:inline}");
                if (kind == "color") Css.SetStyle(_first, "color:#b63342;");
                if (kind == "decoration")
                {
                    _last.Text = ""; var run = new Run(sample.Last); _last.Inlines.Add(run);
                    Css.SetStyle(run, "text-decoration:underline;");
                }
                _firstParent = _first.Parent; _lastParent = _last.Parent;
            }
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }
        internal void Change(bool changed)
        {
            var sample = SharedCase(_kind); double spacing = changed ? sample.Spacing : 0;
            Css.SetStyle(_style, _body + "letter-spacing:" + spacing.ToString(CultureInfo.InvariantCulture) + "px;" +
                (sample.Forced ? "font-feature-settings:'liga' 1;" : ""));
            if (_kind == "selection") _last!.Select(0, 1);
        }
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(View.Dispatcher); _reader?.SetViewportHeight(240);
              View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout(); }
        }
        internal void AssertSources()
        {
            if (_first is not null)
            {
                if (_first.Text != _firstText || _last!.Text != _lastText || !ReferenceEquals(_first.Parent, _firstParent) || !ReferenceEquals(_last.Parent, _lastParent))
                    throw new Exception("Shared formatting edited source text or source ownership");
                foreach (var text in new[] { _first, _last! })
                    foreach (var segment in text.GetSourceRangeSegments(0, text.Text.Length))
                    {
                        if (segment.EndX <= segment.StartX) continue;
                        var point = text.GetPositionFromPoint(new((segment.StartX + segment.EndX) / 2, segment.Baseline - 2), false);
                        if (point is null) throw new Exception("Shared source midpoint lost its text position");
                    }
            }
            else if (_firstRun!.Text != _firstText || _lastRun!.Text != _lastText || !ReferenceEquals(_firstRun.Parent, _firstParent) || !ReferenceEquals(_lastRun.Parent, _lastParent))
                throw new Exception("Document formatting edited original runs");
        }
        internal bool DecorationHasInk(byte[] pixels, int dpi)
        {
            var segment = _last!.GetSourceRangeSegments(0, _lastText.Length).Single();
            var origin = _last.TransformToAncestor(View);
            int x = Math.Clamp((int)Math.Round((origin.X + (segment.StartX + segment.EndX) / 2) * dpi), 0, _width * dpi - 1);
            int first = (int)Math.Ceiling((origin.Y + segment.Baseline) * dpi) + 1;
            int last = Math.Min(240 * dpi, (int)Math.Ceiling((origin.Y + segment.Baseline + 5) * dpi));
            for (int y = Math.Max(0, first); y < last; y++) if (pixels[(y * _width * dpi + x) * 4 + 3] > 0) return true;
            return false;
        }
        public void Dispose() { _reader?.Dispose(); Css.SetStyleSheets(_style, null); }
    }

    // Shape an independent, unpartitioned string with the old paragraph API,
    // then translate retained glyphs. No shared-flow layout or slice API is used.
    private sealed class SharedOracle : FrameworkElement, IDisposable
    {
        private readonly List<NativeTextParagraph> _paragraphs = [];
        private readonly List<(NativeTextParagraph Paragraph, int Start, int Length, double Shift)> _pieces = [];
        private readonly double _baseline;
        internal SharedOracle(string kind)
        {
            var sample = SharedCase(kind); string content = sample.First + sample.Last;
            FontFeature[] features = [new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), sample.Forced ? 1u : 0u),
                new(FontFeature.TagOf("clig"), 0), new(FontFeature.TagOf("dlig"), 0), new(FontFeature.TagOf("hlig"), 0)];
            string family = FontFeatureRenderingSource.Wrap("Seravek", features);
            var font = TextMeasurement.GetFontMetrics(family, 32, 400, 0); _baseline = font.Ascent + (44 - font.Ascent - font.Descent) / 2;
            NativeTextParagraph Make(Color color)
            {
                var paragraph = NativeTextParagraph.TryCreate([new(content, family, 32, 400, 0, color)], family, 32, 100000, 44,
                    TextAlignment.Left, kind == "cursive" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, noWrap: true)
                    ?? throw new Exception("Shared whole-string reference failed"); _paragraphs.Add(paragraph); return paragraph;
            }
            var green = Make(Color.FromRgb(20, 99, 66)); var first = kind == "color" ? Make(Color.FromRgb(182, 51, 66)) : green;
            if (kind == "cursive") { _pieces.Add((green, 0, content.Length, 0)); return; }
            if (sample.Forced) { _pieces.Add((green, 0, content.Length, sample.Spacing)); return; }
            var boundaries = GraphemeClusters.GetBoundaries(content); var visual = new List<(int Start, int End, double X)>();
            for (int i = 0; i + 1 < boundaries.Length; i++)
                visual.Add((boundaries[i], boundaries[i + 1], Math.Min(green.Caret(0, boundaries[i], false).X, green.Caret(0, boundaries[i + 1], true).X)));
            visual.Sort((a, b) => a.X.CompareTo(b.X));
            for (int i = 0; i < visual.Count; i++)
            { var unit = visual[i]; _pieces.Add((unit.End <= sample.First.Length ? first : green, unit.Start, unit.End - unit.Start, i * sample.Spacing)); }
        }
        protected override void OnRender(DrawingContext context)
        {
            foreach (var piece in _pieces)
            {
                var text = new FormattedText(piece.Paragraph.Text, "Seravek", 32) { PlatformTextLine = piece.Paragraph.Lines[0],
                    Foreground = Brushes.Black, MaxTextWidth = 100000, MaxTextHeight = 240,
                    GlyphVisibility = 1, GlyphRanges = [(uint)piece.Start, (uint)piece.Length] };
                context.DrawText(text, new(piece.Shift - piece.Paragraph.Lines[0].Metrics.Line.X, _baseline - piece.Paragraph.Lines[0].Metrics.Baseline));
            }
        }
        public void Dispose() { foreach (var paragraph in _paragraphs) paragraph.Dispose(); }
    }
}
