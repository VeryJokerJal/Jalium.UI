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
internal static class CssPaginationChecks
{
    private static readonly Color Ink = Color.FromRgb(35, 60, 50);
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if (!context.IsValid || context.Backend != RenderBackend.Metal) throw new InvalidOperationException("Metal unavailable");
        var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            const int height = 1100;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("由内容决定的分页", "title")); var status = Label("", "description"); root.Children.Add(status);
            root.Children.Add(Label("标题跟随正文，段落保留首尾行；需要时，为左右页留出空白。", "description"));
            var heading = Para("标题", 2); var body = Para("正文", 2); var keepDoc = Doc(Para("前文", 1), heading, body);
            var widowBody = Para("段落", 5); var widowDoc = Doc(Para("前文", 2), widowBody);
            var next = Para("下一章", 1); var sideDoc = Doc(Para("上一章", 1), next);
            var keepView = View(keepDoc, "标题与正文"); var widowView = View(widowDoc, "段落首尾行"); var sideView = View(sideDoc, "左右页与留白");
            var keepLabel = Label("", "heading"); var widowLabel = Label("", "heading"); var sideLabel = Label("", "heading");
            foreach (var element in new FrameworkElement[] { keepLabel, keepView, widowLabel, widowView, sideLabel, sideView }) root.Children.Add(element);
            root.Children.Add(Label("调整规则后重新分页，原始段落、文字与来源索引保持不变。", "description"));
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:16px;}
                .heading{display:block;font-size:18px;color:#146342;margin-top:16px;margin-bottom:8px;}
                """);
            var docs = new[] { keepDoc, widowDoc, sideDoc }; var originals = docs.Select(doc => doc.GetText()).ToArray();
            foreach (var phase in new[] { "blank", "content", "relaxed", "restored-rtl" })
            {
                var relaxed = phase == "relaxed";
                Css.SetStyle(heading, relaxed ? "break-after:auto;" : phase == "restored-rtl" ? "page-break-after:avoid;" : "break-after:avoid;");
                Css.SetStyle(widowBody, relaxed ? "orphans:1;widows:1;" : "orphans:3;widows:2;");
                sideDoc.FlowDirection = phase == "restored-rtl" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                Css.SetStyle(next, relaxed ? "page-break-before:auto;" : phase == "restored-rtl" ? "break-before:recto;" : "page-break-before:right;");
                status.Text = phase switch { "blank" => "强制从右页开始，先查看自动留出的空白页", "content" => "继续下一页，原文从指定页侧开始", "relaxed" => "放宽相邻与首尾行规则，内容重新分配", _ => "恢复约束，切换为从右向左的页序" };
                Layout(root, width, height);
                if (Page(keepDoc, "标题") != (relaxed ? 1 : 2) || Page(keepDoc, "正文") != 2 || Page(widowDoc, "段落") != (relaxed ? 1 : 2)) throw new InvalidOperationException("Keep/widow pagination mismatch");
                if (((IDocumentPaginatorSource)sideDoc).DocumentPaginator.PageCount != (relaxed ? 1 : 3) || Page(sideDoc, "下一章") != (relaxed ? 1 : 3)) throw new InvalidOperationException("Page-side pagination mismatch");
                keepView.GoToPage(relaxed ? 1 : 2); widowView.GoToPage(2); sideView.GoToPage(phase == "blank" ? 2 : relaxed ? 1 : 3);
                keepLabel.Text = relaxed ? "标题与正文 / 已放宽相邻规则" : "标题与正文 / 保持在同一页";
                widowLabel.Text = relaxed ? "段落 / 放宽首尾行数量" : "段落 / 断点前至少 3 行，断点后至少 2 行";
                sideLabel.Text = phase == "blank" ? "左右页 / 自动留白的第 2 页" : relaxed ? "左右页 / 移除强制规则后合为 1 页" : "左右页 / 原文从第 3 页开始";
                Layout(root, width, height);
                if (!docs.Select(doc => doc.GetText()).SequenceEqual(originals) || docs.Any(doc => doc.Parent is not null)) throw new InvalidOperationException("Pagination changed document content or ownership");
                if (!ReferenceEquals(((IDocumentPaginatorSource)keepDoc).DocumentPaginator, keepView.PageViews[0].DocumentPaginator)) throw new InvalidOperationException("Paginator identity changed");
                using var view = new NSView(new CGRect(0, 0, width, height));
                using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
                target.SetDpi(96, 96); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context);
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4]; if (target.FetchReadback(pixels, (uint)(width * 4), out var w, out var h) != JaliumResult.Ok || w != width || h != height) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_PAGINATION_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                    using var output = File.Create(Path.Combine(path, $"pagination-{engine}-{width}-{phase}.png")); encoder.Save(output);
                }
                foreach (var control in new[] { keepView, widowView, sideView })
                {
                    var origin = control.TransformToAncestor(root); if (origin.X < 8 || origin.X + control.ActualWidth > width - 8 || origin.Y + control.ActualHeight > height) throw new InvalidOperationException("Page outside viewport");
                    if (control.PageViews[0].DocumentPage.Visual is null) throw new InvalidOperationException("Blank page was confused with a missing page");
                    var ink = 0;
                    for (var y = (int)origin.Y; y < Math.Min(height, origin.Y + control.ActualHeight); y++)
                    for (var x = (int)origin.X; x < Math.Min(width, origin.X + control.ActualWidth); x++)
                    {
                        var at = (y * width + x) * 4;
                        // Glyph coverage and renderer color conversion can change the exact RGB value.
                        var b = pixels[at]; var g = pixels[at + 1]; var r = pixels[at + 2];
                        if (r < 100 && g < 130 && b < 120 && g > r + 10 && b > r && b < g) ink++;
                    }
                    if (control == sideView && phase == "blank" ? ink != 0 : ink < 20) throw new InvalidOperationException($"Wrong page ink in {phase} ({AutomationProperties.GetName(control)}): {ink}");
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS pagination GPU checks: {passed}/16 passed"); return passed == 16 ? 0 : 1;
    }
    private static TextBlock Label(string text, string css) { var label = new TextBlock { Text = text }; Css.SetClass(label, css); return label; }
    private static Paragraph Para(string name, int lines)
    { var p = new Paragraph { Margin = new(0), Background = new SolidColorBrush(Color.FromRgb(220, 238, 228)) }; for (var i = 1; i <= lines; i++) { if (i > 1) p.Inlines.Add(new LineBreak()); p.Inlines.Add(new Run($"{name}第 {i} 行，保留原文")); } return p; }
    private static FlowDocument Doc(params Block[] blocks)
    { var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = new SolidColorBrush(Ink), Background = Brushes.White, PageWidth = 300, PageHeight = 140, PagePadding = new(20) }; foreach (var block in blocks) doc.Blocks.Add(block); return doc; }
    private static FlowDocumentPageViewer View(FlowDocument doc, string name)
    { var view = new FlowDocumentPageViewer { Document = doc, Height = 170 }; AutomationProperties.SetName(view, name); return view; }
    private static int Page(FlowDocument doc, string text)
    { var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator; var offset = doc.GetText().IndexOf(text, StringComparison.Ordinal); return (int)paginator.GetType().GetMethod("GetPageNumberForOffset", BindingFlags.Instance | BindingFlags.Public)!.Invoke(paginator, [offset])!; }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}
