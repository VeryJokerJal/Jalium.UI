using System.IO;
using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Xunit;

namespace Jalium.UI.Tests;

// 放大诊断：把用户报告异常的图标光栅化到大尺寸输出 PNG。
// 设 JALIUM_SVG_ZOOM=<输出.png> 时执行。
public class SvgIconZoomDumpTests
{
    [Fact]
    public void Dump_problem_icons_zoomed()
    {
        var outPath = Environment.GetEnvironmentVariable("JALIUM_SVG_ZOOM");
        if (string.IsNullOrEmpty(outPath)) return;

        var icons = new (string Name, string Body)[]
        {
            ("archive", """
                <rect width="20" height="5" x="2" y="3" rx="1"/>
                <path d="M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8"/>
                <path d="M10 12h4"/>
                """),
            ("transform", """
                <g transform="translate(12,12) rotate(45) scale(0.8)">
                  <rect x="-7" y="-7" width="14" height="14" rx="2"/>
                </g>
                """),
            ("rect-rx-plain", """<rect x="4" y="4" width="16" height="16" rx="3"/>"""),
            ("rect-rx-rot", """
                <g transform="rotate(45 12 12)">
                  <rect x="5" y="5" width="14" height="14" rx="3"/>
                </g>
                """),
        };

        const int Cell = 160;
        const int Pad = 12;
        int width = icons.Length * (Cell + Pad) + Pad;
        int height = Cell + Pad * 2;

        var canvas = new byte[width * height * 4];
        for (int i = 0; i < canvas.Length; i += 4)
        {
            canvas[i] = 0x2e; canvas[i + 1] = 0x1e; canvas[i + 2] = 0x1e; canvas[i + 3] = 255;
        }

        for (int k = 0; k < icons.Length; k++)
        {
            string svg = $"""
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"
                     fill="none" stroke="#e6e6f0" stroke-width="2"
                     stroke-linecap="round" stroke-linejoin="round">{icons[k].Body}</svg>
                """;

            var image = SvgImage.FromSvgString(svg);
            Assert.NotNull(image?.Drawing);

            var pixels = SoftwareVectorRasterizer.Rasterize(
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
