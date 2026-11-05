using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssLogicalSizeTests
{
    [Fact]
    public void LogicalSizes_MapToWidthAndHeightInHorizontalWritingMode()
    {
        Assert.True(Css.Supports("inline-size", "40px"));
        Assert.True(Css.Supports("block-size", "50%"));
        Assert.True(Css.Supports("min-inline-size", "0"));
        Assert.True(Css.Supports("min-block-size", "auto"));
        Assert.True(Css.Supports("max-inline-size", "none"));
        Assert.True(Css.Supports("max-block-size", "70px"));
        Assert.False(Css.Supports("inline-size", "10px 20px"));
        Assert.False(Css.Supports("max-block-size", "-1px"));

        var child = new Border();
        Css.SetStyle(child, "direction:rtl; inline-size:40px; block-size:30px; min-inline-size:15px; min-block-size:12px; max-inline-size:60px; max-block-size:50px");
        Assert.Equal(40, child.Width);
        Assert.Equal(30, child.Height);
        Assert.Equal(15, child.MinWidth);
        Assert.Equal(12, child.MinHeight);
        Assert.Equal(60, child.MaxWidth);
        Assert.Equal(50, child.MaxHeight);

        Css.SetStyle(child, "min-inline-size:30px; min-inline-size:initial; max-block-size:20px; max-block-size:initial");
        Assert.Equal(0, child.MinWidth);
        Assert.Equal(double.PositiveInfinity, child.MaxHeight);
    }

    [Fact]
    public void LogicalSizes_ShareSizingStateForPercentAndMathValues()
    {
        var child = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var root = new Border { Width = 300, Height = 200, Child = child };
        Css.SetStyle(child, "inline-size:calc(50% + 10px); block-size:25%");
        Layout(root);
        Assert.Equal(160, child.ActualWidth, 6);
        Assert.Equal(50, child.ActualHeight, 6);

        Css.SetStyle(child, "inline-size:40px; min-inline-size:100px; block-size:100px; max-block-size:60px");
        Assert.Equal(40, child.Width);
        Assert.Equal(100, child.MinWidth);
        Assert.Equal(100, child.Height);
        Assert.Equal(60, child.MaxHeight);
        root.InvalidateMeasure();
        Layout(root);
        Assert.Equal(100, child.ActualWidth, 6);
        Assert.Equal(60, child.ActualHeight, 6);

        Css.SetStyle(child, "inline-size:200px; max-inline-size:25%; block-size:20px; min-block-size:40%");
        root.InvalidateMeasure();
        Layout(root);
        Assert.Equal(75, child.ActualWidth, 6);
        Assert.Equal(80, child.ActualHeight, 6);
    }

    [Fact]
    public void LogicalAndPhysicalSizes_CascadeTogetherIncludingRevertLayer()
    {
        var child = new Border { Name = "hero" };
        Css.SetClass(child, "box");

        Css.SetStyle(child, "width:20px; inline-size:30px; height:40px; block-size:50px");
        Assert.Equal(30, child.Width);
        Assert.Equal(50, child.Height);
        Css.SetStyle(child, "inline-size:30px; width:20px; block-size:50px; height:40px");
        Assert.Equal(20, child.Width);
        Assert.Equal(40, child.Height);
        Css.SetStyle(child, "width:20px!important; inline-size:30px; min-width:10px; min-inline-size:15px");
        Assert.Equal(20, child.Width);
        Assert.Equal(15, child.MinWidth);
        Css.SetStyle(child, "width:20px; inline-size:initial");
        Assert.True(double.IsNaN(child.Width));

        Css.SetStyle(child, "");
        Css.SetStyleSheet(child, "@layer base {.box {width:25px}} @layer app {.box {width:40px; inline-size:revert-layer}}");
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.Equal(25, child.Width);
        Css.SetStyleSheet(child, "#hero {inline-size:35px} .box {width:20px}");
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.Equal(35, child.Width);
    }

    [Fact]
    public void LogicalSizes_InheritResetAndRespectNativeLocalValues()
    {
        var parent = new Border { Width = 120 };
        var child = new Border();
        parent.Child = child;
        Css.SetStyle(child, "inline-size:inherit");
        Assert.Equal(120, child.Width);
        Css.SetStyle(child, "inline-size:50px; all:initial");
        Assert.True(double.IsNaN(child.Width));

        child.Width = 90;
        Css.SetStyle(child, "inline-size:40px");
        Assert.Equal(90, child.Width);
        child.ClearValue(FrameworkElement.WidthProperty);
        Assert.Equal(40, child.Width);

        var grandparent = new Border { Width = 400, Height = 200 };
        var percentParent = new Border();
        var percentChild = new Border { HorizontalAlignment = HorizontalAlignment.Left };
        grandparent.Child = percentParent;
        percentParent.Child = percentChild;
        Css.SetStyle(percentParent, "width:50%");
        Css.SetStyle(percentChild, "inline-size:inherit");
        Layout(grandparent);
        Assert.Equal(200, percentParent.ActualWidth, 6);
        Assert.Equal(100, percentChild.ActualWidth, 6);

        Css.SetStyle(percentParent, "width:25%");
        grandparent.InvalidateMeasure();
        Layout(grandparent);
        Assert.Equal(100, percentParent.ActualWidth, 6);
        Assert.Equal(25, percentChild.ActualWidth, 6);
    }

    private static void Layout(FrameworkElement root)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(400, 200));
        root.Arrange(new Rect(0, 0, 400, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(400, 200));
        root.Arrange(new Rect(0, 0, 400, 200));
    }
}
