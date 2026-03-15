using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
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
            var controlBackgroundPressed = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackgroundPressed"]);
            var controlBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackground"]);

            var dataGrid = new DataGrid();
            var host = new StackPanel { Width = 400, Height = 200 };
            host.Children.Add(dataGrid);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            Assert.True(app.Resources.TryGetValue(typeof(DataGrid), out var styleObj));
            Assert.IsType<Style>(styleObj);

            AssertBrushMatches(controlBackgroundPressed, dataGrid.AlternatingRowBackground);

            var headersBorder = Assert.IsType<Border>(dataGrid.FindName("PART_ColumnHeadersBorder"));
            AssertBrushMatches(controlBackground, headersBorder.Background);
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
            var accentText = Assert.IsAssignableFrom<Brush>(app.Resources["TextOnAccent"]);
            var controlBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackground"]);

            var row = new DataGridRow { IsSelected = true };
            var header = new Jalium.UI.Controls.DataGridColumnHeader { Content = "Name" };
            var host = new StackPanel { Width = 400, Height = 120 };
            host.Children.Add(row);
            host.Children.Add(header);

            host.Measure(new Size(400, 120));
            host.Arrange(new Rect(0, 0, 400, 120));

            AssertBrushMatches(accentBrush, row.Background);
            AssertBrushMatches(accentText, row.Foreground);
            AssertBrushMatches(controlBackground, header.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DataGridPrimitiveResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var accentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrush"]);
            var accentText = Assert.IsAssignableFrom<Brush>(app.Resources["TextOnAccent"]);
            var controlBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackground"]);
            var controlBackgroundPressed = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackgroundPressed"]);
            var controlBorder = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBorder"]);
            var textPrimary = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);

            var rowHeader = new PrimitiveDataGridRowHeader();
            var columnHeader = new PrimitiveDataGridColumnHeader();

            Assert.Same(accentBrush, InvokePrivateBrushResolver(rowHeader, "ResolveSelectedBackgroundBrush"));
            Assert.Same(controlBackgroundPressed, InvokePrivateBrushResolver(rowHeader, "ResolvePressedBackgroundBrush"));
            Assert.Same(controlBackground, InvokePrivateBrushResolver(rowHeader, "ResolveDefaultBackgroundBrush"));
            Assert.Same(controlBorder, InvokePrivateBrushResolver(rowHeader, "ResolveSeparatorBrush"));
            Assert.Same(accentText, InvokePrivateBrushResolver(rowHeader, "ResolveSelectionIndicatorBrush"));

            Assert.Same(controlBackgroundPressed, InvokePrivateBrushResolver(columnHeader, "ResolvePressedBackgroundBrush"));
            Assert.Same(controlBackground, InvokePrivateBrushResolver(columnHeader, "ResolveDefaultBackgroundBrush"));
            Assert.Same(textPrimary, InvokePrivateBrushResolver(columnHeader, "ResolveForegroundBrush"));
            Assert.Same(controlBorder, InvokePrivateBrushResolver(columnHeader, "ResolveSeparatorBrush"));

            var borderPen = InvokePrivatePenResolver(columnHeader, "ResolveBottomBorderPen");
            Assert.Same(controlBorder, borderPen.Brush);
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
            var header = new Jalium.UI.Controls.DataGridColumnHeader
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
            var resizeGrip = Assert.IsType<Border>(header.FindName("PART_ResizeGrip"));

            Assert.Equal(1, headerBorder.BorderThickness.Right);
            Assert.False(resizeGrip.IsHitTestVisible);
            Assert.Equal(8, resizeGrip.Width);
            Assert.Equal(0, resizeGrip.BorderThickness.Left);
            Assert.Equal(0, resizeGrip.BorderThickness.Right);
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
                Binding = new Jalium.UI.Controls.Binding { Path = "Name" },
                Width = 120
            });
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Age",
                Binding = new Jalium.UI.Controls.Binding { Path = "Age" },
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
            var originalHeader = Assert.IsType<Jalium.UI.Controls.DataGridColumnHeader>(headersHost.Children[0]);
            var resizedColumn = dataGrid.Columns[0];

            dataGrid.ResizeColumn(resizedColumn, 180);

            Assert.Same(originalHeader, headersHost.Children[0]);
            Assert.Equal(180, resizedColumn.Width);
            Assert.Equal(180, ((Jalium.UI.Controls.DataGridColumnHeader)headersHost.Children[0]).Width);
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

    private static Brush InvokePrivateBrushResolver(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(target, null));
    }

    private static Pen InvokePrivatePenResolver(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<Pen>(method!.Invoke(target, null));
    }
}
