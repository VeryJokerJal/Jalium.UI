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

/// <summary>Native glyphs, layout and dynamic replacement of named counter definitions.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class CssCounterStyleChecks
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
            const int height = 560;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var rows = new List<StackPanel>();
            foreach (var (kind, label) in new[] { ("pad", "继承十进制并补位"), ("alpha", "字母序列跨越单字符"), ("additive", "加法符号组合"), ("anonymous", "匿名二进制编号") })
            {
                var row = new StackPanel(); Css.SetClass(row, "card " + kind);
                row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
                root.Children.Add(row); rows.Add(row);
            }
            var sheet = CssStyleSheet.Parse("""
                @layer defaults {
                    @counter-style padded { system: extends decimal; pad: 3 '0'; }
                    @counter-style alpha { system: alphabetic; symbols: A B C D E F G H I J K L M N O P Q R S T U V W X Y Z; }
                    @counter-style roman { system: additive; additive-symbols: 10 X, 9 IX, 5 V, 4 IV, 1 I; }
                }
                .root { display: flex; flex-wrap: wrap; gap: 16px; padding: 20px;
                    align-items: start; align-content: start; counter-reset: n total;
                    font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26; }
                .root::before { content: 'CSS 自定义编号'; flex: 0 0 100%; height: 36px; font-size: 24px; color: #146342; }
                .card { display: block; width: 280px; padding: 12px; background: #e9f4ef; border: 1px solid #abcdbd; counter-increment: total; }
                .card::before { height: 30px; font-size: 18px; }
                .pad { counter-set: n 7; } .pad::before { content: '项目 ' counter(n, padded) / '编号' counter(n); }
                .alpha { counter-set: n 27; } .alpha::before { content: '章节 ' counter(n, alpha); }
                .additive { counter-set: n 14; } .additive::before { content: '注释 ' counter(n, roman); }
                .anonymous { counter-set: n 5; } .anonymous::before { content: '二进制 ' counter(n, symbols(numeric '〇' '一')); }
                .root::after { content: counter(total) ' 种编号样式'; flex: 0 0 100%; height: 28px; }
                """);
            if (sheet.Diagnostics.Count > 0) throw new InvalidOperationException("CSS diagnostics: " + string.Join(", ", sheet.Diagnostics));
            Css.GetStyleSheets(root).Add(sheet);
            using var view = new NSView(new CGRect(0, 0, width, height));
            using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine);
            if (target.RenderingEngine != engine) throw new InvalidOperationException("Requested renderer unavailable: " + engine);
            using var drawing = new RenderTargetDrawingContext(target, context);
            foreach (var replaced in new[] { false, true })
            {
                if (replaced) Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@counter-style padded { system: extends decimal; pad: 4 '0'; }"));
                for (var i = 0; i < 4; i++)
                { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
                var expected = new[] { replaced ? "项目 0007" : "项目 007", "章节 AA", "注释 XIV", "二进制 一〇一" };
                for (var i = 0; i < rows.Count; i++)
                {
                    if (Generated(rows[i]).Text != expected[i]) throw new InvalidOperationException("Wrong native counter style: " + Generated(rows[i]).Text);
                    if (rows[i].VisualBounds.Right > width || rows[i].VisualBounds.Bottom > height) throw new InvalidOperationException("Counter style box overflow");
                }
                if (Jalium.UI.Automation.AutomationProperties.GetName(Generated(rows[0])) != "编号7") throw new InvalidOperationException("Alternative counter name stale");
                if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("GPU readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1);
                try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * height * 4];
                if (target.FetchReadback(pixels, (uint)(width * 4), out var actualWidth, out var actualHeight) != JaliumResult.Ok ||
                    actualWidth != width || actualHeight != height) throw new InvalidOperationException("GPU readback failed");
                var dark = Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4] < 130 && pixels[i * 4 + 1] < 130 && pixels[i * 4 + 2] < 130);
                if (dark < 100) throw new InvalidOperationException("Native glyphs missing");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_COUNTER_STYLE_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path);
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(path, $"counter-styles-{engine}-{width}-{(replaced ? "updated" : "initial")}.png")); encoder.Save(output);
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS custom CSS counter styles GPU checks: {passed}/8 passed");
        return passed == 8 ? 0 : 1;
    }

    private static TextBlock Generated(Panel host) => Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(host))
        .Select(i => VisualTreeHelper.GetChild(host, i)).OfType<TextBlock>().First(text => !host.Children.Contains(text));
}
