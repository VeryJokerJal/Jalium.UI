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
internal static class CssMultiColumnChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_MULTICOLUMN_BASELINE") == "1"; var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("正文，流入下一列", "title"));
            root.Children.Add(Label("按完整文字行分列，标题跨列；原文与控件保持原有归属。", "description"));
            var article = new StackPanel(); Css.SetClass(article, "article"); root.Children.Add(article);
            var before = Label("按行分列\n从第一列开始\n沿阅读顺序流动\n到下一列继续\n每行保持完整\n保留原文字体\n选择偏移不变\n点击对应原文\n间距保持留白\n分隔线在中间\n窄屏自动减列\n宽屏容纳三列", "copy"); article.Children.Add(before);
            var heading = Label("一个标题，跨越所有列", "spanner"); article.Children.Add(heading);
            var after = Label("标题后重新分列\n内容继续流动\n从容阅读原文\n切换方向可恢复", "copy alternate"); article.Children.Add(after);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var first = Action("前一段"); var second = Action("后一段"); actions.Children.Add(first); actions.Children.Add(second);
            root.Children.Add(Label("列数随宽度调整。选择、焦点和无障碍仍使用原始内容。", "footer"));
            var status = Label("", "status"); root.Children.Add(status);
            AutomationProperties.SetName(article, "CSS 多列正文");
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:18px;}
                .article{display:block;columns:140px 3;column-gap:20px;column-rule:2px solid #9ab8a8;}
                .copy{display:block;white-space:pre-wrap;line-height:24px;background:#f1f7f3;}
                .alternate{background:#dceee4;}.spanner{display:block;column-span:all;font-size:18px;line-height:30px;color:#146342;margin:12px 0;}
                .actions{display:flex;gap:12px;margin-top:20px;}.action{background:#dceee4;padding:8px 16px;border:1px solid #9ab8a8;border-radius:8px;}
                .action:focus{outline:2px solid #146342;outline-offset:2px;}.footer{display:block;line-height:1.6;margin-top:16px;}
                .status{display:block;line-height:1.5;color:#146342;margin-top:8px;}
                """);
            var sources = new FrameworkElement[] { before, heading, after, first, second }; var parents = sources.Select(source => source.VisualParent).ToArray();
            var original = before.Text; before.Select(0, 2); byte[]? initial = null;
            foreach (var phase in new[] { "initial", "single", "rtl-2x", "restored" })
            {
                var height = phase == "single" || baseline ? 960 : 760;
                var rtl = phase == "rtl-2x"; var scale = rtl ? 2 : 1;
                Css.SetStyle(root, "direction:" + (rtl ? "rtl" : "ltr") + ";");
                Css.SetStyle(article, phase == "single" ? "columns:auto;" : "");
                VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                status.Text = phase switch { "initial" => "当前：平衡分列与跨列标题", "single" => "当前：恢复连续单列", "rtl-2x" => "当前：RTL 阅读顺序 / 双倍像素密度", _ => "已恢复：原始列序与选择" };
                Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4];
                if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_MULTICOLUMN_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var file = File.Create(Path.Combine(path, $"columns-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment columns screenshot captured."); return 0; }
                if (!sources.Select(source => source.VisualParent).SequenceEqual(parents) || before.Text != original || before.SelectedText != "按行") throw new InvalidOperationException("Original source or selection changed");
                if (Math.Abs(heading.ActualWidth - article.ActualWidth) > .01) throw new InvalidOperationException("Heading did not span columns");
                var rects = (IReadOnlyList<Rect>)typeof(TextBlock).GetMethod("DocumentTextBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(before, [0, before.Text.Length])!;
                var columns = rects.Select(rect => Math.Round(rtl ? rect.Right : rect.X, 2)).Distinct().Count();
                if (columns != (phase == "single" ? 1 : width == 375 ? 2 : 3)) throw new InvalidOperationException("Text did not flow across the expected columns");
                var afterRects = (IReadOnlyList<Rect>)typeof(TextBlock).GetMethod("DocumentTextBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(after, [0, after.Text.Length])!;
                var afterOrigin = after.TransformToAncestor(article);
                var firstLineEdge = afterOrigin.X + (rtl ? afterRects[0].Right : afterRects[0].X);
                if (Math.Abs(firstLineEdge - (rtl ? article.ActualWidth : 0)) > .01) throw new InvalidOperationException("An empty column preceded the first line after the spanner");
                foreach (var rect in rects)
                {
                    var point = new Point(rect.X + .1, rect.Y + rect.Height / 2);
                    if (before.GetPositionFromPoint(point, true) is null) throw new InvalidOperationException("Column text point lookup failed");
                }
                foreach (var element in new FrameworkElement[] { article, first, second, status })
                {
                    var at = element.TransformToAncestor(root);
                    if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport");
                }
                if (phase is "initial" or "restored")
                {
                    var at = article.TransformToAncestor(root); var crop = new List<byte>();
                    for (var y = (int)Math.Floor(at.Y); y < Math.Min(height, Math.Ceiling(at.Y + article.ActualHeight)); y++)
                    for (var x = 20; x < width - 20; x++) for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                    if (phase == "initial") initial = crop.ToArray();
                    else if (!crop.SequenceEqual(initial!)) throw new InvalidOperationException("Restored column pixels differ");
                }
                Console.WriteLine($"Columns {engine}/{width}/{phase}: text columns={columns}, article={article.ActualWidth:0.##}x{article.ActualHeight:0.##}, selection={before.SelectedText}"); passed++;
            }
        }
        Console.WriteLine($"macOS CSS multi-column GPU checks: {passed}/16 passed"); return passed == 16 ? 0 : 1;
    }

    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static Border Action(string text) { var result = new Border { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action"); AutomationProperties.SetName(result, text); return result; }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
