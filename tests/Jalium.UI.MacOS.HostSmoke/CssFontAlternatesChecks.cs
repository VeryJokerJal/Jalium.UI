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
    private static string s_alternatesFamily = "";
    private sealed record AlternateCase(string Name, string Value, string Css, string Expected);
    private static readonly AlternateCase[] AlternateCases =
    [
        new("wpt-01", "normal", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: normal;\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n\n", "BBBBBBBBBBBBBBBBBBBB"),
        new("wpt-02", "historical-forms", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: historical-forms;\n  }\n  .low {\n   font-feature-settings: \"hist\" on, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "ABBBBBBBBBBBBBBBBBBB"),
        new("wpt-03", "stylistic(foo)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @stylistic {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n\t  font-variant-alternates: character-variant(doesnt-exist) styleset(doesnt-exist);\n  }\n  .high {\n\t font-variant-alternates: stylistic(foo);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 01, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BABBBBBBBBBBBBBBBBBB"),
        new("wpt-04", "stylistic(bar)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @stylistic {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: stylistic(bar);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 02, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBABBBBBBBBBBBBBBBBB"),
        new("wpt-05", "stylistic(baz)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @stylistic {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: stylistic(baz);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 03, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBABBBBBBBBBBBBBBBB"),
        new("wpt-06", "styleset(foo)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @styleset {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n\t  font-variant-alternates: styleset(doesnt-exist);\n  }\n  .high {\n\t font-variant-alternates: styleset(foo);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" on, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBABBBBBBBBBBBBBBB"),
        new("wpt-07", "styleset(bar)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @styleset {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: styleset(bar);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" on, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBBABBBBBBBBBBBBBB"),
        new("wpt-08", "styleset(baz)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @styleset {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: styleset(baz);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" on,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBBBABBBBBBBBBBBBB"),
        new("wpt-09", "character-variant(foo)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @character-variant {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n\t  font-variant-alternates: character-variant(doesnt-exist);\n  }\n  .high {\n\t font-variant-alternates: character-variant(foo);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" on, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBBBBABBBBBBBBBBBB"),
        new("wpt-10", "character-variant(bar)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @character-variant {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: character-variant(bar);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" on, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBBBBBABBBBBBBBBBB"),
        new("wpt-11", "character-variant(baz)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @character-variant {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: character-variant(baz);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" on,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBBBBBBABBBBBBBBBB"),
        new("wpt-12", "swash(foo)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @swash {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n\t  font-variant-alternates: swash(doesnt-exist);\n  }\n  .high {\n\t font-variant-alternates: swash(foo);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 01, \"cswh\" 01, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBBBBBBBABBABBBBBB"),
        new("wpt-13", "swash(bar)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @swash {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: swash(bar);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 02, \"cswh\" 02, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBBBBBBBBABBABBBBB"),
        new("wpt-14", "swash(baz)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @swash {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: swash(baz);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 03, \"cswh\" 03, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBBBBBBBBBABBABBBB"),
        new("wpt-15", "ornaments(foo)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @ornaments {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n\t  font-variant-alternates: ornaments(doesnt-exist);\n  }\n  .high {\n\t font-variant-alternates: ornaments(foo);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 01, \"nalt\" 00;\n  }\n", "BBBBBBBBBBBBBBBBABBB"),
        new("wpt-16", "ornaments(bar)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @ornaments {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: ornaments(bar);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 02, \"nalt\" 00;\n  }\n", "BBBBBBBBBBBBBBBBBABB"),
        new("wpt-17", "ornaments(baz)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @ornaments {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: ornaments(baz);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 03, \"nalt\" 00;\n  }\n", "BBBBBBBBBBBBBBBBBBAB"),
        new("wpt-18", "annotation(foo)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @annotation {\n      foo: 1;\n      bar: 2;\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n\t  font-variant-alternates: annotation(doesnt-exist);\n  }\n  .high {\n\t font-variant-alternates: annotation(foo);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" off, \"ss02\" off, \"ss03\" off,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 01;\n  }\n", "BBBBBBBBBBBBBBBBBBBA"),
        new("wpt-19", "styleset(foo,bar,baz) ornaments(baz)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n  @font-feature-values fwf {\n    @styleset {\n      foo: 4;\n      bar: 2;\n    }\n  }\n  @font-feature-values fwf {\n    @styleset {\n      baz: 3;\n    }\n  }\n  @font-feature-values fwf {\n    @styleset {\n      foo: 1; /* Overrides foo from first rule. */\n    }\n    @ornaments {\n      baz: 3;\n    }\n  }\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n  .high {\n\t font-variant-alternates: styleset(foo,bar,baz) ornaments(baz);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" on, \"ss02\" on, \"ss03\" on,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 03, \"nalt\" 00;\n  }\n", "BBBBAAABBBBBBBBBBBAB"),
        new("wpt-layers", "styleset(foo,bar,baz)", "\n  @font-face {\n    font-family: fwf;\n    src: url(support/fonts/FontWithFancyFeatures.otf);\n  }\n\n  @layer one, two, three;\n\n  @layer three {\n    #test {\n      background-color: orange;\n    }\n    @font-feature-values fwf {\n      @styleset {\n        foo: 1;\n        bar: 1;\n      }\n    }\n  }\n\n  @layer one {\n    #test {\n      background-color: silver;\n    }\n    @font-feature-values fwf {\n      @styleset {\n        foo: 2;\n        bar: 2;\n        baz: 2;\n      }\n    }\n  }\n\n  @layer two {\n    #test {\n      background-color: lightblue;\n    }\n    @font-feature-values fwf {\n      @styleset {\n          baz: 3;\n      }\n    }\n  }\n\n  .test {\n\t  font-family: fwf;\n\t  font-size: 2em;\n\t  line-height: 1.1;\n  }\n\n  .high {\n\t font-variant-alternates: styleset(foo,bar,baz);\n  }\n  .low {\n   font-feature-settings: \"hist\" off, \"salt\" 00, \"ss01\" on, \"ss02\" off, \"ss03\" on,\n    \"cv01\" off, \"cv02\" off, \"cv03\" off,  \"swsh\" 00, \"cswh\" 00, \"ornm\" 00, \"nalt\" 00;\n  }\n", "BBBBABABBBBBBBBBBBBB"),
    ];
    internal static int RunFontAlternates()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string fixture = Environment.GetEnvironmentVariable("JALIUM_CSS_ALTERNATES_FONT") ?? Path.Combine(Directory.GetCurrentDirectory(), "tests/Jalium.UI.MacOS.Tests/Assets/FontNumeric/FontWithFancyFeatures.otf");
        using var font = CssNativeFontResource.Create(File.ReadAllBytes(fixture)) ?? throw new Exception("WPT alternate font unavailable"); s_alternatesFamily = font.Family;
        var records = new List<object>(); int failed = 0;
        string[] cases = [.. AlternateCases.Select(row => row.Name), "inline-run", "inline-span", "split", "reader-run", "reader-span", "reader-paragraph", "reader-section", "reader-root", "reader-marker", "editor-run", "editor-span", "editor-paragraph", "editor-section", "editor-root", "editor-marker", "marker", "definitions", "reader-definitions", "editor-definitions", "override", "subsets", "native", "fallback", "writing-tools"];
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 220, 420 }) foreach (string kind in cases)
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_ALTERNATES_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; string label = $"alternates-{engine}-{dpi}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                Check(context.DefaultRenderingEngine == engine, "Requested renderer unavailable");
                byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 272 * dpi, dpi, dc => view.Render(dc), () => true) ?? throw new Exception("Alternate readback failed"));
                using var scene = new AlternateScene(kind, width, dpi, false); scene.Layout(); var normal = Capture(scene.View);
                scene.Change(true); scene.Layout(); var changed = Capture(scene.View); scene.AssertSource(); var metrics = EastAsianMetrics(scene.View);
                using var reference = new AlternateScene(kind, width, dpi, true, reference: true); reference.Layout(); var expected = Capture(reference.View);
                var expectedMetrics = EastAsianMetrics(reference.View);
                Check(changed.SequenceEqual(expected), "Named alternate differs from independent WPT glyph reference");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(expectedMetrics), "Alternate source hit/IME geometry differs from fresh reference");
                if (kind is not ("wpt-01" or "fallback")) Check(!normal.SequenceEqual(changed), "Alternate change has no visible effect");
                if (kind == "writing-tools")
                {
                    var editor = (RichTextBox)scene.View; var source = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                    try
                    {
                        MacOSWritingToolsPresentation.Prepare(editor, source, "font-alternate", 0, 1, 0, () => true);
                        var hidden = Capture(editor); Check(hidden.Where((_, index) => index % 4 == 3).All(alpha => alpha == 0), "Hidden alternate retained ink");
                        var preview = MacOSWritingToolsPresentation.CreatePreview(editor, source, 0, 1, Matrix.Identity, dpi, null, () => true);
                        Check(preview is not null && Canvas(preview, width * dpi, 272 * dpi, dpi).SequenceEqual(changed), "Alternate preview retained default glyph");
                        Save(hidden, width * dpi, 272 * dpi, label + "-hidden.png");
                    }
                    finally { MacOSWritingToolsPresentation.Clear(editor); }
                    Check(Capture(editor).SequenceEqual(changed), "Alternate visibility mask leaked after clear");
                }
                scene.Change(false); scene.Layout(); scene.AssertSource(); var restored = Capture(scene.View); Check(normal.SequenceEqual(restored), "Alternate reset retained substituted glyphs");
                foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) }) Save(item.Item2, width * dpi, 272 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, metrics, expectedMetrics, differentBytes = changed.Zip(expected).Count(p => p.First != p.Second), glyphsChanged = !normal.SequenceEqual(changed) });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count > 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool pass, string message) { if (!pass) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "alternates.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records, nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;
    }

    private sealed class AlternateScene : IDisposable
    {
        internal readonly FrameworkElement View;
        private readonly List<TextBlock> _texts = [];
        private readonly Run? _run;
        private readonly object? _parent;
        private readonly FlowDocument? _document;
        private readonly FlowDocumentPresentation? _reader;
        private readonly DependencyObject _scope, _target;
        private readonly string _kind, _source, _expected, _value, _css, _family;
        private readonly int _width;
        private readonly bool _reference;
        private const string Body = "font-size:32px;line-height:44px;color:#146342;white-space:pre-wrap;text-autospace:no-autospace;";
        internal AlternateScene(string kind, int width, int dpi, bool changed, bool reference = false)
        {
            _kind = kind; _width = width; _reference = reference;
            var row = AlternateCases.FirstOrDefault(row => row.Name == kind) ?? AlternateCases.Single(row => row.Name == "wpt-19");
            _source = "Xnophijklmqrstuvwxyz"; _expected = row.Expected; _value = row.Value; _family = "fwf";
            _css = row.Css.Replace("url(support/fonts/FontWithFancyFeatures.otf)", "local('" + s_alternatesFamily + "')") + "Paragraph,Section,List,ListItem{margin:0;padding:0;border:0}";
            if (kind.EndsWith("definitions", StringComparison.Ordinal) || kind is "override" or "native")
            {
                _value = "stylistic(foo)"; _expected = kind.EndsWith("definitions", StringComparison.Ordinal) || kind == "override" ? "BBBABBBBBBBBBBBBBBBB" : "BABBBBBBBBBBBBBBBBBB";
                _css = "@font-face{font-family:fwf;src:local('" + s_alternatesFamily + "')}@font-feature-values fwf,'" + s_alternatesFamily + "'{@stylistic{foo:1;}}";
            }
            if (kind == "native") _family = s_alternatesFamily;
            if (kind.EndsWith("definitions", StringComparison.Ordinal))
            {
                _family = s_alternatesFamily;
                _css = "@font-feature-values '" + _family + "'{@stylistic{foo:1;}}";
            }
            if (kind == "subsets")
            {
                _family = "SubsetOne,SubsetTwo,'" + s_alternatesFamily + "'"; _value = "stylistic(foo)"; _expected = "BAABBBBBBBBBBBBBBBBB";
                _css = "@font-face{font-family:SubsetOne;src:local('" + s_alternatesFamily + "');unicode-range:U+006E}@font-face{font-family:SubsetTwo;src:local('" + s_alternatesFamily + "');unicode-range:U+006F}@font-feature-values SubsetOne{@stylistic{foo:1;}}@font-feature-values SubsetTwo{@stylistic{foo:2;}}";
            }
            if (kind == "fallback") { _family = "'Unavailable Alternate Font',Seravek"; _value = "stylistic(foo)"; _expected = _source; _css = "@font-feature-values 'Unavailable Alternate Font'{@stylistic{foo:1}}"; }
            if (kind == "writing-tools") { _source = "n"; _expected = "A"; _value = "stylistic(foo)"; _css = "@font-face{font-family:fwf;src:local('" + s_alternatesFamily + "')}@font-feature-values fwf{@stylistic{foo:1}}"; }
            if (kind != "writing-tools") { _source = string.Join(' ', _source.ToCharArray()); _expected = string.Join(' ', _expected.ToCharArray()); }
            string sample = reference ? _expected : _source;
            if (kind.StartsWith("reader-", StringComparison.Ordinal) || kind.StartsWith("editor-", StringComparison.Ordinal) || kind == "writing-tools")
            {
                _run = new(sample); var span = new Span(_run); var paragraph = new Paragraph(span); var section = new Section(paragraph);
                _document = new(section) { PagePadding = new(0), FontSize = 32, LineHeight = 44 }; _scope = _document;
                Css.SetStyle(_document, FamilyStyle() + Body);
                string part = kind[(kind.IndexOf('-') + 1)..]; _target = part switch { "run" => _run, "span" => span, "paragraph" => paragraph, "section" => section, _ => _document };
                if (part == "marker") { _run.Text = "B"; _document.Blocks.Clear(); section.Blocks.Remove(paragraph); _document.Blocks.Add(new Jalium.UI.Documents.List(new ListItem(paragraph))); }
                if (kind.StartsWith("editor-", StringComparison.Ordinal) || kind == "writing-tools") View = new RichTextBox(_document) { Template = null, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent };
                else { _reader = new(); _reader.SetDocument(_document); View = _reader; }
                _parent = _run.Parent;
            }
            else if (kind == "split")
            {
                var root = new StackPanel(); _scope = _target = root; View = root;
                foreach (string part in new[] { sample[..12], sample[12..] }) { var text = new TextBlock { Text = part, IsTextSelectionEnabled = true }; _texts.Add(text); root.Children.Add(text); }
                _css += "TextBlock{display:inline}";
            }
            else
            {
                var text = new TextBlock { Text = sample, IsTextSelectionEnabled = true }; _texts.Add(text); _scope = _target = text; View = text;
                Css.SetStyle(text, FamilyStyle() + Body);
                if (kind is "inline-run" or "inline-span") { text.Text = ""; _run = new(sample); var span = new Span(_run); text.Inlines.Add(span); _target = kind == "inline-run" ? _run : span; _parent = _run.Parent; }
                if (kind == "marker") text.Text = "B";
                if (kind == "native") text.FontFamily = reference ? s_alternatesFamily : _family;
            }
            View.SetRootDpi(new(dpi, dpi)); Change(changed);
        }
        private string FamilyStyle()
        {
            string family = _reference ? _kind == "fallback" ? "Seravek" : "'" + s_alternatesFamily + "'" : _family;
            return "font-family:" + family + ";";
        }
        internal void Change(bool changed)
        {
            string css = _css;
            if (_kind.EndsWith("definitions", StringComparison.Ordinal) && changed && !_reference) css += "@font-feature-values '" + _family + "'{@stylistic{foo:3}}";
            string value = _reference ? "normal" : _kind.EndsWith("definitions", StringComparison.Ordinal) || changed ? _value : "normal";
            if (_kind.EndsWith("marker", StringComparison.Ordinal))
            {
                string selector = _kind == "marker" ? "TextBlock" : "ListItem";
                css += $"{selector}::marker{{content:'{(_reference ? _expected : _source)}';font-variant-alternates:{value}}}";
                if (_kind == "marker") Css.SetStyle(_texts[0], FamilyStyle() + Body + "display:list-item;list-style-position:inside");
            }
            else
            {
                string prefix = ReferenceEquals(_target, _scope) ? FamilyStyle() + Body : "";
                if (_kind == "split") prefix += "display:block;";
                Css.SetStyle(_target, prefix + "font-variant-alternates:" + value + ";" + (_kind == "override" && changed && !_reference ? "font-feature-settings:'salt' 3" : ""));
            }
            Css.SetStyleSheet(_scope, css);
        }
        internal void Layout()
        {
            for (int i = 0; i < 8; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                _reader?.SetViewportHeight(272); View.Measure(new(_width, 272)); View.Arrange(new(0, 0, _width, 272)); View.UpdateLayout();
                Css.WaitForFontsAsync(_scope).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); CssFontFaces.FlushNotifications(Dispatcher.CurrentDispatcher);
                // Font completion may invalidate a document during the last flush.
                // Observe its next UI layout before comparing retained geometry.
                if (i >= 2 && View.IsMeasureValid) break;
            }
            if (!View.IsMeasureValid) throw new Exception("Alternate layout did not settle after font completion");
            if (Css.GetFontLoadErrors(_scope).Count != 0) throw new Exception(string.Join(';', Css.GetFontLoadErrors(_scope)));
        }
        internal void AssertSource()
        {
            if (_run is not null && (!ReferenceEquals(_run.Parent, _parent) || _run.Text != (_kind.EndsWith("marker", StringComparison.Ordinal) ? "B" : _source))) throw new Exception("Alternate changed original run or parent");
            foreach (var text in _texts) { text.SelectAll(); if (text.SelectedText != text.Text) throw new Exception("Alternate copy changed source"); text.Select(0, 0); }
        }
        public void Dispose() { Css.SetStyleSheets(_scope, null); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); _reader?.Dispose(); }
    }
}
