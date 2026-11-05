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
internal static class CssTablePercentColumnChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_PERCENT_COLUMNS_BASELINE") == "1"; var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760; var limit = width - 76;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("列宽随比例，原文不变", "title"));
            root.Children.Add(Label("先算内容，再分配百分比。", "description"));
            root.Children.Add(Label("普通表格 · 比例列与自动列", "section"));
            var table = new Table { CellSpacing = 0 }; Css.SetClass(table, "grid"); var group = new TableRowGroup(); table.RowGroups.Add(group);
            var firstColumn = new TableColumn(); table.Columns.Add(firstColumn); table.Columns.Add(new TableColumn()); Css.SetClass(firstColumn, "first-column");
            var sources = new List<Paragraph>(); var ordinary = new StackPanel(); Css.SetClass(ordinary, "grid");
            var ordinaryColumn = new Border(); Css.SetClass(ordinaryColumn, "first-column"); ordinary.Children.Add(ordinaryColumn);
            var ordinaryCells = new List<Border>();
            for (var rowIndex = 0; rowIndex < 4; rowIndex++)
            {
                var row = new TableRow(); group.Rows.Add(row); var ordinaryRow = new StackPanel(); Css.SetClass(ordinaryRow, "row"); ordinary.Children.Add(ordinaryRow);
                for (var columnIndex = 0; columnIndex < 2; columnIndex++)
                {
                    var words = (columnIndex == 0 ? "比例" : "自动") + (rowIndex + 1); var source = P(words); sources.Add(source); Css.SetClass(source, columnIndex == 0 ? "copy" : "side");
                    var cell = new TableCell(source); Css.SetClass(cell, "cell"); row.Cells.Add(cell);
                    var text = Label(words, "body " + (columnIndex == 0 ? "copy" : "side")); var ordinaryCell = new Border { Child = text }; Css.SetClass(ordinaryCell, "cell"); ordinaryRow.Children.Add(ordinaryCell); ordinaryCells.Add(ordinaryCell);
                }
            }
            var footer = P("末段原文"); sources.Add(footer);
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(8), PageWidth = width - 60, PageHeight = 160,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White }; document.Blocks.Add(footer);
            Css.SetStyleSheet(document, """
                FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}.grid{width:auto;max-width:var(--limit);table-layout:auto;margin:0 auto;}
                .first-column{width:var(--share,25%);visibility:var(--column-visibility,visible);}.copy{width:40px;}.side{width:60px;}
                .cell{padding:8px;border:1px solid #146342;vertical-align:top;background:#dceee4;}
                """);
            var ordinaryScroll = new ScrollViewer { Content = ordinary, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(ordinaryScroll);
            var selectionBrush = new SolidColorBrush(Color.FromRgb(181, 222, 198));
            var scroll = new FlowDocumentScrollViewer { Document = document, Height = 200, IsToolBarVisible = false, IsInactiveSelectionHighlightEnabled = true,
                SelectionBrush = selectionBrush, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var pageLabel = Label("", "section pagelabel"); root.Children.Add(pageLabel);
            var pageHost = new Border { Background = Brushes.White }; Css.SetClass(pageHost, "page"); root.Children.Add(pageHost);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var previous = Action("上一页"); var next = Action("下一页"); var ratio = Action("切换比例"); actions.Children.Add(previous); actions.Children.Add(next); actions.Children.Add(ratio);
            var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:14px;}.section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}
                .pagelabel{margin-top:16px;}.page{padding:8px;border:1px solid #9ab8a8;border-radius:8px;}
                .actions{display:flex;gap:12px;margin-top:12px;}.action{min-height:44px;box-sizing:border-box;padding:8px 12px;background:#dceee4;border:1px solid #9ab8a8;border-radius:8px;}
                .grid{display:table;width:auto;max-width:var(--limit);table-layout:auto;margin:0 auto;}.row{display:table-row;}.cell{display:table-cell;padding:8px;border:1px solid #146342;background:#dceee4;vertical-align:top;}
                .first-column{display:table-column;width:var(--share,25%);visibility:var(--column-visibility,visible);}
                .body{display:block;white-space:pre-wrap;line-height:24px;text-align:start;}.copy{width:40px;}.side{width:60px;}
                .action:hover{background:#c9e4d5;}.action:focus{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:1.5;color:#146342;margin-top:10px;}
                """);
            AutomationProperties.SetName(scroll, "可选择的文档原文"); AutomationProperties.SetName(pageHost, "按百分比列宽续读的分页内容");
            var original = document.GetText(); var parents = sources.Select(source => source.Parent).ToArray(); var runs = sources.Select(source => source.Inlines[0]).ToArray();
            UIElement? pageVisual = null; var pageIndex = 0; var share = 25; var collapsed = false; var rtl = false;
            void Apply()
            {
                var style = $"--limit:{limit}px;--share:{share}%;--column-visibility:{(collapsed ? "collapse" : "visible")};direction:{(rtl ? "rtl" : "ltr")};";
                Css.SetStyle(table, style); Css.SetStyle(ordinary, style);
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
            Wire(previous, () => Page(pageIndex - 1)); Wire(next, () => Page(pageIndex + 1)); Wire(ratio, () => { share = share == 25 ? 50 : 25; Apply(); });
            byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "percent-50", "percent-75", "collapsed", "page-2", "rtl-2x", "restored" })
            {
                rtl = phase == "rtl-2x"; collapsed = phase == "collapsed"; share = phase == "percent-75" ? 75 : 25; var scale = rtl ? 2 : 1; Apply(); VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                if (phase == "percent-50") Press(ratio, Key.Enter);
                Layout(root, width, height); Layout(scroll, width - 40, 200); var selected = collapsed ? "自动1" : "末段";
                if (!scroll.Find(selected)) throw new InvalidOperationException("Original source search failed"); Page(0);
                if (phase == "page-2") { Press(next, Key.Enter); if (pageIndex != 1) throw new InvalidOperationException("Page key action failed"); }
                if (rtl) { Press(next, Key.Space); if (pageIndex != 1) throw new InvalidOperationException("RTL page key action failed"); Press(previous, Key.Space); if (pageIndex != 0) throw new InvalidOperationException("RTL page restoration failed"); }
                status.Text = phase switch { "percent-50" => "比例改为 50%，自动列随之调整", "percent-75" => "比例改为 75%，先保留原文最小宽度", "collapsed" => "折叠比例列，自动列保留原有宽度", "page-2" => "第二页续读同一份原文", "rtl-2x" => "RTL · 双倍像素密度 · 原文可选", "restored" => "已恢复：列宽、原文顺序和选择", _ => "比例列 25% · 自动列承接其余空间" }; Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_PERCENT_COLUMNS_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var file = File.Create(Path.Combine(path, $"table-percent-columns-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment table percentage column screenshot captured."); return 0; }
                if (document.GetText() != original || scroll.Selection?.Text != selected || !sources.Select(source => source.Parent).SequenceEqual(parents) || !sources.Select(source => source.Inlines[0]).SequenceEqual(runs)) throw new InvalidOperationException("Original text, parents or selection changed");
                var body = Descendants(scroll).First(item => item.GetType().Name == "FlowDocumentPresentation");
                var provider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!; var found = provider.DocumentRange.FindText(selected, false, false)!.GetBoundingRectangles();
                if (found.Length != 4 || Math.Abs(found[3] - 24) > .03) throw new InvalidOperationException("Found original text geometry was clipped");
                var paragraphs = (IReadOnlyList<TextBlock>)body.GetType().GetProperty("Paragraphs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
                var records = ((System.Collections.IEnumerable)body.GetType().GetProperty("TableCells", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!).Cast<object>().ToArray();
                var fraction = share / 100d; var total = Math.Max(136, Math.Min(limit, Math.Max(58 / fraction, 78 / (1 - fraction))));
                var first = 58 + Math.Min(total - 136, Math.Max(0, fraction * total - 58)); var expected = new[] { collapsed ? 0 : first, total - first };
                if (records.Length != 8 || paragraphs.Count != sources.Count) throw new InvalidOperationException("Original table sources were duplicated");
                for (var index = 0; index < records.Length; index++)
                {
                    var frame = (Rect)records[index].GetType().GetProperty("Bounds")!.GetValue(records[index])!;
                    if (Math.Abs(frame.Width - expected[index % 2]) > .03 || Math.Abs(ordinaryCells[index].ActualWidth - expected[index % 2]) > .03)
                        throw new InvalidOperationException($"Percentage column parity differs at {index}: ordinary={ordinaryCells[index].ActualWidth}, document={frame.Width}, expected={expected[index % 2]}");
                    if (collapsed && index % 2 == 0) continue;
                    var contentWidth = index % 2 == 0 ? 40 : 60; var x = rtl ? frame.Right - 9 - contentWidth + 8 : frame.Left + 17;
                    if (!ReferenceEquals(paragraphs[index].Inlines, sources[index].Inlines) || Math.Abs(paragraphs[index].TransformToAncestor(body).X - x) > .03)
                        throw new InvalidOperationException($"Original paragraph did not follow its source column: index={index}, at={paragraphs[index].TransformToAncestor(body)}, frame={frame}, expectedX={x}");
                }
                foreach (var element in new FrameworkElement[] { ordinaryScroll, pageHost, actions, status })
                { var at = element.TransformToAncestor(root); if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport"); }
                if (phase is "initial" or "restored")
                {
                    var top = ordinaryScroll.TransformToAncestor(root).Y; var bottom = pageHost.TransformToAncestor(root).Y + pageHost.ActualHeight; var crop = new List<byte>();
                    for (var y = (int)Math.Floor(top); y < Math.Min(height, Math.Ceiling(bottom)); y++) for (var x = 20; x < width - 20; x++) for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                    if (phase == "initial") initial = crop.ToArray(); else if (!crop.SequenceEqual(initial!)) throw new InvalidOperationException("Restored document pixels differ");
                }
                Console.WriteLine($"Table percent columns {engine}/{width}/{phase}: columns={string.Join('/', expected)}, pages={((IDocumentPaginatorSource)document).DocumentPaginator.PageCount}, selected={selected}"); passed++;
            }
            foreach (var button in new[] { previous, next, ratio })
            { var peer = button.GetAutomationPeer()!; if (peer.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrWhiteSpace(peer.GetName()) || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider) throw new InvalidOperationException("Custom action semantics missing"); }
            ((IInvokeProvider)next.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 1) throw new InvalidOperationException("Accessible paging failed");
            ((IInvokeProvider)previous.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 0) throw new InvalidOperationException("Accessible paging restoration failed");
            ((IInvokeProvider)ratio.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); Layout(root, width, height); if (share != 50) throw new InvalidOperationException("Accessible ratio action failed");
            pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); scroll.Document = null;
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello }) foreach (var pageHeight in new[] { 72, 144 }) VerifyPagedInk(context, engine, pageHeight);
        Console.WriteLine($"macOS CSS table percentage column GPU checks: {passed}/28 passed; paginated ink 4/4 passed"); return passed == 28 ? 0 : 1;
    }

    private static void VerifyPagedInk(RenderContext context, RenderingEngine engine, int pageHeight)
    {
        FlowDocument Document(bool constrained)
        {
            var table = new Table { CellSpacing = 0 }; var firstColumn = new TableColumn(); table.Columns.Add(firstColumn); table.Columns.Add(new TableColumn()); if (constrained) Css.SetStyle(firstColumn, "width:25%;");
            for (var rowIndex = 0; rowIndex < 3; rowIndex++) {
                var source = P(string.Join('\n', Enumerable.Range(rowIndex * 4, 4).Select(index => $"第{index:00}行")));
                var cell = new TableCell(source) { Padding = new(0), BorderThickness = new(0) }; var row = new TableRow(); row.Cells.Add(cell); var filler = new Section(); Css.SetStyle(filler, "width:20px;height:0;"); row.Cells.Add(new TableCell(filler) { Padding = new(0), BorderThickness = new(0) }); var group = new TableRowGroup(); table.RowGroups.Add(group); group.Rows.Add(row);
                if (constrained && rowIndex != 1) Css.SetStyle(group, "height:25%;");
            }
            if (constrained) Css.SetStyle(table, "width:auto;height:480px;");
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black,
                Background = Brushes.White, PagePadding = new(0), PageWidth = 400, PageHeight = constrained ? pageHeight : 600 };
            document.Blocks.Add(P("末段")); Css.SetStyleSheet(document, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}"); return document;
        }
        int Ink(UIElement page, int height, string label)
        {
            const int width = 400;
            using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Ink renderer unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Ink readback request failed");
            target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { page.Render(drawing); } finally { target.EndDraw(); }
            var pixels = new byte[width * height * 4]; if (target.FetchReadback(pixels, width * 4, out var actualWidth, out var actualHeight) != JaliumResult.Ok || actualWidth != width || actualHeight != height) throw new InvalidOperationException("Ink readback failed");
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_PERCENT_COLUMNS_EVIDENCE_DIR") is { Length: > 0 } path)
            {
                Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                using var file = File.Create(Path.Combine(path, $"ink-{engine}-{pageHeight}-{label}.png")); encoder.Save(file);
            }
            return Enumerable.Range(0, width * height).Count(index => pixels[index * 4] == 0 && pixels[index * 4 + 1] == 0 && pixels[index * 4 + 2] == 0 && pixels[index * 4 + 3] == 255);
        }
        var reference = Document(false); var referencePaginator = ((IDocumentPaginatorSource)reference).DocumentPaginator;
        using var referencePage = (IDisposable)referencePaginator.GetPage(0).Visual!;
        var expected = Ink((UIElement)referencePage, 600, "reference"); var actual = 0;
        var constrained = Document(true); var paginator = ((IDocumentPaginatorSource)constrained).DocumentPaginator;
        for (var index = 0; index < paginator.PageCount; index++)
        { using var page = (IDisposable)paginator.GetPage(index).Visual!; var ink = Ink((UIElement)page, pageHeight, "page-" + index); actual += ink;
            var provider = (ITextProvider)((UIElement)page).GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
            Console.WriteLine($"Percent-column ink page {engine}/{pageHeight}/{index}: ink={ink}, footer=[{string.Join(",", provider.DocumentRange.FindText("末段", false, false)!.GetBoundingRectangles())}]"); }
        if (expected == 0 || actual != expected) throw new InvalidOperationException($"Paged row glyph ink changed for {engine}/{pageHeight}: expected={expected}, actual={actual}");
        Console.WriteLine($"Paged percent-column ink {engine}/{pageHeight}: expected={expected}, actual={actual}, pages={paginator.PageCount}");
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
