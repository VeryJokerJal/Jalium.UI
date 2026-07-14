using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using ControlsDataTemplateSelector = Jalium.UI.Controls.DataTemplateSelector;
using DataGridColumnHeader = Jalium.UI.Controls.Primitives.DataGridColumnHeader;
using PrimitiveDataGridRowHeader = Jalium.UI.Controls.Primitives.DataGridRowHeader;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DataGridContainerStyleSizingValidationParityTests
{
    [Fact]
    public void PublicSurface_DeclaresTheContainerAppearanceSizingAndLayoutContracts()
    {
        Assert.True(typeof(ItemsControl).IsAssignableFrom(typeof(DataGrid)));

        AssertProperty<Style>(nameof(DataGrid.CellStyle), canWrite: true);
        AssertProperty<double>(nameof(DataGrid.CellsPanelHorizontalOffset), canWrite: false);
        AssertProperty<Style>(nameof(DataGrid.ColumnHeaderStyle), canWrite: true);
        AssertProperty<DataGridLength>(nameof(DataGrid.ColumnWidth), canWrite: true);
        AssertProperty<ScrollBarVisibility>(nameof(DataGrid.HorizontalScrollBarVisibility), canWrite: true);
        AssertProperty<double>(nameof(DataGrid.MaxColumnWidth), canWrite: true);
        AssertProperty<double>(nameof(DataGrid.MinColumnWidth), canWrite: true);
        AssertProperty<double>(nameof(DataGrid.MinRowHeight), canWrite: true);
        AssertProperty<Thickness>(nameof(DataGrid.NewItemMargin), canWrite: false);
        AssertProperty<double>(nameof(DataGrid.NonFrozenColumnsViewportHorizontalOffset), canWrite: false);
        AssertProperty<Style>(nameof(DataGrid.RowHeaderStyle), canWrite: true);
        AssertProperty<DataTemplate>(nameof(DataGrid.RowHeaderTemplate), canWrite: true);
        AssertProperty<ControlsDataTemplateSelector>(nameof(DataGrid.RowHeaderTemplateSelector), canWrite: true);
        AssertProperty<Style>(nameof(DataGrid.RowStyle), canWrite: true);
        AssertProperty<StyleSelector>(nameof(DataGrid.RowStyleSelector), canWrite: true);
        AssertProperty<ControlTemplate>(nameof(DataGrid.RowValidationErrorTemplate), canWrite: true);
        AssertProperty<System.Collections.ObjectModel.ObservableCollection<ValidationRule>>(
            nameof(DataGrid.RowValidationRules), canWrite: false);
        AssertProperty<ScrollBarVisibility>(nameof(DataGrid.VerticalScrollBarVisibility), canWrite: true);

        foreach (var propertyName in new[]
                 {
                     nameof(DataGrid.CellStyle), nameof(DataGrid.CellsPanelHorizontalOffset),
                     nameof(DataGrid.ColumnHeaderStyle), nameof(DataGrid.ColumnWidth),
                     nameof(DataGrid.HorizontalScrollBarVisibility), nameof(DataGrid.MaxColumnWidth),
                     nameof(DataGrid.MinColumnWidth), nameof(DataGrid.MinRowHeight),
                     nameof(DataGrid.NewItemMargin), nameof(DataGrid.NonFrozenColumnsViewportHorizontalOffset),
                     nameof(DataGrid.RowHeaderStyle), nameof(DataGrid.RowHeaderTemplate),
                     nameof(DataGrid.RowHeaderTemplateSelector), nameof(DataGrid.RowStyle),
                     nameof(DataGrid.RowStyleSelector), nameof(DataGrid.RowValidationErrorTemplate),
                     nameof(DataGrid.VerticalScrollBarVisibility)
                 })
        {
            Assert.Equal(
                typeof(DependencyProperty),
                typeof(DataGrid).GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static)!.FieldType);
        }

        AssertOverride(nameof(ProbeDataGrid.GetContainer), "GetContainerForItemOverride", Type.EmptyTypes);
        AssertOverride(nameof(ProbeDataGrid.IsOwnContainer), "IsItemItsOwnContainerOverride", [typeof(object)]);
        AssertOverride(nameof(ProbeDataGrid.Prepare), "PrepareContainerForItemOverride", [typeof(DependencyObject), typeof(object)]);
        AssertOverride(nameof(ProbeDataGrid.Clear), "ClearContainerForItemOverride", [typeof(DependencyObject), typeof(object)]);

        var handlesScrolling = typeof(DataGrid).GetProperty(
            "HandlesScrolling", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(handlesScrolling);
        Assert.True(handlesScrolling!.GetMethod!.IsVirtual);
    }

    [Fact]
    public void ContainerHooks_AreUsedForGeneratedRowsAndClearOwnerState()
    {
        var grid = new ProbeDataGrid();
        var item = new RowModel { Name = "row" };
        var loading = 0;
        var unloading = 0;
        grid.LoadingRow += (_, _) => loading++;
        grid.UnloadingRow += (_, _) => unloading++;

        Assert.False(grid.IsOwnContainer(item));
        var row = Assert.IsType<DataGridRow>(grid.GetContainer());
        Assert.True(grid.IsOwnContainer(row));

        grid.Prepare(row, item);

        Assert.Equal(1, loading);
        Assert.Same(item, row.DataItem);
        Assert.Same(item, row.DataContext);
        Assert.Same(grid, row.ParentDataGrid);

        grid.Clear(row, item);

        Assert.Equal(1, unloading);
        Assert.Null(row.DataItem);
        Assert.Null(row.ParentDataGrid);
        Assert.Null(row.RowHeader);
        Assert.Null(row.DataContext);
    }

    [Fact]
    public void RealizedLayout_AppliesStylesTemplatesConstraintsScrollBarsAndOffsets()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var item = new RowModel { Name = "row" };
            var rowStyle = new Style(typeof(DataGridRow));
            var fallbackRowStyle = new Style(typeof(DataGridRow));
            var cellStyle = new Style(typeof(DataGridCell));
            var secondCellStyle = new Style(typeof(DataGridCell));
            var columnHeaderStyle = new Style(typeof(DataGridColumnHeader));
            var secondColumnHeaderStyle = new Style(typeof(DataGridColumnHeader));
            var rowHeaderStyle = new Style(typeof(PrimitiveDataGridRowHeader));
            var rowHeaderTemplate = new DataTemplate();
            rowHeaderTemplate.SetVisualTree(() => new Border());
            var rowStyleSelector = new RecordingStyleSelector(rowStyle);
            var rowHeaderTemplateSelector = new RecordingTemplateSelector(rowHeaderTemplate);

            var firstColumn = new DataGridTextColumn { Header = "First" };
            var secondColumn = new DataGridTextColumn
            {
                Header = "Second",
                Width = 10,
                CellStyle = secondCellStyle,
                HeaderStyle = secondColumnHeaderStyle
            };
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                EnableRowVirtualization = false,
                HeadersVisibility = DataGridHeadersVisibility.All,
                RowHeaderWidth = 34,
                ColumnWidth = new DataGridLength(100),
                MinColumnWidth = 50,
                MaxColumnWidth = 70,
                MinRowHeight = 44,
                FrozenColumnCount = 1,
                CellStyle = cellStyle,
                ColumnHeaderStyle = columnHeaderStyle,
                RowStyle = fallbackRowStyle,
                RowStyleSelector = rowStyleSelector,
                RowHeaderStyle = rowHeaderStyle,
                RowHeaderTemplate = new DataTemplate(),
                RowHeaderTemplateSelector = rowHeaderTemplateSelector,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                ItemsSource = new[] { item },
                Width = 300,
                Height = 160
            };
            grid.Columns.Add(firstColumn);
            grid.Columns.Add(secondColumn);

            var loadedRows = new List<DataGridRow>();
            grid.LoadingRow += (_, e) => loadedRows.Add(e.Row);
            var host = Arrange(grid, 340, 200);

            var row = Assert.Single(loadedRows);
            Assert.Same(rowStyle, row.Style);
            Assert.Same(item, rowStyleSelector.SelectedItem);
            Assert.Same(row, rowStyleSelector.SelectedContainer);
            Assert.Equal(44, row.MinHeight);

            Assert.Equal(2, row.Cells.Count);
            Assert.Equal(70, row.Cells[0].Width);
            Assert.Equal(50, row.Cells[1].Width);
            Assert.Same(cellStyle, row.Cells[0].Style);
            Assert.Same(secondCellStyle, row.Cells[1].Style);

            var rowHeader = Assert.IsType<PrimitiveDataGridRowHeader>(row.RowHeader);
            Assert.Equal(34, rowHeader.Width);
            Assert.Equal(44, rowHeader.Height);
            Assert.Same(rowHeaderStyle, rowHeader.Style);
            Assert.Same(rowHeaderTemplate, rowHeader.ContentTemplate);
            Assert.Same(item, rowHeaderTemplateSelector.SelectedItem);
            Assert.Same(rowHeader, rowHeaderTemplateSelector.SelectedContainer);

            var columnHeadersHost = Assert.IsType<StackPanel>(grid.FindName("PART_ColumnHeadersHost"));
            var headers = columnHeadersHost.Children.OfType<DataGridColumnHeader>().ToArray();
            Assert.Equal(2, headers.Length);
            Assert.Equal(70, headers[0].Width);
            Assert.Equal(50, headers[1].Width);
            Assert.Same(columnHeaderStyle, headers[0].Style);
            Assert.Same(secondColumnHeaderStyle, headers[1].Style);

            var scrollViewer = Assert.IsType<ScrollViewer>(grid.FindName("PART_DataScrollViewer"));
            Assert.Equal(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Hidden, scrollViewer.VerticalScrollBarVisibility);

            Assert.Equal(34, grid.RowHeaderActualWidth);
            Assert.Equal(34, grid.CellsPanelHorizontalOffset);
            Assert.Equal(70, grid.NonFrozenColumnsViewportHorizontalOffset);
            Assert.Equal(new Thickness(0), grid.NewItemMargin);
            Assert.Same(grid, host.Children[0]);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void RowValidationRules_BlockRowCommitAndUseTheConfiguredErrorTemplate()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var item = new RowModel { Name = "initial" };
            var column = new DataGridTextColumn
            {
                Binding = new Binding(nameof(RowModel.Name))
            };
            var errorTemplate = new ControlTemplate(typeof(DataGridRow));
            errorTemplate.SetVisualTree(() => new Border());
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                EnableRowVirtualization = false,
                RowValidationErrorTemplate = errorTemplate,
                ItemsSource = new[] { item },
                Width = 260,
                Height = 120
            };
            grid.Columns.Add(column);
            grid.RowValidationRules.Add(new RequiredRowNameRule());

            var loadedRows = new List<DataGridRow>();
            grid.LoadingRow += (_, e) => loadedRows.Add(e.Row);
            _ = Arrange(grid, 300, 160);

            var row = Assert.Single(loadedRows);
            grid.CurrentCell = new DataGridCellInfo(item, column);
            Assert.True(grid.BeginEdit());
            var editor = Assert.IsType<TextBox>(row.Cells[0].EditingElement);
            editor.Text = string.Empty;

            Assert.False(grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true));
            Assert.True(Validation.GetHasError(row));
            Assert.Same(errorTemplate, Validation.GetErrorTemplate(row));
            Assert.Equal("Name is required.", Assert.Single(Validation.GetErrors(row)!).ErrorContent);

            editor.Text = "valid";
            Assert.True(grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true));
            Assert.Equal("valid", item.Name);
            Assert.False(Validation.GetHasError(row));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static StackPanel Arrange(DataGrid grid, double width, double height)
    {
        var host = new StackPanel { Width = width, Height = height };
        host.Children.Add(grid);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        return host;
    }

    private static void AssertProperty<T>(string name, bool canWrite)
    {
        var property = typeof(DataGrid).GetProperty(name)!;
        Assert.Equal(typeof(T), property.PropertyType);
        Assert.Equal(canWrite, property.SetMethod?.IsPublic == true);
    }

    private static void AssertOverride(string wrapperName, string methodName, Type[] parameterTypes)
    {
        Assert.NotNull(typeof(ProbeDataGrid).GetMethod(wrapperName));
        var method = typeof(DataGrid).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            parameterTypes,
            null);
        Assert.NotNull(method);
        Assert.True(method!.IsVirtual);
        Assert.Equal(typeof(ItemsControl), method.GetBaseDefinition().DeclaringType);
    }

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private sealed class ProbeDataGrid : DataGrid
    {
        public DependencyObject GetContainer() => GetContainerForItemOverride();

        public bool IsOwnContainer(object item) => IsItemItsOwnContainerOverride(item);

        public void Prepare(DependencyObject element, object item) =>
            PrepareContainerForItemOverride(element, item);

        public void Clear(DependencyObject element, object item) =>
            ClearContainerForItemOverride(element, item);
    }

    private sealed class RecordingStyleSelector(Style style) : StyleSelector
    {
        public object? SelectedItem { get; private set; }

        public DependencyObject? SelectedContainer { get; private set; }

        public override Style? SelectStyle(object item, DependencyObject container)
        {
            SelectedItem = item;
            SelectedContainer = container;
            return style;
        }
    }

    private sealed class RecordingTemplateSelector(DataTemplate template) : ControlsDataTemplateSelector
    {
        public object? SelectedItem { get; private set; }

        public DependencyObject? SelectedContainer { get; private set; }

        public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
        {
            SelectedItem = item;
            SelectedContainer = container;
            return template;
        }
    }

    private sealed class RequiredRowNameRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, System.Globalization.CultureInfo cultureInfo) =>
            value is RowModel { Name.Length: > 0 }
                ? ValidationResult.ValidResult
                : new ValidationResult(false, "Name is required.");
    }

    private sealed class RowModel
    {
        public string Name { get; set; } = string.Empty;
    }
}
