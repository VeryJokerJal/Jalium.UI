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
internal static class CssPageTextGeometryChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_PAGE_TEXT_GEOMETRY_BASELINE") == "1"; var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760; var limit = width - 76;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("分页文字定位当前页", "title"));
            root.Children.Add(Label("原文不变，选择随分页边界更新。", "description"));
            root.Children.Add(Label("原文布局 · 内外两层共同展开", "section"));
            var table = new Table { CellSpacing = 0 }; Css.SetClass(table, "grid outer"); var group = new TableRowGroup(); table.RowGroups.Add(group); var outerRow = new TableRow(); group.Rows.Add(outerRow);
            var inner = new Table { CellSpacing = 0 }; Css.SetClass(inner, "grid inner"); var innerGroup = new TableRowGroup(); inner.RowGroups.Add(innerGroup);
            var firstColumn = new TableColumn(); inner.Columns.Add(firstColumn); var secondColumn = new TableColumn(); inner.Columns.Add(secondColumn); Css.SetClass(firstColumn, "inner-column"); Css.SetClass(secondColumn, "auto-column");
            var ordinary = new StackPanel(); Css.SetClass(ordinary, "grid outer"); var ordinaryOuterRow = new StackPanel(); Css.SetClass(ordinaryOuterRow, "row"); ordinary.Children.Add(ordinaryOuterRow);
            var ordinaryInner = new StackPanel(); Css.SetClass(ordinaryInner, "grid inner"); var ordinaryColumn = new Border(); Css.SetClass(ordinaryColumn, "inner-column"); ordinaryInner.Children.Add(ordinaryColumn);
            var container = new TableCell(inner); Css.SetClass(container, "cell outer-cell"); outerRow.Cells.Add(container);
            var ordinaryContainer = new Border { Child = ordinaryInner }; Css.SetClass(ordinaryContainer, "cell outer-cell"); ordinaryOuterRow.Children.Add(ordinaryContainer);
            var maps = new List<(TableCell Source, Border Ordinary, string Role, int Row, int Column)> { (container, ordinaryContainer, "container", 0, 0) };
            var sources = new List<Paragraph>();
            for (var rowIndex = 0; rowIndex < 2; rowIndex++)
            {
                var row = new TableRow(); innerGroup.Rows.Add(row); var ordinaryRow = new StackPanel(); Css.SetClass(ordinaryRow, "row"); ordinaryInner.Children.Add(ordinaryRow);
                for (var columnIndex = 0; columnIndex < 2; columnIndex++)
                {
                    var words = (columnIndex == 0 ? "内容" : "详情") + (rowIndex + 1); var css = rowIndex == 1 && columnIndex == 0 ? "grow" : rowIndex == columnIndex ? "copy" : "wide";
                    var source = P(words); sources.Add(source); Css.SetClass(source, css); var cell = new TableCell(source); Css.SetClass(cell, "cell"); row.Cells.Add(cell);
                    var ordinaryCell = new Border { Child = Label(words, "body " + css) }; Css.SetClass(ordinaryCell, "cell"); ordinaryRow.Children.Add(ordinaryCell); maps.Add((cell, ordinaryCell, "inner", rowIndex, columnIndex));
                }
            }
            var side = P("外侧原文"); sources.Add(side); Css.SetClass(side, "outside"); var sideCell = new TableCell(side); Css.SetClass(sideCell, "cell"); outerRow.Cells.Add(sideCell);
            var ordinarySide = new Border { Child = Label("外侧原文", "body outside") }; Css.SetClass(ordinarySide, "cell"); ordinaryOuterRow.Children.Add(ordinarySide); maps.Add((sideCell, ordinarySide, "side", 0, 1));
            var footer = P("末段原文"); sources.Add(footer);
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(8), PageWidth = width - 60, PageHeight = 160,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White }; document.Blocks.Add(footer);
            Css.SetStyleSheet(document, """
                FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}.grid{width:auto;table-layout:auto;}.outer{max-width:var(--limit);margin:0 auto;}
                .inner-column{width:var(--inner-width,auto);visibility:var(--inner-visibility,visible);}.auto-column{width:auto;}
                .copy{width:32px;}.wide{width:82px;}.grow{width:var(--grow,82px);}.outside{width:40px;}
                .cell{padding:8px;border:1px solid #146342;vertical-align:top;background:#dceee4;}.outer-cell{background:white;}
                """);
            var ordinaryScroll = new ScrollViewer { Content = ordinary, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(ordinaryScroll);
            var selectionBrush = new SolidColorBrush(Color.FromRgb(181, 222, 198));
            var scroll = new FlowDocumentScrollViewer { Document = document, Height = 200, IsToolBarVisible = false, IsInactiveSelectionHighlightEnabled = true,
                SelectionBrush = selectionBrush, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var pageLabel = Label("", "section pagelabel"); root.Children.Add(pageLabel);
            var pageHost = new Border { Background = Brushes.White }; Css.SetClass(pageHost, "page"); root.Children.Add(pageHost);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var previous = Action("上一页"); var next = Action("下一页"); var ratio = Action("加宽内容"); actions.Children.Add(previous); actions.Children.Add(next); actions.Children.Add(ratio);
            var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:14px;}
                .section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}.pagelabel{margin-top:16px;}.page{padding:8px;border:1px solid #9ab8a8;border-radius:8px;}
                .actions{display:flex;gap:12px;margin-top:12px;}.action{min-height:44px;box-sizing:border-box;padding:8px 12px;background:#dceee4;border:1px solid #9ab8a8;border-radius:8px;}
                .action:hover{background:#c9e4d5;}.action:focus{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:1.5;color:#146342;margin-top:10px;}
                .grid{display:table;width:auto;table-layout:auto;}.outer{max-width:var(--limit);margin:0 auto;}.row{display:table-row;}
                .cell{display:table-cell;padding:8px;border:1px solid #146342;background:#dceee4;vertical-align:top;}.outer-cell{background:white;}
                .inner-column{display:table-column;width:var(--inner-width,auto);visibility:var(--inner-visibility,visible);}
                .body{display:block;white-space:pre-wrap;line-height:24px;text-align:start;}.copy{width:32px;}.wide{width:82px;}.grow{width:var(--grow,82px);}.outside{width:40px;}
                """);
            AutomationProperties.SetName(scroll, "可选择的文档原文"); AutomationProperties.SetName(pageHost, "保留嵌套原文的分页内容");
            var original = document.GetText(); var parents = sources.Select(source => source.Parent).ToArray(); var runs = sources.Select(source => source.Inlines[0]).ToArray();
            UIElement? pageVisual = null; var pageIndex = 0; var growWidth = 82; var fixedColumn = false; var collapsed = false; var rtl = false;
            void Apply()
            {
                var style = $"--limit:{limit}px;--grow:{growWidth}px;--inner-width:{(fixedColumn ? "140px" : "auto")};--inner-visibility:{(collapsed ? "collapse" : "visible")};direction:{(rtl ? "rtl" : "ltr")};";
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
            Wire(previous, () => Page(pageIndex - 1)); Wire(next, () => Page(pageIndex + 1)); Wire(ratio, () => { growWidth = growWidth == 82 ? 102 : 82; Apply(); });
            byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "larger-inner", "fixed-column", "collapsed", "page-2", "rtl-2x", "restored" })
            {
                rtl = phase == "rtl-2x"; collapsed = phase == "collapsed"; growWidth = 82; fixedColumn = phase == "fixed-column"; var scale = rtl ? 2 : 1; Apply(); VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                if (phase == "larger-inner") Press(ratio, Key.Enter);
                Layout(root, width, height); Layout(scroll, width - 40, 200); var selected = collapsed ? "详情1" : "末段";
                if (!scroll.Find(selected)) throw new InvalidOperationException("Original source search failed"); Page(0);
                if (phase == "page-2") { Press(next, Key.Enter); if (pageIndex != 1) throw new InvalidOperationException("Page key action failed"); }
                if (rtl) { Press(next, Key.Space); if (pageIndex != 1) throw new InvalidOperationException("RTL page key action failed"); Press(previous, Key.Space); if (pageIndex != 0) throw new InvalidOperationException("RTL page restoration failed"); }
                Layout(root, width, height);
                var pageBoxes = Rectangles(pageVisual!, Text(pageVisual!).DocumentRange.FindText("末段", false, false)!);
                status.Text = pageBoxes.Length > 0 ? "本页已定位末段原文" : "末段位于下一页，原文选择已保留";
                Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_PAGE_TEXT_GEOMETRY_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var file = File.Create(Path.Combine(path, $"page-text-geometry-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment page text geometry screenshot captured."); return 0; }
                if (pageBoxes.Length > 0 != (pageIndex == 1)) throw new InvalidOperationException($"Footer geometry belongs to another page: {engine}/{width}/{phase} [{string.Join(';', pageBoxes)}]");
                AssertPageClip(pageVisual!, pageBoxes);
                if (document.GetText() != original || scroll.Selection?.Text != selected || !sources.Select(source => source.Parent).SequenceEqual(parents) || !sources.Select(source => source.Inlines[0]).SequenceEqual(runs)) throw new InvalidOperationException("Original text, parents or selection changed");
                var body = Descendants(scroll).First(item => item.GetType().Name == "FlowDocumentPresentation");
                var provider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!; var found = provider.DocumentRange.FindText(selected, false, false)!.GetBoundingRectangles();
                if (found.Length != 4 || Math.Abs(found[3] - 24) > .03) throw new InvalidOperationException("Found original text geometry was clipped");
                var paragraphs = (IReadOnlyList<TextBlock>)body.GetType().GetProperty("Paragraphs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
                var records = ((System.Collections.IEnumerable)body.GetType().GetProperty("TableCells", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!).Cast<object>().ToArray();
                var innerFirst = fixedColumn ? Math.Max(100, Math.Min(140, limit - 176)) : growWidth + 18d;
                var expected = new[] { collapsed ? 0 : innerFirst, 100d }; var containerWidth = expected.Sum() + 18;
                if (records.Length != 6 || paragraphs.Count != sources.Count) throw new InvalidOperationException("Original nested table sources were duplicated");
                foreach (var map in maps)
                {
                    var record = records.Single(record => ReferenceEquals(record.GetType().GetProperty("Source")!.GetValue(record), map.Source));
                    var frame = (Rect)record.GetType().GetProperty("Bounds")!.GetValue(record)!;
                    var expectedWidth = map.Role == "container" ? containerWidth : map.Role == "side" ? 58 : expected[map.Column];
                    if (Math.Abs(frame.Width - expectedWidth) > .03 || Math.Abs(map.Ordinary.ActualWidth - expectedWidth) > .03)
                        throw new InvalidOperationException($"Nested table parity differs at {map.Role}/{map.Row}/{map.Column}: ordinary={map.Ordinary.ActualWidth}, document={frame.Width}, expected={expectedWidth}");
                    if (map.Role == "container" || collapsed && map.Role == "inner" && map.Column == 0) continue;
                    var source = (Paragraph)map.Source.Blocks[0]; var proxy = paragraphs.Single(paragraph => ReferenceEquals(paragraph.Inlines, source.Inlines));
                    var contentWidth = map.Role == "side" ? 40 : map.Row == 1 && map.Column == 0 ? growWidth : map.Row == map.Column ? 32 : 82;
                    var x = rtl ? frame.Right - 9 - contentWidth + 8 : frame.Left + 17;
                    if (Math.Abs(proxy.TransformToAncestor(body).X - x) > .03) throw new InvalidOperationException($"Original nested paragraph placement differs: {proxy.TransformToAncestor(body)} / expected {x}");
                }
                foreach (var element in new FrameworkElement[] { ordinaryScroll, pageHost, actions, status })
                { var at = element.TransformToAncestor(root); if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport"); }
                if (phase is "initial" or "restored")
                {
                    var top = ordinaryScroll.TransformToAncestor(root).Y; var bottom = pageHost.TransformToAncestor(root).Y + pageHost.ActualHeight; var crop = new List<byte>();
                    for (var y = (int)Math.Floor(top); y < Math.Min(height, Math.Ceiling(bottom)); y++) for (var x = 20; x < width - 20; x++) for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                    if (phase == "initial") initial = crop.ToArray(); else if (!crop.SequenceEqual(initial!)) throw new InvalidOperationException("Restored document pixels differ");
                }
                Console.WriteLine($"Page text geometry {engine}/{width}/{phase}: columns={string.Join('/', expected)}, pages={((IDocumentPaginatorSource)document).DocumentPaginator.PageCount}, selected={selected}"); passed++;
            }
            foreach (var button in new[] { previous, next, ratio })
            { var peer = button.GetAutomationPeer()!; if (peer.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrWhiteSpace(peer.GetName()) || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider) throw new InvalidOperationException("Custom action semantics missing"); }
            ((IInvokeProvider)next.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 1) throw new InvalidOperationException("Accessible paging failed");
            ((IInvokeProvider)previous.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 0) throw new InvalidOperationException("Accessible paging restoration failed");
            ((IInvokeProvider)ratio.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); Layout(root, width, height); if (growWidth != 102) throw new InvalidOperationException("Accessible nested content action failed");
            pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); scroll.Document = null;
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello }) foreach (var pageHeight in new[] { 72, 144 }) VerifyPagedInk(context, engine, pageHeight);
        Console.WriteLine($"macOS CSS page text geometry GPU checks: {passed}/28 passed; paginated ink 4/4 passed"); return passed == 28 ? 0 : 1;
    }

    private static void VerifyPagedInk(RenderContext context, RenderingEngine engine, int pageHeight)
    {
        FlowDocument Document(bool constrained)
        {
            var inner = new Table { CellSpacing = 0 };
            for (var rowIndex = 0; rowIndex < 3; rowIndex++) {
                var source = P(string.Join('\n', Enumerable.Range(rowIndex * 4, 4).Select(index => $"第{index:00}行"))); Css.SetStyle(source, "width:320px;");
                var cell = new TableCell(source) { ColumnSpan = 2, Padding = new(0), BorderThickness = new(0) }; var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); inner.RowGroups.Add(group); group.Rows.Add(row);
                if (constrained && rowIndex != 1) Css.SetStyle(group, "height:25%;");
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
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_PAGE_TEXT_GEOMETRY_EVIDENCE_DIR") is { Length: > 0 } path)
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
            Console.WriteLine($"Page-text ink page {engine}/{pageHeight}/{index}: ink={ink.Total}, footerInk={ink.Footer}, footer=[{string.Join(';', boxes)}]");
        }
        if (expected.Total == 0 || actual != expected.Total || expected.Footer == 0 || actualFooter != expected.Footer) throw new InvalidOperationException($"Paged glyph/range correspondence differs for {engine}/{pageHeight}: expected={expected}, actual=({actual},{actualFooter})");
        Console.WriteLine($"Paged page-text ink {engine}/{pageHeight}: expected={expected.Total}, actual={actual}, footerInk={actualFooter}, pages={paginator.PageCount}");
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
