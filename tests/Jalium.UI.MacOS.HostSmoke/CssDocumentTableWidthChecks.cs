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
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssDocumentTableWidthChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_TABLE_WIDTHS_BASELINE") == "1"; var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("内容定宽，边框相随", "title"));
            root.Children.Add(Label("单元格、外框和留白保持一致。", "description"));
            root.Children.Add(Label("连续阅读 · 原文可选", "section"));
            var allParagraphs = new List<Paragraph>(); var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); table.RowGroups.Add(group);
            table.Columns.Add(new TableColumn()); table.Columns.Add(new TableColumn()); Css.SetClass(table, "article");
            for (var i = 0; i < 6; i++)
            {
                var row = new TableRow(); group.Rows.Add(row);
                var main = P($"第{i + 1}行原文"); var side = P("信息"); Css.SetClass(main, "copy"); Css.SetClass(side, "side");
                allParagraphs.Add(main); allParagraphs.Add(side); row.Cells.Add(new TableCell(main)); row.Cells.Add(new TableCell(side));
            }
            var footer = P("末段原文\n仍可选择"); allParagraphs.Add(footer); Css.SetClass(footer, "footer");
            var document = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White,
                PagePadding = new(8), PageWidth = width - 60, PageHeight = 184 }; document.Blocks.Add(table); document.Blocks.Add(footer);
            Css.SetStyleSheet(document, """
                FlowDocument{white-space:pre-wrap;text-align:start;}
                Paragraph{margin:0;}
                .article{width:80px;margin:8px auto;padding:12px;border:2px solid #146342;table-layout:auto;}
                TableCell{padding:8px;border:1px solid #9ab8a8;}
                .copy{width:var(--cell-width,100px);background:#dceee4;}
                .side{width:60px;background:#f1f7f3;}
                .footer{width:80%;margin:5% auto;padding:3%;background:#dceee4;}
                """);
            var selectionBrush = new SolidColorBrush(Color.FromRgb(181, 222, 198));
            var scroll = new FlowDocumentScrollViewer { Document = document, Height = 180, IsToolBarVisible = false,
                IsInactiveSelectionHighlightEnabled = true, SelectionBrush = selectionBrush,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(scroll);
            var pageLabel = Label("", "section pagelabel"); root.Children.Add(pageLabel);
            var pageHost = new Border { Background = Brushes.White }; Css.SetClass(pageHost, "page"); root.Children.Add(pageHost);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var previous = Action("上一页"); var next = Action("下一页"); var resize = Action("加宽原文"); actions.Children.Add(previous); actions.Children.Add(next); actions.Children.Add(resize);
            var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;line-height:1.3;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:14px;}.section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}
                .pagelabel{margin-top:16px;}.page{padding:8px;border:1px solid #9ab8a8;border-radius:8px;}
                .actions{display:flex;gap:12px;margin-top:12px;}.action{min-height:44px;box-sizing:border-box;padding:8px 12px;background:#dceee4;border:1px solid #9ab8a8;border-radius:8px;}
                .action:hover{background:#c9e4d5;}.action:focus{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:1.5;color:#146342;margin-top:10px;}
                """);
            AutomationProperties.SetName(scroll, "可选择的文档原文"); AutomationProperties.SetName(pageHost, "同一文档的分页内容");
            var original = document.GetText(); var sources = allParagraphs.ToArray(); var parents = sources.Select(p => p.Parent).ToArray(); var runs = sources.Select(p => p.Inlines[0]).ToArray();
            UIElement? pageVisual = null; var pageIndex = 0; var widened = false; var rtl = false;
            void SetArticle()
            {
                Css.SetStyle(table, $"--cell-width:{(widened ? "160px" : "100px")};direction:{(rtl ? "rtl" : "ltr")};");
                ((TextBlock)resize.Child!).Text = widened ? "还原宽度" : "加宽原文";
                AutomationProperties.SetName(resize, widened ? "还原宽度" : "加宽原文");
            }
            void Page(int index)
            {
                var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator; pageIndex = Math.Clamp(index, 0, Math.Max(0, paginator.PageCount - 1));
                pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); pageVisual = (UIElement)paginator.GetPage(pageIndex).Visual!; pageHost.Child = pageVisual;
                pageVisual.GetType().GetProperty("SelectionBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pageVisual, selectionBrush);
                if (scroll.Selection is { } selection) pageVisual.GetType().GetMethod("SetSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pageVisual, [selection, true]);
                pageLabel.Text = $"分页阅读 · 第 {pageIndex + 1} 页，共 {paginator.PageCount} 页";
            }
            void Wire(PageAction button, System.Action activate)
            {
                button.Activate = activate; button.MouseLeftButtonUp += (_, _) => activate();
                button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { activate(); e.Handled = true; } };
            }
            Wire(previous, () => Page(pageIndex - 1)); Wire(next, () => Page(pageIndex + 1)); Wire(resize, () => { widened = !widened; SetArticle(); });
            byte[]? initial = null; var initialWidth = 0d;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "widened", "collapsed", "page-2", "rtl-2x", "restored" })
            {
                rtl = phase == "rtl-2x"; var scale = rtl ? 2 : 1; widened = false; SetArticle(); Css.SetStyle(table.Columns[1], phase == "collapsed" ? "visibility:collapse;" : ""); VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                if (phase == "widened") Press(resize, Key.Enter);
                Layout(root, width, height); if (!scroll.Find("末段")) throw new InvalidOperationException("Original source search failed"); Page(0);
                if (phase is "page-2" or "rtl-2x") { Press(next, rtl ? Key.Space : Key.Enter); if (pageIndex != 1) throw new InvalidOperationException("Page key action failed"); }
                status.Text = phase switch { "widened" => "当前：加宽原文，外框同步居中", "collapsed" => "当前：折叠信息列，保留原文", "rtl-2x" => "当前：局部 RTL / 双倍像素密度", "restored" => "已恢复：原始间距与原文选择", _ => "选择保留：末段 · 页码对应同一份原文" }; Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_TABLE_WIDTHS_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var file = File.Create(Path.Combine(path, $"document-table-widths-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment document table width screenshot captured."); return 0; }
                if (document.GetText() != original || scroll.Selection?.Text != "末段" || !sources.Select(p => p.Parent).SequenceEqual(parents) || !sources.Select(p => p.Inlines[0]).SequenceEqual(runs)) throw new InvalidOperationException("Original text, parents or selection changed");
                var body = Descendants(scroll).First(item => item.GetType().Name == "FlowDocumentPresentation");
                var paragraphs = (IReadOnlyList<TextBlock>)body.GetType().GetProperty("Paragraphs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
                if (paragraphs[12].SelectedText != "末段") throw new InvalidOperationException("The original selected source range changed");
                var continuousProvider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                var foundBoxes = continuousProvider.DocumentRange.FindText("末段", false, false)!.GetBoundingRectangles();
                if (foundBoxes.Length != 4 || Math.Abs(foundBoxes[3] - 24) > .02) throw new InvalidOperationException("The found source range was clipped after a shared pagination refresh");
                var frameWidth = phase == "collapsed" ? 146 : widened ? 284 : 224;
                var expectedX = 8 + (body.ActualWidth - 16 - frameWidth) / 2 + 14 + 9 + (rtl ? 78 : 0);
                if (Math.Abs(paragraphs[0].TransformToAncestor(body).X - expectedX) > .02)
                    throw new InvalidOperationException("The table frame, cell tracks and auto margins disagree on the used width");
                if (phase == "initial") initialWidth = paragraphs[0].ActualWidth;
                if (phase == "widened" && (!widened || paragraphs[0].ActualWidth <= initialWidth + 20)) throw new InvalidOperationException("The layout-only width variable did not update its consumers");
                if (phase == "restored" && Math.Abs(paragraphs[0].ActualWidth - initialWidth) > .02) throw new InvalidOperationException("The original source width did not restore");
                var provider = (ITextProvider)pageVisual!.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                if (!provider.GetVisibleRanges().Any(range => range.GetBoundingRectangles().Length > 0)) throw new InvalidOperationException("The page omitted visible source geometry");
                foreach (var element in new FrameworkElement[] { scroll, pageHost, actions, status })
                { var at = element.TransformToAncestor(root); if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport"); }
                if (phase is "initial" or "restored")
                {
                    var top = scroll.TransformToAncestor(root).Y; var bottom = pageHost.TransformToAncestor(root).Y + pageHost.ActualHeight; var crop = new List<byte>();
                    for (var y = (int)Math.Floor(top); y < Math.Min(height, Math.Ceiling(bottom)); y++) for (var x = 20; x < width - 20; x++) for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                    if (phase == "initial") initial = crop.ToArray(); else if (!crop.SequenceEqual(initial!)) throw new InvalidOperationException("Restored document pixels differ");
                }
                Console.WriteLine($"Document table widths {engine}/{width}/{phase}: sourceWidth={paragraphs[0].ActualWidth:0.###}, pages={((IDocumentPaginatorSource)document).DocumentPaginator.PageCount}, selected={scroll.Selection.Text}"); passed++;
            }
            foreach (var button in new[] { previous, next, resize })
            { var peer = button.GetAutomationPeer()!; if (peer.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrWhiteSpace(peer.GetName()) || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider) throw new InvalidOperationException("Custom action semantics missing"); }
            ((IInvokeProvider)next.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 1) throw new InvalidOperationException("Accessible paging failed");
            ((IInvokeProvider)previous.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 0) throw new InvalidOperationException("Accessible paging restoration failed");
            ((IInvokeProvider)resize.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); Layout(root, width, height); if (!widened) throw new InvalidOperationException("Accessible width action failed");
            pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); scroll.Document = null;
        }
        Console.WriteLine($"macOS CSS document table width GPU checks: {passed}/24 passed"); return passed == 24 ? 0 : 1;
    }
    private static void Press(PageAction button, Key key)
    {
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        button.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Custom action did not handle its key");
    }
    private sealed class PageAction : Border
    { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new PageActionPeer(this); }
    private sealed class PageActionPeer(PageAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    { protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button; protected override string GetClassNameCore() => "PageAction"; protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern); public void Invoke() => owner.Activate?.Invoke(); }
    private static Paragraph P(string text) => new(new Run(text));
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static PageAction Action(string text) { var result = new PageAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action"); AutomationProperties.SetName(result, text); return result; }
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement source)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++) if (VisualTreeHelper.GetChild(source, i) is FrameworkElement child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
