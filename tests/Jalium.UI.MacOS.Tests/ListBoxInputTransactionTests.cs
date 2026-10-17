using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Controls.Primitives;
using System.ComponentModel;

namespace Jalium.UI.Tests;

// Regression contracts for QA-355, QA-356 and QA-357.
// These are managed routed-input contracts, not physical keyboard or screenshot acceptance.
[Collection("macOS Window globals")]
public sealed class ListBoxInputTransactionTests
{
    [Theory]
    [InlineData(false, SelectionMode.Single, ModifierKeys.None, 1)]
    [InlineData(true, SelectionMode.Single, ModifierKeys.None, 1)]
    [InlineData(false, SelectionMode.Multiple, ModifierKeys.None, 2)]
    [InlineData(true, SelectionMode.Multiple, ModifierKeys.None, 2)]
    [InlineData(false, SelectionMode.Extended, ModifierKeys.Control, 2)]
    [InlineData(true, SelectionMode.Extended, ModifierKeys.Control, 2)]
    [InlineData(false, SelectionMode.Extended, ModifierKeys.Windows, 2)]
    [InlineData(true, SelectionMode.Extended, ModifierKeys.Windows, 2)]
    public void PointerSelectionKeepsCollectionScalarsAndContainersConsistent(
        bool explicitItems, SelectionMode mode, ModifierKeys modifier, int count)
    {
        using var host = new Host(explicitItems, mode);
        host.Click(0);
        host.Click(1, modifier);
        Assert.Equal(count, host.List.SelectedItems.Count);
        Assert.Equal("second", host.List.SelectedItem);
        Assert.Equal(1, host.List.SelectedIndex);
        Assert.Equal("second", host.List.SelectedValue);
        Assert.True(host.Item(1).IsSelected);
        Assert.Equal(count == 2, host.Item(0).IsSelected);
    }

    [Theory]
    [InlineData(false, SelectionMode.Multiple, ModifierKeys.None)]
    [InlineData(true, SelectionMode.Multiple, ModifierKeys.None)]
    [InlineData(false, SelectionMode.Extended, ModifierKeys.Control)]
    [InlineData(true, SelectionMode.Extended, ModifierKeys.Control)]
    [InlineData(false, SelectionMode.Extended, ModifierKeys.Windows)]
    [InlineData(true, SelectionMode.Extended, ModifierKeys.Windows)]
    public void TogglingOneItemPreservesTheOtherAndDeselectingLastClearsScalars(
        bool explicitItems, SelectionMode mode, ModifierKeys modifier)
    {
        using var host = new Host(explicitItems, mode);
        host.Click(0);
        host.Click(1, modifier);
        host.Click(0, modifier);
        Assert.Equal(new object[] { "second" }, host.List.SelectedItems.Cast<object>());
        Assert.Equal("second", host.List.SelectedItem);
        Assert.Equal(1, host.List.SelectedIndex);
        Assert.False(host.Item(0).IsSelected);
        host.Click(1, modifier);
        Assert.Empty(host.List.SelectedItems);
        Assert.Null(host.List.SelectedItem);
        Assert.Null(host.List.SelectedValue);
        Assert.Equal(-1, host.List.SelectedIndex);
    }

    [Theory]
    [InlineData(false, ModifierKeys.Control, false)]
    [InlineData(true, ModifierKeys.Control, false)]
    [InlineData(false, ModifierKeys.Windows, false)]
    [InlineData(true, ModifierKeys.Windows, false)]
    [InlineData(false, ModifierKeys.Control, true)]
    [InlineData(true, ModifierKeys.Control, true)]
    [InlineData(false, ModifierKeys.Windows, true)]
    [InlineData(true, ModifierKeys.Windows, true)]
    public void PointerRangePreservesItsAnchorAndHonorsAdditiveSelection(
        bool explicitItems, ModifierKeys primary, bool additive)
    {
        using var host = new Host(explicitItems, SelectionMode.Extended);
        host.Click(0);
        host.Click(2, primary);
        host.Click(3, ModifierKeys.Shift | (additive ? primary : ModifierKeys.None));
        var expected = additive ? new object[] { "first", "third", "fourth" } : ["third", "fourth"];
        Assert.Equal(expected, host.List.SelectedItems.Cast<object>());
        Assert.Equal("fourth", host.List.SelectedItem);
        Assert.Equal(3, host.List.SelectedIndex);
    }

    [Theory]
    [InlineData(SelectionMode.Single)]
    [InlineData(SelectionMode.Extended)]
    public void OnePointerChangeEmitsOneAccurateSelectionNotification(SelectionMode mode)
    {
        using var host = new Host(false, mode);
        var events = new List<SelectionChangedEventArgs>();
        host.List.SelectionChanged += (_, e) => events.Add(e);
        host.Click(0);
        Assert.Single(events);
        Assert.Equal(new object[] { "first" }, events[0].AddedItems.Cast<object>());
        Assert.Empty(events[0].RemovedItems);
        host.Click(1, ModifierKeys.Control);
        Assert.Equal(2, events.Count);
        Assert.Equal(new object[] { "second" }, events[1].AddedItems.Cast<object>());
        Assert.Equal(mode == SelectionMode.Single ? new object[] { "first" } : [], events[1].RemovedItems.Cast<object>());
    }

