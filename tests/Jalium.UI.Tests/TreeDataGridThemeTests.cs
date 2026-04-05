using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class TreeDataGridThemeTests
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
    public void TreeDataGrid_ThemeStyle_ShouldApplyTemplateResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var controlBackgroundPressed = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackgroundPressed"]);
            var controlBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackground"]);

            var treeDataGrid = new TreeDataGrid();
            var host = new StackPanel { Width = 400, Height = 200 };
            host.Children.Add(treeDataGrid);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            Assert.True(app.Resources.TryGetValue(typeof(TreeDataGrid), out var styleObj));
            Assert.IsType<Style>(styleObj);

            AssertBrushMatches(controlBackgroundPressed, treeDataGrid.AlternatingRowBackground);

            var headersBorder = Assert.IsType<Border>(treeDataGrid.FindName("PART_ColumnHeadersBorder"));
            AssertBrushMatches(controlBackground, headersBorder.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeDataGridRow_Selected_ShouldUseAccentBrush()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var accentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrush"]);
            var accentText = Assert.IsAssignableFrom<Brush>(app.Resources["TextOnAccent"]);

            var row = new TreeDataGridRow { IsSelected = true };
            var host = new StackPanel { Width = 400, Height = 60 };
            host.Children.Add(row);

            host.Measure(new Size(400, 60));
            host.Arrange(new Rect(0, 0, 400, 60));

            AssertBrushMatches(accentBrush, row.Background);
            AssertBrushMatches(accentText, row.Foreground);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeDataGrid_TemplateParts_ShouldExist()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var treeDataGrid = new TreeDataGrid();
            var host = new StackPanel { Width = 400, Height = 200 };
            host.Children.Add(treeDataGrid);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            Assert.NotNull(treeDataGrid.FindName("PART_ColumnHeadersBorder"));
            Assert.NotNull(treeDataGrid.FindName("PART_ColumnHeadersScrollViewer"));
            Assert.NotNull(treeDataGrid.FindName("PART_ColumnHeadersHost"));
            Assert.NotNull(treeDataGrid.FindName("PART_DataScrollViewer"));
            Assert.NotNull(treeDataGrid.FindName("PART_RowsHost"));
            Assert.NotNull(treeDataGrid.FindName("PART_DragOverlay"));
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
