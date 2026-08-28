using System.IO;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Xunit;

namespace Jalium.UI.Tests;

// 视觉核验（非回归断言）：把 lucide 根属性形态的 SVG 图标批量光栅化拼图输出 PNG，
// 供人工确认"根属性继承 + 直线描边"修复后的实际观感。
// 设 JALIUM_SVG_DUMP=<输出.png> 时才执行。
public class SvgIconVisualDumpTests
{
    [Fact]
    public void Dump_lucide_icon_strip()
    {
        var outPath = Environment.GetEnvironmentVariable("JALIUM_SVG_DUMP");
        if (string.IsNullOrEmpty(outPath)) return;

        var icons = new (string Name, string Body)[]
        {
            ("x", "<path d=\"M18 6 6 18\"/><path d=\"m6 6 12 12\"/>"),
            ("menu", "<line x1=\"4\" x2=\"20\" y1=\"6\" y2=\"6\"/><line x1=\"4\" x2=\"20\" y1=\"12\" y2=\"12\"/><line x1=\"4\" x2=\"20\" y1=\"18\" y2=\"18\"/>"),
            ("file", "<path d=\"M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z\"/><path d=\"M14 2v4a2 2 0 0 0 2 2h4\"/>"),
            ("chevron-down", "<path d=\"m6 9 6 6 6-6\"/>"),
            ("archive", "<rect width=\"20\" height=\"5\" x=\"2\" y=\"3\" rx=\"1\"/><path d=\"M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8\"/><path d=\"M10 12h4\"/>"),
            ("search", "<circle cx=\"11\" cy=\"11\" r=\"8\"/><path d=\"m21 21-4.3-4.3\"/>"),
            ("currentColor", "<path d=\"M5 12h14\"/><path d=\"M12 5v14\"/>"),
            ("gradient", """
                <defs><linearGradient id="lg" x1="0%" y1="0%" x2="100%" y2="100%">
                  <stop offset="0%" stop-color="#ff5f6d"/><stop offset="100%" stop-color="#38b6ff"/>
                </linearGradient></defs>
                <rect x="3" y="3" width="18" height="18" rx="4" fill="url(#lg)" stroke="none"/>
                """),
            ("radial", """
                <defs><radialGradient id="rg" cx="35%" cy="35%" r="75%">
                  <stop offset="0%" stop-color="#ffffff"/><stop offset="100%" stop-color="#7048e8"/>
                </radialGradient></defs>
                <circle cx="12" cy="12" r="9" fill="url(#rg)" stroke="none"/>
                """),
            ("dashed", "<circle cx=\"12\" cy=\"12\" r=\"9\" stroke-dasharray=\"3 3\"/>"),
            ("transform", """
                <g transform="translate(12,12) rotate(45) scale(0.8)">
                  <rect x="-7" y="-7" width="14" height="14" rx="2"/>
                </g>
                """),
        };

        const int Cell = 48;
        const int Pad = 8;
        int width = icons.Length * (Cell + Pad) + Pad;
        int height = Cell + Pad * 2;

        var canvas = new byte[width * height * 4];
        for (int i = 0; i < canvas.Length; i += 4)
        {
            canvas[i] = 0x2e; canvas[i + 1] = 0x1e; canvas[i + 2] = 0x1e; canvas[i + 3] = 255;
        }

        for (int k = 0; k < icons.Length; k++)
        {
            // 最后一格用 currentColor + color 属性验证 currentColor 解析
            string svg = icons[k].Name == "currentColor"
                ? $"""
                   <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"
                        fill="none" stroke="currentColor" color="#4ec9b0" stroke-width="2"
                        stroke-linecap="round" stroke-linejoin="round">{icons[k].Body}</svg>
                   """
                : $"""
                   <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"
                        fill="none" stroke="#e6e6f0" stroke-width="2"
                        stroke-linecap="round" stroke-linejoin="round">{icons[k].Body}</svg>
                   """;

            var image = SvgImage.FromSvgString(svg);
            Assert.NotNull(image?.Drawing);

            var pixels = Interop.SoftwareVectorRasterizer.Rasterize(
                image!.Drawing!, Cell, Cell, new Rect(0, 0, 24, 24));
            Assert.NotNull(pixels);

            int x0 = Pad + k * (Cell + Pad);
            for (int y = 0; y < Cell; y++)
            {
                for (int x = 0; x < Cell; x++)
                {
                    int src = (y * Cell + x) * 4;
                    byte a = pixels![src + 3];
                    if (a == 0) continue;
                    int dst = ((y + Pad) * width + (x0 + x)) * 4;
                    float sa = a / 255f;
                    for (int c = 0; c < 3; c++)
                    {
                        canvas[dst + c] = (byte)(pixels[src + c] * sa + canvas[dst + c] * (1 - sa));
                    }
                    canvas[dst + 3] = 255;
                }
            }
        }

        var bmp = BitmapImage.FromPixels(canvas, width, height);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(outPath);
        encoder.Save(fs);
    }
}
