using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class GridParityTests
{
    [Fact]
    public void GridDeclaresWpfSerializationAndDebugLineSurface()
    {
        Assert.Equal(typeof(bool), Grid.ShowGridLinesProperty.PropertyType);
        Assert.Equal(typeof(bool), Grid.IsSharedSizeScopeProperty.PropertyType);

        var grid = new Grid();
        Assert.False(grid.ShowGridLines);
        Assert.False(grid.ShouldSerializeColumnDefinitions());
        Assert.False(grid.ShouldSerializeRowDefinitions());

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        grid.ShowGridLines = true;
        grid.Measure(new Size(100, 50));
        grid.Arrange(new Rect(0, 0, 100, 50));
        var drawingContext = new TextEffects.RecordingDrawingContext();

        grid.Render(drawingContext);

        Assert.True(grid.ShouldSerializeColumnDefinitions());
        Assert.True(grid.ShouldSerializeRowDefinitions());
        Assert.Equal(2, drawingContext.Events.Count(entry => entry == "DrawLine"));
    }

    [Fact]
    public void SharedSizeScopeUsesLargestContributionAndInvalidatesEarlierGrid()
    {
        var scope = new StackPanel();
        Grid.SetIsSharedSizeScope(scope, true);
        var first = CreateSharedGrid(40);
        var second = CreateSharedGrid(90);
        scope.Children.Add(first);
        scope.Children.Add(second);

        first.Measure(new Size(500, 100));
        second.Measure(new Size(500, 100));
        first.Measure(new Size(500, 100));
        first.Arrange(new Rect(0, 0, first.DesiredSize.Width, first.DesiredSize.Height));
        second.Arrange(new Rect(0, 0, second.DesiredSize.Width, second.DesiredSize.Height));

        Assert.True(Grid.GetIsSharedSizeScope(scope));
        Assert.Equal(90, first.ColumnDefinitions[0].ActualWidth);
        Assert.Equal(90, second.ColumnDefinitions[0].ActualWidth);
        Assert.Equal(90, first.DesiredSize.Width);
        Assert.Equal(90, second.DesiredSize.Width);
    }

    [Fact]
    public void RowsAndColumnsWithTheSameGroupNameShareOneRegistry()
    {
        var scope = new StackPanel();
        Grid.SetIsSharedSizeScope(scope, true);

        var columnGrid = CreateSharedGrid(90);
        columnGrid.ColumnDefinitions[0].SharedSizeGroup = "CrossAxis";
        var rowGrid = new Grid();
        rowGrid.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
            SharedSizeGroup = "CrossAxis"
        });
        rowGrid.Children.Add(new Border { Width = 10, Height = 60 });
        scope.Children.Add(columnGrid);
        scope.Children.Add(rowGrid);

        columnGrid.Measure(new Size(500, 500));
        rowGrid.Measure(new Size(500, 500));
        columnGrid.Measure(new Size(500, 500));
        columnGrid.Arrange(new Rect(0, 0, 90, 90));
        rowGrid.Arrange(new Rect(0, 0, 90, 90));

        Assert.Equal(90, columnGrid.ColumnDefinitions[0].ActualWidth);
        Assert.Equal(90, rowGrid.RowDefinitions[0].ActualHeight);
    }

    [Fact]
    public void SharedMaximumOverridesAnIndividualMembersSmallerMaximum()
    {
        var scope = new StackPanel();
        Grid.SetIsSharedSizeScope(scope, true);
        var first = CreateSharedGrid(40);
        first.ColumnDefinitions[0].MaxWidth = 50;
        var second = CreateSharedGrid(90);
        second.ColumnDefinitions[0].MaxWidth = 60;
        scope.Children.Add(first);
        scope.Children.Add(second);

        first.Measure(new Size(500, 100));
        second.Measure(new Size(500, 100));
        first.Measure(new Size(500, 100));
        first.Arrange(new Rect(0, 0, 60, 20));
        second.Arrange(new Rect(0, 0, 60, 20));

        Assert.Equal(60, first.ColumnDefinitions[0].ActualWidth);
        Assert.Equal(60, second.ColumnDefinitions[0].ActualWidth);
    }

    [Fact]
    public void SharedStarDefinitionsMeasureLikeAutoEvenUnderFiniteConstraints()
    {
        var scope = new StackPanel();
        Grid.SetIsSharedSizeScope(scope, true);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Star,
            SharedSizeGroup = "SharedStar"
        });
        grid.Children.Add(new ConstraintSensitiveElement());
        scope.Children.Add(grid);

        grid.Measure(new Size(100, 50));
        grid.Arrange(new Rect(0, 0, grid.DesiredSize.Width, 20));

        Assert.Equal(240, grid.DesiredSize.Width);
        Assert.Equal(240, grid.ColumnDefinitions[0].ActualWidth);
    }

    [Fact]
    public void SharedGroupNamesAreValidatedAndCollectionChangesDropStaleContributions()
    {
        var definition = new ColumnDefinition();
        foreach (var invalid in new[] { string.Empty, "1group", "has space", "dash-name" })
        {
            Assert.Throws<ArgumentException>(() => definition.SharedSizeGroup = invalid);
        }

        definition.SharedSizeGroup = "_Valid42";

        var scope = new StackPanel();
        Grid.SetIsSharedSizeScope(scope, true);
        var first = CreateSharedGrid(40);
        var second = CreateSharedGrid(90);
        scope.Children.Add(first);
        scope.Children.Add(second);
        first.Measure(new Size(500, 100));
        second.Measure(new Size(500, 100));
        first.Measure(new Size(500, 100));
        Assert.Equal(90, first.DesiredSize.Width);

        second.ColumnDefinitions.Clear();
        first.Measure(new Size(500, 100));
        Assert.Equal(40, first.DesiredSize.Width);

        var ownerA = new Grid();
        var ownerB = new Grid();
        var sharedDefinition = new ColumnDefinition();
        ownerA.ColumnDefinitions.Add(sharedDefinition);
        Assert.Throws<ArgumentException>(() => ownerB.ColumnDefinitions.Add(sharedDefinition));
    }

    [Fact]
    public void EqualSharedGroupNamesInDifferentScopesRemainIndependent()
    {
        var firstScope = new StackPanel();
        var secondScope = new StackPanel();
        Grid.SetIsSharedSizeScope(firstScope, true);
        Grid.SetIsSharedSizeScope(secondScope, true);
        var first = CreateSharedGrid(40);
        var second = CreateSharedGrid(90);
        firstScope.Children.Add(first);
        secondScope.Children.Add(second);

        first.Measure(new Size(500, 100));
        second.Measure(new Size(500, 100));

        Assert.Equal(40, first.ColumnDefinitions[0].ActualWidth);
        Assert.Equal(90, second.ColumnDefinitions[0].ActualWidth);
    }

    private static Grid CreateSharedGrid(double childWidth)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "SharedColumn"
        });
        grid.Children.Add(new Border { Width = childWidth, Height = 10 });
        return grid;
    }

    private sealed class ConstraintSensitiveElement : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize) =>
            new(double.IsPositiveInfinity(availableSize.Width) ? 240 : 50, 10);
    }
}
