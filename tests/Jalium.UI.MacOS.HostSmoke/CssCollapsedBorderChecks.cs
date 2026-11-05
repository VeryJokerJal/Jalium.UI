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
internal static class CssCollapsedBorderChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_COLLAPSE_BASELINE") == "1"; var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            const int height = 1100;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("让共享边只画一次", "title"));
            root.Children.Add(Label("相邻边框共同决定一条边；列、行与单元格按同一套规则排版。", "description"));
            var table = new StackPanel(); Css.SetClass(table, "matrix"); root.Children.Add(table);
            var caption = Label("普通控件 / 合并边框", "caption"); table.Children.Add(caption);
            var column = new Border(); Css.SetClass(column, "first-column"); table.Children.Add(column);
            var others = new Border(); Grid.SetColumnSpan(others, 2); Css.SetClass(others, "other-columns"); table.Children.Add(others);
            var header = Row(Cell("分类"), Cell("相邻样式"), Cell("显示结果")); Css.SetClass(header, "row header"); table.Children.Add(header);
            var span = Cell("跨行"); Grid.SetRowSpan(span, 2);
            var strong = Cell("较宽实线"); Css.SetClass(strong, "cell strong"); var soft = Cell("共享一条边"); Css.SetClass(soft, "cell soft");
            table.Children.Add(Row(span, strong, soft)); table.Children.Add(Row(Cell("跨行内部"), Cell("不穿过正文")));
            var merged = Cell("隐藏边会阻止相邻边绘制"); Grid.SetColumnSpan(merged, 2); var hidden = Cell("隐藏边"); Css.SetClass(hidden, "cell hidden-edge"); table.Children.Add(Row(merged, hidden));
            root.Children.Add(Label("同一规则用于文档表格", "section"));
            var document = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(0), Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White };
            var docSpan = DocCell("跨行"); docSpan.RowSpan = 2;
            var docTable = DocumentTable(DocRow(DocCell("原文"), DocCell("共享边"), DocCell("来源")), DocRow(docSpan, DocCell("较宽边优先"), DocCell("保留原文")), DocRow(DocCell("行高共享"), DocCell("选择不变")));
            docTable.Columns.Add(new() { Width = new(.28, GridUnitType.Star) }); docTable.Columns.Add(new() { Width = new(.36, GridUnitType.Star) }); docTable.Columns.Add(new() { Width = new(.36, GridUnitType.Star) }); document.Blocks.Add(docTable);
            Css.SetStyleSheet(document, """
                Table{width:100%;box-sizing:border-box;table-layout:fixed;border-collapse:var(--model);border-spacing:4px 6px;background:#f1f7f3;border:2px solid #9ab8a8;}
                TableCell{padding:10px 8px;border:2px solid #9ab8a8;vertical-align:middle;}
                TableRow:first-child{background:#dceee4;}Paragraph{margin:0;line-height:1.5;}
                """);
            var scroll = new FlowDocumentScrollViewer { Document = document, Height = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; AutomationProperties.SetName(scroll, "共享边框与原文选择"); root.Children.Add(scroll);
            root.Children.Add(Label("省略表格父级", "section")); var anonymous = new StackPanel(); Css.SetClass(anonymous, "anonymous");
            anonymous.Children.Add(Row(Cell("原始节点"), Cell("父级保持不变"))); root.Children.Add(anonymous);
            root.Children.Add(Label("边框宽度、线型和来源共同决定结果；合并时不保留独立单元格之间的间距。", "description footer"));
            var status = Label("", "description status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:18px;}
                .matrix{display:table;width:100%;box-sizing:border-box;table-layout:fixed;border-collapse:var(--model);border-spacing:4px 6px;background:#f1f7f3;border:2px solid #9ab8a8;}
                .caption{display:table-caption;font-size:18px;line-height:1.5;color:#146342;margin-bottom:10px;}
                .first-column{display:table-column;width:28%;background:#dceee4;}.other-columns{display:table-column;width:36%;}
                .row{display:table-row;}.header{background:#dceee4;}.cell{display:table-cell;padding:10px 8px;border:2px solid #9ab8a8;vertical-align:middle;}
                .cell TextBlock{display:block;white-space:normal;line-height:1.5;}.strong{border-inline-end:6px solid #146342;}.soft{border-inline-start:2px dashed #9ab8a8;}
                .hidden-edge{border-inline-start-style:hidden;}.section{display:block;font-size:18px;color:#146342;margin-top:24px;margin-bottom:12px;}
                .anonymous{display:block;border-collapse:var(--model);}.anonymous .cell{padding:8px;border:2px solid #9ab8a8;}
                .footer{margin-top:18px;}.status{color:#146342;}
                """);
            var originals = Descendants(table).ToArray(); var parents = originals.Select(element => element.VisualParent).ToArray(); var sourceText = document.GetText(); byte[]? initialPixels = null;
            foreach (var phase in new[] { "collapsed", "separate", "rtl-2x", "restored" })
            {
                var rtl = phase == "rtl-2x"; var scale = rtl ? 2 : 1; var separate = phase == "separate";
                var model = separate ? "separate" : "collapse"; var direction = rtl ? "rtl" : "ltr";
                Css.SetStyle(root, $"--model:{model};direction:{direction};"); Css.SetStyle(document, $"--model:{model};direction:{direction};"); document.PageWidth = width - 40;
                VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                status.Text = phase switch { "collapsed" => "当前：合并边框，共享内容边界", "separate" => "当前：独立边框，恢复行列间距", "rtl-2x" => "当前：RTL 列序，双倍像素密度", _ => "已恢复：合并边框与原始列序" };
                Layout(root, width, height); if (!scroll.Find("保留原文")) throw new InvalidOperationException("Original document search failed"); Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_COLLAPSE_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var file = File.Create(Path.Combine(path, $"collapse-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment collapsed table screenshot captured."); return 0; }
                if (!originals.Select(element => element.VisualParent).SequenceEqual(parents) || document.GetText() != sourceText || document.Parent is not null || scroll.Selection?.Text != "保留原文") throw new InvalidOperationException("Original table ownership or source selection changed");
                if (Math.Abs(((FrameworkElement)soft.Child!).VisualBounds.X - (separate ? 10 : rtl ? 9 : 11)) > .01) throw new InvalidOperationException("Shared border did not position cell content");
                foreach (var element in new FrameworkElement[] { table, scroll, anonymous, status })
                {
                    var at = element.TransformToAncestor(root); if (at.X < 19 || at.X + element.ActualWidth > width - 19 || at.Y + element.ActualHeight > height - 10) throw new InvalidOperationException("Table outside viewport");
                    var ink = 0;
                    for (var y = (int)Math.Ceiling(at.Y * scale); y < Math.Min(height * scale, (at.Y + element.ActualHeight) * scale); y++)
                    for (var x = (int)Math.Ceiling(at.X * scale); x < Math.Min(width * scale, (at.X + element.ActualWidth) * scale); x++)
                    { var offset = (y * width * scale + x) * 4; if (pixels[offset + 2] < 100 && pixels[offset + 1] < 140 && pixels[offset] < 120) ink++; }
                    if (ink < 40) throw new InvalidOperationException("Table text was clipped or not painted");
                }
                if (phase is "collapsed" or "restored")
                {
                    var start = table.TransformToAncestor(root).Y; var end = anonymous.TransformToAncestor(root).Y + anonymous.ActualHeight; var crop = new List<byte>();
                    for (var y = (int)Math.Floor(start); y < Math.Min(height, Math.Ceiling(end)); y++)
                    for (var x = 20; x < width - 20; x++) for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                    if (phase == "collapsed") initialPixels = crop.ToArray();
                    else { if (!crop.SequenceEqual(initialPixels!)) throw new InvalidOperationException("Restored table pixels differ"); Console.WriteLine($"Collapsed table pixel restoration {engine}/{width}: exact equality"); }
                }
                Console.WriteLine($"Collapsed tables {engine}/{width}/{phase}: generic={table.ActualWidth:0.##}x{table.ActualHeight:0.##}, document selection={scroll.Selection!.Text}"); passed++;
            }
            scroll.Document = null;
        }
        Console.WriteLine($"macOS CSS collapsed table GPU checks: {passed}/16 passed"); return passed == 16 ? 0 : 1;
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
