using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class SelectorTreeViewWpfParityTests
{
    [Fact]
    public void SelectorAttachedSelectionSurfaceMatchesWpfContracts()
    {
        Assert.True(Selector.IsSelectionActiveProperty.ReadOnly);
        Assert.True(Assert.IsType<FrameworkPropertyMetadata>(
            Selector.IsSelectionActiveProperty.GetMetadata(typeof(Selector))).Inherits);
        Assert.False(Selector.IsSelectedProperty.ReadOnly);

        var element = new Border();
        Selector.SetIsSelected(element, true);
        Assert.True(Selector.GetIsSelected(element));
        Assert.False(Selector.GetIsSelectionActive(element));

        var selectedCount = 0;
        RoutedEventHandler selected = (_, _) => selectedCount++;
        Selector.AddSelectedHandler(element, selected);
        element.RaiseEvent(new RoutedEventArgs(Selector.SelectedEvent, element));
        Assert.Equal(1, selectedCount);

        Selector.RemoveSelectedHandler(element, selected);
        element.RaiseEvent(new RoutedEventArgs(Selector.SelectedEvent, element));
        Assert.Equal(1, selectedCount);

        Assert.Throws<ArgumentNullException>(() => Selector.GetIsSelected(null!));
        Assert.Throws<ArgumentNullException>(() => Selector.AddSelectedHandler(element, null!));
    }

    [Fact]
    public void TreeViewSelectionPropertiesAreReadOnlyAndSelectedValuePathIsFunctional()
    {
        Assert.True(TreeView.SelectedItemProperty.ReadOnly);
        Assert.True(TreeView.SelectedValueProperty.ReadOnly);
        Assert.False(TreeView.SelectedValuePathProperty.ReadOnly);
        Assert.Null(typeof(TreeView).GetProperty(nameof(TreeView.SelectedItem))!.SetMethod);
        Assert.Null(typeof(TreeView).GetProperty(nameof(TreeView.SelectedValue))!.SetMethod);

        var tree = new TestTreeView { SelectedValuePath = "Address.City" };
        var item = new Person("Ada", new Address("London"));
        var container = new TreeViewItem();
        tree.Prepare(container, item);

        RoutedPropertyChangedEventArgs<object?>? changed = null;
        tree.SelectedItemChanged += (_, e) => changed = e;
        container.IsSelected = true;

        Assert.Same(item, tree.SelectedItem);
        Assert.Equal("London", tree.SelectedValue);
        Assert.NotNull(changed);
        Assert.Null(changed!.OldValue);
        Assert.Same(item, changed.NewValue);
        Assert.Equal(1, tree.SelectedItemChangedHookCount);

        tree.SelectedValuePath = nameof(Person.Name);
        Assert.Equal("Ada", tree.SelectedValue);

        container.IsSelected = false;
        Assert.Null(tree.SelectedItem);
        Assert.Null(tree.SelectedValue);
        Assert.Equal(2, tree.SelectedItemChangedHookCount);
    }

    [Fact]
    public void TreeViewItemSelectionExpansionAndVirtualHooksAreFunctional()
    {
        Assert.Same(Selector.IsSelectionActiveProperty, TreeViewItem.IsSelectionActiveProperty);
        Assert.Null(typeof(TreeViewItem).GetProperty(nameof(TreeViewItem.IsSelectionActive))!.SetMethod);

        var root = new TestTreeViewItem();
        var child = new TestTreeViewItem();
        var grandchild = new TestTreeViewItem();
        child.Items.Add(grandchild);
        root.Items.Add(child);

        var selectedCount = 0;
        var unselectedCount = 0;
        root.Selected += (_, _) => selectedCount++;
        root.Unselected += (_, _) => unselectedCount++;

        root.IsSelected = true;
        root.IsSelected = false;
        Assert.Equal(1, selectedCount);
        Assert.Equal(1, unselectedCount);
        Assert.Equal(1, root.SelectedHookCount);
        Assert.Equal(1, root.UnselectedHookCount);

        root.ExpandSubtree();
        Assert.True(root.IsExpanded);
        Assert.True(child.IsExpanded);
        Assert.True(grandchild.IsExpanded);
        Assert.Equal(1, root.ExpandedHookCount);

        root.IsExpanded = false;
        Assert.Equal(1, root.CollapsedHookCount);

        var tree = new TestTreeView();
        Assert.False(tree.CallExpandSubtree(null!));
        Assert.True(tree.CallExpandSubtree(root));
    }

    private sealed class TestSelector : Selector
    {
    }

    private sealed class TestTreeView : TreeView
    {
        public int SelectedItemChangedHookCount { get; private set; }

        public void Prepare(TreeViewItem container, object item) =>
            PrepareContainerForItem(container, item);

        public bool CallExpandSubtree(TreeViewItem container) => ExpandSubtree(container);

        protected override void OnSelectedItemChanged(RoutedPropertyChangedEventArgs<object?> e)
        {
            SelectedItemChangedHookCount++;
            base.OnSelectedItemChanged(e);
        }
    }

    private sealed class TestTreeViewItem : TreeViewItem
    {
        public int ExpandedHookCount { get; private set; }
        public int CollapsedHookCount { get; private set; }
        public int SelectedHookCount { get; private set; }
        public int UnselectedHookCount { get; private set; }

        protected override void OnExpanded(RoutedEventArgs e)
        {
            ExpandedHookCount++;
            base.OnExpanded(e);
        }

        protected override void OnCollapsed(RoutedEventArgs e)
        {
            CollapsedHookCount++;
            base.OnCollapsed(e);
        }

        protected override void OnSelected(RoutedEventArgs e)
        {
            SelectedHookCount++;
            base.OnSelected(e);
        }

        protected override void OnUnselected(RoutedEventArgs e)
        {
            UnselectedHookCount++;
            base.OnUnselected(e);
        }
    }

    private sealed record Person(string Name, Address Address);
    private sealed record Address(string City);
}
