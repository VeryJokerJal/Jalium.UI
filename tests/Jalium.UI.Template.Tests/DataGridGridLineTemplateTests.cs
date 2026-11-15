using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DataGridGridLineTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllLineModesUpdateTemplateVisibilityGeometryAndOwnerBrushes(bool tree)
    {
        WithGrid(tree, grid =>
        {
            var horizontal = new SolidColorBrush(Color.FromRgb(180, 30, 90));
            var vertical = new SolidColorBrush(Color.FromRgb(20, 120, 180));
            SetBrushes(grid, horizontal, vertical);
            foreach (var mode in new[] { DataGridGridLinesVisibility.All, DataGridGridLinesVisibility.None,
                         DataGridGridLinesVisibility.Horizontal, DataGridGridLinesVisibility.Vertical, DataGridGridLinesVisibility.All })
            {
                SetMode(grid, mode);
                Layout(grid);
                var row = Rows(grid)[0];
                var cell = Cells(row)[0];
                var h = Assert.IsType<Border>(cell.FindName("PART_HorizontalGridLine"));
                var v = Assert.IsType<Border>(cell.FindName("PART_VerticalGridLine"));
                var showH = mode is DataGridGridLinesVisibility.Horizontal or DataGridGridLinesVisibility.All;
                var showV = mode is DataGridGridLinesVisibility.Vertical or DataGridGridLinesVisibility.All;
                Assert.Equal(mode, cell.GridLinesVisibility);
                Assert.Equal(showH ? Visibility.Visible : Visibility.Collapsed, h.Visibility);
                Assert.Equal(showV ? Visibility.Visible : Visibility.Collapsed, v.Visibility);
                Assert.Equal(showH ? 1 : 0, h.DesiredSize.Height);
                Assert.Equal(showV ? 1 : 0, v.DesiredSize.Width);
                if (showH) Assert.Equal(1, h.ActualHeight);
                if (showV) Assert.Equal(1, v.ActualWidth);
                Assert.Same(horizontal, h.Background);
                Assert.Same(vertical, v.Background);
                var rowBorder = Assert.IsType<Border>(row.FindName("PART_RowBorder"));
                Assert.Equal(showH ? 1 : 0, rowBorder.BorderThickness.Bottom);
                Assert.Same(horizontal, rowBorder.BorderBrush);
            }
            ThemeManager.ApplyTheme(ThemeVariant.Dark);
            Layout(grid);
            Assert.Same(horizontal, Assert.IsType<Border>(Cells(Rows(grid)[0])[0].FindName("PART_HorizontalGridLine")).Background);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowAndCellOverridesWinAndClearingThemRestoresCurrentOwnerValues(bool tree)
    {
        WithGrid(tree, grid =>
        {
            var row = Rows(grid)[0];
            var cell = Cells(row)[0];
            row.SetValue(DataGridRow.GridLinesVisibilityProperty, DataGridGridLinesVisibility.Vertical);
            Layout(grid);
            Assert.Equal(DataGridGridLinesVisibility.Vertical, cell.GridLinesVisibility);
            cell.GridLinesVisibility = DataGridGridLinesVisibility.None;
            var ownBrush = new SolidColorBrush(Color.FromRgb(20, 170, 70));
            cell.HorizontalGridLinesBrush = ownBrush;
            SetMode(grid, DataGridGridLinesVisibility.Horizontal);
            SetBrushes(grid, new SolidColorBrush(Color.FromRgb(170, 40, 70)), new SolidColorBrush(Color.FromRgb(0, 80, 160)));
            Layout(grid);
            Assert.Equal(DataGridGridLinesVisibility.None, cell.GridLinesVisibility);
            Assert.Same(ownBrush, Assert.IsType<Border>(cell.FindName("PART_HorizontalGridLine")).Background);
            cell.ClearValue(DataGridCell.GridLinesVisibilityProperty);
            Assert.Equal(DataGridGridLinesVisibility.Vertical, cell.GridLinesVisibility);
            row.ClearValue(DataGridRow.GridLinesVisibilityProperty);
            Assert.Equal(DataGridGridLinesVisibility.Horizontal, cell.GridLinesVisibility);
            cell.ClearValue(DataGridCell.HorizontalGridLinesBrushProperty);
            Assert.NotSame(ownBrush, cell.HorizontalGridLinesBrush);
        });
    }

    [Fact]
    public void TemplateLinesReserveTheirPixelAndCustomTemplatesMayOmitThem()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        var app = Application.Current ?? new Application();
        var cell = new DataGridCell { Content = "Value", GridLinesVisibility = DataGridGridLinesVisibility.None };
        cell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var noLines = cell.DesiredSize;
        cell.GridLinesVisibility = DataGridGridLinesVisibility.All;
        cell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(noLines.Width + 1, cell.DesiredSize.Width, 4);
        Assert.Equal(noLines.Height + 1, cell.DesiredSize.Height, 4);
        cell.Template = new ControlTemplate(typeof(DataGridCell));
        cell.Template.SetVisualTree(static () => new Border());
        Layout(cell);
        cell.GridLinesVisibility = DataGridGridLinesVisibility.Vertical;
        Layout(cell);
        Assert.Null(cell.FindName("PART_HorizontalGridLine"));
        GC.KeepAlive(app);
    }

    [Fact]
    public void CellFocusableDefaultCanBeOverriddenByAConsumerStyle()
    {
        var app = Application.Current ?? new Application();
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, true));
        var cell = new DataGridCell { Style = style };
        Layout(cell);
        Assert.True(cell.Focusable);
        GC.KeepAlive(app);
    }

    private static void WithGrid(bool tree, Action<Control> action)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        var app = Application.Current ?? new Application();
        var previous = ThemeManager.CurrentTheme;
        try
        {
            ThemeManager.ApplyTheme(ThemeVariant.Light);
            Control grid = tree ? new TreeDataGrid() : new DataGrid { AutoGenerateColumns = false };
            var column = new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(Item.Name)), Width = 200 };
            var items = new[] { new Item("Alpha"), new Item("Beta") };
            if (grid is DataGrid data) { data.Columns.Add(column); data.ItemsSource = items; }
            else { var dataTree = (TreeDataGrid)grid; dataTree.Columns.Add(column); dataTree.ItemsSource = items; }
            Layout(grid);
            action(grid);
        }
        finally { ThemeManager.ApplyTheme(previous); GC.KeepAlive(app); }
    }
    private static void SetMode(Control grid, DataGridGridLinesVisibility value)
    {
        if (grid is DataGrid data) data.GridLinesVisibility = value;
        else ((TreeDataGrid)grid).GridLinesVisibility = value;
    }
    private static void SetBrushes(Control grid, Brush horizontal, Brush vertical)
    {
        if (grid is DataGrid data) { data.HorizontalGridLinesBrush = horizontal; data.VerticalGridLinesBrush = vertical; }
        else { var tree = (TreeDataGrid)grid; tree.HorizontalGridLinesBrush = horizontal; tree.VerticalGridLinesBrush = vertical; }
    }
    private static Control[] Rows(Control grid) => ((IDictionary)grid.GetType()
        .GetField("_realizedRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!).Values.Cast<Control>().ToArray();
    private static DataGridCell[] Cells(Control row) => Assert.IsType<StackPanel>(row.FindName("PART_CellsPanel"))
        .Children.OfType<DataGridCell>().ToArray();
    private static void Layout(UIElement control)
    {
        control.Measure(new Size(480, 250)); control.Arrange(new Rect(0, 0, 480, 250)); control.UpdateLayout();
    }
    private sealed record Item(string Name);
}
