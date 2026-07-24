using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public class PanelSpacingTests
{
    #region StackPanel

    [Fact]
    public void StackPanel_Vertical_Spacing_GapsInsertedBetweenVisibleChildren()
    {
        var panel = new StackPanel { Spacing = 12 };
        var a = new FixedElement(100, 20);
        var b = new FixedElement(100, 20);
        var c = new FixedElement(100, 20);
        panel.Children.Add(a);
        panel.Children.Add(b);
        panel.Children.Add(c);

        panel.Measure(new Size(200, 200));
        panel.Arrange(new Rect(0, 0, 200, 200));

        Assert.Equal(20 * 3 + 12 * 2, panel.DesiredSize.Height);
        Assert.Equal(0, a.VisualBounds.Y);
        Assert.Equal(32, b.VisualBounds.Y);
        Assert.Equal(64, c.VisualBounds.Y);
    }

    [Fact]
    public void StackPanel_Horizontal_Spacing_GapsInsertedBetweenVisibleChildren()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var a = new FixedElement(30, 10);
        var b = new FixedElement(30, 10);
        panel.Children.Add(a);
        panel.Children.Add(b);

        panel.Measure(new Size(200, 200));
        panel.Arrange(new Rect(0, 0, 200, 200));

        Assert.Equal(30 + 8 + 30, panel.DesiredSize.Width);
        Assert.Equal(0, a.VisualBounds.X);
        Assert.Equal(38, b.VisualBounds.X);
    }

    [Fact]
    public void StackPanel_Spacing_CollapsedChildrenDoNotContributeGap()
    {
        var panel = new StackPanel { Spacing = 10 };
        var a = new FixedElement(50, 20);
        var collapsed = new FixedElement(50, 20) { Visibility = Visibility.Collapsed };
        var c = new FixedElement(50, 20);
        panel.Children.Add(a);
        panel.Children.Add(collapsed);
        panel.Children.Add(c);

        panel.Measure(new Size(200, 200));
        panel.Arrange(new Rect(0, 0, 200, 200));

        // Only two visible children → one gap.
        Assert.Equal(20 + 10 + 20, panel.DesiredSize.Height);
        Assert.Equal(0, a.VisualBounds.Y);
        Assert.Equal(30, c.VisualBounds.Y);
    }

    [Fact]
    public void StackPanel_ZeroSpacing_MatchesBaselineLayout()
    {
        var panel = new StackPanel { Spacing = 0 };
        var a = new FixedElement(40, 15);
        var b = new FixedElement(40, 15);
        panel.Children.Add(a);
        panel.Children.Add(b);

        panel.Measure(new Size(100, 100));
        panel.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(30, panel.DesiredSize.Height);
        Assert.Equal(0, a.VisualBounds.Y);
        Assert.Equal(15, b.VisualBounds.Y);
    }

    #endregion

    #region Grid

    [Fact]
    public void Grid_RowAndColumnSpacing_OffsetsShifted_AndDesiredSizeIncludesSpacing()
    {
        var grid = new Grid { RowSpacing = 5, ColumnSpacing = 7 };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

        var topLeft = new FixedElement(10, 10);
        var bottomRight = new FixedElement(10, 10);
        Grid.SetRow(bottomRight, 1);
        Grid.SetColumn(bottomRight, 1);
        grid.Children.Add(topLeft);
        grid.Children.Add(bottomRight);

        grid.Measure(new Size(500, 500));
        grid.Arrange(new Rect(0, 0, 500, 500));

        Assert.Equal(40 + 7 + 60, grid.DesiredSize.Width);
        Assert.Equal(20 + 5 + 30, grid.DesiredSize.Height);

        Assert.Equal(0, topLeft.VisualBounds.X);
        Assert.Equal(0, topLeft.VisualBounds.Y);
        // Second column starts after first column width (40) plus column spacing (7).
        Assert.Equal(47, bottomRight.VisualBounds.X);
        // Second row starts after first row height (20) plus row spacing (5).
        Assert.Equal(25, bottomRight.VisualBounds.Y);
    }

    [Fact]
    public void Grid_SpannedCell_IncludesInternalSpacingInCellExtent()
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

        var spanned = new FixedElement(1, 1);
        Grid.SetColumn(spanned, 0);
        Grid.SetColumnSpan(spanned, 2);
        grid.Children.Add(spanned);

        grid.Measure(new Size(500, 500));
        grid.Arrange(new Rect(0, 0, 500, 500));

        // Spanned cell owns the internal gap: 50 + 10 + 50 = 110, never the trailing gap.
        Assert.Equal(110, spanned.VisualBounds.Width);
        Assert.Equal(0, spanned.VisualBounds.X);
    }

    [Fact]
    public void Grid_StarSizing_SubtractsSpacingBeforeDistribution()
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        var left = new FixedElement(1, 1);
        var right = new FixedElement(1, 1);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);

        grid.Measure(new Size(110, 50));
        grid.Arrange(new Rect(0, 0, 110, 50));

        // 110 - 10 spacing = 100 split in half = 50 each.
        Assert.Equal(0, left.VisualBounds.X);
        Assert.Equal(50, left.VisualBounds.Width);
        Assert.Equal(60, right.VisualBounds.X);
        Assert.Equal(50, right.VisualBounds.Width);
    }

    #endregion

    #region WrapPanel

    [Fact]
    public void WrapPanel_HorizontalSpacing_AppliedBetweenItemsOnSameRow()
    {
        var panel = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 4 };
        var a = new FixedElement(30, 20);
        var b = new FixedElement(30, 20);
        panel.Children.Add(a);
        panel.Children.Add(b);

        panel.Measure(new Size(500, 500));
        panel.Arrange(new Rect(0, 0, 500, 500));

        Assert.Equal(0, a.VisualBounds.X);
        Assert.Equal(30 + 6, b.VisualBounds.X);
    }

    [Fact]
    public void WrapPanel_VerticalSpacing_AppliedBetweenWrappedLines()
    {
        // Two items each 40 wide with spacing 6, viewport 60 → second wraps to row 2.
        var panel = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 12 };
        var a = new FixedElement(40, 20);
        var b = new FixedElement(40, 20);
        panel.Children.Add(a);
        panel.Children.Add(b);

        panel.Measure(new Size(60, 500));
        panel.Arrange(new Rect(0, 0, 60, 500));

        Assert.Equal(0, a.VisualBounds.Y);
        Assert.Equal(20 + 12, b.VisualBounds.Y);
        Assert.Equal(0, b.VisualBounds.X);
    }

    [Fact]
    public void WrapPanel_VerticalOrientation_AppliesItemAndLineSpacingOnCorrectAxes()
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalSpacing = 11,
            VerticalSpacing = 5,
        };
        var a = new FixedElement(20, 30);
        var b = new FixedElement(30, 30);
        var c = new FixedElement(15, 30);
        panel.Children.Add(a);
        panel.Children.Add(b);
        panel.Children.Add(c);

        // Two 30px-tall items plus the 5px primary gap fit exactly. The third item
        // wraps into a second column after the first column's 30px thickness and
        // the 11px secondary gap.
        panel.Measure(new Size(200, 65));
        panel.Arrange(new Rect(0, 0, 200, 65));

        Assert.Equal(0, a.VisualBounds.X);
        Assert.Equal(0, a.VisualBounds.Y);
        Assert.Equal(30, a.VisualBounds.Width);
        Assert.Equal(0, b.VisualBounds.X);
        Assert.Equal(35, b.VisualBounds.Y);
        Assert.Equal(41, c.VisualBounds.X);
        Assert.Equal(0, c.VisualBounds.Y);
    }

    [Fact]
    public void WrapPanel_FixedItemSize_DrivesWrappingAndArrangedBounds()
    {
        var panel = new WrapPanel
        {
            ItemWidth = 40,
            ItemHeight = 24,
            HorizontalSpacing = 5,
            VerticalSpacing = 7,
        };
        var a = new FixedElement(5, 8);
        var b = new FixedElement(60, 50);
        var c = new FixedElement(10, 12);
        panel.Children.Add(a);
        panel.Children.Add(b);
        panel.Children.Add(c);

        // Two fixed cells plus their gap fit exactly in 85px; the third wraps.
        panel.Measure(new Size(85, 200));
        panel.Arrange(new Rect(0, 0, 85, 200));

        Assert.Equal(new Rect(0, 0, 40, 24), a.VisualBounds);
        Assert.Equal(new Rect(45, 0, 40, 24), b.VisualBounds);
        Assert.Equal(new Rect(0, 31, 40, 24), c.VisualBounds);
        Assert.Equal(55, panel.DesiredSize.Height);
    }

    [Fact]
    public void WrapPanel_ArrangeOverride_SteadyStateDoesNotAllocate()
    {
        var panel = new ArrangeProbeWrapPanel
        {
            ItemWidth = 40,
            ItemHeight = 24,
            HorizontalSpacing = 5,
            VerticalSpacing = 7,
        };
        for (int i = 0; i < 12; i++)
        {
            panel.Children.Add(new FixedElement(10 + i, 12 + i));
        }

        var arrangeSize = new Size(175, 200);
        panel.Measure(arrangeSize);

        // Warm the JIT and establish child arrange rects before measuring the hot path.
        for (int i = 0; i < 100; i++)
        {
            panel.InvokeArrangeOverride(arrangeSize);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            panel.InvokeArrangeOverride(arrangeSize);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WrapPanel_ParentWidthResize_ReusesNaturalChildMeasure()
    {
        var panel = new WrapPanel
        {
            HorizontalSpacing = 4,
            VerticalSpacing = 4,
        };
        var children = new List<MeasureCountingElement>();
        for (int i = 0; i < 100; i++)
        {
            var child = new MeasureCountingElement(40, 24);
            children.Add(child);
            panel.Children.Add(child);
        }

        panel.Measure(new Size(300, 400));
        panel.Arrange(new Rect(0, 0, 300, panel.DesiredSize.Height));
        Assert.All(children, child => Assert.Equal(1, child.MeasureCount));

        // The panel must recompute wrapping at the new width, but a natural-size
        // child still receives the same Infinity constraint and short-circuits.
        panel.Measure(new Size(340, 400));
        panel.Arrange(new Rect(0, 0, 340, panel.DesiredSize.Height));

        Assert.All(children, child => Assert.Equal(1, child.MeasureCount));
    }

    [Fact]
    public void WrapPanel_AutoItemSize_UsesNaturalInfinityConstraint()
    {
        var panel = new WrapPanel();
        var child = new MeasureCountingElement(40, 24);
        panel.Children.Add(child);

        panel.Measure(new Size(300, 200));

        Assert.True(double.IsPositiveInfinity(child.LastAvailableSize.Width));
        Assert.True(double.IsPositiveInfinity(child.LastAvailableSize.Height));
    }

    #endregion

    #region DockPanel

    [Fact]
    public void DockPanel_Spacing_GapInsertedBetweenAdjacentDockedSiblings()
    {
        var dock = new DockPanel { Spacing = 8, LastChildFill = true };
        var leftSide = new FixedElement(20, 20);
        DockPanel.SetDock(leftSide, Jalium.UI.Controls.Dock.Left);
        var fill = new FixedElement(1, 1);

        dock.Children.Add(leftSide);
        dock.Children.Add(fill);

        dock.Measure(new Size(200, 100));
        dock.Arrange(new Rect(0, 0, 200, 100));

        Assert.Equal(0, leftSide.VisualBounds.X);
        Assert.Equal(20, leftSide.VisualBounds.Width);
        // Fill starts 8px after the left dock ends.
        Assert.Equal(28, fill.VisualBounds.X);
        Assert.Equal(200 - 28, fill.VisualBounds.Width);
    }

    #endregion

    #region UniformGrid

    [Fact]
    public void UniformGrid_RowAndColumnSpacing_ShrinkCellsAndShiftOffsets()
    {
        var grid = new UniformGrid { Rows = 2, Columns = 2, RowSpacing = 4, ColumnSpacing = 6 };
        var a = new FixedElement(1, 1);
        var b = new FixedElement(1, 1);
        var c = new FixedElement(1, 1);
        var d = new FixedElement(1, 1);
        grid.Children.Add(a);
        grid.Children.Add(b);
        grid.Children.Add(c);
        grid.Children.Add(d);

        grid.Measure(new Size(100, 100));
        grid.Arrange(new Rect(0, 0, 100, 100));

        // Width: (100 - 6) / 2 = 47, Height: (100 - 4) / 2 = 48.
        Assert.Equal(47, a.VisualBounds.Width);
        Assert.Equal(48, a.VisualBounds.Height);

        Assert.Equal(0, a.VisualBounds.X);
        Assert.Equal(0, a.VisualBounds.Y);
        Assert.Equal(47 + 6, b.VisualBounds.X);
        Assert.Equal(0, b.VisualBounds.Y);
        Assert.Equal(0, c.VisualBounds.X);
        Assert.Equal(48 + 4, c.VisualBounds.Y);
    }

    #endregion

    #region VirtualizingStackPanel (non-virtualized fallback)

    [Fact]
    public void VirtualizingStackPanel_Spacing_NonVirtualizedPathAppliesGap()
    {
        // Without an ItemContainerGenerator the panel falls back to the non-virtualized path,
        // which must still honour Spacing uniformly.
        var panel = new VirtualizingStackPanel { Spacing = 9 };
        var a = new FixedElement(100, 20);
        var b = new FixedElement(100, 20);
        panel.Children.Add(a);
        panel.Children.Add(b);

        panel.Measure(new Size(200, 200));
        panel.Arrange(new Rect(0, 0, 200, 200));

        Assert.Equal(0, a.VisualBounds.Y);
        Assert.Equal(29, b.VisualBounds.Y);
    }

    #endregion

    private sealed class FixedElement : FrameworkElement
    {
        private readonly double _width;
        private readonly double _height;

        public FixedElement(double width, double height)
        {
            _width = width;
            _height = height;
        }

        protected override Size MeasureOverride(Size availableSize) => new(_width, _height);
    }

    private sealed class MeasureCountingElement : FrameworkElement
    {
        private readonly Size _desired;

        public MeasureCountingElement(double width, double height)
        {
            _desired = new Size(width, height);
        }

        public int MeasureCount { get; private set; }
        public Size LastAvailableSize { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            LastAvailableSize = availableSize;
            return _desired;
        }
    }

    private sealed class ArrangeProbeWrapPanel : WrapPanel
    {
        public Size InvokeArrangeOverride(Size finalSize) => base.ArrangeOverride(finalSize);
    }
}
