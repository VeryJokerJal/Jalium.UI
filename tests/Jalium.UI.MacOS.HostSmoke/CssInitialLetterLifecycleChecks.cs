using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private const string LifecycleHead = "Alpha beta eta";
    private const string LifecycleTail = "continues with ordinary text. The original content keeps its reading order when its style changes.";
    private const string LifecycleStyle = "display:inline;initial-letter:3;color:#146342;";
    private sealed record LifecycleTree(StackPanel Root, FrameworkElement Head, TextBlock Text, TextBlock Tail);

    private static LifecycleTree LifecycleActual(bool border, bool selectable, string state, int dpi)
    {
        var root = new StackPanel { Width = 312, Height = 320, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
        Css.SetStyle(root, InitialBodyStyle);
        var text = new TextBlock { Text = LifecycleHead, IsTextSelectionEnabled = selectable };
        FrameworkElement head = border ? new Border { Child = text } : text;
        if (border) Css.SetStyle(text, "display:inline;");
        var tail = new TextBlock { Text = LifecycleTail }; Css.SetStyle(tail, "display:inline;");
        root.Children.Add(head); root.Children.Add(tail); Css.SetStyle(head, LifecycleStyle + state);
        VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); FirstLetterLayout(root, 312, 320);
        return new(root, head, text, tail);
    }

    private static TextBlock GeneratedInitialActual(string decoration, int width, int dpi, bool enabled)
    {
        var text = new TextBlock
        {
            Text = "continues beside the final large line. Each fragment keeps its own decoration and reading order.",
            IsTextSelectionEnabled = true, Width = width, Height = 400,
            Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1))
        };
        Css.SetClass(text, enabled ? "generated-initial" : "generated-initial off");
        Css.GetStyleSheets(text).Add(CssStyleSheet.Parse(".generated-initial{" + InitialBodyStyle + "}" +
            ".generated-initial::before{content:'Alpha beta eta';display:inline;initial-letter:3;color:#146342;" +
            "background:#e1eee4;padding:3px;border:2px solid #146342;box-decoration-break:" + decoration + ";}" +
            ".generated-initial.off::before{initial-letter:normal;}"));
        VisualTreeHelper.SetRootDpi(text, new(dpi, dpi)); FirstLetterLayout(text, width, 400);
        return text;
    }

    internal static int RunGeneratedInitialLetters()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var records = new List<object>(); var failures = 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var decoration in new[] { "slice", "clone" }) foreach (var width in new[] { 260, 500 }) foreach (var dpi in new[] { 1, 2 })
                {
                    var actual = GeneratedInitialActual(decoration, width, dpi, true);
                    var reference = InitialFragmentsOracle(decoration, width, dpi);
                    var ordinary = GeneratedInitialActual(decoration, width, dpi, false);
                    byte[] Capture(FrameworkElement root, bool recorded) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 400 * dpi, dpi, dc =>
                    {
                        if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                        else root.Render(dc);
                    }, () => true, context) ?? throw new Exception("Generated initial capture unavailable"));
                    int Difference(byte[] a, byte[] b)
                    {
                        var largest = 0; if (a.Length != b.Length) throw new Exception("Generated reference buffer size differs");
                        for (var index = 0; index < a.Length; index++) largest = Math.Max(largest, Math.Abs(a[index] - b[index])); return largest;
                    }
                    var changed = new Dictionary<bool, (byte[] Active, byte[] Reference, int ActiveDifference, int DisabledDifference)>();
                    foreach (var recorded in new[] { false, true })
                    {
                        var a = Capture(actual, recorded); var b = Capture(reference.Root, recorded);
                        Css.SetClass(actual, "generated-initial off"); FirstLetterLayout(actual, width, 400);
                        var disabled = Difference(Capture(actual, recorded), Capture(ordinary, recorded));
                        Css.SetClass(actual, "generated-initial"); FirstLetterLayout(actual, width, 400);
                        changed[recorded] = (a, b, Difference(a, b), disabled);
                    }
                    foreach (var recorded in new[] { false, true })
                    {
                        var item = changed[recorded]; var restore = Difference(Capture(actual, recorded), Capture(reference.Root, recorded));
                        var label = $"generated-initial-{backend}-{engine}-{decoration}-{width}-{dpi}-{recorded}";
                        var errors = new List<string>();
                        if (item.ActiveDifference > 3 || item.DisabledDifference > 3 || restore > 3) errors.Add($"Pixel deltas active {item.ActiveDifference}, disabled {item.DisabledDifference}, restored {restore}");
                        actual.SelectAll(); if (actual.SelectedText != actual.Text || actual.FontSize != 16) errors.Add("Generated content changed original source/font"); actual.Select(0, 0);
                        if (!actual.IsMeasureValid || !actual.IsArrangeValid) errors.Add("Generated layout did not settle");
                        if (errors.Count > 0 || !recorded && dpi == 1)
                        { Save(item.Active, width * dpi, 400 * dpi, label + ".png"); Save(item.Reference, width * dpi, 400 * dpi, label + "-expected.png"); }
                        if (errors.Count > 0) failures++;
                        records.Add(new { label, active = item.ActiveDifference, disabled = item.DisabledDifference, restore, errors });
                        Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
                    }
                }
            }
            File.WriteAllText(Path.Combine(Output, "initial-letter-generated.json"), JsonSerializer.Serialize(new
            { count = records.Count, imageComparisons = records.Count * 3, failures, records, tolerance = 3,
                nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false,
                oracle = "Independent font metrics, glyphs and ordinary float exclusion for active/restored states; fresh ordinary generated content when disabled" }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }

    internal static int RunInitialLetterLifecycle()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        (string Name, string Style)[] states =
        [
            ("inline", ""), ("block", "display:block;"), ("contents", "display:contents;"),
            ("none", "display:none;"), ("hidden", "visibility:hidden;"),
            ("absolute", "position:absolute;left:180px;top:100px;width:120px;"),
            ("relative", "position:relative;left:4px;"),
            ("left-float", "float:left;width:240px;"), ("right-float", "float:right;width:240px;"),
            ("atomic", "display:inline-block;"), ("flex", "display:inline-flex;"),
            ("normal", "initial-letter:normal;")
        ];
        var records = new List<object>(); var failures = 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var border in new[] { false, true }) foreach (var selectable in new[] { false, true }) foreach (var dpi in new[] { 1, 2 })
                {
                    var actual = LifecycleActual(border, selectable, "", dpi);
                    byte[] Capture(FrameworkElement root, bool recorded) => Pixels(MacOSWritingToolsBitmapCapture.Capture(312 * dpi, 320 * dpi, dpi, dc =>
                    {
                        if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                        else root.Render(dc);
                    }, () => true, context) ?? throw new Exception("Lifecycle capture unavailable"));
                    int Difference(byte[] a, byte[] b)
                    {
                        if (a.Length != b.Length) throw new Exception("Lifecycle buffers differ in size");
                        var largest = 0; for (var index = 0; index < a.Length; index++) largest = Math.Max(largest, Math.Abs(a[index] - b[index]));
                        return largest;
                    }
                    foreach (var state in states)
                    {
                        Capture(actual.Root, false); // Prime retained paint before the mutation.
                        Css.SetStyle(actual.Head, LifecycleStyle + state.Style); FirstLetterLayout(actual.Root, 312, 320);
                        var expected = LifecycleActual(border, selectable, state.Style, dpi);
                        var changed = new Dictionary<bool, (byte[] Actual, byte[] Expected, int Difference)>();
                        foreach (var recorded in new[] { false, true })
                        {
                            var a = Capture(actual.Root, recorded); var b = Capture(expected.Root, recorded);
                            changed[recorded] = (a, b, Difference(a, b));
                        }
                        Css.SetStyle(actual.Head, LifecycleStyle); FirstLetterLayout(actual.Root, 312, 320);
                        var restored = LifecycleActual(border, selectable, "", dpi);
                        foreach (var recorded in new[] { false, true })
                        {
                            var label = $"initial-lifecycle-{backend}-{engine}-{border}-{selectable}-{state.Name}-{dpi}-{recorded}";
                            var errors = new List<string>(); var change = changed[recorded];
                            var a = Capture(actual.Root, recorded); var b = Capture(restored.Root, recorded); var restore = Difference(a, b);
                            if (change.Difference > 3) errors.Add($"Changed-state pixel delta {change.Difference}");
                            if (restore > 3) errors.Add($"Restored-state pixel delta {restore}");
                            if (actual.Text.Text != LifecycleHead || actual.Tail.Text != LifecycleTail || actual.Text.FontSize != 16) errors.Add("Original text/font changed");
                            if (selectable)
                            {
                                actual.Text.SelectAll(); if (actual.Text.SelectedText != LifecycleHead) errors.Add("Selection lost original content");
                                actual.Text.Select(0, 0);
                            }
                            if (!actual.Root.IsMeasureValid || !actual.Root.IsArrangeValid) errors.Add("Layout did not settle");
                            if (errors.Count > 0 || !border && selectable && dpi == 1 && !recorded && state.Name is "block" or "absolute" or "none")
                            {
                                Save(change.Actual, 312 * dpi, 320 * dpi, label + ".png");
                                Save(change.Expected, 312 * dpi, 320 * dpi, label + "-expected.png");
                                Save(a, 312 * dpi, 320 * dpi, label + "-restored.png");
                            }
                            if (errors.Count > 0) failures++;
                            records.Add(new { label, change = change.Difference, restore, errors });
                            Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} changed {change.Difference} restored {restore} {string.Join(" | ", errors)}");
                        }
                    }
                }
            }
            File.WriteAllText(Path.Combine(Output, "initial-letter-lifecycle.json"), JsonSerializer.Serialize(new
            {
                count = records.Count, imageComparisons = records.Count * 2, failures, records, tolerance = 3,
                nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false,
                oracle = "Fresh ordinary controls with identical final styles; retained and selectable text paths"
            }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
