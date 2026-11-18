using System.Diagnostics;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Shapes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class AnimationRenderedFrameTests
{
    [Theory]
    [InlineData("transform")]
    [InlineData("opacity")]
    [InlineData("color")]
    public void AnimationFrame_ChangesRenderedPixels(string property)
    {
        var brush = new SolidColorBrush(Colors.Red);
        var transform = new TranslateTransform();
        var rectangle = new Rectangle { Width = 8, Height = 8, Fill = brush, RenderTransform = transform };
        var canvas = new Canvas { Width = 40, Height = 20 };
        canvas.Children.Add(rectangle);
        canvas.Measure(new Size(40, 20));
        canvas.Arrange(new Rect(0, 0, 40, 20));
        AnimationTimeline animation;
        DependencyObject target;
        DependencyProperty targetProperty;
        if (property == "transform")
        {
            target = transform;
            targetProperty = TranslateTransform.XProperty;
            animation = new DoubleAnimation(0, 20, TimeSpan.FromSeconds(1));
        }
        else if (property == "opacity")
        {
            target = rectangle;
            targetProperty = UIElement.OpacityProperty;
            animation = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));
        }
        else
        {
            target = brush;
            targetProperty = SolidColorBrush.ColorProperty;
            animation = new ColorAnimation(Colors.Red, Colors.Blue, TimeSpan.FromSeconds(1));
        }
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(targetProperty));
        storyboard.Children.Add(animation);
        long start = Stopwatch.GetTimestamp();
        try
        {
            var before = Pixels(canvas);
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            var after = Pixels(canvas);
            Assert.False(before.SequenceEqual(after));
            const int oldPixel = (4 * 40 + 4) * 4;
            if (property == "transform")
            {
                Assert.Equal((byte)0, after[oldPixel + 2]);
                Assert.Equal((byte)255, after[(4 * 40 + 14) * 4 + 2]);
            }
            else
            {
                Assert.InRange(after[oldPixel + 2], (byte)126, (byte)129);
                Assert.Equal(property == "color" ? (byte)127 : (byte)0, after[oldPixel]);
            }
        }
        finally
        {
            storyboard.Stop();
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    private static byte[] Pixels(Visual visual)
    {
        var bitmap = new RenderTargetBitmap(40, 20, 96, 96, PixelFormat.Bgra32);
        bitmap.Clear(Colors.Black);
        bitmap.Render(visual);
        var pixels = new byte[40 * 20 * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, 40, 20), pixels, 40 * 4, 0);
        return pixels;
    }
}
