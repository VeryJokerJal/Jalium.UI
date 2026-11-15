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
internal static class CssParagraphWrapChecks
{
    private const string Words = "MMMM MMMM MMMM MMMM MMMM MMMM M";
    private const string Font = "font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;text-autospace:no-autospace;";
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_PARAGRAPH_WRAP_BASELINE") == "1";
        var records = new List<object>(); var failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var style in new[] { "auto", "balance", "pretty", "avoid-short-last-line" })
        {
            var hard = style == "auto" ? "MMMM MMMM MMMM\nMMMM MMMM MMMM\nM" : "MMMM MMMM\nMMMM MMMM\nMMMM MMMM M";
            var reference = new TextBlock { Text = hard }; Css.SetStyle(reference, Font + "width:150px;white-space:pre;");
            var expected = Render(context, reference, engine, $"wrap-{engine}-{style}-reference.png");
            // Preserve the original source seam in a second, explicitly
            // broken reference. Independently drawn glyph runs can differ in
            // a few antialiased overlap pixels from a single text draw.
            var splitExpected = Render(context, Sample("shared", "auto", 150, hard, true), engine,
                $"wrap-{engine}-{style}-split-reference.png");
            foreach (var path in new[] { "native", "block", "rich-native", "rich-block", "shared", "nested" })
            {
                var sample = Sample(path, style); var pixels = Render(context, sample, engine, $"wrap-{engine}-{style}-{path}.png");
                var equal = pixels.SequenceEqual(path is "shared" or "nested" ? splitExpected : expected); if (!equal) failed++;
                records.Add(new { engine = engine.ToString(), path, style, samePixelsAsHardBreakReference = equal,
                    samePixelsAsSingleTextReference = pixels.SequenceEqual(expected) });
            }
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var side in new[] { "left", "right" })
        {
            var reference = new Canvas();
            var box = new Border { Width = 30, Height = 96, Background = new SolidColorBrush(Color.FromRgb(0xdc, 0xee, 0xe4)) };
            var text = Sample("shared", "auto", 150, "MMMM MMMM\nMMMM MMMM\nMMMM MMMM M", true);
            reference.Children.Add(box); reference.Children.Add(text); Canvas.SetLeft(box, side == "left" ? 0 : 150); Canvas.SetLeft(text, side == "left" ? 30 : 0);
            var expected = Render(context, reference, engine, $"float-{engine}-{side}-reference.png");
            foreach (var path in new[] { "shared", "nested" })
            {
                var root = (StackPanel)Sample(path, "balance", 180);
                var parent = path == "nested" ? (StackPanel)((Border)root.Children[0]).Child! : root;
                var floating = new Border(); Css.SetStyle(floating, $"float:{side};width:30px;height:96px;background:#dceee4;"); parent.Children.Insert(0, floating);
                Css.SetStyle(root, Css.GetStyle(root) + "display:flow-root;");
                var pixels = Render(context, root, engine, $"float-{engine}-{side}-{path}.png");
                var equal = pixels.SequenceEqual(expected); if (!equal) failed++;
                records.Add(new { engine = engine.ToString(), path, side, samePixelsAsHardBreakReference = equal });
            }
        }
        Write("glyphs.json", new { baseline, failed, count = records.Count, cases = records });
        Reader(context, baseline);
        return baseline || failed == 0 ? 0 : 1;
    }

    private static FrameworkElement Sample(string path, string style, int width = 150, string? words = null, bool hard = false)
    {
        words ??= Words;
        var css = Font + $"width:{width}px;white-space:{(hard ? "pre" : "normal")};text-wrap-style:{style};";
        if (path is "native" or "block" or "rich-native" or "rich-block")
        {
            var text = new TextBlock();
            if (path.StartsWith("rich")) { text.Inlines.Add(new Run(words[..7])); text.Inlines.Add(new Run(words[7..])); } else text.Text = words;
            Css.SetStyle(text, (path.EndsWith("block") ? "display:block;" : "") + css); return text;
        }
        var root = new StackPanel(); Css.SetStyle(root, "display:block;" + css); Panel parent = root;
        if (path == "nested")
        {
            var frame = new Border(); Css.SetStyle(frame, "display:inline;"); root.Children.Add(frame);
            var inner = new StackPanel(); Css.SetStyle(inner, "display:inline;"); frame.Child = inner; parent = inner;
        }
        foreach (var value in new[] { words[..7], words[7..] })
        { var text = new TextBlock { Text = value }; Css.SetStyle(text, "display:inline;"); parent.Children.Add(text); }
        return root;
    }

    private static byte[] Render(RenderContext context, FrameworkElement sample, RenderingEngine engine, string name)
    {
        var canvas = new Canvas { Background = Brushes.White }; canvas.Children.Add(sample); Canvas.SetLeft(sample, 16); Canvas.SetTop(sample, 16);
        Layout(canvas, 240, 130); var pixels = Capture(context, canvas, engine, 240, 130, 1); Save(pixels, 240, 130, name);
        if (!pixels.Where((_, i) => i % 4 != 3).Any(value => value < 240)) throw new InvalidOperationException("No paragraph glyph ink captured");
        return pixels;
    }

    private static void Reader(RenderContext context, bool baseline)
    {
        var records = new List<object>();
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 640;
            const string first = "在山林与河岸之间，";
            const string second = "慢慢读完一段旅程。把脚步交给小路，把目光留给沿途的风景。每一行都值得从容读完。";
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("让长短行更均匀", "title")); root.Children.Add(Label("切换换行方式，让每一行更易读。", "description"));
            var note = new StackPanel(); Css.SetClass(note, "note"); root.Children.Add(note); note.Children.Add(Label("阅读随记", "caption"));
            var sample = new StackPanel(); Css.SetClass(sample, "sample"); note.Children.Add(sample);
            var a = Label(first, "source"); var b = Label(second, "source"); a.IsTextSelectionEnabled = b.IsTextSelectionEnabled = true;
            sample.Children.Add(a); sample.Children.Add(b); note.Children.Add(Label("改变的是行长，原文与选择位置始终保留。", "explanation"));
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var balance = Action("均匀换行", 0); var auto = Action("普通换行", 1); var restore = Action("恢复原文", 2);
            actions.Children.Add(balance); actions.Children.Add(auto); actions.Children.Add(restore);
            var status = Label("已使用均匀换行。", "status"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root { display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:#ffffff; }
                .title { display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px; }
                .description { display:block;margin:0 0 20px; }
                .note { display:block;padding:16px;background:#eef5f0;margin:0 0 20px; }
                .caption { display:block;margin:0 0 12px;font-weight:600; }
                .sample { display:block;font-size:22px;line-height:32px;max-width:440px;margin:0 0 16px; }
                .source { display:inline; }
                .explanation { display:block; }
                .actions { display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px; }
                .action { display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342; }
                .action:hover { background:#c5e2d1; }
                .action:active { background:#b4d7c2; }
                .action:focus-visible { border-color:#146342; }
                .status { display:block;font-size:16px;line-height:24px; }
                """));
            var style = "balance";
            void Apply()
            {
                Css.SetStyle(sample, $"text-wrap-style:{style};");
                status.Text = style == "auto" ? "已使用普通换行。" : style == "pretty" ? "已优化正文行长。" : "已使用均匀换行。";
            }
            void Wire(WrapAction action, string value, bool reset = false)
            {
                action.Activate = () => { style = value; if (reset) { a.Text = first; b.Text = second; } Apply(); };
                action.MouseLeftButtonUp += (_, _) => action.Activate();
                action.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { action.Activate(); e.Handled = true; } };
            }
            Wire(balance, "balance"); Wire(auto, "auto"); Wire(restore, "balance", true); byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "auto", "edited", "pretty", "2x", "restored" })
            {
                a.Text = first; b.Text = second + (phase == "edited" ? "文字变多时，段落仍然从原来的来源继续排下去。" : "");
                style = phase == "pretty" ? "pretty" : "balance"; Apply();
                if (phase == "auto") Press(auto, Key.Enter);
                if (phase == "edited") ((IInvokeProvider)balance.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (phase == "restored") Press(restore, Key.Space);
                var scale = phase == "2x" ? 2 : 1; VisualTreeHelper.SetRootDpi(root, new(scale, scale)); Layout(root, width, height);
                var pixels = Capture(context, root, engine, width, height, scale); Save(pixels, width * scale, height * scale, $"reader-{engine}-{width}-{phase}.png");
                var lines = new[] { a, b }.SelectMany(text => Segments(text).Select(segment =>
                { var at = text.TransformToAncestor(sample); return (Left: at.X + segment.Item1, Right: at.X + segment.Item2, Baseline: Math.Round(at.Y + segment.Item3, 3)); }))
                    .GroupBy(segment => segment.Baseline).OrderBy(group => group.Key).ToArray();
                if (!baseline)
                {
                    if (lines.Length < 2 || lines.Any(line => line.Min(s => s.Left) < -.1 || line.Max(s => s.Right) > sample.ActualWidth + .1)) throw new InvalidOperationException("Reader line outside its paragraph");
                    b.Select(0, 1); if (b.SelectedText != "慢") throw new InvalidOperationException("Reader source selection differs"); b.Select(0, 0);
                    foreach (var element in new FrameworkElement[] { note, actions, status })
                    { var at = element.TransformToAncestor(root); if (at.X < 19 || at.X + element.ActualWidth > width - 19 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Reader content outside viewport"); }
                    foreach (var action in new[] { balance, auto, restore })
                        if (action.ActualHeight < 44 || !action.Focusable || action.GetAutomationPeer()!.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrEmpty(action.GetAutomationPeer()!.GetName())) throw new InvalidOperationException("Reader action semantics differ");
                    if (AutomationProperties.GetLiveSetting(status) != AutomationLiveSetting.Polite) throw new InvalidOperationException("Reader status semantics differ");
                    if (phase == "initial") initial = pixels;
                    if (phase == "restored" && !pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored reader pixels differ");
                }
                records.Add(new { engine = engine.ToString(), width, phase, style, lineCount = lines.Length,
                    widths = lines.Select(line => line.Max(s => s.Right) - line.Min(s => s.Left)).ToArray() });
            }
        }
        Write("reader.json", new { baseline, count = records.Count, cases = records });
    }

    private static ValueTuple<double, double, double>[] Segments(TextBlock text) =>
        ((System.Collections.IEnumerable)typeof(TextBlock).GetMethod("GetSourceRangeSegments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(text, [0, text.Text.Length])!).Cast<ValueTuple<double, double, double>>().ToArray();
    private static void Press(WrapAction action, Key key)
    {
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        action.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Wrap action key unhandled");
    }
    private sealed class WrapAction : Border { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new WrapActionPeer(this); }
    private sealed class WrapActionPeer(WrapAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "WrapAction";
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() => owner.Activate?.Invoke();
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static WrapAction Action(string text, int tabIndex)
    {
        var result = new WrapAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action");
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
    { if (Environment.GetEnvironmentVariable("JALIUM_CSS_PARAGRAPH_WRAP_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value)); } }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_PARAGRAPH_WRAP_EVIDENCE_DIR") is not { Length: > 0 } path) return;
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
