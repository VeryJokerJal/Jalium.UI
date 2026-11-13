using System.Collections;
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

/// <summary>Real native text, selection and box decoration across nested inline fragments.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class CssNestedInlineChecks
{
    private sealed record Row(StackPanel Host, FrameworkElement Frame, TextBlock Lead, TextBlock Body, TextBlock Tail, Run Original, Border? Float, string Mode);
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if (!context.IsValid || context.Backend != RenderBackend.Metal) throw new InvalidOperationException("Metal unavailable");
        var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            const int height = 1180;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var rows = new List<Row>();
            foreach (var (mode, title) in new[] { ("slice", "多层容器 · 边框连续断开"), ("clone", "逐行重复 · 保留两侧留白"), ("pseudo", "原始正文与嵌套伪元素"), ("float", "容器内浮动 · 外层文字环绕") })
            {
                var row = new StackPanel { Tag = title }; Css.SetClass(row, "row"); root.Children.Add(row);
                var lead = new TextBlock { Text = "外层文字：" }; Css.SetClass(lead, "lead source"); row.Children.Add(lead);
                var body = new TextBlock { IsTextSelectionEnabled = true }; Css.SetClass(body, mode == "pseudo" ? "body source pseudo" : "body source");
                var original = new Run("嵌套容器中的原始正文在同一段落里连续换行，"); body.Inlines.Add(original);
                body.Inlines.Add(new Run("Run 与选择范围") { Foreground = new SolidColorBrush(Color.FromRgb(20, 76, 142)), FontWeight = FontWeights.Bold });
                body.Inlines.Add(new Run(" 始终保留，不会复制或移动原始子级。"));
                Css.GetAttributes(body)["data-status"] = "初始内容";
                var inner = new Border { Child = body }; Css.SetClass(inner, "inner");
                var content = new StackPanel(); Css.SetClass(content, "inline-content");
                Border? floating = null;
                if (mode == "float")
                {
                    var label = new TextBlock { Text = "侧栏" }; floating = new Border { Child = label }; Css.SetClass(floating, "float"); content.Children.Add(floating);
                }
                content.Children.Add(inner);
                var frame = new Border { Child = content }; Css.SetClass(frame, "frame variant-" + mode); row.Children.Add(frame);
                var tail = new TextBlock { Text = " 〔初始状态〕" }; Css.SetClass(tail, "tail source"); row.Children.Add(tail);
                rows.Add(new(row, frame, lead, body, tail, original, floating, mode));
            }
            var sheet = CssStyleSheet.Parse("""
                .root { display: block; padding: 20px; font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26; }
                .root::before { content: 'CSS 嵌套行内容器'; display: block; font-size: 25px; color: #146342; margin-bottom: 18px; }
                .row { display: flow-root; padding: 14px; margin-bottom: 18px; background: #f4f8f5; border: 1px solid #abcdbd; line-height: 1.85; }
                .row::before { content: attr(Tag); display: block; color: #146342; font-size: 18px; margin-bottom: 8px; }
                .source, .inline-content, .frame, .inner { display: inline; }
                .lead, .tail { color: #32674f; }
                .frame { padding: 0 5px; border: 1px solid #468467; border-radius: 4px; background: #def0e5; }
                .inner { padding: 0 3px; border-left: 1px solid #6b9f86; border-right: 1px solid #6b9f86; }
                .variant-clone { box-decoration-break: clone; margin: 0 3px; }
                .variant-clone .inner { box-decoration-break: clone; }
                .pseudo::before { content: '【'; display: inline; color: #146342; }
                .pseudo::after { content: ' / ' attr(data-status) '】'; display: inline; color: #146342; }
                .float { float: var(--side, left); width: 50px; height: 55px; margin: 0 8px 5px 0; padding: 5px; border: 1px solid #468467; background: #c8e4d4; color: #146342; }
                """);
            if (sheet.Diagnostics.Count > 0) throw new InvalidOperationException("CSS diagnostics"); Css.GetStyleSheets(root).Add(sheet);
            using var view = new NSView(new CGRect(0, 0, width, height));
            using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine);
            if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested renderer unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context);
            foreach (var phase in new[] { "initial", "same-width", "updated" })
            {
                foreach (var row in rows)
                {
                    if (phase != "initial") { row.Tail.Text = " 〔更新状态〕"; Css.GetAttributes(row.Body)["data-status"] = "更新内容"; }
                    if (phase == "updated")
                    {
                        row.Original.Text = "更新正文以后，" + row.Original.Text;
                        if (row.Mode == "clone") { Css.SetStyle(row.Frame, "box-decoration-break: slice;"); row.Host.Tag = "动态切换 · 外层边框连续断开"; }
                        if (row.Float is not null) Css.SetStyle(row.Float, "--side: right;");
                    }
                    row.Body.Select(2, 6);
                }
                root.Measure(new Size(0, height)); root.Measure(new Size(double.PositiveInfinity, height)); root.Measure(new Size(width + 97, height));
                for (var i = 0; i < 5; i++) { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
                var ink = new List<Rect>();
                foreach (var row in rows)
                {
                    if (!ReferenceEquals(row.Body.Inlines[0], row.Original) || row.Body.Inlines.Count != 3 || row.Body.SelectedText != row.Body.Text.Substring(2, 6))
                        throw new InvalidOperationException("Original text or selection changed");
                    if (row.Host.Children.Count != 3 || !ReferenceEquals(row.Host.Children[1], row.Frame)) throw new InvalidOperationException("Native tree changed");
                    foreach (var text in new[] { row.Lead, row.Body, row.Tail })
                    foreach (var rect in LineRects(text))
                    {
                        if (rect.X < -.01 || rect.Y < -.01 || rect.Right > width + .01 || rect.Bottom > height + .01) throw new InvalidOperationException($"Text overflow: {row.Mode}/{text.Text} {rect} root={root.DesiredSize}");
                        var point = new Point(rect.X + rect.Width * .3, rect.Y + rect.Height * .65);
                        var hit = root.InputHitTest(point);
                        if (!ReferenceEquals(text, hit)) throw new InvalidOperationException($"Nested hit ownership: {row.Mode}/{text.Text}, hit={hit?.GetType().Name}/{(hit as TextBlock)?.Text}, point={point}, source={Bounds(text)}, rect={rect}, direct={root.HitTest(point)?.VisualHit?.GetType().Name}");
                        ink.Add(rect);
                        if (row.Float is not null && !ReferenceEquals(text, row.Lead))
                        {
                            var obstacle = Bounds(row.Float);
                            if (rect.IntersectsWith(obstacle) && Math.Min(rect.Right, obstacle.Right) - Math.Max(rect.X, obstacle.X) > .01 && Math.Min(rect.Bottom, obstacle.Bottom) - Math.Max(rect.Y, obstacle.Y) > .01)
                                throw new InvalidOperationException("Nested float overlaps source");
                        }
                    }
                    if (width == 375 && LineRects(row.Body).Count < 2) throw new InvalidOperationException("Narrow content did not fragment");
                }
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("GPU readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4];
                if (target.FetchReadback(pixels, (uint)(width * 4), out var w, out var h) != JaliumResult.Ok || w != width || h != height) throw new InvalidOperationException("GPU readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_NESTED_INLINE_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path);
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(path, $"nested-inline-{engine}-{width}-{phase}.png")); encoder.Save(output);
                }
                foreach (var bounds in ink)
                {
                    var count = 0;
                    for (var y = Math.Max(0, (int)bounds.Y); y < Math.Min(height, Math.Ceiling(bounds.Bottom)); y++)
                    for (var x = Math.Max(0, (int)bounds.X); x < Math.Min(width, Math.Ceiling(bounds.Right)); x++)
                    { var p = (y * width + x) * 4; if (pixels[p] < 180 && pixels[p + 1] < 150 && pixels[p + 2] < 150) count++; }
                    if (count < 3) throw new InvalidOperationException("Native nested glyphs missing");
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS nested inline GPU checks: {passed}/12 passed"); return passed == 12 ? 0 : 1;
    }
    private static IReadOnlyList<Rect> LineRects(TextBlock source)
    {
        var segments = (IReadOnlyList<(double StartX, double EndX, double Baseline)>)typeof(TextBlock)
            .GetMethod("GetSourceRangeSegments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, [0, source.Text.Length])!;
        var lines = ((IEnumerable)typeof(TextBlock).GetField("_layoutLines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!).Cast<object>()
            .Where(line => (int)line.GetType().GetProperty("Length")!.GetValue(line)! > 0).ToArray();
        if (segments.Count != lines.Length) throw new InvalidOperationException("Unexpected source fragment coverage");
        var origin = Bounds(source); var result = new List<Rect>();
        for (var i = 0; i < lines.Length; i++)
        {
            var baseline = (double)lines[i].GetType().GetProperty("Baseline")!.GetValue(lines[i])!;
            var height = (double)lines[i].GetType().GetProperty("Height")!.GetValue(lines[i])!;
            result.Add(new Rect(origin.X + segments[i].StartX, origin.Y + segments[i].Baseline - baseline,
                segments[i].EndX - segments[i].StartX, height));
        }
        return result;
    }
    private static Rect Bounds(FrameworkElement element)
    {
        var x = element.VisualBounds.X; var y = element.VisualBounds.Y;
        for (var parent = VisualTreeHelper.GetParent(element) as FrameworkElement; parent is not null; parent = VisualTreeHelper.GetParent(parent) as FrameworkElement)
        { x += parent.VisualBounds.X; y += parent.VisualBounds.Y; }
        return new(x, y, element.ActualWidth, element.ActualHeight);
    }
}
