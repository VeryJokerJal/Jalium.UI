using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private static byte[] s_ligatureBytes = [];
    private static string s_ligatureFamily = "";
    private static (string Source, string Expected, string Before, string After)? LigatureReference(string kind) => kind switch
    {
        "wpt-common" => ("C", "A", "none", "common-ligatures"),
        "wpt-clig" => ("D", "A", "none", "common-ligatures"),
        "wpt-discretionary" => ("E", "A", "none", "discretionary-ligatures"),
        "wpt-historical" => ("F", "A", "none", "historical-ligatures"),
        "wpt-contextual" => ("G", "A", "none", "contextual"),
        "wpt-normal" => ("CDEFG", "AABBA", "none", "normal"),
        "wpt-none" => ("CDEFG", "BBBBB", "normal", "none"), _ => null,
    };

    internal static int RunFontLigatures()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        s_ligatureBytes = File.ReadAllBytes(Environment.GetEnvironmentVariable("JALIUM_CSS_NUMERIC_FONT") ??
            Path.Combine(Directory.GetCurrentDirectory(), "tests/Jalium.UI.MacOS.Tests/Assets/FontNumeric/FontWithFancyFeatures.otf"));
        using var resource = CssNativeFontResource.Create(s_ligatureBytes) ?? throw new Exception("Ligature fixture failed to load");
        s_ligatureFamily = resource.Family;
        string[] kinds = ["text", "inline-run", "inline-span", "reader-run", "reader-span", "reader-paragraph", "reader-section", "reader-root",
            "editor-run", "editor-span", "editor-paragraph", "editor-section", "editor-root", "marker", "reader-marker", "editor-marker",
            "kerning-auto", "kerning-normal", "kerning-none", "kerning-override", "ligature-override", "required",
            "transition-kerning", "transition-ligatures", "transition-numeric", "transition-features",
            "wpt-common", "wpt-clig", "wpt-discretionary", "wpt-historical", "wpt-contextual", "wpt-normal", "wpt-none"];
        var records = new List<object>(); int failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 170, 360 }) foreach (string kind in kinds)
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_LIGATURE_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; var label = $"{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested ligature engine unavailable");
                using var actual = new LigatureScene(kind, width, dpi, false); actual.Layout();
                byte[] Capture(LigatureScene scene) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => scene.View.Render(dc), () => true) ?? throw new Exception("Ligature readback failed"));
                var source = actual.Source; var parent = actual.Run?.Parent; var normal = Capture(actual);
                actual.Change(true); actual.Layout(); var changed = Capture(actual); var metrics = NumericMetrics(actual.View);
                using var fresh = new LigatureScene(kind, width, dpi, true, reference: true); fresh.Layout();
                var expected = Capture(fresh); var expectedMetrics = NumericMetrics(fresh.View);
                Check(changed.SequenceEqual(expected), "Feature output differs from independently authored fresh view");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(expectedMetrics), "Feature layout or IME geometry differs from fresh view");
                bool control = kind is "kerning-override" or "ligature-override" or "required";
                Check(normal.SequenceEqual(expected) == control, control ? "Explicit override or essential ligature changed glyphs" : "Feature has no observable glyph, spacing or wrapping effect");
                actual.Change(false); actual.Layout(); var restored = Capture(actual);
                Check(normal.SequenceEqual(restored), "Feature reset did not restore original pixels");
                Check(actual.Source == source && ReferenceEquals(actual.Run?.Parent, parent), "Feature change mutated source or ownership");
                foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                    Save(item.Item2, width * dpi, 240 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, control, changedDifferentBytes = changed.Zip(expected).Count(p => p.First != p.Second),
                    restoredDifferentBytes = normal.Zip(restored).Count(p => p.First != p.Second), metrics, expectedMetrics });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count > 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool passed, string error) { if (!passed) errors.Add(error); }
        }
        File.WriteAllText(Path.Combine(Output, "ligatures.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private sealed class LigatureScene : IDisposable
    {
        internal readonly FrameworkElement View;
        internal readonly Run? Run;
        private readonly FlowDocument? _document;
        private readonly FlowDocumentPresentation? _reader;
        private readonly TextBlock? _text;
        private readonly DependencyObject _target;
        private readonly string _kind, _body;
        private readonly int _width;
        private readonly bool _reference;
        private CssDiscreteAnimation? _transition;
        private AnimationClock? _transitionClock;
        private DependencyProperty? _transitionProperty;
        private object? _transitionFrom, _transitionTo;
        internal string Source => _document?.GetText() ?? Run?.Text ?? _text?.Text ?? "";

        internal LigatureScene(string kind, int width, int dpi, bool changed, bool reference = false)
        {
            _kind = kind; _width = width; _reference = reference;
            var test = LigatureReference(kind);
            string sample = test is { } t ? reference ? t.Expected : t.Source : kind == "required" ? "لا" : kind == "transition-numeric" ? "111111 111111" : "AVATAR office affinity";
            string family = test is not null ? s_ligatureFamily : "Seravek";
            _body = $"font-family:'{family}';font-size:32px;line-height:44px;color:#146342;background:#eef5f0;white-space:normal;";
            if (kind.StartsWith("reader-", StringComparison.Ordinal) || kind.StartsWith("editor-", StringComparison.Ordinal))
            {
                Run = new(sample); var span = new Span(Run); var paragraph = new Paragraph(span); var section = new Section(paragraph);
                _document = new(section) { PagePadding = new(0), FontSize = 32, LineHeight = 44 };
                Css.SetStyle(_document, _body); Css.SetStyleSheet(_document, "Paragraph,Section,List,ListItem{margin:0;padding:0;border:0}");
                string target = kind[(kind.IndexOf('-') + 1)..];
                _target = target switch { "run" => Run, "span" => span, "paragraph" => paragraph, "section" => section, _ => _document };
                if (target == "marker")
                { Run.Text = "正文"; _document.Blocks.Clear(); section.Blocks.Remove(paragraph); _document.Blocks.Add(new Jalium.UI.Documents.List(new ListItem(paragraph))); }
                if (kind.StartsWith("editor-", StringComparison.Ordinal)) View = new RichTextBox(_document) { Template = null, Padding = new(0), BorderThickness = new(0), Background = Brushes.White };
                else { _reader = new(); _reader.SetDocument(_document); View = _reader; }
            }
            else
            {
                _text = new() { Text = sample, FontSize = 32 }; View = _text; _target = _text;
                Css.SetStyle(_text, _body);
                if (kind is "inline-run" or "inline-span")
                { _text.Text = ""; Run = new(sample); var span = new Span(Run); _text.Inlines.Add(span); _target = kind == "inline-run" ? Run : span; }
                if (kind == "marker") _text.Text = "正文";
            }
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }

        internal void Change(bool changed)
        {
            string prefix = ReferenceEquals(_target, _document) || ReferenceEquals(_target, _text) ? _body : "";
            if (_kind.StartsWith("transition-", StringComparison.Ordinal))
            {
                var values = _kind switch
                {
                    "transition-kerning" => ("font-kerning", "normal", "none", "'kern' 0"),
                    "transition-ligatures" => ("font-variant-ligatures", "common-ligatures", "none", "'liga' 0,'clig' 0,'dlig' 0,'hlig' 0,'calt' 0"),
                    "transition-numeric" => ("font-variant-numeric", "proportional-nums", "tabular-nums", "'tnum' 1"),
                    _ => ("font-feature-settings", "normal", "'kern' 0,'liga' 0,'clig' 0", "'kern' 0,'liga' 0,'clig' 0"),
                };
                if (_reference) { Css.SetStyle(_target, prefix + "font-feature-settings:" + values.Item4); return; }
                if (_transition is null)
                {
                    _transitionProperty = CssPropertyRegistry.Lookup(values.Item1)?.StorageProperty
                        ?? throw new Exception("Transition font property unavailable");
                    Css.SetStyle(_target, prefix + values.Item1 + ":" + values.Item2); Evaluate();
                    _transitionFrom = _target.GetValue(_transitionProperty);
                    Css.SetStyle(_target, prefix + values.Item1 + ":" + values.Item3); Evaluate();
                    _transitionTo = _target.GetValue(_transitionProperty);
                    _transition = CssTransitions.Create((UIElement)_target, _transitionProperty, _transitionFrom, _transitionTo,
                        new(TimeSpan.FromSeconds(1), TimeSpan.Zero, new("linear"), true)) as CssDiscreteAnimation
                        ?? throw new Exception("Discrete font transition unavailable");
                    _transitionClock = _transition.CreateClock();
                }
                _transitionClock!.SetProgressForSampling(changed ? .5 : .49);
                ((UIElement)_target).SetCssAnimatedValue(_transitionProperty!,
                    _transition.GetCurrentValue(_transitionFrom!, _transitionTo!, _transitionClock));
                return;
                void Evaluate() { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(_target)); CssEvaluationScheduler.FlushIfPending(_target.Dispatcher); }
            }
            if (LigatureReference(_kind) is { } test)
            { Css.SetStyle(_target, prefix + "font-variant-ligatures:" + (_reference ? "normal" : changed ? test.After : test.Before)); return; }
            string declaration;
            if (_kind is "kerning-auto" or "kerning-normal" or "kerning-none")
            {
                string value = changed ? _kind[8..] : _kind == "kerning-none" ? "normal" : "none";
                declaration = _reference ? "font-feature-settings:'kern' " + (value == "none" ? 0 : 1) : "font-kerning:" + value;
            }
            else if (_kind == "kerning-override")
                declaration = _reference ? "font-feature-settings:'kern' 1" : "font-kerning:" + (changed ? "none" : "normal") + ";font-feature-settings:'kern' 1";
            else if (_kind == "ligature-override")
                declaration = (_reference ? "" : "font-variant-ligatures:" + (changed ? "none" : "common-ligatures") + ";") +
                    "font-feature-settings:'liga' 1,'clig' 1,'calt' 0,'dlig' 0,'hlig' 0";
            else if (_kind == "required")
                declaration = _reference ? "font-feature-settings:'liga' 0,'clig' 0,'dlig' 0,'hlig' 0,'calt' 0" : "font-variant-ligatures:" + (changed ? "none" : "normal");
            else
                declaration = _reference ? "font-feature-settings:'kern' 0,'liga' 0,'clig' 0" :
                    "font-kerning:" + (changed ? "none" : "normal") + ";font-variant-ligatures:" + (changed ? "no-common-ligatures" : "common-ligatures");
            if (_kind is "marker" or "reader-marker" or "editor-marker")
            {
                string selector = _kind == "marker" ? "TextBlock" : "ListItem";
                Css.SetStyleSheet(_document ?? (DependencyObject)_text!, $"{selector}::marker{{content:'AV office. ';{declaration}}}Paragraph,Section,List,ListItem{{margin:0;padding:0;border:0}}");
                if (_kind == "marker") Css.SetStyle(_text!, _body + "display:list-item;list-style-position:inside");
                return;
            }
            Css.SetStyle(_target, prefix + declaration);
        }

        internal void Layout()
        {
            for (int pass = 0; pass < 3; pass++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(View.Dispatcher);
                _reader?.SetViewportHeight(240); View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout();
            }
        }
        public void Dispose() { _reader?.Dispose(); Css.SetStyleSheets(_document ?? (DependencyObject)_text!, null); }
    }

}
