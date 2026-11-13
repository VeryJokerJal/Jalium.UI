using AppKit;
using CoreGraphics;
using Jalium.UI.Automation;
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
internal static class CssTableCaptionChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_CAPTION_BASELINE") == "1"; var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            const int height = 940;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("CSS 表格说明", "title")); root.Children.Add(Label("说明与数据分别排版，切换位置后仍使用原来的控件和内容。", "description"));
            var table = new StackPanel(); Css.SetClass(table, "table"); root.Children.Add(table);
            var heading = Label("渲染能力与验证范围", "caption heading-caption"); AutomationProperties.SetName(heading, "能力矩阵标题"); table.Children.Add(heading);
            var firstColumn = new Border(); Css.SetClass(firstColumn, "first-column"); table.Children.Add(firstColumn);
            var remainingColumns = new Border(); Css.SetClass(remainingColumns, "other-columns"); Grid.SetColumnSpan(remainingColumns, 2); table.Children.Add(remainingColumns);
            var header = new StackPanel(); Css.SetClass(header, "header"); header.Children.Add(Row(Cell("分类"), Cell("布局行为"), Cell("验证"))); table.Children.Add(header);
            var body = new StackPanel(); Css.SetClass(body, "body");
            var span = Cell("表格布局"); Grid.SetRowSpan(span, 2); var first = Row(span, Cell("共享行列尺寸"), Cell("自动测试"));
            body.Children.Add(first); body.Children.Add(Row(Cell("跨行与列跨度"), Cell("原生渲染"))); body.Children.Add(Row(Cell("说明区"), Cell("宽度、间距与换行"), Cell("上下切换"))); table.Children.Add(body);
            var footnote = Label("说明：标题和注释位于数据区外；背景与边框只包住数据区。", "caption footnote"); table.Children.Add(footnote);
            root.Children.Add(Label("行内表格与基线", "section-title"));
            var line = new StackPanel(); Css.SetClass(line, "line"); line.Children.Add(Label("同一行：", "inline-label"));
            var inlineTable = new StackPanel(); Css.SetClass(inlineTable, "inline-table"); var inlineCaption = Label("行内说明", "caption inline-caption"); inlineTable.Children.Add(inlineCaption); inlineTable.Children.Add(Row(Cell("共享"), Cell("基线"))); line.Children.Add(inlineTable); root.Children.Add(line);
            root.Children.Add(Label("省略表格父级", "section-title")); var anonymous = new StackPanel(); Css.SetClass(anonymous, "anonymous");
            var looseCaption = Label("自动补全的说明区", "caption loose-caption"); anonymous.Children.Add(Row(Cell("原始节点"), Cell("父级不变"))); anonymous.Children.Add(looseCaption); root.Children.Add(anonymous);
            var status = Label("", "description status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:18px;}
                .table{display:table;width:100%;box-sizing:border-box;table-layout:fixed;border-spacing:4px 6px;background:#f1f7f3;border:1px solid #9ab8a8;border-radius:10px;overflow:hidden;}
                .caption{display:table-caption;line-height:1.6;white-space:normal;}.heading-caption{font-size:18px;color:#146342;margin-bottom:10px;}
                .footnote{caption-side:bottom;margin-top:10px;}.first-column{display:table-column;width:26%;background:#dceee4;}.other-columns{display:table-column;width:37%;}
                .header{display:table-header-group;background:#dceee4;}.body{display:table-row-group;}.row{display:table-row;}
                .cell{display:table-cell;padding:10px 8px;border:1px solid #9ab8a8;vertical-align:middle;}.cell TextBlock{display:block;white-space:normal;line-height:1.5;}
                .section-title{display:block;font-size:18px;color:#146342;margin-top:22px;margin-bottom:10px;}.line,.anonymous{display:block;}
                .inline-label{display:inline;}.inline-table{display:inline-table;border-spacing:3px;background:#f1f7f3;border:1px solid #9ab8a8;}
                .inline-caption{font-size:14px;color:#146342;margin-bottom:4px;}.inline-table .cell,.anonymous .cell{padding:6px;vertical-align:baseline;}
                .loose-caption{color:#146342;margin-bottom:6px;}.status{margin-top:22px;color:#146342;}
                """);
            var originals = Descendants(table).ToArray(); var parents = originals.Select(element => element.VisualParent).ToArray(); byte[]? initialPixels = null;
            foreach (var phase in new[] { "initial", "bottom", "compact", "restored" })
            {
                Css.SetStyle(table, phase == "bottom" ? "caption-side:bottom;" : phase == "compact" ? "caption-side:top;border-spacing:0;" : "caption-side:top;border-spacing:4px 6px;");
                status.Text = phase switch { "initial" => "默认位置：标题在上，注释在下", "bottom" => "位置切换：标题移至数据区下方", "compact" => "紧凑模式：说明区保留各自间距", _ => "恢复默认：说明、正文与边框重新对齐" };
                Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
                target.SetDpi(96, 96); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4]; if (target.FetchReadback(pixels, (uint)(width * 4), out var w, out var h) != JaliumResult.Ok || w != width || h != height) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_CAPTION_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                    using var file = File.Create(Path.Combine(path, $"caption-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment caption screenshot captured."); return 0; }
                if (!originals.Select(element => element.VisualParent).SequenceEqual(parents)) throw new InvalidOperationException("Caption layout reparented source controls");
                if ((phase == "bottom") != (heading.VisualBounds.Top > body.VisualBounds.Top) || footnote.VisualBounds.Top <= body.VisualBounds.Top || span.ActualHeight <= ((Border)first.Children[1]).ActualHeight) throw new InvalidOperationException("Caption order or table span mismatch");
                foreach (var element in new FrameworkElement[] { heading, footnote, inlineCaption, looseCaption, status })
                {
                    var at = element.TransformToAncestor(root); if (at.X < 19 || at.X + element.ActualWidth > width - 19 || at.Y + element.ActualHeight > height - 10) throw new InvalidOperationException("Caption outside viewport");
                    var ink = 0;
                    for (var y = (int)Math.Ceiling(at.Y); y < Math.Min(height, at.Y + element.ActualHeight); y++)
                    for (var x = (int)Math.Ceiling(at.X); x < Math.Min(width, at.X + element.ActualWidth); x++)
                    { var offset = (y * width + x) * 4; if (pixels[offset + 2] < 100 && pixels[offset + 1] < 140 && pixels[offset] < 120) ink++; }
                    if (ink < 40) throw new InvalidOperationException("Caption text was clipped or not painted");
                }
                var origin = table.TransformToAncestor(root); var crop = new List<byte>();
                for (var y = (int)Math.Floor(origin.Y); y < Math.Min(height, Math.Ceiling(origin.Y + table.ActualHeight)); y++)
                for (var x = (int)Math.Floor(origin.X); x < Math.Min(width, Math.Ceiling(origin.X + table.ActualWidth)); x++)
                    for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                if (phase == "initial") initialPixels = crop.ToArray();
                if (phase == "restored") { if (!crop.SequenceEqual(initialPixels!)) throw new InvalidOperationException("Restored caption pixels differ"); Console.WriteLine($"Caption pixel restoration {engine}/{width}: exact equality"); }
                Console.WriteLine($"Caption {engine}/{width}/{phase}: table={table.ActualWidth:0.##}x{table.ActualHeight:0.##}, headingY={heading.VisualBounds.Y:0.##}, footnoteY={footnote.VisualBounds.Y:0.##}"); passed++;
            }
        }
        Console.WriteLine($"macOS CSS table caption GPU checks: {passed}/16 passed"); return passed == 16 ? 0 : 1;
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static Border Cell(string text) { var result = new Border { Child = Label(text, "text") }; Css.SetClass(result, "cell"); return result; }
    private static StackPanel Row(params UIElement[] children) { var result = new StackPanel(); Css.SetClass(result, "row"); foreach (var child in children) result.Children.Add(child); return result; }
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement source)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++) if (VisualTreeHelper.GetChild(source, i) is FrameworkElement child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
