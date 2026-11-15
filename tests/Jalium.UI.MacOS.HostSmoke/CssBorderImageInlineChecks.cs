using System.Reflection;
using AppKit;
using CoreGraphics;
using Jalium.UI;
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
internal static class CssBorderImageInlineChecks
{
    private const string Words = "图片边框跟随每一行的实际位置连续展开，保留原始正文、选择范围与文档节点，窄窗口和宽窗口使用相同的内容。";
    private static readonly Color Blue = Color.FromRgb(24, 75, 126), Green = Color.FromRgb(20, 99, 66), Gold = Color.FromRgb(156, 102, 23);

    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if (!context.IsValid || context.Backend != RenderBackend.Metal) throw new InvalidOperationException("Metal unavailable");
        var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            var height = width == 375 ? 1200 : 1000;
            var resolver = new Images(); var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var title = Text("跨行图片边框", "title"); var status = Text("", "description"); root.Children.Add(title); root.Children.Add(status);
            var text = Text(Words, "body piece sliced");
            var flow = new StackPanel(); Css.SetClass(flow, "flow"); flow.Children.Add(Text("正文：", "body")); flow.Children.Add(text);
            root.Children.Add(Row("连续切片 / slice", flow));
            var cloned = new Border { Child = Text(Words, "body") }; Css.SetClass(cloned, "piece cloned");
            var cloneFlow = new StackPanel(); Css.SetClass(cloneFlow, "flow"); cloneFlow.Children.Add(Text("内容：", "body")); cloneFlow.Children.Add(cloned);
            root.Children.Add(Row("逐行完整边框 / clone", cloneFlow));
            var run = new Run(Words); var span = new Span { Background = Brushes.White }; span.Inlines.Add(run); Css.SetClass(span, "document-image");
            var document = Text("", "document"); document.Inlines.Add(span);
            root.Children.Add(Row("文档 Span / Run", document));
            var rtlRun = new Run(Words); Css.SetClass(rtlRun, "document-image");
            var rtl = Text("", "document rtl"); rtl.Inlines.Add(rtlRun);
            root.Children.Add(Row("右到左段落的首尾边缘", rtl));
            root.Children.Add(Text("边框装饰保留原始文字节点。声明移除后，选择与排版位置保持不变。", "description"));
            var sheet = CssStyleSheet.Parse("""
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;white-space:normal;}
                .title{display:block;font-size:26px;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:18px;}
                .row{display:block;padding:12px;background:#f4f8f5;margin-bottom:18px;}
                .heading{display:block;font-size:17px;color:#146342;margin-bottom:10px;}
                .flow{display:flow-root;padding-top:6px;line-height:2;}
                .body{display:inline;}
                .piece{display:inline;border:4px solid transparent;border-image:url('nine.png') calc(100% / 3) fill / 4px round;}
                .cloned{box-decoration-break:clone;padding:0 2px;}
                .document{display:block;line-height:2.2;white-space:normal;}
                .document-image{border-image:image-set('nine.png' 1x,'high.png' 2x) calc(100% / 3) fill / 4px round;}
                .rtl{direction:rtl;}
                """, "native-border-inline", new Uri("file:///border-inline/"), resolver);
            if (sheet.Diagnostics.Count != 0) throw new InvalidOperationException(string.Join(';', sheet.Diagnostics));
            Css.GetStyleSheets(root).Add(sheet);
            var sources = new DependencyObject[] { text, cloned, span, rtlRun };
            Rect[][]? initial = null;
            foreach (var phase in new[] { "pending-1x", "loaded-1x", "loaded-2x", "restored-1x" })
            {
                var scale = phase == "loaded-2x" ? 2 : 1; VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                status.Text = phase switch { "pending-1x" => "图片加载中…", "loaded-1x" => "图片已就绪，连续切片与逐行克隆", "loaded-2x" => "高密度图片，正文与选择保留", _ => "移除两处图片声明，恢复原始文字" };
                if (phase != "pending-1x") resolver.Open();
                if (phase == "restored-1x")
                {
                    Css.SetStyle(text, "border-image:none;"); Css.SetStyle(span, "border-image:none;");
                }
                Layout(root, width, height);
                if (phase != "pending-1x") Ready(root, width, height, sources);
                var fragments = new[] { Pieces(text), Pieces(cloned), Pieces(document, span), Pieces(rtl, rtlRun) };
                if (fragments.Any(pieces => pieces.Length < 2)) throw new InvalidOperationException("Fixture did not fragment at " + width);
                if (initial is null) initial = fragments;
                else if (!initial.SelectMany(p => p).SequenceEqual(fragments.SelectMany(p => p))) throw new InvalidOperationException("Image state changed text layout");
                text.Select(5, 4); document.Select(5, 4); rtl.Select(5, 4);
                foreach (var source in new[] { text, document, rtl })
                    if (source.Text != Words || source.SelectedText != Words.Substring(5, 4)) throw new InvalidOperationException("Original selection changed");
                if (!ReferenceEquals(document.Inlines[0], span) || !ReferenceEquals(span.Inlines[0], run) || !ReferenceEquals(rtl.Inlines[0], rtlRun)) throw new InvalidOperationException("Original document identity changed");
                if (document.GetAutomationPeer()!.GetName() != Words) throw new InvalidOperationException("Decorative image changed accessibility name");
                using var view = new NSView(new CGRect(0, 0, width, height));
                using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine);
                if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context);
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * scale * scale * 4];
                if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                Color Pixel(double x, double y) { var at = ((int)(y * scale) * (int)w + (int)(x * scale)) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]); }
                for (var group = 0; group < 4; group++)
                {
                    var owner = group switch { 0 => (FrameworkElement)text, 1 => cloned, 2 => document, _ => rtl };
                    var point = owner.TransformToAncestor(root); var pieces = fragments[group];
                    foreach (var piece in pieces)
                        if (point.X + piece.X < 8 || point.Y + piece.Y < 0 || point.X + piece.Right > width - 8 || point.Y + piece.Bottom > height) throw new InvalidOperationException("Fragment outside viewport");
                    if (phase == "pending-1x" || phase == "restored-1x" && group is 0 or 2) continue;
                    var rtlGroup = group == 3; var first = pieces[0];
                    var x = point.X + (rtlGroup ? first.Right - 2 : first.X + 1);
                    var expected = group >= 2 && scale == 2 ? (rtlGroup ? Blue : Gold) : rtlGroup ? Gold : Blue;
                    if (!Close(Pixel(x, point.Y + first.Y + 1), expected)) throw new InvalidOperationException($"Wrong fragment corner {group}: {Pixel(x, point.Y + first.Y + 1)} expected {expected}");
                    if (group < 2)
                    {
                        var next = pieces[1]; var color = Pixel(point.X + next.X + 1, point.Y + next.Y + 1);
                        if (!Close(color, group == 1 ? Blue : Green)) throw new InvalidOperationException("Image restarted at a slice or failed to clone: " + color);
                    }
                    var hit = root.InputHitTest(new(x, point.Y + first.Y + 1));
                    if (!ReferenceEquals(hit, owner)) throw new InvalidOperationException("Fragment input ownership changed: " + hit?.GetType());
                }
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_BORDER_INLINE_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var output = File.Create(Path.Combine(path, $"border-inline-{engine}-{width}-{phase}.png")); encoder.Save(output);
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS border-image inline GPU checks: {passed}/16 passed"); return passed == 16 ? 0 : 1;
    }

    private static TextBlock Text(string value, string css)
    { var text = new TextBlock { Text = value, IsTextSelectionEnabled = true, SelectionBrush = new SolidColorBrush(Color.FromRgb(195, 220, 239)) }; Css.SetClass(text, css); return text; }
    private static StackPanel Row(string heading, UIElement content)
    { var row = new StackPanel(); Css.SetClass(row, "row"); row.Children.Add(Text(heading, "heading")); row.Children.Add(content); return row; }
    private static Rect[] Pieces(FrameworkElement owner, Inline? inline = null)
    {
        object? source;
        if (inline is not null) source = typeof(TextBlock).GetMethod("GetDocumentDecorationFragments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, [inline]);
        else if (owner is TextBlock) source = typeof(TextBlock).GetField("_cssDecorationFragments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
        else
        {
            var allocation = typeof(FrameworkElement).GetField("CssInlineBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
            source = allocation.GetType().GetProperty("Fragments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(allocation);
        }
        return ((Array)source!).Cast<object>().Select(piece => (Rect)piece.GetType().GetProperty("Rect")!.GetValue(piece)!).ToArray();
    }
    private static void Ready(FrameworkElement root, int width, int height, DependencyObject[] owners)
    {
        var assembly = typeof(FrameworkElement).Assembly; var node = assembly.GetType("Jalium.UI.Styling.CssNode")!.GetMethod("Get", BindingFlags.Static | BindingFlags.Public)!;
        var property = (DependencyProperty)assembly.GetType("Jalium.UI.Styling.CssBorderImageProperties")!.GetField("ResourcesProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        for (var round = 0; round < 3; round++)
        {
            Layout(root, width, height);
            foreach (var owner in owners) if (owner.GetValue(property) is Array resources) foreach (var resource in resources)
            {
                Wait((Task)resource!.GetType().GetProperty("Ready", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resource)!);
                if (resource.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(resource, [node.Invoke(null, [owner]), false]) is BitmapImage bitmap) Wait(bitmap.PreloadAsync(400, 200));
            }
        }
        Layout(root, width, height);
    }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 5; i++) { Pump(); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
    private static void Pump() => typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null);
    private static void Wait(Task task)
    { var end = DateTime.UtcNow.AddSeconds(10); while (!task.IsCompleted && DateTime.UtcNow < end) { Pump(); Thread.Sleep(1); } if (!task.IsCompletedSuccessfully) throw new InvalidOperationException("Image operation failed", task.Exception); Pump(); }
    private static bool Close(Color first, Color second) => Math.Abs(first.R - second.R) < 4 && Math.Abs(first.G - second.G) < 4 && Math.Abs(first.B - second.B) < 4;
    private static byte[] Nine(int size, bool high)
    {
        Color[] colors = [high ? Gold : Blue, Green, high ? Blue : Gold, Green, Color.FromRgb(220, 238, 228), Green, Blue, Green, Gold];
        var bytes = new byte[size * size * 4];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        { var color = colors[Math.Min(2, y * 3 / size) * 3 + Math.Min(2, x * 3 / size)]; var at = (y * size + x) * 4; bytes[at] = color.B; bytes[at + 1] = color.G; bytes[at + 2] = color.R; bytes[at + 3] = 255; }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, bytes, size * 4)));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
    private sealed class Images : ICssResourceResolver
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Open() => _gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        { await _gate.Task; return new(uri, new MemoryStream(Nine(uri.LocalPath.EndsWith("high.png") ? 60 : 30, uri.LocalPath.EndsWith("high.png")))); }
    }
}
