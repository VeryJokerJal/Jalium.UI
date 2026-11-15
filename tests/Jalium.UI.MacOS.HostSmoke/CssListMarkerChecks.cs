using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Jalium.UI.MacOS;

/// <summary>Native glyph readback for list formatting before and after live updates.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class CssListMarkerChecks
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
            var height = width < 500 ? 850 : 500;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            Css.GetAttributes(root)["data-state"] = "自动编号、独立样式与动态更新";
            var groups = new Dictionary<string, StackPanel>();
            var items = new Dictionary<string, List<StackPanel>>();
            foreach (var (kind, heading, labels) in new[]
            {
                ("outside", "外侧编号与正文换行", new[] { "自动编号保持顺序，正文换行后仍与首行对齐。", "标记使用自定义补位数字。", "项目更新会重新计算编号。" }),
                ("inside", "内侧标记参与排版", new[] { "接收任务", "确认内容", "完成交付" }),
                ("reversed", "倒序列表自动计数", new[] { "准备发布", "检查资源", "验证结果" }),
                ("rtl", "从右侧显示标记", new[] { "标记靠右，与正文首行对齐。" }),
            })
            {
                var card = new StackPanel(); Css.SetClass(card, "card");
                var title = new TextBlock { Text = heading }; Css.SetClass(title, "heading"); card.Children.Add(title);
                var list = new StackPanel(); Css.SetClass(list, "list " + kind); groups.Add(kind, list); items[kind] = [];
                foreach (var label in labels)
                {
                    var row = new StackPanel(); Css.SetClass(row, "item");
                    var body = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }; Css.SetClass(body, "body");
                    row.Children.Add(body); list.Children.Add(row); items[kind].Add(row);
                }
                card.Children.Add(list); root.Children.Add(card);
            }
            var sheet = CssStyleSheet.Parse("""
                @counter-style padded { system: extends decimal; pad: 2 '0'; suffix: '. '; }
                .root { display: flex; flex-wrap: wrap; gap: 16px; padding: 20px; align-items: start; align-content: start;
                    font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26; }
                .root::before { content: 'CSS 列表与标记'; flex: 0 0 100%; height: 36px; font-size: 24px; color: #146342; }
                .root::after { content: attr(data-state); flex: 0 0 100%; height: 26px; font-size: 14px; }
                .card { display: block; width: 280px; padding: 12px; background: #e9f4ef; border: 1px solid #abcdbd; }
                .heading { display: block; font-size: 18px; margin-bottom: 12px; color: #146342; }
                .list { display: block; padding-left: 38px; counter-reset: list-item; }
                .item { display: list-item; list-style: padded outside; margin-bottom: 8px; line-height: 1.5; }
                .item::marker { color: #146342; font-weight: 600; }
                .body { display: block; }
                .inside .item { list-style: symbols(cyclic '甲' '乙' '丙') inside; }
                .inside .body { display: inline-block; }
                .reversed { counter-reset: reversed(list-item); }
                .reversed .item { list-style-type: decimal; }
                .rtl { direction: rtl; padding: 0 40px 0 0; }
                .rtl .item::marker { content: counter(list-item) '、 ' / '编号' counter(list-item); }
                """);
            if (sheet.Diagnostics.Count > 0) throw new InvalidOperationException("CSS diagnostics: " + string.Join(", ", sheet.Diagnostics));
            Css.GetStyleSheets(root).Add(sheet);
            using var view = new NSView(new CGRect(0, 0, width, height));
            using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine);
            if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested renderer unavailable: " + engine);
            using var drawing = new RenderTargetDrawingContext(target, context);
            foreach (var updated in new[] { false, true })
            {
                if (updated)
                {
                    Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@counter-style padded { system: extends decimal; pad: 3 '0'; suffix: '. '; }"));
                    Css.GetAttributes(root)["data-state"] = "已更新编号、位置与倒序项目";
                    ((TextBlock)((Panel)VisualTreeHelper.GetParent(groups["inside"])!).Children[0]).Text = "内侧改为外侧";
                    foreach (var row in items["inside"]) Css.SetStyle(row, "list-style-position: outside;");
                    items["reversed"][1].Visibility = Visibility.Collapsed;
                }
                for (var i = 0; i < 5; i++)
                { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
                CheckText(items["outside"], updated ? ["001. ", "002. ", "003. "] : ["01. ", "02. ", "03. "]);
                CheckText(items["inside"], ["甲 ", "乙 ", "丙 "]);
                CheckText(items["reversed"].Where(row => row.Visibility != Visibility.Collapsed).ToList(), updated ? ["2. ", "1. "] : ["3. ", "2. ", "1. "]);
                CheckText(items["rtl"], ["1、 "]);
                if (Jalium.UI.Automation.AutomationProperties.GetName(Generated(items["rtl"][0])) != "编号1") throw new InvalidOperationException("Alternative marker name stale");
                var visible = items.Values.SelectMany(rows => rows).Where(row => row.Visibility != Visibility.Collapsed).ToArray();
                foreach (var row in visible)
                {
                    if (row.Children.Count != 1 || Generated(row).Focusable) throw new InvalidOperationException("Marker changed document or focus order");
                    var rect = Bounds(Generated(row));
                    if (rect.X < 0 || rect.Y < 0 || rect.Right > width || rect.Bottom > height) throw new InvalidOperationException("Marker overflow: " + rect);
                }
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("GPU readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4];
                if (target.FetchReadback(pixels, (uint)(width * 4), out var actualWidth, out var actualHeight) != JaliumResult.Ok ||
                    actualWidth != width || actualHeight != height) throw new InvalidOperationException("GPU readback failed");
                foreach (var row in visible)
                {
                    var bounds = Bounds(Generated(row)); var ink = 0;
                    for (var y = Math.Max(0, (int)bounds.Y); y < Math.Min(height, Math.Ceiling(bounds.Bottom)); y++)
                    for (var x = Math.Max(0, (int)bounds.X); x < Math.Min(width, Math.Ceiling(bounds.Right)); x++)
                    { var p = (y * width + x) * 4; if (pixels[p] < 170 && pixels[p + 1] < 170 && pixels[p + 2] < 170) ink++; }
                    if (ink < 3) throw new InvalidOperationException("Native marker glyph missing: " + Generated(row).Text + " " + bounds);
                }
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_LIST_MARKER_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path);
                    var name = $"list-markers-{engine}-{width}-{(updated ? "updated" : "initial")}";
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(path, name + ".png")); encoder.Save(output);
                    File.WriteAllText(Path.Combine(path, name + ".json"), JsonSerializer.Serialize(visible.Select(row =>
                        new { Text = Generated(row).Text, Bounds = Bounds(Generated(row)), Body = Bounds((FrameworkElement)row.Children[0]) }), new JsonSerializerOptions { WriteIndented = true }));
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS list markers GPU checks: {passed}/8 passed");
        return passed == 8 ? 0 : 1;
    }

    private static TextBlock Generated(Panel host) => Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(host))
        .Select(i => VisualTreeHelper.GetChild(host, i)).OfType<TextBlock>().Single(text => !host.Children.Contains(text));
    private static void CheckText(List<StackPanel> rows, string[] expected)
    {
        if (!rows.Select(row => Generated(row).Text).SequenceEqual(expected)) throw new InvalidOperationException("Wrong native list markers");
    }
    private static Rect Bounds(FrameworkElement element)
    {
        var x = element.VisualBounds.X; var y = element.VisualBounds.Y;
        for (var parent = VisualTreeHelper.GetParent(element) as FrameworkElement; parent is not null; parent = VisualTreeHelper.GetParent(parent) as FrameworkElement)
        { x += parent.VisualBounds.X; y += parent.VisualBounds.Y; }
        return new(x, y, element.ActualWidth, element.ActualHeight);
    }
}
