using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class ResizeLayoutOptimizationTests
{
    [Theory]
    [InlineData(220, 100)]
    [InlineData(80, 200)]
    [InlineData(double.PositiveInfinity, 200)]
    [InlineData(120, double.PositiveInfinity)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity)]
    public void OverlayGrid_MatchesAnExplicitStarCell(double width, double height)
    {
        var overlay = CreateGrid(explicitCell: false);
        var explicitGrid = CreateGrid(explicitCell: true);
        var available = new Size(width, height);
        overlay.Measure(available);
        explicitGrid.Measure(available);
        Assert.Equal(explicitGrid.DesiredSize, overlay.DesiredSize);
        var final = new Rect(0, 0, 180, 240);
        overlay.Arrange(final);
        explicitGrid.Arrange(final);
        for (int i = 0; i < overlay.Children.Count; i++)
        {
            Assert.Equal(((FrameworkElement)explicitGrid.Children[i]).VisualBounds,
                ((FrameworkElement)overlay.Children[i]).VisualBounds);
        }
    }

    [Fact]
    public void OverlayGrid_CanGainAndLoseDefinitionsBetweenLayouts()
    {
        var grid = new Grid();
        var first = new Border { MinWidth = 30, MinHeight = 20 };
        var second = new Border { MinWidth = 40, MinHeight = 30 };
        Grid.SetColumn(second, 1);
        grid.Children.Add(first);
        grid.Children.Add(second);
        Layout(grid, 200, 100);
        Assert.Equal(200, first.ActualWidth);
        Assert.Equal(200, second.ActualWidth);

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Layout(grid, 240, 100);
        Assert.Equal(120, first.ActualWidth);
        Assert.Equal(120, second.VisualBounds.X);

        grid.ColumnDefinitions.Clear();
        Layout(grid, 180, 100);
        Assert.Equal(180, first.ActualWidth);
        Assert.Equal(180, second.ActualWidth);
        Assert.Equal(0, second.VisualBounds.X);
    }

    [Fact]
    public void OverlayGrid_WidthCorrectionRemainsAMeasurePass()
    {
        var child = new MeasuringElement();
        var grid = new Grid { Children = { child } };
        grid.Measure(new Size(200, 100));
        int measured = child.MeasureCount;
        grid.Arrange(new Rect(0, 0, 100, 100));
        Assert.Equal(measured, child.MeasureCount);
        Assert.False(grid.IsMeasureValid);
        grid.Measure(new Size(100, 100));
        grid.Arrange(new Rect(0, 0, 100, 100));
        Assert.Equal(new Size(100, 100), child.PreviousAvailableSize);
        Assert.True(grid.IsMeasureValid);
        Assert.True(grid.IsArrangeValid);
    }

    [Fact]
    public void NoWrapText_WidthChangesReuseTheExistingLineMetrics()
    {
        var text = new FontReadCountingTextBlock { Text = "A long unwrapped test name", TextTrimming = TextTrimming.CharacterEllipsis };
        text.Measure(new Size(400, 100));
        int fontReads = text.FontReads;
        Assert.True(fontReads > 0);
        double height = text.DesiredSize.Height;
        for (int width = 399; width >= 150; width--)
        {
            text.Measure(new Size(width, 100));
            Assert.Equal(height, text.DesiredSize.Height);
            Assert.InRange(text.DesiredSize.Width, 0, width);
        }
        Assert.Equal(fontReads, text.FontReads);
    }

    [Fact]
    public void TextLineMetrics_RefreshForInheritedFontChangesAndReparenting()
    {
        var first = new StackPanel();
        var second = new StackPanel();
        first.SetValue(TextBlock.FontSizeProperty, 10.0);
        second.SetValue(TextBlock.FontSizeProperty, 30.0);
        var text = new TextBlock { Text = "Font metrics" };
        first.Children.Add(text);
        Layout(first, 240, 100);
        double smallHeight = text.DesiredSize.Height;

        first.SetValue(TextBlock.FontSizeProperty, 20.0);
        Assert.Equal(20, text.FontSize);
        text.Measure(new Size(220, 100));
        Layout(first, 240, 100);
        Assert.True(text.DesiredSize.Height > smallHeight * 1.5);
        double mediumHeight = text.DesiredSize.Height;
        first.Children.Remove(text);
        second.Children.Add(text);
        Layout(second, 240, 100);
        Assert.True(text.DesiredSize.Height > mediumHeight);
    }

    [Fact]
    public void TextLineMetrics_RefreshWhenExplicitLineHeightAndStackingChange()
    {
        var text = new TextBlock { Text = "First\nSecond", FontSize = 20, LineHeight = 10, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        text.Measure(new Size(200, 200));
        Assert.Equal(22, text.DesiredSize.Height);
        text.LineHeight = 40;
        text.Measure(new Size(200, 200));
        Assert.Equal(82, text.DesiredSize.Height);
        text.LineHeight = 10;
        text.LineStackingStrategy = LineStackingStrategy.MaxHeight;
        text.Measure(new Size(200, 200));
        Assert.True(text.DesiredSize.Height > 22);
    }

    [Fact]
    public void TextLineMetrics_RefreshAfterTheFontMetricsCacheIsCleared()
    {
        var text = new FontReadCountingTextBlock { Text = "Fresh font metrics" };
        text.Measure(new Size(200, 100));
        int previousReads = text.FontReads;
        Jalium.UI.Interop.TextMeasurement.ClearCache();
        text.Measure(new Size(180, 100));
        Assert.True(text.FontReads > previousReads);
    }

    [Fact]
    public void FastPropertyReads_PreserveAnimatedPrecedenceAndCoercion()
    {
        var plain = DependencyProperty.Register("ResizeValue", typeof(double), typeof(ResizeLayoutOptimizationTests), new PropertyMetadata(10.0));
        int coercions = 0;
        var coerced = DependencyProperty.Register("ResizeCoerced", typeof(double), typeof(ResizeLayoutOptimizationTests),
            new PropertyMetadata(10.0, null, (_, value) => { coercions++; return Math.Min((double)value!, 25); }));
        var element = new Border();
        element.SetValue(plain, 20.0);
        element.SetAnimatedValue(plain, 30.0, holdEndValue: false);
        Assert.Equal(30.0, element.GetValue(plain));
        element.ClearAnimatedValue(plain);
        Assert.Equal(20.0, element.GetValue(plain));
        element.SetValue(coerced, 40.0);
        int previous = coercions;
        Assert.Equal(25.0, element.GetValue(coerced));
        Assert.Equal(previous + 1, coercions);
        element.SetAnimatedValue(coerced, 50.0, holdEndValue: false);
        Assert.Equal(25.0, element.GetValue(coerced));
    }

    private static Grid CreateGrid(bool explicitCell)
    {
        var grid = new Grid();
        if (explicitCell)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }
        grid.Children.Add(new Border { Width = 50, Height = 25, Margin = new Thickness(3) });
        grid.Children.Add(new TextBlock { Text = "Text that wraps across several cell widths", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 6, 3) });
        var hidden = new Border { Width = 1000, Height = 1000, Visibility = Visibility.Collapsed };
        Grid.SetRow(hidden, 7); Grid.SetColumn(hidden, 9);
        Grid.SetColumnSpan(hidden, 5); Grid.SetRowSpan(hidden, 4);
        grid.Children.Add(hidden);
        return grid;
    }

    [Fact]
    public void InheritedSource_RefreshesWhenAValueEqualToTheDefaultIsSetOrCleared()
    {
        var property = DependencyProperty.Register("SameDefaultInheritance", typeof(double), typeof(ResizeLayoutOptimizationTests), new PropertyMetadata(10.0, null, null, inherits: true));
        property.OverrideMetadata(typeof(InheritanceLeaf), new PropertyMetadata(20.0, null, null, inherits: true));
        var parent = new StackPanel();
        var child = new InheritanceLeaf();
        parent.Children.Add(child);
        Assert.Equal(20.0, child.GetValue(property));
        parent.SetValue(property, 10.0); // No effective-value-change event on the parent.
        Assert.Equal(10.0, child.GetValue(property));
        parent.ClearValue(property);
        Assert.Equal(20.0, child.GetValue(property));
    }

    [Fact]
    public void InheritedSource_RefreshesForIntermediateLayersAndReparenting()
    {
        var property = DependencyProperty.Register("LayeredInheritance", typeof(double), typeof(ResizeLayoutOptimizationTests), new PropertyMetadata(10.0, null, null, inherits: true));
        var root = new StackPanel();
        var intermediate = new StackPanel();
        var child = new Border();
        root.SetValue(property, 30.0);
        root.Children.Add(intermediate); intermediate.Children.Add(child);
        Assert.Equal(30.0, child.GetValue(property));
        intermediate.SetLayerValue(property, 40.0, DependencyObject.LayerValueSource.StyleSetter);
        Assert.Equal(40.0, child.GetValue(property));
        intermediate.SetValue(property, 50.0);
        Assert.Equal(50.0, child.GetValue(property));
        intermediate.ClearValue(property);
        Assert.Equal(40.0, child.GetValue(property));
        intermediate.ClearLayerValue(property, DependencyObject.LayerValueSource.StyleSetter);
        Assert.Equal(30.0, child.GetValue(property));
        var other = new StackPanel(); other.SetValue(property, 60.0);
        root.Children.Remove(intermediate); other.Children.Add(intermediate);
        Assert.Equal(60.0, child.GetValue(property));
    }

    [Fact]
    public void InheritedSource_DoesNotCacheAnAnimatedProvidersCoercedValue()
    {
        var parent = new StackPanel();
        int reads = 0;
        var property = DependencyProperty.Register("AnimatedInheritance", typeof(double), typeof(ResizeLayoutOptimizationTests),
            new PropertyMetadata(10.0, null, (owner, value) => ReferenceEquals(owner, parent) ? (double)value! + ++reads : value, inherits: true));
        var child = new Border(); parent.Children.Add(child);
        Assert.Equal(10.0, child.GetValue(property));
        parent.SetAnimatedValue(property, 20.0, holdEndValue: false);
        reads = 0;
        Assert.Equal(21.0, child.GetValue(property));
        Assert.Equal(22.0, child.GetValue(property));
        parent.DiscardAnimatedValue(property);
        Assert.Equal(10.0, child.GetValue(property));
    }

    [Fact]
    public void InheritedSource_RespectsMetadataBarriersAndLaterOverrides()
    {
        var property = DependencyProperty.Register("MetadataInheritance", typeof(double), typeof(ResizeLayoutOptimizationTests), new PropertyMetadata(10.0));
        property.OverrideMetadata(typeof(MetadataLeaf), new PropertyMetadata(20.0, null, null, inherits: true));
        var root = new StackPanel(); root.SetValue(property, 30.0);
        var barrier = new MetadataBarrier(); var child = new MetadataLeaf();
        root.Children.Add(barrier); barrier.Children.Add(child);
        Assert.Equal(20.0, child.GetValue(property));
        property.OverrideMetadata(typeof(MetadataBarrier), new PropertyMetadata(10.0, null, null, inherits: true));
        Assert.Equal(30.0, child.GetValue(property));
    }

    private sealed class InheritanceLeaf : Border { }
    private sealed class MetadataLeaf : Border { }
    private sealed class MetadataBarrier : StackPanel { }

    [Fact]
    public void HorizontalResize_DoesNotMutateTheUnchangedActualHeight()
    {
        var element = new ActualSizeReadCountingElement();
        Layout(element, 100, 80);
        element.HeightReads = 0;
        int notifications = 0;
        element.SizeChanged += (_, e) =>
        {
            notifications++;
            Assert.Equal(e.NewSize.Width, element.ActualWidth);
            Assert.Equal(80, element.RenderSize.Height);
        };
        for (int width = 101; width <= 110; width++) Layout(element, width, 80);
        Assert.Equal(0, element.HeightReads);
        Assert.Equal(10, notifications);
        Assert.Equal(80, element.ActualHeight);
    }

    private sealed class ActualSizeReadCountingElement : FrameworkElement
    {
        public int HeightReads;
        public override object? GetValue(DependencyProperty property)
        {
            if (property == ActualHeightProperty) HeightReads++;
            return base.GetValue(property);
        }
    }

    [Fact]
    public void ExplicitMeasureInvalidation_RefreshesCoercedLineMetrics()
    {
        double lineHeight = 20;
        TextBlock.LineHeightProperty.OverrideMetadata(typeof(CoercedLineHeightText),
            new PropertyMetadata(double.NaN, null, (_, _) => lineHeight));
        var text = new CoercedLineHeightText { Text = "Line metrics", FontSize = 10 };
        text.Measure(new Size(200, 100));
        Assert.Equal(22, text.DesiredSize.Height);
        lineHeight = 40;
        text.InvalidateMeasure();
        text.Measure(new Size(200, 100));
        Assert.Equal(42, text.DesiredSize.Height);
    }

    private sealed class CoercedLineHeightText : TextBlock { }

    [Fact]
    public void InheritedSource_DiscardsAnswersObservedDuringAFailedMetadataOverride()
    {
        var property = DependencyProperty.Register("RolledBackInheritance", typeof(double), typeof(ResizeLayoutOptimizationTests), new PropertyMetadata(10.0));
        property.OverrideMetadata(typeof(RollbackLeaf), new PropertyMetadata(20.0, null, null, inherits: true));
        var root = new StackPanel(); root.SetValue(property, 30.0);
        var barrier = new RollbackBarrier(); var child = new RollbackLeaf();
        root.Children.Add(barrier); barrier.Children.Add(child);
        Assert.Equal(20.0, child.GetValue(property));
        Assert.Throws<InvalidOperationException>(() => property.OverrideMetadata(typeof(RollbackBarrier),
            new FailingInheritanceMetadata(() => Assert.Equal(30.0, child.GetValue(property)))));
        Assert.Equal(20.0, child.GetValue(property));
    }

    private sealed class RollbackLeaf : Border { }
    private sealed class RollbackBarrier : StackPanel { }
    private sealed class FailingInheritanceMetadata(Action query) : PropertyMetadata(10.0, null, null, inherits: true)
    {
        protected override void OnApply(DependencyProperty property, Type targetType)
        {
            query();
            throw new InvalidOperationException("Test metadata rollback");
        }
    }

    private static void Layout(UIElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
    }

    private sealed class MeasuringElement : FrameworkElement
    {
        public int MeasureCount { get; private set; }
        protected override Size MeasureOverride(Size availableSize) { MeasureCount++; return new Size(30, 20); }
    }

    private sealed class FontReadCountingTextBlock : TextBlock
    {
        public int FontReads { get; private set; }
        public override object? GetValue(DependencyProperty property)
        {
            if (property == FontFamilyProperty || property == FontSizeProperty || property == FontWeightProperty || property == FontStyleProperty || property == LineHeightProperty)
                FontReads++;
            return base.GetValue(property);
        }
    }
}
