using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssLogicalInsetTests
{
    private static void Layout(FrameworkElement element, double width = 200, double height = 100)
    {
        // Headless tests have no LayoutManager to propagate child invalidation.
        element.InvalidateMeasure();
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void LogicalInsets_ParseAndPositionAbsoluteChildrenInBothDirections()
    {
        Assert.True(Css.Supports("inset-inline", "10px auto"));
        Assert.True(Css.Supports("inset-block", "5% 2px"));
        Assert.True(Css.Supports("inset-inline-start", "calc(10% + 2px)"));
        Assert.False(Css.Supports("inset-inline", "1px 2px 3px"));
        Assert.False(Css.Supports("inset-block-start", "red"));

        var child = new Border { Width = 40, Height = 20 };
        var parent = new StackPanel();
        parent.Children.Add(child);

        Css.SetStyle(child, "position:absolute; direction:ltr; inset-inline-start:10px; inset-block-start:15px");
        Layout(parent);
        Assert.Equal(10, child.VisualBounds.X);
        Assert.Equal(15, child.VisualBounds.Y);

        Css.SetStyle(child, "position:absolute; direction:rtl; inset-inline-start:10px; inset-block-start:15px");
        Layout(parent);
        Assert.Equal(150, child.VisualBounds.X);
        Assert.Equal(15, child.VisualBounds.Y);

        Css.SetStyle(child, "position:absolute; direction:rtl; inset-inline:10px 30px; inset-block:5px 7px");
        Layout(parent);
        Assert.Equal(30, child.VisualBounds.X);
        Assert.Equal(5, child.VisualBounds.Y);
    }

    [Fact]
    public void LogicalInsets_CascadeWithPhysicalEdgesAndLayers()
    {
        var child = new Border { Width = 40, Height = 20 };
        var parent = new StackPanel();
        parent.Children.Add(child);
        Css.SetStyle(child, "position:absolute; direction:rtl; right:20px; inset-inline-start:10px");
        Layout(parent);
        Assert.Equal(150, child.VisualBounds.X);

        Css.SetStyle(child, "position:absolute; direction:rtl; inset-inline-start:10px; right:20px");
        Layout(parent);
        Assert.Equal(140, child.VisualBounds.X);

        Css.SetStyle(child, "position:absolute; direction:rtl; right:20px!important; inset-inline-start:10px");
        Layout(parent);
        Assert.Equal(140, child.VisualBounds.X);

        Css.SetClass(child, "placed");
        Css.SetStyle(child, "");
        Css.SetStyleSheet(parent,
            "@layer base {.placed {position:absolute; direction:rtl; right:25px}} " +
            "@layer app {.placed {right:10px; inset-inline-start:revert-layer}}");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Layout(parent);
        Assert.Equal(135, child.VisualBounds.X);
    }

    [Fact]
    public void LogicalInsets_AutoAndAllInitialClearOffsets()
    {
        var child = new Border { Width = 40, Height = 20 };
        var parent = new StackPanel();
        parent.Children.Add(child);
        Css.SetStyle(child, "position:absolute; inset-inline:10px 30px");
        Layout(parent);
        Assert.Equal(10, child.VisualBounds.X);

        Css.SetStyle(child, "position:absolute; inset-inline:auto 30px");
        Layout(parent);
        Assert.Equal(130, child.VisualBounds.X);

        Css.SetStyle(child, "right:30px; all:initial; position:absolute; width:40px; height:20px");
        Layout(parent);
        Assert.Equal(0, child.VisualBounds.X);
    }

    [Fact]
    public void LogicalInsetInherit_PreservesParentPercentageAcrossOppositeDirections()
    {
        var outer = new StackPanel();
        var parent = new StackPanel { Width = 100, Height = 50 };
        var child = new Border { Width = 20, Height = 10 };
        outer.Children.Add(parent);
        parent.Children.Add(child);
        Css.SetStyle(parent, "position:absolute; direction:ltr; inset-inline-start:10%; inset-block-start:5px");
        Css.SetStyle(child, "position:absolute; direction:rtl; inset-inline-start:inherit; inset-block-start:inherit");
        CssEvaluationScheduler.FlushIfPending(outer.Dispatcher);
        Assert.True(child.CssLayout!.InsetRight.IsPercent);
        Assert.Equal(10, child.CssLayout.InsetRight.Resolve(100, 0), 6);
        Assert.Equal(5, child.CssLayout.InsetTop.Resolve(50, 0), 6);
        Layout(outer);
        Assert.Equal(70, child.VisualBounds.X);

        Css.SetStyle(parent, "position:absolute; direction:ltr; inset-inline-start:20%; inset-block-start:5px");
        CssEvaluationScheduler.FlushIfPending(outer.Dispatcher);
        Assert.Equal(20, child.CssLayout!.InsetRight.Resolve(100, 0), 6);
    }

    [Fact]
    public void LogicalInsets_KeepStaticCanvasCompatibilityAndRtlOverconstraint()
    {
        var child = new Border { Width = 40, Height = 20 };
        var canvas = new Canvas();
        canvas.Children.Add(child);
        Css.SetStyle(child, "direction:rtl; inset-inline-start:15px; inset-block-start:25px");
        Layout(canvas);
        Assert.Equal(15, Canvas.GetRight(child));
        Assert.Equal(25, Canvas.GetTop(child));

        Css.SetStyle(child, "inset:5px 10px 15px 20px");
        Assert.Equal(20, Canvas.GetLeft(child));
        Assert.Equal(5, Canvas.GetTop(child));
        child.SetValue(Canvas.LeftProperty, 8.0);
        Assert.Equal(8, Canvas.GetLeft(child));
        child.ClearValue(Canvas.LeftProperty);
        Assert.Equal(20, Canvas.GetLeft(child));

        Css.SetStyle(child, "position:absolute; direction:rtl; left:10px; right:30px; top:0");
        Layout(canvas);
        Assert.Equal(10, child.VisualBounds.X);
        canvas.FlowDirection = FlowDirection.RightToLeft;
        Layout(canvas);
        Assert.Equal(130, child.VisualBounds.X);
    }

    [Fact]
    public void LogicalInsets_UseComputedDirectionAndContainingBlockPercentageBases()
    {
        var child = new Border { Width = 40, Height = 20 };
        var parent = new StackPanel();
        parent.Children.Add(child);
        Css.SetStyle(child,
            "--side:rtl; position:absolute; direction:var(--side); " +
            "inset-inline-start:calc(10% + 2px); inset-block-start:25%");
        Layout(parent);
        Assert.Equal(138, child.VisualBounds.X);
        Assert.Equal(25, child.VisualBounds.Y);

        child.FlowDirection = FlowDirection.LeftToRight;
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Layout(parent);
        Assert.Equal(22, child.VisualBounds.X);

        child.ClearValue(FrameworkElement.FlowDirectionProperty);
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Layout(parent);
        Assert.Equal(138, child.VisualBounds.X);
    }

    [Fact]
    public void NativeLocalCanvasInset_OutranksCssAbsoluteOffset()
    {
        var child = new Border { Width = 40, Height = 20 };
        var parent = new StackPanel();
        parent.Children.Add(child);
        Css.SetStyle(child, "position:absolute; direction:rtl; inset-inline-start:10px; top:0");
        Layout(parent);
        Assert.Equal(150, child.VisualBounds.X);

        Canvas.SetRight(child, 25);
        Layout(parent);
        Assert.Equal(135, child.VisualBounds.X);

        child.ClearValue(Canvas.RightProperty);
        Layout(parent);
        Assert.Equal(150, child.VisualBounds.X);

        Css.SetStyle(child, "position:absolute; direction:rtl");
        Canvas.SetLeft(child, 10);
        Canvas.SetRight(child, 30);
        Layout(parent);
        Assert.Equal(10, child.VisualBounds.X);
        parent.FlowDirection = FlowDirection.RightToLeft;
        Layout(parent);
        Assert.Equal(130, child.VisualBounds.X);
    }
}
