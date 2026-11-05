using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssLogicalBoxEdgeTests
{
    [Fact]
    public void LogicalMarginAndPadding_ParseOneOrTwoValuesAndMapInBothDirections()
    {
        Assert.True(Css.Supports("margin-inline", "auto 12px"));
        Assert.True(Css.Supports("margin-block", "3px"));
        Assert.True(Css.Supports("padding-inline", "2px 4px"));
        Assert.True(Css.Supports("padding-block-start", "5%"));
        Assert.False(Css.Supports("padding-inline", "auto"));
        Assert.False(Css.Supports("margin-block", "1px 2px 3px"));

        var border = new Border();
        Css.SetStyle(border, "margin-inline:10px 20px; margin-block:3px 4px; padding-inline:5px 6px; padding-block:7px 8px");
        Assert.Equal(new Thickness(10, 3, 20, 4), border.Margin);
        Assert.Equal(new Thickness(5, 7, 6, 8), border.Padding);

        Css.SetStyle(border, "direction:rtl; margin-inline:10px 20px; margin-block:3px 4px; padding-inline:5px 6px; padding-block:7px 8px");
        Assert.Equal(new Thickness(20, 3, 10, 4), border.Margin);
        Assert.Equal(new Thickness(6, 7, 5, 8), border.Padding);

        Css.SetStyle(border, "direction:rtl; margin-inline-start:auto; padding-inline:4px");
        Assert.True(border.CssLayout!.MarginRight.IsAuto);
        Assert.Equal(new Thickness(4, 0, 4, 0), border.Padding);
    }

    [Fact]
    public void LogicalEdges_CascadeWithPhysicalEdgesByOrderImportanceAndLayer()
    {
        var border = new Border();
        Css.SetStyle(border, "direction:rtl; margin-left:4px; margin-inline-end:8px; padding-right:3px; padding-inline-start:6px");
        Assert.Equal(8, border.Margin.Left);
        Assert.Equal(6, border.Padding.Right);

        Css.SetStyle(border, "direction:rtl; margin-inline-end:8px; margin-left:4px; padding-inline-start:6px; padding-right:3px");
        Assert.Equal(4, border.Margin.Left);
        Assert.Equal(3, border.Padding.Right);

        Css.SetStyle(border, "direction:rtl; margin-left:4px!important; margin-inline-end:8px; padding-inline-start:6px!important; padding-right:3px");
        Assert.Equal(4, border.Margin.Left);
        Assert.Equal(6, border.Padding.Right);

        Css.SetClass(border, "box");
        Css.SetStyle(border, "");
        Css.SetStyleSheet(border, "@layer base {.box {margin-left:5px}} @layer app {.box {margin-left:8px; margin-inline-start:revert-layer}}");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(5, border.Margin.Left);

        Css.SetStyleSheet(border, "@layer base {.box {padding-left:7px}} @layer app {.box {padding-left:9px; padding-inline-start:revert-layer}}");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(7, border.Padding.Left);
    }

    [Fact]
    public void LogicalEdges_UseComputedDirectionIncludingVariablesAndNativeValues()
    {
        var border = new Border();
        Css.SetStyle(border, "--dir:rtl; direction:var(--dir); margin-inline-start:11px");
        Assert.Equal(new Thickness(0, 0, 11, 0), border.Margin);

        Css.SetStyle(border, "direction:ltr; margin-inline-start:11px");
        Assert.Equal(new Thickness(11, 0, 0, 0), border.Margin);
        Css.SetStyle(border, "direction:rtl; margin-inline-start:11px");
        Assert.Equal(new Thickness(0, 0, 11, 0), border.Margin);

        border.FlowDirection = FlowDirection.RightToLeft;
        Css.SetStyle(border, "direction:ltr; margin-inline-start:12px");
        Assert.Equal(new Thickness(0, 0, 12, 0), border.Margin);
        border.ClearValue(FrameworkElement.FlowDirectionProperty);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(new Thickness(12, 0, 0, 0), border.Margin);

        border.Margin = new Thickness(4);
        Css.SetStyle(border, "margin-inline-start:30px");
        Assert.Equal(new Thickness(4), border.Margin);
        border.ClearValue(FrameworkElement.MarginProperty);
        Assert.Equal(new Thickness(30, 0, 0, 0), border.Margin);
    }

    [Fact]
    public void LogicalEdgeInherit_UsesTheParentsLogicalSideBeforeMappingTheChild()
    {
        var parent = new Border();
        var child = new Border();
        parent.Child = child;
        Css.SetStyle(parent, "direction:ltr; margin-inline-start:13px; padding-inline-end:17px");
        Css.SetStyle(child, "direction:rtl; margin-inline-start:inherit; padding-inline-end:inherit");
        Assert.Equal(new Thickness(0, 0, 13, 0), child.Margin);
        Assert.Equal(new Thickness(17, 0, 0, 0), child.Padding);

        Css.SetStyle(child, "margin-inline:initial; padding-inline:initial");
        Assert.Equal(default, child.Margin);
        Assert.Equal(default, child.Padding);
    }

    [Fact]
    public void LogicalPaddingPercent_ResolvesAgainstTheContainingBlockWidth()
    {
        var child = new Border();
        var root = new Border { Width = 200, Height = 100, Child = child };
        Css.SetStyle(child, "direction:rtl; padding-inline-start:10%; padding-block-start:5%");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(200, 100));
        root.Arrange(new Rect(0, 0, 200, 100));
        Assert.Equal(20, child.Padding.Right, 6);
        Assert.Equal(10, child.Padding.Top, 6);
    }

    [Fact]
    public void LogicalEdgeInherit_PreservesPercentageAndMathAcrossOppositeDirections()
    {
        var parent = new Border();
        var child = new Border();
        parent.Child = child;
        Css.SetStyle(parent, "direction:ltr; margin-inline-start:10%; padding-inline-end:calc(5% + 2px)");
        Css.SetStyle(child, "direction:rtl; margin-inline-start:inherit; padding-inline-end:inherit");

        Assert.True(child.CssLayout!.MarginRight.IsPercent);
        Assert.Equal(20, child.CssLayout.MarginRight.Resolve(200, 0), 6);
        Assert.True(child.CssLayout.PaddingLeft.IsPercent);
        Assert.Equal(12, child.CssLayout.PaddingLeft.Resolve(200, 0), 6);

        Css.SetStyle(parent, "direction:ltr; margin-inline-start:20%; padding-inline-end:calc(10% + 3px)");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal(40, child.CssLayout!.MarginRight.Resolve(200, 0), 6);
        Assert.Equal(23, child.CssLayout.PaddingLeft.Resolve(200, 0), 6);
    }
}
