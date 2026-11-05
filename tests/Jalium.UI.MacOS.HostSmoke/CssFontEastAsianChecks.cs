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
    private static string s_eastAsianFamily = "";
    private static readonly (string Keyword, string Tag)[] EastAsianFeatures =
    [
        ("jis78", "jp78"), ("jis83", "jp83"), ("jis90", "jp90"), ("jis04", "jp04"),
        ("simplified", "smpl"), ("traditional", "trad"), ("full-width", "fwid"), ("proportional-width", "pwid"), ("ruby", "ruby"),
    ];

    internal static int RunFontEastAsian()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string fixture = Environment.GetEnvironmentVariable("JALIUM_CSS_EAST_ASIAN_FONT") ??
            Path.Combine(Directory.GetCurrentDirectory(), "tests/Jalium.UI.MacOS.Tests/Assets/FontNumeric/FontWithFancyFeatures.otf");
        using var font = CssNativeFontResource.Create(File.ReadAllBytes(fixture)) ?? throw new Exception("WPT feature font could not load");
        s_eastAsianFamily = font.Family;
        var records = new List<object>(); int failed = 0;
        string[] cases = [.. EastAsianFeatures.Select(f => f.Keyword), "combined", "override", "inline-run", "inline-span", "split",
            "reader-run", "reader-span", "reader-paragraph", "reader-section", "reader-root", "reader-marker",
            "editor-run", "editor-span", "editor-paragraph", "editor-section", "editor-root", "editor-marker", "marker", "writing-tools", "cjk", "unsupported"];
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 220, 420 }) foreach (string kind in cases)
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_EAST_ASIAN_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; string label = $"east-asian-{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested renderer unavailable");
                byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => view.Render(dc), () => true) ?? throw new Exception("East Asian readback failed"));
                using var scene = new EastAsianScene(kind, width, dpi, false); scene.Layout(); var normal = Capture(scene.View);
                scene.Change(true); scene.Layout(); var changed = Capture(scene.View); scene.AssertSources(); var metrics = EastAsianMetrics(scene.View);
                using var fresh = new EastAsianScene(kind, width, dpi, true, reference: true); fresh.Layout(); var expected = Capture(fresh.View);
                Check(changed.SequenceEqual(expected), "Changed view differs from independent WPT glyph/native feature reference");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(EastAsianMetrics(fresh.View)), "Changed layout/source hit/IME geometry differs from reference");
                if (kind is not ("cjk" or "unsupported")) Check(!normal.SequenceEqual(changed), "Requested feature has no visible effect in WPT font");
                if (kind == "unsupported") Check(normal.SequenceEqual(changed), "Unavailable feature changed ordinary glyphs");
                if (kind == "writing-tools")
                {
                    var editor = (RichTextBox)scene.View;
                    var source = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                    try
                    {
                        MacOSWritingToolsPresentation.Prepare(editor, source, "east-asian-glyph", 0, 1, 0, () => true);
                        var hidden = Capture(editor); Check(hidden.Where((_, index) => index % 4 == 3).All(alpha => alpha == 0), "Hidden variant glyph left ink");
                        var preview = MacOSWritingToolsPresentation.CreatePreview(editor, source, 0, 1, Matrix.Identity, dpi, null, () => true);
                        Check(preview is not null && Canvas(preview, width * dpi, 240 * dpi, dpi).SequenceEqual(changed), "Writing Tools preview changed substituted glyph");
                        Save(hidden, width * dpi, 240 * dpi, label + "-hidden.png");
                    }
                    finally { MacOSWritingToolsPresentation.Clear(editor); }
                    Check(Capture(editor).SequenceEqual(changed), "Variant glyph mask leaked after clear");
                }
                scene.Change(false); scene.Layout(); scene.AssertSources(); var restored = Capture(scene.View);
                Check(normal.SequenceEqual(restored), "Reset retained old feature glyphs");
                foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                    Save(item.Item2, width * dpi, 240 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, metrics, differentBytes = changed.Zip(expected).Count(p => p.First != p.Second),
                    glyphsChanged = !normal.SequenceEqual(changed) });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count != 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool value, string message) { if (!value) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "east-asian.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private static object EastAsianMetrics(FrameworkElement root)
    {
        var cells = new List<object>(); var imeCells = new List<object>(); Walk(root);
        if (root is RichTextBox editor && editor is IImeSupport ime)
            for (int index = 0; index < editor.Document.GetText().Length; index++)
                if (ime.TryGetImeTextRangeGeometry(index, 1, false, out var geometry))
                    imeCells.Add(new { index, geometry.Rectangle, geometry.Start, geometry.Length });
        return new { views = NumericMetrics(root), cells, imeCells };
        void Walk(Visual view)
        {
            if (view is TextBlock text)
                for (int index = 0; index < text.Text.Length; index++)
                    foreach (var segment in text.GetSourceRangeSegments(index, 1))
                        cells.Add(new { index, segment.StartX, segment.EndX, segment.Baseline,
                            hit = text.GetPositionFromPoint(new((segment.StartX + segment.EndX) / 2, segment.Baseline - 2), true)?.Offset });
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(view); index++)
                if (VisualTreeHelper.GetChild(view, index) is Visual child) Walk(child);
        }
    }

    private sealed class EastAsianScene : IDisposable
    {
        internal readonly FrameworkElement View;
        private readonly List<TextBlock> _texts = [];
        private readonly Run? _run;
        private readonly object? _parent;
        private readonly FlowDocument? _document;
        private readonly FlowDocumentPresentation? _reader;
        private readonly DependencyObject _target;
        private readonly string _kind, _source, _variant, _referenceText, _extra, _family;
        private readonly int _width;
        private readonly bool _reference;
        private const string Body = "font-size:32px;line-height:44px;color:#146342;white-space:pre-wrap;text-autospace:no-autospace;";
        internal EastAsianScene(string kind, int width, int dpi, bool changed, bool reference = false)
        {
            _kind = kind; _width = width; _reference = reference; _variant = "jis04 full-width ruby"; _referenceText = "BBBABBABA";
            int feature = Array.FindIndex(EastAsianFeatures, f => f.Keyword == kind);
            if (feature >= 0) { _variant = kind; _referenceText = new string('B', feature) + "A" + new string('B', 8 - feature); }
            _extra = kind == "override" ? "font-feature-settings:'jp04' off,'ruby' 0;" : "";
            if (kind == "override") _referenceText = "BBBBBBABB";
            _family = kind == "cjk" ? "Hiragino Sans" : kind == "unsupported" ? "Seravek" : s_eastAsianFamily;
            _source = kind == "cjk" ? "辻葛飴 全角ABC 123" : kind == "unsupported" ? "ABC 123" : "YZabcdefg";
            if (kind == "writing-tools") { _source = "e"; _variant = "full-width"; _referenceText = "A"; }
            if (kind is "cjk" or "unsupported") _referenceText = _source;
            else if (kind != "writing-tools")
            {
                // Spaces expose every WPT probe glyph even in the narrow viewport.
                _source = string.Join(' ', _source.ToCharArray());
                _referenceText = string.Join(' ', _referenceText.ToCharArray());
            }
            string sample = reference ? _referenceText : _source;
            if (kind.StartsWith("reader-", StringComparison.Ordinal) || kind.StartsWith("editor-", StringComparison.Ordinal) || kind == "writing-tools")
            {
                _run = new(sample); var span = new Span(_run); var paragraph = new Paragraph(span); var section = new Section(paragraph);
                _document = new(section) { PagePadding = new(0), FontFamily = _family, FontSize = 32, LineHeight = 44 };
                Css.SetStyle(_document, $"font-family:'{_family}';" + Body);
                Css.SetStyleSheet(_document, "Paragraph,Section,List,ListItem{margin:0;padding:0;border:0}");
                string scope = kind[(kind.IndexOf('-') + 1)..];
                _target = scope switch { "run" => _run, "span" => span, "paragraph" => paragraph, "section" => section, _ => _document };
                if (scope == "marker") { _run.Text = "B"; _document.Blocks.Clear(); section.Blocks.Remove(paragraph); _document.Blocks.Add(new Jalium.UI.Documents.List(new ListItem(paragraph))); }
                if (kind.StartsWith("editor-", StringComparison.Ordinal) || kind == "writing-tools")
                    View = new RichTextBox(_document) { Template = null, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent };
                else { _reader = new(); _reader.SetDocument(_document); View = _reader; }
                _parent = _run.Parent;
            }
            else if (kind == "split")
            {
                var root = new StackPanel(); _target = root; View = root; Css.SetStyleSheet(root, "TextBlock{display:inline}");
                foreach (string piece in new[] { sample[..4], sample[4..] }) { var text = new TextBlock { Text = piece, IsTextSelectionEnabled = true }; _texts.Add(text); root.Children.Add(text); }
            }
            else
            {
                var text = new TextBlock { Text = sample, IsTextSelectionEnabled = true }; _texts.Add(text); _target = text; View = text;
                Css.SetStyle(text, $"font-family:'{_family}';" + Body);
                if (kind is "inline-run" or "inline-span")
                {
                    text.Text = ""; _run = new(sample); var span = new Span(_run); text.Inlines.Add(span); _target = kind == "inline-run" ? _run : span; _parent = _run.Parent;
                }
                if (kind == "marker") text.Text = "B";
            }
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }
        internal void Change(bool changed)
        {
            if (_kind.EndsWith("marker", StringComparison.Ordinal))
            {
                string selector = _kind == "marker" ? "TextBlock" : "ListItem";
                string content = _reference ? _referenceText : _source;
                Css.SetStyleSheet(_document ?? (DependencyObject)_texts[0], $"{selector}::marker{{content:'{content}';font-variant-east-asian:{(changed && !_reference ? _variant : "normal")}}}Paragraph,Section,List,ListItem{{margin:0;padding:0;border:0}}");
                if (_kind == "marker") Css.SetStyle(_texts[0], $"font-family:'{_family}';" + Body + "display:list-item;list-style-position:inside");
                return;
            }
            string prefix = ReferenceEquals(_target, View) || ReferenceEquals(_target, _document) ? $"font-family:'{_family}';" + Body : "";
            if (_kind == "split") prefix += "display:block;";
            string value = changed && !_reference ? _variant : "normal";
            string low = _extra;
            if (_reference && changed && _kind == "cjk") low = "font-feature-settings:'jp04' 1,'fwid' 1,'ruby' 1;";
            Css.SetStyle(_target, prefix + "font-variant-east-asian:" + value + ";" + low);
        }
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                _reader?.SetViewportHeight(240); View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout();
            }
        }
        internal void AssertSources()
        {
            if (_run is not null && (!ReferenceEquals(_parent, _run.Parent) || _run.Text != (_kind.EndsWith("marker", StringComparison.Ordinal) ? "B" : _source))) throw new Exception("Variant changed source run or ownership");
            foreach (var text in _texts)
            {
                text.SelectAll(); if (text.SelectedText != text.Text) throw new Exception("Copy did not preserve source text"); text.Select(0, 0);
                foreach (var segment in text.GetSourceRangeSegments(0, text.Text.Length))
                    if (segment.EndX > segment.StartX && text.GetPositionFromPoint(new((segment.StartX + segment.EndX) / 2, segment.Baseline - 2), false) is null)
                        throw new Exception("Variant source range is not hit-testable");
            }
        }
        public void Dispose() { _reader?.Dispose(); Css.SetStyleSheets(_document ?? (DependencyObject)View, null); }
    }
}
