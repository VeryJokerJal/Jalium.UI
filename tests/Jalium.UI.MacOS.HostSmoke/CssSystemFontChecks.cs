using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record SystemFontReference(string role, string family, double size, int weight, bool italic);
    internal static int RunSystemFonts()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string[] roles = ["caption", "icon", "menu", "message-box", "small-caption", "status-bar"];
        var oracle = Environment.GetEnvironmentVariable("JALIUM_CSS_SYSTEM_FONT_ORACLE");
        var references = oracle is { Length: > 0 }
            ? JsonSerializer.Deserialize<SystemFontReference[]>(File.ReadAllText(oracle))!
            : roles.Select(role => { var f = SystemFonts.CssFont(role); return new SystemFontReference(role, f.Family.Source, f.Size, f.Weight.ToOpenTypeWeight(), f.Style == FontStyles.Italic); }).ToArray();
        var records = new List<object>(); int failed = 0;
        string[] kinds = ["text", "run", "span", "shared", "marker", "reader-root", "reader-run", "reader-span", "reader-paragraph", "reader-section", "editor-root", "editor-run", "editor-span", "editor-paragraph", "editor-section"];
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 220, 420 })
        foreach (string role in roles) foreach (string kind in kinds)
        {
            context.DefaultRenderingEngine = engine; string label = $"system-font-{engine}-{dpi}-{width}-{role}-{kind}"; var errors = new List<string>();
            try
            {
                var font = references.Single(f => f.role == role);
                byte[] Capture(FrameworkElement view) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 272 * dpi, dpi,
                    dc => view.Render(dc), () => true) ?? throw new Exception("System font readback failed"));
                using var scene = new WidthAnimationScene(kind, width, dpi); Prepare(scene, kind); scene.Change(null); scene.Layout(); var normal = Capture(scene.View);
                string prefix = Prefix(kind); Css.SetStyle(scene.Target, prefix + "font:" + role); scene.Layout(); scene.AssertSource();
                byte[] actual = Capture(scene.View); var metrics = EastAsianMetrics(scene.View);
                using var fresh = new WidthAnimationScene(kind, width, dpi);
                Prepare(fresh, kind);
                Css.SetStyle(fresh.Target, prefix + "font-family:'" + font.family.Replace("\\", "\\\\").Replace("'", "\\'") + "';font-size:" + font.size.ToString("R", CultureInfo.InvariantCulture) +
                    "px;font-weight:" + font.weight + ";font-style:" + (font.italic ? "italic" : "normal") + ";font-width:normal;line-height:normal;");
                fresh.Layout(); byte[] expected = Capture(fresh.View); var expectedMetrics = EastAsianMetrics(fresh.View);
                Check(context.DefaultRenderingEngine == engine, "Requested rendering engine unavailable");
                Check(actual.SequenceEqual(expected), "System font differs from explicit CoreText role reference");
                Check(JsonSerializer.Serialize(metrics) == JsonSerializer.Serialize(expectedMetrics), "System font source hit/IME/layout geometry differs from reference");
                var node = CssNode.Get(scene.Target); var familyProperty = CssDependencyPropertyLookup.Find(node.GetType(), "FontFamily")!;
                var sizeProperty = CssDependencyPropertyLookup.Find(node.GetType(), "FontSize")!;
                string actualFamily = ((FontFamily)node.GetValue(familyProperty)!).Source;
                double actualSize = (double)node.GetValue(sizeProperty)!;
                Check(actualFamily == font.family && actualSize == font.size, "System font computed family/size differs from CoreText");
                Check(CssFontStretchValue.Computed(node) == 100, "System font did not reset width");
                scene.Change(null); scene.Layout(); var restored = Capture(scene.View); Check(normal.SequenceEqual(restored), "Removing system font did not restore original view");
                foreach (var item in new[] { ("normal", normal), ("actual", actual), ("expected", expected), ("restored", restored) }) Save(item.Item2, width * dpi, 272 * dpi, label + "-" + item.Item1 + ".png");
                records.Add(new { label, errors, role, kind, actualFamily, actualSize, metrics, expectedMetrics, differentBytes = actual.Zip(expected).Count(p => p.First != p.Second), changed = !actual.SequenceEqual(normal) });
            }
            catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
            if (errors.Count > 0) failed++;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            void Check(bool pass, string message) { if (!pass) errors.Add(message); }
        }
        var actualRoles = roles.Select(role => { var f = SystemFonts.CssFont(role); return new { role, family = f.Family.Source, size = f.Size, weight = f.Weight.ToOpenTypeWeight(), italic = f.Style == FontStyles.Italic }; });
        File.WriteAllText(Path.Combine(Output, "system-fonts.json"), JsonSerializer.Serialize(new
        { count = records.Count, failed, records, actualRoles, nativeLibraries = NativeLibraries(), oracle, independentCoreTextOracle = oracle is { Length: > 0 }, physicalInput = false, voiceOver = false }));
        PopPool(pool); return failed == 0 ? 0 : 1;

        static string Prefix(string kind)
        {
            bool root = kind is "text" or "shared" or "marker" || kind.EndsWith("-root", StringComparison.Ordinal);
            return (root ? "font-family:'SF Pro';font-size:26px;line-height:38px;color:#146342;white-space:pre-wrap;text-autospace:no-autospace;" : "") +
                (kind == "shared" ? "display:block;" : kind == "marker" ? "display:list-item;list-style-type:decimal;list-style-position:inside;" : "");
        }
        static void Prepare(WidthAnimationScene scene, string kind)
        {
            if (kind is not ("reader-root" or "editor-root")) return;
            foreach (string name in new[] { "FontFamily", "FontSize", "FontWeight", "FontStyle", "LineHeight" })
                if (CssDependencyPropertyLookup.Find(scene.Target.GetType(), name) is { } property)
                    scene.Target.ClearValue(property);
        }
    }
}
