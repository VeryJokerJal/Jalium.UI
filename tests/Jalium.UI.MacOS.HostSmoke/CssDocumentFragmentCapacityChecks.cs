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
internal static class CssDocumentFragmentCapacityChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_FRAGMENT_CAPACITIES_BASELINE") == "1"; var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 720 } : new[] { 375, 720 })
        {
            const int height = 760;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("长短页面，各列续排", "title"));
            root.Children.Add(Label("前页填满，末页按剩余空间平衡。", "description"));
            root.Children.Add(Label("连续阅读 · 可滚动选择", "section"));
            var before = P(string.Join("\n", Enumerable.Range(0, 36).Select(i => $"第{i:00}行原文"))); Css.SetClass(before, "copy");
            var heading = P("局部跨列标题"); heading.ClearValue(Block.MarginProperty); Css.SetClass(heading, "heading");
            var after = P("标题后重新分列\n末段仍可选择\n内容继续流动\n恢复原始列序"); Css.SetClass(after, "copy alternate");
            var document = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White,
                PagePadding = new(8), PageWidth = width - 60, PageHeight = 184 };
            var section = new Section { Margin = new(0) }; Css.SetClass(section, "local");
            section.Blocks.Add(before); section.Blocks.Add(heading); section.Blocks.Add(after); document.Blocks.Add(section);
            Css.SetStyleSheet(document, """
                FlowDocument{columns:300px 2;column-gap:20px;column-rule:2px solid #9ab8a8;white-space:pre-wrap;text-align:start;}
                .local{columns:2;column-gap:16px;column-rule:2px solid #146342;}
                .copy{background:#f1f7f3;line-height:24px;}.alternate{background:#dceee4;}
                .heading{column-span:all;font-size:18px;line-height:30px;margin:8px 0;color:#146342;}
                """);
            var selectionBrush = new SolidColorBrush(Color.FromRgb(181, 222, 198));
            var scroll = new FlowDocumentScrollViewer { Document = document, Height = 180, IsToolBarVisible = false,
                IsInactiveSelectionHighlightEnabled = true, SelectionBrush = selectionBrush,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            root.Children.Add(scroll);
            var pageLabel = Label("", "section pagelabel"); root.Children.Add(pageLabel);
            var pageHost = new Border { Background = Brushes.White }; Css.SetClass(pageHost, "page"); root.Children.Add(pageHost);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var previous = Action("上一页"); var next = Action("下一页"); actions.Children.Add(previous); actions.Children.Add(next);
            var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;line-height:1.3;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:14px;}.section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}
                .pagelabel{margin-top:16px;}.page{padding:8px;border:1px solid #9ab8a8;border-radius:8px;}
                .actions{display:flex;gap:12px;margin-top:12px;}.action{min-height:44px;box-sizing:border-box;padding:8px 16px;background:#dceee4;border:1px solid #9ab8a8;border-radius:8px;}
                .action:hover{background:#c9e4d5;}.action:focus{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:1.5;color:#146342;margin-top:10px;}
                """);
            AutomationProperties.SetName(scroll, "可选择的连续多列原文"); AutomationProperties.SetName(pageHost, "同一文档的分页内容");
            var original = document.GetText(); var parents = new[] { before.Parent, heading.Parent, after.Parent };
            UIElement? pageVisual = null; var pageIndex = 0;
            void Page(int index)
            {
                var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
                pageIndex = Math.Clamp(index, 0, Math.Max(0, paginator.PageCount - 1));
                pageHost.Child = null; (pageVisual as IDisposable)?.Dispose();
                pageVisual = (UIElement)paginator.GetPage(pageIndex).Visual!; pageHost.Child = pageVisual;
                pageVisual.GetType().GetProperty("SelectionBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pageVisual, selectionBrush);
                if (scroll.Selection is { } selection) pageVisual.GetType().GetMethod("SetSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pageVisual, [selection, true]);
                pageLabel.Text = $"分页阅读 · 第 {pageIndex + 1} 页，共 {paginator.PageCount} 页";
            }
            void Wire(PageAction button, int delta)
            {
                button.Activate = () => Page(pageIndex + delta);
                button.MouseLeftButtonUp += (_, _) => button.Activate();
                button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { button.Activate(); e.Handled = true; } };
            }
            Wire(previous, -1); Wire(next, 1);
            byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "page-2" } : new[] { "initial", "page-2", "inner-single", "rtl-2x", "restored" })
            {
                var rtl = phase == "rtl-2x"; var scale = rtl ? 2 : 1;
                Css.SetStyle(root, "direction:ltr;");
                Css.SetStyle(document, "direction:ltr;");
                Css.SetStyle(section, "direction:" + (rtl ? "rtl" : "ltr") + ";" + (phase == "inner-single" ? "columns:auto;" : ""));
                VisualTreeHelper.SetRootDpi(root, new(scale, scale)); Layout(root, width, height);
                if (!scroll.Find("末段")) throw new InvalidOperationException("Source search failed");
                Page(0);
                if (phase is "page-2" or "rtl-2x")
                {
                    var actionKey = rtl ? Key.Space : Key.Enter;
                    var key = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                        [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!
                        .Invoke([UIElement.PreviewKeyDownEvent, actionKey, ModifierKeys.None, true, false, 0]); next.RaiseEvent(key);
                    if (!key.Handled || pageIndex != 1) throw new InvalidOperationException("Page action did not handle Enter/Space");
                }
                status.Text = phase switch { "inner-single" => "当前：只保留外层的列规则", "rtl-2x" => "当前：局部 RTL / 双倍像素密度", "restored" => "已恢复：原始列序与原文选择", _ => "选择保留：末段 · 读完各列，再继续下一页" };
                Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4];
                if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_FRAGMENT_CAPACITIES_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var file = File.Create(Path.Combine(path, $"fragment-capacities-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment fragment capacity screenshot captured."); return 0; }
                var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
                if (paginator.PageCount < 2) throw new InvalidOperationException("Expected the original document to continue on later pages");
                if (pageIndex == 0)
                {
                    var provider = (ITextProvider)pageVisual!.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                    if (provider.DocumentRange.FindText("第00行原文", false, false)!.GetBoundingRectangles().Length == 0)
                        throw new InvalidOperationException("The first page omitted the first source line after a layout change");
                }
                if (document.GetText() != original || scroll.Selection?.Text != "末段" || !parents.SequenceEqual(new[] { before.Parent, heading.Parent, after.Parent })) throw new InvalidOperationException("Original document nodes or selection changed");
                var body = Descendants(scroll).First(item => item.GetType().Name == "FlowDocumentPresentation");
                var paragraphs = (IReadOnlyList<TextBlock>)body.GetType().GetProperty("Paragraphs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
                if (paragraphs[2].SelectedText != "末段") throw new InvalidOperationException("The continuous paragraph lost its selected source range");
                if (phase != "inner-single")
                {
                    var tail = (IReadOnlyList<Rect>)typeof(TextBlock).GetMethod("DocumentTextBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(paragraphs[2], [0, paragraphs[2].Text.Length])!;
                    if (tail.Count != 4 || Math.Abs(tail[0].Y - tail[2].Y) > .02 || Math.Abs(tail[1].Y - tail[3].Y) > .02)
                        throw new InvalidOperationException("The paragraph after the local spanner lost its balanced source line order");
                }
                var rectangles = (IReadOnlyList<Rect>)typeof(TextBlock).GetMethod("DocumentTextBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(paragraphs[0], [0, paragraphs[0].Text.Length])!;
                var columns = rectangles.Select(rect => Math.Round(rtl ? rect.Right : rect.X, 2)).Distinct().Count();
                if (columns != (phase == "inner-single" ? width == 375 ? 1 : 2 : width == 375 ? 2 : 4)) throw new InvalidOperationException("Continuous view has an incorrect column count");
                if (pageIndex == 1 && width == 720 && phase != "inner-single")
                {
                    var paged = (IReadOnlyList<TextBlock>)pageVisual!.GetType().GetProperty("Paragraphs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pageVisual)!;
                    var titleAt = paged[1].TransformToAncestor(pageVisual); var afterAt = paged[2].TransformToAncestor(pageVisual);
                    if (Math.Abs(afterAt.Y - titleAt.Y - 38) > .02 || Math.Abs(titleAt.Y - 16) > .02)
                        throw new InvalidOperationException("The balanced last page reclaimed an author margin or retained an unused row gap");
                }
                if (pageIndex == 1)
                {
                    var provider = (ITextProvider)pageVisual!.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                    if (!provider.GetVisibleRanges().Any(range => range.GetBoundingRectangles().Length > 0)) throw new InvalidOperationException("Original ranges missing from the second page");
                }
                foreach (var element in new FrameworkElement[] { scroll, pageHost, actions, status })
                {
                    var at = element.TransformToAncestor(root);
                    if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport");
                }
                if (phase is "initial" or "restored")
                {
                    var top = scroll.TransformToAncestor(root).Y; var bottom = pageHost.TransformToAncestor(root).Y + pageHost.ActualHeight; var crop = new List<byte>();
                    for (var y = (int)Math.Floor(top); y < Math.Min(height, Math.Ceiling(bottom)); y++)
                    for (var x = 20; x < width - 20; x++) for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                    if (phase == "initial") initial = crop.ToArray();
                    else if (!crop.SequenceEqual(initial!)) throw new InvalidOperationException("Restored document pixels differ");
                }
                Console.WriteLine($"Document fragment capacities {engine}/{width}/{phase}: columns={columns}, pages={paginator.PageCount}, selected={scroll.Selection!.Text}"); passed++;
            }
            if (!baseline)
            {
                var nextPeer = next.GetAutomationPeer()!; var previousPeer = previous.GetAutomationPeer()!;
                if (nextPeer.GetAutomationControlType() != AutomationControlType.Button || nextPeer.GetName() != "下一页")
                    throw new InvalidOperationException("Custom page actions omitted their accessible button semantics");
                ((IInvokeProvider)nextPeer.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (pageIndex != 1) throw new InvalidOperationException("Accessible next-page action failed");
                ((IInvokeProvider)previousPeer.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (pageIndex != 0) throw new InvalidOperationException("Accessible previous-page action failed");
            }
            pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); scroll.Document = null;
        }
        Console.WriteLine($"macOS CSS document fragment capacity GPU checks: {passed}/20 passed"); return passed == 20 ? 0 : 1;
    }
    private sealed class PageAction : Border
    {
        internal System.Action? Activate;
        protected override AutomationPeer OnCreateAutomationPeer() => new PageActionPeer(this);
    }
    private sealed class PageActionPeer(PageAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "PageAction";
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() => owner.Activate?.Invoke();
    }
    private static Paragraph P(string text) => new(new Run(text)) { Margin = new(0) };
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static PageAction Action(string text) { var result = new PageAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action"); AutomationProperties.SetName(result, text); return result; }
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement source)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++) if (VisualTreeHelper.GetChild(source, i) is FrameworkElement child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
