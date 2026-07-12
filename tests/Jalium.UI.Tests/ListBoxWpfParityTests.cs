using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class ListBoxWpfParityTests
{
    [Fact]
    public void TierOneApiSurfaceMatchesWpfContracts()
    {
        FieldInfo selectedItemsField = typeof(ListBox).GetField(nameof(ListBox.SelectedItemsProperty))!;
        PropertyInfo anchorProperty = typeof(ListBox).GetProperty(
            "AnchorItem",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        PropertyInfo handlesScrollingProperty = typeof(ListBox).GetProperty(
            "HandlesScrolling",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo setSelectedItems = typeof(ListBox).GetMethod(
            "SetSelectedItems",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(IEnumerable)],
            modifiers: null)!;

        Assert.True(selectedItemsField.IsPublic);
        Assert.True(selectedItemsField.IsStatic);
        Assert.True(selectedItemsField.IsInitOnly);
        Assert.Equal(typeof(IList), ListBox.SelectedItemsProperty.PropertyType);
        Assert.Equal(typeof(Selector), ListBox.SelectedItemsProperty.OwnerType);
        Assert.True(ListBox.SelectedItemsProperty.ReadOnly);
        Assert.True(anchorProperty.GetMethod!.IsFamily);
        Assert.True(anchorProperty.SetMethod!.IsFamily);
        Assert.True(handlesScrollingProperty.GetMethod!.IsFamilyOrAssembly);
        Assert.True(handlesScrollingProperty.GetMethod.IsVirtual);
        Assert.True(setSelectedItems.IsFamily);
        Assert.Equal(typeof(bool), setSelectedItems.ReturnType);
        Assert.NotNull(typeof(ListBox).GetMethod(nameof(ListBox.ScrollIntoView), [typeof(object)]));
    }

    [Fact]
    public void SelectedItemsPropertyStoresTheLiveReadOnlyCollection()
    {
        var listBox = new TestListBox();

        Assert.Same(listBox.SelectedItems, listBox.GetValue(ListBox.SelectedItemsProperty));
        Assert.Same(ListBox.SelectedItemsProperty, Selector.SelectedItemsImplProperty);
    }

    [Fact]
    public void SetSelectedItemsAtomicallyUpdatesCollectionScalarPropertiesAndEvent()
    {
        var listBox = CreateSelectionListBox(SelectionMode.Extended);
        var changes = new List<SelectionChangedEventArgs>();
        listBox.SelectionChanged += (_, args) => changes.Add(args);

        Assert.True(listBox.SetSelection(new[] { "c", "a" }));

        Assert.Equal(new object[] { "c", "a" }, listBox.SelectedItems.Cast<object>());
        Assert.Equal("c", listBox.SelectedItem);
        Assert.Equal(2, listBox.SelectedIndex);
        Assert.Equal("c", listBox.SelectedValue);
        Assert.Single(changes);
        Assert.Empty(changes[0].RemovedItems);
        Assert.Equal(new object[] { "c", "a" }, changes[0].AddedItems.Cast<object>().ToArray());

        Assert.False(listBox.SetSelection(new[] { "b", "missing" }));

        Assert.Equal(new object[] { "c", "a" }, listBox.SelectedItems.Cast<object>());
        Assert.Equal("c", listBox.SelectedItem);
        Assert.Equal(2, listBox.SelectedIndex);
        Assert.Single(changes);

        Assert.True(listBox.SetSelection(new[] { "b" }));

        Assert.Equal(new object[] { "b" }, listBox.SelectedItems.Cast<object>());
        Assert.Equal("b", listBox.SelectedItem);
        Assert.Equal(1, listBox.SelectedIndex);
        Assert.Equal(2, changes.Count);
        Assert.Equal(new object[] { "c", "a" }, changes[1].RemovedItems.Cast<object>().ToArray());
        Assert.Equal(new object[] { "b" }, changes[1].AddedItems.Cast<object>().ToArray());

        Assert.True(listBox.SetSelection(null));
        Assert.Empty(listBox.SelectedItems);
        Assert.Null(listBox.SelectedItem);
        Assert.Equal(-1, listBox.SelectedIndex);
        Assert.Null(listBox.SelectedValue);
    }

    [Fact]
    public void SetSelectedItemsRejectsTooManySingleModeItemsWithoutPartialMutation()
    {
        var listBox = CreateSelectionListBox(SelectionMode.Single);
        Assert.True(listBox.SetSelection(new[] { "a" }));

        Assert.False(listBox.SetSelection(new[] { "b", "c" }));

        Assert.Equal(new object[] { "a" }, listBox.SelectedItems.Cast<object>());
        Assert.Equal("a", listBox.SelectedItem);
        Assert.Equal(0, listBox.SelectedIndex);
    }

    [Fact]
    public void SetSelectedItemsRejectsReentrantSelectionTransactions()
    {
        var listBox = CreateSelectionListBox(SelectionMode.Multiple);
        bool? nestedResult = null;
        listBox.SelectionChanged += (_, _) => nestedResult = listBox.SetSelection(new[] { "b" });

        Assert.True(listBox.SetSelection(new[] { "a" }));

        Assert.Equal(false, nestedResult);
        Assert.Equal(new object[] { "a" }, listBox.SelectedItems.Cast<object>());
    }

    [Fact]
    public void AnchorItemTracksLogicalItemAcrossIndexChangesAndRejectsForeignItems()
    {
        var listBox = CreateSelectionListBox(SelectionMode.Extended);

        listBox.Anchor = "b";
        Assert.Equal("b", listBox.Anchor);

        listBox.Items.RemoveAt(0);
        Assert.Equal("b", listBox.Anchor);

        Assert.Throws<InvalidOperationException>(() => listBox.Anchor = "foreign");
        Assert.Equal("b", listBox.Anchor);

        listBox.Items.Remove("b");
        Assert.Null(listBox.Anchor);

        listBox.Anchor = null;
        Assert.Null(listBox.Anchor);
    }

    [Fact]
    public void HandlesScrollingLetsTemplatedListBoxOwnNavigationKeys()
    {
        var listBox = new TestListBox();
        var plainControl = new TestControl();
        Assert.True(listBox.HandlesScrollingValue);
        Assert.False(plainControl.HandlesScrollingValue);

        var templatedViewer = new TestScrollViewer();
        templatedViewer.SetTemplatedParent(listBox);
        var delegatedKey = KeyDown(Key.Down);

        templatedViewer.ProcessKey(delegatedKey);

        Assert.False(delegatedKey.Handled);

        var standaloneViewer = new TestScrollViewer();
        standaloneViewer.SetTemplatedParent(plainControl);
        var handledKey = KeyDown(Key.Down);

        standaloneViewer.ProcessKey(handledKey);

        Assert.True(handledKey.Handled);
    }

    [Fact]
    public void ScrollIntoViewMovesVirtualizingHostAndRealizesRequestedItem()
    {
        var listBox = new TestListBox { Width = 320, Height = 120 };
        for (int i = 0; i < 500; i++)
        {
            listBox.Items.Add($"Item {i}");
        }

        listBox.Measure(new Size(320, 120));
        listBox.Arrange(new Rect(0, 0, 320, 120));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(400));

        listBox.ScrollIntoView("Item 400");

        Assert.True(host.VerticalOffset > 0);
        host.Measure(new Size(320, 120));
        host.Arrange(new Rect(0, 0, 320, 120));
        Assert.NotNull(listBox.ItemContainerGenerator.ContainerFromIndex(400));

        double offset = host.VerticalOffset;
        listBox.ScrollIntoView("not-an-item");
        Assert.Equal(offset, host.VerticalOffset);
    }

    private static TestListBox CreateSelectionListBox(SelectionMode mode)
    {
        var listBox = new TestListBox { SelectionMode = mode };
        listBox.Items.Add("a");
        listBox.Items.Add("b");
        listBox.Items.Add("c");
        return listBox;
    }

    private static KeyEventArgs KeyDown(Key key)
        => new(UIElement.KeyDownEvent, key, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0);

    private sealed class TestListBox : ListBox
    {
        public object? Anchor
        {
            get => AnchorItem;
            set => AnchorItem = value;
        }

        public bool SetSelection(IEnumerable? selectedItems) => SetSelectedItems(selectedItems!);

        public bool HandlesScrollingValue => HandlesScrolling;

        public Panel? Host => ItemsHost;
    }

    private sealed class TestControl : Control
    {
        public bool HandlesScrollingValue => HandlesScrolling;
    }

    private sealed class TestScrollViewer : ScrollViewer
    {
        public void ProcessKey(KeyEventArgs args) => base.OnKeyDown(args);
    }
}
