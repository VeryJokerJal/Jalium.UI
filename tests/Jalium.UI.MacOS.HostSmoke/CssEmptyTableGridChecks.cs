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
internal static class CssEmptyTableGridChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_EMPTY_TABLE_GRID_BASELINE") == "1"; var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760; var gridWidth = width - 76;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root"); root.Children.Add(Label("空表格也保持预设布局", "title")); root.Children.Add(Label("清空记录后，预设空间和阅读位置仍然保留。", "description")); root.Children.Add(Label("普通表格", "section"));
            var table = new Table { CellSpacing = 0 }; Css.SetClass(table, "grid"); var ordinary = new StackPanel(); Css.SetClass(ordinary, "grid");
            var columns = new[] { new TableColumn(), new TableColumn() }; var ordinaryColumns = new[] { new Border(), new Border() };
            foreach (var column in columns) Css.SetClass(column, "col"); foreach (var column in ordinaryColumns) Css.SetClass(column, "col");
            var groups = new TableRowGroup[3]; var ordinaryGroups = new StackPanel[3]; var sourceRows = new List<TableRow>[3]; var ordinaryRows = new List<StackPanel>[3]; var sources = new List<Paragraph>();
            var words = new[] { new[] { new[] { "合计", "2 条" } }, new[] { new[] { "对齐", "已检查" }, new[] { "分页", "原文可选" } }, new[] { new[] { "条目", "状态" } } };
            for (var g = 0; g < 3; g++)
            {
                var group = groups[g] = new(); table.RowGroups.Add(group); var other = ordinaryGroups[g] = new(); ordinary.Children.Add(other); sourceRows[g] = []; ordinaryRows[g] = [];
                var role = g == 0 ? "table-footer-group" : g == 2 ? "table-header-group" : "table-row-group"; Css.SetStyle(group, "display:" + role + ";"); Css.SetStyle(other, "display:" + role + ";");
                for (var r = 0; r < words[g].Length; r++)
                {
                    var row = new TableRow(); sourceRows[g].Add(row); Css.SetClass(row, "row"); var otherRow = new StackPanel(); ordinaryRows[g].Add(otherRow); Css.SetClass(otherRow, "row");
                    for (var c = 0; c < 2; c++) { var p = P(words[g][r][c]); sources.Add(p); var cell = new TableCell(p); Css.SetClass(cell, "cell"); row.Cells.Add(cell); var otherCell = new Border { Child = Label(words[g][r][c], "body") }; Css.SetClass(otherCell, "cell"); otherRow.Children.Add(otherCell); }
                }
            }
            var emptyRows = new[] { new TableRow(), new TableRow() }; var ordinaryEmptyRows = new[] { new StackPanel(), new StackPanel() }; foreach (var row in emptyRows) Css.SetClass(row, "row"); foreach (var row in ordinaryEmptyRows) Css.SetClass(row, "row");
            var footer = P("末段原文"); sources.Add(footer);
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(8), PageWidth = width - 60, PageHeight = 128, Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White }; document.Blocks.Add(footer);
            const string gridStyle = "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}.grid{display:table;width:var(--grid-width);height:176px;box-sizing:border-box;table-layout:fixed;border-spacing:0;border:2px solid #146342;padding:0;background:#eef5f0;margin:0 auto;}.col{display:table-column;width:var(--column-width);}.row{display:table-row;height:44px;}.cell{display:table-cell;padding:8px;border:1px solid #146342;vertical-align:top;background:#ffffff;}.body{display:block;font-family:'PingFang SC';font-size:16px;line-height:24px;white-space:pre-wrap;text-align:start;}";
            Css.SetStyleSheet(document, gridStyle); Css.SetStyleSheet(ordinary, gridStyle);
            var ordinaryScroll = new ScrollViewer { Content = ordinary, Height = 184, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(ordinaryScroll);
            var selectionBrush = new SolidColorBrush(Color.FromRgb(181, 222, 198)); var scroll = new FlowDocumentScrollViewer { Document = document, Height = 500, IsToolBarVisible = false, IsInactiveSelectionHighlightEnabled = true, SelectionBrush = selectionBrush, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var pageLabel = Label("", "section pagelabel"); root.Children.Add(pageLabel); var pageHost = new Border { Background = Brushes.White }; Css.SetClass(pageHost, "page"); root.Children.Add(pageHost);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions); var previous = Action("上一页"); var next = Action("下一页"); var toggle = Action("添加记录"); actions.Children.Add(previous); actions.Children.Add(next); actions.Children.Add(toggle); var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, ".root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}.title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:14px;}.section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}.pagelabel{margin-top:16px;}.page{padding:8px;border:1px solid #9ab8a8;border-radius:8px;}.actions{display:flex;gap:12px;margin-top:12px;}.action{min-height:44px;box-sizing:border-box;padding:8px 12px;background:#dceee4;border:1px solid #9ab8a8;border-radius:8px;}.action:hover{background:#c9e4d5;}.action:focus{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:1.5;color:#146342;margin-top:10px;}");
            AutomationProperties.SetName(scroll, "保留原文的表格阅读区"); AutomationProperties.SetName(pageHost, "保留空表格空间的分页原文");
            var parents = sources.Select(p => p.Parent).ToArray(); var runs = sources.Select(p => p.Inlines[0]).ToArray(); UIElement? pageVisual = null; var pageIndex = 0; var filled = false; var onlyEmptyRows = false; var collapsed = false; var rtl = false;
            void Apply()
            {
                for (var g = 0; g < 3; g++) { groups[g].Rows.Clear(); ordinaryGroups[g].Children.Clear(); }
                table.Columns.Clear(); foreach (var column in ordinaryColumns) ordinary.Children.Remove(column);
                if (filled) for (var g = 0; g < 3; g++) { foreach (var row in sourceRows[g]) groups[g].Rows.Add(row); foreach (var row in ordinaryRows[g]) ordinaryGroups[g].Children.Add(row); }
                else if (onlyEmptyRows) { foreach (var row in emptyRows) groups[1].Rows.Add(row); foreach (var row in ordinaryEmptyRows) ordinaryGroups[1].Children.Add(row); }
                if (!onlyEmptyRows) { foreach (var column in columns) table.Columns.Add(column); for (var c = 0; c < ordinaryColumns.Length; c++) ordinary.Children.Insert(c, ordinaryColumns[c]); }
                var style = $"--grid-width:{gridWidth}px;--column-width:{(filled ? 100 : 600)}px;direction:{(rtl ? "rtl" : "ltr")};border-collapse:{(collapsed ? "collapse" : "separate")};"; Css.SetStyle(table, style); Css.SetStyle(ordinary, style);
                ((TextBlock)toggle.Child!).Text = filled ? "清空记录" : "添加记录"; AutomationProperties.SetName(toggle, filled ? "清空记录" : "添加记录");
            }
            void Page(int index)
            {
                var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator; pageIndex = Math.Clamp(index, 0, Math.Max(0, paginator.PageCount - 1)); pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); pageVisual = (UIElement)paginator.GetPage(pageIndex).Visual!; pageHost.Child = pageVisual;
                pageVisual.GetType().GetProperty("SelectionBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pageVisual, selectionBrush); if (scroll.Selection is { } selection) pageVisual.GetType().GetMethod("SetSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pageVisual, [selection, true]); pageLabel.Text = $"分页原文 · 第 {pageIndex + 1} 页，共 {paginator.PageCount} 页";
            }
            void Wire(PageAction button, System.Action activate) { button.Activate = activate; button.MouseLeftButtonUp += (_, _) => activate(); button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { activate(); e.Handled = true; } }; }
            Wire(previous, () => Page(pageIndex - 1)); Wire(next, () => Page(pageIndex + 1)); Wire(toggle, () => { filled = !filled; onlyEmptyRows = false; Apply(); Page(0); });
            byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "empty-groups" } : new[] { "empty-groups", "empty-rows", "filled", "cleared", "collapsed-border", "page-2", "rtl-2x", "restored" })
            {
                filled = phase == "cleared"; onlyEmptyRows = phase == "empty-rows"; collapsed = phase == "collapsed-border"; rtl = phase == "rtl-2x"; var scale = rtl ? 2 : 1; Apply(); VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                if (phase == "filled") Press(toggle, Key.Enter); if (phase == "cleared") Press(toggle, Key.Space); var expectedText = document.GetText(); Layout(root, width, height); Layout(scroll, width - 40, 500); if (!scroll.Find("末段")) throw new InvalidOperationException("Original footer search failed"); Page(0);
                if (phase == "page-2") { Press(next, Key.Enter); if (pageIndex != 1) throw new InvalidOperationException("Empty-space next-page action failed"); }
                if (rtl) { Press(next, Key.Space); Press(previous, Key.Space); if (pageIndex != 0) throw new InvalidOperationException("RTL page restoration failed"); }
                Layout(root, width, height); var footerBoxes = Rectangles(pageVisual!, Text(pageVisual!).DocumentRange.FindText("末段", false, false)!); status.Text = filled ? "记录已添加，原文仍可选择" : footerBoxes.Length > 0 ? "空表格后的原文在本页继续" : onlyEmptyRows ? "尚无单元格，预设空间仍然保留" : "记录尚为空，预设空间仍然保留"; Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale); target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable"); using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed"); target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_EMPTY_TABLE_GRID_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4))); using var file = File.Create(Path.Combine(path, $"empty-table-grid-{engine}-{width}-{phase}.png")); encoder.Save(file); }
                if (baseline) { Console.WriteLine("Before-increment empty table grid screenshot captured."); return 0; }
                if (document.GetText() != expectedText || scroll.Selection?.Text != "末段" || !sources.Select(p => p.Parent).SequenceEqual(parents) || !sources.Select(p => p.Inlines[0]).SequenceEqual(runs)) throw new InvalidOperationException("Original source or selection changed");
                if (((IDocumentPaginatorSource)document).DocumentPaginator.PageCount != 2 || (footerBoxes.Length > 0) != (pageIndex == 1)) throw new InvalidOperationException("Empty grid reserved space or footer page was lost"); AssertPageClip(pageVisual!, Rectangles(pageVisual!, Text(pageVisual!).DocumentRange)); if (pageIndex == 0 && !filled && Text(pageVisual!).RangeFromPoint(pageVisual!.PointToScreen(new(20, 20))) is not null) throw new InvalidOperationException("Empty page has a text hit");
                var body = Descendants(scroll).First(e => e.GetType().Name == "FlowDocumentPresentation"); var geometry = (Dictionary<DependencyObject, Rect>)body.GetType().GetField("_tableGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!; var frame = geometry[table];
                var expectedHeight = filled ? 180 : 176; if (Math.Abs(frame.Width - gridWidth) > .03 || Math.Abs(frame.Height - expectedHeight) > .03 || Math.Abs(ordinary.ActualWidth - gridWidth) > .03 || Math.Abs(ordinary.ActualHeight - expectedHeight) > .03) throw new InvalidOperationException($"Empty grid dimensions differ: {engine}/{width}/{phase}: document={frame}, ordinary={ordinary.RenderSize}, expected={gridWidth}/{expectedHeight}");
                for (var g = 0; g < 3; g++) { var box = geometry[groups[g]]; if (Math.Abs(box.Width - gridWidth + 4) > .03 || Math.Abs(ordinaryGroups[g].ActualWidth - box.Width) > .03) throw new InvalidOperationException("Empty group lost the table content width"); }
                if (!filled && !onlyEmptyRows && (groups.Any(g => g.Rows.Count != 0) || table.Columns.Count != 2 || !table.Columns.SequenceEqual(columns))) throw new InvalidOperationException("Empty layout invented rows or lost column definitions");
                foreach (var element in new FrameworkElement[] { ordinaryScroll, pageHost, actions, status }) { var at = element.TransformToAncestor(root); if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport"); }
                if (phase is "empty-groups" or "restored") { if (phase == "empty-groups") initial = pixels; else if (!pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Cleared grid pixels did not restore"); }
                Console.WriteLine($"Empty table grid {engine}/{width}/{phase}: size={frame.Width}/{frame.Height}, rows={groups.Sum(g => g.Rows.Count)}, pages=2, selected=末段"); passed++;
            }
            foreach (var button in new[] { previous, next, toggle }) { var peer = button.GetAutomationPeer()!; if (peer.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrWhiteSpace(peer.GetName()) || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider) throw new InvalidOperationException("Custom action semantics missing"); }
            ((IInvokeProvider)next.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 1) throw new InvalidOperationException("Accessible paging failed"); ((IInvokeProvider)previous.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 0) throw new InvalidOperationException("Accessible paging restoration failed"); ((IInvokeProvider)toggle.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (!filled) throw new InvalidOperationException("Accessible add failed"); ((IInvokeProvider)toggle.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (filled) throw new InvalidOperationException("Accessible clear failed"); pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); scroll.Document = null;
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello }) foreach (var pageHeight in new[] { 72, 144 }) foreach (var state in new[] { "empty", "filled", "cleared" }) VerifyPagedInk(context, engine, pageHeight, state);
        Console.WriteLine($"macOS CSS empty table grid GPU checks: {passed}/32 passed; paginated ink 12/12 passed"); return passed == 32 ? 0 : 1;
    }
    private static void VerifyPagedInk(RenderContext context, RenderingEngine engine, int pageHeight, string state)
    {
        FlowDocument Document(bool constrained)
        {
            var table = new Table { CellSpacing = 0 }; var columns = new[] { new TableColumn(), new TableColumn() }; foreach (var column in columns) table.Columns.Add(column);
            var footerGroup = new TableRowGroup(); var headerGroup = new TableRowGroup(); var body = new TableRowGroup(); table.RowGroups.Add(footerGroup); table.RowGroups.Add(headerGroup); table.RowGroups.Add(body);
            Css.SetStyle(footerGroup, "display:table-footer-group;"); Css.SetStyle(headerGroup, "display:table-header-group;");
            var row = new TableRow(); foreach (var words in new[] { "原文甲", "原文乙" }) { var cell = new TableCell(P(words)) { Padding = new(0), BorderThickness = new(0) }; Css.SetStyle(cell, "vertical-align:top;"); row.Cells.Add(cell); }
            if (state != "empty") body.Rows.Add(row); if (state == "cleared") body.Rows.Clear(); foreach (var column in columns) Css.SetStyle(column, $"width:{(state == "filled" ? 100 : 600)}px;"); Css.SetStyle(table, "width:400px;height:176px;table-layout:fixed;");
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new(0), PageWidth = 400, PageHeight = constrained ? pageHeight : 600 };
            document.Blocks.Add(P("末段")); Css.SetStyleSheet(document, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}"); return document;
        }
        (int Total, int Footer) Ink(UIElement page, int height, string label, Rect[] footer)
        {
            const int width = 400;
            using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Ink renderer unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Ink readback request failed");
            target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { page.Render(drawing); } finally { target.EndDraw(); }
            var pixels = new byte[width * height * 4]; if (target.FetchReadback(pixels, width * 4, out var actualWidth, out var actualHeight) != JaliumResult.Ok || actualWidth != width || actualHeight != height) throw new InvalidOperationException("Ink readback failed");
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_EMPTY_TABLE_GRID_EVIDENCE_DIR") is { Length: > 0 } path)
            {
                Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                using var file = File.Create(Path.Combine(path, $"ink-{engine}-{pageHeight}-{state}-{label}.png")); encoder.Save(file);
            }
            var total = 0; var footerInk = 0;
            for (var index = 0; index < width * height; index++)
            {
                if (pixels[index * 4] != 0 || pixels[index * 4 + 1] != 0 || pixels[index * 4 + 2] != 0 || pixels[index * 4 + 3] != 255) continue;
                total++; if (footer.Any(box => box.Contains(new Point(index % width + .5, index / width + .5)))) footerInk++;
            }
            return (total, footerInk);
        }
        var reference = Document(false); var referencePaginator = ((IDocumentPaginatorSource)reference).DocumentPaginator;
        using var referencePage = (IDisposable)referencePaginator.GetPage(0).Visual!;
        var referenceBoxes = Rectangles((UIElement)referencePage, Text((UIElement)referencePage).DocumentRange.FindText("末段", false, false)!);
        var expected = Ink((UIElement)referencePage, 600, "reference", referenceBoxes); var actual = 0; var actualFooter = 0;
        var constrained = Document(true); var paginator = ((IDocumentPaginatorSource)constrained).DocumentPaginator;
        for (var index = 0; index < paginator.PageCount; index++)
        {
            using var page = (IDisposable)paginator.GetPage(index).Visual!; var visual = (UIElement)page; var provider = Text(visual);
            var boxes = Rectangles(visual, provider.DocumentRange.FindText("末段", false, false)!);
            if (boxes.Length > 0 != (index == paginator.PageCount - 1)) throw new InvalidOperationException($"Footer range belongs to another page: {engine}/{pageHeight}/{index} [{string.Join(';', boxes)}]");
            AssertPageClip(visual, Rectangles(visual, provider.DocumentRange));
            var visible = string.Concat(provider.GetVisibleRanges().Select(range => range.GetText(-1)));
            if (visible.Contains("末段") != (index == paginator.PageCount - 1)) throw new InvalidOperationException("Visible page range does not match the original footer");
            if (Rectangles(visual, provider.DocumentRange).Length == 0 && provider.RangeFromPoint(visual.PointToScreen(new(20, 20))) is not null) throw new InvalidOperationException("Blank page returned a text hit");
            var ink = Ink(visual, pageHeight, "page-" + index, boxes); actual += ink.Total; actualFooter += ink.Footer;
            Console.WriteLine($"Empty-grid ink page {engine}/{pageHeight}/{state}/{index}: ink={ink.Total}, footerInk={ink.Footer}, footer=[{string.Join(';', boxes)}]");
        }
        if (expected.Total == 0 || actual != expected.Total || expected.Footer == 0 || actualFooter != expected.Footer) throw new InvalidOperationException($"Paged glyph/range correspondence differs for {engine}/{pageHeight}: expected={expected}, actual=({actual},{actualFooter})");
        Console.WriteLine($"Paged empty-grid ink {engine}/{pageHeight}/{state}: expected={expected.Total}, actual={actual}, footerInk={actualFooter}, pages={paginator.PageCount}");
    }
    private static ITextProvider Text(UIElement page) => (ITextProvider)page.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
    private static Rect[] Rectangles(UIElement page, ITextRangeProvider range) => range.GetBoundingRectangles().Chunk(4).Select(box => new Rect(page.PointFromScreen(new(box[0], box[1])), page.PointFromScreen(new(box[0] + box[2], box[1] + box[3])))).ToArray();
    private static void AssertPageClip(UIElement page, Rect[] boxes)
    {
        var clip = ((Geometry)page.GetType().GetMethod("GetChildLayoutClip", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!).Bounds;
        foreach (var box in boxes) if (box.Left < clip.Left - .01 || box.Top < clip.Top - .01 || box.Right > clip.Right + .01 || box.Bottom > clip.Bottom + .01) throw new InvalidOperationException($"Text {box} is outside the current page {clip}");
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
