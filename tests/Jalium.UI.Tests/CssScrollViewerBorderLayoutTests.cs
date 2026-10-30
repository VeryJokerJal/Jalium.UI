using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssScrollViewerBorderLayoutTests
{
    [Fact]
    public void CssBorderAndPadding_ReduceViewportAndInsetContentAndBars()
    {
        var content = new Border { Width = 300, Height = 240 };
        var viewer = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            IsScrollBarAutoHideEnabled = false,
        };
        Css.SetStyle(viewer, "border:8px solid red; padding:4px 6px");

        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.Equal(160, viewer.ViewportWidth);
        Assert.Equal(104, viewer.ViewportHeight);
        Assert.Equal(140, viewer.ScrollableWidth);
        Assert.Equal(136, viewer.ScrollableHeight);
        Assert.Equal(new Vector(14, 12), VisualTreeHelper.GetOffset(content));

        var verticalBar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        var horizontalBar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(2));
        Assert.Equal(new Vector(180, 8), VisualTreeHelper.GetOffset(verticalBar));
        Assert.Equal(new Size(12, 112), verticalBar.RenderSize);
        Assert.Equal(new Vector(8, 120), VisualTreeHelper.GetOffset(horizontalBar));
        Assert.Equal(new Size(172, 12), horizontalBar.RenderSize);
    }

    [Fact]
    public void ScrolledContent_StaysInsideViewportWhileBorderAndPaddingRemainVisible()
    {
        var content = new Border { Width = 100, Height = 200, Background = Brushes.Lime };
        var viewer = new ScrollViewer
        {
            Content = content,
            Background = Brushes.Blue,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            IsScrollBarAutoHideEnabled = false,
        };
        Css.SetStyle(viewer, "border:8px solid red; padding:4px");
        CssBorderRadiusTests.Layout(viewer, 100, 80);
        viewer.ScrollToVerticalOffset(20);

        var pixels = CssBorderRadiusTests.Pixels(viewer, 100, 80);
        Assert.Equal((byte)255, pixels[(4 * 100 + 50) * 4 + 2]);
        Assert.Equal((byte)255, pixels[(10 * 100 + 50) * 4]);
        Assert.Equal((byte)0, pixels[(10 * 100 + 50) * 4 + 1]);
        Assert.Equal((byte)255, pixels[(20 * 100 + 50) * 4 + 1]);
        Assert.Same(viewer, viewer.HitTest(new Point(50, 10))?.VisualHit);
        Assert.Same(content, viewer.HitTest(new Point(50, 20))?.VisualHit);
    }

    [Fact]
    public void CssRemovalAndNativeBorderThickness_ReflowViewport()
    {
        var content = new Border { Width = 300, Height = 240 };
        var viewer = new ScrollViewer { Content = content };
        Css.SetStyle(viewer, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Css.SetStyle(viewer, "padding:2px");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Equal(new Size(184, 136), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(new Vector(2, 2), VisualTreeHelper.GetOffset(content));

        viewer.BorderThickness = new Thickness(2, 3, 4, 5);
        Css.SetStyle(viewer, "border:8px solid red; padding:4px 6px");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Equal(new Size(170, 124), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(new Vector(8, 7), VisualTreeHelper.GetOffset(content));
    }

    [Fact]
    public void RoundedViewport_ClipsScrolledContentAtItsInnerCorner()
    {
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = 100, Height = 200, Background = Brushes.Lime },
            Background = Brushes.Blue,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Css.SetStyle(viewer, "border:8px solid red; padding:4px; border-radius:30px");
        CssBorderRadiusTests.Layout(viewer, 100, 80);
        viewer.ScrollToVerticalOffset(20);

        var pixels = CssBorderRadiusTests.Pixels(viewer, 100, 80);
        Assert.Equal((byte)0, pixels[(12 * 100 + 12) * 4 + 1]);
        Assert.Equal((byte)255, pixels[(12 * 100 + 30) * 4 + 1]);
        Assert.Same(viewer, viewer.HitTest(new Point(12, 12))?.VisualHit);
    }

    [Fact]
    public void ScrollInfoContent_ReceivesTheInsetViewportConstraint()
    {
        var content = new ScrollInfoProbe();
        var viewer = new ScrollViewer { Content = content };
        Css.SetStyle(viewer, "border:8px solid red; padding:4px 6px");

        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.Same(viewer, content.ScrollOwner);
        Assert.Equal(new Size(160, 116), content.LastConstraint);
        Assert.Equal(new Vector(14, 12), VisualTreeHelper.GetOffset(content));
        Assert.Equal(new Size(160, 116), content.RenderSize);
        Assert.Equal(140, viewer.ScrollableWidth);
        Assert.Equal(124, viewer.ScrollableHeight);
    }

    [Fact]
    public void EmptyViewer_StillMeasuresItsBorderAndPadding()
    {
        var viewer = new ScrollViewer();
        Css.SetStyle(viewer, "border:8px solid red; padding:4px");

        viewer.Measure(new Size(200, 140));

        Assert.Equal(new Size(24, 24), viewer.DesiredSize);
    }

    private sealed class ScrollInfoProbe : FrameworkElement, IScrollInfo
    {
        public Size LastConstraint { get; private set; }
        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth => 300;
        public double ExtentHeight => 240;
        public double ViewportWidth => LastConstraint.Width;
        public double ViewportHeight => LastConstraint.Height;
        public double HorizontalOffset { get; private set; }
        public double VerticalOffset { get; private set; }
        public ScrollViewer? ScrollOwner { get; set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            LastConstraint = availableSize;
            return availableSize;
        }

        public void LineUp() { }
        public void LineDown() { }
        public void LineLeft() { }
        public void LineRight() { }
        public void PageUp() { }
        public void PageDown() { }
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelUp() { }
        public void MouseWheelDown() { }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }
        public void SetHorizontalOffset(double offset) => HorizontalOffset = offset;
        public void SetVerticalOffset(double offset) => VerticalOffset = offset;
        public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
    }
}
