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
internal static class CssDocumentCellHeightChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_CELL_HEIGHTS_BASELINE") == "1"; var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("单元格高度与原文续读", "title"));
            root.Children.Add(Label("行高确定后，按内容区解析百分比。", "description"));
            root.Children.Add(Label("连续阅读 · 同一份原文", "section"));
            var source = P(string.Join('\n', Enumerable.Range(0, 4).Select(index => $"第{index:00}行"))); var article = new Section(source); Css.SetClass(article, "article"); Css.SetClass(source, "copy");
            var spacer = new Section(); Css.SetClass(spacer, "spacer"); var footer = P("末段原文\n仍可选择"); Css.SetClass(footer, "footer");
            var document = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(8), PageWidth = width - 60, PageHeight = 160,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White };
            var cell = new TableCell(article); cell.Blocks.Add(spacer); cell.Blocks.Add(footer); Css.SetClass(cell, "cell");
            var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row);
            var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group); Css.SetClass(table, "table"); document.Blocks.Add(table);
            Css.SetStyleSheet(document, """
                FlowDocument{white-space:pre-wrap;text-align:start;}.table{height:var(--row-height,240px);border-collapse:var(--collapse,separate);}.cell{padding:10px;border:2px solid #146342;vertical-align:top;}
                Paragraph,Section,Table{margin:0;}.article{height:50%;background:#f1f7f3;}.copy{background:#dceee4;}
                .spacer{height:36px;box-sizing:border-box;border:1px solid #9ab8a8;background:#f8fbf9;}
                .footer{padding:0;background:#dceee4;}
                """);
            var selectionBrush = new SolidColorBrush(Color.FromRgb(181, 222, 198));
            var scroll = new FlowDocumentScrollViewer { Document = document, Height = 200, IsToolBarVisible = false, IsInactiveSelectionHighlightEnabled = true,
                SelectionBrush = selectionBrush, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(scroll);
            var pageLabel = Label("", "section pagelabel"); root.Children.Add(pageLabel);
            var pageHost = new Border { Background = Brushes.White }; Css.SetClass(pageHost, "page"); root.Children.Add(pageHost);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var previous = Action("上一页"); var next = Action("下一页"); var resize = Action("增高单元格"); actions.Children.Add(previous); actions.Children.Add(next); actions.Children.Add(resize);
            var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:14px;}.section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}
                .pagelabel{margin-top:16px;}.page{padding:8px;border:1px solid #9ab8a8;border-radius:8px;}
                .actions{display:flex;gap:12px;margin-top:12px;}.action{min-height:44px;box-sizing:border-box;padding:8px 12px;background:#dceee4;border:1px solid #9ab8a8;border-radius:8px;}
                .action:hover{background:#c9e4d5;}.action:focus{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:1.5;color:#146342;margin-top:10px;}
                """);
            AutomationProperties.SetName(scroll, "可选择的文档原文"); AutomationProperties.SetName(pageHost, "保留单元格尺寸的分页内容");
            var original = document.GetText(); var sources = new[] { source, footer }; var parents = sources.Select(p => p.Parent).ToArray(); var runs = sources.Select(p => p.Inlines[0]).ToArray();
            UIElement? pageVisual = null; var pageIndex = 0; var taller = false; var collapsed = false; var rtl = false;
            void SetCellStyle()
            {
                Css.SetStyle(table, $"--row-height:{(taller ? 320 : 240)}px;--collapse:{(collapsed ? "collapse" : "separate")};direction:{(rtl ? "rtl" : "ltr")};");
                ((TextBlock)resize.Child!).Text = taller ? "还原高度" : "增高单元格"; AutomationProperties.SetName(resize, taller ? "还原高度" : "增高单元格");
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
            Wire(previous, () => Page(pageIndex - 1)); Wire(next, () => Page(pageIndex + 1)); Wire(resize, () => { taller = !taller; SetCellStyle(); });
            byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "taller", "collapsed", "page-2", "rtl-2x", "restored" })
            {
                rtl = phase == "rtl-2x"; collapsed = phase == "collapsed"; taller = false; var scale = rtl ? 2 : 1; SetCellStyle(); VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                if (phase == "taller") Press(resize, Key.Enter);
                Layout(root, width, height); var selected = phase == "page-2" ? "末段" : "第00行";
                if (!scroll.Find(selected)) throw new InvalidOperationException("Original source search failed"); Page(0);
                if (phase == "page-2") { Press(next, Key.Enter); if (pageIndex != 1) throw new InvalidOperationException("Page key action failed"); }
                if (rtl)
                {
                    Press(next, Key.Space); if (pageIndex != 1) throw new InvalidOperationException("RTL page key action failed");
                    Press(previous, Key.Space); if (pageIndex != 0) throw new InvalidOperationException("RTL page restoration failed");
                }
                status.Text = phase switch { "taller" => "行高 320px，子段按内容区的 50% 排版", "collapsed" => "合并边框后，按实际内边距重新排版", "page-2" => "第二页续读原文，格内留白只消耗一次", "rtl-2x" => "RTL · 双倍像素密度 · 原文可选", "restored" => "已恢复：尺寸、原文顺序和选择", _ => "行高 240px，子段高度 50%" }; Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_CELL_HEIGHTS_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var file = File.Create(Path.Combine(path, $"document-cell-heights-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment document cell height screenshot captured."); return 0; }
                if (document.GetText() != original || scroll.Selection?.Text != selected || !sources.Select(p => p.Parent).SequenceEqual(parents) || !sources.Select(p => p.Inlines[0]).SequenceEqual(runs)) throw new InvalidOperationException("Original text, parents or selection changed");
                var body = Descendants(scroll).First(item => item.GetType().Name == "FlowDocumentPresentation");
                var paragraphs = (IReadOnlyList<TextBlock>)body.GetType().GetProperty("Paragraphs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
                var cellRecord = ((System.Collections.IEnumerable)body.GetType().GetProperty("TableCells", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!).Cast<object>().Single();
                var frame = (Rect)cellRecord.GetType().GetProperty("Bounds")!.GetValue(cellRecord)!;
                frame = new(frame.X + document.PagePadding.Left, frame.Y + document.PagePadding.Top, frame.Width, frame.Height);
                var origin = paragraphs[0].TransformToAncestor(body); var inset = origin.X - frame.X;
                var expectedFooter = origin.Y + (frame.Height - 2 * inset) / 2 + 36;
                if (Math.Abs(paragraphs[0].ActualWidth - (frame.Width - 2 * inset)) > .02 || Math.Abs(paragraphs[1].TransformToAncestor(body).Y - expectedFooter) > .02)
                    throw new InvalidOperationException("Cell percentage content was not laid out against the final row content area");
                var originalBoxes = (IReadOnlyList<Rect>)typeof(TextBlock).GetMethod("DocumentTextBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(paragraphs[0], [0, paragraphs[0].Text.Length])!;
                if (originalBoxes.Count != 4) throw new InvalidOperationException("Cell content relayout lost original source lines");
                var provider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                var foundBoxes = provider.DocumentRange.FindText(selected, false, false)!.GetBoundingRectangles();
                if (foundBoxes.Length != 4 || Math.Abs(foundBoxes[3] - 24) > .02) throw new InvalidOperationException("Found source geometry was clipped by its specified box height");
                var count = ((IDocumentPaginatorSource)document).DocumentPaginator.PageCount; if (count < 2) throw new InvalidOperationException("Specified blank space was dropped from pagination");
                foreach (var element in new FrameworkElement[] { scroll, pageHost, actions, status })
                { var at = element.TransformToAncestor(root); if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport"); }
                if (phase is "initial" or "restored")
                {
                    var top = scroll.TransformToAncestor(root).Y; var bottom = pageHost.TransformToAncestor(root).Y + pageHost.ActualHeight; var crop = new List<byte>();
                    for (var y = (int)Math.Floor(top); y < Math.Min(height, Math.Ceiling(bottom)); y++) for (var x = 20; x < width - 20; x++) for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                    if (phase == "initial") initial = crop.ToArray(); else if (!crop.SequenceEqual(initial!)) throw new InvalidOperationException("Restored document pixels differ");
                }
                Console.WriteLine($"Document cell heights {engine}/{width}/{phase}: footerTop={paragraphs[1].TransformToAncestor(body).Y:0.###}, pages={count}, selected={selected}"); passed++;
            }
            foreach (var button in new[] { previous, next, resize })
            { var peer = button.GetAutomationPeer()!; if (peer.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrWhiteSpace(peer.GetName()) || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider) throw new InvalidOperationException("Custom action semantics missing"); }
            ((IInvokeProvider)next.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 1) throw new InvalidOperationException("Accessible paging failed");
            ((IInvokeProvider)previous.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 0) throw new InvalidOperationException("Accessible paging restoration failed");
            ((IInvokeProvider)resize.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); Layout(root, width, height); if (!taller) throw new InvalidOperationException("Accessible height action failed");
            pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); scroll.Document = null;
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
            foreach (var pageHeight in new[] { 72, 144 }) VerifyPagedInk(context, engine, pageHeight);
        Console.WriteLine($"macOS CSS document cell height GPU checks: {passed}/24 passed; paginated ink 4/4 passed"); return passed == 24 ? 0 : 1;
    }
    private static void VerifyPagedInk(RenderContext context, RenderingEngine engine, int pageHeight)
    {
        FlowDocument Document(bool constrained)
        {
            var source = P(string.Join('\n', Enumerable.Range(0, 12).Select(index => $"第{index:00}行"))); var section = new Section(source); Css.SetClass(section, "columns");
            var cell = new TableCell(section) { Padding = new(0), BorderThickness = new(0) }; cell.Blocks.Add(P("末段"));
            var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group);
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black,
                Background = Brushes.White, PagePadding = new(0), PageWidth = 400, PageHeight = constrained ? pageHeight : 400 };
            Css.SetStyleSheet(document, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Section,Table{margin:0;}.columns{columns:2;column-gap:20px;}" +
                (constrained ? "Table{height:240px;}.columns{height:50%;}" : "")); return document;
        }
        int Ink(UIElement page, int height, string label)
        {
            const int width = 400;
            using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Ink renderer unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Ink readback request failed");
            target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { page.Render(drawing); } finally { target.EndDraw(); }
            var pixels = new byte[width * height * 4]; if (target.FetchReadback(pixels, width * 4, out var actualWidth, out var actualHeight) != JaliumResult.Ok || actualWidth != width || actualHeight != height) throw new InvalidOperationException("Ink readback failed");
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_CELL_HEIGHTS_EVIDENCE_DIR") is { Length: > 0 } path)
            {
                Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                using var file = File.Create(Path.Combine(path, $"ink-{engine}-{pageHeight}-{label}.png")); encoder.Save(file);
            }
            return Enumerable.Range(0, width * height).Count(index => pixels[index * 4] == 0 && pixels[index * 4 + 1] == 0 && pixels[index * 4 + 2] == 0 && pixels[index * 4 + 3] == 255);
        }
        var reference = Document(false); var referencePaginator = ((IDocumentPaginatorSource)reference).DocumentPaginator;
        using var referencePage = (IDisposable)referencePaginator.GetPage(0).Visual!;
        var expected = Ink((UIElement)referencePage, 400, "reference"); var actual = 0;
        var constrained = Document(true); var paginator = ((IDocumentPaginatorSource)constrained).DocumentPaginator;
        for (var index = 0; index < paginator.PageCount; index++)
        { using var page = (IDisposable)paginator.GetPage(index).Visual!; var ink = Ink((UIElement)page, pageHeight, "page-" + index); actual += ink;
            var provider = (ITextProvider)((UIElement)page).GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
            Console.WriteLine($"Cell ink page {engine}/{pageHeight}/{index}: ink={ink}, footer=[{string.Join(",", provider.DocumentRange.FindText("末段", false, false)!.GetBoundingRectangles())}]"); }
        if (expected == 0 || actual != expected) throw new InvalidOperationException($"Paged cell glyph ink changed for {engine}/{pageHeight}: expected={expected}, actual={actual}");
        Console.WriteLine($"Paged cell ink {engine}/{pageHeight}: expected={expected}, actual={actual}, pages={paginator.PageCount}");
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
