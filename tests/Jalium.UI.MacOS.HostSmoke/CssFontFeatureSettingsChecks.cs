using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private static byte[] s_featureSettingsBytes = [];
    private static string s_featureSettingsFamily = "";
    private static (string Tag, string Source, int Value)? FeatureReference(string kind) => kind switch
    {
        "feature-lining" => ("lnum", "P", 1), "feature-oldstyle" => ("onum", "Q", 1),
        "feature-proportional" => ("pnum", "R", 1), "feature-tabular" => ("tnum", "S", 1),
        "feature-fraction" => ("frac", "T", 1), "feature-stacked" => ("afrc", "U", 1),
        "feature-ordinal" => ("ordn", "V", 1), "feature-zero" => ("zero", "W", 1),
        "alternate-one" => ("salt", "n", 1), "alternate-two" => ("salt", "o", 2),
        "alternate-three" => ("salt", "p", 3), _ => null,
    };

    internal static int RunFontFeatureSettings()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        s_featureSettingsBytes = File.ReadAllBytes(Environment.GetEnvironmentVariable("JALIUM_CSS_NUMERIC_FONT") ??
            Path.Combine(Directory.GetCurrentDirectory(), "tests/Jalium.UI.MacOS.Tests/Assets/FontNumeric/FontWithFancyFeatures.otf"));
        using var resource = CssNativeFontResource.Create(s_featureSettingsBytes) ?? throw new Exception("Feature fixture failed to load");
        s_featureSettingsFamily = resource.Family;
        string[] kinds = ["text", "inline-run", "inline-span", "reader-run", "reader-span", "reader-paragraph", "reader-section", "reader-root",
            "editor-run", "editor-span", "editor-paragraph", "editor-section", "editor-root", "ligature", "kerning", "marker", "reader-marker", "editor-marker",
            "feature-lining", "feature-oldstyle", "feature-proportional", "feature-tabular", "feature-fraction", "feature-stacked", "feature-ordinal", "feature-zero",
            "alternate-one", "alternate-two", "alternate-three", "face-default", "face-property", "face-variant", "face-ranges", "face-fallback", "unknown", "tag-case"];
        var records = new List<object>(); int failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 170, 360 }) foreach (string kind in kinds)
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_FEATURE_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; var label = $"{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested feature engine unavailable");
                using var actual = new FeatureSettingsScene(kind, width, dpi, false); actual.Layout();
                byte[] Capture(FeatureSettingsScene scene) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => scene.View.Render(dc), () => true) ?? throw new Exception("Feature readback failed"));
                var source = actual.Source; var parent = actual.Run?.Parent; var normal = Capture(actual);
                actual.Change(true); actual.Layout(); var changed = Capture(actual); var metrics = NumericMetrics(actual.View);
                using var fresh = new FeatureSettingsScene(kind, width, dpi, true, reference: true); fresh.Layout();
                var expected = Capture(fresh); var expectedMetrics = NumericMetrics(fresh.View);
                Check(changed.SequenceEqual(expected), "Feature output differs from independently authored fresh view");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(expectedMetrics), "Feature layout or IME geometry differs from fresh view");
                bool control = kind is "unknown" or "tag-case";
                Check(normal.SequenceEqual(expected) == control, control ? "Unknown tag changed glyphs" : "Feature has no observable glyph, spacing or wrapping effect");
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
        File.WriteAllText(Path.Combine(Output, "features.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private sealed class FeatureSettingsScene : IDisposable
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
        private bool _waited;
        internal string Source => _document?.GetText() ?? Run?.Text ?? _text?.Text ?? "";

        internal FeatureSettingsScene(string kind, int width, int dpi, bool changed, bool reference = false)
        {
            _kind = kind; _width = width; _reference = reference;
            var test = FeatureReference(kind); bool face = kind.StartsWith("face-", StringComparison.Ordinal);
            string sample = test is { } t ? reference ? "A" : t.Source : face ? reference ? kind == "face-ranges" ? "AA" : "A" : kind == "face-ranges" ? "TA" : "T"
                : kind == "tag-case" ? "T" : kind == "ligature" ? "office affinity" : kind == "kerning" ? "AVATAR To WA" : "111111 111111";
            string family = test is not null || face || kind == "tag-case" ? s_featureSettingsFamily : "Seravek";
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
            if (FeatureReference(_kind) is { } test)
            { Css.SetStyle(_target, prefix + "font-feature-settings:" + (_reference ? "normal" : $"'{test.Tag}' {(changed ? test.Value : 0)}") + ";"); return; }
            if (_kind.StartsWith("face-", StringComparison.Ordinal) && !_reference)
            {
                bool fallback = _kind == "face-fallback";
                string range = _kind == "face-ranges" ? "unicode-range:U+0054;" : fallback ? "unicode-range:U+0030-0039;" : "";
                int descriptor = _kind == "face-default" ? changed ? 1 : 0 : _kind == "face-variant" ? 0 : 1;
                string rules = $"@font-face{{font-family:FeatureFixture;src:url('fancy.otf');{range}font-feature-settings:'frac' {descriptor};font-display:swap}}";
                if (_kind == "face-ranges") rules += "@font-face{font-family:FeatureFixture;src:url('fancy.otf');unicode-range:U+0041;font-feature-settings:'frac' 0}";
                Css.SetStyleSheets(_text!, null);
                Css.GetStyleSheets(_text!).Add(CssStyleSheet.Parse(rules, "feature fixture", new Uri("https://feature.invalid/"), new FeatureFixtureResolver()));
                string property = _kind is "face-property" or "face-ranges" ? changed ? "normal" : "'frac' 0" : fallback ? changed ? "'frac' 1" : "normal" : "normal";
                string variant = _kind == "face-variant" && changed ? "diagonal-fractions" : "normal";
                Css.SetStyle(_text!, _body + $"font-family:FeatureFixture,'{s_featureSettingsFamily}';font-feature-settings:{property};font-variant-numeric:{variant}");
                _waited = false; return;
            }
            if (_kind.StartsWith("face-", StringComparison.Ordinal)) { Css.SetStyle(_target, _body + "font-feature-settings:normal"); return; }
            string value = _kind switch { "ligature" => changed ? "'liga' off" : "'liga' on", "kerning" => changed ? "'kern' off" : "'kern' on",
                "unknown" => changed ? "'XXXX' 2" : "normal", "tag-case" => changed ? "'FRAC' on" : "normal", _ => changed ? "'tnum' 1" : "'pnum' 1" };
            if (_kind is "marker" or "reader-marker" or "editor-marker")
            {
                string selector = _kind == "marker" ? "TextBlock" : "ListItem";
                Css.SetStyleSheet(_document ?? (DependencyObject)_text!, $"{selector}::marker{{content:'111111. ';font-variant-numeric:normal;font-feature-settings:{value}}}Paragraph,Section,List,ListItem{{margin:0;padding:0;border:0}}");
                if (_kind == "marker") Css.SetStyle(_text!, _body + "display:list-item;list-style-position:inside");
                return;
            }
            Css.SetStyle(_target, prefix + "font-feature-settings:" + value);
        }

        internal void Layout()
        {
            for (int pass = 0; pass < 3; pass++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(View.Dispatcher);
                _reader?.SetViewportHeight(240); View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout();
                if (!_waited && _kind.StartsWith("face-", StringComparison.Ordinal) && !_reference)
                {
                    Css.WaitForFontsAsync(_text!).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                    CssFontFaces.FlushNotifications(View.Dispatcher); _waited = true;
                    if (Css.GetFontLoadErrors(_text!).Count > 0) throw new Exception(string.Join(';', Css.GetFontLoadErrors(_text!)));
                }
            }
        }
        public void Dispose() { _reader?.Dispose(); Css.SetStyleSheets(_document ?? (DependencyObject)_text!, null); }
    }

    private sealed class FeatureFixtureResolver : ICssResourceResolver
    {
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CssResource(uri, new MemoryStream(s_featureSettingsBytes), "font/otf"));
    }
}
