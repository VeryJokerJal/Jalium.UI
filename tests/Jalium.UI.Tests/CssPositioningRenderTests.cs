using Jalium.UI.Controls;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssPositioningRenderTests
{
    [Fact]
    public void StickyPaintAndPointerHitStayAboveTheFollowingFlowContent()
    {
        var sticky = new Border { Height = 32, Background = Brushes.Red };
        Css.SetStyle(sticky, "position: sticky; top: 8px;");
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 40 });
        content.Children.Add(sticky);
        content.Children.Add(new Border { Height = 400, Background = Brushes.Blue });
        var viewer = Viewer(content, 240, 120);
        viewer.ScrollToVerticalOffset(90);
        Assert.Equal(Colors.Red, Pixel(Render(viewer), 20, 20));
        Assert.Same(sticky, viewer.HitTest(new Point(20, 20))?.VisualHit);
    }

    [Theory]
    [InlineData(720, 460)]
    [InlineData(375, 520)]
    public void FixedPaintAndPointerHitEscapeIntermediateScrollClips(double width, double height)
    {
        var fixedBox = new Border { Background = Brushes.Red };
        Css.SetStyle(fixedBox, "position: fixed; top: 12px; right: 16px; width: 80px; height: 32px;");
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 600, Background = Brushes.Blue });
        content.Children.Add(fixedBox);
        var viewer = Viewer(content, 200, 140);
        viewer.HorizontalAlignment = HorizontalAlignment.Left;
        viewer.VerticalAlignment = VerticalAlignment.Top;
        viewer.Margin = new Thickness(32, 80, 0, 0);
        var root = new Grid { Background = Brushes.White };
        root.Children.Add(viewer);
        Layout(root, width, height);
        Assert.Equal(Colors.Blue, Pixel(Render(root), 48, 96));
        var point = new Point(width - 40, 24);
        Assert.Equal(Colors.Red, Pixel(Render(root), (int)point.X, (int)point.Y));
        Assert.Same(fixedBox, root.HitTest(point)?.VisualHit);
        Assert.Same(fixedBox, root.InputHitTest(point));
        var peer = new UIElementAutomationPeer(fixedBox);
        Assert.False(peer.IsOffscreen());
        Assert.Equal(width - 96, peer.GetBoundingRectangle().X, 6);
        Assert.True(new Rect(width - 96, 12, 80, 32).Contains(peer.GetClickablePoint()));
        SaveEvidence(Render(root), $"fixed-{width}-before.png");
        viewer.ScrollToVerticalOffset(160);
        Assert.Equal(Colors.Red, Pixel(Render(root), (int)point.X, (int)point.Y));
        Assert.Same(fixedBox, root.HitTest(point)?.VisualHit);
        SaveEvidence(Render(root), $"fixed-{width}-scrolled.png");
    }

    [Fact]
    public void PromotedFixedBoxRetainsAncestorOpacityVisibilityAndInputEligibility()
    {
        var fixedBox = new Border { Background = Brushes.Red };
        Css.SetStyle(fixedBox, "position: fixed; left: 10px; top: 10px; width: 50px; height: 30px;");
        var content = new StackPanel { Opacity = .5 };
        content.Children.Add(fixedBox);
        var viewer = Viewer(content, 100, 80);
        viewer.Margin = new Thickness(100, 80, 0, 0);
        viewer.HorizontalAlignment = HorizontalAlignment.Left;
        viewer.VerticalAlignment = VerticalAlignment.Top;
        var root = new Grid { Background = Brushes.White };
        root.Children.Add(viewer);
        Layout(root, 240, 200);
        var color = Pixel(Render(root), 20, 20);
        Assert.Equal(255, color.R);
        Assert.InRange(color.G, (byte)127, (byte)128);
        Assert.InRange(color.B, (byte)127, (byte)128);
        Assert.Same(fixedBox, root.HitTest(new Point(20, 20))?.VisualHit);
        content.IsHitTestVisible = false;
        Assert.NotSame(fixedBox, root.HitTest(new Point(20, 20))?.VisualHit);
        content.Visibility = Visibility.Hidden;
        Assert.Equal(Colors.White, Pixel(Render(root), 20, 20));
    }

    [Fact]
    public void FixedZIndexIsComparedWithContainingBlockSiblings()
    {
        var fixedBox = new Border { Background = Brushes.Red };
        Css.SetStyle(fixedBox, "position: fixed; left: 10px; top: 10px; width: 50px; height: 30px;");
        var content = new StackPanel();
        content.Children.Add(fixedBox);
        var root = new Grid();
        root.Children.Add(content);
        var overlay = new Border { Background = Brushes.Blue };
        Panel.SetZIndex(overlay, 5);
        root.Children.Add(overlay);
        Layout(root, 200, 120);
        Assert.Equal(Colors.Blue, Pixel(Render(root), 20, 20));
        Assert.Same(overlay, root.HitTest(new Point(20, 20))?.VisualHit);
        Panel.SetZIndex(fixedBox, 6);
        Assert.Equal(Colors.Red, Pixel(Render(root), 20, 20));
        Assert.Same(fixedBox, root.HitTest(new Point(20, 20))?.VisualHit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedEscapesOverflowButRetainsExplicitAncestorPathClips(bool css)
    {
        var fixedBox = new Border { Background = Brushes.Red };
        Css.SetStyle(fixedBox, "position: fixed; left: 10px; top: 10px; width: 40px; height: 30px;");
        var parent = new StackPanel { Width = 80, Height = 60, Margin = new Thickness(100, 80, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        parent.Children.Add(fixedBox);
        var root = new Grid { Background = Brushes.White }; root.Children.Add(parent);
        Css.SetStyle(parent, "overflow: hidden;");
        Layout(root, 240, 180);
        Assert.Equal(Colors.Red, Pixel(Render(root), 20, 20));
        if (css) Css.SetStyle(parent, "overflow: hidden; clip-path: inset(0px);");
        else parent.Clip = new RectangleGeometry(new Rect(0, 0, 80, 60));
        Layout(root, 240, 180);
        Assert.Equal(Colors.White, Pixel(Render(root), 20, 20));
        Assert.NotSame(fixedBox, root.HitTest(new Point(20, 20))?.VisualHit);
        Assert.NotSame(fixedBox, root.InputHitTest(new Point(20, 20)));
        Assert.True(new UIElementAutomationPeer(fixedBox).IsOffscreen());
    }

    private static ScrollViewer Viewer(UIElement content, double width, double height)
    {
        var viewer = new ScrollViewer
        {
            Width = width, Height = height,
            Content = content, CanContentScroll = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Layout(viewer, width, height);
        return viewer;
    }

    private static void Layout(UIElement root, double width, double height)
    {
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height));
    }

    private static RenderTargetBitmap Render(FrameworkElement root)
    {
        var target = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormat.Bgra32);
        target.Render(root);
        return target;
    }

    private static Color Pixel(RenderTargetBitmap target, int x, int y)
    {
        var pixel = new byte[4];
        target.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static void SaveEvidence(RenderTargetBitmap target, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_POSITION_EVIDENCE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }
}
