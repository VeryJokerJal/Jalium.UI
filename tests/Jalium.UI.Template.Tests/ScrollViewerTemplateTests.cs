using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ScrollViewerTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultTemplateOwnsViewportAndBarsAndPreservesContentIdentity()
    {
        var content = new Border { Width = 600, Height = 1000 };
        var viewer = Create(content);
        Layout(viewer);
        Assert.IsType<Grid>(viewer.GetVisualChild(0));
        var presenter = Assert.IsType<ScrollContentPresenter>(viewer.FindName("PART_ScrollContentPresenter"));
        Assert.Same(content, presenter.Content);
        Assert.Same(presenter, content.VisualParent);
        Assert.IsType<ScrollViewerLayoutPanel>(viewer.FindName("PART_LayoutPanel"));
        var vertical = Assert.IsType<ScrollBar>(viewer.FindName("PART_VerticalScrollBar"));
        var horizontal = Assert.IsType<ScrollBar>(viewer.FindName("PART_HorizontalScrollBar"));
        Assert.Equal(600, viewer.ExtentWidth);
        Assert.Equal(1000, viewer.ExtentHeight);
        Assert.Equal(240, viewer.ViewportWidth);
        Assert.Equal(160, viewer.ViewportHeight);
        Assert.Equal(840, vertical.Maximum);
        Assert.Equal(360, horizontal.Maximum);
        viewer.ScrollToVerticalOffset(120);
        Layout(viewer);
        Assert.Equal(120, viewer.VerticalOffset);
        Assert.Equal(-120, presenter.VisualBounds.Y);
    }

    [Fact]
    public void TemplateReplacementReleasesOldInputAndRestoresTheSameContent()
    {
        var content = new Border { Width = 600, Height = 1000 };
        var viewer = Create(content); Layout(viewer);
        var oldBar = Assert.IsType<ScrollBar>(viewer.FindName("PART_VerticalScrollBar"));
        var oldPresenter = Assert.IsType<ScrollContentPresenter>(viewer.FindName("PART_ScrollContentPresenter"));
        var custom = new ControlTemplate(typeof(ScrollViewer));
        custom.SetVisualTree(() => new Border { Height = 30 });
        viewer.Template = custom; Layout(viewer);
        oldBar.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbPosition, 400) { RoutedEvent = ScrollBar.ScrollEvent });
        Assert.Equal(0, viewer.VerticalOffset);
        viewer.ClearValue(Control.TemplateProperty); Layout(viewer);
        var current = Assert.IsType<ScrollContentPresenter>(viewer.FindName("PART_ScrollContentPresenter"));
        Assert.NotSame(oldPresenter, current);
        Assert.Same(content, current.Content);
        Assert.Same(current, content.VisualParent);
        Assert.NotSame(oldBar, viewer.FindName("PART_VerticalScrollBar"));
        viewer.ScrollToBottom(); Layout(viewer);
        Assert.Equal(viewer.ScrollableHeight, viewer.VerticalOffset);
    }

    [Fact]
    public void ScalarAndDataTemplateContentUseThePresenterPipeline()
    {
        var viewer = Create("Short content"); Layout(viewer);
        var presenter = Assert.IsType<ScrollContentPresenter>(viewer.FindName("PART_ScrollContentPresenter"));
        Assert.Equal("Short content", Assert.IsType<TextBlock>(presenter.GetVisualChild(0)).Text);
        Assert.True(viewer.ExtentWidth > 0);
        var template = new DataTemplate(); template.SetVisualTree(() => new Border { Width = 380, Height = 720 });
        viewer.ContentTemplate = template; viewer.Content = new object(); Layout(viewer);
        Assert.IsType<Border>(presenter.GetVisualChild(0));
        Assert.Equal(380, viewer.ExtentWidth);
        Assert.Equal(720, viewer.ExtentHeight);
    }

    [Fact]
    public void LogicalContentKeepsItsOwnerAndVirtualizesInsideTheDefaultTemplate()
    {
        var row = new DataTemplate(); row.SetVisualTree(() => new Border { Width = 220, Height = 25 });
        var host = new RazorItemsHost { ItemsSource = Enumerable.Range(0, 5000).ToArray(), ItemTemplate = row };
        var viewer = Create(host); viewer.CanContentScroll = true;
        for(int pass=0;pass<3;pass++) Layout(viewer);
        Assert.Same(viewer, host.ScrollOwner);
        Assert.DoesNotContain(Descendants(host), child => child is ScrollViewer);
        var panel = Assert.Single(Descendants(host).OfType<VirtualizingPanel>());
        Assert.InRange(panel.Children.Count, 6, 100);
        viewer.ScrollToVerticalOffset(2400);
        for(int pass=0;pass<3;pass++) Layout(viewer);
        Assert.True(viewer.VerticalOffset > 0);
        Assert.InRange(panel.Children.Count, 6, 100);
        Assert.Same(host, Assert.IsType<ScrollContentPresenter>(viewer.FindName("PART_ScrollContentPresenter")).Content);
    }

    [Fact]
    public void AnUnboundedFirstMeasureStillArrangesLogicalContentAtTheFiniteViewport()
    {
        var row = new DataTemplate(); row.SetVisualTree(() => new Border { Width = 220, Height = 25 });
        var host = new RazorItemsHost { ItemsSource = Enumerable.Range(0, 5000).ToArray(), ItemTemplate = row };
        var viewer = Create(host); viewer.Height = double.NaN; viewer.CanContentScroll = true;
        viewer.Measure(new Size(240, double.PositiveInfinity));
        viewer.Arrange(new Rect(0, 0, 240, 160));
        var presenter = Assert.IsType<ScrollContentPresenter>(viewer.FindName("PART_ScrollContentPresenter"));
        Assert.Equal(160, presenter.RenderSize.Height);
        Assert.Equal(160, host.RenderSize.Height);
    }

    private static IEnumerable<Visual> Descendants(Visual parent)
    {
        for(int index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
            if(VisualTreeHelper.GetChild(parent,index) is Visual child)
            {
                yield return child;
                foreach(var descendant in Descendants(child)) yield return descendant;
            }
    }

    private static ScrollViewer Create(object content)
    {
        var viewer = new ScrollViewer
        {
            Content = content, Width = 240, Height = 160,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsOverlayScrollBarEnabled = true, IsScrollBarAutoHideEnabled = false,
            IsScrollInertiaEnabled = false
        };
        viewer.Resources.MergedDictionaries.Add(Defaults.Value);
        return viewer;
    }

    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure();
        element.Measure(new Size(240, 160));
        element.Arrange(new Rect(0, 0, 240, 160));
        element.UpdateLayout();
    }
}
