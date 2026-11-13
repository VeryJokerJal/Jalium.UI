using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

/// <summary>
/// ColorPicker 在拖动期间每次指针移动都整块重绘，因此把每帧新建的画刷/画笔改成复用实例，
/// 并把透明度棋盘底从「逐格绘制」改成「铺底 + 只画深格」。两处都改的是绘制路径，
/// 必须逐像素证明画面没变。
/// </summary>
public class ColorPickerRenderReuseTests
{
    private const int Bpp = 4;

    private static DrawingVisual CreateVisual(Action<DrawingContext, Rect> draw, Rect rect)
    {
        var visual = new DrawingVisual();
        var dc = visual.RenderOpen();
        try
        {
            draw(dc, rect);
        }
        finally
        {
            dc.Close();
        }

        return visual;
    }

    private static byte[] RenderToPixels(Visual visual, int width, int height, out int stride)
    {
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
        target.Clear(Color.FromArgb(255, 0, 0, 0));
        target.Render(visual);

        stride = width * Bpp;
        var pixels = new byte[stride * height];
        target.CopyPixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return pixels;
    }

    /// <summary>改动前的逐格算法，作为参考实现。</summary>
    private static void DrawCheckerboardReference(DrawingContext dc, Rect rect)
    {
        var lightBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
        var darkBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        const int cellSize = 4;

        for (double x = rect.X; x < rect.Right; x += cellSize)
        {
            for (double y = rect.Y; y < rect.Bottom; y += cellSize)
            {
                var isLight = ((int)((x - rect.X) / cellSize) + (int)((y - rect.Y) / cellSize)) % 2 == 0;
                var cellRect = new Rect(x, y, Math.Min(cellSize, rect.Right - x), Math.Min(cellSize, rect.Bottom - y));
                dc.DrawRectangle(isLight ? lightBrush : darkBrush, null, cellRect);
            }
        }
    }

    [Theory]
    // 透明度条与预览块的实际尺寸，外加两个非 4 倍数的尺寸以覆盖边缘格子的截断。
    [InlineData(0, 0, 200, 20)]
    [InlineData(0, 0, 40, 40)]
    [InlineData(0, 0, 26, 14)]
    [InlineData(3, 5, 22, 18)]
    public void Checkerboard_MatchesPreviousPerCellAlgorithm(double x, double y, double width, double height)
    {
        var rect = new Rect(x, y, width, height);
        var canvasWidth = (int)Math.Ceiling(rect.Right);
        var canvasHeight = (int)Math.Ceiling(rect.Bottom);

        var surface = new ColorPreviewSurface();
        var actualVisual = CreateVisual((dc,r)=>surface.DrawCheckerboard(dc,r),rect);
        var referenceVisual = CreateVisual(DrawCheckerboardReference, rect);

        var actual = RenderToPixels(actualVisual, canvasWidth, canvasHeight, out var stride);
        var expected = RenderToPixels(referenceVisual, canvasWidth, canvasHeight, out _);

        for (var py = 0; py < canvasHeight; py++)
        {
            for (var px = 0; px < canvasWidth; px++)
            {
                var offset = (py * stride) + (px * Bpp);
                Assert.True(
                    actual[offset] == expected[offset] &&
                    actual[offset + 1] == expected[offset + 1] &&
                    actual[offset + 2] == expected[offset + 2] &&
                    actual[offset + 3] == expected[offset + 3],
                    $"({px},{py}) 处像素不一致：" +
                    $"实际 {actual[offset + 2]:X2}{actual[offset + 1]:X2}{actual[offset]:X2}，" +
                    $"参考 {expected[offset + 2]:X2}{expected[offset + 1]:X2}{expected[offset]:X2}");
            }
        }
    }

    /// <summary>
    /// 复用的前提是「实例不变、内容改变」——渲染后端按实例身份缓存原生画刷，靠内容变化
    /// 重建。若某次重构把这些字段又改回每次 new，缓存会重新开始每帧落空。
    /// </summary>
    [Fact]
    public void RepeatedRender_ReusesBrushAndPenInstances()
    {
        var spectrum=new ColorSpectrumSurface { BorderBrush=Brushes.Gray };
        var preview=new ColorPreviewSurface { Color=Color.Red };
        var ramp=new ColorRamp { Mode=ColorRampMode.Alpha,Color=Color.Red,Maximum=255 };
        void RenderPart(Control part)
        {
            part.Measure(new Size(200,200));part.Arrange(new Rect(0,0,200,200));
            RenderToPixels(CreateVisual((dc,_)=>part.Render(dc),new Rect(0,0,200,200)),200,200,out _);
        }
        RenderPart(spectrum);RenderPart(preview);
        var hueBrush=GetField<SolidColorBrush>(spectrum,"_hueBrush");
        var previewBrush=GetField<SolidColorBrush>(preview,"_colorBrush");
        var checker=GetField<BitmapImage>(preview,"_checker");
        var borderPen=GetField<Pen>(spectrum,"_borderPen");
        var alphaBrush=ramp.RampBrush;
        Assert.NotNull(hueBrush);Assert.NotNull(previewBrush);Assert.NotNull(checker);Assert.NotNull(borderPen);
        spectrum.Hue=240;preview.Color=Color.FromArgb(128,17,34,51);ramp.Color=preview.Color;
        RenderPart(spectrum);RenderPart(preview);
        Assert.Same(hueBrush,GetField<SolidColorBrush>(spectrum,"_hueBrush"));
        Assert.Same(previewBrush,GetField<SolidColorBrush>(preview,"_colorBrush"));
        Assert.Same(checker,GetField<BitmapImage>(preview,"_checker"));
        Assert.Same(borderPen,GetField<Pen>(spectrum,"_borderPen"));
        Assert.Same(alphaBrush,ramp.RampBrush);Assert.Equal(preview.Color,previewBrush!.Color);
        Assert.Equal(Color.Blue,hueBrush!.Color);
    }

    private static T? GetField<T>(object instance, string name) where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(instance) as T;
    }
}
