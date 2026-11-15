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
internal static class CssLineHeightChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_LINE_HEIGHT_BASELINE") == "1";
        var records = new List<object>(); var failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var path in new[] { "native", "block", "rich-native", "rich-block", "shared", "nested" })
        foreach (var clipped in new[] { false, true })
        {
            byte[]? reference = null;
            foreach (var height in new[] { 64, 0, 8 })
            {
                var canvas = new Canvas { Background = Brushes.White };
                var metrics = TextMeasurement.GetFontMetrics("Courier New", 24);
                var above = metrics.Ascent + (height - metrics.Ascent - metrics.Descent) / 2;
                foreach (var (text, x) in new[] { ("Agyp", 20d), ("Ag水", 180d) })
                {
                    var sample = Sample(path, text, height); canvas.Children.Add(sample);
                    Canvas.SetLeft(sample, x); Canvas.SetTop(sample, 64 - above);
                }
                Layout(canvas, 320, 128);
                byte[] pixels;
                try { pixels = Capture(context, canvas, engine, 320, 128, clipped); }
                catch (Exception error)
                {
                    failed++; records.Add(new { engine = engine.ToString(), path, clipped, lineHeight = height, error = error.Message });
                    Console.WriteLine($"Line-height ink {engine}/{path}/{height}: {error.Message}"); continue;
                }
                Save(pixels, 320, 128, $"ink-{engine}-{path}-{(clipped ? "overhang" : "full")}-{height}.png");
                if (height == 64) reference = pixels;
                var latin = Ink(pixels, 320, 0, 160); var fallback = Ink(pixels, 320, 160, 320);
                var equal = pixels.SequenceEqual(reference!);
                if (latin == 0 || fallback == 0 || !equal) failed++;
                records.Add(new { engine = engine.ToString(), path, clipped, lineHeight = height, latin, fallback, samePixelsAs64 = equal });
                Console.WriteLine($"Line-height ink {engine}/{path}/{height}/{clipped}: latin={latin}, fallback={fallback}, same={equal}");
            }
        }
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_LINE_HEIGHT_EVIDENCE_DIR") is { Length: > 0 } directory)
            File.WriteAllText(Path.Combine(directory, "ink.json"), JsonSerializer.Serialize(new { failed, baseline, cases = records }));
        if (RunReader(context, baseline) != 0) failed++;
        return baseline || failed == 0 ? 0 : 1;
    }

    private static int RunReader(RenderContext context, bool baseline)
    {
        var records = new List<object>();
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 640;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("让每一行都有节奏", "title"));
            root.Children.Add(Label("调整行距，找到适合自己的阅读节奏。", "description"));
            var note = new StackPanel(); Css.SetClass(note, "note"); root.Children.Add(note);
            note.Children.Add(Label("今日摘录", "caption"));
            const string original = "Ag水\ngpy";
            var sample = new TextBlock { Text = original, IsTextSelectionEnabled = true };
            Css.SetClass(sample, "sample"); note.Children.Add(sample);
            note.Children.Add(Label("行距改变，原文和选择位置保持不变。", "explanation"));
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var compact = Action("紧凑行距", 0); var spacious = Action("舒展行距", 1); var restore = Action("恢复原文", 2);
            actions.Children.Add(compact); actions.Children.Add(spacious); actions.Children.Add(restore);
            var status = Label("已恢复原文与默认行距。", "status");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root { display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:#ffffff; }
                .title { display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px; }
                .description { display:block;margin:0 0 20px; }
                .note { display:block;padding:16px;background:#eef5f0;margin:0 0 20px; }
                .caption { display:block;margin:0 0 12px;font-weight:600; }
                .sample { display:block;font-family:"Courier New";font-size:24px;white-space:pre-wrap;background:#dceee4;color:#146342;margin:0 0 12px; }
                .explanation { display:block; }
                .actions { display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px; }
                .action { display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342; }
                .action:hover { background:#c5e2d1; }
                .action:active { background:#b4d7c2; }
                .action:focus-visible { border-color:#146342; }
                .status { display:block;font-size:16px;line-height:24px; }
                """));
            var used = 32; var rtl = false;
            void Apply()
            {
                Css.SetStyle(root, $"direction:{(rtl ? "rtl" : "ltr")};");
                Css.SetStyle(sample, $"line-height:{used}px;");
                status.Text = used == 24 ? "行距已调为紧凑。" : used == 48 ? "行距已调为舒展。" : "已恢复原文与默认行距。";
            }
            void Wire(LineAction button, int value, bool reset = false)
            {
                button.Activate = () => { used = value; if (reset) sample.Text = original; Apply(); };
                button.MouseLeftButtonUp += (_, _) => button.Activate();
                button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { button.Activate(); e.Handled = true; } };
            }
            Wire(compact, 24); Wire(spacious, 48); Wire(restore, 32, true); byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "compact", "spacious", "edited", "rtl-2x", "restored" })
            {
                used = 32; rtl = phase == "rtl-2x"; sample.Text = phase == "edited" ? "Ag水\ngpy\nMMMM" : original; Apply();
                if (phase == "compact") Press(compact, Key.Enter);
                if (phase == "spacious") ((IInvokeProvider)spacious.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (phase == "restored") Press(restore, Key.Space);
                var scale = rtl ? 2 : 1; VisualTreeHelper.SetRootDpi(root, new(scale, scale)); Layout(root, width, height);
                var pixels = Capture(context, root, engine, width, height, false, scale);
                Save(pixels, width * scale, height * scale, $"reader-{engine}-{width}-{phase}.png");
                if (!baseline)
                {
                    var ranges = sample.GetType().GetMethod("GetSourceRangeSegments", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var lines = ((System.Collections.IEnumerable)ranges.Invoke(sample, [0, sample.Text.Length])!).Cast<object>().ToArray();
                    var baselines = lines.Select(line => ((ValueTuple<double, double, double>)line).Item3).ToArray();
                    var count = phase == "edited" ? 3 : 2;
                    if (baselines.Length != count || Math.Abs(sample.ActualHeight - count * used) > .06 || baselines.Skip(1).Where((b, i) => Math.Abs(b - baselines[i] - used) > .06).Any())
                        throw new InvalidOperationException("Reader line boxes differ");
                    sample.Select(2, 1); if (sample.SelectedText != "水") throw new InvalidOperationException("Reader source selection differs"); sample.Select(0, 0);
                    foreach (var element in new FrameworkElement[] { note, actions, status })
                    {
                        var at = element.TransformToAncestor(root);
                        if (at.X < 19 || at.X + element.ActualWidth > width - 19 || at.Y + element.ActualHeight > height - 8)
                            throw new InvalidOperationException("Reader content outside viewport");
                    }
                    foreach (var button in new[] { compact, spacious, restore })
                        if (button.ActualHeight < 44 || !button.Focusable || button.GetAutomationPeer()!.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrEmpty(button.GetAutomationPeer()!.GetName()))
                            throw new InvalidOperationException("Reader action semantics differ");
                    if (AutomationProperties.GetLiveSetting(status) != AutomationLiveSetting.Polite) throw new InvalidOperationException("Reader status semantics differ");
                    if (phase == "initial") initial = pixels;
                    if (phase == "restored" && !pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored reader pixels differ");
                    records.Add(new { engine = engine.ToString(), width, phase, used, count, height = sample.ActualHeight, baselines });
                }
                Console.WriteLine($"Line-height reader {engine}/{width}/{phase}: {sample.ActualHeight}px");
            }
        }
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_LINE_HEIGHT_EVIDENCE_DIR") is { Length: > 0 } directory)
            File.WriteAllText(Path.Combine(directory, "reader.json"), JsonSerializer.Serialize(new { baseline, count = records.Count, cases = records }));
        return baseline || records.Count == 24 ? 0 : 1;
    }

    private static void Press(LineAction button, Key key)
    {
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        button.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Line-height action key unhandled");
    }
    private sealed class LineAction : Border { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new LineActionPeer(this); }
    private sealed class LineActionPeer(LineAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "LineAction";
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() => owner.Activate?.Invoke();
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static LineAction Action(string text, int tabIndex)
    {
        var result = new LineAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action");
        KeyboardNavigation.SetTabIndex(result, tabIndex); AutomationProperties.SetName(result, text); return result;
    }

    private static FrameworkElement Sample(string path, string words, int height)
    {
        var style = $"font-family:'Courier New';font-size:24px;line-height:{height}px;white-space:pre-wrap;width:120px;color:#146342;";
        if (path is "native" or "block" or "rich-native" or "rich-block")
        {
            var text = new TextBlock();
            if (path.StartsWith("rich")) text.Inlines.Add(new Run(words)); else text.Text = words;
            Css.SetStyle(text, (path.EndsWith("block") ? "display:block;" : "") + style); return text;
        }
        var root = new StackPanel(); Css.SetStyle(root, "display:block;" + style);
        if (path == "shared")
        {
            foreach (var value in new[] { words[..2], words[2..] })
            {
                var text = new TextBlock { Text = value }; Css.SetStyle(text, "display:inline;"); root.Children.Add(text);
            }
        }
        else
        {
            var text = new TextBlock { Text = words }; Css.SetStyle(text, "display:inline;");
            var frame = new Border { Child = text }; Css.SetStyle(frame, "display:inline;"); root.Children.Add(frame);
        }
        return root;
    }

    private static byte[] Capture(RenderContext context, FrameworkElement root, RenderingEngine engine, int width, int height, bool overhangClip, int scale = 1)
    {
        using var view = new NSView(new CGRect(0, 0, width, height));
        using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
        target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine);
        if (target.RenderingEngine != engine || target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Line-height renderer/readback unavailable");
        using var drawing = new RenderTargetDrawingContext(target, context);
        target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
        drawing.PushClip(new RectangleGeometry(overhangClip ? new Rect(0, 48, width, 8) : new Rect(0, 0, width, height)));
        try { root.Render(drawing); } finally { drawing.Pop(); target.EndDraw(); }
        var pixels = new byte[width * height * scale * scale * 4];
        if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale)
            throw new InvalidOperationException("Line-height readback failed");
        return pixels;
    }
    private static int Ink(byte[] pixels, int width, int left, int right)
    {
        var count = 0;
        for (var y = 0; y < pixels.Length / width / 4; y++) for (var x = left; x < right; x++)
        {
            var i = (y * width + x) * 4;
            if (pixels[i] < 250 || pixels[i + 1] < 250 || pixels[i + 2] < 250) count++;
        }
        return count;
    }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_LINE_HEIGHT_EVIDENCE_DIR") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = File.Create(Path.Combine(path, name)); encoder.Save(file);
    }
    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null);
            root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout();
        }
    }
}
