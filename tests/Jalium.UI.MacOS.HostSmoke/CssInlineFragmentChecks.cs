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
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Native text, rich inlines and selection alongside generated boxes.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class CssInlineFragmentChecks
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
            const int height = 920;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var texts = new List<TextBlock>();
            foreach (var (mode, title) in new[] { ("plain", "前置标签："), ("decorated", "生成文字在窄窗口中会拆成多个片段，并在最后一行衔接原始正文："), ("mixed", "块与行内生成内容") })
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
                .root::before { content: 'CSS 行内文字碎分'; display: block; font-size: 25px; color: #146342; margin-bottom: 18px; }
                .text { display: block; padding: 14px; margin-bottom: 18px; background: #f0f7f3;
                    border: 1px solid #abcdbd; line-height: 1.6; white-space: normal; }
                .text::before { content: attr(Tag) / attr(Tag); display: inline; color: #146342; }
                .text::after { content: ' 〔' attr(data-status) '〕'; display: inline; color: #32674f; }
                .decorated::before { padding: 2px 4px; border: 1px solid #4c8c6b; background: #d3eadc;
                    border-radius: 4px; box-decoration-break: var(--decoration, slice); }
                .mixed::before { display: block; margin-bottom: 8px; font-size: 19px; }
                """);
            if (sheet.Diagnostics.Count > 0) throw new InvalidOperationException("CSS diagnostics");
            Css.GetStyleSheets(root).Add(sheet);
            using var view = new NSView(new CGRect(0, 0, width, height));
            using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine);
            if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested renderer unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context);
            foreach (var phase in new[] { "initial", "tail-only", "updated" })
            {
                var updated = phase == "updated";
                if (phase == "tail-only")
                    foreach (var text in texts) Css.GetAttributes(text)["data-status"] = "内容更新";
                if (updated)
                    foreach (var text in texts)
                    {
                        Css.SetStyle(text, "--decoration: clone;");
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
                    var generated = children.OfType<TextBlock>().ToArray();
                    var status = phase == "initial" ? "初始内容" : phase == "tail-only" ? "内容更新" : "正文与尾注已更新";
                    if (generated[0].Text != text.Tag?.ToString() || generated[1].Text != " 〔" + status + "〕")
                        throw new InvalidOperationException("Generated source text stale in " + phase);
                    if (body.Focusable || body.GetAutomationPeer() is not null || text.Inlines.Count != 3) throw new InvalidOperationException("Anonymous box changed source or semantics");
                    if (width == 375 && ReferenceEquals(text, texts[1]))
                    {
                        var prefix = children.OfType<TextBlock>().First();
                        var prefixLines = Segments(prefix); var bodyLines = Segments(text);
                        if (prefixLines.Count < 2 || Math.Abs(Bounds(prefix).Y + prefixLines[^1].Baseline -
                                Bounds(text).Y - bodyLines[0].Baseline) > .01)
                            throw new InvalidOperationException("Long generated text did not continue into the original body on its last line");
                    }
                    if (text.SelectedText != text.Text.Substring(updated ? 4 : 2, 6)) throw new InvalidOperationException("Selection source changed");
                    foreach (var child in children)
                    {
                        var bounds = Bounds(child);
                        if (bounds.X < 0 || bounds.Y < 0 || bounds.Right > width || bounds.Bottom > height) throw new InvalidOperationException("Content overflow: " + bounds);
                        var source = child as TextBlock ?? text;
                        var origin = Bounds(source);
                        var segments = Segments(source);
                        foreach (var segment in segments)
                            if (segment.EndX > segment.StartX)
                                inkRegions.Add((new Rect(origin.X + segment.StartX, origin.Y + segment.Baseline - 22,
                                    segment.EndX - segment.StartX, 30), source.Text));
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
                    { var p = (y * width + x) * 4; if (pixels[p] < 180 && pixels[p + 1] < 150 && pixels[p + 2] < 150) ink++; }
                    if (ink < 3) throw new InvalidOperationException("Native text missing: " + description);
                }
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_INLINE_FRAGMENT_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path);
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(path, $"inline-fragments-{engine}-{width}-{phase}.png")); encoder.Save(output);
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS inline fragments GPU checks: {passed}/12 passed"); return passed == 12 ? 0 : 1;
    }
    private static IReadOnlyList<(double StartX, double EndX, double Baseline)> Segments(TextBlock source) =>
        (IReadOnlyList<(double StartX, double EndX, double Baseline)>)typeof(TextBlock)
            .GetMethod("GetSourceRangeSegments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, [0, source.Text.Length])!;
    private static Rect Bounds(FrameworkElement element)
    {
        var x = element.VisualBounds.X; var y = element.VisualBounds.Y;
        for (var parent = VisualTreeHelper.GetParent(element) as FrameworkElement; parent is not null; parent = VisualTreeHelper.GetParent(parent) as FrameworkElement)
        { x += parent.VisualBounds.X; y += parent.VisualBounds.Y; }
        return new(x, y, element.ActualWidth, element.ActualHeight);
    }
}
