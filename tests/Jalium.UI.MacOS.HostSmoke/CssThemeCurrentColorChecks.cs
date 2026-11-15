using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record ThemeInkCase(string Name, string Change = "", bool Effects = false,
        bool Mixed = false, bool Selected = false, bool LineOnly = false, bool Runs = false);

    private static ThemeInkCase[] ThemeInkCases() =>
    [
        new("theme"), new("replace", "replace"), new("mutate", "mutate"),
        new("reparent", "reparent"), new("inherited", "inherited"), new("local", "local"),
        new("effects", Effects: true), new("replace-effects", "replace", Effects: true),
        new("mutate-effects", "mutate", Effects: true), new("reparent-effects", "reparent", Effects: true),
        new("mixed-effects", Effects: true, Mixed: true), new("line-only", LineOnly: true),
        new("runs-line", LineOnly: true, Runs: true),
        new("selection", Effects: true, Selected: true),
        new("selection-mix", Effects: true, Mixed: true, Selected: true),
    ];

    private static bool ThemeInkApplies(string kind, ThemeInkCase row)
        => !row.Runs || kind is "rich" or "rich-shared";

    private static Color ThemeBodyColor(string kind, ThemeInkCase row)
        => row.Change == "inherited" ? Color.FromRgb(20, 99, 66)
            : row.Change == "local" ? Color.FromRgb(137, 65, 18)
            : kind == "password" ? Colors.White
            : row.Change == "replace" ? Color.FromRgb(20, 99, 66)
            : row.Change == "mutate" ? Color.FromArgb(128, 102, 45, 112)
            : row.Change == "reparent" ? Color.FromRgb(137, 65, 18)
            : Color.FromRgb(32, 83, 122);

    private static (StackPanel Root, Control Input, Func<string> Source) MakeThemeInkScene(
        string kind, ThemeInkCase row, int width, bool reference)
    {
        var expected = ThemeBodyColor(kind, row);
        var expression = row.Mixed ? "color-mix(in srgb,currentColor 60%,transparent)" : "currentColor";
        var css = "caret-color:" + expression + ";";
        if (row.LineOnly) css += "text-decoration-line:underline;";
        if (row.Effects) css += "text-emphasis:dot;text-emphasis-color:" + expression +
            ";text-decoration:underline " + expression + ";text-shadow:1px 2px 0 " + expression + ";";
        if (row.Change == "local") css += "color:#146342!important;";
        var scene = MakeInputInkScene(kind, new(row.Name, css, expected, Selected: row.Selected), width, reference);
        if (row.Runs && scene.Input is RichTextBox rich)
            ((Paragraph)rich.Document.Blocks[0]).Inlines[0].SetValue(TextElement.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(20, 99, 66)));
        scene.Root.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(32, 83, 122));
        if (row.Change == "local") scene.Input.Foreground = new SolidColorBrush(expected);
        if (row.Selected && row.Mixed)
            Css.GetStyleSheets(scene.Root).Add(CssStyleSheet.Parse(".subject::selection{color:" + expression +
                ";background:#dcebe4;text-decoration:underline " + expression + ";text-shadow:1px 1px 0 " + expression + ";}"));
        FirstLetterLayout(scene.Root, width, 220);
        if (row.Change.Length > 0)
        {
            scene.Root.Render(new SelectionReferenceGeometry());
            if (row.Change == "replace") scene.Root.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(20, 99, 66));
            else if (row.Change == "mutate") ((SolidColorBrush)scene.Root.Resources["TextPrimary"]!).Color = Color.FromArgb(128, 102, 45, 112);
            else if (row.Change == "inherited") Css.SetStyle(scene.Root, "display:block;font-family:Georgia;font-size:24px;color:#146342;");
            else if (row.Change == "reparent")
            {
                scene.Root.Children.Remove(scene.Input);
                var destination = new StackPanel { Width = width, Height = 220 };
                Css.SetStyle(destination, "display:block;font-family:Georgia;font-size:24px;");
                destination.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(137, 65, 18));
                destination.Children.Add(scene.Input);
                scene.Root.Resources["TextPrimary"] = new SolidColorBrush(Colors.Red);
                scene = (destination, scene.Input, scene.Source);
            }
            FirstLetterLayout(scene.Root, width, 220);
        }
        return scene;
    }

    internal static int RunThemeCurrentColor()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var records = new List<object>(); var failures = 0;
        try
        {
            var requested = Environment.GetEnvironmentVariable("JALIUM_CSS_SELECTION_ENGINE");
            foreach (var engine in (backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
                .Where(value => string.IsNullOrEmpty(requested) || value.ToString().Equals(requested, StringComparison.OrdinalIgnoreCase)))
            {
                context.DefaultRenderingEngine = engine;
                foreach (var kind in SelectionFilter(new[] { "text", "auto", "number", "password", "rich", "rich-shared" }, "JALIUM_CSS_THEME_KINDS", value => value))
                foreach (var row in SelectionFilter(ThemeInkCases(), "JALIUM_CSS_THEME_CASES", value => value.Name).Where(value => ThemeInkApplies(kind, value)))
                foreach (var width in new[] { 312, 600 }) foreach (var dpi in new[] { 1, 2 }) foreach (var recorded in new[] { false, true })
                {
                    GC.Collect(); GC.WaitForPendingFinalizers();
                    var label = $"theme-currentcolor-{backend}-{engine}-{kind}-{row.Name}-{width}-{dpi}-{recorded}";
                    var errors = new List<string>(); var largest = 0; var differences = 0;
                    try
                    {
                        var actual = MakeThemeInkScene(kind, row, width, false);
                        var reference = MakeThemeInkScene(kind, row, width, true);
                        var before = actual.Source();
                        byte[] Capture(StackPanel root) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 220 * dpi, dpi, dc =>
                        {
                            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(241, 245, 241)), null, new Rect(0, 0, width, 220));
                            if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                            else root.Render(dc);
                        }, () => true, context) ?? throw new Exception("Theme native capture unavailable"));
                        var pixels = Capture(actual.Root); var expected = Capture(reference.Root);
                        for (var index = 0; index < pixels.Length; index++)
                        {
                            var delta = Math.Abs(pixels[index] - expected[index]); largest = Math.Max(largest, delta);
                            if (delta > 3) differences++;
                        }
                        if (largest > 3 || width == 312 && dpi == 1 && !recorded)
                        { Save(pixels, width * dpi, 220 * dpi, label + ".png"); Save(expected, width * dpi, 220 * dpi, label + "-expected.png"); }
                        if (largest > 3) errors.Add($"Maximum channel difference {largest}; {differences} channels differ");
                        var ink = ThemeBodyColor(kind, row);
                        var caret = row.Mixed ? Color.FromArgb((byte)Math.Round(ink.A * .6), ink.R, ink.G, ink.B) : ink;
                        if (CssCaretColorProperties.Get(actual.Input) is not SolidColorBrush brush || brush.Color.ToArgb() != caret.ToArgb())
                            errors.Add("Computed currentColor caret " + CssCaretColorProperties.Get(actual.Input) + " differs from expected " + caret);
                        if (actual.Source() != before || reference.Source() != before) errors.Add("Original input source changed");
                        if (row.Change != "local" && actual.Input.HasLocalValue(Control.ForegroundProperty))
                            errors.Add("The theme fallback was promoted to a local foreground");
                    }
                    catch (Exception error) { errors.Add(error.ToString()); }
                    if (errors.Count > 0) failures++;
                    records.Add(new { label, largest, differences, errors });
                    Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join("; ", errors));
                }
            }
            File.WriteAllText(Path.Combine(Output, "theme-currentcolor.json"), JsonSerializer.Serialize(new
            { count = records.Count, failures, records, tolerance = 3, nativeLibraries = NativeLibraries(), liveWindow = false,
                physicalInput = false, voiceOver = false, caretScope = "Computed caret brush checked; no visible native caret claim",
                oracle = "Independent local native owner foreground and explicit rich document/run colors; actual scene retains only native theme fallback. No source foreground promotion." }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }

    internal static int RunThemeCurrentColorGallery()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var records = new List<object>();
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (var dark in new[] { false, true }) foreach (var width in new[] { 375, 720 }) foreach (var dpi in new[] { 1, 2 })
            {
                context.DefaultRenderingEngine = engine; const int height = 700;
                var ink = dark ? Color.FromRgb(155, 213, 176) : Color.FromRgb(32, 83, 122);
                var muted = dark ? Color.FromRgb(175, 208, 188) : Color.FromRgb(68, 100, 84);
                var paper = dark ? Color.FromRgb(24, 37, 31) : Color.FromRgb(243, 247, 241);
                var card = dark ? Color.FromRgb(32, 54, 42) : Colors.White;
                var edge = dark ? Color.FromRgb(70, 103, 83) : Color.FromRgb(214, 226, 217);
                var root = new StackPanel { Width = width, Height = height, Background = new SolidColorBrush(paper) };
                root.Children.Add(new TextBlock { Text = "输入细节，保持一致", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), Margin = new Thickness(18, 26, 18, 8) });
                root.Children.Add(new TextBlock { Text = "自动补全与数值输入的 CSS 文本效果", FontSize = 13, Foreground = new SolidColorBrush(muted), Margin = new Thickness(18, 0, 18, 18) });
                var kinds = new[] { "auto", "number", "auto", "number" };
                var labels = new[] { "自动补全", "数值输入", "模板中的自动补全", "模板中的数值输入" };
                for (var index = 0; index < kinds.Length; index++)
                {
                    var kind = kinds[index];
                    var scene = MakeThemeInkScene(kind, new("gallery", Effects: true), width - 64, false);
                    scene.Root.Height = 56; scene.Input.Height = 56; scene.Input.FontSize = 16;
                    if (index >= 2)
                    {
                        var template = new ControlTemplate(scene.Input.GetType());
                        template.SetVisualTree(() => new Border { Name = "PART_ContentHost" }); scene.Input.Template = template;
                    }
                    scene.Input.ClearValue(Control.PaddingProperty); scene.Input.ClearValue(Control.BorderThicknessProperty);
                    Css.SetStyle(scene.Input, Css.GetStyle(scene.Input) +
                        "padding:0;border:0;text-shadow:0 1px 1px color-mix(in srgb,currentColor 12%,transparent);" +
                        "text-decoration-thickness:1px;text-underline-offset:3px;" +
                        (index == 3 ? "text-emphasis-position:under left;" : ""));
                    if (scene.Input is NumberBox number) number.SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden;
                    scene.Root.Resources["TextPrimary"] = new SolidColorBrush(ink);
                    if (scene.Input is TextBox text) text.Text = "主题变化，继续编辑。";
                    else if (scene.Input is AutoCompleteBox auto) auto.Text = "输入内容，颜色同步。";
                    else if (scene.Input is RichTextBox rich)
                    { rich.Document.FontSize = 16; ((Run)((Paragraph)rich.Document.Blocks[0]).Inlines[0]).Text = "正文与标记同步更新。"; }
                    AutomationProperties.SetName(scene.Input, labels[index]);
                    var password = kind == "password";
                    var labelInk = password ? Color.FromRgb(155, 213, 176) : ink;
                    var content = new StackPanel();
                    content.Children.Add(new TextBlock { Text = labels[index], FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(labelInk), Margin = new Thickness(0, 0, 0, 8) });
                    content.Children.Add(scene.Root);
                    root.Children.Add(new Border { Child = content, Height = 116, Background = new SolidColorBrush(password ? Color.FromRgb(32, 54, 42) : card), BorderBrush = new SolidColorBrush(edge), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(13), Margin = new Thickness(18, 5, 18, 5) });
                }
                root.Children.Add(new TextBlock { Text = "输入内容与文档格式保持原样。", FontSize = 12, Foreground = new SolidColorBrush(muted), Margin = new Thickness(18, 14, 18, 0) });
                FirstLetterLayout(root, width, height);
                var label = $"theme-currentcolor-gallery-{backend}-{engine}-{(dark ? "dark" : "light")}-{width}-{dpi}.png";
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, height * dpi, dpi,
                    dc => root.Render(dc), () => true, context) ?? throw new Exception("Theme gallery capture unavailable")), width * dpi, height * dpi, label);
                records.Add(new { label, width, height, dpi, dark, layoutSettled = root.IsMeasureValid && root.IsArrangeValid });
            }
            File.WriteAllText(Path.Combine(Output, "theme-currentcolor-gallery.json"), JsonSerializer.Serialize(new
            { records, nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false }));
            return 0;
        }
        finally { PopPool(pool); }
    }
}
