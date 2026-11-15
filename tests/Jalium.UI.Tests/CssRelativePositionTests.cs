using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssRelativePositionTests
{
    private static void Layout(FrameworkElement element, double width = 200, double height = 100)
    {
        element.InvalidateMeasure();
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void RelativeInsets_MoveVisualBoxWithoutTakingFlowSpace()
    {
        var parent = new StackPanel();
        var first = new Border { Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Red };
        var second = new Border { Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
        parent.Children.Add(first);
        parent.Children.Add(second);

        Layout(parent);
        var original = first.VisualBounds;
        var following = second.VisualBounds;

        Assert.True(Css.Supports("position", "relative"));
        Css.SetStyle(first, "position:relative; left:15px; top:6px");
        Layout(parent);

        Assert.Equal(original.X + 15, first.VisualBounds.X);
        Assert.Equal(original.Y + 6, first.VisualBounds.Y);
        Assert.Equal(original.Size, first.VisualBounds.Size);
        Assert.Equal(following, second.VisualBounds);
        Assert.Equal(40, parent.DesiredSize.Height);
        Assert.Same(first, parent.InputHitTest(new Point(16, 7)));
        Assert.NotSame(first, parent.InputHitTest(new Point(1, 1)));

        Css.SetStyle(first, "");
        Layout(parent);
        Assert.Equal(original, first.VisualBounds);
        Assert.Equal(following, second.VisualBounds);
    }

    [Fact]
    public void RelativeInsets_ResolvePercentagesAgainstParentAndOpposingEdges()
    {
        var parent = new StackPanel();
        var child = new Border { Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
        parent.Children.Add(child);
        Layout(parent);
        var original = child.VisualBounds;

        Css.SetStyle(child, "position:relative; left:10%; right:30px; top:25%; bottom:5px");
        Layout(parent);
        Assert.Equal(original.X + 20, child.VisualBounds.X);
        Assert.Equal(original.Y + 25, child.VisualBounds.Y);

        parent.FlowDirection = FlowDirection.RightToLeft;
        Layout(parent);
        Assert.Equal(original.X - 30, child.VisualBounds.X);
        Assert.Equal(original.Y + 25, child.VisualBounds.Y);

        Css.SetStyle(child, "position:relative; right:10%; bottom:25%");
        Layout(parent);
        Assert.Equal(original.X - 20, child.VisualBounds.X);
        Assert.Equal(original.Y - 25, child.VisualBounds.Y);

        Layout(parent, 300, 160);
        Assert.Equal(original.X - 30, child.VisualBounds.X);
        Assert.Equal(original.Y - 40, child.VisualBounds.Y);
    }

    [Fact]
    public void RelativeLogicalInsets_UseComputedDirectionAndNativeCanvasPriority()
    {
        var parent = new Canvas();
        var child = new Border { Width = 40, Height = 20 };
        parent.Children.Add(child);
        Layout(parent);

        Css.SetStyle(child, "position:relative; direction:rtl; inset-inline-start:calc(10% + 2px); inset-block-start:5px");
        Layout(parent);
        Assert.Equal(-22, child.VisualBounds.X);
        Assert.Equal(5, child.VisualBounds.Y);

        Canvas.SetRight(child, 25);
        Layout(parent);
        Assert.Equal(135, child.VisualBounds.X);
        Assert.Equal(5, child.VisualBounds.Y);

        child.ClearValue(Canvas.RightProperty);
        Layout(parent);
        Assert.NotNull(child.CssLayout);
        Assert.Equal(CssPositionMode.Relative, child.CssLayout.Position);
        Assert.True(child.CssLayout.InsetRight.IsSet);
        Assert.False(child.HasLocalOrAnimatedValue(Canvas.RightProperty));
        Assert.Equal(-22, child.VisualBounds.X);
    }

    [Fact]
    public void RelativePercentInsets_UseCssFormatterContentBox()
    {
        var parent = new StackPanel();
        var child = new Border { Width = 40, Height = 20 };
        parent.Children.Add(child);
        Css.SetStyle(parent, "display:flex; padding:10px; align-items:flex-start");
        Layout(parent);
        var normal = child.VisualBounds;

        Css.SetStyle(child, "position:relative; left:10%; top:25%");
        Layout(parent);
        Assert.Equal(normal.X + 18, child.VisualBounds.X);
        Assert.Equal(normal.Y + 20, child.VisualBounds.Y);
    }
}
