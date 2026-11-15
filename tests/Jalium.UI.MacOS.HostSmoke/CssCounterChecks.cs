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

/// <summary>Actual glyph rasterization and GPU readback for dynamic CSS content.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class CssCounterChecks
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
            const int height = 420;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var rows = new List<StackPanel>();
            foreach (var name in new[] { "面板 A", "面板 B", "面板 C" })
            {
                var row = new StackPanel { Tag = name }; Css.SetClass(row, "card");
                row.Children.Add(new TextBlock { Text = "生成内容跟随文档顺序。", TextWrapping = TextWrapping.Wrap });
                root.Children.Add(row); rows.Add(row);
            }
            var sheet = CssStyleSheet.Parse("""
                .root { display: flex; flex-wrap: wrap; gap: 16px; padding: 20px;
                    align-items: start; align-content: start; counter-reset: reversed(step) total;
                    quotes: '“' '”' '‘' '’'; font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26; }
                .root::before { content: 'CSS 计数器'; flex: 0 0 100%; height: 36px; font-size: 24px; color: #146342; }
                .card { display: block; width: 280px; padding: 12px; background: #e9f4ef; border: 1px solid #abcdbd; counter-increment: step -1 total; }
                .card::before { content: '#' counter(step, decimal-leading-zero) ' ' open-quote attr(Tag) close-quote; height: 30px; font-size: 18px; }
                .root::after { content: '合计：' counter(total) ' 项'; flex: 0 0 100%; height: 28px; }
                """);
            if (sheet.Diagnostics.Count > 0) throw new InvalidOperationException("CSS diagnostics: " + string.Join(", ", sheet.Diagnostics));
            Css.GetStyleSheets(root).Add(sheet);
            using var view = new NSView(new CGRect(0, 0, width, height));
            using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine);
            if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested renderer unavailable: " + engine);
            using var drawing = new RenderTargetDrawingContext(target, context);
            foreach (var hidden in new[] { false, true })
            {
                rows[1].Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
                for (var i = 0; i < 4; i++)
                { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
                var first = Generated(rows[0]);
                var last = Generated(rows[2]);
                if (first != (hidden ? "#02 “面板 A”" : "#03 “面板 A”") || last != "#01 “面板 C”")
                    throw new InvalidOperationException($"Wrong dynamic content: {first}, {last}");
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("GPU readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4];
                if (target.FetchReadback(pixels, (uint)(width * 4), out var actualWidth, out var actualHeight) != JaliumResult.Ok ||
                    actualWidth != width || actualHeight != height) throw new InvalidOperationException("GPU readback failed");
                // Unlike the managed bitmap fixture, this must contain native text glyphs.
                var dark = Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4] < 130 && pixels[i * 4 + 1] < 130 && pixels[i * 4 + 2] < 130);
                if (dark < 100) throw new InvalidOperationException("Generated glyphs missing from GPU output");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_COUNTER_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path);
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(path, $"counters-{engine}-{width}-{(hidden ? "hidden" : "all")}.png")); encoder.Save(output);
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS counters and quotes GPU checks: {passed}/8 passed");
        return passed == 8 ? 0 : 1;
    }

    private static string Generated(Panel host) => Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(host))
        .Select(i => VisualTreeHelper.GetChild(host, i)).OfType<TextBlock>().First(text => !host.Children.Contains(text)).Text;
}
