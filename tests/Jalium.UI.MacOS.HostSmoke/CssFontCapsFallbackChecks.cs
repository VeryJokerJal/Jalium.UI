using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record CapsFixtureOptions(string Variant, string FamilyCss, string Source,
        string Expected, string Css = "", string ReferenceCss = "", string StyleSheet = "", string? Language = null);
    private sealed record CapsFallbackCase(string Name, CapsFixtureOptions Options, bool Changes = true);

    internal static int RunFontCapsFallback()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string root = Environment.GetEnvironmentVariable("JALIUM_REPO_ROOT") ?? Directory.GetCurrentDirectory();
        string assets = Path.Combine(root, "tests/Jalium.UI.MacOS.Tests/Assets");
        var resources = new List<Jalium.UI.Styling.CssNativeFontResource>();
        string Load(string path)
        {
            var resource = Jalium.UI.Styling.CssNativeFontResource.Create(File.ReadAllBytes(path)) ?? throw new Exception("Caps fixture unavailable");
            resources.Add(resource); return resource.Family;
        }
        try
        {
            string full = Load(Path.Combine(assets, "FontNumeric/FontWithFancyFeatures.otf"));
            string none = Load(Path.Combine(assets, "FontCapsFallback/no-petite.otf"));
            string lower = Load(Path.Combine(assets, "FontCapsFallback/lower-petite-only.otf"));
            string upper = Load(Path.Combine(assets, "FontCapsFallback/upper-petite-only.otf"));
            const string source = "J K L M N O";
            var cases = new List<CapsFallbackCase>
            {
                new("no-petite", new("petite-caps", $"'{none}'", source, "A B B B B B")),
                new("no-all-petite", new("all-petite-caps", $"'{none}'", source, "A A B B B B")),
                new("lower-petite", new("petite-caps", $"'{lower}'", source, "B B A B B B")),
                new("lower-all-petite", new("all-petite-caps", $"'{lower}'", source, "A A B B B B")),
                new("upper-petite", new("petite-caps", $"'{upper}'", source, "A B B B B B")),
                new("upper-all-petite", new("all-petite-caps", $"'{upper}'", source, "A A B B B B")),
                new("font-wide", new("petite-caps", $"'{full}'", "J", "B"), false),
                new("element-override", new("petite-caps", $"'{none}'", source, "B B B B B B", "font-feature-settings:'smcp' off;"), false),
                new("partial-override", new("all-petite-caps", $"'{lower}'", source, "B A A B B B", "font-feature-settings:'pcap' on,'smcp' off;")),
                new("descriptor-override", new("petite-caps", $"capsDescriptor,'{full}'", source, "A B B B B B", StyleSheet:
                    $"@font-face{{font-family:capsDescriptor;src:local('{none}');font-feature-settings:'smcp' off}}")),
                new("descriptor-partial", new("all-petite-caps", $"capsPartial,'{full}'", source, "A A B B B B", StyleSheet:
                    $"@font-face{{font-family:capsPartial;src:local('{lower}');font-feature-settings:'pcap' on}}")),
                new("raw-unsupported", new("normal", $"'{none}'", source, "B B B B B B", "font-feature-settings:'pcap' on;"), false),
                new("mixed-faces", new("petite-caps", $"capsSubset,'{full}'", source, "A B A B B B", StyleSheet:
                    $"@font-face{{font-family:capsSubset;src:local('{none}');unicode-range:U+4A}}@font-face{{font-family:capsSubset;src:local('{full}');unicode-range:U+4C}}")),
                new("natural", new("petite-caps", "Seravek", "Jalium Aa fi AV 中文 2026", "Jalium Aa fi AV 中文 2026", ReferenceCss:"font-feature-settings:'smcp' on;")),
            };
            string[] kinds = ["text", "inline-run", "inline-span", "split", "reader-run", "reader-span", "reader-paragraph", "reader-section", "reader-root", "reader-marker",
                "editor-run", "editor-span", "editor-paragraph", "editor-section", "editor-root", "editor-marker", "marker", "writing-tools"];
            var records = new List<object>(); int failed = 0;
            foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
            foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 220, 420 })
            foreach (var row in cases) foreach (string kind in kinds)
            {
                if (kind == "writing-tools" && row.Name != "no-petite") continue;
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_CAPS_FALLBACK_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(row.Name)) continue;
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_CAPS_FALLBACK_KINDS") is { Length: > 0 } chosen && !chosen.Split(',').Contains(kind)) continue;
                context.DefaultRenderingEngine = engine;
                string label = $"caps-fallback-{engine}-{dpi}-{width}-{row.Name}-{kind}"; var errors = new List<string>();
                var options = kind == "writing-tools" ? row.Options with { Source = "J", Expected = "A" } : row.Options;
                try
                {
                    Check(context.DefaultRenderingEngine == engine, "Requested renderer unavailable");
                    byte[] Capture(FrameworkElement view)
                    {
                        Check(context.Backend == RenderBackend.Metal && ReferenceEquals(RenderContext.Current, context), "Requested backend is not current");
                        var bitmap = MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi, dc => view.Render(dc), () => true) ?? throw new Exception("Caps fallback readback failed");
                        Check(context.Backend == RenderBackend.Metal && ReferenceEquals(RenderContext.Current, context) && context.DefaultRenderingEngine == engine, "Readback switched backend or renderer");
                        return Pixels(bitmap);
                    }
                    using var scene = new CapsScene(kind, width, dpi, false, options: options); scene.Layout(); var normal = Capture(scene.View);
                    scene.Change(true); scene.Layout(); scene.AssertSources(); var changed = Capture(scene.View); var metrics = CapsMetrics(scene.View);
                    using var reference = new CapsScene(kind, width, dpi, true, reference: true, options: options); reference.Layout(); var expected = Capture(reference.View);
                    Check(changed.SequenceEqual(expected), "Caps fallback differs from independent glyph/reference features");
                    Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(CapsMetrics(reference.View)), "Caps fallback layout/source hit/IME differs from reference");
                    Check(!normal.SequenceEqual(changed) == row.Changes, "Caps fallback visible change differs from the specified font-wide result");
                    if (kind == "writing-tools")
                    {
                        var editor = (RichTextBox)scene.View; var text = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                        try
                        {
                            MacOSWritingToolsPresentation.Prepare(editor, text, "caps-fallback", 0, 1, 0, () => true);
                            var hidden = Capture(editor); Check(hidden.Where((_, index) => index % 4 == 3).All(alpha => alpha == 0), "Hidden fallback glyph retained ink");
                            var preview = MacOSWritingToolsPresentation.CreatePreview(editor, text, 0, 1, Matrix.Identity, dpi, null, () => true);
                            Check(preview is not null && Canvas(preview, width * dpi, 240 * dpi, dpi).SequenceEqual(changed), "Fallback glyph preview differs");
                            Save(hidden, width * dpi, 240 * dpi, label + "-hidden.png");
                        }
                        finally { MacOSWritingToolsPresentation.Clear(editor); }
                        Check(Capture(editor).SequenceEqual(changed), "Fallback visibility mask leaked");
                    }
                    scene.Change(false); scene.Layout(); scene.AssertSources(); var restored = Capture(scene.View);
                    Check(normal.SequenceEqual(restored), "Caps fallback reset retained old glyphs");
                    foreach (var image in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                        Save(image.Item2, width * dpi, 240 * dpi, label + "-" + image.Item1 + ".png");
                    records.Add(new { label, backend = context.Backend.ToString(), engine = context.DefaultRenderingEngine.ToString(), errors, metrics,
                        differentBytes = changed.Zip(expected).Count(pair => pair.First != pair.Second), glyphsChanged = !normal.SequenceEqual(changed) });
                }
                catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
                if (errors.Count > 0) failed++;
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
                void Check(bool value, string message) { if (!value) errors.Add(message); }
            }
            File.WriteAllText(Path.Combine(Output, "caps-fallback.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
                nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
            return failed == 0 ? 0 : 1;
        }
        finally { foreach (var resource in resources) resource.Dispose(); PopPool(pool); }
    }
}
