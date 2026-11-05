using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TreeSelectorHierarchyTests
{
    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    [InlineData(Key.Down)]
    [InlineData(Key.F4)]
    public void TriggerKeyboardOpensAndEscapeClosesWithoutRequiringTemplateParts(Key key)
    {
        var selector = Create(new Node("root"));
        var minimal = new ControlTemplate(typeof(TreeSelector)); minimal.SetVisualTree(() => new Border()); selector.Template = minimal; selector.ApplyTemplate();
        var args = KeyArgs(selector, key); selector.RaiseEvent(args);
        Assert.True(args.Handled); Assert.True(selector.IsDropDownOpen);
        selector.RaiseEvent(KeyArgs(selector, Key.Escape)); Assert.False(selector.IsDropDownOpen);
        selector.IsEnabled = false; selector.RaiseEvent(KeyArgs(selector, key)); Assert.False(selector.IsDropDownOpen);
    }

    private static KeyEventArgs KeyArgs(UIElement target, Key key) => new(Keyboard.PrimaryDevice, new Source { RootVisual = target }, Environment.TickCount, key) { RoutedEvent = UIElement.KeyDownEvent, Source = target };
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    [Fact]
    public void DataHierarchyRemainsDataAndUsesGeneratedContainersOnDemand()
    {
        var child = new Node("child"); var root = new Node("root", child);
        var selector = Create(root);
        Assert.Same(root, selector.Items[0]);
        Assert.Null(selector.ItemContainerGenerator.ContainerFromIndex(0));
        var container = Container(selector, 0);
        Assert.Same(root, container.Header);
        Assert.Same(child, container.Items[0]);
        Assert.Null(container.ItemContainerGenerator.ContainerFromIndex(0));
        var leaf = Container(container, 0);
        Assert.Same(child, leaf.Header);
        Assert.Same(selector, leaf.ParentSelector);
        Assert.Same(container, leaf.ParentItem);
        Assert.Equal(1, leaf.Level);
    }

    [Fact]
    public void CascadeIncludesCollapsedGeneratedDescendantsAndRecomputesMixedParent()
    {
        var first = new Node("first"); var second = new Node("second");
        var root = new Node("root", new Node("branch", first, second));
        var selector = Create(root); selector.ShowCheckBoxes = true;
        selector.CheckCascadeMode = TreeSelectorCheckCascadeMode.Cascade;
        var container = Container(selector, 0);
        Assert.False(container.IsExpanded);
        container.IsChecked = true;
        Assert.Equal(4, selector.CheckedItems.Count);
        Assert.Contains(first, selector.CheckedItems.Cast<object>());
        var branch = Container(container, 0); var leaf = Container(branch, 0);
        Assert.True(leaf.IsChecked); Assert.True(leaf.IsCheckBoxVisible);
        leaf.IsChecked = false;
        Assert.Null(branch.IsChecked); Assert.Null(container.IsChecked);
        Assert.Equal(new object[] { second }, selector.CheckedItems.Cast<object>());
    }

    [Fact]
    public void SelectAllAndExternalSelectionUseDataAndSynchronizeLateContainers()
    {
        var leaf = new Node("leaf"); var root = new Node("root", leaf);
        var selector = Create(root); selector.SelectionMode = SelectionMode.Multiple;
        selector.SelectAll();
        Assert.Equal(new object[] { root, leaf }, selector.SelectedItems.Cast<object>());
        Assert.True(Container(Container(selector, 0), 0).IsSelected);
        selector.UnselectAll();
        Assert.Empty(selector.SelectedItems);
        Assert.False(Container(selector, 0).IsSelected);
        selector.SelectedItems.Add(leaf);
        Assert.True(Container(Container(selector, 0), 0).IsSelected);
    }

    [Fact]
    public void SearchFindsUnrealizedDescendantsAndRestoresEveryBranch()
    {
        var selector = Create(new Node("East", new Node("Shanghai", new Node("Pudong"))), new Node("West"));
        selector.IsSearchEnabled = true; selector.SearchText = "Pudong";
        var east = Container(selector, 0); var city = Container(east, 0);
        Assert.True(east.IsExpanded); Assert.True(city.IsExpanded);
        Assert.Equal(Visibility.Visible, Container(city, 0).Visibility);
        Assert.Equal(Visibility.Collapsed, Container(selector, 1).Visibility);
        selector.SearchText = "";
        Assert.Equal(Visibility.Visible, Container(selector, 1).Visibility);
    }

    [Fact]
    public void ObservableChildSourceAndPropertyReplacementUpdateHierarchy()
    {
        var root = new Node("root", new Node("old")); var selector = Create(root);
        var container = Container(selector, 0); var oldLeaf = Container(container, 0);
        root.Children = new ObservableCollection<Node> { new("new") };
        Assert.Same(root.Children, container.ItemsSource);
        Assert.Same(root.Children[0], Container(container, 0).Header);
        root.Children.Clear();
        Assert.Empty(container.Items);
        Assert.Null(oldLeaf.ParentSelector);
        Assert.Null(oldLeaf.DataContext);
    }

    [Fact]
    public void ReplacingSourceClearsStateAndRetiresNestedBindings()
    {
        var root = new Node("old", new Node("leaf")); var selector = Create(root);
        var retired = Container(selector, 0); var retiredChild = Container(retired, 0);
        selector.SelectedItem = root; retired.IsChecked = true;
        var replacement = new Node("replacement"); selector.ItemsSource = new[] { replacement };
        Assert.Empty(selector.SelectedItems); Assert.Empty(selector.CheckedItems);
        Assert.Null(selector.SelectedItem); Assert.Null(retiredChild.ParentSelector);
        Assert.Null(BindingOperations.GetBindingBase(retired, ItemsControl.ItemsSourceProperty));
        root.Children.Add(new Node("late"));
        Assert.Empty(retired.Items);
        Assert.Same(replacement, Container(selector, 0).Header);
        Assert.Empty(Container(selector, 0).Items);
    }

    [Fact]
    public void RootResetAttachesAndDetachesDeclaredHierarchyBeforePopupRealization()
    {
        var child = new TreeSelectorItem { Header = "child" };
        var root = new TreeSelectorItem { Header = "root" }; root.Items.Add(child);
        var roots = new ObservableCollection<TreeSelectorItem> { root };
        var selector = new TreeSelector { ItemsSource = roots, ShowCheckBoxes = true };
        Assert.Same(selector, child.ParentSelector); Assert.True(child.IsCheckBoxVisible);
        roots.Clear();
        Assert.Null(root.ParentSelector); Assert.Null(child.ParentSelector);
        Assert.False(child.IsCheckBoxVisible);
        roots.Add(root);
        Assert.Same(selector, child.ParentSelector);
    }

    [Fact]
    public void ChildTemplateSelectorAndContainerStyleAreHonored()
    {
        var leafTemplate = new DataTemplate();
        var style = new Style(typeof(TreeSelectorItem)); style.Setters.Add(new Setter(TreeSelectorItem.IsExpandedProperty, true));
        var selector = Create(new Node("root", new Node("leaf")));
        ((HierarchicalDataTemplate)selector.ItemTemplate!).ItemTemplateSelector = new PickTemplate(leafTemplate);
        ((HierarchicalDataTemplate)selector.ItemTemplate!).ItemContainerStyle = style;
        var root = Container(selector, 0); var child = Container(root, 0);
        Assert.Same(leafTemplate, child.HeaderTemplate); Assert.Same(style, child.Style);
        Assert.True(child.IsExpanded);
        selector.ItemTemplate = leafTemplate;
        Assert.Same(root, selector.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Empty(root.Items); Assert.Same(leafTemplate, root.HeaderTemplate);
        Assert.Null(child.ParentSelector);
    }

    [Fact]
    public void RemovingCheckedAndSelectedChildrenPrunesPublicStateAndReleasesHeaders()
    {
        var leaf = new Node("leaf"); var root = new Node("root", leaf); var selector = Create(root);
        var container = Container(selector, 0); var retired = Container(container, 0);
        selector.SelectedItem = leaf; retired.IsChecked = true;
        root.Children.Clear();
        Assert.Empty(selector.CheckedItems); Assert.Empty(selector.SelectedItems); Assert.Null(selector.SelectedItem);
        Assert.Null(retired.Header); Assert.Null(retired.HeaderTemplate); Assert.Null(retired.ParentSelector);
    }

    [Fact]
    public void ObservableRootReplacementDoesNotReuseExpandedCheckedOrSelectedState()
    {
        var roots = new ObservableCollection<Node> { new("old", new Node("leaf")) };
        var selector = Create(); selector.ItemsSource = roots;
        var root = Container(selector, 0); root.IsExpanded = true; root.IsChecked = true; selector.SelectedItem = roots[0];
        for (var cycle = 0; cycle < 20; cycle++)
        {
            var replacement = new Node("new" + cycle); roots[0] = replacement;
            var current = Container(selector, 0);
            Assert.Same(replacement, current.Header); Assert.Empty(current.Items);
            Assert.False(current.IsExpanded); Assert.False(current.IsSelected); Assert.False(current.IsChecked);
            Assert.Empty(selector.SelectedItems); Assert.Empty(selector.CheckedItems);
        }
    }

    [Fact]
    public void UnrelatedObservableChangesKeepSelectedAndCheckedItems()
    {
        var leaf = new Node("leaf"); var root = new Node("root", leaf); var selector = Create(root);
        var current = Container(Container(selector, 0), 0); selector.SelectedItem = leaf; current.IsChecked = true;
        root.Children.Add(new Node("other"));
        Assert.Same(leaf, selector.SelectedItem); Assert.Contains(leaf, selector.CheckedItems.Cast<object>());
        root.Children.Move(0, 1);
        Assert.Same(leaf, selector.SelectedItem); Assert.Contains(leaf, selector.CheckedItems.Cast<object>());
        Assert.True(Container(Container(selector, 0), 1).IsSelected);
    }

    [Fact]
    public void DataChildrenOfDeclaredItemsInheritTheSelectorHeaderTemplate()
    {
        var template = new DataTemplate(); var selector = new TreeSelector { ItemTemplate = template };
        var parent = new TreeSelectorItem { Header = "parent" }; parent.Items.Add(new Node("data child")); selector.Items.Add(parent);
        Assert.Same(template, Container(parent, 0).HeaderTemplate);
    }

    [Fact]
    public void ChildTemplateSelectorReturningNullKeepsTheDefaultHeaderPresentation()
    {
        var selector = Create(new Node("root", new Node("child", new Node("grandchild"))));
        ((HierarchicalDataTemplate)selector.ItemTemplate!).ItemTemplateSelector = new NullTemplate();
        var child = Container(Container(selector, 0), 0);
        Assert.Null(child.HeaderTemplate); Assert.Empty(child.Items);
    }

    private sealed class NullTemplate : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object item, DependencyObject container) => null;
    }

    [Fact]
    public void ObservableChildrenKeepCascadeParentsConsistent()
    {
        var root = new Node("root", new Node("first")); var selector = Create(root);
        selector.CheckCascadeMode = TreeSelectorCheckCascadeMode.Cascade;
        var container = Container(selector, 0); container.IsChecked = true;
        var added = new Node("second"); root.Children.Add(added);
        Assert.Null(container.IsChecked); Assert.Single(selector.CheckedItems.Cast<object>());
        root.Children.Remove(added);
        Assert.True(container.IsChecked); Assert.Equal(2, selector.CheckedItems.Count);
        root.Children.Clear();
        Assert.False(container.IsChecked); Assert.Empty(selector.CheckedItems);
    }

    private static TreeSelector Create(params Node[] roots)
    {
        PropertyAccessorRegistry.Register(typeof(Node), nameof(Node.Children), value => ((Node)value).Children);
        return new TreeSelector { ItemTemplate = new HierarchicalDataTemplate { ItemsSource = new Binding(nameof(Node.Children)) }, ItemsSource = roots };
    }

    private static TreeSelectorItem Container(ItemsControl owner, int index)
    {
        if (owner.ItemContainerGenerator.ContainerFromIndex(index) is TreeSelectorItem current) return current;
        var generated = Assert.IsType<TreeSelectorItem>(owner.ItemContainerGenerator.GetOrCreateContainerForIndex(index, out var created));
        if (created) owner.ItemContainerGenerator.PrepareItemContainer(generated);
        return generated;
    }

    private sealed class PickTemplate(DataTemplate template) : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object item, DependencyObject container) => template;
    }

    private sealed class Node(string label, params Node[] children) : INotifyPropertyChanged
    {
        private ObservableCollection<Node> _children = new(children);
        public ObservableCollection<Node> Children
        {
            get => _children;
            set { _children = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Children))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => label;
    }
}
