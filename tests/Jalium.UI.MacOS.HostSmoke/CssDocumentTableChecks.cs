using System.Collections;
using System.Reflection;
using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssDocumentTableChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if (!context.IsValid || context.Backend != RenderBackend.Metal) throw new InvalidOperationException("Metal unavailable");
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_BASELINE") == "1"; var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            const int height = 1100;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("让表格按内容对齐", "title")); var status = Label("", "description"); root.Children.Add(status);
            root.Children.Add(Label("同列共享宽度，跨行与跨列保持结构；点击与选择仍指向原文。", "description"));
            var first = Row(Cell("组件"), Cell("状态"), Cell("说明")); Css.SetClass(first, "header");
            var span = Cell("文本"); span.RowSpan = 2; Css.SetClass(span, "accent"); Css.SetStyle(span, "vertical-align:middle;");
            var nested = Table(Row(Cell("内列甲"), Cell("内列乙"))); var merged = Cell("嵌套表格"); merged.ColumnSpan = 2;
            var empty = new TableCell(); Css.SetClass(empty, "accent");
            var table = Table(first, Row(span, Cell("已启用"), Cell("按列共享宽度\n保留原文选择")), Row(Cell("跨行"), Cell("行高随正文调整")),
                Row(merged, new TableCell(nested)), Row(empty, Cell("留空"), Cell("空单元格可隐藏")));
            table.Columns.Add(new() { Width = new(70) }); table.Columns.Add(new() { Width = new(80) }); table.Columns.Add(new() { Width = new(1, GridUnitType.Star) });
            var document = Doc(table); var scroll = new FlowDocumentScrollViewer { Document = document, Height = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; AutomationProperties.SetName(scroll, "表格行列与嵌套单元格"); root.Children.Add(scroll);
            root.Children.Add(Label("长单元格 / 同时保留两列正文", "heading"));
            var longTable = Table(Row(Cell(string.Join('\n', Enumerable.Range(1, 16).Select(i => $"甲列第 {i} 行"))), Cell(string.Join('\n', Enumerable.Range(1, 10).Select(i => $"乙列第 {i} 行")))));
            var pageDocument = Doc(longTable); pageDocument.PageHeight = 164; pageDocument.PagePadding = new(10);
            var paged = new FlowDocumentPageViewer { Document = pageDocument, Height = 194 }; AutomationProperties.SetName(paged, "表格跨页正文"); root.Children.Add(paged);
            root.Children.Add(Label("切换间距、列宽与页序后重新排版，文档节点和来源索引保持不变。", "description"));
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:16px;}
                .heading{display:block;font-size:18px;color:#146342;margin-top:16px;margin-bottom:12px;}
                """);
            var original = document.GetText(); var pageText = pageDocument.GetText();
            var cells = table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).ToArray(); var parents = cells.Select(c => c.Parent).ToArray();
            foreach (var phase in new[] { "initial", "compact", "rtl", "restored" })
            {
                var compact = phase == "compact"; var rtl = phase == "rtl";
                table.Columns[0].Width = new(compact ? 90 : 70); table.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                pageDocument.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                Css.SetStyle(table, compact ? "table-layout:fixed;border-spacing:0;empty-cells:hide;" : "table-layout:fixed;border-spacing:6px 8px;empty-cells:show;");
                document.PageWidth = width - 40; pageDocument.PageWidth = Math.Min(width - 40, 500);
                status.Text = phase switch { "initial" => "默认间距，跨行与嵌套内容按列对齐", "compact" => "收紧间距并加宽首列，隐藏空单元格", "rtl" => "切换 RTL 列序，查看跨页后的原文", _ => "恢复原始列宽和间距，返回第一页" };
                Layout(root, width, height); paged.GoToPage(phase == "compact" ? 2 : rtl ? 3 : 1); Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
                target.SetDpi(96, 96); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context);
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4]; if (target.FetchReadback(pixels, (uint)(width * 4), out var w, out var h) != JaliumResult.Ok || w != width || h != height) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                    using var file = File.Create(Path.Combine(path, $"table-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment table screenshot captured."); return 0; }
                if (document.GetText() != original || pageDocument.GetText() != pageText || document.Parent is not null || pageDocument.Parent is not null || !cells.Select(c => c.Parent).SequenceEqual(parents)) throw new InvalidOperationException("Original table content or ownership changed");
                var body = Descendants(scroll).Single(e => e.GetType().Name == "FlowDocumentPresentation");
                var boxes = (IEnumerable)body.GetType().GetProperty("TableCells", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
                var bounds = boxes.Cast<object>().ToDictionary(item => (TableCell)item.GetType().GetProperty("Source")!.GetValue(item)!, item => (Rect)item.GetType().GetProperty("Bounds")!.GetValue(item)!);
                foreach (var box in bounds.Values) if (box.Left < -.01 || box.Right > body.ActualWidth + .01) throw new InvalidOperationException("Table cell outside viewport");
                if ((bounds[first.Cells[0]].Left < bounds[first.Cells[1]].Left) == rtl || Math.Abs(bounds[first.Cells[0]].Width - (compact ? 90 : 70)) > .01) throw new InvalidOperationException("Column direction or allocation mismatch");
                if (bounds[span].Height <= bounds[table.RowGroups[0].Rows[1].Cells[1]].Height) throw new InvalidOperationException("Row span was flattened");
                if (!ReferenceEquals(((IDocumentPaginatorSource)pageDocument).DocumentPaginator, paged.PageViews[0].DocumentPaginator) || paged.PageViews[0].DocumentPage.Visual is null) throw new InvalidOperationException("Page source lost");
                foreach (var control in new FrameworkElement[] { scroll, paged })
                {
                    var origin = control.TransformToAncestor(root); if (origin.X < 8 || origin.X + control.ActualWidth > width - 8 || origin.Y + control.ActualHeight > height) throw new InvalidOperationException("Viewer outside viewport");
                    var ink = 0;
                    for (var y = (int)origin.Y; y < Math.Min(height, origin.Y + control.ActualHeight); y++)
                    for (var x = (int)origin.X; x < Math.Min(width, origin.X + control.ActualWidth); x++)
                    { var at = (y * width + x) * 4; var b = pixels[at]; var g = pixels[at + 1]; var r = pixels[at + 2]; if (r < 100 && g < 130 && b < 120 && g > r + 10 && b > r && b < g) ink++; }
                    if (ink < 30) throw new InvalidOperationException($"Table text not visible: {phase}, {ink}");
                }
                // Compare every page with the same continuous source. Intersection
                // tests alone would not detect a few lost strokes at a page edge.
                var continuous = new FlowDocumentScrollViewer { Document = pageDocument, Height = 1000,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                Layout(continuous, (int)pageDocument.PageWidth, 1000);
                var fullInk = RenderedInk(context, engine, continuous, (int)pageDocument.PageWidth, 1000);
                var pageInk = 0; var paginator = ((IDocumentPaginatorSource)pageDocument).DocumentPaginator;
                for (var page = 0; page < paginator.PageCount; page++)
                {
                    var visual = (FrameworkElement)paginator.GetPage(page).Visual!;
                    pageInk += RenderedInk(context, engine, visual, (int)pageDocument.PageWidth, (int)pageDocument.PageHeight);
                    if (visual is IDisposable disposable) disposable.Dispose();
                }
                continuous.Document = null;
                if (Math.Abs(fullInk - pageInk) > Math.Max(2, fullInk / 2000)) throw new InvalidOperationException($"Lost table glyph ink in {engine}/{width}/{phase}: full={fullInk}, pages={pageInk}");
                Console.WriteLine($"Table glyph conservation {engine}/{width}/{phase}: full={fullInk}, pages={pageInk}");
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS document table GPU checks: {passed}/16 passed"); return passed == 16 ? 0 : 1;
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static int RenderedInk(RenderContext context, RenderingEngine engine, FrameworkElement visual, int width, int height)
    {
        using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
        target.SetDpi(96, 96); target.SetRenderingEngine(engine); using var drawing = new RenderTargetDrawingContext(target, context);
        if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Ink readback request failed");
        target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { visual.Render(drawing); } finally { target.EndDraw(); }
        var pixels = new byte[width * height * 4];
        if (target.FetchReadback(pixels, (uint)(width * 4), out var w, out var h) != JaliumResult.Ok || w != width || h != height) throw new InvalidOperationException("Ink readback failed");
        var ink = 0;
        for (var at = 0; at < pixels.Length; at += 4)
        { var b = pixels[at]; var g = pixels[at + 1]; var r = pixels[at + 2]; if (r < 100 && g < 130 && b < 120 && g > r + 10 && b > r && b < g) ink++; }
        return ink;
    }
    private static TableCell Cell(string text)
    { var p = new Paragraph { Margin = new(0) }; var lines = text.Split('\n'); for (var i = 0; i < lines.Length; i++) { if (i > 0) p.Inlines.Add(new LineBreak()); p.Inlines.Add(new Run(lines[i])); } return new TableCell(p); }
    private static TableRow Row(params TableCell[] cells) { var row = new TableRow(); foreach (var cell in cells) row.Cells.Add(cell); return row; }
    private static Table Table(params TableRow[] rows) { var table = new Table(); var group = new TableRowGroup(); foreach (var row in rows) group.Rows.Add(row); table.RowGroups.Add(group); return table; }
    private static FlowDocument Doc(Table table)
    {
        var document = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White, PageWidth = 500, PageHeight = 500, PagePadding = new(0) }; document.Blocks.Add(table);
        Css.SetStyleSheet(document, """
            Table{table-layout:fixed;border-spacing:6px 8px;background-color:#f1f7f3;}
            TableCell{padding:8px;border:1px solid #9ab8a8;vertical-align:top;}
            .header{background-color:#146342;color:white;font-weight:bold;}
            .accent{background-color:#dceee4;}
            """); return document;
    }
    private static IEnumerable<FrameworkElement> Descendants(Visual source)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++) { var child = VisualTreeHelper.GetChild(source, i); if (child is FrameworkElement element) yield return element; if (child is Visual visual) foreach (var nested in Descendants(visual)) yield return nested; } }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
