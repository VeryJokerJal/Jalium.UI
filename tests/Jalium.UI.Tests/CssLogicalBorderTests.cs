using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssLogicalBorderTests
{
    [Fact]
    public void LogicalBorderWidths_ParseAndMapInBothDirections()
    {
        Assert.True(Css.Supports("border-inline-width", "thin thick"));
        Assert.True(Css.Supports("border-block-end-width", "4px"));
        Assert.False(Css.Supports("border-inline-width", "1px 2px 3px"));
        Assert.False(Css.Supports("border-inline-start-width", "-1px"));
        Assert.False(Css.Supports("border-inline-start-width", "10%"));
        Assert.False(Css.Supports("border-left-width", "10%"));
        Assert.False(Css.Supports("border-left-width", "2"));

        var border = new Border();
        Css.SetStyle(border, "direction:ltr; border-inline-width:2px 4px; border-block-width:6px 8px; border-style:solid");
        Assert.Equal(new Thickness(2, 6, 4, 8), border.BorderThickness);

        Css.SetStyle(border, "direction:rtl; border-inline-width:2px 4px; border-block-width:6px 8px; border-style:solid");
        Assert.Equal(new Thickness(4, 6, 2, 8), border.BorderThickness);
    }

    [Fact]
    public void LogicalBorderWidths_CascadeWithPhysicalWidthsAndLayers()
    {
        var border = new Border();
        Css.SetStyle(border, "direction:rtl; border-right-width:3px; border-inline-start-width:7px; border-right-style:solid");
        Assert.Equal(7, border.BorderThickness.Right);
        Css.SetStyle(border, "direction:rtl; border-inline-start-width:7px; border-right-width:3px; border-right-style:solid");
        Assert.Equal(3, border.BorderThickness.Right);
        Css.SetStyle(border, "direction:rtl; border-right-width:3px!important; border-inline-start-width:7px; border-right-style:solid");
        Assert.Equal(3, border.BorderThickness.Right);

        Css.SetClass(border, "box");
        Css.SetStyle(border, "");
        Css.SetStyleSheet(border,
            "@layer base {.box {border-left-width:5px; border-left-style:solid}} " +
            "@layer app {.box {border-left-width:8px; border-inline-start-width:revert-layer}}");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(5, border.BorderThickness.Left);

        border.BorderThickness = new Thickness(9);
        Css.SetStyle(border, "border-inline-start-width:20px; border-inline-start-style:solid");
        Assert.Equal(new Thickness(9), border.BorderThickness);
        border.ClearValue(Border.BorderThicknessProperty);
        Assert.Equal(20, border.BorderThickness.Left);
    }

    [Fact]
    public void LogicalBorderWidthInherit_UsesParentsLogicalSide()
    {
        var parent = new Border();
        var child = new Border();
        parent.Child = child;
        Css.SetStyle(parent, "direction:ltr; border-inline-start-width:11px; border-inline-start-style:solid");
        Css.SetStyle(child, "direction:rtl; border-inline-start-width:inherit; border-inline-start-style:solid");
        Assert.Equal(11, child.BorderThickness.Right);
        Assert.Equal(0, child.BorderThickness.Left);

        parent.BorderThickness = new Thickness(15, 0, 20, 0);
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal(15, child.BorderThickness.Right);
        parent.ClearValue(Border.BorderThicknessProperty);
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal(11, child.BorderThickness.Right);

        Css.SetStyle(child, "");
        Assert.Empty(CssNode.Get(child).CssRuntimeState!.InheritedBoxObservers!);
    }

    [Fact]
    public void LogicalCornerRadii_MapCascadeAndClipUnderRtl()
    {
        Assert.True(Css.Supports("border-start-start-radius", "30px 20px"));
        Assert.False(Css.Supports("border-start-start-radius", "auto"));

        var border = new Border();
        Css.SetStyle(border,
            "direction:rtl; border-start-start-radius:30px 20px; " +
            "border-start-end-radius:11px; border-end-start-radius:12px; border-end-end-radius:13px");
        var radius = Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(border));
        Assert.Equal(new Size(11, 11), radius.TopLeft.Resolve(new Size(100, 100)));
        Assert.Equal(new Size(30, 20), radius.TopRight.Resolve(new Size(100, 100)));
        Assert.Equal(new Size(12, 12), radius.BottomRight.Resolve(new Size(100, 100)));
        Assert.Equal(new Size(13, 13), radius.BottomLeft.Resolve(new Size(100, 100)));

        Css.SetStyle(border,
            "direction:rtl; background-color:lime; border-top-right-radius:4px; " +
            "border-start-start-radius:30px 20px");
        border.Measure(new Size(100, 100));
        border.Arrange(new Rect(0, 0, 100, 100));
        Assert.Null(border.HitTest(new Point(99, 1)));
        Assert.Same(border, border.HitTest(new Point(50, 1))?.VisualHit);
        Assert.Equal(new Size(30, 20),
            Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(border)).TopRight.Resolve(new Size(100, 100)));

        Css.SetStyle(border, "direction:rtl; border-start-start-radius:30px 20px; border-top-right-radius:4px");
        Assert.Equal(new Size(4, 4),
            Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(border)).TopRight.Resolve(new Size(100, 100)));
    }

    [Fact]
    public void LogicalCornerInherit_PreservesPercentAcrossOppositeDirections()
    {
        var parent = new Border();
        var child = new Border();
        parent.Child = child;
        Css.SetStyle(parent, "direction:ltr; border-start-start-radius:20% 25%");
        Css.SetStyle(child, "direction:rtl; border-start-start-radius:inherit");
        var radius = Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(child));
        Assert.Equal(new Size(20, 25), radius.TopRight.Resolve(new Size(100, 100)));
        Assert.Equal(default, radius.TopLeft.Resolve(new Size(100, 100)));

        Css.SetStyle(parent, "direction:ltr; border-start-start-radius:30% 40%");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        radius = Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(child));
        Assert.Equal(new Size(30, 40), radius.TopRight.Resolve(new Size(100, 100)));

        parent.CornerRadius = new CornerRadius(7, 8, 9, 10);
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        radius = Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(child));
        Assert.Equal(new Size(7, 7), radius.TopRight.Resolve(new Size(100, 100)));
    }

    [Fact]
    public void LogicalCorners_UseVariablesLayersAndNativeLocalPriority()
    {
        var border = new Border();
        Css.SetStyle(border, "--dir:rtl; --radius:14px 9px; direction:var(--dir); border-start-start-radius:var(--radius)");
        var radius = Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(border));
        Assert.Equal(new Size(14, 9), radius.TopRight.Resolve(new Size(100, 100)));

        Css.SetClass(border, "rounded");
        Css.SetStyle(border, "");
        Css.SetStyleSheet(border,
            "@layer base {.rounded {border-top-left-radius:6px}} " +
            "@layer app {.rounded {border-top-left-radius:12px; border-start-start-radius:revert-layer}}");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        radius = Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(border));
        Assert.Equal(new Size(6, 6), radius.TopLeft.Resolve(new Size(100, 100)));

        border.CornerRadius = new CornerRadius(3);
        Assert.Null(CssBorderRadiusProperties.Get(border));
        border.ClearValue(Border.CornerRadiusProperty);
        Assert.NotNull(CssBorderRadiusProperties.Get(border));

        Css.SetStyleSheet(border, null);
        Css.SetStyle(border, "border-start-start-radius:20px; all:initial");
        radius = Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(border));
        Assert.Equal(default, radius.TopLeft.Resolve(new Size(100, 100)));
    }

    [Fact]
    public void LogicalCornerInherit_UpdatesWhenAnEqualNativeRadiusTakesOver()
    {
        var parent = new Border();
        var child = new Border();
        parent.Child = child;
        Css.SetStyle(parent, "direction:ltr; border-start-start-radius:20px 30px");
        Css.SetStyle(child, "direction:rtl; border-start-start-radius:inherit");
        Assert.Equal(new Size(20, 30),
            Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(child)).TopRight.Resolve(new Size(100, 100)));

        parent.CornerRadius = new CornerRadius(20, 0, 0, 0);
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal(new Size(20, 20),
            Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(child)).TopRight.Resolve(new Size(100, 100)));
        parent.ClearValue(Border.CornerRadiusProperty);
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal(new Size(20, 30),
            Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(child)).TopRight.Resolve(new Size(100, 100)));
    }
}
