using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPointerSelectionTests
{
    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Windows)]
    public void ListBoxPointerTogglesPreserveOtherItems(ModifierKeys modifiers)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var list = new ListBox { SelectionMode = SelectionMode.Extended };
        var first = new ListBoxItem { Content = "first" };
        var second = new ListBoxItem { Content = "second" };
        list.Items.Add(first); list.Items.Add(second);
        using var host = new Host(list);
        first.RaiseEvent(Mouse(ModifierKeys.None));
        second.RaiseEvent(Mouse(modifiers));
        Assert.Equal(2, list.SelectedItems.Count);
        first.RaiseEvent(Mouse(modifiers));
        Assert.Single(list.SelectedItems);
        Assert.Equal("second", list.SelectedItems[0]);
    }

    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Windows)]
    public void DataGridRowPointerTogglesPreserveOtherItems(ModifierKeys modifiers)
    {
        if (!OperatingSystem.IsMacOS()) return;
        object first = new(), second = new();
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { first, second },
            SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow };
        using var host = new Host(grid);
        ClickRow(grid, new DataGridRow { RowIndex = 0 }, ModifierKeys.None);
        ClickRow(grid, new DataGridRow { RowIndex = 1 }, modifiers);
        Assert.Equal(2, grid.SelectedItems.Count);
        ClickRow(grid, new DataGridRow { RowIndex = 0 }, modifiers);
        Assert.Single(grid.SelectedItems);
        Assert.Same(second, grid.SelectedItems[0]);
    }

    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Windows)]
    public void TreeGridRowPointerTogglesPreserveOtherItems(ModifierKeys modifiers)
    {
        if (!OperatingSystem.IsMacOS()) return;
        object first = new(), second = new();
        var grid = new TreeDataGrid { ItemsSource = new[] { first, second }, SelectionMode = DataGridSelectionMode.Extended };
        using var host = new Host(grid);
        ClickRow(grid, new TreeDataGridRow { RowIndex = 0 }, ModifierKeys.None);
        ClickRow(grid, new TreeDataGridRow { RowIndex = 1 }, modifiers);
        Assert.Equal(2, grid.SelectedItems.Count);
        ClickRow(grid, new TreeDataGridRow { RowIndex = 0 }, modifiers);
        Assert.Single(grid.SelectedItems);
        Assert.Same(second, grid.SelectedItems[0]);
    }

    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Windows)]
    public void DataGridCellPointerTogglesPreserveOtherCells(ModifierKeys modifiers)
    {
        if (!OperatingSystem.IsMacOS()) return;
        object first = new(), second = new();
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { first, second },
            SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.Cell };
        var column = new DataGridTextColumn { Header = "value" }; grid.Columns.Add(column);
        using var host = new Host(grid);
        // The pointer row handler delegates cell targeting to this private core.
        var select = typeof(DataGrid).GetMethod("SelectCell", BindingFlags.Instance | BindingFlags.NonPublic)!;
        select.Invoke(grid, [0, 0, ModifierKeys.None]);
        select.Invoke(grid, [1, 0, modifiers]);
        Assert.Equal(2, grid.SelectedCells.Count);
        select.Invoke(grid, [0, 0, modifiers]);
        Assert.Single(grid.SelectedCells);
        Assert.Same(second, grid.SelectedCells[0].Item);
        Assert.Same(column, grid.SelectedCells[0].Column);
    }

    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Windows)]
    public void TreeSelectorPointerTogglesPreserveOtherItems(ModifierKeys modifiers)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var selector = new TreeSelector { SelectionMode = SelectionMode.Extended };
        var first = new TreeSelectorItem { Header = "first" };
        var second = new TreeSelectorItem { Header = "second" };
        selector.Items.Add(first); selector.Items.Add(second);
        using var host = new Host(selector);
        ClickHeader(first, ModifierKeys.None);
        ClickHeader(second, modifiers);
        Assert.Equal(new object[] { "first", "second" }, selector.SelectedItems.Cast<object>());
        ClickHeader(first, modifiers);
        Assert.Equal(new object[] { "second" }, selector.SelectedItems.Cast<object>());
    }

    [Fact]
    public void TreeSelectorShiftPointerSelectsTheVisibleRange()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var selector = new TreeSelector { SelectionMode = SelectionMode.Extended };
        var items = Enumerable.Range(0, 3).Select(i => new TreeSelectorItem { Header = i }).ToArray();
        foreach (var item in items) selector.Items.Add(item);
        using var host = new Host(selector);
        ClickHeader(items[0], ModifierKeys.None);
        ClickHeader(items[2], ModifierKeys.Shift);
        Assert.Equal(new object[] { 0, 1, 2 }, selector.SelectedItems.Cast<object>());
    }

    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Windows)]
    public void TreeSelectorPrimaryShiftPointerAddsTheRange(ModifierKeys primary)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var selector = new TreeSelector { SelectionMode = SelectionMode.Extended };
        var items = Enumerable.Range(0, 4).Select(i => new TreeSelectorItem { Header = i }).ToArray();
        foreach (var item in items) selector.Items.Add(item);
        using var host = new Host(selector);
        ClickHeader(items[0], ModifierKeys.None);
        ClickHeader(items[2], primary);
        ClickHeader(items[3], primary | ModifierKeys.Shift);
        Assert.Equal(new object[] { 0, 2, 3 }, selector.SelectedItems.Cast<object>());
    }

    [Fact]
    public void TreeSelectorUnmodifiedPointerReplacesTheSelection()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var selector = new TreeSelector { SelectionMode = SelectionMode.Extended };
        var first = new TreeSelectorItem { Header = "first" };
        var second = new TreeSelectorItem { Header = "second" };
        selector.Items.Add(first); selector.Items.Add(second);
        using var host = new Host(selector);
        selector.SelectedItems.Add("first"); selector.SelectedItems.Add("second");
        ClickHeader(first, ModifierKeys.None);
        Assert.Equal(new object[] { "first" }, selector.SelectedItems.Cast<object>());
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void TreeSelectorPrimarySelectionKeyUsesTheLogicalEventModifiers(Key key)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var selector = new TreeSelector { SelectionMode = SelectionMode.Extended };
        var first = new TreeSelectorItem { Header = "first" };
        var second = new TreeSelectorItem { Header = "second" };
        selector.Items.Add(first); selector.Items.Add(second);
        using var host = new Host(selector);
        ClickHeader(first, ModifierKeys.None);
        var input = new KeyEventArgs(UIElement.KeyDownEvent, key, ModifierKeys.Control,
            isDown: true, isRepeat: false, timestamp: 0)
            { PhysicalModifiers = ModifierKeys.Windows };
        second.RaiseEvent(input);
        Assert.True(input.Handled);
        Assert.Equal(new object[] { "first", "second" }, selector.SelectedItems.Cast<object>());
    }

    private static void ClickHeader(TreeSelectorItem item, ModifierKeys modifiers) =>
        typeof(TreeSelectorItem).GetMethod("OnHeaderMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(item, [item, Mouse(modifiers)]);

    private static void ClickRow(Control grid, Control row, ModifierKeys modifiers) =>
        grid.GetType().GetMethod("OnRowMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [row, Mouse(modifiers)]);

    private static MouseButtonEventArgs Mouse(ModifierKeys modifiers) =>
        new(UIElement.MouseDownEvent, new Point(10, 10), MouseButton.Left, MouseButtonState.Pressed, 1,
            MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, modifiers, 0);

    private sealed class Host : IDisposable
    {
        private readonly DisplayedTestWindow _window;
        internal Host(Control control)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            _window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Content = control };
            _window.Measure(new Size(640, 300)); _window.Arrange(new Rect(0, 0, 640, 300));
        }
        public void Dispose() { Jalium.UI.Input.Mouse.Capture(null); Keyboard.ClearFocus(); _window.Close(); }
    }
}
