using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Shapes;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRenderTargetDpiTests
{
    [Theory]
    [InlineData(48, 48, false)]
    [InlineData(48, 48, true)]
    [InlineData(96, 96, false)]
    [InlineData(96, 96, true)]
    [InlineData(120, 120, false)]
    [InlineData(120, 120, true)]
    [InlineData(144, 144, false)]
    [InlineData(144, 144, true)]
    [InlineData(192, 192, false)]
    [InlineData(192, 192, true)]
    [InlineData(144, 192, false)]
    [InlineData(144, 192, true)]
    [InlineData(192, 144, false)]
    [InlineData(192, 144, true)]
    public void PublicRenderScalesNestedOffsetsMasksAndRotationExactlyOnce(double dpiX, double dpiY, bool rotated)
    {
        var visual = Scene(rotated);
        var target = new RenderTargetBitmap((int)(120 * dpiX / 96), (int)(120 * dpiY / 96), dpiX, dpiY, PixelFormat.Bgra32);
        target.Render(visual);
        var first = Pixels(target);
        Assert.Equal(255, Alpha(first, target, Map(new Point(20, 50), rotated)));
        Assert.Equal(255, Alpha(first, target, Map(new Point(95, 50), rotated)));
        Assert.Equal(0, Alpha(first, target, Map(new Point(1, 1), rotated)));
        Assert.Equal(0, Alpha(first, target, Map(new Point(65, 25), rotated)));
        Assert.Equal(0, Alpha(first, target, Map(new Point(101, 50), rotated)));
        target.Clear(Color.FromArgb(0, 0, 0, 0));
        target.Render(visual);
        Assert.Equal(first, Pixels(target));
    }

    [Theory]
    [InlineData(96, 96)]
    [InlineData(144, 192)]
    [InlineData(192, 144)]
    public void PoppingTransformAndClipRestoresTheTargetDpi(double dpiX, double dpiY)
    {
        var target = new RenderTargetBitmap((int)(96 * dpiX / 96), (int)(96 * dpiY / 96), dpiX, dpiY, PixelFormat.Bgra32);
        var context = new SoftwareDrawingContext(target) { Offset = new Point(4, 6) };
        context.PushTransform(new ScaleTransform(2, 1.5));
        context.PushClip(new RectangleGeometry(new Rect(0, 0, 12, 10)));
        context.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 20, 20));
        context.Pop(); context.Pop();
        context.Offset = new Point();
        context.DrawRectangle(Brushes.Blue, null, new Rect(40, 30, 12, 12));
        context.Close();
        var pixels = Pixels(target);
        Assert.Equal(255, Channel(pixels, target, new Point(14, 13), 2));
        Assert.Equal(0, Alpha(pixels, target, new Point(32, 12)));
        Assert.Equal(255, Channel(pixels, target, new Point(45, 35), 0));
        Assert.Equal(0, Alpha(pixels, target, new Point(65, 35)));
    }

    [Theory]
    [InlineData(0, 0, 96, 96)]
    [InlineData(-96, double.NaN, 96, 96)]
    [InlineData(double.PositiveInfinity, 192, 96, 192)]
    [InlineData(144, double.PositiveInfinity, 144, 96)]
    public void InvalidDpiRetainsTheFiniteDefaultAndStillRenders(double dpiX, double dpiY, double expectedX, double expectedY)
    {
        var target = new RenderTargetBitmap(128, 128, dpiX, dpiY, PixelFormat.Bgra32);
        Assert.Equal(expectedX, target.DpiX);
        Assert.Equal(expectedY, target.DpiY);
        var visual = new Border { Width = 30, Height = 20, Background = Brushes.Red };
        visual.Measure(new Size(30, 20)); visual.Arrange(new Rect(0, 0, 30, 20));
        target.Render(visual);
        var pixels = Pixels(target);
        Assert.Equal(255, Alpha(pixels, target, new Point(10, 10)));
        Assert.Equal(0, Alpha(pixels, target, new Point(35, 10)));
    }

    private static Canvas Scene(bool rotated)
    {
        var content = new Canvas
        {
            Width = 100, Height = 100,
            Clip = Geometry.Parse("M0,0 L100,0 100,100 0,100 Z M50,15 L85,15 85,35 50,35 Z"),
        };
        content.Children.Add(new Rectangle { Width = 100, Height = 100, Fill = Brushes.Red });
        var rounded = new Canvas
        {
            Width = 100, Height = 100,
            Clip = new RectangleGeometry(new Rect(0, 0, 100, 100), 30, 30),
        };
        rounded.Children.Add(content);
        if (rotated) rounded.RenderTransform = new MatrixTransform(new Matrix(0, 1, -1, 0, 100, 0));
        var root = new Canvas { Width = 120, Height = 120 };
        Canvas.SetLeft(rounded, 10); Canvas.SetTop(rounded, 8); root.Children.Add(rounded);
        root.Measure(new Size(120, 120));
        // Rendering a subtree ignores the root's parent-space arrange offset.
        root.Arrange(new Rect(11, 7, 120, 120));
        return root;
    }

    private static Point Map(Point point, bool rotated) => rotated
        ? new Point(10 + 100 - point.Y, 8 + point.X)
        : new Point(10 + point.X, 8 + point.Y);
    private static byte[] Pixels(RenderTargetBitmap target)
    {
        var data = new byte[target.PixelWidth * target.PixelHeight * 4];
        target.CopyPixels(new Int32Rect(0, 0, target.PixelWidth, target.PixelHeight), data, target.PixelWidth * 4, 0);
        return data;
    }
    private static byte Alpha(byte[] pixels, RenderTargetBitmap target, Point point) => Channel(pixels, target, point, 3);
    private static byte Channel(byte[] pixels, RenderTargetBitmap target, Point point, int channel)
        => pixels[((int)(point.Y * target.DpiY / 96) * target.PixelWidth + (int)(point.X * target.DpiX / 96)) * 4 + channel];
}
