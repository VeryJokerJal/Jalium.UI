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
    private static string s_capsFamily = "";
    private static readonly (string Keyword, string Expected)[] CapsCases =
    [
        ("normal", "BBBBBB"),
        ("small-caps", "ABBBBB"),
        ("all-small-caps", "AABBBB"),
        ("petite-caps", "BBABBB"),
        ("all-petite-caps", "BBAABB"),
        ("unicase", "BBBBAB"),
        ("titling-caps", "BBBBBA"),
    ];

    internal static int RunFontCaps()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string fixture = Environment.GetEnvironmentVariable("JALIUM_CSS_CAPS_FONT") ??
            Path.Combine(Directory.GetCurrentDirectory(), "tests/Jalium.UI.MacOS.Tests/Assets/FontNumeric/FontWithFancyFeatures.otf");
        using var font = CssNativeFontResource.Create(File.ReadAllBytes(fixture)) ?? throw new Exception("WPT feature font could not load");
        s_capsFamily = font.Family;
        var records = new List<object>(); int failed = 0;
        string[] cases = [.. CapsCases.Select(f => f.Keyword), "shorthand", "override", "inline-run", "inline-span", "split",
            "reader-run", "reader-span", "reader-paragraph", "reader-section", "reader-root", "reader-marker",
            "editor-run", "editor-span", "editor-paragraph", "editor-section", "editor-root", "editor-marker", "marker", "writing-tools", "natural", "unsupported"];
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 220, 420 }) foreach (string kind in cases)
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_CAPS_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; string label = $"caps-{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested renderer unavailable");
                byte[] Capture(FrameworkElement view)
                {
                    Check(context.Backend == RenderBackend.Metal && ReferenceEquals(RenderContext.Current, context), "Requested backend is not current");
                    var bitmap = MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                        dc => view.Render(dc), () => true) ?? throw new Exception("Caps readback failed");
                    Check(context.Backend == RenderBackend.Metal && ReferenceEquals(RenderContext.Current, context), "Readback switched backend");
                    Check(context.DefaultRenderingEngine == engine, "Readback switched renderer");
                    return Pixels(bitmap);
                }
                using var scene = new CapsScene(kind, width, dpi, false); scene.Layout(); var normal = Capture(scene.View);
                scene.Change(true); scene.Layout(); var changed = Capture(scene.View); scene.AssertSources(); var metrics = CapsMetrics(scene.View);
                using var fresh = new CapsScene(kind, width, dpi, true, reference: true); fresh.Layout(); var expected = Capture(fresh.View);
                Check(changed.SequenceEqual(expected), "Changed view differs from independent WPT glyph/native feature reference");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(CapsMetrics(fresh.View)), "Changed layout/source hit/IME geometry differs from reference");
                if (kind is not ("normal" or "unsupported")) Check(!normal.SequenceEqual(changed), "Requested feature has no visible effect in WPT font");
                if (kind == "unsupported") Check(normal.SequenceEqual(changed), "Unavailable feature changed ordinary glyphs");
                if (kind == "writing-tools")
                {
                    var editor = (RichTextBox)scene.View;
                    var source = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                    try
                    {
                        MacOSWritingToolsPresentation.Prepare(editor, source, "caps-glyph", 0, 1, 0, () => true);
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
                records.Add(new { label, backend = context.Backend.ToString(), engine = context.DefaultRenderingEngine.ToString(), errors, metrics, differentBytes = changed.Zip(expected).Count(p => p.First != p.Second),
                    glyphsChanged = !normal.SequenceEqual(changed) });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count != 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool value, string message) { if (!value) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "caps.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private static object CapsMetrics(FrameworkElement root)
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

    private sealed class CapsScene : IDisposable
    {
        internal readonly FrameworkElement View;
        private readonly List<TextBlock> _texts = [];
        private readonly Run? _run;
        private readonly object? _parent;
        private readonly FlowDocument? _document;
        private readonly FlowDocumentPresentation? _reader;
        private readonly DependencyObject _target;
        private readonly string _kind, _source, _variant, _referenceText, _extra, _family, _familyCss, _fixtureCss;
        private readonly int _width;
        private readonly bool _reference;
        private const string Body = "font-size:32px;line-height:44px;color:#146342;white-space:pre-wrap;text-autospace:no-autospace;";
        internal CapsScene(string kind, int width, int dpi, bool changed, bool reference = false, CapsFixtureOptions? options = null)
        {
            _kind = kind; _width = width; _reference = reference;
            _variant = "all-small-caps"; _referenceText = "AABBBB";
            int feature = Array.FindIndex(CapsCases, f => f.Keyword == kind);
            if (feature >= 0) { _variant = CapsCases[feature].Keyword; _referenceText = CapsCases[feature].Expected; }
            _extra = kind == "override" ? "font-feature-settings:'smcp' off;" : "";
            if (kind == "override") _referenceText = "BABBBB";
            if (kind == "shorthand") { _variant = "small-caps"; _referenceText = "ABBBBB"; }
            _family = kind == "natural" ? "Seravek" : kind == "unsupported" ? "Arial" : s_capsFamily;
            _source = kind == "natural" ? "Jalium Aa fi AV 中文 2026" : kind == "unsupported" ? "ABC 123" : "JKLMNO";
            if (kind == "unsupported") _variant = "titling-caps";
            if (kind == "writing-tools") { _source = "J"; _variant = "small-caps"; _referenceText = "A"; }
            if (kind is "natural" or "unsupported") _referenceText = _source;
            else if (kind != "writing-tools")
            {
                // Spaces expose every WPT probe glyph even in the narrow viewport.
                _source = string.Join(' ', _source.ToCharArray());
                _referenceText = string.Join(' ', _referenceText.ToCharArray());
            }
            if (options is not null)
            {
                _variant = options.Variant; _source = options.Source; _referenceText = options.Expected;
                _extra = reference ? options.ReferenceCss : options.Css;
            }
            _familyCss = options?.FamilyCss ?? $"'{_family}'";
            _fixtureCss = options?.StyleSheet ?? "";
            string sample = reference ? _referenceText : _source;
            if (kind.StartsWith("reader-", StringComparison.Ordinal) || kind.StartsWith("editor-", StringComparison.Ordinal) || kind == "writing-tools")
            {
                _run = new(sample); var span = new Span(_run); var paragraph = new Paragraph(span); var section = new Section(paragraph);
                _document = new(section) { PagePadding = new(0), FontFamily = _family, FontSize = 32, LineHeight = 44 };
                if (options is not null) _document.ClearValue(TextElement.FontFamilyProperty);
                Css.SetStyle(_document, $"font-family:{_familyCss};" + Body);
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
                foreach (string piece in new[] { sample[..Math.Min(4, sample.Length)], sample[Math.Min(4, sample.Length)..] }) { var text = new TextBlock { Text = piece, IsTextSelectionEnabled = true }; _texts.Add(text); root.Children.Add(text); }
            }
            else
            {
                var text = new TextBlock { Text = sample, IsTextSelectionEnabled = true }; _texts.Add(text); _target = text; View = text;
                Css.SetStyle(text, $"font-family:{_familyCss};" + Body);
                if (kind is "inline-run" or "inline-span")
                {
                    text.Text = ""; _run = new(sample); var span = new Span(_run); text.Inlines.Add(span); _target = kind == "inline-run" ? _run : span; _parent = _run.Parent;
                }
                if (kind == "marker") text.Text = "B";
            }
            if (_fixtureCss.Length > 0)
                Css.SetStyleSheet(_document ?? (DependencyObject)View, _fixtureCss +
                    (kind == "split" ? "TextBlock{display:inline}" : "") + "Paragraph,Section,List,ListItem{margin:0;padding:0;border:0}");
            if (options?.Language is { } language)
                (_document ?? (DependencyObject)View).SetValue(FrameworkElement.LanguageProperty, Jalium.UI.Markup.XmlLanguage.GetLanguage(language));
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }
        internal void Change(bool changed)
        {
            if (_kind.EndsWith("marker", StringComparison.Ordinal))
            {
                string selector = _kind == "marker" ? "TextBlock" : "ListItem";
                string content = _reference ? _referenceText : _source;
                Css.SetStyleSheet(_document ?? (DependencyObject)_texts[0], _fixtureCss + $"{selector}::marker{{content:'{content}';font-variant-caps:{(changed && !_reference ? _variant : "normal")};{_extra}}}Paragraph,Section,List,ListItem{{margin:0;padding:0;border:0}}");
                if (_kind == "marker") Css.SetStyle(_texts[0], $"font-family:{_familyCss};" + Body + "display:list-item;list-style-position:inside");
                return;
            }
            string prefix = ReferenceEquals(_target, View) || ReferenceEquals(_target, _document) ? $"font-family:{_familyCss};" + Body : "";
            if (_kind == "split") prefix += "display:block;";
            string value = changed && !_reference ? _variant : "normal";
            string low = _extra;
            if (_reference && changed && _kind == "natural") low = "font-feature-settings:'smcp' 1,'c2sc' 1;";
            if (_kind == "shorthand")
            {
                Css.SetStyle(_target, prefix + $"font:{(changed && !_reference ? "small-caps " : "")}32px/44px {_familyCss};");
                return;
            }
            Css.SetStyle(_target, prefix + "font-variant-caps:" + value + ";" + low);
        }
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                _reader?.SetViewportHeight(240); View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout();
                // Local @font-face resolution can complete after the first
                // layout. A baseline taken during font-display:block is not
                // the ordinary-font image used by the reset assertion.
                var ready = Css.WaitForFontsAsync(_document ?? (DependencyObject)View);
                long deadline = Environment.TickCount64 + 5000;
                while (!ready.IsCompleted && Environment.TickCount64 < deadline) {
                    Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                    Thread.Sleep(1);
                }
                if (!ready.IsCompleted) throw new TimeoutException("Caps fixture font loading did not finish");
                ready.GetAwaiter().GetResult();
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
