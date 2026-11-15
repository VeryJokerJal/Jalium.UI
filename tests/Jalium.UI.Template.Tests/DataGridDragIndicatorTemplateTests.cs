using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DataGridDragIndicatorTemplateTests
{
    [Fact]
    public void IndicatorsHaveNoConstructorVisualsAndMissingOptionalSurfaceIsSafe()
    {
        var header = new DataGridColumnDragIndicator { Content = "Column" };
        var marker = new DataGridDropLocationIndicator();
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(header));
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(marker));
        Assert.False(header.Focusable);
        Assert.False(marker.IsHitTestVisible);
        var minimal = new ControlTemplate(typeof(DataGridColumnDragIndicator));
        minimal.SetVisualTree(() => new Grid());
        header.Template = minimal;
        Layout(header);
        header.IsEnabled = false;
        Layout(header);
        Assert.Null(header.FindName("PART_Surface"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReorderingReusesTheTemplatePartsAndReleasesTheirTransientContent(bool tree)
    {
        var grid = CreateGrid(tree);
        var columns = Columns(grid);
        var source = columns[0];
        var ghost = Assert.IsType<DataGridColumnDragIndicator>(grid.FindName("PART_DragGhost"));
        var marker = Assert.IsType<DataGridDropLocationIndicator>(grid.FindName("PART_DropIndicator"));
        var overlay = Assert.IsType<Canvas>(grid.FindName("PART_DragOverlay"));
        Assert.Equal(2, overlay.Children.Count);
        Start(grid, source);
        Layout(grid);
        Assert.Equal(Visibility.Visible, overlay.Visibility);
        Assert.Equal("First", ghost.Content);
        Assert.True(ghost.ActualWidth > 0);
        Invoke(grid, "UpdateColumnDrag", new Point(260, 0));
        Assert.Equal(Visibility.Visible, marker.Visibility);
        Assert.Equal(260 - ghost.Width / 2, Canvas.GetLeft(ghost));
        Invoke(grid, "EndColumnDrag", new Point(399, 0));
        Assert.Same(source, columns[^1]);
        Assert.Equal(Visibility.Collapsed, overlay.Visibility);
        Assert.Equal(Visibility.Collapsed, marker.Visibility);
        Assert.Null(ghost.Content);
        Assert.Equal(2, overlay.Children.Count);
        Assert.Same(ghost, grid.FindName("PART_DragGhost"));
        Start(grid, columns[0]);
        Invoke(grid, "CancelColumnDrag");
        Assert.False(IsDragging(grid));
        Assert.Same(ghost, grid.FindName("PART_DragGhost"));
        Assert.Equal(2, overlay.Children.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATemplateWithoutDragVisualsCanStillReorderAndRetirementCancelsTheOldDrag(bool tree)
    {
        var grid = CreateGrid(tree);
        var originalTemplate = grid.Template;
        var ghost = Assert.IsType<DataGridColumnDragIndicator>(grid.FindName("PART_DragGhost"));
        var overlay = Assert.IsType<Canvas>(grid.FindName("PART_DragOverlay"));
        Start(grid, Columns(grid)[0]);
        grid.Template = null;
        Assert.False(IsDragging(grid));
        Assert.Null(ghost.Content);
        Assert.Equal(Visibility.Collapsed, overlay.Visibility);
        var minimal = new ControlTemplate(grid.GetType());
        minimal.SetVisualTree(() => new StackPanel { Name = "PART_ColumnHeadersHost", Orientation = Orientation.Horizontal });
        grid.Template = minimal;
        Layout(grid);
        var source = Columns(grid)[0];
        Start(grid, source);
        Assert.True(IsDragging(grid));
        Invoke(grid, "UpdateColumnDrag", new Point(399, 0));
        Invoke(grid, "EndColumnDrag", new Point(399, 0));
        Assert.Same(source, Columns(grid)[^1]);
        grid.Template = originalTemplate;
        Layout(grid);
        Assert.NotSame(ghost, grid.FindName("PART_DragGhost"));
        Start(grid, Columns(grid)[0]);
        Invoke(grid, "CancelColumnDrag");
    }

    [Fact]
    public void OwnerStylesSupportControlAndBorderTargetsAndRespectExternalChanges()
    {
        var grid = Assert.IsType<DataGrid>(CreateGrid(false));
        var ghost = Assert.IsType<DataGridColumnDragIndicator>(grid.FindName("PART_DragGhost"));
        var marker = Assert.IsType<DataGridDropLocationIndicator>(grid.FindName("PART_DropIndicator"));
        var controlStyle = new Style(typeof(DataGridColumnDragIndicator));
        controlStyle.Setters.Add(new Setter(UIElement.OpacityProperty, .6));
        var surfaceStyle = new Style(typeof(Border));
        surfaceStyle.Setters.Add(new Setter(UIElement.OpacityProperty, .4));
        grid.DragIndicatorStyle = controlStyle;
        grid.DropLocationIndicatorStyle = surfaceStyle;
        Start(grid, grid.Columns[0]);
        Layout(grid);
        Assert.Same(controlStyle, ghost.Style);
        Assert.Equal(.6, ghost.Opacity);
        Assert.Same(surfaceStyle, marker.SurfaceStyle);
        Assert.Equal(.4, Assert.IsType<Border>(marker.FindName("PART_Surface")).Opacity);
        var external = new Style(typeof(DataGridColumnDragIndicator));
        external.Setters.Add(new Setter(UIElement.OpacityProperty, .8));
        ghost.Style = external;
        Invoke(grid, "CancelColumnDrag");
        Assert.Same(external, ghost.Style);
        Assert.Null(marker.SurfaceStyle);
        Assert.Equal(1, Assert.IsType<Border>(marker.FindName("PART_Surface")).Opacity);
        grid.DragIndicatorStyle = null!;
        grid.DropLocationIndicatorStyle = null!;
        Start(grid, grid.Columns[0]);
        Invoke(grid, "CancelColumnDrag");
        Assert.Same(external, ghost.Style);
    }

    private static Control CreateGrid(bool tree)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        var defaults = ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
        Control grid = tree ? new TreeDataGrid() : new DataGrid();
        grid.Resources.MergedDictionaries.Add(defaults);
        foreach (var header in new[] { "First", "Second", "Third" })
            Columns(grid).Add(new DataGridTextColumn { Header = header, Width = 100 });
        Layout(grid);
        return grid;
    }
    private static IList<DataGridColumn> Columns(Control grid) => grid is DataGrid data ? data.Columns : ((TreeDataGrid)grid).Columns;
    private static void Start(Control grid, DataGridColumn column)
    {
        var host = Assert.IsType<StackPanel>(grid.FindName("PART_ColumnHeadersHost"));
        var header = host.Children.OfType<DataGridColumnHeader>().First();
        Invoke(grid, "StartColumnDrag", header, column);
    }
    private static bool IsDragging(Control grid) => (bool)grid.GetType().GetField("_isColumnDragging", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!;
    private static void Invoke(object target, string method, params object?[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
    private static void Layout(UIElement element)
    {
        element.Measure(new Size(400, 250)); element.Arrange(new Rect(0, 0, 400, 250)); element.UpdateLayout();
    }
}
