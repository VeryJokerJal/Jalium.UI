using Jalium.UI.Controls;
using Jalium.UI.Controls.Virtualization;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTallVirtualizedTreeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderHoverUsesTheConfigurableBrushForTreesAndSelectors(bool selector)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        HeaderedItemsControl item = selector ? new TreeSelectorItem() : new TreeViewItem();
        item.Header = "Peer";
        if (!selector) item.Style = DevToolsThemeResources.GetStyle("DevTools.TreeItem");
        Layout(item);
        var header = Assert.IsType<Border>(item.FindName("PART_HeaderBorder"));
        header.SetIsMouseOver(true); Assert.Same(item.GetValue(TreeViewItem.HoverBrushProperty), header.Background);
        var replacement = new SolidColorBrush(Color.FromRgb(21, 31, 41)); item.SetValue(TreeViewItem.HoverBrushProperty, replacement);
        Assert.Same(replacement, header.Background);
        header.SetIsMouseOver(false); Assert.NotSame(replacement, header.Background);
    }

    [Fact]
    public void MeasuredItemsAbove4096PixelsKeepTheirFullIndexedHeight()
    {
        var index = new ItemHeightIndex(); index.Reset(3, 28);
        index.SetMeasuredHeight(0, 9000); index.SetMeasuredHeight(1, 48); index.SetMeasuredHeight(2, 28);
        Assert.Equal(9000, index.GetHeightAt(0)); Assert.Equal(9048, index.GetOffsetForIndex(2));
        Assert.Equal(9076, index.TotalHeight); Assert.Equal(0, index.GetIndexAtOffset(8000)); Assert.Equal(2, index.GetIndexAtOffset(9050));
    }

    [Fact]
    public void ExpandedTreeBranchesCanScrollPast4096ToTheLastSibling()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        var root = new TreeViewItem { Header = "Large branch", IsExpanded = true };
        for (int i = 0; i < 160; i++) root.Items.Add(new TreeViewItem { Header = "Child " + i, Height = 48 });
        var last = new TreeViewItem { Header = "Last sibling", Height = 32 };
        var tree = new ProbeTree(); tree.Items.Add(root); tree.Items.Add(last);
        var window = new DisplayedTestWindow { Content = tree };
        try
        {
            Layout(window); var panel = Assert.IsType<VirtualizingStackPanel>(tree.Host);
            Assert.True(root.DesiredSize.Height > 4096);
            Assert.True(panel.ExtentHeight >= root.DesiredSize.Height + last.DesiredSize.Height - .5,
                $"extent={panel.ExtentHeight}; root={root.DesiredSize}; last={last.DesiredSize}");
            var scroller = Assert.IsType<ScrollViewer>(tree.FindName("PART_ScrollViewer"));
            scroller.ScrollToBottom(); Layout(window);
            double top = last.TranslatePoint(new Point(), tree).Y;
            Assert.True(top >= 0 && top + last.ActualHeight <= tree.ActualHeight + .5, $"last={top}; height={last.ActualHeight}; tree={tree.ActualHeight}; offset={scroller.VerticalOffset}");
        }
        finally { window.Close(); }
    }

    private sealed class ProbeTree : TreeView { internal Panel? Host => ItemsHost; }
    private static void Layout(UIElement element) { for (int i = 0; i < 3; i++) { element.Measure(new Size(480, 360)); element.Arrange(new Rect(0, 0, 480, 360)); element.UpdateLayout(); } }
}
