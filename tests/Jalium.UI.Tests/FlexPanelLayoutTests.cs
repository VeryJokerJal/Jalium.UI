using Jalium.UI.Controls;
using Jalium.UI.Styling;
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

    [Fact]
    public void Wrap_WithMainGapReportsEachLineOnce()
    {
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap, ColumnSpacing = 10 };
        panel.Children.Add(Fixed(40, 20));
        panel.Children.Add(Fixed(40, 20));
        panel.Children.Add(Fixed(40, 20));

        panel.Measure(new Size(100, double.PositiveInfinity));
        Assert.Equal(90, panel.DesiredSize.Width, 6);
        Assert.Equal(40, panel.DesiredSize.Height, 6);
        panel.Arrange(new Rect(0, 0, 100, 40));
        Assert.Equal(50, panel.Children[1].VisualBounds.X, 6);
        Assert.Equal(20, panel.Children[2].VisualBounds.Y, 6);
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
    public void Baseline_UsesAscentAndDescentForLineCrossSize()
    {
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 25);
        var panel = new FlexPanel { AlignItems = FlexAlign.Baseline };
        panel.Children.Add(first);
        panel.Children.Add(second);

        Layout(panel, 100, 35);

        Assert.Equal(35, panel.DesiredSize.Height, 6);
        Assert.Equal(15, first.VisualBounds.Y, 6);
        Assert.Equal(0, second.VisualBounds.Y, 6);
    }

    [Fact]
    public void Baseline_SynthesizesBorderEdgeAndHonorsCrossMargins()
    {
        var text = Fixed(40, 20);
        TextBlock.SetBaselineOffset(text, 10);
        var box = Fixed(40, 30);
        box.Margin = new Thickness(0, 3, 0, 7);
        var panel = new FlexPanel { AlignItems = FlexAlign.Baseline, Wrap = FlexWrap.Wrap };
        panel.Children.Add(text);
        panel.Children.Add(box);

        Layout(panel, 100, 40);

        // The synthesized baseline sits at the border bottom (3 + 30),
        // leaving the 7px bottom margin below the shared baseline.
        Assert.Equal(43, panel.DesiredSize.Height, 6);
        Assert.Equal(23, text.VisualBounds.Y, 6);
        Assert.Equal(3, box.VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData("last baseline")]
    [InlineData("baseline last")]
    public void CssLastBaseline_UsesTheLastSharingGroupAndIntrinsicLineSize(string value)
    {
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 25);
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap };
        Css.SetStyle(panel, $"align-items:{value}");
        panel.Children.Add(first);
        panel.Children.Add(second);

        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        panel.Measure(new Size(100, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 100, panel.DesiredSize.Height));

        Assert.Equal(FlexAlign.LastBaseline, panel.AlignItems);
        Assert.Equal(35, panel.DesiredSize.Height, 6);
        Assert.Equal(15, first.VisualBounds.Y, 6);
        Assert.Equal(0, second.VisualBounds.Y, 6);
        Assert.Equal(first.VisualBounds.Y + 10, second.VisualBounds.Y + 25, 6);
    }

    [Fact]
    public void CssFlexHost_AlignsLastBaselinesWithoutReparenting()
    {
        var host = new StackPanel();
        Css.SetStyle(host, "display:flex; align-items:last baseline; flex-wrap:wrap; width:80px");
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 25);
        host.Children.Add(first);
        host.Children.Add(second);

        CssEvaluationScheduler.FlushIfPending(host.Dispatcher);
        host.Measure(new Size(80, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, 80, host.DesiredSize.Height));

        Assert.Equal(35, host.DesiredSize.Height, 6);
        Assert.Equal(first.VisualBounds.Y + 10, second.VisualBounds.Y + 25, 6);
        Assert.Same(host, first.Parent);
        Assert.Same(host, second.Parent);
    }

    [Fact]
    public void MixedFlexBaselinePreferences_FormSeparateGroups()
    {
        var first = Fixed(40, 20);
        var firstPeer = Fixed(40, 30);
        var last = Fixed(40, 20);
        var lastPeer = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 5);
        TextBlock.SetBaselineOffset(firstPeer, 25);
        TextBlock.SetBaselineOffset(last, 10);
        TextBlock.SetBaselineOffset(lastPeer, 25);
        Css.SetStyle(first, "align-self:first baseline");
        Css.SetStyle(firstPeer, "align-self:baseline");
        Css.SetStyle(last, "align-self:last baseline");
        Css.SetStyle(lastPeer, "align-self:baseline last");
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap, AlignItems = FlexAlign.FlexStart };
        panel.Children.Add(first);
        panel.Children.Add(firstPeer);
        panel.Children.Add(last);
        panel.Children.Add(lastPeer);

        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        panel.Measure(new Size(160, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 160, panel.DesiredSize.Height));

        Assert.Equal(40, panel.DesiredSize.Height, 6);
        Assert.Equal(20, first.VisualBounds.Y, 6);
        Assert.Equal(0, firstPeer.VisualBounds.Y, 6);
        Assert.Equal(20, last.VisualBounds.Y, 6);
        Assert.Equal(5, lastPeer.VisualBounds.Y, 6);
        Assert.Equal(first.VisualBounds.Y + 5, firstPeer.VisualBounds.Y + 25, 6);
        Assert.Equal(last.VisualBounds.Y + 10, lastPeer.VisualBounds.Y + 25, 6);
    }

    [Fact]
    public void NestedFlex_ExportsItsSharedLastLineBaseline()
    {
        var inner = new FlexPanel { Width = 80, Wrap = FlexWrap.Wrap, AlignItems = FlexAlign.LastBaseline };
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 25);
        inner.Children.Add(first);
        inner.Children.Add(second);
        var peer = Fixed(40, 20);
        TextBlock.SetBaselineOffset(peer, 15);
        var outer = new FlexPanel { AlignItems = FlexAlign.LastBaseline };
        outer.Children.Add(inner);
        outer.Children.Add(peer);

        Layout(outer, 120, 80);

        Assert.Equal(inner.VisualBounds.Y + first.VisualBounds.Y + 10,
            peer.VisualBounds.Y + 15, 6);
        Assert.Equal(inner.VisualBounds.Y + second.VisualBounds.Y + 25,
            peer.VisualBounds.Y + 15, 6);
    }

    [Fact]
    public void WrapReverseLastBaseline_AnchorsTheGroupAtPhysicalTop()
    {
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 25);
        var panel = new FlexPanel { Wrap = FlexWrap.WrapReverse, AlignItems = FlexAlign.LastBaseline };
        panel.Children.Add(first);
        panel.Children.Add(second);

        Layout(panel, 80, 80);

        Assert.Equal(15, first.VisualBounds.Y, 6);
        Assert.Equal(0, second.VisualBounds.Y, 6);
        Assert.Equal(first.VisualBounds.Y + 10, second.VisualBounds.Y + 25, 6);
    }

    [Fact]
    public void MultiLineText_UsesItsLastLineForLastBaselineAlignment()
    {
        var text = new TextBlock { Text = "A\nB", Width = 40, FontSize = 16 };
        var peer = new TextBlock { Text = "A", Width = 40, FontSize = 16 };
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap, AlignItems = FlexAlign.LastBaseline };
        panel.Children.Add(text);
        panel.Children.Add(peer);

        panel.Measure(new Size(80, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 80, panel.DesiredSize.Height));

        var last = text.GetLastBaselineOffset(text.VisualBounds.Height);
        var first = text.GetFirstBaselineOffset(text.VisualBounds.Height);
        Assert.True(last > first);
        Assert.Equal(text.VisualBounds.Y + last,
            peer.VisualBounds.Y + peer.GetLastBaselineOffset(peer.VisualBounds.Height), 1);
    }

    [Fact]
    public void Baseline_UsesResolvedCssMargins()
    {
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        Css.SetStyle(second, "margin-top: 3px; margin-bottom: 7px");
        var panel = new FlexPanel { AlignItems = FlexAlign.Baseline, Wrap = FlexWrap.Wrap };
        panel.Children.Add(first);
        panel.Children.Add(second);

        Layout(panel, 100, 43);

        Assert.Equal(43, panel.DesiredSize.Height, 6);
        Assert.Equal(23, first.VisualBounds.Y, 6);
        Assert.Equal(3, second.VisualBounds.Y, 6);
    }

    [Fact]
    public void Baseline_WrappedLinesHaveIndependentBaselines()
    {
        var panel = new FlexPanel { AlignItems = FlexAlign.Baseline, Wrap = FlexWrap.Wrap,
            RowSpacing = 5 };
        var items = new[] { Fixed(50, 20), Fixed(50, 20), Fixed(50, 30), Fixed(50, 10) };
        var baselines = new[] { 5.0, 15.0, 20.0, 5.0 };
        for (var i = 0; i < items.Length; i++)
        {
            TextBlock.SetBaselineOffset(items[i], baselines[i]);
            panel.Children.Add(items[i]);
        }

        Layout(panel, 100, 65);

        Assert.Equal(65, panel.DesiredSize.Height, 6);
        Assert.Equal(10, items[0].VisualBounds.Y, 6);
        Assert.Equal(0, items[1].VisualBounds.Y, 6);
        Assert.Equal(35, items[2].VisualBounds.Y, 6);
        Assert.Equal(50, items[3].VisualBounds.Y, 6);
    }

    [Fact]
    public void Baseline_WrapReverseAnchorsCrossStartAtBottom()
    {
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 25);
        var panel = new FlexPanel { AlignItems = FlexAlign.Baseline, Wrap = FlexWrap.WrapReverse };
        panel.Children.Add(first);
        panel.Children.Add(second);

        Layout(panel, 100, 100);

        Assert.Equal(80, first.VisualBounds.Y, 6);
        Assert.Equal(65, second.VisualBounds.Y, 6);
        Assert.Equal(first.VisualBounds.Y + 10, second.VisualBounds.Y + 25, 6);
    }

    [Fact]
    public void BaselineOffsetChange_ReflowsAnExistingRow()
    {
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 25);
        var panel = new FlexPanel { AlignItems = FlexAlign.Baseline };
        panel.Children.Add(first);
        panel.Children.Add(second);
        Layout(panel, 100, 40);

        TextBlock.SetBaselineOffset(first, 5);
        Layout(panel, 100, 40);

        Assert.Equal(40, panel.DesiredSize.Height, 6);
        Assert.Equal(20, first.VisualBounds.Y, 6);
    }

    [Fact]
    public void Baseline_TextBlocksUseTheirRenderedFirstLine()
    {
        var small = new TextBlock { Text = "Small", FontSize = 12 };
        var large = new TextBlock { Text = "Large", FontSize = 28 };
        var panel = new FlexPanel { AlignItems = FlexAlign.Baseline };
        panel.Children.Add(small);
        panel.Children.Add(large);

        Layout(panel, 300, 80);

        var smallBaseline = small.VisualBounds.Y + small.GetFirstBaselineOffset(small.VisualBounds.Height);
        var largeBaseline = large.VisualBounds.Y + large.GetFirstBaselineOffset(large.VisualBounds.Height);
        Assert.Equal(largeBaseline, smallBaseline, 1);
        Assert.True(small.VisualBounds.Y > large.VisualBounds.Y);
    }

    [Fact]
    public void NestedRowFlex_ExportsItsSharedFirstLineBaseline()
    {
        var inner = new FlexPanel { Width = 80, AlignItems = FlexAlign.Baseline };
        var shortItem = Fixed(40, 20);
        var tallItem = Fixed(40, 30);
        TextBlock.SetBaselineOffset(shortItem, 10);
        TextBlock.SetBaselineOffset(tallItem, 25);
        inner.Children.Add(shortItem);
        inner.Children.Add(tallItem);
        var peer = Fixed(40, 20);
        TextBlock.SetBaselineOffset(peer, 15);
        var outer = new FlexPanel { AlignItems = FlexAlign.Baseline };
        outer.Children.Add(peer);
        outer.Children.Add(inner);

        Layout(outer, 140, 60);

        Assert.Equal(inner.VisualBounds.Y + tallItem.VisualBounds.Y + 25,
            peer.VisualBounds.Y + 15, 6);
        Assert.Equal(10, peer.VisualBounds.Y, 6);
    }

    [Fact]
    public void NestedBaselineChange_ReflowsTheOuterFlexLine()
    {
        var inner = new FlexPanel { Width = 80, AlignItems = FlexAlign.Baseline };
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 25);
        inner.Children.Add(first);
        inner.Children.Add(second);
        var peer = Fixed(40, 20);
        TextBlock.SetBaselineOffset(peer, 15);
        var outer = new FlexPanel { AlignItems = FlexAlign.Baseline };
        outer.Children.Add(inner);
        outer.Children.Add(peer);
        Layout(outer, 140, 60);

        TextBlock.SetBaselineOffset(second, 5);
        Layout(outer, 140, 60);

        Assert.Equal(inner.VisualBounds.Y + first.VisualBounds.Y + 10,
            peer.VisualBounds.Y + 15, 6);
        Assert.Equal(5, inner.VisualBounds.Y, 6);
    }

    [Fact]
    public void NestedRowFlexWithoutBaselineGroup_UsesOrderedFirstItemsPlacedBaseline()
    {
        var inner = new FlexPanel { Width = 80, Height = 60, AlignItems = FlexAlign.Center };
        var later = Fixed(40, 20);
        var first = Fixed(40, 20);
        FlexPanel.SetOrder(later, 1);
        FlexPanel.SetOrder(first, 0);
        TextBlock.SetBaselineOffset(later, 5);
        TextBlock.SetBaselineOffset(first, 15);
        inner.Children.Add(later);
        inner.Children.Add(first);
        var peer = Fixed(40, 20);
        TextBlock.SetBaselineOffset(peer, 10);
        var outer = new FlexPanel { AlignItems = FlexAlign.Baseline };
        outer.Children.Add(inner);
        outer.Children.Add(peer);

        Layout(outer, 140, 80);

        Assert.Equal(inner.VisualBounds.Y + first.VisualBounds.Y + 15,
            peer.VisualBounds.Y + 10, 6);
        Assert.Equal(0, first.VisualBounds.X, 6);
    }

    [Fact]
    public void CssFlexHost_ExportsItsFirstBaselineWithoutReparenting()
    {
        var inner = new StackPanel();
        Css.SetStyle(inner, "display:flex; align-items:baseline; width:80px");
        var first = Fixed(40, 20);
        var second = Fixed(40, 30);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 25);
        inner.Children.Add(first);
        inner.Children.Add(second);
        var peer = Fixed(40, 20);
        TextBlock.SetBaselineOffset(peer, 15);
        var outer = new FlexPanel { AlignItems = FlexAlign.Baseline };
        outer.Children.Add(inner);
        outer.Children.Add(peer);

        Layout(outer, 140, 60);

        Assert.Equal(inner.VisualBounds.Y + second.VisualBounds.Y + 25,
            peer.VisualBounds.Y + 15, 6);
        Assert.Same(inner, second.VisualParent);
    }

    [Fact]
    public void FlowItem_ExportsFirstLineRatherThanLastLineBaseline()
    {
        var flow = new StackPanel();
        Css.SetStyle(flow, "display:flow-root; width:40px; line-height:0");
        var first = Fixed(40, 20);
        var second = Fixed(40, 20);
        Css.SetStyle(first, "display:inline-block; width:40px; height:20px");
        Css.SetStyle(second, "display:inline-block; width:40px; height:20px");
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 15);
        flow.Children.Add(first);
        flow.Children.Add(second);
        var peer = Fixed(40, 20);
        TextBlock.SetBaselineOffset(peer, 15);
        var outer = new FlexPanel { AlignItems = FlexAlign.Baseline };
        outer.Children.Add(flow);
        outer.Children.Add(peer);

        Layout(outer, 120, 80);

        var peerBaseline = peer.VisualBounds.Y + 15;
        Assert.Equal(flow.VisualBounds.Y + first.VisualBounds.Y + 10, peerBaseline, 6);
        Assert.True(flow.VisualBounds.Y + second.VisualBounds.Y + 15 > peerBaseline);
    }

    [Fact]
    public void FlowItem_PropagatesNestedBlockBaselineThroughPaddingAndMargin()
    {
        var wrapper = new StackPanel();
        Css.SetStyle(wrapper, "display:flow-root; width:40px; padding-top:3px; line-height:0");
        var nested = new StackPanel();
        Css.SetStyle(nested, "display:flow-root; width:40px; margin-top:7px; line-height:0");
        wrapper.Children.Add(nested);
        var inline = Fixed(40, 20);
        Css.SetStyle(inline, "display:inline-block; width:40px; height:20px");
        TextBlock.SetBaselineOffset(inline, 10);
        nested.Children.Add(inline);
        var peer = Fixed(40, 20);
        TextBlock.SetBaselineOffset(peer, 15);
        var outer = new FlexPanel { AlignItems = FlexAlign.Baseline };
        outer.Children.Add(wrapper);
        outer.Children.Add(peer);

        Layout(outer, 120, 80);

        Assert.Equal(wrapper.VisualBounds.Y + nested.VisualBounds.Y + inline.VisualBounds.Y + 10,
            peer.VisualBounds.Y + 15, 6);
    }

    [Fact]
    public void ColumnFlex_ExportsFirstItemsBaselineAfterJustification()
    {
        var inner = new FlexPanel
        {
            Width = 40, Height = 80, Direction = FlexDirection.Column,
            JustifyContent = FlexJustify.FlexEnd,
        };
        var child = Fixed(40, 20);
        TextBlock.SetBaselineOffset(child, 10);
        inner.Children.Add(child);
        var peer = Fixed(40, 20);
        TextBlock.SetBaselineOffset(peer, 15);
        var outer = new FlexPanel { AlignItems = FlexAlign.Baseline };
        outer.Children.Add(inner);
        outer.Children.Add(peer);

        Layout(outer, 100, 100);

        Assert.Equal(inner.VisualBounds.Y + child.VisualBounds.Y + 10,
            peer.VisualBounds.Y + 15, 6);
    }

    [Fact]
    public void WrapReverseFlex_ExportsThePhysicallyPositionedFirstLineBaseline()
    {
        var inner = new FlexPanel
        {
            Width = 40, Height = 100, Wrap = FlexWrap.WrapReverse,
            AlignItems = FlexAlign.Baseline,
        };
        var first = Fixed(40, 20);
        var second = Fixed(40, 20);
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(second, 15);
        inner.Children.Add(first);
        inner.Children.Add(second);
        var peer = Fixed(40, 20);
        TextBlock.SetBaselineOffset(peer, 15);
        var outer = new FlexPanel { AlignItems = FlexAlign.Baseline };
        outer.Children.Add(inner);
        outer.Children.Add(peer);

        Layout(outer, 100, 120);

        Assert.Equal(inner.VisualBounds.Y + first.VisualBounds.Y + 10,
            peer.VisualBounds.Y + 15, 6);
    }

    [Fact]
    public void MainAxisAutoMarginAbsorbsFreeSpaceBeforeJustification()
    {
        var first = Fixed(40, 20);
        var pushed = Fixed(40, 20);
        var last = Fixed(40, 20);
        Css.SetStyle(pushed, "margin-left:auto");
        var panel = new FlexPanel { JustifyContent = FlexJustify.SpaceEvenly };
        panel.Children.Add(first);
        panel.Children.Add(pushed);
        panel.Children.Add(last);

        Layout(panel, 200, 50);

        Assert.Equal(0, first.VisualBounds.X, 6);
        Assert.Equal(120, pushed.VisualBounds.X, 6);
        Assert.Equal(160, last.VisualBounds.X, 6);
    }

    [Theory]
    [InlineData(200, 40, 80)]
    [InlineData(100, 120, 0)]
    public void MainAxisAutoMarginsSharePositiveSpaceAndStayZeroOnOverflow(
        double panelWidth, double itemWidth, double expectedX)
    {
        var child = Fixed(itemWidth, 20);
        FlexPanel.SetShrink(child, 0);
        Css.SetStyle(child, "margin-left:auto; margin-right:auto");
        var panel = new FlexPanel();
        panel.Children.Add(child);

        Layout(panel, panelWidth, 50);

        Assert.Equal(expectedX, child.VisualBounds.X, 6);
    }

    [Fact]
    public void MainAxisAutoMarginsRespectReverseDirection()
    {
        var first = Fixed(40, 20);
        var pushed = Fixed(40, 20);
        Css.SetStyle(pushed, "margin-right:auto");
        var panel = new FlexPanel { Direction = FlexDirection.RowReverse };
        panel.Children.Add(first);
        panel.Children.Add(pushed);

        Layout(panel, 200, 50);

        Assert.Equal(160, first.VisualBounds.X, 6);
        Assert.Equal(0, pushed.VisualBounds.X, 6);
    }

    [Fact]
    public void RightToLeftMainAxisAutoMarginUsesPhysicalRightEdge()
    {
        var first = Fixed(40, 20);
        var pushed = Fixed(40, 20);
        Css.SetStyle(pushed, "margin-right:auto");
        var panel = new FlexPanel { FlowDirection = FlowDirection.RightToLeft };
        panel.Children.Add(first);
        panel.Children.Add(pushed);

        Layout(panel, 200, 50);

        Assert.Equal(160, first.VisualBounds.X, 6);
        Assert.Equal(0, pushed.VisualBounds.X, 6);
    }

    [Fact]
    public void ColumnMainAxisAutoMarginUsesFinalHeight()
    {
        var first = Fixed(20, 40);
        var pushed = Fixed(20, 40);
        Css.SetStyle(pushed, "margin-top:auto");
        var panel = new FlexPanel { Direction = FlexDirection.Column,
            JustifyContent = FlexJustify.Center };
        panel.Children.Add(first);
        panel.Children.Add(pushed);

        panel.Measure(new Size(100, 200));
        panel.Arrange(new Rect(0, 0, 100, 100));
        Assert.Equal(60, pushed.VisualBounds.Y, 6);
        panel.Arrange(new Rect(0, 0, 100, 200));
        Assert.Equal(160, pushed.VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData("margin-top:auto", 80)]
    [InlineData("margin-bottom:auto", 0)]
    [InlineData("margin-top:auto; margin-bottom:auto", 40)]
    public void CrossAxisAutoMarginsOverrideAlignSelf(string margin, double expectedY)
    {
        var child = Fixed(40, 20);
        Css.SetStyle(child, margin);
        var panel = new FlexPanel { AlignItems = FlexAlign.FlexEnd };
        panel.Children.Add(child);

        Layout(panel, 100, 100);

        Assert.Equal(expectedY, child.VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData("margin-left:auto", 80)]
    [InlineData("margin-right:auto", 0)]
    [InlineData("margin-left:auto; margin-right:auto", 40)]
    public void ColumnCrossAxisAutoMarginsDistributeHorizontalSpace(string margin, double expectedX)
    {
        var child = Fixed(20, 20);
        Css.SetStyle(child, margin);
        var panel = new FlexPanel { Direction = FlexDirection.Column,
            AlignItems = FlexAlign.FlexEnd };
        panel.Children.Add(child);

        Layout(panel, 100, 100);

        Assert.Equal(expectedX, child.VisualBounds.X, 6);
    }

    [Theory]
    [InlineData(FlexWrap.NoWrap, 80)]
    [InlineData(FlexWrap.WrapReverse, 0)]
    public void RightToLeftColumnCrossStartFollowsWrapDirection(FlexWrap wrap, double expectedX)
    {
        var child = Fixed(20, 20);
        var panel = new FlexPanel { Direction = FlexDirection.Column,
            FlowDirection = FlowDirection.RightToLeft, Wrap = wrap,
            AlignItems = FlexAlign.FlexStart, AlignContent = FlexContentAlign.FlexStart };
        panel.Children.Add(child);

        Layout(panel, 100, 100);

        Assert.Equal(expectedX, child.VisualBounds.X, 6);
    }

    [Fact]
    public void RightToLeftColumnAutoMarginOverridesCrossAlignment()
    {
        var child = Fixed(20, 20);
        Css.SetStyle(child, "margin-right:auto");
        var panel = new FlexPanel { Direction = FlexDirection.Column,
            FlowDirection = FlowDirection.RightToLeft, AlignItems = FlexAlign.FlexEnd };
        panel.Children.Add(child);

        Layout(panel, 100, 100);

        Assert.Equal(0, child.VisualBounds.X, 6);
    }

    [Fact]
    public void CrossAxisAutoMarginPreventsBaselineGroupParticipation()
    {
        var auto = Fixed(40, 20);
        var baseline = Fixed(40, 30);
        TextBlock.SetBaselineOffset(auto, 5);
        TextBlock.SetBaselineOffset(baseline, 25);
        Css.SetStyle(auto, "margin-top:auto");
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap, AlignItems = FlexAlign.Baseline };
        panel.Children.Add(auto);
        panel.Children.Add(baseline);

        Layout(panel, 100, 30);

        Assert.Equal(30, panel.DesiredSize.Height, 6);
        Assert.Equal(10, auto.VisualBounds.Y, 6);
        Assert.Equal(0, baseline.VisualBounds.Y, 6);
    }

    [Fact]
    public void OverflowingCrossAxisAutoMarginKeepsItemAtCrossStart()
    {
        var child = Fixed(40, 120);
        Css.SetStyle(child, "margin-top:auto");
        var panel = new FlexPanel { AlignItems = FlexAlign.Center };
        panel.Children.Add(child);

        Layout(panel, 100, 100);

        Assert.Equal(0, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void NativeMarginOverridesCssAutoMarginInFlex()
    {
        var child = Fixed(40, 20);
        child.Margin = new Thickness(0, 5, 0, 0);
        Css.SetStyle(child, "margin-top:auto");
        var panel = new FlexPanel { AlignItems = FlexAlign.FlexStart };
        panel.Children.Add(child);

        Layout(panel, 100, 100);

        Assert.Equal(5, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void WrapReverseFlexStartAndFlexEndUseReversedCrossEdges()
    {
        var start = Fixed(40, 20);
        var end = Fixed(40, 20);
        var tall = Fixed(40, 40);
        FlexPanel.SetAlignSelf(end, FlexAlign.FlexEnd);
        var panel = new FlexPanel { Wrap = FlexWrap.WrapReverse,
            AlignContent = FlexContentAlign.FlexStart, AlignItems = FlexAlign.FlexStart };
        panel.Children.Add(start);
        panel.Children.Add(end);
        panel.Children.Add(tall);

        Layout(panel, 120, 100);

        Assert.Equal(80, start.VisualBounds.Y, 6);
        Assert.Equal(60, end.VisualBounds.Y, 6);
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

    [Theory]
    [InlineData(FlexDirection.Row, 160, 120)]
    [InlineData(FlexDirection.RowReverse, 0, 40)]
    public void RightToLeftFlowDirectionSetsRowMainStart(
        FlexDirection direction, double firstX, double secondX)
    {
        var first = Fixed(40, 20);
        var second = Fixed(40, 20);
        var panel = new FlexPanel { FlowDirection = FlowDirection.RightToLeft,
            Direction = direction };
        panel.Children.Add(first);
        panel.Children.Add(second);

        Layout(panel, 200, 40);

        Assert.Equal(firstX, first.VisualBounds.X, 6);
        Assert.Equal(secondX, second.VisualBounds.X, 6);
    }

    [Fact]
    public void RightToLeftFlowDirectionUpdatesExistingFlexLayout()
    {
        var first = Fixed(40, 20);
        var second = Fixed(40, 20);
        var panel = new FlexPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);
        Layout(panel, 200, 40);
        Assert.Equal(0, first.VisualBounds.X, 6);

        panel.FlowDirection = FlowDirection.RightToLeft;
        Layout(panel, 200, 40);

        Assert.Equal(160, first.VisualBounds.X, 6);
        Assert.Equal(120, second.VisualBounds.X, 6);
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
