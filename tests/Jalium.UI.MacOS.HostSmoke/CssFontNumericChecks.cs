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
    private static string s_numericFeatureFamily = "";

    private static (string Keyword, string Source)? WptNumeric(string kind) => kind switch
    {
        "wpt-lining" => ("lining-nums", "P"), "wpt-oldstyle" => ("oldstyle-nums", "Q"),
        "wpt-proportional" => ("proportional-nums", "R"), "wpt-tabular" => ("tabular-nums", "S"),
        "wpt-fraction" => ("diagonal-fractions", "T"), "wpt-stacked" => ("stacked-fractions", "U"),
        "wpt-ordinal" => ("ordinal", "V"), "zero" => ("slashed-zero", "W"), _ => null,
    };

    internal static int RunFontNumeric()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var fixture = Environment.GetEnvironmentVariable("JALIUM_CSS_NUMERIC_FONT") ??
            Path.Combine(Directory.GetCurrentDirectory(), "tests/Jalium.UI.MacOS.Tests/Assets/FontNumeric/FontWithFancyFeatures.otf");
        using var featureFont = CssNativeFontResource.Create(File.ReadAllBytes(fixture)) ?? throw new Exception("WPT numeric font could not load");
        s_numericFeatureFamily = featureFont.Family;
        var records = new List<object>(); int failures = 0;
        string[] cases = ["text", "inline-run", "inline-span", "reader-run", "reader-span", "reader-paragraph", "reader-section", "reader-root",
            "editor-run", "editor-span", "editor-paragraph", "editor-section", "editor-root", "fraction", "ordinal", "zero", "unsupported-afrc",
            "marker", "reader-marker", "editor-marker", "wpt-lining", "wpt-oldstyle", "wpt-proportional", "wpt-tabular",
            "wpt-fraction", "wpt-stacked", "wpt-ordinal"];
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 })
        foreach (int width in new[] { 220, 360 })
        foreach (string kind in cases)
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_NUMERIC_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; var label = $"{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested rendering engine unavailable");
                using var actual = new NumericScene(kind, width, dpi, false); actual.Layout();
                byte[] Capture(NumericScene scene) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi,
                    dc => scene.View.Render(dc), () => true) ?? throw new Exception("Native numeric readback failed"));
                var source = actual.Source; var parent = actual.Run?.Parent; var normal = Capture(actual);
                actual.Change(true); actual.Layout(); var changed = Capture(actual); var metrics = NumericMetrics(actual.View);
                using var fresh = new NumericScene(kind, width, dpi, true, reference: true); fresh.Layout(); var expected = Capture(fresh); var expectedMetrics = NumericMetrics(fresh.View);
                Check(changed.SequenceEqual(expected), "Changed numeric view differs from independently authored fresh view");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(expectedMetrics), "Changed numeric layout/hit geometry differs from fresh view");
                if (kind != "unsupported-afrc") Check(!normal.SequenceEqual(expected), "Requested numeric feature produced no observable glyph or spacing change");
                else Check(normal.SequenceEqual(expected), "Unsupported font feature changed ordinary glyphs");
                actual.Change(false); actual.Layout(); var restored = Capture(actual);
                Check(normal.SequenceEqual(restored), "Numeric reset did not restore original pixels");
                Check(actual.Source == source, "Numeric feature changed source text");
                if (actual.Run is { } run) Check(ReferenceEquals(parent, run.Parent), "Numeric feature reparented source run");
                foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                    Save(item.Item2, width * dpi, 240 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, requiresDifference = kind != "unsupported-afrc",
                    changedDifferentBytes = changed.Zip(expected).Count(p => p.First != p.Second),
                    restoredDifferentBytes = normal.Zip(restored).Count(p => p.First != p.Second), metrics, expectedMetrics });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count > 0) failures++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join("; ", errors));
            void Check(bool passed, string error) { if (!passed) errors.Add(error); }
        }
        File.WriteAllText(Path.Combine(Output, "numeric.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failures == 0 ? 0 : 1;
    }

    private static object[] NumericMetrics(FrameworkElement root)
    {
        var result = new List<object>(); Walk(root);
        if (root is RichTextBox editor && editor is IImeSupport ime && ime.TryGetImeTextRangeGeometry(0, editor.Document.GetText().Length, false, out var geometry))
            result.Add(new { geometry.Rectangle, geometry.Start, geometry.Length });
        return result.ToArray();
        void Walk(Visual current)
        {
            if (current is FrameworkElement view)
            {
                var offset = view.TransformToAncestor(root);
                result.Add(new { type = view.GetType().Name, x = Finite(offset.X), y = Finite(offset.Y), width = Finite(view.RenderSize.Width),
                    height = Finite(view.RenderSize.Height), desiredWidth = Finite(view.DesiredSize.Width), desiredHeight = Finite(view.DesiredSize.Height) });
            }
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                if (VisualTreeHelper.GetChild(current, index) is Visual child) Walk(child);
        }
        static double? Finite(double value) => double.IsFinite(value) ? value : null;
    }

    private sealed class NumericScene : IDisposable
    {
        internal readonly FrameworkElement View;
        internal readonly Run? Run;
        private readonly FlowDocument? _document;
        private readonly FlowDocumentPresentation? _reader;
        private readonly TextBlock? _text;
        private readonly DependencyObject _target;
        private readonly string _kind;
        private readonly int _width;
        private readonly bool _reference;
        internal string Source => _document?.GetText() ?? Run?.Text ?? _text?.Text ?? "";
        internal NumericScene(string kind, int width, int dpi, bool changed, bool reference = false)
        {
            _kind = kind; _width = width; _reference = reference;
            var wpt = WptNumeric(kind);
            string sample = wpt is { } test ? reference ? "A" : test.Source :
                kind == "fraction" ? "1/2 3/4 5/8" : kind == "ordinal" ? "1st 2nd 3rd" : "111111 888888";
            string family = wpt is not null ? s_numericFeatureFamily : "Seravek";
            const string body = "font-size:32px;line-height:44px;color:#146342;background:#eef5f0;white-space:pre-wrap;";
            if (kind.StartsWith("reader-", StringComparison.Ordinal) || kind.StartsWith("editor-", StringComparison.Ordinal))
            {
                Run = new(sample); var span = new Span(Run); var paragraph = new Paragraph(span); var section = new Section(paragraph);
                _document = new(section) { PagePadding = new(0), FontFamily = family, FontSize = 32, LineHeight = 44 };
                Css.SetStyle(_document, $"font-family:'{family}';" + body);
                Css.SetStyleSheet(_document, "Paragraph,Section,List,ListItem{margin:0;padding:0;border:0;}");
                string target = kind[(kind.IndexOf('-') + 1)..];
                _target = target switch { "run" => Run, "span" => span, "paragraph" => paragraph, "section" => section, _ => _document };
                if (target == "marker")
                {
                    Run.Text = "正文"; _document.Blocks.Clear(); section.Blocks.Remove(paragraph);
                    _document.Blocks.Add(new Jalium.UI.Documents.List(new ListItem(paragraph)));
                }
                if (kind.StartsWith("editor-", StringComparison.Ordinal)) View = new RichTextBox(_document) { Template = null,
                    Padding = new(0), BorderThickness = new(0), Background = Brushes.White };
                else { _reader = new(); _reader.SetDocument(_document); View = _reader; }
            }
            else
            {
                _text = new() { Text = sample, FontFamily = family, FontSize = 32, Foreground = new SolidColorBrush(Color.FromRgb(20, 99, 66)) };
                Css.SetStyle(_text, $"font-family:'{family}';" + body); _target = _text; View = _text;
                if (kind is "inline-run" or "inline-span")
                {
                    _text.Text = ""; Run = new(sample); var span = new Span(Run); _text.Inlines.Add(span);
                    _target = kind == "inline-run" ? Run : span;
                }
                if (kind == "marker") _text.Text = "正文";
            }
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }

        internal void Change(bool changed)
        {
            if (WptNumeric(_kind) is { } test)
            {
                Css.SetStyle(_target, $"font-family:'{s_numericFeatureFamily}';font-size:32px;line-height:44px;color:#146342;background:#eef5f0;white-space:pre-wrap;" +
                    "font-variant-numeric:" + (changed && !_reference ? test.Keyword : "normal") + ";");
                return;
            }
            var value = changed ? _kind switch { "fraction" => "diagonal-fractions", "ordinal" => "ordinal", "zero" => "slashed-zero",
                "unsupported-afrc" => "stacked-fractions", _ => "oldstyle-nums tabular-nums" } : "lining-nums proportional-nums";
            if (_kind == "unsupported-afrc" && !changed) value = "normal";
            if (_kind is "marker" or "reader-marker" or "editor-marker")
            {
                string selector = _kind == "marker" ? "TextBlock" : "ListItem";
                Css.SetStyleSheet(_document ?? (DependencyObject)_text!, $"{selector}::marker{{content:'111111. ';font-variant-numeric:{value};}}Paragraph,Section,List,ListItem{{margin:0;padding:0;border:0;}}");
                if (_kind == "marker") Css.SetStyle(_text!, "font-family:Seravek;font-size:32px;line-height:44px;color:#146342;display:list-item;list-style-position:inside;");
                return;
            }
            string prefix = ReferenceEquals(_target, _document) || ReferenceEquals(_target, _text)
                ? $"font-family:'{(_kind == "zero" ? "Trattatello" : "Seravek")}';font-size:32px;line-height:44px;color:#146342;background:#eef5f0;white-space:pre-wrap;" : "";
            Css.SetStyle(_target, prefix + "font-variant-numeric:" + value + ";");
        }

        internal void Layout()
        {
            for (int index = 0; index < 3; index++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                _reader?.SetViewportHeight(240); View.Measure(new(_width, 240)); View.Arrange(new(0, 0, _width, 240)); View.UpdateLayout();
            }
        }
        public void Dispose() => _reader?.Dispose();
    }
}
