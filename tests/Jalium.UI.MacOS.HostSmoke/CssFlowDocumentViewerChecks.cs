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
internal static class CssFlowDocumentViewerChecks
{
    private const string Words = "共享原文保留字体、行内背景和连续图片边框，分页与搜索使用实际的排版行及选择范围。";
    private static readonly Color Green = Color.FromRgb(20, 99, 66), Blue = Color.FromRgb(24, 75, 126), Gold = Color.FromRgb(156, 102, 23);
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
            var root = new StackPanel { Background = Brushes.White };
            Css.SetClass(root, "root"); root.Children.Add(Label("共享文档与 CSS 分页", "title"));
            var status = Label("", "description"); root.Children.Add(status);
            root.Children.Add(Label("同一份原文，保留段落层级、字体、选择范围与图片装饰。", "description"));
            var run = new Run(Words + Words); var span = new Span(); span.Inlines.Add(run); Css.SetClass(span, "piece");
            var last = new Run(Words + "定位原文"); Css.SetClass(last, "piece cloned");
            var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White, PagePadding = new(12), PageWidth = 300, PageHeight = 180 };
            doc.Blocks.Add(new Paragraph(span) { Margin = new(0, 0, 0, 12) }); doc.Blocks.Add(new Paragraph(last));
            var images = new Images(); var documentSheet = CssStyleSheet.Parse("""
                .piece{background:#dceee4;border-image:image-set('nine.png' 1x,'high.png' 2x) 33.3333% fill / 4px stretch;}
                .cloned{font-size:19px;box-decoration-break:clone;}
                """, "native-flow-viewer-document", new Uri("file:///flow-viewer/"), images);
            if (documentSheet.Diagnostics.Count != 0) throw new InvalidOperationException(string.Join(';', documentSheet.Diagnostics));
            Css.GetStyleSheets(doc).Add(documentSheet);
            var scroll = new FlowDocumentScrollViewer { Document = doc, Height = 200, IsInactiveSelectionHighlightEnabled = true, SelectionBrush = new SolidColorBrush(Color.FromRgb(195, 220, 239)), SelectionOpacity = 1 };
            var page = new FlowDocumentPageViewer { Document = doc, Height = 205, IsInactiveSelectionHighlightEnabled = true, SelectionBrush = new SolidColorBrush(Color.FromRgb(195, 220, 239)), SelectionOpacity = 1 };
            var reader = new FlowDocumentReader { Document = doc, Height = 230, ViewingMode = width > 500 ? FlowDocumentReaderViewingMode.TwoPage : FlowDocumentReaderViewingMode.Page, IsInactiveSelectionHighlightEnabled = true, SelectionBrush = new SolidColorBrush(Color.FromRgb(195, 220, 239)), SelectionOpacity = 1 };
            AutomationProperties.SetName(scroll, "滚动原文"); AutomationProperties.SetName(page, "按行分页原文"); AutomationProperties.SetName(reader, "阅读原文");
            root.Children.Add(Label("连续阅读", "heading")); root.Children.Add(scroll);
            root.Children.Add(Label("按实际行分页", "heading")); root.Children.Add(page);
            root.Children.Add(Label(width > 500 ? "双页阅读与原文搜索" : "单页阅读与原文搜索", "heading")); root.Children.Add(reader);
            root.Children.Add(Label("图片加载和密度切换只更新装饰；移除声明后，原文与选择继续保留。", "description"));
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:28px;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:16px;}
                .heading{display:block;font-size:18px;color:#146342;margin-top:16px;margin-bottom:8px;}
                """);
            var original = doc.GetText();
            foreach (var phase in new[] { "pending-1x", "loaded-1x", "loaded-2x", "restored-1x" })
            {
                var scale = phase == "loaded-2x" ? 2 : 1; VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                status.Text = phase switch { "pending-1x" => "图片加载中…", "loaded-1x" => "图片就绪，定位原文中的搜索结果", "loaded-2x" => "高密度显示，滚动视图缩放至 125%", _ => "移除图片声明，保留原始字体、背景和选择" };
                scroll.Zoom = phase == "loaded-2x" ? 125 : 100;
                if (phase != "pending-1x") images.Open();
                if (phase == "restored-1x") { Css.SetStyle(span, "border-image:none;"); Css.SetStyle(last, "border-image:none;"); }
                Layout(root, width, height); if (phase != "pending-1x") Ready(root, width, height, span, last);
                if (phase is "loaded-1x" or "loaded-2x")
                    foreach (var source in new DependencyObject[] { span, last }) VerifyDensity(source, scale == 2);
                if (phase != "pending-1x")
                {
                    if (!scroll.Find("选择范围") || !page.Find("定位原文") || !reader.Find("定位原文")) throw new InvalidOperationException("Original document search failed");
                    Layout(root, width, height);
                    if (scroll.Selection?.Text != "选择范围" || page.Selection?.Text != "定位原文" || reader.Selection?.Text != "定位原文") throw new InvalidOperationException("Original source selection lost");
                }
                if (doc.GetText() != original || doc.Parent is not null || !ReferenceEquals(span, ((Paragraph)doc.Blocks[0]).Inlines[0]) || !ReferenceEquals(run, span.Inlines[0])) throw new InvalidOperationException("Shared document was mutated or reparented");
                using var view = new NSView(new CGRect(0, 0, width, height));
                using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine);
                if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context);
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4];
                if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_FLOW_VIEWER_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var output = File.Create(Path.Combine(path, $"flow-viewer-{engine}-{width}-{phase}.png")); encoder.Save(output);
                }
                foreach (var control in new FrameworkElement[] { scroll, page, reader })
                {
                    var origin = control.TransformToAncestor(root);
                    if (origin.X < 8 || origin.Y < 0 || origin.X + control.ActualWidth > width - 8 || origin.Y + control.ActualHeight > height) throw new InvalidOperationException("Document view outside viewport");
                    var expected = scale == 2 ? Gold : Blue; var count = 0;
                    for (var y = (int)origin.Y; y < Math.Min(height, origin.Y + control.ActualHeight); y++)
                    for (var x = (int)origin.X; x < Math.Min(width, origin.X + control.ActualWidth); x++)
                    { var at = ((y * scale) * width * scale + x * scale) * 4; var color = Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]); if (Close(color, expected)) count++; }
                    if (phase is "loaded-1x" or "loaded-2x" && count < 20) throw new InvalidOperationException($"CSS image missing from {control.GetType().Name}: {count}");
                    if (phase == "restored-1x" && count != 0) throw new InvalidOperationException("Retired image still painted");
                    if (control.GetAutomationPeer()?.GetName() is not { Length: > 0 }) throw new InvalidOperationException("Document view has no accessible name");
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS FlowDocument viewer GPU checks: {passed}/16 passed"); return passed == 16 ? 0 : 1;
    }

    private static TextBlock Label(string text, string css) { var label = new TextBlock { Text = text }; Css.SetClass(label, css); return label; }
    private static void VerifyDensity(DependencyObject source, bool high)
    {
        var type = typeof(FrameworkElement).Assembly.GetType("Jalium.UI.Styling.CssBorderImageProperties")!;
        var property = (DependencyProperty)type.GetField("ResourcesProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = (Array)source.GetValue(property)!;
        if (resources.Length != 1) throw new InvalidOperationException("Expected one selected image candidate");
        var resource = resources.GetValue(0)!;
        var uri = (Uri)resource.GetType().GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resource)!;
        if (!uri.LocalPath.EndsWith(high ? "high.png" : "nine.png", StringComparison.Ordinal)) throw new InvalidOperationException("Document image-set candidate has the wrong density: " + uri);
    }
    private static void Ready(FrameworkElement root, int width, int height, params DependencyObject[] sources)
    {
        var assembly = typeof(FrameworkElement).Assembly;
        var property = (DependencyProperty)assembly.GetType("Jalium.UI.Styling.CssBorderImageProperties")!.GetField("ResourcesProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var node = assembly.GetType("Jalium.UI.Styling.CssNode")!.GetMethod("Get", BindingFlags.Static | BindingFlags.Public)!;
        for (var round = 0; round < 3; round++)
        {
            Layout(root, width, height);
            foreach (var source in sources) if (source.GetValue(property) is Array resources) foreach (var resource in resources)
            { Wait((Task)resource!.GetType().GetProperty("Ready", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resource)!); if (resource.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(resource, [node.Invoke(null, [source]), false]) is BitmapImage bitmap) Wait(bitmap.PreloadAsync(400, 200)); }
        }
        Layout(root, width, height);
    }
    private static void Layout(FrameworkElement root, int width, int height) { for (var i = 0; i < 4; i++) { Pump(); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
    private static void Pump() => typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null);
    private static void Wait(Task task) { var end = DateTime.UtcNow.AddSeconds(10); while (!task.IsCompleted && DateTime.UtcNow < end) { Pump(); Thread.Sleep(1); } if (!task.IsCompletedSuccessfully) throw new InvalidOperationException("Image operation failed", task.Exception); Pump(); }
    private static bool Close(Color a, Color b) => Math.Abs(a.R - b.R) < 4 && Math.Abs(a.G - b.G) < 4 && Math.Abs(a.B - b.B) < 4;
    private sealed class Images : ICssResourceResolver
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Open() => _gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            await _gate.Task; var high = uri.LocalPath.EndsWith("high.png"); var size = high ? 60 : 30;
            Color[] colors = [high ? Gold : Blue, high ? Gold : Green, Gold, Green, Color.FromRgb(220, 238, 228), Green, Blue, high ? Gold : Green, Gold]; var pixels = new byte[size * size * 4];
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) { var c = colors[Math.Min(2, y * 3 / size) * 3 + Math.Min(2, x * 3 / size)]; var at = (y * size + x) * 4; pixels[at] = c.B; pixels[at + 1] = c.G; pixels[at + 2] = c.R; pixels[at + 3] = 255; }
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4))); using var stream = new MemoryStream(); encoder.Save(stream); return new(uri, new MemoryStream(stream.ToArray()));
        }
    }
}
