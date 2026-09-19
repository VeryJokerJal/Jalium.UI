using Jalium.UI.Controls;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>FlexPanel layout algorithm: flexible lengths, wrapping, distribution, alignment.</summary>
public sealed class FlexPanelLayoutTests
{
    private static Border Fixed(double width, double height) => new() { Width = width, Height = height };

    /// <summary>A flex participant: main size comes from Basis (explicit Width would be
    /// clamped by ArrangeCore — the documented Width-vs-Basis contract).</summary>
    private static Border Flexing(double basis, double height)
    {
        var border = new Border { Height = height };
        FlexPanel.SetBasis(border, basis);
        return border;
    }

    private static FlexPanel Layout(FlexPanel panel, double width, double height)
    {
        panel.Measure(new Size(width, height));
        panel.Arrange(new Rect(0, 0, width, height));
        return panel;
    }

    [Fact]
    public void Row_Grow_TakesFreeSpace()
    {
        var a = Fixed(50, 20);
        var b = Flexing(50, 20);
        var c = Fixed(50, 20);
        FlexPanel.SetGrow(b, 1);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        panel.Children.Add(c);
        Layout(panel, 300, 100);

        Assert.Equal(50.0, a.VisualBounds.Width);
        Assert.Equal(200.0, b.VisualBounds.Width);
        Assert.Equal(50.0, c.VisualBounds.Width);
        Assert.Equal(50.0, b.VisualBounds.X);
    }

    [Fact]
    public void Grow_WeightedSplit()
    {
        var a = Flexing(50, 20);
        var b = Flexing(50, 20);
        FlexPanel.SetGrow(a, 1);
        FlexPanel.SetGrow(b, 3);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        Layout(panel, 300, 100);

        // 200 free split 1:3 → +50 / +150.
        Assert.Equal(100.0, a.VisualBounds.Width);
        Assert.Equal(200.0, b.VisualBounds.Width);
    }

    [Fact]
    public void Grow_SumBelowOne_DistributesPartialFree()
    {
        var a = Flexing(100, 20);
        FlexPanel.SetGrow(a, 0.5);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        Layout(panel, 300, 100);

        // CSS §9.7.4.b: sum(<1) scales the distributed free space.
        Assert.Equal(200.0, a.VisualBounds.Width);
    }

    [Fact]
    public void Shrink_ScaledByBasisTimesFactor()
    {
        var a = new Border { Height = 20 };
        var b = new Border { Height = 20 };
        FlexPanel.SetBasis(a, 200);
        FlexPanel.SetBasis(b, 100);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        Layout(panel, 240, 100);

        // Deficit 60 split ∝ shrink×basis (200:100) → −40 / −20.
        Assert.Equal(160.0, a.VisualBounds.Width);
        Assert.Equal(80.0, b.VisualBounds.Width);
    }

    [Fact]
    public void Shrink_MinFreezesAndRedistributes()
    {
        var a = new Border { Height = 20, MinWidth = 180 };
        var b = new Border { Height = 20 };
        FlexPanel.SetBasis(a, 200);
        FlexPanel.SetBasis(b, 200);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        Layout(panel, 300, 100);

        Assert.Equal(180.0, a.VisualBounds.Width);
        Assert.Equal(120.0, b.VisualBounds.Width);
    }

    [Fact]
    public void Grow_MaxFreezesAndRedistributes()
    {
        var a = new Border { Height = 20, MaxWidth = 120 };
        var b = new Border { Height = 20 };
        FlexPanel.SetBasis(a, 100);
        FlexPanel.SetBasis(b, 100);
        FlexPanel.SetGrow(a, 1);
        FlexPanel.SetGrow(b, 1);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        Layout(panel, 400, 100);

        Assert.Equal(120.0, a.VisualBounds.Width);
        Assert.Equal(280.0, b.VisualBounds.Width);
    }

    [Fact]
    public void Wrap_BreaksLines_AndStacksCross()
    {
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap, RowSpacing = 10 };
        for (var i = 0; i < 4; i++)
        {
            panel.Children.Add(Fixed(80, 30));
        }

        panel.Measure(new Size(200, double.PositiveInfinity));
        // Two per line (80+80=160 ≤ 200; +80 > 200) → two lines of 30 + 10 gap.
        Assert.Equal(70.0, panel.DesiredSize.Height);
        Assert.Equal(160.0, panel.DesiredSize.Width);

