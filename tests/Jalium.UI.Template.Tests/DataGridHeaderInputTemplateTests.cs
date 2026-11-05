using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DataGridHeaderInputTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewAndBubbleMouseClickSortsExactlyOnce(bool tree)
    {
        var grid = CreateGrid(tree);
        var column = Columns(grid)[0];
        var header = Header(grid);
        var clicks = 0;
        header.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => ++clicks), true);
        var p = RootPoint(header, 30);
        MouseButton(header, p, true);
        MouseButton(header, p, false);
        Assert.Equal(1, clicks);
        Assert.Equal(System.ComponentModel.ListSortDirection.Ascending, column.SortDirection);
        Assert.False(header.IsPressed);
        Assert.False(header.IsMouseCaptured);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MouseReleaseCommitsADragBeforeButtonBaseReleasesCapture(bool tree)
    {
        var grid = CreateGrid(tree);
        var column = Columns(grid)[0];
        var header = Header(grid);
        var clicks = 0;
        header.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => ++clicks), true);
        var p = RootPoint(header, 30);
        try
        {
            MouseButton(header, p, true);
            header.RaiseEvent(new MouseEventArgs(UIElement.MouseMoveEvent, new Point(p.X + 20, p.Y),
                MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
                MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 0));
            MouseButton(header, new Point(399, p.Y), false);
            Assert.Same(column, Columns(grid)[^1]);
            Assert.Equal(0, clicks);
            Assert.False(header.IsPressed);
            Assert.False(header.IsMouseCaptured);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Canvas>(grid.FindName("PART_DragOverlay")).Visibility);
        }
        finally { header.ReleaseMouseCapture(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureLossCancelsWithoutSortingOrCommittingAReorder(bool tree)
    {
        var grid = CreateGrid(tree);
        var header = Header(grid);
        var source = Columns(grid)[0];
        var p = RootPoint(header, 30);
        MouseButton(header, p, true);
        Move(header, new Point(p.X + 20, p.Y));
        header.ReleaseMouseCapture();
        MouseButton(header, new Point(399, p.Y), false);
        Assert.Same(source, Columns(grid)[0]);
        Assert.Null(source.SortDirection);
        Assert.False(header.IsPressed);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Canvas>(grid.FindName("PART_DragOverlay")).Visibility);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AResizeReleaseKeepsItsWidthAndDoesNotSort(bool tree)
    {
        var grid = CreateGrid(tree);
        var header = Header(grid);
        var source = Columns(grid)[0];
        var p = RootPoint(header, header.ActualWidth - 2);
        var oldWidth = source.ActualWidth;
        MouseButton(header, p, true);
        Move(header, new Point(p.X + 20, p.Y));
        MouseButton(header, new Point(p.X + 20, p.Y), false);
        Assert.Equal(oldWidth + 20, source.ActualWidth);
        Assert.Same(header, Header(grid));
        Assert.Null(source.SortDirection);
        Assert.False(header.IsMouseCaptured);
    }

    private static void Move(UIElement target, Point point) => target.RaiseEvent(new MouseEventArgs(
        UIElement.MouseMoveEvent, point, MouseButtonState.Pressed, MouseButtonState.Released,
        MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 0));

    private static void MouseButton(UIElement target, Point point, bool down)
    {
        var state = down ? MouseButtonState.Pressed : MouseButtonState.Released;
        var args = new MouseButtonEventArgs(down ? UIElement.PreviewMouseDownEvent : UIElement.PreviewMouseUpEvent,
            point, Jalium.UI.Input.MouseButton.Left, state, 1, state, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 0);
        target.RaiseEvent(args);
        args.RoutedEvent = down ? UIElement.MouseDownEvent : UIElement.MouseUpEvent;
        target.RaiseEvent(args);
    }
    private static Point RootPoint(FrameworkElement element, double x)
    {
        var point = new Point(x, 15);
        Visual? current = element;
        while (current is FrameworkElement part)
        {
            point = new Point(point.X + part.VisualBounds.X, point.Y + part.VisualBounds.Y);
            current = part.VisualParent;
        }
        return point;
    }
    private static Control CreateGrid(bool tree)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        var defaults = ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
        Control grid = tree ? new TreeDataGrid() : new DataGrid();
        grid.Resources.MergedDictionaries.Add(defaults);
        foreach (var name in new[] { "First", "Second", "Third" })
            Columns(grid).Add(new DataGridTextColumn { Header = name, Binding = new Binding(nameof(RowData.Name)), Width = 100 });
        var rows = new[] { new RowData { Name = "Beta" }, new RowData { Name = "Alpha" } };
        if (grid is DataGrid data) data.ItemsSource = rows; else ((TreeDataGrid)grid).ItemsSource = rows;
        grid.Measure(new Size(400, 250)); grid.Arrange(new Rect(0, 0, 400, 250)); grid.UpdateLayout();
        return grid;
    }
    private static IList<DataGridColumn> Columns(Control grid) => grid is DataGrid data ? data.Columns : ((TreeDataGrid)grid).Columns;
    private static DataGridColumnHeader Header(Control grid) =>
        Assert.IsType<StackPanel>(grid.FindName("PART_ColumnHeadersHost")).Children.OfType<DataGridColumnHeader>().First();
    private sealed class RowData { public string Name { get; set; } = ""; }
}
