using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Native text, rich inlines and selection alongside generated boxes.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class CssTextContentChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if (!context.IsValid || context.Backend != RenderBackend.Metal) throw new InvalidOperationException("Metal unavailable");
        var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            const int height = 760;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var texts = new List<TextBlock>();
            foreach (var (mode, title) in new[] { ("block", "正文与前后生成内容"), ("flex", "Flex 中的原生文本"), ("grid", "Grid 中的正文与选择") })
            {
                var text = new TextBlock { IsTextSelectionEnabled = true }; Css.SetClass(text, "text " + mode); text.Tag = title;
                text.Inlines.Add(new Run("TextBlock 保留原有正文，"));
                text.Inlines.Add(new Run("中文与 Inlines") { Foreground = new SolidColorBrush(Color.FromRgb(20, 76, 142)), FontWeight = FontWeights.Bold });
                text.Inlines.Add(new Run(" 正常换行，选择高亮跟随分配后的正文区域。"));
                Css.GetAttributes(text)["data-status"] = "初始内容";
                root.Children.Add(text); texts.Add(text);
            }
            var sheet = CssStyleSheet.Parse("""
                .root { display: block; padding: 20px; font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26; }
                .root::before { content: 'CSS 原生文本正文'; display: block; font-size: 25px; color: #146342; margin-bottom: 18px; }
                .text { display: block; padding: 14px; margin-bottom: 16px; background: #f0f7f3;
                    border: 1px solid #abcdbd; line-height: 1.5; white-space: normal; }
                .text::before { content: attr(Tag) / attr(Tag); display: block; font-size: 19px; color: #146342; margin-bottom: 8px; }
                .text::after { content: attr(data-status); display: block; font-size: 14px; color: #32674f; margin-top: 8px; }
                .flex { display: flex; flex-direction: column; align-items: stretch; }
                .grid { display: grid; grid-template-columns: 1fr; }
                """);
            if (sheet.Diagnostics.Count > 0) throw new InvalidOperationException("CSS diagnostics");
            Css.GetStyleSheets(root).Add(sheet);
            using var view = new NSView(new CGRect(0, 0, width, height));
            using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine);
            if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested renderer unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context);
            foreach (var updated in new[] { false, true })
            {
                if (updated)
                    foreach (var text in texts)
                    {
                        ((Run)text.Inlines[0]).Text = "更新后的正文仍由原始 TextBlock 绘制，";
                        Css.GetAttributes(text)["data-status"] = "正文与尾注已更新";
                    }
                foreach (var text in texts) text.Select(updated ? 4 : 2, 6);
                for (var i = 0; i < 5; i++) { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
                var inkRegions = new List<(Rect Bounds, string Description)>();
                foreach (var text in texts)
                {
                    var children = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(text)).Select(i => (FrameworkElement)VisualTreeHelper.GetChild(text, i)!).ToArray();
                    if (children.Length != 3 || children.OfType<TextBlock>().Count() != 2) throw new InvalidOperationException("Duplicate or missing content box");
                    var body = children.Single(child => child is not TextBlock);
                    if (body.Focusable || body.GetAutomationPeer() is not null || text.Inlines.Count != 3) throw new InvalidOperationException("Anonymous box changed source or semantics");
                    if (text.SelectedText != text.Text.Substring(updated ? 4 : 2, 6)) throw new InvalidOperationException("Selection source changed");
                    foreach (var child in children)
                    {
                        var bounds = Bounds(child);
                        if (bounds.X < 0 || bounds.Y < 0 || bounds.Right > width || bounds.Bottom > height) throw new InvalidOperationException("Content overflow: " + bounds);
                        inkRegions.Add((bounds, child is TextBlock generated ? generated.Text : text.Text));
                    }
                }
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("GPU readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4];
                if (target.FetchReadback(pixels, (uint)(width * 4), out var w, out var h) != JaliumResult.Ok || w != width || h != height) throw new InvalidOperationException("GPU readback failed");
                foreach (var (bounds, description) in inkRegions)
                {
                    var ink = 0;
                    for (var y = Math.Max(0, (int)bounds.Y); y < Math.Min(height, Math.Ceiling(bounds.Bottom)); y++)
                    for (var x = Math.Max(0, (int)bounds.X); x < Math.Min(width, Math.Ceiling(bounds.Right)); x++)
                    { var p = (y * width + x) * 4; if (pixels[p] < 170 && pixels[p + 1] < 170 && pixels[p + 2] < 170) ink++; }
                    if (ink < 3) throw new InvalidOperationException("Native text missing: " + description);
                }
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_TEXT_CONTENT_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path);
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(path, $"text-content-{engine}-{width}-{(updated ? "updated" : "initial")}.png")); encoder.Save(output);
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS text content GPU checks: {passed}/8 passed"); return passed == 8 ? 0 : 1;
    }
    private static Rect Bounds(FrameworkElement element)
    {
        var x = element.VisualBounds.X; var y = element.VisualBounds.Y;
        for (var parent = VisualTreeHelper.GetParent(element) as FrameworkElement; parent is not null; parent = VisualTreeHelper.GetParent(parent) as FrameworkElement)
        { x += parent.VisualBounds.X; y += parent.VisualBounds.Y; }
        return new(x, y, element.ActualWidth, element.ActualHeight);
    }
}
