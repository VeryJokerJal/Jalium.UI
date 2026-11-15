using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    internal static int RunFontWidthAnimation()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failed = 0;
        string[] kinds = ["text", "run", "span", "shared", "marker", "shorthand", "reader-root", "reader-run", "reader-span", "reader-paragraph", "reader-section", "editor-root", "editor-run", "editor-span", "editor-paragraph", "editor-section"];
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 220, 420 }) foreach (string kind in kinds)
        {
            context.DefaultRenderingEngine = engine;
            using var scene = new WidthAnimationScene(kind, width, dpi); scene.Change(null); scene.Layout();
            byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 272 * dpi, dpi,
                dc => view.Render(dc), () => true) ?? throw new Exception("Font width readback failed"));
            byte[] normal = Capture(scene.View);
            foreach (double progress in new[] { 0d, .5, 1d })
            {
                string label = $"font-width-{engine}-{dpi}-{width}-{kind}-{progress.ToString(CultureInfo.InvariantCulture)}";
                var errors = new List<string>();
                try
                {
                    Check(context.DefaultRenderingEngine == engine, "Requested rendering engine unavailable");
                    scene.Change(progress); scene.Layout(); scene.AssertSource();
                    byte[] actual = Capture(scene.View); var metrics = EastAsianMetrics(scene.View);
                    using var reference = new WidthAnimationScene(kind, width, dpi);
                    reference.Change(null, 83.2 + 9.6 * progress); reference.Layout();
                    byte[] expected = Capture(reference.View); var expectedMetrics = EastAsianMetrics(reference.View);
                    Check(actual.SequenceEqual(expected), "Animated font width differs from a fresh static percentage");
                    Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(expectedMetrics), "Animated layout/source hit/IME geometry differs from static reference");
                    double computed = CssFontStretchValue.Computed(CssNode.Get(scene.Target));
                    Check(Math.Abs(computed - (83.2 + 9.6 * progress)) < 1e-6, "Computed width lost continuous percentage");
                    scene.Change(null); scene.Layout(); byte[] restored = Capture(scene.View);
                    Check(normal.SequenceEqual(restored), "Clearing animation did not restore the original view");
                    foreach (var item in new[] { ("actual", actual), ("expected", expected), ("restored", restored) }) Save(item.Item2, width * dpi, 272 * dpi, label + "-" + item.Item1 + ".png");
                    records.Add(new { label, errors, progress, computed, expectedComputed = 83.2 + 9.6 * progress, metrics, expectedMetrics,
                        differentBytes = actual.Zip(expected).Count(p => p.First != p.Second), changedFromStart = !normal.SequenceEqual(actual) });
                }
                catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
                if (errors.Count > 0) failed++;
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
                void Check(bool pass, string message) { if (!pass) errors.Add(message); }
            }
        }
        File.WriteAllText(Path.Combine(Output, "font-width-animation.json"), JsonSerializer.Serialize(new
        { count = records.Count, failed, records, nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private sealed class WidthAnimationScene : IDisposable
    {
        internal readonly FrameworkElement View;
        internal readonly DependencyObject Target;
        private readonly List<TextBlock> _texts = [];
        private readonly FlowDocument? _document;
        private readonly FlowDocumentPresentation? _reader;
        private readonly Run? _run;
        private readonly int _width;
        private readonly string _kind;
        private const string Source = "AVATAR office 012345 中文\nWidth changes retain source";
        private const string Body = "font-family:'SF Pro';font-size:26px;line-height:38px;color:#146342;white-space:pre-wrap;text-autospace:no-autospace;";
        internal WidthAnimationScene(string kind, int width, int dpi)
        {
            _kind = kind; _width = width;
            if (kind.StartsWith("reader-", StringComparison.Ordinal) || kind.StartsWith("editor-", StringComparison.Ordinal))
            {
                _run = new Run(Source); var span = new Span(_run); var paragraph = new Paragraph(span) { Margin = new(0) }; var section = new Section(paragraph);
                _document = new FlowDocument(section) { PagePadding = new(0), FontFamily = "SF Pro", FontSize = 26, LineHeight = 38 };
                Css.SetStyle(_document, Body);
                Target = kind.EndsWith("-run", StringComparison.Ordinal) ? _run : kind.EndsWith("-span", StringComparison.Ordinal) ? span
                    : kind.EndsWith("-paragraph", StringComparison.Ordinal) ? paragraph : kind.EndsWith("-section", StringComparison.Ordinal) ? section : _document;
                if (kind.StartsWith("reader-", StringComparison.Ordinal))
                {
                    _reader = new FlowDocumentPresentation(); _reader.SetDocument(_document); View = _reader;
                }
                else View = new RichTextBox(_document) { Padding = new(0), BorderThickness = new(0) };
            }
            else if (kind == "shared")
            {
                var panel = new StackPanel();
                foreach (string part in new[] { "AVATAR office ", "012345 中文", " Width changes retain source" })
                {
                    var text = new TextBlock { Text = part }; _texts.Add(text); panel.Children.Add(text); Css.SetStyle(text, "display:inline");
                }
                Css.SetStyle(panel, Body + "display:block"); View = panel; Target = panel;
            }
            else
            {
                var text = new TextBlock { Text = Source }; _texts.Add(text); View = text; Target = text;
                if (kind is "run" or "span")
                {
                    text.Text = ""; _run = new Run(Source);
                    if (kind == "run") { text.Inlines.Add(_run); Target = _run; }
                    else { var span = new Span(_run); text.Inlines.Add(span); Target = span; }
                }
                Css.SetStyle(text, Body + (kind == "marker" ? "display:list-item;list-style-type:decimal;list-style-position:inside;" : ""));
            }
            View.SetRootDpi(new(dpi, dpi));
            Css.SetStyleSheet(_document ?? (DependencyObject)View, "@keyframes width{from{font-width:83.2%}to{font-stretch:92.8%}}");
        }
        internal void Change(double? progress, double literal = 83.2)
        {
            string number = literal.ToString("R", CultureInfo.InvariantCulture);
            bool root = ReferenceEquals(Target, View) || ReferenceEquals(Target, _document);
            string css = root ? Body : "";
            if (_kind == "shared") css += "display:block;";
            if (_kind == "marker") css += "display:list-item;list-style-type:decimal;list-style-position:inside;";
            if (_kind == "shorthand") css += "font:26px/calc(1em + 12px) 'SF Pro';";
            css += "font-width:" + number + "%;";
            css += progress is double p ? "animation:width 100s linear both paused;animation-delay:" + (-p * 100).ToString("R", CultureInfo.InvariantCulture) + "s;" : "animation:none;";
            Css.SetStyle(Target, css);
        }
        internal void Layout()
        {
            for (int i = 0; i < 4; i++)
            {
                CssEvaluationScheduler.FlushIfPending(View.Dispatcher);
                _reader?.SetViewportHeight(272);
                View.Measure(new(_width, 272)); View.Arrange(new(0, 0, _width, 272)); View.UpdateLayout();
            }
            if (!View.IsMeasureValid) throw new Exception("Width animation left an invalid layout");
        }
        internal void AssertSource()
        {
            if (_document is not null && _document.GetText() != Source + "\n") throw new Exception("Width animation edited document source");
            if (_run is not null && _run.Text != Source) throw new Exception("Width animation edited run source");
            if (_document is null && _run is null && string.Concat(_texts.Select(t => t.Text)) != (_kind == "shared" ? "AVATAR office 012345 中文 Width changes retain source" : Source))
                throw new Exception("Width animation edited source text");
        }
        public void Dispose()
        {
            Css.SetStyle(Target, ""); Css.SetStyleSheets(View, null);
            if (_document is not null) { Css.SetStyle(_document, ""); Css.SetStyleSheets(_document, null); }
            CssEvaluationScheduler.FlushIfPending(View.Dispatcher);
            if (View is RichTextBox editor) editor.Document = new();
            _reader?.Dispose();
        }
    }
}
