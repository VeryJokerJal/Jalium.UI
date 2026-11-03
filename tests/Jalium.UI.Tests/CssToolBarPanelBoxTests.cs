using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssToolBarPanelBoxTests
{
    [Theory]
    [InlineData(Orientation.Horizontal, 69, 26)]
    [InlineData(Orientation.Vertical, 44, 41)]
    public void ToolBarPanel_UsesCssContentBoxAndGap(
        Orientation orientation, int expectedWidth, int expectedHeight)
    {
        var first = new Button { Width = 20, Height = 10 };
        var second = new Button { Width = 30, Height = 12 };
        var panel = new ToolBarPanel { Orientation = orientation };
        panel.Children.Add(first);
        panel.Children.Add(second);
        Css.SetStyle(panel,
            "border:4px solid transparent;padding:3px;" +
            (orientation == Orientation.Horizontal
                ? "row-gap:2px;column-gap:5px"
                : "row-gap:5px;column-gap:2px"));
        CssBorderRadiusTests.Layout(panel, 100, 50);

        Assert.Equal(new Size(expectedWidth, expectedHeight), panel.DesiredSize);
        Assert.Equal(5, panel.GetItemSpacing(100));
        if (orientation == Orientation.Horizontal)
        {
            Assert.Equal(new Rect(7, 20, 20, 10), first.VisualBounds);
            Assert.Equal(new Rect(32, 19, 30, 12), second.VisualBounds);
        }
        else
        {
            Assert.Equal(new Rect(40, 7, 20, 10), first.VisualBounds);
            Assert.Equal(new Rect(35, 22, 30, 12), second.VisualBounds);
        }

        Css.SetStyle(panel, "");
        CssBorderRadiusTests.Layout(panel, 100, 50);
        Assert.Equal(0, panel.GetItemSpacing(100));
        Assert.Equal(orientation == Orientation.Horizontal
            ? new Rect(0, 20, 20, 10)
            : new Rect(40, 0, 20, 10), first.VisualBounds);
        Assert.Equal(orientation == Orientation.Horizontal
            ? new Rect(20, 19, 30, 12)
            : new Rect(35, 10, 30, 12), second.VisualBounds);
    }

    [Fact]
    public void ToolBarOverflowPanel_WrapsWithinCssContentBoxAndRestoresOnRemoval()
    {
        var first = new Button { Width = 40, Height = 10 };
        var second = new Button { Width = 40, Height = 10 };
        var panel = new ToolBarOverflowPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);
        Css.SetStyle(panel,
            "border:8px solid transparent;padding:4px;row-gap:6px;column-gap:5px");
        CssBorderRadiusTests.Layout(panel, 100, 60);

        Assert.Equal(new Size(64, 50), panel.DesiredSize);
        Assert.Equal(new Rect(12, 12, 40, 10), first.VisualBounds);
        Assert.Equal(new Rect(12, 28, 40, 10), second.VisualBounds);

        Css.SetStyle(panel, "");
        CssBorderRadiusTests.Layout(panel, 100, 60);
        Assert.Equal(new Size(80, 10), panel.DesiredSize);
        Assert.Equal(new Rect(0, 0, 40, 10), first.VisualBounds);
        Assert.Equal(new Rect(40, 0, 40, 10), second.VisualBounds);
    }

    [Fact]
    public void ToolBarPanel_PercentageGapYieldsToLocalSpacing()
    {
        var panel = new ToolBarPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Button { Width = 20, Height = 10 });
        var second = new Button { Width = 20, Height = 10 };
        panel.Children.Add(second);
        Css.SetStyle(panel, "column-gap:10%");
        CssBorderRadiusTests.Layout(panel, 100, 30);
        Assert.Equal(10, panel.GetItemSpacing(100));
        Assert.Equal(30, second.VisualBounds.X);

        panel.Spacing = 3;
        CssBorderRadiusTests.Layout(panel, 100, 30);
        Assert.Equal(3, panel.GetItemSpacing(100));
        Assert.Equal(23, second.VisualBounds.X);

        panel.ClearValue(StackPanel.SpacingProperty);
        CssBorderRadiusTests.Layout(panel, 100, 30);
        Assert.Equal(10, panel.GetItemSpacing(100));
        Assert.Equal(30, second.VisualBounds.X);

        Css.SetStyle(panel,
            "border:4px solid transparent;padding:3px;column-gap:10%");
        CssBorderRadiusTests.Layout(panel, 100, 30);
        Assert.Equal(8.6, panel.GetItemSpacing(86), 4);
        Assert.InRange(second.VisualBounds.X, 35.5, 36.5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolBarPanels_PercentagePaddingResolvesAndYieldsToLocalValue(bool overflow)
    {
        Panel panel = overflow ? new ToolBarOverflowPanel() : new ToolBarPanel();
        Css.SetStyle(panel, "padding:10%");
        CssBorderRadiusTests.Layout(panel, 100, 60);
        Assert.Equal(new Thickness(10), CssBoxMetrics.ContentInsets(panel, 100));

        if (panel is ToolBarPanel itemsPanel)
            itemsPanel.Padding = new Thickness(4);
        else
            ((ToolBarOverflowPanel)panel).Padding = new Thickness(4);
        CssBorderRadiusTests.Layout(panel, 100, 60);
        Assert.Equal(new Thickness(4), CssBoxMetrics.ContentInsets(panel, 100));

        panel.ClearValue(panel is ToolBarPanel
            ? ToolBarPanel.PaddingProperty
            : ToolBarOverflowPanel.PaddingProperty);
        CssBorderRadiusTests.Layout(panel, 100, 60);
        Assert.Equal(new Thickness(10), CssBoxMetrics.ContentInsets(panel, 100));

        CssBorderRadiusTests.Layout(panel, 200, 60);
        Assert.Equal(new Thickness(20), CssBoxMetrics.ContentInsets(panel, 200));
    }

    [Fact]
    public void ToolBar_OverflowDecisionIncludesCssGapOnItsItemsPanel()
    {
        var toolBar = new ToolBar { Width = 90, Height = 30 };
        var first = new Button { Width = 40, Height = 20 };
        var second = new Button { Width = 40, Height = 20 };
        toolBar.Items.Add(first);
        toolBar.Items.Add(second);
        CssBorderRadiusTests.Layout(toolBar, 90, 30);
        Assert.False(toolBar.HasOverflowItems);

        var panel = Assert.IsType<ToolBarPanel>(toolBar.GetVisualChild(0));
        Css.SetStyle(panel, "row-gap:1px;column-gap:15px");
        CssBorderRadiusTests.Layout(toolBar, 90, 30);
        Assert.Equal(15, panel.GetItemSpacing(90));
        Assert.True(ToolBar.GetIsOverflowItem(second));

        Css.SetStyle(panel, "");
        CssBorderRadiusTests.Layout(toolBar, 90, 30);
        Assert.False(toolBar.HasOverflowItems);
        Assert.False(ToolBar.GetIsOverflowItem(second));

        Css.SetStyle(panel, "border:4px solid transparent;padding:3px");
        CssBorderRadiusTests.Layout(toolBar, 90, 30);
        Assert.True(ToolBar.GetIsOverflowItem(second));

        Css.SetStyle(panel, "");
        CssBorderRadiusTests.Layout(toolBar, 90, 30);
        Assert.False(toolBar.HasOverflowItems);
    }
}
