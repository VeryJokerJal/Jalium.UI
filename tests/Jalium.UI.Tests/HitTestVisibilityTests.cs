using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class HitTestVisibilityTests
{
    [Fact]
    public void VisualTreeHelper_HitTest_ShouldSkipElement_WhenIsHitTestVisibleFalse()
    {
        var back = new Border { Width = 40, Height = 30 };
        var front = new Border { Width = 40, Height = 30, IsHitTestVisible = false };
        var root = new Grid { Width = 40, Height = 30 };
        root.Children.Add(back);
        root.Children.Add(front);

        root.Measure(new Size(40, 30));
        root.Arrange(new Rect(0, 0, 40, 30));

        var hit = VisualTreeHelper.HitTest(root, new Point(10, 10));

        Assert.NotNull(hit);
        Assert.Same(back, hit!.VisualHit);
    }

    [Fact]
    public void WindowHitTest_ShouldSkipEntireSubtree_WhenParentIsNotHitTestVisible()
    {
        var back = new Border { Width = 40, Height = 30 };
        var child = new CountingBorder { Width = 40, Height = 30 };
        var blockedParent = new Border
        {
            Width = 40,
            Height = 30,
            IsHitTestVisible = false,
            Child = child
        };

        var root = new Grid { Width = 40, Height = 30 };
        root.Children.Add(back);
        root.Children.Add(blockedParent);

        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 40,
            Height = 30,
            Content = root
        };

        window.Measure(new Size(40, 30));
        window.Arrange(new Rect(0, 0, 40, 30));

        Assert.False(child.IsHitTestVisible);

        var hit = InvokeHitTestElement(window, new Point(10, 10));

        Assert.Same(back, hit);
        Assert.Equal(0, child.HitTestCount);
    }

    [Fact]
    public void WindowHitTest_ShouldPassThroughTransparentStackPanel_WhenPointMissesChildren()
    {
        var glass = new Border { Width = 120, Height = 80 };
        var overlay = new StackPanel
        {
            Width = 80,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        overlay.Children.Add(new Border { Width = 40, Height = 20 });

        var root = new Grid { Width = 120, Height = 80 };
        root.Children.Add(glass);
        root.Children.Add(overlay);

        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 120,
            Height = 80,
            Content = root
        };

        window.Measure(new Size(120, 80));
        window.Arrange(new Rect(0, 0, 120, 80));

        var pointInsideOverlayGap = new Point(60, 60);
        var hit = InvokeHitTestElement(window, pointInsideOverlayGap);

        Assert.Same(glass, hit);
    }

    [Fact]
    public void WindowHitTestCache_ShouldIgnoreContentClippedByScrollViewerViewport()
    {
        var background = new Border { Width = 200, Height = 100 };
        var viewer = new ScrollViewer
        {
            Width = 80,
            Height = 80,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            IsScrollBarAutoHideEnabled = false,
            Content = new Border
            {
                Width = 200,
                Height = 200
            }
        };

        var root = new Grid { Width = 200, Height = 100 };
        root.Children.Add(background);
        root.Children.Add(viewer);

        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 200,
            Height = 100,
            Content = root
        };

        window.Measure(new Size(200, 100));
        window.Arrange(new Rect(0, 0, 200, 100));

        var contentHit = InvokeHitTestElement(window, new Point(20, 20));
        Assert.NotNull(contentHit);

        var clippedAreaHit = InvokeHitTestElement(window, new Point(120, 20));

        Assert.Same(background, clippedAreaHit);
    }

    [Fact]
    public void WindowHitTestCache_ShouldAllowScrollViewerScrollBarToWinOverContent()
    {
        var viewer = new ScrollViewer
        {
            Width = 80,
            Height = 80,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            IsScrollBarAutoHideEnabled = false,
            Content = new Border
            {
                Width = 200,
                Height = 200
            }
        };

        var root = new Grid { Width = 120, Height = 120 };
        root.Children.Add(viewer);

        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 120,
            Height = 120,
            Content = root
        };

        window.Measure(new Size(120, 120));
        window.Arrange(new Rect(0, 0, 120, 120));

        var contentHit = InvokeHitTestElement(window, new Point(20, 20));
        Assert.NotNull(contentHit);

        var scrollBarHit = InvokeHitTestElement(window, new Point(74, 20));

        Assert.NotNull(scrollBarHit);
        Assert.NotNull(FindVisualAncestor<ScrollBar>(scrollBarHit));
    }

    [Fact]
    public void WindowHitTest_ShouldTreatPointsOutsideScrollViewerViewportAsClipped()
    {
        // Scroll the ScrollViewer so an input control lives past the top of the
        // viewport. Its VisualBounds still intersect a sibling above the
        // ScrollViewer, but the user only sees the sibling — clicks there must
        // hit the sibling, not the scrolled-off input control. This is a scaled
        // down version of the "click Light mode above a scrollable region" bug.
        var topBar = new Border { Width = 200, Height = 40 };

        var scrollContent = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Width = 200
        };
        for (int i = 0; i < 12; i++)
        {
            scrollContent.Children.Add(new Border { Width = 200, Height = 30 });
        }

        var viewer = new ScrollViewer
        {
            Width = 200,
            Height = 120,
            Content = scrollContent,
            IsScrollBarAutoHideEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical, Width = 200 };
        stack.Children.Add(topBar);
        stack.Children.Add(viewer);

        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 200,
            Height = 160,
            Content = stack
        };

        window.Measure(new Size(200, 160));
        window.Arrange(new Rect(0, 0, 200, 160));

        // Prime the cache with a hit inside the ScrollViewer viewport, then
        // scroll the content so the cached element is no longer visible.
        var viewportHit = InvokeHitTestElement(window, new Point(100, 60));
        Assert.NotNull(viewportHit);

        viewer.ScrollToVerticalOffset(100);
        window.Measure(new Size(200, 160));
        window.Arrange(new Rect(0, 0, 200, 160));

        // Clicking inside the topBar should land on the topBar regardless of
        // what was cached below — the scrolled-off content must not reach up
        // into the bar just because its VisualBounds technically extend there.
        var topBarHit = InvokeHitTestElement(window, new Point(100, 20));
        Assert.Same(topBar, topBarHit);
    }

    private static UIElement? InvokeHitTestElement(Window window, Point point)
    {
        var method = typeof(Window).GetMethod("HitTestElement", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(window, [point, "hit-test-visible-test"]) as UIElement;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? start) where T : class
    {
        for (var current = start; current != null; current = (current as UIElement)?.VisualParent as DependencyObject)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private sealed class CountingBorder : Border
    {
        public int HitTestCount { get; private set; }

        protected override HitTestResult? HitTestCore(Point point)
        {
            HitTestCount++;
            return base.HitTestCore(point);
        }
    }
}
