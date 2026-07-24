using Jalium.UI;
using Jalium.UI.Controls;
using System.Reflection;

namespace Jalium.UI.Tests;

public class GridLayoutTests
{
    [Fact]
    public void Grid_RepeatedLayoutReusesImplicitDefinitionsAndTrackBuffers()
    {
        var grid = new Grid();
        grid.Children.Add(new Border { Width = 80, Height = 24 });
        grid.Measure(new Size(320, 200));
        grid.Arrange(new Rect(0, 0, 320, 200));

        var fieldNames = new[]
        {
            "_effectiveRowDefinitions",
            "_effectiveColumnDefinitions",
            "_rowHeights",
            "_columnWidths",
            "_rowStarValues",
            "_columnStarValues",
            "_rowContent",
            "_columnContent",
        };
        var fields = fieldNames.Select(name => typeof(Grid).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)!).ToArray();
        var originalStorage = fields.Select(field => field.GetValue(grid)).ToArray();

        for (var pass = 0; pass < 8; pass++)
        {
            var width = (pass & 1) == 0 ? 321 : 320;
            grid.Measure(new Size(width, 200));
            grid.Arrange(new Rect(0, 0, width, 200));
        }

        for (var index = 0; index < fields.Length; index++)
        {
            Assert.Same(originalStorage[index], fields[index].GetValue(grid));
        }

        Assert.False(grid.ShouldSerializeRowDefinitions());
        Assert.False(grid.ShouldSerializeColumnDefinitions());
    }

    [Fact]
    public void Grid_AutoRow_ShouldTrackChildHeight_AfterFinalCellWidthMeasure()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 120 });

        var text = new TextBlock
        {
            Text = string.Join(" ", Enumerable.Repeat("longword", 40)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };

        grid.Children.Add(text);
        grid.Measure(new Size(500, double.PositiveInfinity));

        Assert.True(text.DesiredSize.Height > 30, $"Expected wrapped text height, got {text.DesiredSize.Height}");
        Assert.True(
            grid.DesiredSize.Height + 0.01 >= text.DesiredSize.Height,
            $"Grid height {grid.DesiredSize.Height} should include wrapped text height {text.DesiredSize.Height}");
    }

    // ---- Star-track DesiredSize regression suite ------------------------------------------------
    // A star track must report its CONTENT size as desired (not its full proportional allocation)
    // when measured under a finite constraint; otherwise a Grid balloons to fill any content-sizing
    // parent (WrapPanel / horizontal StackPanel / auto-sized Border|Button). The star allocation is
    // applied only at arrange. See Grid.MeasureOverride.

    [Fact]
    public void Grid_BareGrid_FiniteMeasure_ReportsContentDesiredSize_NotFullAvailable()
    {
        // Bare Grid => one implicit Star row + one implicit Star column.
        var grid = new Grid();
        grid.Children.Add(new Border { Width = 80, Height = 24 });

        grid.Measure(new Size(1000, 500));

        Assert.True(Math.Abs(grid.DesiredSize.Width - 80) < 1,
            $"Expected content width 80, got {grid.DesiredSize.Width}");
        Assert.True(Math.Abs(grid.DesiredSize.Height - 24) < 1,
            $"Expected content height 24, got {grid.DesiredSize.Height}");
    }

    [Fact]
    public void Grid_ExplicitStarTracks_FiniteMeasure_ReportsContentDesiredSize()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        grid.Children.Add(new Border { Width = 120, Height = 40 });

        grid.Measure(new Size(1000, 800));

        Assert.True(Math.Abs(grid.DesiredSize.Width - 120) < 1,
            $"Expected content width 120, got {grid.DesiredSize.Width}");
        Assert.True(Math.Abs(grid.DesiredSize.Height - 40) < 1,
            $"Expected content height 40, got {grid.DesiredSize.Height}");
    }

    [Fact]
    public void Grid_StarTracks_FillAvailable_AtArrange()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        grid.Children.Add(new Border { Width = 120, Height = 40 });

        grid.Measure(new Size(1000, 800));
        grid.Arrange(new Rect(0, 0, 1000, 800));

        // Content-based desired must NOT prevent star tracks from filling at arrange.
        Assert.True(Math.Abs(grid.ColumnDefinitions[0].ActualWidth - 1000) < 1,
            $"Star column should fill 1000 at arrange, got {grid.ColumnDefinitions[0].ActualWidth}");
        Assert.True(Math.Abs(grid.RowDefinitions[0].ActualHeight - 800) < 1,
            $"Star row should fill 800 at arrange, got {grid.RowDefinitions[0].ActualHeight}");
    }

    [Fact]
    public void Grid_StarTrack_InfiniteMeasure_ReportsContentDesiredSize()
    {
        // Pre-existing "treat star as Auto under infinity" behavior must be preserved.
        var grid = new Grid();
        grid.Children.Add(new Border { Width = 80, Height = 24 });

        grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.True(Math.Abs(grid.DesiredSize.Width - 80) < 1,
            $"Expected content width 80, got {grid.DesiredSize.Width}");
        Assert.True(Math.Abs(grid.DesiredSize.Height - 24) < 1,
            $"Expected content height 24, got {grid.DesiredSize.Height}");
    }

    [Fact]
    public void Grid_StarColumn_MinWidth_RaisesContentDesiredWidth()
    {
        // A star column's content-based desired must still honour an explicit MinWidth.
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star, MinWidth = 200 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new Border { Width = 50, Height = 30 });

        grid.Measure(new Size(1000, double.PositiveInfinity));

        Assert.True(grid.DesiredSize.Width >= 200 - 0.01,
            $"Star column MinWidth 200 should floor desired width, got {grid.DesiredSize.Width}");
    }

    [Fact]
    public void WrapPanel_WithBareGridChildren_SizesToContent_DoesNotBalloon()
    {
        // Reproduces the chip-explosion scenario: bare Grid wrappers inside a horizontal WrapPanel
        // measured with a finite width and infinite height.
        var wrap = new WrapPanel();
        for (int i = 0; i < 3; i++)
        {
            var g = new Grid();
            g.Children.Add(new Border { Width = 60, Height = 20 });
            wrap.Children.Add(g);
        }

        wrap.Measure(new Size(1000, double.PositiveInfinity));

        // Three 60-wide chips fit on a single 20-tall line. Before the fix each bare Grid reported
        // 1000 wide, forcing one chip per line => the panel ballooned to ~3 lines tall and full width.
        Assert.True(wrap.DesiredSize.Height < 40,
            $"WrapPanel ballooned vertically: height={wrap.DesiredSize.Height}");
        Assert.True(wrap.DesiredSize.Width < 500,
            $"WrapPanel ballooned horizontally: width={wrap.DesiredSize.Width}");
    }
}
