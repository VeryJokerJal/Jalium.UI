using AppKit;
using CoreGraphics;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Reflection;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssTableVisibilityChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_VISIBILITY_BASELINE") == "1"; var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            const int height = 760;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("收起行列，保留原文", "title"));
            root.Children.Add(Label("折叠后释放空间，剩余列保持原宽；跨格内容保留排版并裁剪。", "description"));
            var table = new StackPanel(); Css.SetClass(table, "matrix"); root.Children.Add(table);
            table.Children.Add(Label("普通控件 / 行列折叠", "caption"));
            var auxiliaryColumn = new Border(); Css.SetClass(auxiliaryColumn, "column auxiliary");
            var firstColumn = new Border(); Css.SetClass(firstColumn, "column"); var lastColumn = new Border(); Css.SetClass(lastColumn, "column");
            table.Children.Add(firstColumn); table.Children.Add(auxiliaryColumn); table.Children.Add(lastColumn);
            var header = Row(Cell("项目"), Cell("辅助列"), Cell("结果")); Css.SetClass(header, "row header"); table.Children.Add(header);
            var span = Cell("跨行保留"); Grid.SetRowSpan(span, 2); var keptCell = Cell("宽度不变");
            table.Children.Add(Row(span, Cell("内部信息"), keptCell));
            var optionalRow = Row(Cell("临时内容"), Cell("折叠此行")); Css.SetClass(optionalRow, "row optional"); table.Children.Add(optionalRow);
            root.Children.Add(Label("同一规则用于文档表格", "section"));
            var document = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(0), Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White };
            var documentSpan = DocCell("跨行保留"); documentSpan.RowSpan = 2;
            var documentRow = DocRow(DocCell("临时内容"), DocCell("折叠此行"));
            var documentTable = DocumentTable(DocRow(DocCell("项目"), DocCell("辅助列"), DocCell("结果")), DocRow(documentSpan, DocCell("内部信息"), DocCell("保留原文")), documentRow);
            for (var i = 0; i < 3; i++) documentTable.Columns.Add(new TableColumn()); document.Blocks.Add(documentTable);
            Css.SetStyleSheet(document, """
                Table{width:100%;box-sizing:border-box;table-layout:fixed;border-collapse:collapse;border:2px solid #9ab8a8;background:#f1f7f3;}
                TableCell{padding:8px;border:2px solid #9ab8a8;vertical-align:top;}
                TableRow:first-child{background:#dceee4;}Paragraph{margin:0;line-height:1.5;text-align:start;}
                """);
            var scroll = new FlowDocumentScrollViewer { Document = document, Height = 172, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            AutomationProperties.SetName(scroll, "原文保留与行列折叠"); root.Children.Add(scroll);
            root.Children.Add(Label("隐藏保留空位，折叠释放空间。切换显示状态不会删除源单元格或改变原文选择。", "description footer"));
            var status = Label("", "description status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:18px;}
                .matrix{display:table;width:100%;box-sizing:border-box;table-layout:fixed;border-collapse:collapse;background:#f1f7f3;border:2px solid #9ab8a8;}
                .caption{display:table-caption;font-size:18px;line-height:1.5;color:#146342;margin-bottom:10px;}
                .column{display:table-column;}.auxiliary{visibility:var(--column);}.row{display:table-row;}.optional{visibility:var(--row);}
                .header{background:#dceee4;}.cell{display:table-cell;padding:8px;border:2px solid #9ab8a8;vertical-align:top;}
                .cell TextBlock{display:block;white-space:normal;line-height:1.5;}
                .section{display:block;font-size:18px;color:#146342;margin-top:24px;margin-bottom:12px;}.footer{margin-top:18px;}.status{color:#146342;}
                """);
            var originals = Descendants(table).ToArray(); var parents = originals.Select(element => element.VisualParent).ToArray(); var originalText = document.GetText();
            byte[]? initialPixels = null; double initialWidth = 0, initialHeight = 0, removedWidth = 0, removedHeight = 0;
            foreach (var phase in new[] { "initial", "hidden", "folded", "rtl-2x", "restored" })
            {
                var folded = phase is "folded" or "rtl-2x"; var rtl = phase == "rtl-2x"; var scale = rtl ? 2 : 1;
                var visibility = folded ? "collapse" : phase == "hidden" ? "hidden" : "visible";
                Css.SetStyle(root, $"--column:{visibility};--row:{visibility};direction:{(rtl ? "rtl" : "ltr")};");
                Css.SetStyle(document, $"direction:{(rtl ? "rtl" : "ltr")};");
                Css.SetStyle(documentTable.Columns[1], $"visibility:{visibility};"); Css.SetStyle(documentRow, $"visibility:{visibility};"); document.PageWidth = width - 40;
                VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                status.Text = phase switch { "initial" => "当前：全部显示", "hidden" => "当前：隐藏内容，保留行高", "folded" => "当前：折叠辅助列与末行", "rtl-2x" => "当前：RTL 折叠，双倍像素密度", _ => "已恢复：全部显示与原始列序" };
                Layout(root, width, height); if (!scroll.Find("保留原文")) throw new InvalidOperationException("Original document search failed"); Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4];
                if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_TABLE_VISIBILITY_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var file = File.Create(Path.Combine(path, $"visibility-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline && phase == "folded") { Console.WriteLine("Before-increment visibility screenshot captured."); return 0; }
                if (phase == "initial")
                {
                    initialWidth = table.ActualWidth; initialHeight = table.ActualHeight; removedWidth = header.Children.OfType<FrameworkElement>().ElementAt(1).ActualWidth; removedHeight = optionalRow.ActualHeight;
                }
                if (!baseline)
                {
                    if (Math.Abs(table.ActualWidth - (initialWidth - (folded ? removedWidth : 0))) > .01 || Math.Abs(table.ActualHeight - (initialHeight - (folded ? removedHeight : 0))) > .01) throw new InvalidOperationException("Table track dimensions changed incorrectly");
                    if (optionalRow.Children.Cast<UIElement>().Any(child => child.IsVisible) != (phase is "initial" or "restored")) throw new InvalidOperationException("Hidden or removed row remained visible");
                }
                if (!originals.Select(element => element.VisualParent).SequenceEqual(parents) || document.GetText() != originalText || scroll.Selection?.Text != "保留原文") throw new InvalidOperationException("Original source or selection changed");
                foreach (var element in new FrameworkElement[] { table, scroll, status })
                {
                    var at = element.TransformToAncestor(root); if (at.X < 19 || at.X + element.ActualWidth > width - 19 || at.Y + element.ActualHeight > height - 10) throw new InvalidOperationException("Content outside viewport");
                    var ink = 0;
                    for (var y = (int)Math.Ceiling(at.Y * scale); y < Math.Min(height * scale, (at.Y + element.ActualHeight) * scale); y++)
                    for (var x = (int)Math.Ceiling(at.X * scale); x < Math.Min(width * scale, (at.X + element.ActualWidth) * scale); x++)
                    { var offset = (y * width * scale + x) * 4; if (pixels[offset + 2] < 100 && pixels[offset + 1] < 140 && pixels[offset] < 120) ink++; }
                    if (ink < 40) throw new InvalidOperationException("Visible table text was not painted");
                }
                if (phase is "initial" or "restored")
                {
                    var start = table.TransformToAncestor(root).Y; var end = scroll.TransformToAncestor(root).Y + scroll.ActualHeight; var crop = new List<byte>();
                    for (var y = (int)Math.Floor(start); y < Math.Min(height, Math.Ceiling(end)); y++)
                    for (var x = 20; x < width - 20; x++) for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                    if (phase == "initial") initialPixels = crop.ToArray();
                    else if (!baseline) { if (!crop.SequenceEqual(initialPixels!)) throw new InvalidOperationException("Restored table pixels differ"); Console.WriteLine($"Visibility pixel restoration {engine}/{width}: exact equality"); }
                }
                Console.WriteLine($"Visibility tables {engine}/{width}/{phase}: {table.ActualWidth:0.##}x{table.ActualHeight:0.##}, selection={scroll.Selection!.Text}"); passed++;
            }
            scroll.Document = null;
        }
        Console.WriteLine($"macOS CSS table visibility GPU checks: {passed}/20 passed"); return passed == 20 ? 0 : 1;
    }

    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static Border Cell(string text) { var result = new Border { Child = Label(text, "text") }; Css.SetClass(result, "cell"); return result; }
    private static StackPanel Row(params UIElement[] children) { var result = new StackPanel(); Css.SetClass(result, "row"); foreach (var child in children) result.Children.Add(child); return result; }
    private static TableCell DocCell(string text) => new(new Paragraph(new Run(text)));
    private static TableRow DocRow(params TableCell[] cells) { var result = new TableRow(); foreach (var cell in cells) result.Cells.Add(cell); return result; }
    private static Table DocumentTable(params TableRow[] rows) { var result = new Table(); var group = new TableRowGroup(); foreach (var row in rows) group.Rows.Add(row); result.RowGroups.Add(group); return result; }
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement source)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++) if (VisualTreeHelper.GetChild(source, i) is FrameworkElement child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
