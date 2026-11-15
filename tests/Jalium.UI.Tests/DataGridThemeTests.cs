using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Media;

using PrimitiveDataGridColumnHeader = Jalium.UI.Controls.Primitives.DataGridColumnHeader;
using PrimitiveDataGridRowHeader = Jalium.UI.Controls.Primitives.DataGridRowHeader;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DataGridThemeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void DataGrid_ThemeStyle_ShouldApplyTemplateResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var alternatingRowBackground = Assert.IsAssignableFrom<Brush>(
                app.Resources["SubtleFillColorSecondaryBrush"]);
            var headerBackground = Assert.IsAssignableFrom<Brush>(
                app.Resources["LayerFillColorAltBrush"]);

            var dataGrid = new DataGrid();
            var host = new StackPanel { Width = 400, Height = 200 };
            host.Children.Add(dataGrid);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            Assert.True(app.Resources.TryGetValue(typeof(DataGrid), out var styleObj));
            Assert.IsType<Style>(styleObj);

            AssertBrushMatches(alternatingRowBackground, dataGrid.AlternatingRowBackground);

            var headersSurface = Assert.IsType<Grid>(dataGrid.FindName("PART_ColumnHeadersBorder"));
            AssertBrushMatches(headerBackground, headersSurface.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DataGridRow_And_Header_ShouldUseThemeSelectionAndHeaderBrushes()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var accentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrush"]);
            var textPrimary = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);
            var headerBackground = Assert.IsAssignableFrom<Brush>(
                app.Resources["LayerFillColorAltBrush"]);

            var row = new DataGridRow { IsSelected = true };
            var header = new PrimitiveDataGridColumnHeader { Content = "Name" };
            var host = new StackPanel { Width = 400, Height = 120 };
            host.Children.Add(row);
            host.Children.Add(header);

            host.Measure(new Size(400, 120));
            host.Arrange(new Rect(0, 0, 400, 120));

            var selectionTint = Assert.IsType<Grid>(row.FindName("PART_SelectionTint"));
            AssertBrushMatches(accentBrush, selectionTint.Background);
            Assert.Equal(0.12, selectionTint.Opacity);
            Assert.Equal(Visibility.Visible, selectionTint.Visibility);
            AssertBrushMatches(textPrimary, row.Foreground);
            AssertBrushMatches(headerBackground, header.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DataGridPrimitiveTemplates_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var accentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrush"]);
            var headerBackground = Assert.IsAssignableFrom<Brush>(app.Resources["LayerFillColorAltBrush"]);
            var controlBorder = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBorder"]);
            var textPrimary = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);

            var rowHeader = new PrimitiveDataGridRowHeader();
            var columnHeader = new PrimitiveDataGridColumnHeader { Content = "Name" };

            rowHeader.IsRowSelected = true;
            rowHeader.Measure(new Size(30, 30));
            rowHeader.Arrange(new Rect(0, 0, 30, 30));
            var selectionTint = Assert.IsType<Grid>(rowHeader.FindName("PART_SelectionTint"));
            AssertBrushMatches(accentBrush, selectionTint.Background);
            Assert.Equal(Visibility.Visible, selectionTint.Visibility);
            AssertBrushMatches(controlBorder, rowHeader.SeparatorBrush);
            AssertBrushMatches(textPrimary, rowHeader.Foreground);

            columnHeader.Measure(new Size(120, 32));
            columnHeader.Arrange(new Rect(0, 0, 120, 32));
            var headerBorder = Assert.IsType<Border>(columnHeader.FindName("PART_HeaderBorder"));
            AssertBrushMatches(headerBackground, headerBorder.Background);
            AssertBrushMatches(controlBorder, headerBorder.BorderBrush);
            AssertBrushMatches(textPrimary, columnHeader.Foreground);
            Assert.Equal(1, headerBorder.BorderThickness.Right);

        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DataGridColumnHeader_Template_ShouldUseSingleVisibleSeparator()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var header = new PrimitiveDataGridColumnHeader
            {
                Content = "Name",
                Width = 120,
                Height = 32
            };

            var host = new StackPanel { Width = 200, Height = 80 };
            host.Children.Add(header);

            host.Measure(new Size(200, 80));
            host.Arrange(new Rect(0, 0, 200, 80));

            var headerBorder = Assert.IsType<Border>(header.FindName("PART_HeaderBorder"));
            var resizeGrip = Assert.IsType<Jalium.UI.Shapes.Rectangle>(header.FindName("PART_ResizeGrip"));

            Assert.Equal(1, headerBorder.BorderThickness.Right);
            Assert.False(resizeGrip.IsHitTestVisible);
            Assert.Equal(8, resizeGrip.Width);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DataGrid_ResizeColumn_ShouldPreserveHeaderInstanceDuringDrag()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var dataGrid = new DataGrid
            {
                Width = 320,
                Height = 160
            };
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Name",
                Binding = new Jalium.UI.Data.Binding("Name"),
                Width = 120
            });
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Age",
                Binding = new Jalium.UI.Data.Binding("Age"),
                Width = 80
            });
            dataGrid.ItemsSource = new[]
            {
                new { Name = "Alice", Age = 30 }
            };

            var host = new StackPanel { Width = 400, Height = 240 };
            host.Children.Add(dataGrid);

            host.Measure(new Size(400, 240));
            host.Arrange(new Rect(0, 0, 400, 240));

            var headersHost = Assert.IsType<StackPanel>(dataGrid.FindName("PART_ColumnHeadersHost"));
            var originalHeader = Assert.IsType<PrimitiveDataGridColumnHeader>(headersHost.Children[0]);
            var resizedColumn = dataGrid.Columns[0];

            dataGrid.ResizeColumn(resizedColumn, 180);

            Assert.Same(originalHeader, headersHost.Children[0]);
            Assert.Equal(180, resizedColumn.Width);
            Assert.Equal(180, ((PrimitiveDataGridColumnHeader)headersHost.Children[0]).Width);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void AssertBrushMatches(Brush expected, Brush? actual)
    {
        var actualBrush = Assert.IsAssignableFrom<Brush>(actual);

        if (expected is SolidColorBrush expectedSolid && actualBrush is SolidColorBrush actualSolid)
        {
            Assert.Equal(expectedSolid.Color, actualSolid.Color);
            return;
        }

        Assert.Same(expected, actualBrush);
    }

}
