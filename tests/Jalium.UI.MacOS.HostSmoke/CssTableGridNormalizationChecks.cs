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
internal static class CssTableGridNormalizationChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_GRID_NORMALIZATION_BASELINE") == "1"; var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760; var gridWidth = width - 76;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("不齐的行也保持对齐", "title")); root.Children.Add(Label("记录暂缺时，背景和原文仍保持对齐。", "description")); root.Children.Add(Label("普通表格", "section"));
            var table = new Table { CellSpacing = 0 }; Css.SetClass(table, "grid"); var group = new TableRowGroup(); table.RowGroups.Add(group);
            var ordinary = new StackPanel(); Css.SetClass(ordinary, "grid"); var ordinaryGroup = new StackPanel(); Css.SetClass(ordinaryGroup, "group"); ordinary.Children.Add(ordinaryGroup);
            var first = new TableRow(); var lower = new TableRow(); Css.SetClass(first, "row"); Css.SetClass(lower, "row lower");
            var ordinaryFirst = new StackPanel(); var ordinaryLower = new StackPanel(); Css.SetClass(ordinaryFirst, "row"); Css.SetClass(ordinaryLower, "row lower");
            var words = new[] { "汇总", "原文", "甲项", "乙项", "丙项" }; var sources = words.Select(P).ToList();
            var cells = sources.Select(p => new TableCell(p)).ToArray(); var ordinaryCells = words.Select(text => new Border { Child = Label(text, "body") }).ToArray();
            foreach (var cell in cells) Css.SetClass(cell, "cell"); foreach (var cell in ordinaryCells) Css.SetClass(cell, "cell");
            first.Cells.Add(cells[0]); first.Cells.Add(cells[1]); ordinaryFirst.Children.Add(ordinaryCells[0]); ordinaryFirst.Children.Add(ordinaryCells[1]);
            var empty = new TableCell(); var ordinaryEmpty = new Border(); Css.SetClass(empty, "cell empty"); Css.SetClass(ordinaryEmpty, "cell empty");
            var columns = Enumerable.Range(0, 4).Select(_ => new TableColumn()).ToArray(); var ordinaryColumns = Enumerable.Range(0, 4).Select(_ => new Border()).ToArray();
            foreach (var column in columns) Css.SetClass(column, "col"); foreach (var column in ordinaryColumns) Css.SetClass(column, "col");
            var footer = P("末段原文"); sources.Add(footer);
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(8), PageWidth = width - 60, PageHeight = 96, Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White }; document.Blocks.Add(footer);
            const string gridSheet = "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}.grid{display:table;width:var(--grid-width);table-layout:auto;border-spacing:8px;border:2px solid #146342;padding:0;box-sizing:border-box;background:#eef5f0;margin:0 auto;}.group{display:table-row-group;}.row{display:table-row;height:44px;}.lower{background:#dceee4;}.cell{display:table-cell;padding:8px;border:1px solid #146342;vertical-align:top;background:white;}.empty{empty-cells:hide;background:transparent;}.col{display:table-column;width:25%;}.body{display:block;font-family:'PingFang SC';font-size:16px;line-height:24px;white-space:pre-wrap;text-align:start;}";
            Css.SetStyleSheet(document, gridSheet); Css.SetStyleSheet(ordinary, gridSheet);
            var ordinaryScroll = new ScrollViewer { Content = ordinary, Height = 184, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(ordinaryScroll);
            var selectionBrush = new SolidColorBrush(Color.FromRgb(181, 222, 198)); var scroll = new FlowDocumentScrollViewer { Document = document, Height = 500, IsToolBarVisible = false, IsInactiveSelectionHighlightEnabled = true, SelectionBrush = selectionBrush, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var pageLabel = Label("", "section pagelabel"); root.Children.Add(pageLabel); var pageHost = new Border { Background = Brushes.White }; Css.SetClass(pageHost, "page"); root.Children.Add(pageHost);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions); var previous = Action("上一页"); var next = Action("下一页"); var toggle = Action("补齐记录"); actions.Children.Add(previous); actions.Children.Add(next); actions.Children.Add(toggle); var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, ".root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}.title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:14px;}.section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}.pagelabel{margin-top:16px;}.page{padding:8px;border:1px solid #9ab8a8;border-radius:8px;}.actions{display:flex;gap:12px;margin-top:12px;}.action{min-height:44px;box-sizing:border-box;padding:8px 12px;background:#dceee4;border:1px solid #9ab8a8;border-radius:8px;}.action:hover{background:#c9e4d5;}.action:focus{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:1.5;color:#146342;margin-top:10px;}");
            AutomationProperties.SetName(scroll, "保留原文的表格阅读区"); AutomationProperties.SetName(pageHost, "记录表格的分页原文");
            var parents = sources.Select(p => p.Parent).ToArray(); var runs = sources.Select(p => p.Inlines[0]).ToArray(); UIElement? pageVisual = null; var pageIndex = 0; var filled = false; var ragged = false; var intrinsic = false; var rtl = false;
            void Apply()
            {
                group.Rows.Clear(); ordinaryGroup.Children.Clear(); table.Columns.Clear(); foreach (var column in ordinaryColumns) ordinary.Children.Remove(column);
                lower.Cells.Clear(); lower.Cells.Add(empty); ordinaryLower.Children.Clear(); ordinaryLower.Children.Add(ordinaryEmpty);
                if (filled) for (var i = 2; i < cells.Length; i++) { lower.Cells.Add(cells[i]); ordinaryLower.Children.Add(ordinaryCells[i]); }
                cells[0].ColumnSpan = ragged ? 3 : 10; Grid.SetColumnSpan(ordinaryCells[0], ragged ? 3 : 10); group.Rows.Add(first); ordinaryGroup.Children.Add(ordinaryFirst);
                if (ragged) { group.Rows.Add(lower); ordinaryGroup.Children.Add(ordinaryLower); for (var i = 0; i < columns.Length; i++) { table.Columns.Add(columns[i]); ordinary.Children.Insert(i, ordinaryColumns[i]); } }
                var style = $"--grid-width:{(intrinsic ? "auto" : gridWidth + "px")};direction:{(rtl ? "rtl" : "ltr")};border-collapse:{(rtl ? "collapse" : "separate")};"; Css.SetStyle(table, style); Css.SetStyle(ordinary, style);
                ((TextBlock)toggle.Child!).Text = filled ? "收起记录" : "补齐记录"; AutomationProperties.SetName(toggle, filled ? "收起记录" : "补齐记录");
            }
            void Page(int index)
            {
                var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator; pageIndex = Math.Clamp(index, 0, Math.Max(0, paginator.PageCount - 1)); pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); pageVisual = (UIElement)paginator.GetPage(pageIndex).Visual!; pageHost.Child = pageVisual;
                pageVisual.GetType().GetProperty("SelectionBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pageVisual, selectionBrush); if (scroll.Selection is { } selection) pageVisual.GetType().GetMethod("SetSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pageVisual, [selection, true]); pageLabel.Text = $"分页原文 · 第 {pageIndex + 1} 页，共 {paginator.PageCount} 页";
            }
            void Wire(PageAction button, System.Action activate) { button.Activate = activate; button.MouseLeftButtonUp += (_, _) => activate(); button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { activate(); e.Handled = true; } }; }
            Wire(previous, () => Page(pageIndex - 1)); Wire(next, () => Page(pageIndex + 1)); Wire(toggle, () => { filled = !filled; ragged = true; intrinsic = false; Apply(); Page(0); }); byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "intrinsic", "missing", "filled", "cleared", "page-2", "rtl-2x", "restored" })
            {
                ragged = phase is not ("initial" or "intrinsic" or "restored"); intrinsic = phase == "intrinsic"; rtl = phase == "rtl-2x"; filled = phase == "cleared"; var scale = rtl ? 2 : 1; Apply(); VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                if (phase == "filled") Press(toggle, Key.Enter); if (phase == "cleared") ((IInvokeProvider)toggle.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (rtl) { Press(toggle, Key.Space); Press(toggle, Key.Space); }
                var expectedText = document.GetText(); Layout(root, width, height); Layout(scroll, width - 40, 500); if (!scroll.Find("末段")) throw new InvalidOperationException("Original footer search failed"); Page(0);
                if (phase == "page-2") { Press(next, Key.Enter); if (pageIndex != 1) throw new InvalidOperationException("Next-page action failed"); }
                if (rtl) { Press(next, Key.Space); Press(previous, Key.Space); if (pageIndex != 0) throw new InvalidOperationException("RTL page restoration failed"); }
                status.Text = filled ? "记录已补齐，原文仍可选择" : ragged ? "本行暂缺记录，背景与列仍然对齐" : "汇总与原文各占一列，宽度保持均衡"; Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale); target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable"); using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed"); target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var actualWidth, out var actualHeight) != JaliumResult.Ok || actualWidth != width * scale || actualHeight != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_GRID_NORMALIZATION_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(actualWidth, actualHeight, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, actualWidth * 4))); using var file = File.Create(Path.Combine(path, $"table-grid-{engine}-{width}-{phase}.png")); encoder.Save(file); }
                if (baseline) { Console.WriteLine("Before-increment table grid screenshot captured."); return 0; }
                var body = Descendants(scroll).First(item => item.GetType().Name == "FlowDocumentPresentation"); var geometry = (Dictionary<DependencyObject, Rect>)body.GetType().GetField("_tableGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
                var expectedWidth = intrinsic ? 128 : gridWidth; if (Math.Abs(geometry[table].Width - expectedWidth) > .04 || Math.Abs(ordinary.ActualWidth - expectedWidth) > .04) throw new InvalidOperationException($"Table width differs: {geometry[table].Width}/{ordinary.ActualWidth}, expected={expectedWidth}; nativeCells={geometry[cells[0]].Width}/{geometry[cells[1]].Width}; ordinaryCells={ordinaryCells[0].ActualWidth}/{ordinaryCells[1].ActualWidth}; sourceFonts={sources[0].FontSize}/{sources[0].FontFamily}; ordinaryFonts={((TextBlock)ordinaryCells[0].Child!).FontSize}/{((TextBlock)ordinaryCells[0].Child!).FontFamily}");
                foreach (var index in filled ? Enumerable.Range(0, cells.Length) : new[] { 0, 1 }) if (Math.Abs(geometry[cells[index]].Width - ordinaryCells[index].ActualWidth) > .04) throw new InvalidOperationException("Table adapter cell widths differ");
                if (!ragged && Math.Abs(geometry[cells[0]].Width - geometry[cells[1]].Width) > .04) throw new InvalidOperationException("Merged columns are not balanced");
                if (ragged && Math.Abs(geometry[lower].Height - 44) > .04) throw new InvalidOperationException("Missing-cell row lost its height");
                if (document.GetText() != expectedText || !sources.Select(p => p.Parent).SequenceEqual(parents) || !sources.Select(p => p.Inlines[0]).SequenceEqual(runs) || scroll.Selection?.Text != "末段") throw new InvalidOperationException("Original text identity or selection changed");
                if (ragged && !filled)
                {
                    var at = ordinaryLower.TransformToAncestor(root); var last = ordinaryCells[1].TransformToAncestor(root); Pixel(pixels, actualWidth, scale, last.X + ordinaryCells[1].ActualWidth / 2, at.Y + 22, 220, 238, 228);
                    var pageTop = (double)pageVisual!.GetType().GetField("_pageTop", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pageVisual)!; var rowBounds = geometry[lower]; var start = Math.Max(pageTop, rowBounds.Top); var end = Math.Min(pageTop + 80, rowBounds.Bottom);
                    if (end - start > 3) { var pageAt = ((FrameworkElement)pageVisual).TransformToAncestor(root); var x = geometry[cells[1]].Left + geometry[cells[1]].Width / 2; Pixel(pixels, actualWidth, scale, pageAt.X + 8 + x, pageAt.Y + 8 + (start + end) / 2 - pageTop, 220, 238, 228); }
                }
                foreach (var element in new FrameworkElement[] { ordinaryScroll, pageHost, actions, status }) { var at = element.TransformToAncestor(root); if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport"); }
                if (phase == "initial") initial = pixels; if (phase == "restored" && !pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored pixels differ");
                Console.WriteLine($"Table grid normalization {engine}/{width}/{phase}: table={geometry[table].Width}, first={geometry[cells[0]].Width}, last={geometry[cells[1]].Width}, pages={((IDocumentPaginatorSource)document).DocumentPaginator.PageCount}"); passed++;
                if (phase == "initial")
                {
                    Press(toggle, Key.Enter); if (!ragged || !filled || group.Rows.Count != 2 || lower.Cells.Count != 4) throw new InvalidOperationException("Initial record action did not expose the original records");
                    Press(toggle, Key.Space); if (filled || lower.Cells.Count != 1) throw new InvalidOperationException("Initial record action did not restore the missing row");
                }
            }
            pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); scroll.Document = null;
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello }) foreach (var pageHeight in new[] { 72, 144 }) foreach (var state in new[] { "empty", "filled", "cleared" }) VerifyPagedInk(context, engine, pageHeight, state);
        Console.WriteLine($"macOS CSS table grid normalization GPU checks: {passed}/32 passed; paginated ink 12/12 passed"); return passed == 32 ? 0 : 1;
    }
    private static void Pixel(byte[] pixels, int width, int scale, double x, double y, byte red, byte green, byte blue)
    { var at = ((int)(y * scale) * width + (int)(x * scale)) * 4; if (at < 0 || at + 3 >= pixels.Length || pixels[at] != blue || pixels[at + 1] != green || pixels[at + 2] != red || pixels[at + 3] != 255) throw new InvalidOperationException($"Missing-cell background differs at {x}/{y}"); }
    private static void VerifyPagedInk(RenderContext context, RenderingEngine engine, int pageHeight, string state)
    {
        FlowDocument Document(bool constrained)
        {
            var table = new Table { CellSpacing = 0 }; for (var i = 0; i < 4; i++) { var col = new TableColumn(); Css.SetStyle(col, "width:25%;"); table.Columns.Add(col); }
            var group = new TableRowGroup(); table.RowGroups.Add(group); var first = new TableRow(); var lower = new TableRow(); group.Rows.Add(first); group.Rows.Add(lower);
            foreach (var words in new[] { "汇总", "原文" }) { var cell = new TableCell(P(words)); if (first.Cells.Count == 0) cell.ColumnSpan = 3; first.Cells.Add(cell); }
            var empty = new TableCell(); Css.SetStyle(empty, "empty-cells:hide;"); lower.Cells.Add(empty);
            if (state != "empty") foreach (var words in new[] { "甲项", "乙项", "丙项" }) lower.Cells.Add(new TableCell(P(words)));
            if (state == "cleared") lower.Cells.RemoveRange(1, 3);
            Css.SetStyle(table, "width:400px;table-layout:auto;border-spacing:8px;");
            var document = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new(0), PageWidth = 400, PageHeight = constrained ? pageHeight : 600 };
            document.Blocks.Add(P("末段")); Css.SetStyleSheet(document, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}TableRow{height:44px;}TableCell{padding:0;border:0;vertical-align:top;}"); return document;
        }
        (int Total, int Footer) Ink(UIElement page, int height, string label, Rect[] footer)
        {
            const int width = 400;
            using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Ink renderer unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Ink readback request failed");
            target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { page.Render(drawing); } finally { target.EndDraw(); }
            var pixels = new byte[width * height * 4]; if (target.FetchReadback(pixels, width * 4, out var actualWidth, out var actualHeight) != JaliumResult.Ok || actualWidth != width || actualHeight != height) throw new InvalidOperationException("Ink readback failed");
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_GRID_NORMALIZATION_EVIDENCE_DIR") is { Length: > 0 } path)
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
            Console.WriteLine($"Grid ink page {engine}/{pageHeight}/{state}/{index}: ink={ink.Total}, footerInk={ink.Footer}, footer=[{string.Join(';', boxes)}]");
        }
        if (expected.Total == 0 || actual != expected.Total || expected.Footer == 0 || actualFooter != expected.Footer) throw new InvalidOperationException($"Paged glyph/range correspondence differs for {engine}/{pageHeight}: expected={expected}, actual=({actual},{actualFooter})");
        Console.WriteLine($"Paged grid ink {engine}/{pageHeight}/{state}: expected={expected.Total}, actual={actual}, footerInk={actualFooter}, pages={paginator.PageCount}");
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
    private static Paragraph P(string text) { var result = new Paragraph(new Run(text)); Css.SetClass(result, "body"); return result; }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static PageAction Action(string text) { var result = new PageAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action"); AutomationProperties.SetName(result, text); return result; }
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement source)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++) if (VisualTreeHelper.GetChild(source, i) is FrameworkElement child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