        panel.Arrange(new Rect(0, 0, 200, 70));
        Assert.Equal(0.0, panel.Children[0].VisualBounds.Y);
        Assert.Equal(40.0, panel.Children[2].VisualBounds.Y);
    }

    [Theory]
    [InlineData(FlexJustify.FlexStart, 0.0, 60.0)]
    [InlineData(FlexJustify.FlexEnd, 180.0, 240.0)]
    [InlineData(FlexJustify.Center, 90.0, 150.0)]
    [InlineData(FlexJustify.SpaceBetween, 0.0, 240.0)]
    [InlineData(FlexJustify.SpaceAround, 45.0, 195.0)]
    [InlineData(FlexJustify.SpaceEvenly, 60.0, 180.0)]
    public void Justify_FiveDistributions(FlexJustify justify, double firstX, double secondX)
    {
        var a = Fixed(60, 20);
        var b = Fixed(60, 20);
        var panel = new FlexPanel { JustifyContent = justify };
        panel.Children.Add(a);
        panel.Children.Add(b);
        Layout(panel, 300, 100);

        Assert.Equal(firstX, a.VisualBounds.X, 6);
        Assert.Equal(secondX, b.VisualBounds.X, 6);
    }

    [Fact]
    public void AlignItems_StretchAndCenter()
    {
        var stretch = new Border { Width = 40 };
        var centered = Fixed(40, 20);
        FlexPanel.SetAlignSelf(centered, FlexAlign.Center);
        var panel = new FlexPanel();
        panel.Children.Add(stretch);
        panel.Children.Add(centered);
        Layout(panel, 200, 100);

        // NoWrap single line fills the cross axis; Stretch spans it, Center offsets.
        Assert.Equal(100.0, stretch.VisualBounds.Height);
        Assert.Equal(40.0, centered.VisualBounds.Y);
        Assert.Equal(20.0, centered.VisualBounds.Height);
    }

    [Fact]
    public void Order_ReordersLayoutNotChildren()
    {
        var a = Fixed(50, 20);
        var b = Fixed(50, 20);
        var c = Fixed(50, 20);
        FlexPanel.SetOrder(a, 2);
        FlexPanel.SetOrder(b, 1);
        FlexPanel.SetOrder(c, 2);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        panel.Children.Add(c);
        Layout(panel, 300, 50);

        Assert.Equal(0.0, b.VisualBounds.X);
        Assert.Equal(50.0, a.VisualBounds.X);   // same order keeps document order
        Assert.Equal(100.0, c.VisualBounds.X);
        Assert.Same(a, panel.Children[0]);       // Children untouched
    }

    [Fact]
    public void RowReverse_MirrorsMainAxis()
    {
        var a = Fixed(50, 20);
        var b = Fixed(50, 20);
        var panel = new FlexPanel { Direction = FlexDirection.RowReverse };
        panel.Children.Add(a);
        panel.Children.Add(b);
        Layout(panel, 300, 50);

        Assert.Equal(250.0, a.VisualBounds.X);
        Assert.Equal(200.0, b.VisualBounds.X);
    }

    [Fact]
    public void Column_MainAxisIsVertical()
    {
        var a = Fixed(40, 30);
        var b = new Border { Width = 40 };
        FlexPanel.SetBasis(b, 30);
        FlexPanel.SetGrow(b, 1);
        var panel = new FlexPanel { Direction = FlexDirection.Column };
        panel.Children.Add(a);
        panel.Children.Add(b);
        Layout(panel, 100, 200);

        Assert.Equal(30.0, a.VisualBounds.Height);
        Assert.Equal(170.0, b.VisualBounds.Height);
        Assert.Equal(30.0, b.VisualBounds.Y);
    }

    [Fact]
    public void Gap_MainAxisUsesColumnSpacingInRow()
    {
        var a = Fixed(50, 20);
        var b = Fixed(50, 20);
        var panel = new FlexPanel { ColumnSpacing = 12 };
        panel.Children.Add(a);
        panel.Children.Add(b);
        Layout(panel, 300, 50);

        Assert.Equal(62.0, b.VisualBounds.X);
        Assert.Equal(112.0, panel.DesiredSize.Width);
    }

    [Fact]
    public void DesiredSize_IsContentBased_GrowDoesNotInflate()
    {
        var a = Fixed(100, 20);
        FlexPanel.SetGrow(a, 1);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        panel.Measure(new Size(300, 100));

        Assert.Equal(100.0, panel.DesiredSize.Width);
    }

    [Fact]
    public void DesiredSize_ReportsOverflowWhenMinsHold()
    {
        var a = new Border { Height = 20, MinWidth = 150 };
        var b = new Border { Height = 20, MinWidth = 150 };
        FlexPanel.SetBasis(a, 200);
        FlexPanel.SetBasis(b, 200);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        panel.Measure(new Size(200, 100));

        // Both freeze at 150 → 300 true overflow (a ScrollViewer must see it).
        Assert.Equal(300.0, panel.DesiredSize.Width);
    }

    [Fact]
    public void CollapsedChildren_SkippedIncludingGap()
    {
        var a = Fixed(50, 20);
        var hidden = Fixed(50, 20);
        hidden.Visibility = Visibility.Collapsed;
        var b = Fixed(50, 20);
        var panel = new FlexPanel { ColumnSpacing = 10 };
        panel.Children.Add(a);
        panel.Children.Add(hidden);
        panel.Children.Add(b);
        Layout(panel, 300, 50);

        Assert.Equal(60.0, b.VisualBounds.X);
        Assert.Equal(110.0, panel.DesiredSize.Width);
    }

    [Fact]
    public void Basis_OverridesContentSize()
    {
        var a = Fixed(50, 20);
        FlexPanel.SetBasis(a, 90);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        Layout(panel, 300, 50);

        // Basis defines the flex size; the child's own Width then aligns inside the slot.
        Assert.Equal(90.0, panel.DesiredSize.Width);
    }

    [Fact]
    public void InfiniteMain_NaturalSizesNoFlex()
    {
        var a = Fixed(50, 20);
        FlexPanel.SetGrow(a, 5);
        var panel = new FlexPanel();
        panel.Children.Add(a);
        panel.Measure(new Size(double.PositiveInfinity, 100));

        Assert.Equal(50.0, panel.DesiredSize.Width);
    }
}
