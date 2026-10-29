using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Xunit;

namespace Jalium.UI.Tests;

// SVG 解析一致性：对照 SVG 1.1/2 规范锁死 2026-08-28 批量修复的解析缺陷。
// 每个测试对应一个修复前必红的缺陷。
public class SvgParserConformanceTests
{
    private static (byte B, byte G, byte R, byte A) Px(byte[] px, int width, int x, int y)
    {
        int off = (y * width + x) * 4;
        return (px[off], px[off + 1], px[off + 2], px[off + 3]);
    }

    private static byte[] Raster(SvgImage image, int w, int h)
    {
        var pixels = SoftwareVectorRasterizer.Rasterize(image.Drawing!, w, h, new Rect(0, 0, w, h));
        Assert.NotNull(pixels);
        return pixels!;
    }

    private static GeometryDrawing FirstGeometryDrawing(Drawing drawing)
    {
        while (true)
        {
            switch (drawing)
            {
                case GeometryDrawing gd:
                    return gd;
                case DrawingGroup dg:
                    Assert.True(dg.Children.Count > 0, "empty DrawingGroup");
                    drawing = dg.Children[0];
                    continue;
                default:
                    Assert.Fail($"unexpected drawing type {drawing.GetType().Name}");
                    return null!;
            }
        }
    }

