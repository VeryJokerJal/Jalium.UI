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

/// <summary>Native Metal paragraphs follow float exclusion bands and retain original text sources.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class CssInlineFloatChecks
{
    private sealed record Row(StackPanel Host, Border Float, TextBlock? Lead, TextBlock Body, TextBlock Tail, Run Original, string Side);
    private const string BodyText = "正文根据浮动的实际范围连续换行，越过底边后恢复整行宽度。";
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if (!context.IsValid || context.Backend != RenderBackend.Metal) throw new InvalidOperationException("Metal unavailable");
        var passed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 375, 720 })
        {
            const int height = 1280;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var rows = new List<Row>();
            foreach (var (mode, side, title) in new[] { ("left", "left", "左侧浮动"), ("right", "right", "右侧浮动"), ("late", "right", "正文中途遇到浮动") })
            {
                var row = new StackPanel { Tag = title }; Css.SetClass(row, "row " + mode); root.Children.Add(row);
                TextBlock? lead = null;
                if (mode == "late") { lead = new TextBlock { Text = "先排原始子级的文字，浮动只能从它在正文中的位置开始，前面的行不会被挪到浮动旁边。" }; Css.SetClass(lead, "lead"); row.Children.Add(lead); }
                var label = new TextBlock { Text = "侧栏" };
                var floating = new Border { Child = label }; Css.SetClass(floating, "float"); Css.SetStyle(floating, "--side: " + side + ";");
                if (mode == "left")
                {
                    var exporter = new Border { Child = floating }; Css.SetStyle(exporter, "display: block;"); row.Children.Add(exporter);
                    row.Tag = "普通容器导出浮动";
                }
                else row.Children.Add(floating);
                var body = new TextBlock { IsTextSelectionEnabled = true }; Css.SetClass(body, "body"); row.Children.Add(body);
                var original = new Run(BodyText); body.Inlines.Add(original);
                body.Inlines.Add(new Run("原始 Run、选择与命中") { Foreground = new SolidColorBrush(Color.FromRgb(20, 76, 142)), FontSize = 18, FontWeight = FontWeights.Bold });
                body.Inlines.Add(new Run(" 始终保留各自的来源，不会落入浮动区域。"));
                var tail = new TextBlock { Text = " 〔初始内容〕" }; Css.SetClass(tail, "tail"); row.Children.Add(tail);
                rows.Add(new(row, floating, lead, body, tail, original, side));
            }
            var own = new TextBlock { IsTextSelectionEnabled = true }; Css.SetClass(own, "own"); root.Children.Add(own);
            var ownRun = new Run("TextBlock 自身的原始正文也能绕过浮动伪元素，保留选择与富文本，并在侧栏结束后恢复整行排版。行尾继续显示生成的状态文字。"); own.Inlines.Add(ownRun);
            Css.GetAttributes(own)["data-status"] = "初始内容";
            var sheet = CssStyleSheet.Parse("""
                .root { display: block; padding: 20px; font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26; }
                .root::before { content: 'CSS 共享行框与浮动'; display: block; font-size: 25px; color: #146342; margin-bottom: 18px; }
                .row, .own { display: flow-root; padding: 14px; margin-bottom: 18px; background: #f0f7f3;
                    border: 1px solid #abcdbd; line-height: 1.65; white-space: normal; }
                .row::before { content: attr(Tag); display: block; color: #146342; font-size: 18px; margin-bottom: 8px; }
                .lead, .body, .tail { display: inline; }
                .lead, .tail { color: #32674f; }
                .float, .own::before { float: var(--side, left); width: 66px; height: 62px; padding: 6px;
                    border: 1px solid #4c8c6b; border-radius: 4px; margin: 0 10px 6px 0; background: #d3eadc; color: #146342; }
                .own::before { content: '伪元素'; }
                .own::after { content: ' 〔' attr(data-status) '〕'; color: #32674f; }
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
                if (phase != "initial")
                {
                    foreach (var item in rows) item.Tail.Text = updated ? " 〔浮动与正文已更新〕" : " 〔内容更新〕";
                    Css.GetAttributes(own)["data-status"] = updated ? "浮动与正文已更新" : "内容更新";
                }
                if (updated)
                {
                    foreach (var item in rows)
                    {
                        var side = item.Side == "left" ? "right" : "left";
                        Css.SetStyle(item.Float, "--side: " + side + ";");
                        item.Host.Tag = item.Lead is null ? (side == "left" ? "已切换为左浮动" : "已切换为右浮动") : "正文中途遇到左浮动";
                        item.Original.Text = "更新正文并重新计算浮动排除区，" + BodyText;
                    }
                    Css.SetStyle(own, "--side: right;"); ownRun.Text = "更新后，" + ownRun.Text;
                }
                foreach (var item in rows) item.Body.Select(updated ? 4 : 2, 6); own.Select(2, 6);
                root.Measure(new Size(0, height)); root.Measure(new Size(double.PositiveInfinity, height)); root.Measure(new Size(width + 97, height));
                for (var i = 0; i < 5; i++) { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
                var inkRegions = new List<(Rect Bounds, string Description)>();
                foreach (var item in rows)
                {
                    if (!ReferenceEquals(item.Body.Inlines[0], item.Original) || item.Body.Inlines.Count != 3 ||
                        item.Body.SelectedText != item.Body.Text.Substring(updated ? 4 : 2, 6)) throw new InvalidOperationException("Native source or selection changed");
                    if (item.Host.Children.Count != (item.Lead is null ? 3 : 4) || !ReferenceEquals(VisualTreeHelper.GetParent(item.Body), item.Host))
                        throw new InvalidOperationException("Native child structure changed");
                    var floated = Bounds(item.Float);
                    inkRegions.Add((new Rect(floated.X + 8, floated.Y + 8, floated.Width - 16, floated.Height - 16), "Float sidebar"));
                    foreach (var source in new[] { item.Lead, item.Body, item.Tail }.OfType<TextBlock>())
                        CheckSource(root, source, floated, width, height, inkRegions);
                    if (item.Lead is not null && LineRects(item.Lead) is { Count: > 1 } leadLines && floated.Y + .01 < leadLines[^2].Bottom)
                        throw new InvalidOperationException("Late float rose above previously completed text lines");
                    if (width == 375 && !LineRects(item.Body).Any(rect => rect.Y >= floated.Bottom && rect.X < floated.X + 1))
                        throw new InvalidOperationException("Narrow paragraph did not recover after the float bottom");
                }
                var pseudo = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(own)).Select(i => VisualTreeHelper.GetChild(own, i)).OfType<TextBlock>().First();
                var pseudoBounds = Bounds(pseudo);
                inkRegions.Add((new Rect(pseudoBounds.X + 8, pseudoBounds.Y + 8, pseudoBounds.Width - 16, pseudoBounds.Height - 16), "Floated pseudo"));
                CheckSource(root, own, pseudoBounds, width, height, inkRegions);
                if (!ReferenceEquals(own.Inlines[0], ownRun) || own.SelectedText != own.Text.Substring(2, 6)) throw new InvalidOperationException("Own generated float changed the source");
                var pointInFloat = new Point(pseudoBounds.X - Bounds(own).X + 5, pseudoBounds.Y - Bounds(own).Y + 5);
                if (own.GetPositionFromPoint(pointInFloat, false) is not null) throw new InvalidOperationException("Float region became a body text position");
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("GPU readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4];
                if (target.FetchReadback(pixels, (uint)(width * 4), out var w, out var h) != JaliumResult.Ok || w != width || h != height) throw new InvalidOperationException("GPU readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_INLINE_FLOAT_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path);
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(path, $"inline-floats-{engine}-{width}-{phase}.png")); encoder.Save(output);
                }
                foreach (var (bounds, description) in inkRegions)
                {
                    var ink = 0;
                    for (var y = Math.Max(0, (int)bounds.Y); y < Math.Min(height, Math.Ceiling(bounds.Bottom)); y++)
                    for (var x = Math.Max(0, (int)bounds.X); x < Math.Min(width, Math.Ceiling(bounds.Right)); x++)
                    { var p = (y * width + x) * 4; if (pixels[p] < 180 && pixels[p + 1] < 150 && pixels[p + 2] < 150) ink++; }
                    if (ink < 3) throw new InvalidOperationException("Native text missing: " + description);
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS inline float GPU checks: {passed}/12 passed"); return passed == 12 ? 0 : 1;
    }
    private static void CheckSource(FrameworkElement root, TextBlock source, Rect floating, int width, int height, List<(Rect Bounds, string Description)> ink)
    {
        foreach (var rect in LineRects(source))
        {
            if (rect.X < -.01 || rect.Y < -.01 || rect.Right > width + .01 || rect.Bottom > height + .01) throw new InvalidOperationException("Text overflow: " + source.Text);
            if (rect.IntersectsWith(floating) && Math.Min(rect.Right, floating.Right) - Math.Max(rect.X, floating.X) > .01 && Math.Min(rect.Bottom, floating.Bottom) - Math.Max(rect.Y, floating.Y) > .01)
                throw new InvalidOperationException("Text overlaps float: " + source.Text);
            var point = new Point(rect.X + rect.Width * .2, rect.Y + rect.Height * .65);
            if (!ReferenceEquals(source, root.InputHitTest(point))) throw new InvalidOperationException("Fragment pointer ownership stale");
            ink.Add((rect, source.Text));
        }
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