    [Theory]
    [InlineData(false, ModifierKeys.Control)]
    [InlineData(true, ModifierKeys.Control)]
    [InlineData(false, ModifierKeys.Windows)]
    [InlineData(true, ModifierKeys.Windows)]
    public void FocusOnlyArrowPreservesSelectionAndSpaceTargetsTheNewFocus(bool explicitItems, ModifierKeys primary)
    {
        using var host = new Host(explicitItems, SelectionMode.Extended);
        Assert.True(host.List.Seed(["first", "third"]));
        host.Item(0).Focus();
        host.Key(Key.Down, primary);
        Assert.Equal(new object[] { "first", "third" }, host.List.SelectedItems.Cast<object>());
        Assert.Equal("first", host.List.SelectedItem);
        Assert.Equal(0, host.List.SelectedIndex);
        Assert.Same(host.Item(1), Keyboard.FocusedElement);
        host.Key(Key.Space, primary);
        Assert.Equal(new object[] { "first", "third", "second" }, host.List.SelectedItems.Cast<object>());
        Assert.Equal("second", host.List.SelectedItem);
        Assert.Equal(1, host.List.SelectedIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleModeArrowMovesFocusWithoutSelectingTheTarget(bool explicitItems)
    {
        using var host = new Host(explicitItems, SelectionMode.Multiple);
        Assert.True(host.List.Seed(["first", "third"]));
        host.Item(0).Focus();
        host.Key(Key.Down, ModifierKeys.None);
        Assert.Equal(new object[] { "first", "third" }, host.List.SelectedItems.Cast<object>());
        Assert.Same(host.Item(1), Keyboard.FocusedElement);
        host.Key(Key.Space, ModifierKeys.None);
        Assert.Equal(new object[] { "first", "third", "second" }, host.List.SelectedItems.Cast<object>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointerSelectionPreservesTheExistingTwoWayBinding(bool explicitItems)
    {
        using var host = new Host(explicitItems, SelectionMode.Single);
        var source = new SelectionSource();
        host.List.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SelectionSource.SelectedItem)) { Source = source });
        var expression = host.List.GetBindingExpression(Selector.SelectedItemProperty);
        host.Click(0);
        host.Click(1);
        Assert.Same(expression, host.List.GetBindingExpression(Selector.SelectedItemProperty));
        Assert.Equal("second", source.SelectedItem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RangeNavigationRetainsAnchorAndReportsOnlyTheActualDelta(bool explicitItems)
    {
        using var host = new Host(explicitItems, SelectionMode.Extended);
        host.Click(1);
        var changes = new List<SelectionChangedEventArgs>();
        host.List.SelectionChanged += (_, e) => changes.Add(e);
        host.Key(Key.End, ModifierKeys.Shift);
        Assert.Equal(new object[] { "second", "third", "fourth" }, host.List.SelectedItems.Cast<object>());
        Assert.Single(changes);
        Assert.Empty(changes[0].RemovedItems);
        Assert.Equal(new object[] { "third", "fourth" }, changes[0].AddedItems.Cast<object>());
        host.Key(Key.Up, ModifierKeys.Shift);
        Assert.Equal(new object[] { "second", "third" }, host.List.SelectedItems.Cast<object>());
        Assert.Equal("third", host.List.SelectedItem);
        Assert.Equal(2, changes.Count);
        Assert.Equal(new object[] { "fourth" }, changes[1].RemovedItems.Cast<object>());
        Assert.Empty(changes[1].AddedItems);
        host.Key(Key.Home, ModifierKeys.Shift);
        Assert.Equal(new object[] { "first", "second" }, host.List.SelectedItems.Cast<object>());
        Assert.Equal("first", host.List.SelectedItem);
    }

    [Theory]
    [InlineData(false, SelectionMode.Extended)]
    [InlineData(true, SelectionMode.Extended)]
    [InlineData(false, SelectionMode.Multiple)]
    [InlineData(true, SelectionMode.Multiple)]
    public void SelectAllAndUnselectAllAreAtomicAndIdempotent(bool explicitItems, SelectionMode mode)
    {
        using var host = new Host(explicitItems, mode);
        host.Click(1);
        var changes = new List<SelectionChangedEventArgs>();
        host.List.SelectionChanged += (_, e) => changes.Add(e);
        host.Key(Key.A, ModifierKeys.Control);
        Assert.Equal(4, host.List.SelectedItems.Count);
        Assert.Single(changes);
        Assert.Equal(3, changes[0].AddedItems.Count);
        Assert.Empty(changes[0].RemovedItems);
        host.List.SelectAll();
        Assert.Single(changes);
        host.List.UnselectAll();
        Assert.Equal(2, changes.Count);
        Assert.Equal(4, changes[1].RemovedItems.Count);
        Assert.Empty(changes[1].AddedItems);
        Assert.Empty(host.List.SelectedItems);
        Assert.Null(host.List.SelectedItem);
        Assert.Equal(-1, host.List.SelectedIndex);
        for (int i = 0; i < 4; i++) Assert.False(host.Item(i).IsSelected);
        host.List.UnselectAll();
        Assert.Equal(2, changes.Count);
    }

    [Theory]
    [InlineData(false, SelectionMode.Extended)]
    [InlineData(true, SelectionMode.Extended)]
    [InlineData(false, SelectionMode.Multiple)]
    [InlineData(true, SelectionMode.Multiple)]
    public void DragSelectionKeepsLogicalAndRealizedStateConsistent(bool explicitItems, SelectionMode mode)
    {
        using var host = new Host(explicitItems, mode);
        host.Click(1);
        host.List.HandleDragSelect(host.Item(3));
        var expected = mode == SelectionMode.Extended ? new object[] { "second", "third", "fourth" } : ["second", "fourth"];
        Assert.Equal(expected, host.List.SelectedItems.Cast<object>());
        Assert.Equal("fourth", host.List.SelectedItem);
        Assert.Equal(3, host.List.SelectedIndex);
        Assert.False(host.Item(0).IsSelected);
        Assert.True(host.Item(1).IsSelected);
        Assert.Equal(mode == SelectionMode.Extended, host.Item(2).IsSelected);
        Assert.True(host.Item(3).IsSelected);
    }

    [Theory]
    [InlineData(SelectionMode.Multiple)]
    [InlineData(SelectionMode.Extended)]
    public void ProgrammaticContainerSelectionCanBeToggledOffByInput(SelectionMode mode)
    {
        using var host = new Host(true, mode);
        host.List.SelectedIndex = 0;
        Assert.Same(host.Item(0), host.List.SelectedItem);
        var before = host.List.SelectedItems.Cast<object>().ToArray();
        var changes = new List<SelectionChangedEventArgs>();
        host.List.SelectionChanged += (_, e) => changes.Add(e);
        host.Click(0, ModifierKeys.Control);
        Assert.Empty(host.List.SelectedItems);
        Assert.Null(host.List.SelectedItem);
        Assert.Single(changes);
        Assert.Equal(before, changes[0].RemovedItems.Cast<object>());
        Assert.Empty(changes[0].AddedItems);
    }

    [Theory]
    [InlineData(SelectionMode.Multiple)]
    [InlineData(SelectionMode.Extended)]
    public void PointerAdditionAfterProgrammaticSelectionReportsOnlyTheNewLogicalItem(SelectionMode mode)
    {
        using var host = new Host(true, mode);
        host.List.SelectedIndex = 0;
        var changes = new List<SelectionChangedEventArgs>();
        host.List.SelectionChanged += (_, e) => changes.Add(e);
        host.Click(1, ModifierKeys.Control);
        Assert.Equal(new object[] { "first", "second" }, host.List.SelectedItems.Cast<object>());
        Assert.Single(changes);
        Assert.Empty(changes[0].RemovedItems);
        Assert.Equal(new object[] { "second" }, changes[0].AddedItems.Cast<object>());
    }

    private sealed class SelectionSource : INotifyPropertyChanged
    {
        private object? _selectedItem;
        public object? SelectedItem
        {
            get => _selectedItem;
            set { if (Equals(_selectedItem, value)) return; _selectedItem = value; PropertyChanged?.Invoke(this, new(nameof(SelectedItem))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class InputList : ListBox
    {
        public bool Seed(object[] values) => SetSelectedItems(values);
    }

    private sealed class Host : IDisposable
    {
        public InputList List { get; }
        private readonly DisplayedTestWindow _window;
        public Host(bool explicitItems, SelectionMode mode)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            List = new InputList { SelectionMode = mode };
            foreach (var value in new[] { "first", "second", "third", "fourth" })
                List.Items.Add(explicitItems ? new ListBoxItem { Content = value } : value);
            _window = new DisplayedTestWindow { Content = List };
            _window.Measure(new Size(640, 300)); _window.Arrange(new Rect(0, 0, 640, 300));
        }
        public ListBoxItem Item(int index) => Assert.IsType<ListBoxItem>(List.ItemContainerGenerator.ContainerFromIndex(index));
        public void Click(int index, ModifierKeys modifiers = ModifierKeys.None) => Item(index).RaiseEvent(
            new MouseButtonEventArgs(UIElement.MouseDownEvent, new Point(10, 10), MouseButton.Left, MouseButtonState.Pressed, 1,
                MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
                MouseButtonState.Released, MouseButtonState.Released, modifiers, 0));
        public void Key(Key key, ModifierKeys modifiers)
        {
            var input = new KeyEventArgs(UIElement.KeyDownEvent, key, modifiers, true, false, 0);
            List.RaiseEvent(input);
            Assert.True(input.Handled);
        }
        public void Dispose() { Jalium.UI.Input.Mouse.Capture(null); Keyboard.ClearFocus(); _window.Close(); }
    }
}
