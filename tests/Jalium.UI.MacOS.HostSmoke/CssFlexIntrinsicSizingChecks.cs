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

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssFlexIntrinsicSizingChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_FLEX_INTRINSIC_SIZING_BASELINE") == "1";
        var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("组合随内容展开", "title")); root.Children.Add(Label("内容确定组合宽度，后续记录顺序保持不变。", "description")); root.Children.Add(Label("内容组合", "section"));
            var groupHost = new StackPanel(); Css.SetClass(groupHost, "group-host"); root.Children.Add(groupHost);
            var horizontal = new StackPanel(); Css.SetClass(horizontal, "horizontal"); groupHost.Children.Add(horizontal);
            var firstText = Body("设计稿"); var secondText = Body("实现中");
            var first = new Border { Child = firstText }; var second = new Border { Child = secondText }; Css.SetClass(first, "record"); Css.SetClass(second, "record"); horizontal.Children.Add(first); horizontal.Children.Add(second);
            root.Children.Add(Label("自动高度", "section second-section"));
            var vertical = new StackPanel(); Css.SetClass(vertical, "vertical"); root.Children.Add(vertical);
            var detailText = Body("原文记录"); var detail = new Border { Child = detailText }; Css.SetClass(detail, "detail"); vertical.Children.Add(detail);
            var footerText = Body("末段原文"); var footer = new Border { Child = footerText }; Css.SetClass(footer, "footer"); vertical.Children.Add(footer);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var compactButton = Action("紧凑宽度"); var content = Action("内容宽度"); var restore = Action("恢复组合"); actions.Children.Add(compactButton); actions.Children.Add(content); actions.Children.Add(restore);
            var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, ".root{display:block;padding:20px;font-family:'PingFang SC';font-size:16px;color:#233c32;}.title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:14px;}.section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}.second-section{margin-top:16px;}.group-host{display:grid;grid-template-columns:max-content;}.horizontal{display:flex;align-items:flex-start;gap:12px;height:76px;}.record{display:block;height:64px;min-width:0;min-height:0;padding:8px;border:1px solid #146342;background:#eef5f0;box-sizing:border-box;}.vertical{display:flex;flex-direction:column;align-items:stretch;gap:8px;}.detail{display:block;width:100%;height:240px;min-height:0;flex:1 1 0%;padding:8px;border:1px solid #146342;background:#eef5f0;box-sizing:border-box;}.footer{display:block;flex:none;height:40px;padding:6px 8px;border:1px solid #9ab8a8;background:#dceee4;box-sizing:border-box;}.body{display:block;font-family:'PingFang SC';font-size:16px;line-height:24px;white-space:pre-wrap;text-align:start;}.actions{display:flex;gap:12px;margin-top:16px;}.action{min-height:44px;padding:8px 12px;border:1px solid #9ab8a8;background:#dceee4;border-radius:8px;box-sizing:border-box;}.action:hover{background:#c9e4d5;}.action:focus-visible{outline:2px solid #146342;outline-offset:2px;}.status{display:block;color:#146342;line-height:1.5;margin-top:10px;}");
            var labels = new[] { firstText, secondText, detailText, footerText }; var runs = labels.Select(label => label.Inlines[0]).ToArray();
            var mode = "full"; var definite = false; var rtl = false; byte[]? initial = null;
            void Apply()
            {
                Css.SetStyle(groupHost, $"grid-template-columns:{(mode == "compact" ? "min-content" : "max-content")};direction:{(rtl ? "rtl" : "ltr")};");
                Css.SetStyle(horizontal, $"flex-wrap:{(mode == "compact" ? "wrap" : "nowrap")};direction:{(rtl ? "rtl" : "ltr")};");
                Css.SetStyle(vertical, $"width:{width - 40}px;height:{(definite ? "140px" : "auto")};direction:{(rtl ? "rtl" : "ltr")};");
                var flex = mode == "content" ? "flex:0 0 content;" : "flex:1 1 0px;";
                Css.SetStyle(first, "width:168px;" + flex); Css.SetStyle(second, "width:112px;" + flex);
                status.Text = mode == "compact" ? "组合收紧后，原文仍然清晰。" : mode == "content" ? "按原文内容收紧组合宽度。" : "内容决定组合宽度，后续记录保持原位。";
            }
            void Wire(FlexAction button, string value)
            {
                button.Activate = () => { mode = value; Apply(); };
                button.MouseLeftButtonUp += (_, _) => button.Activate();
                button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { button.Activate(); e.Handled = true; } };
            }
            Wire(compactButton, "compact"); Wire(content, "content"); Wire(restore, "full");
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "compact", "content", "definite", "rtl-2x", "restored" })
            {
                mode = "full"; definite = phase == "definite"; rtl = phase == "rtl-2x"; Apply();
                if (phase == "compact") Press(compactButton, Key.Enter);
                if (phase == "content") ((IInvokeProvider)content.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (phase == "restored") Press(restore, Key.Space);
                var scale = rtl ? 2 : 1; VisualTreeHelper.SetRootDpi(root, new(scale, scale)); Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Flex renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Flex readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var actualWidth, out var actualHeight) != JaliumResult.Ok || actualWidth != width * scale || actualHeight != height * scale) throw new InvalidOperationException("Flex readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_FLEX_INTRINSIC_SIZING_EVIDENCE_DIR") is { Length: > 0 } path)
                { Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(actualWidth, actualHeight, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, actualWidth * 4))); using var file = File.Create(Path.Combine(path, $"intrinsic-{engine}-{width}-{phase}.png")); encoder.Save(file); }
                if (baseline) { Console.WriteLine($"Before intrinsic sizing: group={horizontal.ActualWidth}, detail={detail.ActualHeight}, vertical={vertical.ActualHeight}"); return 0; }
                var expectedGroup = mode == "compact" ? 168 : mode == "content" ? 144 : 292;
                Near(expectedGroup, horizontal.ActualWidth); Near((expectedGroup - 12) / 2d, first.ActualWidth); Near(first.ActualWidth, second.ActualWidth);
                var naturalDetail = detailText.DesiredSize.Height + 18;
                Near(definite ? 92 : naturalDetail, detail.ActualHeight); Near(definite ? 140 : naturalDetail + 48, vertical.ActualHeight);
                Near(detail.ActualHeight + 8, footer.TransformToAncestor(vertical).Y);
                Near(168, first.Width); Near(112, second.Width); Near(240, detail.Height);
                var a = first.TransformToAncestor(horizontal); var b = second.TransformToAncestor(horizontal);
                if (!rtl) { Near(0, a.X); Near(first.ActualWidth + 12, b.X); }
                else { Near(horizontal.ActualWidth - first.ActualWidth, a.X); Near(a.X - 12 - second.ActualWidth, b.X); }
                foreach (var element in new FrameworkElement[] { groupHost, horizontal, vertical, actions, status })
                { var at = element.TransformToAncestor(root); if (at.X < 19 || at.X + element.ActualWidth > width - 19 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Flex content outside viewport"); }
                foreach (var action in new[] { compactButton, content, restore })
                { if (action.ActualHeight < 44 || !action.Focusable || action.GetAutomationPeer()!.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrEmpty(action.GetAutomationPeer()!.GetName())) throw new InvalidOperationException("Flex action semantics differ"); }
                for (var index = 0; index < labels.Length; index++) if (!ReferenceEquals(runs[index], labels[index].Inlines[0])) throw new InvalidOperationException("Flex layout replaced an original Run");
                if (phase == "initial") initial = pixels;
                if (phase == "restored" && !pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored flex pixels differ");
                Console.WriteLine($"Intrinsic sizing {engine}/{width}/{phase}: group={horizontal.ActualWidth}, first={first.ActualWidth}, second={second.ActualWidth}, detail={detail.ActualHeight}, vertical={vertical.ActualHeight}, footer={footer.TransformToAncestor(vertical).Y}"); passed++;
            }
        }
        Console.WriteLine($"macOS CSS flex intrinsic sizing GPU checks: {passed}/24 passed"); return passed == 24 ? 0 : 1;
    }
    private static void Near(double expected, double actual)
    { if (Math.Abs(expected - actual) > .06) throw new InvalidOperationException($"Flex geometry differs: expected={expected}, actual={actual}"); }
    private static void Press(FlexAction button, Key key)
    {
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        button.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Flex action did not handle its key");
    }
    private sealed class FlexAction : Border
    { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new FlexActionPeer(this); }
    private sealed class FlexActionPeer(FlexAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    { protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button; protected override string GetClassNameCore() => "FlexAction"; protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern); public void Invoke() => owner.Activate?.Invoke(); }
    private static TextBlock Body(string words) { var text = new TextBlock { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 }; text.Inlines.Add(new Run(words)); Css.SetClass(text, "body"); return text; }
    private static TextBlock Label(string words, string css) { var text = new TextBlock { Text = words }; Css.SetClass(text, css); return text; }
    private static FlexAction Action(string words) { var result = new FlexAction { Child = Label(words, ""), Focusable = true }; Css.SetClass(result, "action"); AutomationProperties.SetName(result, words); return result; }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var pass = 0; pass < 4; pass++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
