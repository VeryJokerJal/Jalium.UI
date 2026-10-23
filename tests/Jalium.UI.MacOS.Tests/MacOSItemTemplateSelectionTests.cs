using System.Collections.ObjectModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSItemTemplateSelectionTests
{
    private sealed class Selector(Func<object?, DataTemplate?> choose) : DataTemplateSelector
    {
        internal readonly List<object?> Calls = [];
        public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
        { Calls.Add(item); return choose(item); }
    }
    private sealed class PrepareList : ListBox
    { internal void Prepare(FrameworkElement element, object item) => base.PrepareContainerForItem(element, item); }
    private sealed class PrepareTree : TreeView
    { internal void Prepare(FrameworkElement element, object item) => base.PrepareContainerForItem(element, item); }
    private sealed class Node
    { public ObservableCollection<object> Children { get; } = []; public bool IsSelected { get; set; } }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void GeneratedContainersUseTheSelectedTemplateExactlyOnce(bool outline)
    {
        var selected = new DataTemplate(); var selector = new Selector(_ => selected); var item = new object();
        if (outline)
        {
            var tree = new PrepareTree { ItemTemplateSelector = selector }; var row = new TreeViewItem();
            tree.Prepare(row, item); Assert.Same(selected, row.HeaderTemplate); Assert.Same(item, row.Header);
        }
        else
        {
            var list = new PrepareList { ItemTemplateSelector = selector }; var row = new ListBoxItem();
            list.Prepare(row, item); Assert.Same(selected, row.ContentTemplate); Assert.Same(item, row.Content);
        }
        Assert.Same(item, Assert.Single(selector.Calls));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AnExplicitItemTemplateTakesPrecedenceOverItsSelector(bool outline)
    {
        var template = new DataTemplate(); var selector = new Selector(_ => throw new InvalidOperationException());
        if (outline)
        {
            var row = new TreeViewItem(); new PrepareTree { ItemTemplate = template, ItemTemplateSelector = selector }.Prepare(row, new object());
            Assert.Same(template, row.HeaderTemplate);
        }
        else
        {
            var row = new ListBoxItem(); new PrepareList { ItemTemplate = template, ItemTemplateSelector = selector }.Prepare(row, new object());
            Assert.Same(template, row.ContentTemplate);
        }
        Assert.Empty(selector.Calls);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void OwnContainersKeepTheirContentTemplateAndDoNotRunTheSelector(bool outline)
    {
        var template = new DataTemplate(); var selector = new Selector(_ => throw new InvalidOperationException());
        if (outline)
        {
            var row = new TreeViewItem { Header = "own", HeaderTemplate = template };
            new PrepareTree { ItemTemplateSelector = selector }.Prepare(row, row);
            Assert.Equal("own", row.Header); Assert.Same(template, row.HeaderTemplate);
        }
        else
        {
            var row = new ListBoxItem { Content = "own", ContentTemplate = template };
            new PrepareList { ItemTemplateSelector = selector }.Prepare(row, row);
            Assert.Equal("own", row.Content); Assert.Same(template, row.ContentTemplate);
        }
        Assert.Empty(selector.Calls);
    }

    [Fact]
    public void NestedGeneratedItemsUseTheirParentsSelector()
    {
        var selected = new DataTemplate(); var selector = new Selector(_ => selected);
        var parent = new TreeViewItem { ItemTemplateSelector = selector }; var row = new TreeViewItem(); var item = new object();
        parent.PrepareContainerForItemInternal(row, item);
        Assert.Same(selected, row.HeaderTemplate); Assert.Same(item, Assert.Single(selector.Calls));
    }

    [Fact]
    public void HierarchicalSelectorsApplyToDeferredAndIncrementallyChangedChildren()
    {
        var leaf = new DataTemplate(); var selector = new Selector(_ => leaf); var root = new Node(); var first = new Node();
        root.Children.Add(first); var row = new TreeViewItem();
        TreeViewItem.ApplyHierarchicalDataTemplate(row, root, Hierarchy(selector));
        Assert.Empty(selector.Calls); Assert.Empty(row.Items); row.IsExpanded = true;
        Assert.Same(leaf, Assert.IsType<TreeViewItem>(Assert.Single(row.Items)).HeaderTemplate);
        Assert.Same(first, Assert.Single(selector.Calls));
        var second = new Node(); root.Children.Add(second);
        Assert.Equal(2, selector.Calls.Count); Assert.Same(second, selector.Calls[1]);
        Assert.All(row.Items.Cast<object>(), item => Assert.Same(leaf, Assert.IsType<TreeViewItem>(item).HeaderTemplate));
        var replacement = new Node(); root.Children[0] = replacement;
        Assert.Same(leaf, ((TreeViewItem)row.Items[0]).HeaderTemplate); Assert.Same(replacement, ((TreeViewItem)row.Items[0]).Header);
        root.Children.Clear(); Assert.Empty(row.Items); root.Children.Add(first);
        Assert.Same(leaf, Assert.IsType<TreeViewItem>(Assert.Single(row.Items)).HeaderTemplate);
    }

    [Fact]
    public void HierarchicalChildTemplatesOverrideTheSelectorAndOwnRowsStayIntact()
    {
        var leaf = new DataTemplate(); var selector = new Selector(_ => throw new InvalidOperationException());
        var ownTemplate = new DataTemplate(); var own = new TreeViewItem { Header = "own", HeaderTemplate = ownTemplate };
        var root = new Node(); root.Children.Add(new Node()); root.Children.Add(own);
        var hierarchy = Hierarchy(selector); hierarchy.ItemTemplate = leaf;
        var row = new TreeViewItem(); TreeViewItem.ApplyHierarchicalDataTemplate(row, root, hierarchy); row.IsExpanded = true;
        Assert.Same(leaf, ((TreeViewItem)row.Items[0]).HeaderTemplate); Assert.Same(own, row.Items[1]); Assert.Same(ownTemplate, own.HeaderTemplate);
        Assert.Empty(selector.Calls);
    }

    [Fact]
    public void ASelectorCanChooseADifferentHierarchyForANestedBranch()
    {
        var leaf = new DataTemplate(); var branchTemplate = Hierarchy(null); branchTemplate.ItemTemplate = leaf;
        var branch = new Node(); branch.Children.Add(new Node()); var root = new Node(); root.Children.Add(branch);
        var selector = new Selector(_ => branchTemplate); var row = new TreeViewItem();
        TreeViewItem.ApplyHierarchicalDataTemplate(row, root, Hierarchy(selector)); row.IsExpanded = true;
        var child = Assert.IsType<TreeViewItem>(Assert.Single(row.Items)); Assert.Same(branchTemplate, child.HeaderTemplate);
        child.IsExpanded = true; Assert.Same(leaf, Assert.IsType<TreeViewItem>(Assert.Single(child.Items)).HeaderTemplate);
        Assert.Single(selector.Calls);
    }

    [Fact]
    public void DeferredSelectedDataNodesHaveTheirTreeOwnerBeforeStateBridging()
    {
        var child = new Node { IsSelected = true }; var root = new Node(); root.Children.Add(child);
        var tree = new PrepareTree { ItemTemplate = Hierarchy(null) }; var row = new TreeViewItem();
        tree.Prepare(row, root); row.IsExpanded = true;
        Assert.Same(child, tree.SelectedItem);
        Assert.True(Assert.IsType<TreeViewItem>(Assert.Single(row.Items)).IsSelected);
    }

    [Fact]
    public void MovingATemplateGeneratedRowToAnotherOwnerRestoresOwnContainerIdentity()
    {
        var data = new Node(); var root = new Node(); root.Children.Add(data); var parent = new TreeViewItem();
        TreeViewItem.ApplyHierarchicalDataTemplate(parent, root, Hierarchy(null)); parent.IsExpanded = true;
        var child = Assert.IsType<TreeViewItem>(Assert.Single(parent.Items)); Assert.Same(data, child.ItemData);
        parent.Items.Remove(child); var other = new TreeViewItem();
        other.PrepareContainerForItemInternal(child, child);
        Assert.Same(child, child.ItemData); Assert.Same(other, child.ParentItem);
    }

    [Fact]
    public void RetirementCallbacksCannotEraseANewerDataOwner()
    {
        var data = new Node { IsSelected = true }; var root = new Node(); root.Children.Add(data);
        var tree = new PrepareTree { ItemTemplate = Hierarchy(null) }; var row = new TreeViewItem();
        tree.Prepare(row, root); row.IsExpanded = true;
        var child = Assert.IsType<TreeViewItem>(Assert.Single(row.Items));
        var nextOwner = new PrepareTree(); var nextData = new Node();
        tree.SelectedItemChanged += (_, args) => { if (args.NewValue == null) nextOwner.Prepare(child, nextData); };
        child.ResetContainerState();
        Assert.Same(nextData, child.ItemData); Assert.Same(nextOwner, child.ParentTreeView); Assert.True(data.IsSelected);
    }

    private static HierarchicalDataTemplate Hierarchy(DataTemplateSelector? selector) => new()
    { ItemsSource = new Binding(nameof(Node.Children)), ItemTemplateSelector = selector };
}
