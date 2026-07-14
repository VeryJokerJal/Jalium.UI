using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class ToolBarParityTests
{
    [Fact]
    public void ToolBarExposesStableStyleKeysAndReadOnlyOverflowState()
    {
        Assert.Same(ToolBar.ButtonStyleKey, ToolBar.ButtonStyleKey);
        Assert.Same(ToolBar.CheckBoxStyleKey, ToolBar.CheckBoxStyleKey);
        Assert.Same(ToolBar.ComboBoxStyleKey, ToolBar.ComboBoxStyleKey);
        Assert.Same(ToolBar.MenuStyleKey, ToolBar.MenuStyleKey);
        Assert.Same(ToolBar.RadioButtonStyleKey, ToolBar.RadioButtonStyleKey);
        Assert.Same(ToolBar.SeparatorStyleKey, ToolBar.SeparatorStyleKey);
        Assert.Same(ToolBar.TextBoxStyleKey, ToolBar.TextBoxStyleKey);
        Assert.Same(ToolBar.ToggleButtonStyleKey, ToolBar.ToggleButtonStyleKey);
        Assert.True(ToolBar.HasOverflowItemsProperty.ReadOnly);
        Assert.True(ToolBar.IsOverflowItemProperty.ReadOnly);
        Assert.True(ToolBar.OrientationProperty.ReadOnly);
        Assert.Throws<ArgumentException>(() =>
            new Button().SetValue(ToolBar.OverflowModeProperty, (OverflowMode)99));
    }

    [Fact]
    public void OverflowModesReparentItemsDuringMeasureAndKeepMainPanelCompact()
    {
        var never = new Button { Width = 70, Height = 20 };
        var asNeeded = new Button { Width = 50, Height = 20 };
        var always = new Button { Width = 10, Height = 20 };
        ToolBar.SetOverflowMode(never, OverflowMode.Never);
        ToolBar.SetOverflowMode(always, OverflowMode.Always);
        var toolBar = new ToolBar
        {
            Width = 100,
            Height = 30
        };
        toolBar.Items.Add(never);
        toolBar.Items.Add(asNeeded);
        toolBar.Items.Add(always);

        toolBar.Measure(new Size(100, 30));

        Assert.True(toolBar.HasOverflowItems);
        Assert.False(ToolBar.GetIsOverflowItem(never));
        Assert.True(ToolBar.GetIsOverflowItem(asNeeded));
        Assert.True(ToolBar.GetIsOverflowItem(always));

        var mainPanel = Assert.IsType<ToolBarPanel>(toolBar.GetVisualChild(0));
        var overflowPanel = Assert.IsType<ToolBarOverflowPanel>(toolBar.GetVisualChild(1));
        Assert.Single(mainPanel.Children);
        Assert.Same(never, mainPanel.Children[0]);
        Assert.Equal(
            new UIElement[] { asNeeded, always },
            overflowPanel.Children.Cast<UIElement>().ToArray());
        Assert.Same(mainPanel, never.VisualParent);
        Assert.Same(overflowPanel, asNeeded.VisualParent);

        toolBar.IsOverflowOpen = true;
        toolBar.Arrange(new Rect(0, 0, 100, 30));
        Assert.Equal(Visibility.Visible, overflowPanel.Visibility);
        Assert.Equal(70, never.RenderSize.Width);
        Assert.True(asNeeded.RenderSize.Width > 0);

        ToolBar.SetOverflowMode(asNeeded, OverflowMode.Never);
        toolBar.Measure(new Size(100, 30));
        Assert.Equal(
            new UIElement[] { never, asNeeded },
            mainPanel.Children.Cast<UIElement>().ToArray());
        Assert.Single(overflowPanel.Children);
        Assert.Same(always, overflowPanel.Children[0]);
    }

    [Fact]
    public void VerticalOrientationUsesHeightForOverflowDecisions()
    {
        var retained = new Button { Width = 20, Height = 50 };
        var overflow = new Button { Width = 20, Height = 40 };
        ToolBar.SetOverflowMode(retained, OverflowMode.Never);
        var toolBar = new ToolBar();
        var tray = new ToolBarTray { Orientation = Orientation.Vertical };
        tray.ToolBars.Add(toolBar);
        toolBar.Items.Add(retained);
        toolBar.Items.Add(overflow);

        toolBar.Measure(new Size(30, 80));

        Assert.False(ToolBar.GetIsOverflowItem(retained));
        Assert.True(ToolBar.GetIsOverflowItem(overflow));
        Assert.True(toolBar.HasOverflowItems);
        Assert.Equal(Orientation.Vertical, toolBar.Orientation);
    }

    [Fact]
    public void TrayLockedAttachedPropertyRoundTripsAndInherits()
    {
        var tray = new ToolBarTray();
        var toolBar = new ToolBar();
        tray.ToolBars.Add(toolBar);

        ToolBarTray.SetIsLocked(tray, true);

        Assert.True(ToolBarTray.GetIsLocked(tray));
        Assert.True(ToolBarTray.GetIsLocked(toolBar));
    }
}
