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
internal static class CssTableRowGroupOrderChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_ROW_GROUP_ORDER_BASELINE") == "1"; var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760; var gridWidth = width - 76;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("表头表尾按角色排列", "title")); root.Children.Add(Label("切换身份，原文顺序保持不变。", "description"));
            root.Children.Add(Label("普通表格", "section"));
            var table = new Table { CellSpacing = 0 }; Css.SetClass(table, "grid"); var ordinary = new StackPanel(); Css.SetClass(ordinary, "grid");
            var groups = new TableRowGroup[3]; var ordinaryGroups = new StackPanel[3];
            var maps = new List<(TableCell Source, Border Ordinary, int Group, int Row, int Column)>(); var sources = new List<Paragraph>();
            var words = new[] { new[] { new[] { "合计", "2 条" } }, new[] { new[] { "对齐", "已检查" }, new[] { "分页", "原文可选" } }, new[] { new[] { "条目", "状态" } } };
            for (var groupIndex = 0; groupIndex < 3; groupIndex++)
            {
                var group = groups[groupIndex] = new(); table.RowGroups.Add(group); var ordinaryGroup = ordinaryGroups[groupIndex] = new(); ordinary.Children.Add(ordinaryGroup);
                for (var rowIndex = 0; rowIndex < words[groupIndex].Length; rowIndex++)
                {
                    var row = new TableRow(); group.Rows.Add(row); Css.SetClass(row, "row"); var ordinaryRow = new StackPanel(); Css.SetClass(ordinaryRow, "row"); ordinaryGroup.Children.Add(ordinaryRow);
                    for (var column = 0; column < 2; column++)
                    {
                        var source = P(words[groupIndex][rowIndex][column]); sources.Add(source); var cell = new TableCell(source); Css.SetClass(cell, "cell"); row.Cells.Add(cell);
                        var ordinaryCell = new Border { Child = Label(words[groupIndex][rowIndex][column], "body") }; Css.SetClass(ordinaryCell, "cell"); ordinaryRow.Children.Add(ordinaryCell); maps.Add((cell, ordinaryCell, groupIndex, rowIndex, column));
                    }
                }
            }
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(8), PageWidth = width - 60, PageHeight = 128, Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White };
            const string gridStyle = "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}.grid{display:table;width:var(--grid-width);height:var(--grid-height,176px);table-layout:fixed;border-spacing:0;margin:0 auto;}.row{display:table-row;height:44px;}.cell{display:table-cell;padding:8px;border:1px solid #146342;vertical-align:top;}.body{display:block;white-space:pre-wrap;font-family:'PingFang SC';font-size:16px;line-height:24px;text-align:start;}";
            Css.SetStyleSheet(document, gridStyle); Css.SetStyleSheet(ordinary, gridStyle);
            var ordinaryScroll = new ScrollViewer { Content = ordinary, Height = 176, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(ordinaryScroll);
            var selectionBrush = new SolidColorBrush(Color.FromRgb(181, 222, 198));
            var scroll = new FlowDocumentScrollViewer { Document = document, Height = 500, IsToolBarVisible = false, IsInactiveSelectionHighlightEnabled = true, SelectionBrush = selectionBrush, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var pageLabel = Label("", "section pagelabel"); root.Children.Add(pageLabel); var pageHost = new Border { Background = Brushes.White }; Css.SetClass(pageHost, "page"); root.Children.Add(pageHost);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions); var previous = Action("上一页"); var next = Action("下一页"); var swap = Action("切换表头"); actions.Children.Add(previous); actions.Children.Add(next); actions.Children.Add(swap);
            var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, ".root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}.title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:14px;}.section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}.pagelabel{margin-top:16px;}.page{padding:8px;border:1px solid #9ab8a8;border-radius:8px;}.actions{display:flex;gap:12px;margin-top:12px;}.action{min-height:44px;box-sizing:border-box;padding:8px 12px;background:#dceee4;border:1px solid #9ab8a8;border-radius:8px;}.action:hover{background:#c9e4d5;}.action:focus{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:1.5;color:#146342;margin-top:10px;}");
            AutomationProperties.SetName(scroll, "可选择的表格原文"); AutomationProperties.SetName(pageHost, "按表头表尾排列的分页原文");
            var original = document.GetText(); var parents = sources.Select(p => p.Parent).ToArray(); var runs = sources.Select(p => p.Inlines[0]).ToArray();
            UIElement? pageVisual = null; var pageIndex = 0; var swapped = false; var duplicate = false; var hidden = false; var collapsed = false; var rtl = false;
            void Apply()
            {
                var style = $"--grid-width:{gridWidth}px;--grid-height:{(duplicate ? 240 : 176)}px;direction:{(rtl ? "rtl" : "ltr")};"; Css.SetStyle(table, style); Css.SetStyle(ordinary, style); ordinaryScroll.Height = duplicate ? 240 : 176;
                for (var group = 0; group < 3; group++)
                {
                    var header = group == (swapped ? 0 : 2); var footer = group == (swapped ? 2 : 0); var role = header ? "table-header-group" : footer || duplicate ? "table-footer-group" : "table-row-group";
                    var groupStyle = $"display:{role};background:{(header ? "#dceee4" : footer ? "#eef5f0" : "white")};" + (footer ? $"visibility:{(collapsed ? "collapse" : hidden ? "hidden" : "visible")};" : ""); Css.SetStyle(groups[group], groupStyle); Css.SetStyle(ordinaryGroups[group], groupStyle);
                }
            }
            void Page(int index)
            {
                var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator; pageIndex = Math.Clamp(index, 0, Math.Max(0, paginator.PageCount - 1)); pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); pageVisual = (UIElement)paginator.GetPage(pageIndex).Visual!; pageHost.Child = pageVisual;
                pageVisual.GetType().GetProperty("SelectionBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pageVisual, selectionBrush);
                if (scroll.Selection is { } selection) pageVisual.GetType().GetMethod("SetSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pageVisual, [selection, true]);
                pageLabel.Text = $"分页原文 · 第 {pageIndex + 1} 页，共 {paginator.PageCount} 页";
            }
            void Wire(PageAction button, System.Action activate) { button.Activate = activate; button.MouseLeftButtonUp += (_, _) => activate(); button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { activate(); e.Handled = true; } }; }
            Wire(previous, () => Page(pageIndex - 1)); Wire(next, () => Page(pageIndex + 1)); Wire(swap, () => { swapped = !swapped; Apply(); Page(0); });
            byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "duplicate-footer", "swapped", "hidden-footer", "collapsed", "page-2", "rtl-2x", "restored" })
            {
                swapped = false; duplicate = phase == "duplicate-footer"; hidden = phase == "hidden-footer"; collapsed = phase == "collapsed"; rtl = phase == "rtl-2x"; var scale = rtl ? 2 : 1; Apply(); VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                if (phase == "swapped") Press(swap, Key.Enter);
                Layout(root, width, height); Layout(scroll, width - 40, 500); var footerWord = swapped ? "条目" : "合计"; var selected = hidden || collapsed ? "条目" : footerWord; if (!scroll.Find(selected)) throw new InvalidOperationException("Original source search failed"); Page(0);
                if (phase == "page-2") { Press(next, Key.Enter); if (pageIndex != 1) throw new InvalidOperationException("Page action failed"); }
                if (rtl) { Press(next, Key.Space); Press(previous, Key.Space); if (pageIndex != 0) throw new InvalidOperationException("RTL page restoration failed"); }
                Layout(root, width, height); var footerBoxes = Rectangles(pageVisual!, Text(pageVisual!).DocumentRange.FindText(footerWord, false, false)!);
                status.Text = hidden ? "表尾已隐藏，原文仍可搜索" : collapsed ? "表尾已折叠，正文保持原有列宽" : footerBoxes.Length > 0 ? "本页包含表尾原文" : "原文选择保留，表尾位于后续页"; Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale); target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed"); target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_ROW_GROUP_ORDER_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4))); using var file = File.Create(Path.Combine(path, $"table-row-group-order-{engine}-{width}-{phase}.png")); encoder.Save(file); }
                if (baseline) { Console.WriteLine("Before-increment row group order screenshot captured."); return 0; }
                if (document.GetText() != original || scroll.Selection?.Text != selected || !sources.Select(p => p.Parent).SequenceEqual(parents) || !sources.Select(p => p.Inlines[0]).SequenceEqual(runs) || !table.RowGroups.SequenceEqual(groups)) throw new InvalidOperationException("Original source, group identity or selection changed");
                if (footerBoxes.Length > 0 != (!hidden && !collapsed && pageIndex == ((IDocumentPaginatorSource)document).DocumentPaginator.PageCount - 1)) throw new InvalidOperationException("Footer range belongs to another visual page"); AssertPageClip(pageVisual!, Rectangles(pageVisual!, Text(pageVisual!).DocumentRange));
                var body = Descendants(scroll).First(item => item.GetType().Name == "FlowDocumentPresentation"); var records = ((System.Collections.IEnumerable)body.GetType().GetProperty("TableCells", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!).Cast<object>().ToArray(); var paragraphs = (IReadOnlyList<TextBlock>)body.GetType().GetProperty("Paragraphs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
                var bodyHeight = duplicate ? 76 : 44; var footerGroup = swapped ? 2 : 0; var headerGroup = swapped ? 0 : 2;
                foreach (var map in maps)
                {
                    var record = records.Single(item => ReferenceEquals(item.GetType().GetProperty("Source")!.GetValue(item), map.Source)); var frame = (Rect)record.GetType().GetProperty("Bounds")!.GetValue(record)!;
                    var expectedTop = map.Group == headerGroup ? 0 : map.Group == 1 ? 44 + map.Row * bodyHeight : 44 + 2 * bodyHeight; var expectedHeight = map.Group == footerGroup && collapsed ? 0 : map.Group == 1 ? bodyHeight : 44;
                    var ordinaryAt = map.Ordinary.TransformToAncestor(ordinary); if (Math.Abs(frame.Top - expectedTop) > .03 || Math.Abs(frame.Height - expectedHeight) > .03 || Math.Abs(ordinaryAt.Y - expectedTop) > .03 || Math.Abs(map.Ordinary.ActualHeight - expectedHeight) > .03 || Math.Abs(frame.Width - map.Ordinary.ActualWidth) > .03) throw new InvalidOperationException($"Group visual geometry differs: {engine}/{width}/{phase}/{map.Group}/{map.Row}, document={frame}, ordinary={ordinaryAt}/{map.Ordinary.RenderSize}, expected={expectedTop}/{expectedHeight}");
                    if (expectedHeight == 0 || hidden && map.Group == footerGroup) continue;
                    var source = (Paragraph)map.Source.Blocks[0]; var proxy = paragraphs.Single(p => ReferenceEquals(p.Inlines, source.Inlines)); if (Math.Abs(proxy.TransformToAncestor(body).Y - expectedTop - 17) > .03) throw new InvalidOperationException("Original paragraph was not moved with its visual row");
                }
                foreach (var element in new FrameworkElement[] { ordinaryScroll, pageHost, actions, status }) { var at = element.TransformToAncestor(root); if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport"); }
                if (phase is "initial" or "restored") { if (phase == "initial") initial = pixels; else if (!pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored pixels differ"); }
                Console.WriteLine($"Table row-group order {engine}/{width}/{phase}: header={headerGroup}, footer={footerGroup}, bodyHeight={bodyHeight}, pages={((IDocumentPaginatorSource)document).DocumentPaginator.PageCount}, selected={selected}"); passed++;
            }
            foreach (var button in new[] { previous, next, swap }) { var peer = button.GetAutomationPeer()!; if (peer.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrWhiteSpace(peer.GetName()) || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider) throw new InvalidOperationException("Custom action semantics missing"); }
            ((IInvokeProvider)next.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 1) throw new InvalidOperationException("Accessible next-page action failed"); ((IInvokeProvider)previous.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 0) throw new InvalidOperationException("Accessible previous-page action failed"); ((IInvokeProvider)swap.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (!swapped) throw new InvalidOperationException("Accessible role action failed");
            pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); scroll.Document = null;
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello }) foreach (var pageHeight in new[] { 72, 144 }) VerifyPagedInk(context, engine, pageHeight);
        Console.WriteLine($"macOS CSS table row-group order GPU checks: {passed}/32 passed; paginated ink 4/4 passed"); return passed == 32 ? 0 : 1;
    }

    private static void VerifyPagedInk(RenderContext context, RenderingEngine engine, int pageHeight)
    {
        FlowDocument Document(bool constrained)
        {
            var inner = new Table { CellSpacing = 0 };
            for (var rowIndex = 0; rowIndex < 3; rowIndex++) {
                var source = P(string.Join('\n', Enumerable.Range(rowIndex * 4, 4).Select(index => $"第{index:00}行"))); Css.SetStyle(source, "width:320px;");
                var cell = new TableCell(source) { ColumnSpan = 2, Padding = new(0), BorderThickness = new(0) }; var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); inner.RowGroups.Add(group); group.Rows.Add(row);
                Css.SetStyle(group, (rowIndex == 0 ? "display:table-footer-group;" : rowIndex == 2 ? "display:table-header-group;" : "display:table-row-group;") + (constrained && rowIndex != 1 ? "height:25%;" : ""));
            }
            var basis = new TableRow(); foreach (var width in new[] { 40, 120 }) { var empty = new Section(); Css.SetStyle(empty, $"width:{width}px;height:0;"); basis.Cells.Add(new TableCell(empty) { Padding = new(0), BorderThickness = new(0) }); } inner.RowGroups[2].Rows.Add(basis);
            Css.SetStyle(inner, constrained ? "width:auto;height:480px;" : "width:auto;");
            var table = new Table { CellSpacing = 0 }; var outerGroup = new TableRowGroup(); var outerRow = new TableRow(); table.RowGroups.Add(outerGroup); outerGroup.Rows.Add(outerRow); outerRow.Cells.Add(new TableCell(inner) { Padding = new(0), BorderThickness = new(0) });
            var side = new Section(); Css.SetStyle(side, "width:20px;height:0;"); outerRow.Cells.Add(new TableCell(side) { Padding = new(0), BorderThickness = new(0) }); Css.SetStyle(table, "width:auto;");
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black,
                Background = Brushes.White, PagePadding = new(0), PageWidth = 400, PageHeight = constrained ? pageHeight : 600 };
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
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_ROW_GROUP_ORDER_EVIDENCE_DIR") is { Length: > 0 } path)
            {
                Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                using var file = File.Create(Path.Combine(path, $"ink-{engine}-{pageHeight}-{label}.png")); encoder.Save(file);
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
            Console.WriteLine($"Row-group ink page {engine}/{pageHeight}/{index}: ink={ink.Total}, footerInk={ink.Footer}, footer=[{string.Join(';', boxes)}]");
        }
        if (expected.Total == 0 || actual != expected.Total || expected.Footer == 0 || actualFooter != expected.Footer) throw new InvalidOperationException($"Paged glyph/range correspondence differs for {engine}/{pageHeight}: expected={expected}, actual=({actual},{actualFooter})");
        Console.WriteLine($"Paged row-group ink {engine}/{pageHeight}: expected={expected.Total}, actual={actual}, footerInk={actualFooter}, pages={paginator.PageCount}");
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
