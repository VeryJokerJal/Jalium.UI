using System.Reflection;
using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Runtime.Versioning;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssRichDocumentImageChecks
{
    private const string Words = "正文保留原始文档节点和选择范围，图片边框沿用原生行片段的坐标，滚动后继续保持完整的切片相位。";
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
            const int height = 1100; var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("富文本文档图片边框", "title")); var status = Label("", "description"); root.Children.Add(status);
            root.Children.Add(Label("连续切片与逐行克隆跟随原生行盒，文档节点、选择和文本可访问接口保持不变。", "description"));
            var sliceRun = new Run(string.Concat(Enumerable.Repeat(Words, 6))); var sliceSpan = new Span(); sliceSpan.Inlines.Add(sliceRun); Css.SetClass(sliceSpan, "piece");
            var slice = Editor(new Paragraph(sliceSpan), 180, "连续切片编辑器");
            root.Children.Add(Label("连续切片与内部滚动 / slice", "heading")); root.Children.Add(slice);
            var cloneRun = new Run(Words + Words); var clone = Editor(new Paragraph(cloneRun), 160, "逐行克隆编辑器"); Css.SetClass(cloneRun, "piece cloned");
            root.Children.Add(Label("每行独立的完整边框 / clone", "heading")); root.Children.Add(clone);
            var nestedRun = new Run(Words + Words); var paragraph = new Paragraph(nestedRun) { Margin = new(6, 0, 0, 10) };
            var section = new Section { Margin = new(10, 0, 0, 8) }; var items = new DocList(); var item = new ListItem(); item.Blocks.Add(paragraph); items.ListItems.Add(item); section.Blocks.Add(items);
            Css.SetClass(nestedRun, "piece"); var nested = Editor(section, 160, "嵌套文档编辑器");
            root.Children.Add(Label("Section / List 中的文字坐标", "heading")); root.Children.Add(nested);
            root.Children.Add(Label("加载、密度与声明更新只改变装饰。滚动裁剪之外的片段仍参与连续图片区域的计算。", "description"));
            var images = new Images(); var sheet = CssStyleSheet.Parse("""
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;white-space:normal;}
                .title{display:block;font-size:26px;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:18px;}
                .heading{display:block;font-size:17px;color:#146342;margin-bottom:10px;}
                .editor{display:block;margin-bottom:22px;}
                .piece{border-image:image-set('nine.png' 1x,'high.png' 2x) calc(100% / 3) fill / 4px stretch;}
                .cloned{box-decoration-break:clone;border-image-repeat:round;}
                """, "native-rich-document-image", new Uri("file:///rich-document-image/"), images);
            if (sheet.Diagnostics.Count != 0) throw new InvalidOperationException(string.Join(';', sheet.Diagnostics));
            Css.GetStyleSheets(root).Add(sheet); var editors = new[] { slice, clone, nested }; var sources = new Inline[] { sliceSpan, cloneRun, nestedRun };
            var originals = editors.Select(editor => editor.Document.GetText()).ToArray(); Rect[]? initial = null;
            foreach (var phase in new[] { "pending-1x", "scrolled-1x", "loaded-2x", "restored-1x" })
            {
                var scale = phase == "loaded-2x" ? 2 : 1; VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                status.Text = phase switch { "pending-1x" => "图片加载中…", "scrolled-1x" => "图片就绪，首个文档向下滚动 43 px", "loaded-2x" => "高密度图片，滚动与原始选择保留", _ => "移除首个文档的图片声明，恢复原始内容" };
                if (phase != "pending-1x") images.Open();
                if (phase == "restored-1x") Css.SetStyle(sliceSpan, "border-image:none;");
                Layout(root, width, height); if (phase != "pending-1x") Ready(root, width, height, sources);
                for (var i = 0; i < editors.Length; i++)
                {
                    editors[i].Selection.Select(editors[i].Document.GetPositionAtOffset(5, LogicalDirection.Forward)!,
                        editors[i].Document.GetPositionAtOffset(9, LogicalDirection.Forward)!);
                    if (editors[i].Document.GetText() != originals[i] || editors[i].Selection.Text != originals[i].Substring(5, 4)) throw new InvalidOperationException("Original document or selection changed");
                    var peer = editors[i].GetAutomationPeer()!;
                    var provider = peer.GetPattern(Jalium.UI.Automation.Peers.PatternInterface.Text) as Jalium.UI.Automation.Provider.ITextProvider;
                    if (provider?.DocumentRange.GetText(-1) != originals[i]) throw new InvalidOperationException("Decorative source replaced accessibility text");
                }
                slice.ScrollToVerticalOffset(phase is "scrolled-1x" or "loaded-2x" ? 43 : 0); Layout(root, width, height);
                var pieces = new[] { Pieces(slice, sliceSpan), Pieces(clone, cloneRun), Pieces(nested, nestedRun) };
                if (initial is null) initial = pieces[0];
                if (phase != "restored-1x")
                {
                    var shift = phase is "scrolled-1x" or "loaded-2x" ? 43 : 0;
                    if (pieces[0].Length != initial.Length || !pieces[0].Zip(initial).All(pair => Math.Abs(pair.First.Y - pair.Second.Y + shift) < .001 && pair.First.Size == pair.Second.Size)) throw new InvalidOperationException("Scroll or image update changed source layout");
                }
                if (!ReferenceEquals(((Paragraph)slice.Document.Blocks[0]).Inlines[0], sliceSpan) || !ReferenceEquals(sliceSpan.Inlines[0], sliceRun)) throw new InvalidOperationException("Document source was cloned");
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
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_RICH_DOCUMENT_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var output = File.Create(Path.Combine(path, $"rich-document-{engine}-{width}-{phase}.png")); encoder.Save(output);
                }
                for (var i = 0; i < editors.Length; i++)
                {
                    var owner = editors[i]; var point = owner.TransformToAncestor(root); var content = Content(owner);
                    if (point.X < 8 || point.Y < 0 || point.X + owner.ActualWidth > width - 8 || point.Y + owner.ActualHeight > height) throw new InvalidOperationException("Editor outside viewport");
                    if (phase == "restored-1x" && i == 0) continue;
                    var visible = pieces[i].Select((rect, index) => (rect, index)).First(piece => piece.rect.Y >= content.Y && piece.rect.Y + 3 < content.Bottom);
                    var at = new Point(visible.rect.X + 1, visible.rect.Y + 1);
                    if (phase != "pending-1x")
                    {
                        var expected = scale == 2 ? Gold : i == 1 || visible.index == 0 ? Blue : Green;
                        var actual = Pixel(point.X + at.X, point.Y + at.Y);
                        if (!Close(actual, expected)) throw new InvalidOperationException($"Wrong rich-document slice {i}: {actual} expected {expected}");
                    }
                    if (!ReferenceEquals(root.InputHitTest(new(point.X + at.X, point.Y + at.Y)), owner)) throw new InvalidOperationException("Image introduced a new input target");
                    var hit = owner.GetPositionFromPoint(new Point(at.X + 2, visible.rect.Y + visible.rect.Height / 2), true);
                    if (hit is null || !ReferenceEquals(hit.Document, owner.Document)) throw new InvalidOperationException("Document hit test lost its source");
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS rich-document image GPU checks: {passed}/16 passed"); return passed == 16 ? 0 : 1;
    }

    private static TextBlock Label(string text, string css) { var label = new TextBlock { Text = text }; Css.SetClass(label, css); return label; }
    private static RichTextBox Editor(Block block, double height, string name)
    {
        var document = new FlowDocument { FontSize = 16, FontFamily = "PingFang SC", Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)) }; document.Blocks.Add(block);
        var editor = new RichTextBox(document) { Height = height, Padding = new(10), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Green), Background = Brushes.White,
            IsReadOnly = true, SelectionBrush = new SolidColorBrush(Color.FromRgb(195, 220, 239)) };
        AutomationProperties.SetName(editor, name); Css.SetClass(editor, "editor"); return editor;
    }
    private static Rect Content(RichTextBox editor) => new(editor.Padding.Left + editor.BorderThickness.Left, editor.Padding.Top + editor.BorderThickness.Top,
        editor.ActualWidth - editor.Padding.Left - editor.Padding.Right - editor.BorderThickness.Left - editor.BorderThickness.Right,
        editor.ActualHeight - editor.Padding.Top - editor.Padding.Bottom - editor.BorderThickness.Top - editor.BorderThickness.Bottom);
    private static Rect[] Pieces(RichTextBox editor, Inline source) => ((Array)typeof(RichTextBox).GetMethod("GetDocumentDecorationFragments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [source])!).Cast<object>()
        .Select(piece => (Rect)piece.GetType().GetProperty("Rect")!.GetValue(piece)!).ToArray();
    // The resolver is gated until the pending frame has rendered. Observe each selected
    // bitmap BEFORE its first preload; pixel publication precedes the UI load notification
    // which invalidates retained drawings and GPU raster entries.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapImage, TaskCompletionSource> ImageNotifications = new();

    private static void Ready(FrameworkElement root, int width, int height, Inline[] sources)
    {
        var assembly = typeof(FrameworkElement).Assembly; var node = assembly.GetType("Jalium.UI.Styling.CssNode")!.GetMethod("Get", BindingFlags.Static | BindingFlags.Public)!;
        var property = (DependencyProperty)assembly.GetType("Jalium.UI.Styling.CssBorderImageProperties")!.GetField("ResourcesProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        for (var round = 0; round < 3; round++)
        {
            Layout(root, width, height);
            foreach (var source in sources) if (source.GetValue(property) is Array resources) foreach (var resource in resources)
            {
                Wait((Task)resource!.GetType().GetProperty("Ready", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resource)!);
                if (resource.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(resource, [node.Invoke(null, [source]), false]) is BitmapImage bitmap)
                {
                    var notification = ImageNotifications.GetValue(bitmap, image =>
                    {
                        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        image.OnImageLoaded += (_, _) => ready.TrySetResult();
                        return ready;
                    });
                    Wait(bitmap.PreloadAsync(400, 200));
                    Wait(notification.Task);
                }
            }
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
