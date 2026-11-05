using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Native glyph readback for single-child, direct-content and template hosts.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class CssContentHostChecks
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
            var height = width < 500 ? 850 : 520;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var hosts = new List<(FrameworkElement Host, TextBlock Body, string Title)>();
            foreach (var (kind, title, description) in new[]
            {
                ("border", "Border 的生成内容", "原始子级继续保留在 Child，前后文本参与流式布局。"),
                ("content", "ContentControl 的内容", "直接内容与生成盒共用 flex 排版，原有 Content 保持独立。"),
                ("presenter", "ContentPresenter 的网格", "前置文本、正文和尾注使用同一套 grid 格式化。"),
                ("template", "Control 模板与生成盒", "模板视觉子级保留原来的父级关系，生成内容不进入模板。"),
            })
            {
                var body = Body(description);
                FrameworkElement host = kind switch
                {
                    "border" => new Border { Child = body },
                    "content" => new ContentControl { Content = body },
                    "presenter" => new ContentPresenter { Content = body },
                    _ => new Control { Template = Template(body) },
                };
                Css.SetClass(host, "host " + kind); host.Tag = title;
                Css.GetAttributes(host)["data-status"] = "状态：初始内容";
                var frame = new Border { Child = host }; Css.SetClass(frame, "frame"); root.Children.Add(frame);
                hosts.Add((host, body, title));
            }
            var sheet = CssStyleSheet.Parse("""
                .root { display: flex; flex-wrap: wrap; gap: 16px; padding: 20px; align-items: start; align-content: start;
                    font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26; }
                .root::before { content: 'CSS 内容宿主'; flex: 0 0 100%; height: 36px; font-size: 24px; color: #146342; }
                .root::after { content: '原生内容、模板与生成内容协同排版'; flex: 0 0 100%; height: 26px; font-size: 14px; }
                .frame { width: 280px; padding: 12px; background: #e9f4ef; border: 1px solid #abcdbd; }
                .host { display: block; }
                .host::before { content: attr(Tag) / attr(Tag); display: block; font-size: 18px; color: #146342; }
                .host::after { content: attr(data-status); display: block; font-size: 14px; color: #146342; }
                .border::before, .template::before { margin-bottom: 8px; }
                .border::after, .template::after { margin-top: 8px; }
                .content { display: flex; flex-direction: column; align-items: stretch; gap: 8px; }
                .presenter { display: grid; grid-template-columns: 1fr; row-gap: 8px; }
                """);
            if (sheet.Diagnostics.Count > 0) throw new InvalidOperationException("CSS diagnostics: " + string.Join(", ", sheet.Diagnostics));
            Css.GetStyleSheets(root).Add(sheet);
            using var view = new NSView(new CGRect(0, 0, width, height));
            using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine);
            if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested renderer unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context);
            foreach (var updated in new[] { false, true })
            {
                if (updated)
                    for (var i = 0; i < hosts.Count; i++)
                    {
                        var entry = hosts[i]; entry.Host.Tag = entry.Title + "已更新";
                        Css.GetAttributes(entry.Host)["data-status"] = "状态：更新完成";
                        var replacement = Body("替换后的原生内容正常排版，生成盒继续保留。 ");
                        switch (entry.Host)
                        {
                            case Border border: border.Child = replacement; break;
                            case ContentControl control: control.Content = replacement; break;
                            case ContentPresenter presenter: presenter.Content = replacement; break;
                            case Control templated: templated.Template = Template(replacement); break;
                        }
                        hosts[i] = (entry.Host, replacement, entry.Title);
                    }
                for (var i = 0; i < 5; i++)
                { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
                foreach (var (host, body, title) in hosts)
                {
                    var generated = Generated(host, body);
                    if (generated.Length != 2 || generated[0].Text != title + (updated ? "已更新" : "") || generated[1].Text != (updated ? "状态：更新完成" : "状态：初始内容"))
                        throw new InvalidOperationException("Generated content stale");
                    if (VisualTreeHelper.GetParent(body) != host || VisualTreeHelper.GetChildrenCount(host) != 3 || generated.Any(t => t.Focusable))
                        throw new InvalidOperationException("Native content or focus contract changed");
                    if (Jalium.UI.Automation.AutomationProperties.GetName(generated[0]) != generated[0].Text)
                        throw new InvalidOperationException("Generated alternative stale");
                    foreach (var text in generated.Append(body))
                    { var rect = Bounds(text); if (rect.X < 0 || rect.Y < 0 || rect.Right > width || rect.Bottom > height) throw new InvalidOperationException($"Text overflow: {title}; text={text.Text}; rect={rect}; host={host.VisualBounds}; desired={host.DesiredSize}; textFont={text.FontSize}; textDesired={text.DesiredSize}"); }
                }
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("GPU readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4];
                if (target.FetchReadback(pixels, (uint)(width * 4), out var actualWidth, out var actualHeight) != JaliumResult.Ok ||
                    actualWidth != width || actualHeight != height) throw new InvalidOperationException("GPU readback failed");
                foreach (var text in hosts.SelectMany(entry => Generated(entry.Host, entry.Body).Append(entry.Body)))
                {
                    var rect = Bounds(text); var ink = 0;
                    for (var y = Math.Max(0, (int)rect.Y); y < Math.Min(height, Math.Ceiling(rect.Bottom)); y++)
                    for (var x = Math.Max(0, (int)rect.X); x < Math.Min(width, Math.Ceiling(rect.Right)); x++)
                    { var pixel = (y * width + x) * 4; if (pixels[pixel] < 170 && pixels[pixel + 1] < 170 && pixels[pixel + 2] < 170) ink++; }
                    if (ink < 3) throw new InvalidOperationException("Native text missing: " + text.Text + " " + rect);
                }
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_CONTENT_HOST_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path);
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(path, $"content-hosts-{engine}-{width}-{(updated ? "updated" : "initial")}.png")); encoder.Save(output);
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS content hosts GPU checks: {passed}/8 passed");
        return passed == 8 ? 0 : 1;
    }
    private static TextBlock Body(string text)
    { var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; Css.SetStyle(body, "display: block; line-height: 1.5;"); return body; }
    private static ControlTemplate Template(TextBlock body)
    { var template = new ControlTemplate(typeof(Control)); template.SetVisualTree(() => body); return template; }
    private static TextBlock[] Generated(FrameworkElement host, TextBlock body) => Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(host))
        .Select(i => VisualTreeHelper.GetChild(host, i)).OfType<TextBlock>().Where(text => !ReferenceEquals(text, body)).ToArray();
    private static Rect Bounds(FrameworkElement element)
    {
        var x = element.VisualBounds.X; var y = element.VisualBounds.Y;
        for (var parent = VisualTreeHelper.GetParent(element) as FrameworkElement; parent is not null; parent = VisualTreeHelper.GetParent(parent) as FrameworkElement)
        { x += parent.VisualBounds.X; y += parent.VisualBounds.Y; }
        return new(x, y, element.ActualWidth, element.ActualHeight);
    }
}
