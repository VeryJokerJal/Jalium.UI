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
internal static class CssLineClampChecks
{
    private const string Words = "MMMM MMMM MMMM MMMM MMMM MMMM M";
    private const string Font = "font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;white-space:normal;text-autospace:no-autospace;width:150px;";
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_LINE_CLAMP_BASELINE") == "1";
        var records = new List<object>(); var failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var (value, marker, lastLine) in new[] { ("no-ellipsis", "", "MMMM MMMM MMMM"), ("ellipsis", "…", "MMMM MMMM MMMM"), ("'++'", "++", "MMMM MMMM") })
        {
            var expected = new Dictionary<bool, byte[]>();
            foreach (var split in new[] { false, true })
            {
                var canvas = new Canvas { Background = Brushes.White };
                var text = Sample(split ? "shared" : "native", "MMMM MMMM MMMM\n" + lastLine, "white-space:pre;");
                canvas.Children.Add(text); Canvas.SetLeft(text, 16); Canvas.SetTop(text, 16);
                if (marker.Length > 0)
                {
                    var ellipsis = new TextBlock { Text = marker }; Css.SetStyle(ellipsis, Font + "white-space:pre;"); canvas.Children.Add(ellipsis);
                    var advance = new FormattedText(lastLine, "Courier New", 16); TextMeasurement.MeasureText(advance);
                    Canvas.SetLeft(ellipsis, 16 + advance.WidthIncludingTrailingWhitespace); Canvas.SetTop(ellipsis, 40);
                }
                Layout(canvas, 240, 140); expected[split] = Capture(context, canvas, engine, 240, 140, 1);
                Save(expected[split], 240, 140, $"reference-{engine}-{value.Replace("'", "")}-{split}.png");
            }
            foreach (var path in new[] { "native", "block", "rich-native", "rich-block", "shared", "nested" })
            {
                var canvas = new Canvas { Background = Brushes.White }; var sample = Sample(path, Words, $"line-clamp:2 {value};");
                canvas.Children.Add(sample); Canvas.SetLeft(sample, 16); Canvas.SetTop(sample, 16); Layout(canvas, 240, 140);
                var pixels = Capture(context, canvas, engine, 240, 140, 1); Save(pixels, 240, 140, $"clamp-{engine}-{value.Replace("'", "")}-{path}.png");
                var equal = pixels.SequenceEqual(expected[path is "shared" or "nested"]); if (!equal) failed++;
                records.Add(new { engine = engine.ToString(), path, value, samePixelsAsExplicitPrefixAndMarker = equal });
            }
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        {
            var canvas = new Canvas { Background = Brushes.White }; var root = new StackPanel(); Css.SetStyle(root, "display:block;" + Font + "line-clamp:2;");
            var text = new TextBlock { Text = Words }; Css.SetClass(text, "source"); Css.SetStyle(text, "display:block;"); root.Children.Add(text);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".source::before{content:'HEAD ';}")); canvas.Children.Add(root); Canvas.SetLeft(root, 16); Canvas.SetTop(root, 16);
            Layout(canvas, 240, 140); var actual = Capture(context, canvas, engine, 240, 140, 1); Save(actual, 240, 140, $"generated-{engine}.png");
            var reference = new Canvas { Background = Brushes.White }; var paragraph = new StackPanel(); Css.SetStyle(paragraph, "display:block;" + Font + "white-space:pre;");
            foreach (var chunk in new[] { "HEAD ", "MMMM MMMM\nMMMM MMMM MMMM" })
            { var part = new TextBlock { Text = chunk }; Css.SetStyle(part, "display:inline;"); paragraph.Children.Add(part); }
            reference.Children.Add(paragraph); Canvas.SetLeft(paragraph, 16); Canvas.SetTop(paragraph, 16);
            var marker = new TextBlock { Text = "…" }; Css.SetStyle(marker, Font + "white-space:pre;"); reference.Children.Add(marker);
            var advance = new FormattedText("MMMM MMMM MMMM", "Courier New", 16); TextMeasurement.MeasureText(advance);
            Canvas.SetLeft(marker, 16 + advance.WidthIncludingTrailingWhitespace); Canvas.SetTop(marker, 40); Layout(reference, 240, 140);
            var expected = Capture(context, reference, engine, 240, 140, 1); Save(expected, 240, 140, $"generated-reference-{engine}.png");
            Css.GetStyleSheets(root).Clear();
            Layout(canvas, 240, 140);
            var withoutBefore = Capture(context, canvas, engine, 240, 140, 1);
            Save(withoutBefore, 240, 140, $"generated-without-before-{engine}.png");
            var equal = actual.SequenceEqual(expected);
            var noBeforeDiffers = !actual.SequenceEqual(withoutBefore);
            if (!equal || !noBeforeDiffers) failed++;
            records.Add(new { engine = engine.ToString(), path = "generated-before",
                samePixelsAsExplicitPrefixAndMarker = equal, noBeforeDiffers });
        }
        Reader(context, baseline);
        Write("native.json", new { baseline, count = records.Count, failed, cases = records });
        return baseline ? 0 : failed == 0 ? 0 : 1;
    }

    private static FrameworkElement Sample(string path, string words, string extra)
    {
        if (path is "native" or "block" or "rich-native" or "rich-block")
        {
            var text = new TextBlock();
            if (path.StartsWith("rich")) { text.Inlines.Add(new Run(words[..7])); text.Inlines.Add(new Run(words[7..])); }
            else text.Text = words;
            Css.SetStyle(text, (path.EndsWith("block") ? "display:block;" : "") + Font + extra); return text;
        }
        var root = new StackPanel(); Css.SetStyle(root, "display:block;" + Font + extra); Panel parent = root;
        if (path == "nested")
        {
            var border = new Border(); Css.SetStyle(border, "display:inline;"); root.Children.Add(border);
            var inner = new StackPanel(); Css.SetStyle(inner, "display:inline;"); border.Child = inner; parent = inner;
        }
        foreach (var chunk in new[] { words[..7], words[7..] })
        { var text = new TextBlock { Text = chunk }; Css.SetStyle(text, "display:inline;"); parent.Children.Add(text); }
        return root;
    }

    private static void Reader(RenderContext context, bool baseline)
    {
        var records = new List<object>();
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 320, 375, 720 })
        {
            var height = width == 320 ? 420 : 640;
            const string first = "在山林与河岸之间，";
            const string second = "慢慢读完一段旅程。把脚步交给小路，把目光留给沿途的风景。拐过石桥，又看见一盏温暖的灯。每一行都值得从容读完，也值得留下一点期待。";
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("把长文留给想读的人", "title")); root.Children.Add(Label("先看摘要，展开后继续阅读。", "description"));
            var note = new StackPanel(); Css.SetClass(note, "note"); root.Children.Add(note); note.Children.Add(Label("阅读随记", "caption"));
            var sample = new StackPanel(); Css.SetClass(sample, "sample"); note.Children.Add(sample);
            var a = Label(first, "source"); var b = Label(second, "source"); a.IsTextSelectionEnabled = b.IsTextSelectionEnabled = true;
            sample.Children.Add(a); sample.Children.Add(b); note.Children.Add(Label("展开后可阅读并选择完整原文。", "explanation"));
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var toggle = Action("展开全文", 0); var edit = Action("补写一句", 1); var restore = Action("恢复原文", 2);
            actions.Children.Add(toggle); actions.Children.Add(edit); actions.Children.Add(restore);
            var status = Label("", "status"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root { display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:#ffffff; }
                .title { display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px; }
                .description { display:block;margin:0 0 20px; }
                .note { display:block;padding:16px;background:#eef5f0;margin:0 0 20px; }
                .caption { display:block;margin:0 0 12px;font-weight:600; }
                .sample { display:block;font-size:18px;line-height:28px;max-width:440px;margin:0 0 16px;text-wrap:balance; }
                .source { display:inline; }
                .explanation { display:block; }
                .actions { display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px; }
                .action { display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342; }
                .action:hover { background:#c5e2d1; }
                .action:active { background:#b4d7c2; }
                .action:focus-visible { border-color:#146342; }
                .status { display:block;font-size:16px;line-height:24px; }
                """));
            var expanded = false;
            void Apply()
            {
                Css.SetStyle(sample, expanded ? "line-clamp:none;" : "line-clamp:3;");
                ((TextBlock)toggle.Child!).Text = expanded ? "收起全文" : "展开全文";
                AutomationProperties.SetName(toggle, expanded ? "收起全文" : "展开全文");
                status.Text = expanded ? "已展开，可阅读完整内容。" : "已收起，显示三行摘要。";
            }
            toggle.Activate = () => { expanded = !expanded; Apply(); };
            edit.Activate = () => { b.Text += "走到下一处风景时，再补写一句。"; Apply(); };
            restore.Activate = () => { a.Text = first; b.Text = second; expanded = false; Apply(); };
            foreach (var action in new[] { toggle, edit, restore })
            {
                action.MouseLeftButtonUp += (_, _) => action.Activate?.Invoke();
                action.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { action.Activate?.Invoke(); e.Handled = true; } };
            }
            Apply(); var viewport = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "expanded", "edited", "collapsed", "2x", "restored" })
            {
                if (phase == "expanded") Press(toggle, Key.Enter);
                if (phase == "edited") ((IInvokeProvider)edit.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (phase == "collapsed") Press(toggle, Key.Space);
                if (phase == "restored") ((IInvokeProvider)restore.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                var scale = phase == "2x" ? 2 : 1; Layout(viewport, width, height);
                var pixels = Capture(context, viewport, engine, width, height, scale); Save(pixels, width * scale, height * scale, $"reader-{engine}-{width}-{phase}.png");
                var lines = new[] { a, b }.SelectMany(text => Segments(text).Select(s => s.Item3 + text.TransformToAncestor(sample).Y)).DistinctBy(y => Math.Round(y, 4)).ToArray();
                if (!baseline)
                {
                    if (!expanded && lines.Length != 3 || expanded && lines.Length <= 3) throw new InvalidOperationException("Reader line count differs");
                    a.SelectAll(); b.SelectAll(); if (a.SelectedText != first || !b.SelectedText.StartsWith(second)) throw new InvalidOperationException("Original reader text changed"); a.Select(0, 0); b.Select(0, 0);
                    foreach (var action in new[] { toggle, edit, restore })
                    {
                        var at = action.TransformToAncestor(root);
                        if (action.ActualHeight < 44 || at.X < 19 || at.X + action.ActualWidth > width - 19 || !action.Focusable || action.GetAutomationPeer()!.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrEmpty(action.GetAutomationPeer()!.GetName())) throw new InvalidOperationException("Reader action layout or semantics differ");
                    }
                    if (AutomationProperties.GetLiveSetting(status) != AutomationLiveSetting.Polite) throw new InvalidOperationException("Reader live status differs");
                    if (phase == "initial") initial = pixels;
                    if (phase == "restored" && !pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored reader pixels differ");
                }
                records.Add(new { engine = engine.ToString(), width, height, phase, expanded, visibleTextLines = lines.Length, viewport.ExtentHeight, viewport.ViewportHeight });
            }
            viewport.ScrollToBottom(); Layout(viewport, width, height);
            if (!baseline)
            {
                var at = restore.TransformToAncestor(viewport);
                if (at.Y < 0 || at.Y + restore.ActualHeight > height) throw new InvalidOperationException("Scrolled reader action is not reachable");
            }
            Save(Capture(context, viewport, engine, width, height, 1), width, height, $"reader-{engine}-{width}-scrolled.png");
        }
        Write("reader.json", new { baseline, count = records.Count, cases = records });
    }

    private static ValueTuple<double, double, double>[] Segments(TextBlock text) =>
        ((System.Collections.IEnumerable)typeof(TextBlock).GetMethod("GetSourceRangeSegments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(text, [0, text.Text.Length])!).Cast<ValueTuple<double, double, double>>().ToArray();
    private static void Press(ClampAction action, Key key)
    {
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        action.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Clamp action key unhandled");
    }
    private sealed class ClampAction : Border { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new ClampActionPeer(this); }
    private sealed class ClampActionPeer(ClampAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "ClampAction";
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() => owner.Activate?.Invoke();
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static ClampAction Action(string text, int tabIndex)
    {
        var result = new ClampAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action");
        KeyboardNavigation.SetTabIndex(result, tabIndex); AutomationProperties.SetName(result, text); return result;
    }
    private static byte[] Capture(RenderContext context, FrameworkElement root, RenderingEngine engine, int width, int height, int scale)
    {
        using var view = new NSView(new CGRect(0, 0, width, height));
        using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
        target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine);
        if (target.RenderingEngine != engine || target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Requested paragraph renderer unavailable");
        using var drawing = new RenderTargetDrawingContext(target, context);
        target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        try { root.Render(drawing); } finally { drawing.Pop(); target.EndDraw(); }
        var pixels = new byte[width * height * scale * scale * 4];
        if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Paragraph readback failed");
        return pixels;
    }
    private static void Write(string name, object value)
    { if (Environment.GetEnvironmentVariable("JALIUM_CSS_LINE_CLAMP_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value)); } }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_LINE_CLAMP_EVIDENCE_DIR") is not { Length: > 0 } path) return;
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
