using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DataGridCellRenderingTests
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

    private sealed class EmployeeRecord
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int Years { get; set; }
    }

    private static List<T> Descendants<T>(Visual root) where T : Visual
    {
        var result = new List<T>();
        void Walk(Visual v)
        {
            var count = v.VisualChildrenCount;
            for (var i = 0; i < count; i++)
            {
                if (v.GetVisualChild(i) is Visual child)
                {
                    if (child is T match) result.Add(match);
                    Walk(child);
                }
            }
        }
        Walk(root);
        return result;
    }

    [Fact]
    public void DataGrid_AutoGenerate_RealizesCellsWithVisibleText()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = true,
                ItemsSource = new[]
                {
                    new EmployeeRecord { Name = "Ava Chen",   Role = "Engineer", Years = 4 },
                    new EmployeeRecord { Name = "Liam Patel", Role = "Designer", Years = 6 },
                    new EmployeeRecord { Name = "Noah Kim",   Role = "Manager",  Years = 9 },
                },
                SelectedIndex = 0,
                Width = 452,
                Height = 190,
            };

            var host = new StackPanel { Width = 480, Height = 240 };
            host.Children.Add(grid);

            host.Measure(new Size(480, 240));
            host.Arrange(new Rect(0, 0, 480, 240));
            // second pass so realized rows/cells get measured+arranged
            host.Measure(new Size(480, 240));
            host.Arrange(new Rect(0, 0, 480, 240));

            // 1. Columns should be auto-generated from the item's 3 public props.
            Assert.Equal(3, grid.Columns.Count);

            // 2. Rows should be realized.
            var rowsHost = Assert.IsType<StackPanel>(grid.FindName("PART_RowsHost"));
            var rows = Descendants<DataGridRow>(rowsHost);
            Assert.NotEmpty(rows);

            // 3. Each realized row should have 3 cells whose content is a TextBlock.
            var firstRow = rows.First(r => r.DataItem != null);
            var cells = Descendants<DataGridCell>(firstRow);
            Assert.Equal(3, cells.Count);

            var cellTexts = cells
                .Select(c => (c.Content as TextBlock)?.Text)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();
            Assert.Contains("Ava Chen", cellTexts);
            Assert.Contains("Engineer", cellTexts);
            Assert.Contains("4", cellTexts);

            // 4. The TextBlocks must be realized into the visual tree with a
            //    non-zero rendered size (this is what "no visible content" would
            //    break: content exists on the model but never lays out/paints).
            var textBlocks = Descendants<TextBlock>(firstRow);
            Assert.NotEmpty(textBlocks);
            var avaText = textBlocks.FirstOrDefault(t => t.Text == "Ava Chen");
            Assert.NotNull(avaText);
            Assert.True(avaText!.RenderSize.Width > 0,
                $"Ava Chen TextBlock has zero render width (RenderSize={avaText.RenderSize}).");
            Assert.True(avaText.RenderSize.Height > 0,
                $"Ava Chen TextBlock has zero render height (RenderSize={avaText.RenderSize}).");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DataGrid_ExplicitColumns_RealizesCellsWithVisibleText()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                Width = 452,
                Height = 190,
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Jalium.UI.Data.Binding("Name"), Width = 200 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Jalium.UI.Data.Binding("Role"), Width = 120 });
            grid.ItemsSource = new[]
            {
                new EmployeeRecord { Name = "Ava Chen",   Role = "Engineer", Years = 4 },
                new EmployeeRecord { Name = "Liam Patel", Role = "Designer", Years = 6 },
            };

            var host = new StackPanel { Width = 480, Height = 240 };
            host.Children.Add(grid);

            host.Measure(new Size(480, 240));
            host.Arrange(new Rect(0, 0, 480, 240));
            host.Measure(new Size(480, 240));
            host.Arrange(new Rect(0, 0, 480, 240));

            Assert.Equal(2, grid.Columns.Count);

            var rowsHost = Assert.IsType<StackPanel>(grid.FindName("PART_RowsHost"));
            var rows = Descendants<DataGridRow>(rowsHost);
            Assert.NotEmpty(rows);

            var firstRow = rows.First(r => r.DataItem != null);
            var textBlocks = Descendants<TextBlock>(firstRow);
            var avaText = textBlocks.FirstOrDefault(t => t.Text == "Ava Chen");
            Assert.NotNull(avaText);
            Assert.True(avaText!.RenderSize.Width > 0 && avaText.RenderSize.Height > 0,
                $"Ava Chen TextBlock render size is degenerate (RenderSize={avaText.RenderSize}).");

            // Header text should also be realized.
            var headersHost = Assert.IsType<StackPanel>(grid.FindName("PART_ColumnHeadersHost"));
            var headerTexts = Descendants<TextBlock>(headersHost);
            Assert.Contains(headerTexts, t => t.Text == "Name");
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
