using System.IO;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

/// <summary>
/// 把 Markdown 渲染到位图上做像素判定：可视树里属性对了，不代表真的画到了屏幕上。
/// </summary>
/// <remarks>
/// <para><b>这里只能验证非文字的绘制。</b><see cref="RenderTargetBitmap"/> 的软件光栅在无 GUI 环境下
/// 不出字形——一个纯 <see cref="TextBlock"/> 也只得到空白位图。所以凡是「文字画在哪、字有多宽」
/// 的判定都必须走可视树属性（见 <c>MarkdownRenderingFeatureTests</c>），别在这里写。</para>
/// <para>删除线是个例外：它是一条 <c>DrawLine</c>，与字形渲染无关，因此可以、也值得在像素上验证——
/// 装饰位是否真的接到了绘制上，只有画出来才能确认。</para>
/// <para>设环境变量 <c>JALIUM_MARKDOWN_DUMP=&lt;目录&gt;</c> 会同时落 PNG 供肉眼复核。</para>
/// </remarks>
[Collection("Application")]
public class MarkdownVisualDumpTests
{
    private const int Width = 460;
    private const int Height = 160;
    private const int Bpp = 4;

    private static void ResetApplicationState()
    {
        typeof(Application)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);

        typeof(ThemeManager)
            .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private static byte[] Render(string markdown, string dumpName)
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var host = new StackPanel { Width = Width, Height = Height };
            host.Children.Add(new Markdown { Text = markdown, Width = Width, Height = Height });
            host.Measure(new Size(Width, Height));
            host.Arrange(new Rect(0, 0, Width, Height));

            var target = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormat.Bgra32);
            target.Clear(Color.FromArgb(255, 255, 255, 255));
            target.Render(host);

            var pixels = new byte[Width * Bpp * Height];
            target.CopyPixels(new Int32Rect(0, 0, Width, Height), pixels, Width * Bpp, 0);

            if (Environment.GetEnvironmentVariable("JALIUM_MARKDOWN_DUMP") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapImage.FromPixels(pixels, Width, Height)));
                using var stream = File.Create(Path.Combine(dir, dumpName + ".png"));
                encoder.Save(stream);
            }

            return pixels;
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// 主题背景是深色，所以「非白即墨」是行不通的判据；改成拿控件正中偏下一块空处采背景色，
    /// 再数与它明显不同的像素。
    /// </summary>
    private static byte SampleBackgroundLuma(byte[] pixels) => Luma(pixels, Width / 2, Height - 12);

    private static byte Luma(byte[] pixels, int x, int y)
    {
        var offset = (y * Width * Bpp) + (x * Bpp);
        return (byte)((pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3);
    }

    /// <summary>控件有圆角，边缘之外还是 Clear 留下的白底；只在稳稳落在控件内部的这块里判定。</summary>
    private const int InsetX = 24;
    private const int InsetTop = 14;
    private const int InsetBottom = 20;

    /// <summary>该行上与背景亮度差超过阈值的像素数，以及它们的水平跨度。</summary>
    private static (int Count, int Span) ScanRow(byte[] pixels, int y, byte background)
    {
        var count = 0;
        var left = int.MaxValue;
        var right = -1;

        for (var x = InsetX; x < Width - InsetX; x++)
        {
            if (Math.Abs(Luma(pixels, x, y) - background) < 24)
            {
                continue;
            }

            count++;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
        }

        return (count, right < 0 ? 0 : right - left + 1);
    }

    [Fact]
    public void Strikethrough_ActuallyPaintsALineAcrossTheText()
    {
        // 同一段文字，一份带 ~~ 一份不带：带的那份必须多出一条横贯的实线。
        var struck = Render("~~aaaaaaaaaaaaaaaa~~", "strike-on");
        var plain = Render("aaaaaaaaaaaaaaaa", "strike-off");
        var background = SampleBackgroundLuma(struck);

        var struckLine = -1;
        var struckSpan = 0;
        for (var y = InsetTop; y < Height - InsetBottom; y++)
        {
            var (count, span) = ScanRow(struck, y, background);
            if (count > struckSpan)
            {
                struckSpan = count;
                struckLine = y;
            }
        }

        Assert.True(struckSpan > 40, $"没找到删除线：最长的一行只有 {struckSpan} 个像素（y={struckLine}）。");

        // 同一行在没有 ~~ 的那份里应该几乎是空的——否则这条“线”其实是别的东西画的。
        var (plainCount, _) = ScanRow(plain, struckLine, SampleBackgroundLuma(plain));
        Assert.True(
            plainCount * 4 < struckSpan,
            $"对照组在同一行也有 {plainCount} 个像素，这条线并非删除线带来的。");
    }

    [Fact]
    public void PlainText_LeavesNoStrikethroughLine()
    {
        // 反向守卫：删除线不该在普通文本上冒出来。
        var pixels = Render("aaaaaaaaaaaaaaaa", "plain-only");
        var background = SampleBackgroundLuma(pixels);

        for (var y = InsetTop; y < Height - InsetBottom; y++)
        {
            var (count, _) = ScanRow(pixels, y, background);
            Assert.True(count <= 10, $"第 {y} 行有 {count} 个非背景像素，普通文本不该画出这种横线。");
        }
    }
}
