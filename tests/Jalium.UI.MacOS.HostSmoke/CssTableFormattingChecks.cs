using AppKit;
using CoreGraphics;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Reflection;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssTableFormattingChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var passed = 0; var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_FORMATTING_BASELINE") == "1";
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            const int height = 820;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("CSS 表格布局", "title"));
            root.Children.Add(Label("普通控件共享行列几何，内容与键盘顺序保留在原来的控件树中。", "description"));
            var table = new StackPanel(); Css.SetClass(table, "table"); root.Children.Add(table);
            var firstColumn = new Border(); Css.SetClass(firstColumn, "first-column"); table.Children.Add(firstColumn);
            var otherColumns = new Border(); Grid.SetColumnSpan(otherColumns, 2); Css.SetClass(otherColumns, "other-columns"); table.Children.Add(otherColumns);
            var footer = Group("footer", Row(Cell("来源"), WideCell("原始控件与内容节点保持不变", 2)));
            var spanning = Cell("布局能力"); Grid.SetRowSpan(spanning, 2); Css.SetClass(spanning, "cell category");
            var first = Row(spanning, Cell("固定列宽、共享行高"), Cell("已接入"));
            var second = Row(Cell("跨行与列跨度"), Cell("可组合")); var empty = new Border(); Css.SetClass(empty, "cell empty");
            var body = Group("body", first, second, Row(Cell("空单元格"), Cell("切换 hide / show"), empty));
            var header = Group("header", Row(Cell("分类"), Cell("布局行为"), Cell("状态")));
            table.Children.Add(footer); table.Children.Add(body); table.Children.Add(header);
            root.Children.Add(Label("行内表格", "heading")); var inlineFlow = new StackPanel(); Css.SetClass(inlineFlow, "inline-flow");
            inlineFlow.Children.Add(Label("同一行：", "inline-label")); var inline = new StackPanel(); Css.SetClass(inline, "inline-table");
            inline.Children.Add(Row(Cell("原生节点"), Cell("共享基线"))); inlineFlow.Children.Add(inline); root.Children.Add(inlineFlow);
            root.Children.Add(Label("匿名表格盒", "heading")); var anonymous = new StackPanel(); Css.SetClass(anonymous, "anonymous");
            var loose = Row(Cell("省略 table"), Cell("自动补全结构")); anonymous.Children.Add(loose); root.Children.Add(anonymous);
            var textRow = Label("文字行保留正文", "text-row"); anonymous.Children.Add(textRow);
            var status = Label("", "description status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:16px;}
                .heading{display:block;font-size:18px;color:#146342;margin-top:20px;margin-bottom:10px;}
                .table{display:table;width:100%;table-layout:fixed;border-spacing:4px 6px;background:#f1f7f3;}
                .first-column{display:table-column;width:26%;background:#dceee4;}.other-columns{display:table-column;width:37%;}
                .header{display:table-header-group;background:#dceee4;}.body{display:table-row-group;}.footer{display:table-footer-group;background:#e8f2ec;}
                .row,.text-row{display:table-row;}.cell{display:table-cell;padding:10px 8px;border:1px solid #9ab8a8;vertical-align:middle;}
                .cell TextBlock{display:block;line-height:1.5;white-space:normal;}.category{color:#146342;}.empty{background:#dceee4;}
                .inline-flow,.anonymous{display:block;}.inline-label{display:inline;}.inline-table{display:inline-table;border-spacing:3px;background:#f1f7f3;}
                .inline-table .cell,.anonymous .cell{padding:6px;border:1px solid #9ab8a8;}.inline-table .cell{vertical-align:baseline;}.status{margin-top:20px;color:#146342;}
                """);
            var originals = Descendants(table).Where(element => element is Border or TextBlock).ToArray(); var parents = originals.Select(element => element.VisualParent).ToArray();
            byte[]? initialTablePixels = null;
            foreach (var phase in new[] { "initial", "compact", "rtl", "restored" })
            {
                var compact = phase == "compact"; var rtl = phase == "rtl";
                Css.SetStyle(table, compact ? "border-spacing:0;empty-cells:hide;" : "border-spacing:4px 6px;empty-cells:show;"); table.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                status.Text = phase switch { "initial" => "默认间距 · 表头与表尾按格式顺序排列", "compact" => "紧凑模式 · 收紧间距并隐藏空单元格", "rtl" => "RTL 模式 · 列序反转，跨行内容仍对齐", _ => "恢复默认 · 几何、内容和来源关系恢复" };
                Layout(root, width, height);
                if (!baseline && Descendants(table).OfType<TextBlock>().Any(text => text.FlowDirection != table.FlowDirection)) throw new InvalidOperationException("Inherited table text direction was not restored");
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
                target.SetDpi(96, 96); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context);
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4]; if (target.FetchReadback(pixels, (uint)(width * 4), out var w, out var h) != JaliumResult.Ok || w != width || h != height) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_FORMATTING_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                    using var file = File.Create(Path.Combine(path, $"formatting-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment CSS table screenshot captured."); return 0; }
                var tableOrigin = table.TransformToAncestor(root); var tablePixels = new List<byte>();
                for (var y = (int)Math.Floor(tableOrigin.Y); y < Math.Min(height, Math.Ceiling(tableOrigin.Y + table.ActualHeight)); y++)
                for (var x = (int)Math.Floor(tableOrigin.X); x < Math.Min(width, Math.Ceiling(tableOrigin.X + table.ActualWidth)); x++)
                    for (var component = 0; component < 4; component++) tablePixels.Add(pixels[(y * width + x) * 4 + component]);
                if (phase == "initial") initialTablePixels = tablePixels.ToArray();
                if (phase == "restored")
                {
                    if (!tablePixels.SequenceEqual(initialTablePixels!)) throw new InvalidOperationException($"Table pixels were not restored in {engine}/{width}");
                    Console.WriteLine($"Table pixel restoration {engine}/{width}: exact equality");
                }
                if (!originals.Select(element => element.VisualParent).SequenceEqual(parents)) throw new InvalidOperationException("Original table controls were reparented");
                var left = spanning.TransformToAncestor(table).X; var right = ((Border)first.Children[1]).TransformToAncestor(table).X;
                if ((left < right) == rtl || spanning.ActualHeight <= ((Border)first.Children[1]).ActualHeight || header.VisualBounds.Top >= body.VisualBounds.Top || footer.VisualBounds.Top <= body.VisualBounds.Top) throw new InvalidOperationException($"Table geometry mismatch {width}/{phase}");
                if (firstColumn.IsVisible || otherColumns.IsVisible || table.ActualWidth > width - 39 || loose.VisualBounds.Left < 0) throw new InvalidOperationException("Column definition or containing block mismatch");
                foreach (var cell in originals.OfType<Border>().Where(element => Css.GetClass(element).Split(' ').Contains("cell")))
                { var at = cell.TransformToAncestor(table); if (at.X < -.01 || at.X + cell.ActualWidth > table.ActualWidth + .01) throw new InvalidOperationException("Cell extends beyond the table"); }
                foreach (var element in new FrameworkElement[] { table, inlineFlow, anonymous, status })
                { var at = element.TransformToAncestor(root); if (at.X < 19 || at.X + element.ActualWidth > width - 19 || at.Y + element.ActualHeight > height - 10) throw new InvalidOperationException("Fixture outside viewport"); }
                var dark = 0; for (var at = 0; at < pixels.Length; at += 4) if (pixels[at + 2] < 100 && pixels[at + 1] < 140 && pixels[at] < 120) dark++;
                if (dark < 1000) throw new InvalidOperationException("Table content was not painted");
                var origin = textRow.TransformToAncestor(root); var rowInk = 0;
                for (var y = (int)Math.Ceiling(origin.Y); y < Math.Min(height, origin.Y + textRow.ActualHeight); y++)
                for (var x = (int)Math.Ceiling(origin.X); x < Math.Min(width, origin.X + textRow.ActualWidth); x++)
                { var at = (y * width + x) * 4; if (pixels[at + 2] < 100 && pixels[at + 1] < 140 && pixels[at] < 120) rowInk++; }
                if (rowInk < 50) throw new InvalidOperationException("TextBlock table-row body was not painted");
                Console.WriteLine($"CSS table {engine}/{width}/{phase}: height={table.ActualHeight:0.##}, glyphPixels={dark}"); passed++;
            }
        }
        Console.WriteLine($"macOS CSS table formatting GPU checks: {passed}/16 passed"); return passed == 16 ? 0 : 1;
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static Border Cell(string text) { var result = new Border { Child = Label(text, "text") }; Css.SetClass(result, "cell"); return result; }
    private static Border WideCell(string text, int span) { var result = Cell(text); Grid.SetColumnSpan(result, span); return result; }
    private static StackPanel Row(params UIElement[] cells) { var result = new StackPanel(); Css.SetClass(result, "row"); foreach (var cell in cells) result.Children.Add(cell); return result; }
    private static StackPanel Group(string css, params UIElement[] rows) { var result = new StackPanel(); Css.SetClass(result, css); foreach (var row in rows) result.Children.Add(row); return result; }
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement owner)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++) if (VisualTreeHelper.GetChild(owner, i) is FrameworkElement child) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
