using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using ControlsDataTemplateSelector = Jalium.UI.Controls.DataTemplateSelector;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DataGridCurrentCellRowLifecycleParityTests
{
    [Fact]
    public void PublicSurface_MatchesTheWpfCurrentCellSelectionAndRowLifecycleContracts()
    {
        AssertProperty<DataGrid, object>(nameof(DataGrid.CurrentItem), canWrite: true);
        AssertProperty<DataGrid, DataGridColumn>(nameof(DataGrid.CurrentColumn), canWrite: true);
        AssertProperty<DataGrid, DataGridCellInfo>(nameof(DataGrid.CurrentCell), canWrite: true);
        AssertProperty<DataGrid, IList<DataGridCellInfo>>(nameof(DataGrid.SelectedCells), canWrite: false);
        AssertProperty<DataGrid, ControlsDataTemplateSelector>(nameof(DataGrid.RowDetailsTemplateSelector), canWrite: true);
        AssertProperty<DataGrid, double>(nameof(DataGrid.RowHeaderActualWidth), canWrite: false);

        foreach (var propertyName in new[]
                 {
                     nameof(DataGrid.CurrentItem), nameof(DataGrid.CurrentColumn), nameof(DataGrid.CurrentCell),
                     nameof(DataGrid.AreRowDetailsFrozen), nameof(DataGrid.CanUserResizeRows),
                     nameof(DataGrid.RowDetailsTemplateSelector), nameof(DataGrid.RowHeaderActualWidth)
                 })
        {
            Assert.Equal(typeof(DependencyProperty),
                typeof(DataGrid).GetField(propertyName + "Property")!.FieldType);
        }

        AssertEvent<EventHandler<EventArgs>>(nameof(DataGrid.CurrentCellChanged));
        AssertEvent<EventHandler<DataGridRowEventArgs>>(nameof(DataGrid.LoadingRow));
        AssertEvent<EventHandler<DataGridRowEventArgs>>(nameof(DataGrid.UnloadingRow));
        AssertEvent<EventHandler<DataGridRowDetailsEventArgs>>(nameof(DataGrid.LoadingRowDetails));
        AssertEvent<EventHandler<DataGridRowDetailsEventArgs>>(nameof(DataGrid.UnloadingRowDetails));
        AssertEvent<EventHandler<DataGridRowDetailsEventArgs>>(nameof(DataGrid.RowDetailsVisibilityChanged));
        AssertEvent<EventHandler<DataGridRowEditEndingEventArgs>>(nameof(DataGrid.RowEditEnding));

        AssertHook("OnCurrentCellChanged", typeof(EventArgs), protectedInternal: false);
        AssertHook("OnLoadingRow", typeof(DataGridRowEventArgs), protectedInternal: false);
        AssertHook("OnUnloadingRow", typeof(DataGridRowEventArgs), protectedInternal: false);
        AssertHook("OnLoadingRowDetails", typeof(DataGridRowDetailsEventArgs), protectedInternal: false);
        AssertHook("OnUnloadingRowDetails", typeof(DataGridRowDetailsEventArgs), protectedInternal: false);
        AssertHook("OnRowDetailsVisibilityChanged", typeof(DataGridRowDetailsEventArgs), protectedInternal: true);
        AssertHook("OnRowEditEnding", typeof(DataGridRowEditEndingEventArgs), protectedInternal: false);

        Assert.False(typeof(DataGridRowEventArgs).IsSealed);
        Assert.False(typeof(DataGridRowDetailsEventArgs).IsSealed);
        Assert.False(typeof(DataGridRowEditEndingEventArgs).IsSealed);
        Assert.NotNull(typeof(DataGridRowDetailsEventArgs).GetConstructor(
            [typeof(DataGridRow), typeof(FrameworkElement)]));
    }

    [Fact]
    public void CurrentCellAndSelectedCells_StaySynchronizedAndRaisePreciseChanges()
    {
        var first = new RowModel { Name = "first" };
        var second = new RowModel { Name = "second" };
        var firstColumn = new DataGridTextColumn();
        var secondColumn = new DataGridTextColumn();
        var grid = new DataGrid { AutoGenerateColumns = false, SelectionUnit = DataGridSelectionUnit.Cell };
        grid.Columns.Add(firstColumn);
        grid.Columns.Add(secondColumn);
        grid.ItemsSource = new[] { first, second };

        var currentChanges = 0;
        var selectedChanges = new List<SelectedCellsChangedEventArgs>();
        grid.CurrentCellChanged += (_, _) => currentChanges++;
        grid.SelectedCellsChanged += (_, e) => selectedChanges.Add(e);

        grid.CurrentCell = new DataGridCellInfo(first, secondColumn);
        Assert.Same(first, grid.CurrentItem);
        Assert.Same(secondColumn, grid.CurrentColumn);
        Assert.Equal(1, currentChanges);

        grid.CurrentItem = second;
        Assert.Equal(new DataGridCellInfo(second, secondColumn), grid.CurrentCell);
        Assert.Equal(2, currentChanges);

        grid.SelectedCells.Add(new DataGridCellInfo(first, firstColumn));
        Assert.Single(grid.SelectedCells);
        Assert.Single(selectedChanges);
        Assert.Single(selectedChanges[0].AddedCells);

        grid.SelectAllCells();
        Assert.Equal(4, grid.SelectedCells.Count);
        Assert.Equal(2, selectedChanges.Count);
        Assert.Equal(3, selectedChanges[1].AddedCells.Count);

        grid.UnselectAllCells();
        Assert.Empty(grid.SelectedCells);
        Assert.Equal(3, selectedChanges.Count);
        Assert.Equal(4, selectedChanges[2].RemovedCells.Count);
    }

    [Fact]
    public void RealizedRows_DriveDetailsLifecycleAndExplicitRowEditEnding()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var item = new RowModel { Name = "row" };
            var detailsTemplate = new DataTemplate();
            detailsTemplate.SetVisualTree(() => new Border());
            var column = new DataGridTextColumn
            {
                Binding = new Binding(nameof(RowModel.Name))
            };
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                RowDetailsTemplate = detailsTemplate,
                RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Visible,
                ItemsSource = new[] { item },
                Width = 300,
                Height = 140
            };
            grid.Columns.Add(column);

            var loadingRows = new List<DataGridRow>();
            var unloadingRows = new List<DataGridRow>();
            var loadingDetails = new List<DataGridRowDetailsEventArgs>();
            var unloadingDetails = new List<DataGridRowDetailsEventArgs>();
            var visibilityChanges = new List<Visibility>();
            grid.LoadingRow += (_, e) => loadingRows.Add(e.Row);
            grid.UnloadingRow += (_, e) => unloadingRows.Add(e.Row);
            grid.LoadingRowDetails += (_, e) => loadingDetails.Add(e);
            grid.UnloadingRowDetails += (_, e) => unloadingDetails.Add(e);
            grid.RowDetailsVisibilityChanged += (_, e) => visibilityChanges.Add(e.Row.DetailsVisibility);

            var host = new StackPanel { Width = 320, Height = 180 };
            host.Children.Add(grid);
            host.Measure(new Size(320, 180));
            host.Arrange(new Rect(0, 0, 320, 180));
            host.Measure(new Size(320, 180));
            host.Arrange(new Rect(0, 0, 320, 180));

            var row = Assert.Single(loadingRows);
            Assert.Single(loadingDetails);
            Assert.Same(row, loadingDetails[0].Row);
            Assert.NotNull(loadingDetails[0].DetailsElement);

            grid.SetDetailsVisibilityForItem(item, Visibility.Collapsed);
            Assert.Equal(Visibility.Collapsed, row.DetailsVisibility);
            grid.ClearDetailsVisibilityForItem(item);
            Assert.Equal(Visibility.Visible, row.DetailsVisibility);
            Assert.Equal(new[] { Visibility.Collapsed, Visibility.Visible }, visibilityChanges);

            grid.CurrentCell = new DataGridCellInfo(item, column);
            Assert.True(grid.BeginEdit());
            DataGridRowEditEndingEventArgs? observedEnding = null;
            EventHandler<DataGridRowEditEndingEventArgs> cancelCommit = (_, e) =>
            {
                observedEnding = e;
                e.Cancel = true;
            };
            grid.RowEditEnding += cancelCommit;
            Assert.False(grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true));
            Assert.Equal(DataGridEditAction.Commit, observedEnding!.EditAction);
            grid.RowEditEnding -= cancelCommit;
            Assert.True(grid.CancelEdit(DataGridEditingUnit.Row));

            grid.ItemsSource = Array.Empty<RowModel>();
            Assert.Single(unloadingRows);
            Assert.Single(unloadingDetails);
            Assert.Same(row, unloadingRows[0]);
            Assert.Same(row, unloadingDetails[0].Row);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void AssertProperty<TDeclaring, TProperty>(string name, bool canWrite)
    {
        var property = typeof(TDeclaring).GetProperty(name)!;
        Assert.Equal(typeof(TProperty), property.PropertyType);
        Assert.Equal(canWrite, property.SetMethod?.IsPublic == true);
    }

    private static void AssertEvent<THandler>(string name) where THandler : Delegate =>
        Assert.Equal(typeof(THandler), typeof(DataGrid).GetEvent(name)!.EventHandlerType);

    private static void AssertHook(string name, Type argumentType, bool protectedInternal)
    {
        var method = typeof(DataGrid).GetMethod(
            name, BindingFlags.Instance | BindingFlags.NonPublic, null, [argumentType], null)!;
        Assert.True(method.IsVirtual);
        Assert.Equal(protectedInternal, method.IsFamilyOrAssembly);
        Assert.Equal(!protectedInternal, method.IsFamily);
    }

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private sealed class RowModel
    {
        public string Name { get; set; } = string.Empty;
    }
}
