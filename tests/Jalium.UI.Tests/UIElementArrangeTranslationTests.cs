using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class UIElementArrangeTranslationTests
{
    private sealed class CountingWrapPanel : WrapPanel
    {
        public int ArrangeOverrideCount { get; private set; }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ArrangeOverrideCount++;
            return base.ArrangeOverride(finalSize);
        }
    }

    [Fact]
    public void PureParentTranslation_DoesNotRearrangeWrapPanelSubtree()
    {
        var panel = new CountingWrapPanel();
        for (int i = 0; i < 12; i++)
        {
            panel.Children.Add(new Border { Width = 30, Height = 20 });
        }

        panel.Measure(new Size(100, double.PositiveInfinity));
        double height = panel.DesiredSize.Height;
        panel.Arrange(new Rect(0, 0, 100, height));
        Rect childBounds = ((FrameworkElement)panel.Children[0]).VisualBounds;

        panel.Arrange(new Rect(0, -18, 100, height));

        Assert.Equal(1, panel.ArrangeOverrideCount);
        Assert.Equal(-18, panel.VisualBounds.Y);
        Assert.Equal(childBounds, ((FrameworkElement)panel.Children[0]).VisualBounds);
    }

    [Fact]
    public void ScrollViewerOffsetChange_TranslatesWrapPanelWithoutRearrangingItems()
    {
        var panel = new CountingWrapPanel
        {
            ItemWidth = 40,
            ItemHeight = 24,
            HorizontalSpacing = 4,
            VerticalSpacing = 4,
        };
        for (int i = 0; i < 30; i++)
        {
            panel.Children.Add(new Border());
        }

        var viewer = new ScrollViewer
        {
            Width = 140,
            Height = 90,
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            IsScrollBarAutoHideEnabled = false,
        };
        viewer.Measure(new Size(140, 90));
        viewer.Arrange(new Rect(0, 0, 140, 90));
        int initialArrangeCount = panel.ArrangeOverrideCount;
        Rect initialBounds = panel.VisualBounds;

        viewer.ScrollToVerticalOffset(36);
        viewer.Arrange(new Rect(0, 0, 140, 90));

        Assert.Equal(initialArrangeCount, panel.ArrangeOverrideCount);
        Assert.Equal(initialBounds.Y - 36, panel.VisualBounds.Y);
    }

    [Fact]
    public void InvalidatedArrange_DoesNotUseTranslationFastPath()
    {
        var panel = new CountingWrapPanel();
        panel.Children.Add(new Border { Width = 30, Height = 20 });
        panel.Measure(new Size(100, 100));
        panel.Arrange(new Rect(0, 0, 100, 100));

        panel.InvalidateArrange();
        panel.Arrange(new Rect(0, -18, 100, 100));

        Assert.Equal(2, panel.ArrangeOverrideCount);
    }

    [Fact]
    public void LayoutRounding_DoesNotUseTranslationFastPath()
    {
        var panel = new CountingWrapPanel { UseLayoutRounding = true };
        panel.Children.Add(new Border { Width = 30, Height = 20 });
        panel.Measure(new Size(100, 100));
        panel.Arrange(new Rect(0.4, 0.4, 100, 100));

        panel.Arrange(new Rect(0.8, 0.8, 100, 100));

        Assert.Equal(2, panel.ArrangeOverrideCount);
    }

    [Fact]
    public void CachedDescendantScreenBoundsRefreshAfterAncestorTranslation()
    {
        var child = new Border { Width = 30, Height = 20 };
        var parent = new Grid { Children = { child } };
        parent.Measure(new Size(100, 80));
        parent.Arrange(new Rect(10, 20, 100, 80));
        var before = child.GetScreenBounds();

        parent.Arrange(new Rect(35, 55, 100, 80));
        var after = child.GetScreenBounds();

        Assert.Equal(before.X + 25, after.X);
        Assert.Equal(before.Y + 35, after.Y);
    }

    [Fact]
    public void CachedScreenOriginKeepsLiveRenderSizeAfterSizeOnlyArrange()
    {
        var child = new Border();
        var parent = new Grid { Children = { child } };
        parent.Measure(new Size(100, 80));
        parent.Arrange(new Rect(10, 20, 100, 80));
        var before = child.GetScreenBounds();

        parent.Measure(new Size(180, 130));
        parent.Arrange(new Rect(10, 20, 180, 130));
        var after = child.GetScreenBounds();

        Assert.Equal(before.X, after.X);
        Assert.Equal(before.Y, after.Y);
        Assert.Equal(180, after.Width);
        Assert.Equal(130, after.Height);
    }
}
