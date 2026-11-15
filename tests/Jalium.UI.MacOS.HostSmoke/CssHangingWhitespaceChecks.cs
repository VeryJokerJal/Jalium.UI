using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Reflection;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssHangingWhitespaceChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_WHITESPACE_BASELINE") == "1";
        var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 640;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("空白留在原文里", "title"));
            root.Children.Add(Label("在窄行里阅读，空格保留，文字自然换行。", "description"));
            var note = new StackPanel(); Css.SetClass(note, "note"); root.Children.Add(note);
            note.Children.Add(Label("今日摘录", "caption"));
            var sample = new TextBlock { Text = "MMMM   MMMM", FontFamily = "Courier New", FontSize = 24, LineHeight = 32, IsTextSelectionEnabled = true };
            Css.SetClass(sample, "sample"); note.Children.Add(sample);
            note.Children.Add(Label("原文中的三个空格仍然可以选择。", "explanation"));
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var preserve = Action("保留空白", 0); var each = Action("逐格换行", 1); var restore = Action("恢复原文", 2);
            actions.Children.Add(preserve); actions.Children.Add(each); actions.Children.Add(restore);
            var status = Label("空格已保留，文字按词换行。", "status");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root { display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:#ffffff; }
                .title { display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px; }
                .description { display:block;margin:0 0 20px; }
                .note { display:block;padding:16px;background:#eef5f0;margin:0 0 20px; }
                .caption { display:block;margin:0 0 12px;font-weight:600; }
                .sample { background:#dceee4;color:#146342;margin:0 0 12px; }
                .explanation { display:block; }
                .actions { display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px; }
                .action { display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342; }
                .action:hover { background:#c5e2d1; }
                .action:focus-visible { border-color:#146342; }
                .status { display:block;font-size:16px;line-height:24px; }
                """));
            string mode = "pre-wrap"; bool rtl = false; byte[]? initial = null;
            void Apply()
            {
                Css.SetStyle(root, $"direction:{(rtl ? "rtl" : "ltr")};");
                Css.SetStyle(sample, $"display:block;white-space:{mode};width:min-content;");
                status.Text = mode == "break-spaces" ? "每个空格都可以成为换行位置。" : "空格已保留，文字按词换行。";
            }
            void Wire(SpaceAction button, string value)
            {
                button.Activate = () => { mode = value; Apply(); };
                button.MouseLeftButtonUp += (_, _) => button.Activate();
                button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { button.Activate(); e.Handled = true; } };
            }
            Wire(preserve, "pre-wrap"); Wire(each, "break-spaces"); Wire(restore, "pre-wrap");
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "break-spaces", "edited", "rtl-2x", "restored" })
            {
                mode = "pre-wrap"; rtl = phase == "rtl-2x";
                sample.Text = phase == "edited" ? "MMMM   MMMM   MMMM" : "MMMM   MMMM";
                Apply();
                if (phase == "break-spaces") Press(each, Key.Enter);
                if (phase == "edited") ((IInvokeProvider)preserve.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (phase == "restored") Press(restore, Key.Space);
                var scale = rtl ? 2 : 1; VisualTreeHelper.SetRootDpi(root, new(scale, scale)); Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height));
                using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine);
                if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested whitespace renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context);
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Whitespace readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4];
                if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var actualWidth, out var actualHeight) != JaliumResult.Ok || actualWidth != width * scale || actualHeight != height * scale)
                    throw new InvalidOperationException("Whitespace readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_WHITESPACE_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(actualWidth, actualHeight, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, actualWidth * 4)));
                    using var file = File.Create(Path.Combine(path, $"whitespace-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine($"Before whitespace: width={sample.ActualWidth}, height={sample.ActualHeight}"); return 0; }
                var expected = ReferenceWidth(mode == "break-spaces" ? "MMMM " : "MMMM");
                if (Math.Abs(sample.ActualWidth - expected) > .06) throw new InvalidOperationException($"Whitespace width differs: {sample.ActualWidth}/{expected}");
                sample.Select(4, 3); if (sample.SelectedText != "   ") throw new InvalidOperationException("Original whitespace selection changed"); sample.Select(0, 0);
                foreach (var element in new FrameworkElement[] { note, actions, status })
                {
                    var at = element.TransformToAncestor(root);
                    if (at.X < 19 || at.X + element.ActualWidth > width - 19 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Whitespace content outside viewport");
                }
                foreach (var button in new[] { preserve, each, restore })
                    if (button.ActualHeight < 44 || !button.Focusable || button.GetAutomationPeer()!.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrEmpty(button.GetAutomationPeer()!.GetName()))
                        throw new InvalidOperationException("Whitespace action semantics differ");
                if (AutomationProperties.GetLiveSetting(status) != AutomationLiveSetting.Polite) throw new InvalidOperationException("Whitespace status semantics differ");
                if (phase == "initial") initial = pixels;
                if (phase == "restored" && !pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored whitespace pixels differ");
                Console.WriteLine($"Whitespace {engine}/{width}/{phase}: width={sample.ActualWidth}, height={sample.ActualHeight}"); passed++;
            }
        }
        Console.WriteLine($"macOS CSS whitespace GPU checks: {passed}/20 passed"); return passed == 20 ? 0 : 1;
    }

    private static double ReferenceWidth(string value)
    {
        var text = new TextBlock { Text = value, FontFamily = "Courier New", FontSize = 24, LineHeight = 32 };
        Css.SetStyle(text, "display:block;white-space:pre;width:max-content;"); Layout(text, 300, 200); return text.ActualWidth;
    }
    private static void Press(SpaceAction button, Key key)
    {
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        button.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Whitespace key unhandled");
    }
    private sealed class SpaceAction : Border { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new SpaceActionPeer(this); }
    private sealed class SpaceActionPeer(SpaceAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "SpaceAction";
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() => owner.Activate?.Invoke();
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static SpaceAction Action(string text, int tabIndex)
    {
        var result = new SpaceAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action");
        KeyboardNavigation.SetTabIndex(result, tabIndex); AutomationProperties.SetName(result, text); return result;
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
