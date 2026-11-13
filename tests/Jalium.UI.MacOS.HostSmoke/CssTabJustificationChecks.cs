using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssTabJustificationChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_TAB_JUSTIFICATION_BASELINE") == "1";
        var records = new List<object>(); var failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var method in new[] { "inter-word", "inter-character" })
        {
            byte[]? reference = null;
            foreach (var path in new[] { "native", "block", "rich-native", "rich-block", "shared", "nested" })
            {
                var sample = Sample(path, method); var canvas = new Canvas { Background = Brushes.White };
                canvas.Children.Add(sample); Canvas.SetLeft(sample, 16); Canvas.SetTop(sample, 16); Layout(canvas, 320, 100);
                var pixels = Capture(context, canvas, engine, 320, 100, 1); Save(pixels, 320, 100, $"glyph-{engine}-{method}-{path}.png");
                if (path == "native") reference = pixels;
                if (!pixels.Where((_, i) => i % 4 != 3).Any(value => value < 240)) throw new InvalidOperationException("No native glyph ink captured");
                var equal = pixels.SequenceEqual(reference!); if (!equal) failed++;
                records.Add(new { engine = engine.ToString(), method, path, samePixelsAsNative = equal });
                Console.WriteLine($"Justification glyphs {engine}/{method}/{path}: {equal}");
            }
            if (method != "inter-word") continue;
            reference = null;
            foreach (var path in new[] { "native", "block", "rich-native", "rich-block", "shared", "nested" })
            {
                var sample = Sample(path, method); var canvas = new Canvas { Background = Brushes.White };
                canvas.Children.Add(sample); Canvas.SetLeft(sample, 16); Canvas.SetTop(sample, 16); Layout(canvas, 320, 100);
                var pixels = Capture(context, canvas, engine, 320, 100, 1, true); Save(pixels, 320, 100, $"edge-{engine}-{path}.png");
                if (path == "native") reference = pixels;
                var equal = pixels.SequenceEqual(reference!); if (!equal) failed++;
                if (path == "native" && !baseline && !pixels.Where((_, i) => i % 4 != 3).Any(value => value < 254)) throw new InvalidOperationException("No edge coverage captured");
                records.Add(new { engine = engine.ToString(), method, path, edgeOnly = true, samePixelsAsNative = equal });
            }
        }
        Write("glyphs.json", new { baseline, failed, cases = records });
        var columns = Columns(context);
        var reader = Reader(context, baseline);
        return baseline || failed == 0 && columns == 0 && reader == 0 ? 0 : 1;
    }

    private static FrameworkElement Sample(string path, string method)
    {
        var style = $"font-family:'Courier New';font-size:16px;line-height:24px;width:200px;white-space:pre;tab-size:64px;text-align:justify-all;text-justify:{method};color:#146342;";
        if (path is "native" or "block" or "rich-native" or "rich-block")
        {
            var text = new TextBlock();
            if (path.StartsWith("rich")) { text.Inlines.Add(new Run("MM")); text.Inlines.Add(new Run(" MM\tMM MM")); }
            else text.Text = "MM MM\tMM MM";
            Css.SetStyle(text, (path.EndsWith("block") ? "display:block;" : "") + style); return text;
        }
        var root = new StackPanel(); Css.SetStyle(root, "display:block;" + style); Panel parent = root;
        if (path == "nested")
        {
            var frame = new Border(); Css.SetStyle(frame, "display:inline;"); root.Children.Add(frame);
            var inner = new StackPanel(); Css.SetStyle(inner, "display:inline;"); frame.Child = inner; parent = inner;
        }
        foreach (var value in new[] { "MM", " MM\tMM MM" })
        { var text = new TextBlock { Text = value }; Css.SetStyle(text, "display:inline;"); parent.Children.Add(text); }
        return root;
    }

    private static int Columns(RenderContext context)
    {
        const string row = "M M\tM M";
        var records = new List<object>(); var failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var alignment in new[] { "justify-all", "left" })
        foreach (var indent in new[] { 0, 4 })
        {
            var typography = $"font-family:'Courier New';font-size:16px;line-height:24px;white-space:pre;tab-size:40px;text-align:{alignment};text-indent:{indent}px each-line;text-justify:inter-word;color:#146342;";
            var reference = new Canvas { Background = Brushes.White };
            for (var index = 0; index < 4; index++)
            {
                var text = new TextBlock { Text = row }; Css.SetStyle(text, typography + "width:94px;"); reference.Children.Add(text);
                Canvas.SetLeft(text, 16 + (index / 2) * 111); Canvas.SetTop(text, 16 + (index % 2) * 24);
            }
            Layout(reference, 320, 100); var expected = Capture(context, reference, engine, 320, 100, 1);
            Save(expected, 320, 100, $"columns-{engine}-{alignment}-{indent}-reference.png");
            foreach (var path in new[] { "native", "block", "rich-native", "rich-block" })
            {
                var root = new StackPanel(); Css.SetStyle(root, "display:block;width:205px;column-count:2;column-gap:17px;" + typography);
                var text = new TextBlock(); var words = string.Join('\n', Enumerable.Repeat(row, 4));
                if (path.StartsWith("rich")) { text.Inlines.Add(new Run(words[..2])); text.Inlines.Add(new Run(words[2..])); }
                else text.Text = words;
                if (path.EndsWith("block")) Css.SetStyle(text, "display:block;");
                root.Children.Add(text); var canvas = new Canvas { Background = Brushes.White }; canvas.Children.Add(root);
                Canvas.SetLeft(root, 16); Canvas.SetTop(root, 16); Layout(canvas, 320, 100);
                var pixels = Capture(context, canvas, engine, 320, 100, 1); Save(pixels, 320, 100, $"columns-{engine}-{alignment}-{indent}-{path}.png");
                var equal = pixels.SequenceEqual(expected); if (!equal) failed++;
                var at = text.TransformToAncestor(root);
                var suffixStarts = Enumerable.Range(0, 4).Select(index =>
                    at.X + text.GetSourceRangeSegments(index * (row.Length + 1) + 4, 1).Single().StartX).ToArray();
                records.Add(new { engine = engine.ToString(), alignment, indent, path, samePixelsAsReference = equal, suffixStarts });
                Console.WriteLine($"Tab columns {engine}/{alignment}/{indent}/{path}: {equal}");
            }
        }
        Write("columns.json", new { failed, count = records.Count, cases = records }); return failed;
    }

    private static int Reader(RenderContext context, bool baseline)
    {
        var records = new List<object>();
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 640;
            const string first = "项目\t状态\t";
            const string second = "保留原文 与选择位置。调整对齐方式时，制表位会保留每一列的起点，段落仍然自然换行。阅读时可以改变对齐方式，原文和选择位置保持不变。";
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("让制表位保持整齐", "title"));
            root.Children.Add(Label("切换对齐方式，保留每一列的位置。", "description"));
            var note = new StackPanel(); Css.SetClass(note, "note"); root.Children.Add(note);
            note.Children.Add(Label("进度速览", "caption"));
            var sample = new StackPanel(); Css.SetClass(sample, "sample"); note.Children.Add(sample);
            var a = Label(first, "source"); var b = Label(second, "source"); a.IsTextSelectionEnabled = b.IsTextSelectionEnabled = true;
            sample.Children.Add(a); sample.Children.Add(b);
            note.Children.Add(Label("切换对齐方式，保留你正在读的原文。", "explanation"));
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var justify = Action("两端对齐", 0); var left = Action("左对齐", 1); var restore = Action("恢复原文", 2);
            actions.Children.Add(justify); actions.Children.Add(left); actions.Children.Add(restore);
            var status = Label("段落已两端对齐。", "status"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root { display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:#ffffff; }
                .title { display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px; }
                .description { display:block;margin:0 0 20px; }
                .note { display:block;padding:16px;background:#eef5f0;margin:0 0 20px; }
                .caption { display:block;margin:0 0 12px;font-weight:600; }
                .sample { display:block;white-space:pre-wrap;tab-size:80px;line-height:28px;margin:0 0 12px; }
                .source { display:inline; }
                .explanation { display:block; }
                .actions { display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px; }
                .action { display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342; }
                .action:hover { background:#c5e2d1; }
                .action:active { background:#b4d7c2; }
                .action:focus-visible { border-color:#146342; }
                .status { display:block;font-size:16px;line-height:24px; }
                """));
            var alignment = "justify"; var method = "auto";
            void Apply()
            {
                Css.SetStyle(sample, $"text-align:{alignment};text-justify:{method};");
                status.Text = alignment == "left" ? "段落已左对齐。" : "段落已两端对齐。";
            }
            void Wire(AlignAction button, string value, bool reset = false)
            {
                button.Activate = () => { alignment = value; if (reset) { a.Text = first; b.Text = second; method = "auto"; } Apply(); };
                button.MouseLeftButtonUp += (_, _) => button.Activate();
                button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { button.Activate(); e.Handled = true; } };
            }
            Wire(justify, "justify"); Wire(left, "left"); Wire(restore, "justify", true); byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "left", "edited", "character", "2x", "restored" })
            {
                alignment = "justify"; method = phase == "character" ? "inter-character" : "auto";
                a.Text = first; b.Text = second + (phase == "edited" ? "文字变多时，段落仍然自然换行。" : ""); Apply();
                if (phase == "left") Press(left, Key.Enter);
                if (phase == "edited") ((IInvokeProvider)justify.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (phase == "restored") Press(restore, Key.Space);
                var scale = phase == "2x" ? 2 : 1; VisualTreeHelper.SetRootDpi(root, new(scale, scale)); Layout(root, width, height);
                var pixels = Capture(context, root, engine, width, height, scale); Save(pixels, width * scale, height * scale, $"reader-{engine}-{width}-{phase}.png");
                if (!baseline)
                {
                    var lines = new[] { a, b }.SelectMany(text => Segments(text).Select(segment =>
                    { var at = text.TransformToAncestor(sample); return (Left: at.X + segment.Item1, Right: at.X + segment.Item2, Baseline: Math.Round(at.Y + segment.Item3, 3)); }))
                        .GroupBy(segment => segment.Baseline).OrderBy(group => group.Key).ToArray();
                    if (lines.Length < 2 || alignment == "justify" && Math.Abs(lines[0].Max(segment => segment.Right) - sample.ActualWidth) > .08)
                        throw new InvalidOperationException("Reader paragraph alignment differs");
                    b.Select(0, 1); if (b.SelectedText != "保") throw new InvalidOperationException("Reader selection source differs"); b.Select(0, 0);
                    foreach (var element in new FrameworkElement[] { note, actions, status })
                    { var at = element.TransformToAncestor(root); if (at.X < 19 || at.X + element.ActualWidth > width - 19 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Reader content outside viewport"); }
                    foreach (var button in new[] { justify, left, restore })
                        if (button.ActualHeight < 44 || !button.Focusable || button.GetAutomationPeer()!.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrEmpty(button.GetAutomationPeer()!.GetName()))
                            throw new InvalidOperationException("Reader action semantics differ");
                    if (AutomationProperties.GetLiveSetting(status) != AutomationLiveSetting.Polite) throw new InvalidOperationException("Reader status semantics differ");
                    if (phase == "initial") initial = pixels;
                    if (phase == "restored" && !pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored reader pixels differ");
                    records.Add(new { engine = engine.ToString(), width, phase, alignment, method, lineCount = lines.Length, firstLineRight = lines[0].Max(segment => segment.Right), availableWidth = sample.ActualWidth });
                }
                Console.WriteLine($"Justification reader {engine}/{width}/{phase}");
            }
        }
        Write("reader.json", new { baseline, count = records.Count, cases = records }); return baseline || records.Count == 24 ? 0 : 1;
    }

    private static ValueTuple<double, double, double>[] Segments(TextBlock text) =>
        ((System.Collections.IEnumerable)typeof(TextBlock).GetMethod("GetSourceRangeSegments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(text, [0, text.Text.Length])!).Cast<ValueTuple<double, double, double>>().ToArray();
    private static void Press(AlignAction button, Key key)
    {
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        button.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Alignment action key unhandled");
    }
    private sealed class AlignAction : Border { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new AlignActionPeer(this); }
    private sealed class AlignActionPeer(AlignAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "AlignAction";
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() => owner.Activate?.Invoke();
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static AlignAction Action(string text, int tabIndex)
    {
        var result = new AlignAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action");
        KeyboardNavigation.SetTabIndex(result, tabIndex); AutomationProperties.SetName(result, text); return result;
    }
    private static byte[] Capture(RenderContext context, FrameworkElement root, RenderingEngine engine, int width, int height, int scale, bool edgeOnly = false)
    {
        using var view = new NSView(new CGRect(0, 0, width, height));
        using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
        target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine);
        if (target.RenderingEngine != engine || target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Requested justification renderer unavailable");
        using var drawing = new RenderTargetDrawingContext(target, context);
        target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); drawing.PushClip(new RectangleGeometry(edgeOnly ? new Rect(35.25, 0, .75, height) : new Rect(0, 0, width, height)));
        try { root.Render(drawing); } finally { drawing.Pop(); target.EndDraw(); }
        var pixels = new byte[width * height * scale * scale * 4];
        if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Justification readback failed");
        return pixels;
    }
    private static void Write(string name, object value)
    { if (Environment.GetEnvironmentVariable("JALIUM_CSS_TAB_JUSTIFICATION_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value)); } }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_TAB_JUSTIFICATION_EVIDENCE_DIR") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = File.Create(Path.Combine(path, name)); encoder.Save(file);
    }
    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 4; pass++)
        { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); }
    }
}
