using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSRazorItemsHostTemplateTests
{
    [Theory]
    [InlineData(RazorVirtualizeScrollHost.Self, Orientation.Vertical, RazorVirtualizeLayout.Stack)]
    [InlineData(RazorVirtualizeScrollHost.Self, Orientation.Horizontal, RazorVirtualizeLayout.Stack)]
    [InlineData(RazorVirtualizeScrollHost.Outer, Orientation.Vertical, RazorVirtualizeLayout.Stack)]
    [InlineData(RazorVirtualizeScrollHost.None, Orientation.Horizontal, RazorVirtualizeLayout.Stack)]
    [InlineData(RazorVirtualizeScrollHost.Self, Orientation.Horizontal, RazorVirtualizeLayout.Wrap)]
    [InlineData(RazorVirtualizeScrollHost.Self, Orientation.Vertical, RazorVirtualizeLayout.Wrap)]
    public void ThemeSelectsTheRequiredDirectScrollInfoChain(RazorVirtualizeScrollHost mode, Orientation orientation, RazorVirtualizeLayout layout)
    {
        var host = Create();
        host.ScrollHost = mode;
        host.Layout = layout;
        host.Orientation = orientation;
        Layout(host);
        var presenter = Assert.IsType<ItemsPresenter>(host.FindName("PART_ItemsPresenter"));
        Assert.Equal(mode, host.ResolvedScrollHost);
        Assert.Equal(mode.ToString(), VisualStateManager.GetCurrentStateName(host, "ScrollHostStates"));
        if (mode == RazorVirtualizeScrollHost.Self)
        {
            var viewer = Assert.IsType<ScrollViewer>(host.FindName("PART_ScrollViewer"));
            Assert.Same(presenter, viewer.Content);
            Assert.True(viewer.CanContentScroll);
            Assert.Equal(host.ScrollOrientation == Orientation.Vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled, viewer.VerticalScrollBarVisibility);
        }
        else
        {
            Assert.Null(host.FindName("PART_ScrollViewer"));
            Assert.Same(presenter, host.GetVisualChild(0));
        }
    }

    [Theory]
    [InlineData(RazorVirtualizeLayout.Stack, Orientation.Vertical)]
    [InlineData(RazorVirtualizeLayout.Stack, Orientation.Horizontal)]
    [InlineData(RazorVirtualizeLayout.Wrap, Orientation.Vertical)]
    [InlineData(RazorVirtualizeLayout.Wrap, Orientation.Horizontal)]
    public void ThemeChoosesTheVirtualizingPanel(RazorVirtualizeLayout layout, Orientation orientation)
    {
        var host = Create();
        host.Layout = layout;
        host.Orientation = orientation;
        Layout(host);
        var panel = host.ItemsPanel!.CreatePanel();
        if (layout == RazorVirtualizeLayout.Wrap)
            Assert.Equal(orientation, Assert.IsType<VirtualizingWrapPanel>(panel).Orientation);
        else
            Assert.Equal(orientation, Assert.IsType<VirtualizingStackPanel>(panel).Orientation);
        Assert.Equal(layout.ToString(), VisualStateManager.GetCurrentStateName(host, "LayoutStates"));
    }

    [Fact]
    public void CallerTemplateAndPanelSurviveModeAndShapeChanges()
    {
        var host = Create();
        var custom = new ControlTemplate(typeof(RazorItemsHost));
        custom.SetVisualTree(() => new Border());
        var panel = new ItemsPanelTemplate
        {
            PanelType = typeof(StackPanel)
        };
        host.Template = custom;
        host.ItemsPanel = panel;
        Layout(host);
        host.Orientation = Orientation.Horizontal;
        host.Layout = RazorVirtualizeLayout.Wrap;
        host.ScrollHost = RazorVirtualizeScrollHost.Outer;
        Layout(host);
        Assert.Same(custom, host.Template);
        Assert.Same(panel, host.ItemsPanel);
        Assert.Null(host.FindName("PART_ItemsPresenter"));
        Assert.Single(Enumerable.Range(0, host.VisualChildrenCount));
        Assert.IsType<Border>(host.GetVisualChild(0));
    }

    [Fact]
    public void AutoModeFollowsTheOuterOwnerAndReturnsToSelf()
    {
        var host = Create();
        var viewer = new ScrollViewer
        {
            CanContentScroll = true,
            Content = host
        };
        Layout(viewer);
        Assert.Equal(RazorVirtualizeScrollHost.Outer, host.ResolvedScrollHost);
        Assert.Null(host.FindName("PART_ScrollViewer"));
        viewer.Content = null;
        Layout(host);
        Assert.Equal(RazorVirtualizeScrollHost.Self, host.ResolvedScrollHost);
        Assert.IsType<ScrollViewer>(host.FindName("PART_ScrollViewer"));
        host.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(host, "CommonStates"));
    }

    private static RazorItemsHost Create() => new()
    {
        Style = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(RazorItemsHost)]
    };
    private static void Layout(UIElement element)
    {
        for (var i = 0; i < 3; i++)
        {
            element.Measure(new Size(320, 240));
            element.Arrange(new Rect(0, 0, 320, 240));
        }
    }
}