    [Fact]
    public void HiddenSvgGroupKeepsAnExplicitlyVisibleChild()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'>" +
                  "<g visibility='hidden'><rect width='20' height='20' fill='red'/>" +
                  "<rect x='20' width='20' height='20' fill='blue' " +
                  "visibility='visible'/></g></svg>";
        var pixels = Raster(SvgImage.FromSvgString(svg), 40, 20);
        Assert.Equal((byte)0, Px(pixels, 40, 10, 10).A);
        Assert.True(Px(pixels, 40, 30, 10).A >= 250);
        Assert.True(Px(pixels, 40, 30, 10).B >= 250);
    }

    [Fact]
    public void LocalClipPathClipsShapesGroupsAndUseInTheirUserSpace()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="70" height="24">
              <defs>
                <clipPath id="round"><circle cx="10" cy="10" r="8"/></clipPath>
                <rect id="tile" width="20" height="20" fill="white"/>
              </defs>
              <rect width="20" height="20" fill="white" clip-path="url(#round)"/>
              <g transform="translate(25 0)" clip-path="url(#round)">
                <rect width="20" height="20" fill="white"/>
              </g>
              <use href="#tile" x="50" clip-path="url(#round)"/>
            </svg>
            """;
        var pixels = Raster(SvgImage.FromSvgString(svg), 70, 24);
        foreach (var x in new[] { 0, 25, 50 })
        {
            Assert.True(Px(pixels, 70, x + 10, 10).A >= 250);
            Assert.Equal((byte)0, Px(pixels, 70, x + 2, 2).A);
        }
    }

    [Fact]
    public void LocalClipPathObjectBoundingBoxUsesTargetOriginAndSize()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="50" height="30">
              <defs><clipPath id="left" clipPathUnits="objectBoundingBox">
                <rect x="0" y="0" width="0.5" height="1"/>
              </clipPath></defs>
              <rect x="10" y="5" width="30" height="20" fill="white" clip-path="url(#left)"/>
            </svg>
            """;
        var pixels = Raster(SvgImage.FromSvgString(svg), 50, 30);
        Assert.True(Px(pixels, 50, 15, 15).A >= 250);
        Assert.Equal((byte)0, Px(pixels, 50, 35, 15).A);
    }

    [Fact]
    public void LocalClipPathUnitesOppositeWindingChildrenAndPreservesHoles()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="80" height="40">
              <defs>
                <clipPath id="overlap">
                  <path d="M0 0 H30 V30 H0 Z"/>
                  <path d="M40 0 V30 H10 V0 Z"/>
                </clipPath>
                <clipPath id="hole">
                  <path clip-rule="evenodd" d="M0 0 H40 V40 H0 Z M10 10 H30 V30 H10 Z"/>
                  <rect x="15" y="15" width="10" height="10"/>
                </clipPath>
              </defs>
              <rect width="40" height="40" fill="white" clip-path="url(#overlap)"/>
              <g transform="translate(40 0)" clip-path="url(#hole)">
                <rect width="40" height="40" fill="white"/>
              </g>
            </svg>
            """;
        var pixels = Raster(SvgImage.FromSvgString(svg), 80, 40);
        Assert.True(Px(pixels, 80, 20, 15).A >= 250);
        Assert.True(Px(pixels, 80, 45, 20).A >= 250);
        Assert.Equal((byte)0, Px(pixels, 80, 52, 20).A);
        Assert.True(Px(pixels, 80, 60, 20).A >= 250);
    }

    [Fact]
    public void LocalNestedClipPathsIntersectAndCyclesAreIgnored()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="90" height="25">
              <defs>
                <clipPath id="mask"><rect x="10" width="10" height="25"/></clipPath>
                <clipPath id="parent" clip-path="url(#mask)">
                  <rect width="30" height="25"/>
                </clipPath>
                <clipPath id="child">
                  <rect width="30" height="25" clip-path="url(#mask)"/>
                </clipPath>
                <clipPath id="cycle" clip-path="url(#cycle)">
                  <rect width="30" height="25"/>
                </clipPath>
              </defs>
              <rect width="30" height="25" fill="white" clip-path="url(#parent)"/>
              <g transform="translate(30 0)">
                <rect width="30" height="25" fill="white" clip-path="url(#child)"/>
              </g>
              <g transform="translate(60 0)">
                <rect width="30" height="25" fill="white" clip-path="url(#cycle)"/>
              </g>
            </svg>
            """;
        var pixels = Raster(SvgImage.FromSvgString(svg), 90, 25);
        Assert.Equal((byte)0, Px(pixels, 90, 5, 12).A);
        Assert.True(Px(pixels, 90, 15, 12).A >= 250);
        Assert.Equal((byte)0, Px(pixels, 90, 25, 12).A);
        Assert.Equal((byte)0, Px(pixels, 90, 35, 12).A);
        Assert.True(Px(pixels, 90, 45, 12).A >= 250);
        Assert.Equal((byte)0, Px(pixels, 90, 55, 12).A);
        Assert.True(Px(pixels, 90, 65, 12).A >= 250);
    }

    [Fact]
    public void LocalEmptyClipHidesPaintWhileInvalidTargetsLeaveItVisible()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="60">
              <defs>
                <clipPath id="empty">
                  <line x1="0" y1="0" x2="20" y2="20" stroke="white"/>
                  <animate attributeName="opacity" from="1" to="1" dur="1s"/>
                </clipPath>
                <rect id="notClip" width="20" height="20"/>
              </defs>
              <rect width="20" height="20" fill="white" clip-path="url(#empty)"/>
              <rect y="20" width="20" height="20" fill="white" clip-path="url(#notClip)"/>
              <rect y="40" width="20" height="20" fill="white" clip-path="url(other.svg#empty)"/>
            </svg>
            """;
        var pixels = Raster(SvgImage.FromSvgString(svg), 20, 60);
        Assert.Equal((byte)0, Px(pixels, 20, 10, 10).A);
        Assert.True(Px(pixels, 20, 10, 30).A >= 250);
        Assert.True(Px(pixels, 20, 10, 50).A >= 250);
    }

    // ── ① SVG transform 列表：右侧变换先作用（translate(10) scale(2) ≠ scale 后再平移 20）──
    [Fact]
    public void Transform_list_composes_right_to_left()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32">
              <g transform="translate(10,0) scale(2)">
                <rect x="0" y="0" width="5" height="16" fill="#ffffff"/>
              </g>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
        var pixels = Raster(image!, 32, 32);

        // 正确：rect 先 scale→[0,10]，再 translate→[10,20]。中点 (15,8) 白。
        var inside = Px(pixels, 32, 15, 8);
        Assert.True(inside.A >= 250 && inside.R >= 250,
            $"(15,8) = A{inside.A} R{inside.R}, expected white (rect at x∈[10,20])");

        // 反序组合会落 [20,30]：(25,8) 必须是空
        var outside = Px(pixels, 32, 25, 8);
        Assert.True(outside.A == 0,
            $"(25,8) = A{outside.A}, expected empty (reversed composition puts rect at x∈[20,30])");
    }

    // ── ② 渐变坐标百分比（objectBoundingBox 下 % = 分数）──
    [Fact]
    public void Gradient_percentage_coordinates_parse_as_fractions()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <defs>
                <linearGradient id="g" x1="0%" y1="0%" x2="0%" y2="100%">
                  <stop offset="0%" stop-color="#ff0000"/>
                  <stop offset="100%" stop-color="#0000ff"/>
                </linearGradient>
              </defs>
              <rect width="24" height="24" fill="url(#g)"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);

        var gd = FirstGeometryDrawing(image!.Drawing!);
        var brush = Assert.IsType<LinearGradientBrush>(gd.Brush);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(0, 1), brush.EndPoint);   // 修复前 y2="100%" 解析失败回默认 (1,0)
        Assert.Equal(2, brush.GradientStops.Count);
        Assert.Equal(1.0, brush.GradientStops[1].Offset, 3);
    }

    // ── ③ 形状坐标百分比（相对 viewport）──
    [Fact]
    public void Rect_percentage_size_resolves_against_viewport()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <rect x="0" y="0" width="100%" height="50%" fill="#ffffff"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
        var pixels = Raster(image!, 24, 24);

        var top = Px(pixels, 24, 12, 6);
        Assert.True(top.A >= 250 && top.R >= 250,
            $"(12,6) = A{top.A} R{top.R}, expected white (rect covers top half)");

        var bottom = Px(pixels, 24, 12, 18);
        Assert.True(bottom.A == 0,
            $"(12,18) = A{bottom.A}, expected empty (height=50%)");
    }

    // ── ④ use 循环引用不许栈溢出 ──
    [Fact]
    public void Use_reference_cycle_does_not_overflow()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <g id="a"><use href="#b"/><rect width="4" height="4" fill="#fff"/></g>
              <g id="b"><use href="#a"/></g>
              <use href="#a"/>
            </svg>
            """;
        // 修复前：ParseUse → ParseElement → ParseUse … 无限递归 StackOverflow(进程级崩溃)
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
    }

    // ── ⑤ 无 fill 声明 + 有 stroke：填充仍默认黑（SVG fill 初始值与 stroke 无关）──
    [Fact]
    public void Missing_fill_defaults_to_black_even_with_stroke()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <rect x="4" y="4" width="16" height="16" stroke="#ff0000" stroke-width="2"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);

        var gd = FirstGeometryDrawing(image!.Drawing!);
        Assert.NotNull(gd.Pen);
        var fill = Assert.IsType<SolidColorBrush>(gd.Brush);
        Assert.Equal(Color.FromRgb(0, 0, 0), fill.Color);

        // 像素级：矩形中心是黑色不透明（不是空）
        var pixels = Raster(image!, 24, 24);
        var center = Px(pixels, 24, 12, 12);
        Assert.True(center.A >= 250 && center.R <= 5,
            $"(12,12) = A{center.A} R{center.R}, expected opaque black fill");
    }

    // ── ⑥ viewBox 带非零 min-x/min-y、无显式 width/height：内容须平移进视口 ──
    [Fact]
    public void ViewBox_min_offset_translates_without_explicit_size()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="100 200 24 24">
              <rect x="100" y="200" width="24" height="24" fill="#ffffff"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
        var pixels = Raster(image!, 24, 24);

        var center = Px(pixels, 24, 12, 12);
        Assert.True(center.A >= 250 && center.R >= 250,
            $"(12,12) = A{center.A} R{center.R}, expected white (content translated by -minX/-minY)");
    }

    // ── ⑦ 开放 polyline 的 fill 语义：按隐式闭合填充 ──
    [Fact]
    public void Polyline_fills_as_implicitly_closed()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <polyline points="2,22 12,2 22,22" fill="#ffffff" stroke="none"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
        var pixels = Raster(image!, 24, 24);

        // 三角形内部 (12,16) 应被填充
        var inside = Px(pixels, 24, 12, 16);
        Assert.True(inside.A >= 250 && inside.R >= 250,
            $"(12,16) = A{inside.A} R{inside.R}, expected white (polyline fill treats shape as closed)");
    }

    // ── 渐变须逐像素渲染（不是平均色平涂）──
    [Fact]
    public void Gradient_fill_renders_per_pixel_not_flat_average()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <defs>
                <linearGradient id="g" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0" stop-color="#ff0000"/>
                  <stop offset="1" stop-color="#0000ff"/>
                </linearGradient>
              </defs>
              <rect width="24" height="24" fill="url(#g)"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
        var pixels = Raster(image!, 24, 24);

        var top = Px(pixels, 24, 12, 1);
        var bottom = Px(pixels, 24, 12, 22);

        // 顶部红占优、底部蓝占优；平均色平涂时两端相同（紫）
        Assert.True(top.R > 200 && top.B < 60,
            $"top (12,1) = R{top.R} B{top.B}, expected red-dominant");
        Assert.True(bottom.B > 200 && bottom.R < 60,
            $"bottom (12,22) = R{bottom.R} B{bottom.B}, expected blue-dominant");
    }

    // ── stroke-dasharray 须渲染成虚线（不是实线）──
    [Fact]
    public void Dashed_stroke_renders_gaps()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="48" height="16">
              <path d="M2 8H46" stroke="#ffffff" stroke-width="2" stroke-dasharray="6 6" fill="none"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
        var pixels = SoftwareVectorRasterizer.Rasterize(image!.Drawing!, 48, 16, new Rect(0, 0, 48, 16));
        Assert.NotNull(pixels);

        int on = 0, off = 0;
        for (int x = 2; x < 46; x++)
        {
            var a = pixels![(8 * 48 + x) * 4 + 3];
            if (a >= 200) on++;
            else if (a == 0) off++;
        }
        Assert.True(on >= 12, $"opaque run pixels = {on}, expected >= 12");
        Assert.True(off >= 12, $"gap pixels = {off}, expected >= 12 (solid line means dashes ignored)");
    }

    // ── visibility:hidden 须隐藏且可继承 ──
    [Fact]
    public void Visibility_hidden_inherits_and_hides()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <g visibility="hidden">
                <rect width="24" height="24" fill="#ffffff"/>
              </g>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
        var pixels = SoftwareVectorRasterizer.Rasterize(image!.Drawing!, 24, 24, new Rect(0, 0, 24, 24));
        // 整树被隐藏时 Drawing 可能为空组；光栅化结果必须全透明
        if (pixels != null)
        {
            for (int i = 3; i < pixels.Length; i += 4)
                Assert.True(pixels[i] == 0, $"pixel alpha {pixels[i]} at byte {i}, expected fully hidden");
        }
    }

    // ── 组 opacity 不许因属性继承被双乘 ──
    [Fact]
    public void Group_opacity_applies_once_not_squared()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <g opacity="0.5">
                <rect width="24" height="24" fill="#ffffff"/>
              </g>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
        var pixels = SoftwareVectorRasterizer.Rasterize(image!.Drawing!, 24, 24, new Rect(0, 0, 24, 24));
        Assert.NotNull(pixels);

        var a = pixels![(12 * 24 + 12) * 4 + 3];
        Assert.True(a is >= 120 and <= 136,
            $"center alpha = {a}, expected ≈128 (0.5 applied once; 64 means the opacity was inherited and squared)");
    }

    // ── 圆角矩形描边：四角必须是外凸圆弧，不许翘刺/缺口/错位 ──
    [Fact]
    public void Rounded_rect_stroke_corners_are_convex_and_continuous()
    {
        // 5x 放大光栅化让角部几何清晰可判
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24"
                 fill="none" stroke="#ffffff" stroke-width="2" stroke-linejoin="round">
              <rect x="4" y="4" width="16" height="16" rx="3"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        Assert.NotNull(image?.Drawing);
        var pixels = SoftwareVectorRasterizer.Rasterize(image!.Drawing!, 120, 120, new Rect(0, 0, 24, 24));
        Assert.NotNull(pixels);

        byte AlphaAt(double ux, double uy)
        {
            int x = (int)(ux * 5), y = (int)(uy * 5);
            return pixels![(y * 120 + x) * 4 + 3];
        }

        // 1) 四个外角点（rect 角 ± 描边半宽之外）必须是空——凹弧/翘刺会覆盖到这里。
        //    外角 (20,4)：取 (20.4, 3.6) 这类越过圆角外切线的点。
        Assert.True(AlphaAt(20.4, 3.6) == 0, $"top-right outer corner covered: A={AlphaAt(20.4, 3.6)}");
        Assert.True(AlphaAt(20.4, 20.4) == 0, $"bottom-right outer corner covered: A={AlphaAt(20.4, 20.4)}");
        Assert.True(AlphaAt(3.6, 20.4) == 0, $"bottom-left outer corner covered: A={AlphaAt(3.6, 20.4)}");
        Assert.True(AlphaAt(3.6, 3.6) == 0, $"top-left outer corner covered: A={AlphaAt(3.6, 3.6)}");

        // 2) 每个角的 45° 方向、圆弧中线处必须是满描边——错位断裂会让这里空。
        //    TR 圆心 (17,7)，半径 3，45° 弧中点 = (17+2.12, 7-2.12)=(19.12,4.88)。
        Assert.True(AlphaAt(19.12, 4.88) >= 250, $"top-right arc mid missing: A={AlphaAt(19.12, 4.88)}");
        Assert.True(AlphaAt(19.12, 19.12) >= 250, $"bottom-right arc mid missing: A={AlphaAt(19.12, 19.12)}");
        Assert.True(AlphaAt(4.88, 19.12) >= 250, $"bottom-left arc mid missing: A={AlphaAt(4.88, 19.12)}");
        Assert.True(AlphaAt(4.88, 4.88) >= 250, $"top-left arc mid missing: A={AlphaAt(4.88, 4.88)}");
    }

    // ── Z 后跟坐标（非法数据）须报错而不是死循环挂死线程 ──
    [Fact]
    public void Path_data_with_coordinates_after_Z_fails_fast()
    {
        var done = System.Threading.Tasks.Task.Run(() =>
            Assert.Throws<FormatException>(() => Geometry.Parse("M0 0 L5 5 Z 3 3")));
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)),
            "parser hung — implicit repeat of Z consumed nothing and spun forever");
    }

    // ── .svgz（gzip 压缩 SVG）须能加载 ──
    [Fact]
    public void Gzip_compressed_svgz_bytes_load()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <rect width="24" height="24" fill="#ffffff"/>
            </svg>
            """;
        byte[] gz;
        using (var ms = new System.IO.MemoryStream())
        {
            using (var z = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
            {
                var raw = System.Text.Encoding.UTF8.GetBytes(svg);
                z.Write(raw, 0, raw.Length);
            }
            gz = ms.ToArray();
        }

        var image = SvgImage.FromBytes(gz);
        Assert.NotNull(image?.Drawing);
        var pixels = Raster(image!, 24, 24);
        var center = Px(pixels, 24, 12, 12);
        Assert.True(center.A >= 250 && center.R >= 250,
            $"(12,12) = A{center.A} R{center.R}, expected white (gzip payload inflated)");
    }

    // ── stop offset 越界须 clamp、乱序须单调提升 ──
    [Fact]
    public void Gradient_stop_offsets_clamped_and_monotonic()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
              <defs>
                <linearGradient id="g">
                  <stop offset="-0.5" stop-color="#ff0000"/>
                  <stop offset="0.8" stop-color="#00ff00"/>
                  <stop offset="0.3" stop-color="#0000ff"/>
                  <stop offset="150%" stop-color="#ffffff"/>
                </linearGradient>
              </defs>
              <rect width="24" height="24" fill="url(#g)"/>
            </svg>
            """;
        var image = SvgImage.FromSvgString(svg);
        var gd = FirstGeometryDrawing(image!.Drawing!);
        var brush = Assert.IsType<LinearGradientBrush>(gd.Brush);

        Assert.Equal(4, brush.GradientStops.Count);
        Assert.Equal(0.0, brush.GradientStops[0].Offset, 3);   // clamp 到 0
        Assert.Equal(0.8, brush.GradientStops[1].Offset, 3);
        Assert.Equal(0.8, brush.GradientStops[2].Offset, 3);   // 乱序提升到前值
        Assert.Equal(1.0, brush.GradientStops[3].Offset, 3);   // clamp 到 1
    }
}
