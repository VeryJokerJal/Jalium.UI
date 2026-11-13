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

/// <summary>Shared inline line boxes over several original native child elements.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class CssSharedInlineChecks
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
            const int height = 940;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var rows = new List<(StackPanel Row, TextBlock Head, TextBlock Body, TextBlock Tail, Run Original)>();
            foreach (var (mode, title) in new[] { ("plain", "标签："), ("decorated", "行内文字拆成多个片段，在最后一行接续下一个原始子级："), ("atomic", "原生控件：") })
            {
                var row = new StackPanel(); Css.SetClass(row, "row " + mode); root.Children.Add(row);
                var head = new TextBlock { Text = title }; Css.SetClass(head, "head"); row.Children.Add(head);
                if (mode == "atomic")
                {
                    var label = new TextBlock { Text = "独立盒", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(20, 99, 66)) };
                    var box = new Border { Child = label }; Css.SetClass(box, "atomic-box"); row.Children.Add(box);
                }
                var body = new TextBlock { IsTextSelectionEnabled = true }; Css.SetClass(body, "body"); row.Children.Add(body);
                var original = new Run("多个 TextBlock 连续换行，"); body.Inlines.Add(original);
                body.Inlines.Add(new Run("原始 Run 与选择") { Foreground = new SolidColorBrush(Color.FromRgb(20, 76, 142)), FontWeight = FontWeights.Bold });
                body.Inlines.Add(new Run(" 保持各自来源，片段不会占据整块空白。"));
                var tail = new TextBlock { Text = " 〔初始内容〕" }; Css.SetClass(tail, "tail"); row.Children.Add(tail);
                rows.Add((row, head, body, tail, original));
            }
            var sheet = CssStyleSheet.Parse("""
                .root { display: block; padding: 20px; font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26; }
                .root::before { content: 'CSS 跨子级共享行框'; display: block; font-size: 25px; color: #146342; margin-bottom: 18px; }
                .row { display: block; padding: 14px; margin-bottom: 18px; background: #f0f7f3;
                    border: 1px solid #abcdbd; line-height: 1.7; white-space: normal; }
                .head, .body, .tail { display: inline; }
                .plain { line-padding: 3px; }
                .head { color: #146342; }
                .tail { color: #32674f; }
                .decorated > .head { padding: 2px 4px; border: 1px solid #4c8c6b; background: #d3eadc;
                    border-radius: 4px; box-decoration-break: var(--decoration, slice); }
                .atomic-box { display: inline-block; width: 66px; height: 30px; padding: 3px 5px;
                    border: 1px solid #4c8c6b; background: #d3eadc; vertical-align: middle; }
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
                if (phase != "initial") foreach (var item in rows) item.Tail.Text = updated ? " 〔正文与尾注已更新〕" : " 〔内容更新〕";
                if (updated) foreach (var item in rows)
                { Css.SetStyle(item.Head, "--decoration: clone;"); item.Original.Text = "更新后的子级仍共享同一段落，"; }
                // Exercise a different width and both intrinsic probes before replaying this viewport.
                root.Measure(new Size(width + 97, height)); root.Measure(new Size(0, height)); root.Measure(new Size(double.PositiveInfinity, height));
                foreach (var item in rows) item.Body.Select(updated ? 4 : 2, 6);
                for (var i = 0; i < 5; i++) { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
                var inkRegions = new List<(Rect Bounds, string Description)>();
                foreach (var item in rows)
                {
                    if (!ReferenceEquals(item.Body.Inlines[0], item.Original) || item.Body.Inlines.Count != 3 ||
                        item.Body.SelectedText != item.Body.Text.Substring(updated ? 4 : 2, 6)) throw new InvalidOperationException("Source or selection changed");
                    if (!ReferenceEquals(VisualTreeHelper.GetParent(item.Head), item.Row) || item.Row.Children.Count != (ReferenceEquals(item.Row, rows[2].Row) ? 4 : 3))
                        throw new InvalidOperationException("Original child collection changed");
                    var head = Segments(item.Head); var body = Segments(item.Body); var tail = Segments(item.Tail);
                    if (!ReferenceEquals(item.Row, rows[2].Row))
                    {
                        var headBounds = Bounds(item.Head); var bodyBounds = Bounds(item.Body);
                        if (Math.Abs(headBounds.Y + head[^1].Baseline - bodyBounds.Y - body[0].Baseline) > .01 ||
                            Math.Abs(headBounds.X + head[^1].EndX + (ReferenceEquals(item.Row, rows[1].Row) ? 5 : 0) - bodyBounds.X - body[0].StartX) > .01)
                            throw new InvalidOperationException("Native sibling text did not share a line");
                    }
                    if (width == 375 && ReferenceEquals(item.Row, rows[1].Row) && head.Count < 2) throw new InvalidOperationException("Decorated prefix did not fragment");
                    foreach (var source in new[] { item.Head, item.Body, item.Tail })
                    {
                        var bounds = Bounds(source);
                        if (bounds.X < 0 || bounds.Y < 0 || bounds.Right > width || bounds.Bottom > height) throw new InvalidOperationException("Content overflow: " + bounds);
                        foreach (var segment in Segments(source)) if (segment.EndX > segment.StartX)
                        {
                            var point = new Point(bounds.X + (segment.StartX + segment.EndX) / 2, bounds.Y + segment.Baseline - 3);
                            if (!ReferenceEquals(source, root.InputHitTest(point))) throw new InvalidOperationException("Fragment pointer hit stale: " + source.Text);
                            inkRegions.Add((new Rect(bounds.X + segment.StartX, bounds.Y + segment.Baseline - 22,
                                segment.EndX - segment.StartX, 30), source.Text));
                        }
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
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_SHARED_INLINE_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path);
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(path, $"shared-inline-{engine}-{width}-{phase}.png")); encoder.Save(output);
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS shared inline GPU checks: {passed}/12 passed"); return passed == 12 ? 0 : 1;
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
