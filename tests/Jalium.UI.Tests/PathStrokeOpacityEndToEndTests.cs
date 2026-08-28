using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Xunit;
using Path = Jalium.UI.Shapes.Path;

namespace Jalium.UI.Tests;

// 端到端复现"图标线条呈半透明/透明"：真实 Path 控件（lucide 描边图标形态）
// 经 Measure/Arrange 后走 RenderTargetBitmap 软件光栅，逐像素断言线条主体满 alpha。
public class PathStrokeOpacityEndToEndTests
{
    private const int Bpp = 4;

    private static byte[] RenderToPixels(Visual visual, int width, int height)
    {
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
        target.Clear(Color.FromArgb(255, 0, 0, 0));
        target.Render(visual);

        var stride = width * Bpp;
        var pixels = new byte[stride * height];
        target.CopyPixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return pixels;
    }

    private static (byte B, byte G, byte R) Px(byte[] pixels, int width, int x, int y)
    {
        var i = (y * width + x) * Bpp;
        return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }

    private static Path MakeIcon(string data, double thickness = 2)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Stroke = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
        };
        path.Measure(new Size(24, 24));
        path.Arrange(new Rect(0, 0, 24, 24));
        return path;
    }

    [Fact]
    public void Lucide_file_icon_stroke_body_is_opaque()
    {
        // lucide "file"：主体带圆角的闭合轮廓 + 折角开放折线
        var icon = MakeIcon(
            "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z");

        var pixels = RenderToPixels(icon, 24, 24);

        // 左墙 x=4（线宽 [3,5)），中段像素应满白
        var (b1, g1, r1) = Px(pixels, 24, 4, 12);
        Assert.True(r1 >= 250 && g1 >= 250 && b1 >= 250,
            $"left wall (4,12) = R{r1} G{g1} B{b1}, expected opaque white");

        // 底边 y=22 中段
        var (b2, g2, r2) = Px(pixels, 24, 12, 22);
        Assert.True(r2 >= 250 && g2 >= 250 && b2 >= 250,
            $"bottom edge (12,22) = R{r2} G{g2} B{b2}, expected opaque white");

        // 顶边 y=2 中段
        var (b3, g3, r3) = Px(pixels, 24, 10, 2);
        Assert.True(r3 >= 250 && g3 >= 250 && b3 >= 250,
            $"top edge (10,2) = R{r3} G{g3} B{b3}, expected opaque white");
    }

    [Fact]
    public void Lucide_x_icon_lines_and_crossing_are_opaque()
    {
        // lucide "x"：两条对角线（开放 figure，round cap）
        var icon = MakeIcon("M18 6 6 18M6 6l12 12");

        var pixels = RenderToPixels(icon, 24, 24);

        var (b1, g1, r1) = Px(pixels, 24, 12, 12);
        Assert.True(r1 >= 250 && g1 >= 250 && b1 >= 250,
            $"crossing (12,12) = R{r1} G{g1} B{b1}, expected opaque white");

        var (b2, g2, r2) = Px(pixels, 24, 9, 9);
        Assert.True(r2 >= 250 && g2 >= 250 && b2 >= 250,
            $"line body (9,9) = R{r2} G{g2} B{b2}, expected opaque white");
    }

    [Fact]
    public void Straight_line_only_figure_is_opaque()
    {
        // 纯直线 figure 走 DrawPathFigurePolygon → DrawPolygon 管线
        var icon = MakeIcon("M5 12H19");

        var pixels = RenderToPixels(icon, 24, 24);

        var (b, g, r) = Px(pixels, 24, 12, 12);
        Assert.True(r >= 250 && g >= 250 && b >= 250,
            $"line center (12,12) = R{r} G{g} B{b}, expected opaque white");
    }

    [Fact]
    public void Open_polyline_does_not_grow_phantom_closing_edge()
    {
        // lucide "chevron-down"：开放三点折线。终点(18,9)与起点(6,9)之间
        // 不许出现闭合横线——fill 语义的强制闭合絶不能泄漏进 stroke。
        var icon = MakeIcon("M6 9l6 6 6-6");

        var pixels = RenderToPixels(icon, 24, 24);

        // 折线主体应存在
        var (b1, g1, r1) = Px(pixels, 24, 9, 12);
        Assert.True(r1 >= 250 && g1 >= 250 && b1 >= 250,
            $"chevron body (9,12) = R{r1} G{g1} B{b1}, expected opaque white");

        // (12,9) 位于假想闭合边的正中——必须是背景（黑）
        var (b2, g2, r2) = Px(pixels, 24, 12, 9);
        Assert.True(r2 <= 5 && g2 <= 5 && b2 <= 5,
            $"phantom closing edge midpoint (12,9) = R{r2} G{g2} B{b2}, expected background");
    }

    [Fact]
    public void Svg_image_pipeline_renders_straight_line_stroke()
    {
        // <Image Source="*.svg"> 链路：SvgParser → SoftwareVectorRasterizer。
        // 两点直线的描边同样必须落到像素。
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"
                 fill="none" stroke="#ffffff" stroke-width="2"
                 stroke-linecap="round" stroke-linejoin="round">
              <path d="M5 12h14"/>
              <path d="M6 6l12 12"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(image!.Drawing!, 24, 24, new Rect(0, 0, 24, 24));
        Assert.NotNull(pixels);

        (byte B, byte G, byte R, byte A) At(int x, int y)
        {
            var i = (y * 24 + x) * 4;
            return (pixels![i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
        }

        var h = At(12, 12);
        Assert.True(h.A >= 250 && h.R >= 250,
            $"horizontal line center (12,12) = A{h.A} R{h.R}, expected opaque white");

        var d = At(9, 9);
        Assert.True(d.A >= 250 && d.R >= 250,
            $"diagonal line body (9,9) = A{d.A} R{d.R}, expected opaque white");
    }
}
