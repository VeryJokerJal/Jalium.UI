using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Input;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSCustomElementAccessibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TreeHeaderCheckboxKeepsFocusToggleAndParentIdentity(bool virtualizing, bool collapsedBranch)
    {
        using var fixture = new Fixture();
        var treeTemplate = new ControlTemplate(typeof(TreeView));
        treeTemplate.SetVisualTree(static () => new ItemsPresenter());
        var rowTemplate = HeaderTemplate();
        var check = new CheckBox { Height = 26 };
        AutomationProperties.SetName(check, "勾选测试：中文用例🙂");
        var row = new TreeViewItem { Header = new Border { Child = check }, Template = rowTemplate };
        AutomationProperties.SetName(row, "中文用例🙂，通过");
        if (collapsedBranch)
            row.Items.Add(new TreeViewItem { Header = "折叠内的用例", Template = rowTemplate });
        var tree = new TreeView { Template = treeTemplate };
        VirtualizingPanel.SetIsVirtualizing(tree, virtualizing);
        tree.Items.Add(row);
        fixture.SetContent(tree);

        ulong treeId = Assert.Single(fixture.Children(1));
        ulong rowId = Assert.Single(fixture.Children(treeId));
        ulong checkId = Assert.Single(fixture.Children(rowId));
        Assert.Equal((int)AutomationControlType.CheckBox, fixture.Request(checkId, MacOSAXOperation.Info).Role);
        Assert.Equal("勾选测试：中文用例🙂", fixture.Text(checkId, MacOSAXString.Name));
        Assert.Equal(rowId, fixture.Request(checkId, MacOSAXOperation.Info).ParentId);
        Assert.True(fixture.Request(checkId, MacOSAXOperation.Info).Flags.HasFlag(MacOSAXFlags.Toggle));
        Assert.True(fixture.Action(checkId, MacOSAXAction.SetFocus));
        Assert.Equal(checkId, fixture.Request(1, MacOSAXOperation.Focus).ResultId);
        Assert.True(fixture.Action(checkId, MacOSAXAction.Press));
        Assert.True(check.IsChecked);
        Assert.Equal(1, fixture.Request(checkId, MacOSAXOperation.Info).Value);
        Assert.False(row.IsSelected);
        Assert.True(fixture.Action(checkId, MacOSAXAction.Press));
        Assert.False(check.IsChecked);
        Assert.Equal(0, fixture.Request(checkId, MacOSAXOperation.Info).Value);
        Assert.Equal(checkId, Assert.Single(fixture.Children(rowId)));
    }

    [Fact]
    public void LogicalTreeItemPeerExposesHeaderActionsAndRetainsNestedItemPeers()
    {
        var button = new Button { Content = "行内动作" };
        var nested = new TreeViewItem { Header = "子项" };
        var row = new TreeViewItem { Header = new Border { Child = button }, Items = { nested } };
        var tree = new TreeView { Items = { row } };
        var logicalRow = Assert.IsType<TreeViewDataItemAutomationPeer>(Assert.Single(tree.GetAutomationPeer()!.GetChildren()));
        var children = logicalRow.GetChildren();
        Assert.Contains(button.GetAutomationPeer(), children);
        Assert.Single(children.OfType<TreeViewDataItemAutomationPeer>());
        Assert.Equal(2, children.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollapsingTreeBranchRetainsItsHeaderAndHidesOnlyNestedRows(bool virtualizing)
    {
        using var fixture = new Fixture();
        var treeTemplate = new ControlTemplate(typeof(TreeView));
        treeTemplate.SetVisualTree(static () => new ItemsPresenter());
        var check = new CheckBox { Content = "勾选分组", Height = 26 };
        var child = new TreeViewItem { Header = "子测试", Template = HeaderTemplate() };
        var row = new TreeViewItem { Header = check, Template = HeaderTemplate(), IsExpanded = true, Items = { child } };
        var tree = new TreeView { Template = treeTemplate, Items = { row } };
        VirtualizingPanel.SetIsVirtualizing(tree, virtualizing);
        fixture.SetContent(tree);
        ulong treeId = Assert.Single(fixture.Children(1));
        ulong rowId = Assert.Single(fixture.Children(treeId));
        var expanded = fixture.Children(rowId);
        ulong checkId = Assert.Single(expanded, id => fixture.Request(id, MacOSAXOperation.Info).Role == (int)AutomationControlType.CheckBox);
        Assert.Single(expanded, id => fixture.Request(id, MacOSAXOperation.Info).Role == (int)AutomationControlType.TreeItem);

        Assert.True(fixture.Action(rowId, MacOSAXAction.Collapse));
        fixture.Layout();
        Assert.Equal(checkId, Assert.Single(fixture.Children(rowId)));
        Assert.True(fixture.Action(checkId, MacOSAXAction.Press));
        Assert.True(check.IsChecked);
        Assert.False(child.IsSelected);
        Assert.True(fixture.Action(rowId, MacOSAXAction.Expand));
        fixture.Layout();
        Assert.Contains(checkId, fixture.Children(rowId));
        Assert.Equal(2, fixture.Children(rowId).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingTreeHeaderDoesNotRetainRemovedControlsInAccessibility(bool virtualizing)
    {
        using var fixture = new Fixture();
        var treeTemplate = new ControlTemplate(typeof(TreeView));
        treeTemplate.SetVisualTree(static () => new ItemsPresenter());
        var original = new CheckBox { Content = "原勾选", Height = 26 };
        var row = new TreeViewItem { Header = original, Template = HeaderTemplate() };
        var tree = new TreeView { Template = treeTemplate, Items = { row } };
        VirtualizingPanel.SetIsVirtualizing(tree, virtualizing);
        fixture.SetContent(tree);
        ulong rowId = Assert.Single(fixture.Children(Assert.Single(fixture.Children(1))));
        ulong oldId = Assert.Single(fixture.Children(rowId));
        var replacement = new Button { Content = "替换动作", Height = 26 };
        row.Header = replacement;
        fixture.Layout();
        ulong newId = Assert.Single(fixture.Children(rowId));
        Assert.NotEqual(oldId, newId);
        Assert.Equal("替换动作", fixture.Text(newId, MacOSAXString.Name));
        Assert.False(fixture.TryRequest(oldId, MacOSAXOperation.Info, out _));
        Assert.False(fixture.Action(oldId, MacOSAXAction.Press));
        Assert.False(original.IsChecked);
        Assert.Equal(rowId, fixture.Request(newId, MacOSAXOperation.Info).ParentId);
    }

    private static ControlTemplate HeaderTemplate()
    {
        var template = new ControlTemplate(typeof(TreeViewItem));
        template.SetVisualTree(static () =>
        {
            var presenter = new ContentPresenter();
            presenter.SetTemplateBinding(ContentPresenter.ContentProperty, HeaderedItemsControl.HeaderProperty);
            return new StackPanel { Children =
            {
                new Border { Name = "PART_HeaderBorder", Child = presenter },
                new ItemsPresenter { Name = "PART_ItemsHost" }
            } };
        });
        return template;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TreeRowsExposeRealizedContainerBoundsAcrossVirtualizationModes(bool virtualizing)
    {
        using var fixture = new Fixture();
        // Geometry contracts use local templates, without creating a process-wide AppKit
        // application or leaving theme resources owned by this test's worker thread.
        var treeTemplate = new ControlTemplate(typeof(TreeView));
        treeTemplate.SetVisualTree(static () => new ItemsPresenter());
        var rowTemplate = new ControlTemplate(typeof(TreeViewItem));
        rowTemplate.SetVisualTree(static () =>
        {
            var presenter = new ContentPresenter();
            presenter.SetTemplateBinding(ContentPresenter.ContentProperty, HeaderedItemsControl.HeaderProperty);
            return presenter;
        });
        var tree = new TreeView { Template = treeTemplate };
        VirtualizingPanel.SetIsVirtualizing(tree, virtualizing);
        tree.Items.Add(new TreeViewItem { Header = "Visible row", Template = rowTemplate });
        fixture.SetContent(tree);
        var treeId = Assert.Single(fixture.Children(1));
        var rowId = Assert.Single(fixture.Children(treeId));
        var info = fixture.Request(rowId, MacOSAXOperation.Info);
        Assert.Equal((int)AutomationControlType.TreeItem, info.Role);
        Assert.True(info.Width > 0 && info.Height > 0);
        Assert.True(info.Flags.HasFlag(MacOSAXFlags.Selectable));
    }

    [Fact]
    public void NamedFocusableSourceHasItsOwnFocusAndGroupIdentity()
    {
        using var fixture = new Fixture();
        var source = new Border { Focusable = true, Height = 70, Padding = new Thickness(12),
            Child = new TextBlock { Text = "中文拖放样例🙂" } };
        AutomationProperties.SetName(source, "拖放样例来源");
        fixture.SetContent(source);

        ulong id = Assert.Single(fixture.Children(1));
        var info = fixture.Request(id, MacOSAXOperation.Info);
        Assert.Equal((int)AutomationControlType.Custom, info.Role);
        Assert.Equal("拖放样例来源", fixture.Text(id, MacOSAXString.Name));
        Assert.Equal(1ul, info.ParentId);
        Assert.True(info.Flags.HasFlag(MacOSAXFlags.Focusable));
        Assert.False(info.Flags.HasFlag(MacOSAXFlags.Pressable));
        Assert.True(fixture.Action(id, MacOSAXAction.SetFocus));
        Assert.True(source.IsKeyboardFocused);
        Assert.Equal(id, fixture.Request(1, MacOSAXOperation.Focus).ResultId);
        Assert.True(fixture.Request(id, MacOSAXOperation.Info).Flags.HasFlag(MacOSAXFlags.Focused));
        Assert.Equal(id, fixture.Request(1, MacOSAXOperation.HitTest, x: info.X + 2, y: info.Y + 2).ResultId);
    }

    [Fact]
    public void AnonymousLayoutDoesNotAddAccessibilityStopsOrDecorations()
    {
        using var fixture = new Fixture();
        var button = new Button { Content = "实际动作" };
        fixture.SetContent(new Border { Child = new StackPanel { Children = { button } } });
        ulong id = Assert.Single(fixture.Children(1));
        Assert.Equal((int)AutomationControlType.Button, fixture.Request(id, MacOSAXOperation.Info).Role);
        Assert.Equal("实际动作", fixture.Text(id, MacOSAXString.Name));
        Assert.Empty(fixture.Children(id));
        Assert.Equal(1ul, fixture.Request(id, MacOSAXOperation.Info).ParentId);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("identifier")]
    [InlineData("label")]
    public void AuthorSemanticsCanBeAddedAndRemovedAfterChildrenWereCached(string property)
    {
        using var fixture = new Fixture();
        var button = new Button { Content = "子动作" };
        var group = new Border { Child = button };
        fixture.SetContent(group);
        ulong buttonId = Assert.Single(fixture.Children(1));
        var label = new TextBlock { Text = "来源标签" };

        Change(true);
        ulong groupId = Assert.Single(fixture.Children(1));
        Assert.NotEqual(buttonId, groupId);
        Assert.Equal(buttonId, Assert.Single(fixture.Children(groupId)));
        Assert.Equal(groupId, fixture.Request(buttonId, MacOSAXOperation.Info).ParentId);
        Assert.Equal((int)AutomationControlType.Custom, fixture.Request(groupId, MacOSAXOperation.Info).Role);
        if (property == "name") Assert.Equal("具名分组", fixture.Text(groupId, MacOSAXString.Name));
        if (property == "identifier") Assert.Equal("SampleGroup", fixture.Text(groupId, MacOSAXString.Identifier));
        if (property == "label") Assert.Equal("来源标签", fixture.Text(groupId, MacOSAXString.Name));

        Change(false);
        Assert.Equal(buttonId, Assert.Single(fixture.Children(1)));
        Assert.Equal(1ul, fixture.Request(buttonId, MacOSAXOperation.Info).ParentId);
        Assert.False(fixture.TryRequest(groupId, MacOSAXOperation.Info, out _));
        Assert.False(fixture.Action(groupId, MacOSAXAction.SetFocus));
        Change(true);
        Assert.Equal(groupId, Assert.Single(fixture.Children(1)));

        void Change(bool add)
        {
            if (property == "name") AutomationProperties.SetName(group, add ? "具名分组" : "");
            else if (property == "identifier") AutomationProperties.SetAutomationId(group, add ? "SampleGroup" : "");
            else AutomationProperties.SetLabeledBy(group, add ? label : null);
        }
    }

    [Fact]
    public void ChangingFocusabilityRebuildsSemanticsAndKeepsChildIdentity()
    {
        using var fixture = new Fixture();
        var button = new Button { Content = "后续动作" };
        var group = new Border { Child = button };
        fixture.SetContent(group);
        ulong buttonId = Assert.Single(fixture.Children(1));
        group.Focusable = true;
        ulong groupId = Assert.Single(fixture.Children(1));
        Assert.NotEqual(buttonId, groupId);
        Assert.True(fixture.Action(groupId, MacOSAXAction.SetFocus));
        Assert.Equal(groupId, fixture.Request(1, MacOSAXOperation.Focus).ResultId);
        group.Focusable = false;
        Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.False(group.IsKeyboardFocused);
        Assert.Equal(buttonId, Assert.Single(fixture.Children(1)));
        Assert.False(fixture.Action(groupId, MacOSAXAction.SetFocus));
        Assert.NotEqual(groupId, fixture.Request(1, MacOSAXOperation.Focus).ResultId);
        group.Focusable = true;
        Assert.Equal(groupId, Assert.Single(fixture.Children(1)));
    }

    [Theory]
    [InlineData(Visibility.Hidden)]
    [InlineData(Visibility.Collapsed)]
    public void HiddenGroupAndItsCachedActionsAreUnavailableUntilRedisplay(Visibility visibility)
    {
        using var fixture = new Fixture();
        var group = new Border { Focusable = true, Child = new Button { Content = "内部动作" } };
        AutomationProperties.SetName(group, "可重用分组"); fixture.SetContent(group);
        ulong id = Assert.Single(fixture.Children(1));
        Assert.True(fixture.Action(id, MacOSAXAction.SetFocus));
        group.Visibility = visibility;
        Assert.Empty(fixture.Children(1));
        Assert.False(fixture.TryRequest(id, MacOSAXOperation.Info, out _));
        Assert.False(fixture.Action(id, MacOSAXAction.SetFocus));
        group.Visibility = Visibility.Visible; fixture.Layout();
        Assert.Equal(id, Assert.Single(fixture.Children(1)));
        Assert.True(fixture.Action(id, MacOSAXAction.SetFocus));
    }

    [Fact]
    public void DisabledNamedGroupExposesStateButCannotTakeFocusOrInventActions()
    {
        using var fixture = new Fixture();
        var group = new Border { Focusable = true, IsEnabled = false };
        AutomationProperties.SetName(group, "禁用来源"); fixture.SetContent(group);
        ulong id = Assert.Single(fixture.Children(1));
        var info = fixture.Request(id, MacOSAXOperation.Info);
        Assert.False(info.Flags.HasFlag(MacOSAXFlags.Enabled));
        Assert.False(info.Flags.HasFlag(MacOSAXFlags.Focusable));
        Assert.False(info.Flags.HasFlag(MacOSAXFlags.Pressable));
        Assert.False(info.Flags.HasFlag(MacOSAXFlags.Value));
        Assert.False(fixture.Action(id, MacOSAXAction.SetFocus));
        Assert.False(fixture.Action(id, MacOSAXAction.Press));
        group.IsEnabled = true;
        Assert.True(fixture.Action(id, MacOSAXAction.SetFocus));
    }

    [Fact]
    public void ReparentingRejectsOldWindowReferencesAndFindsTheNewParent()
    {
        using var first = new Fixture(); using var second = new Fixture();
        var group = new Border { Focusable = true };
        AutomationProperties.SetName(group, "移动的来源"); first.SetContent(group);
        ulong oldId = Assert.Single(first.Children(1));
        first.Window.Content = null; second.SetContent(group);
        Assert.False(first.TryRequest(oldId, MacOSAXOperation.Attached, out _));
        Assert.False(first.Action(oldId, MacOSAXAction.SetFocus));
        ulong id = Assert.Single(second.Children(1));
        Assert.Equal(1ul, second.Request(id, MacOSAXOperation.Info).ParentId);
        Assert.True(second.Action(id, MacOSAXAction.SetFocus));
        Assert.Equal(id, second.Request(1, MacOSAXOperation.Focus).ResultId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CssHiddenLayoutPromotesVisibleChildrenButDisplayNoneGatesTheWholeSubtree(bool named)
    {
        using var fixture = new Fixture();
        var child = new Button { Content = "显式可见后代" };
        var group = new Border { Child = child };
        if (named) AutomationProperties.SetName(group, "可隐藏分组");
        fixture.SetContent(group);
        ulong first = Assert.Single(fixture.Children(1));
        ulong childId = named ? Assert.Single(fixture.Children(first)) : first;
        Jalium.UI.Styling.Css.SetStyle(group, "visibility: hidden");
        Jalium.UI.Styling.Css.SetStyle(child, "visibility: visible");
        Assert.False(group.IsVisible); Assert.True(child.IsVisible);
        Assert.Equal(childId, Assert.Single(fixture.Children(1)));
        Assert.Equal(1ul, fixture.Request(childId, MacOSAXOperation.Info).ParentId);
        Jalium.UI.Styling.Css.SetStyle(group, "display: none");
        Assert.Empty(fixture.Children(1));
        Assert.False(fixture.TryRequest(childId, MacOSAXOperation.Info, out _));
        Assert.False(fixture.Action(childId, MacOSAXAction.Press));
    }

    [Fact]
    public void CachedCustomPeerAnnouncesChangesToAuthorSemantics()
    {
        using var fixture = new Fixture();
        var group = new Border { Child = new Button { Content = "动作" } };
        fixture.SetContent(group); fixture.Children(1);
        var previous = AutomationPeer.EventSink;
        var sink = new RecordingSink(); AutomationPeer.EventSink = sink;
        try
        {
            group.Focusable = true; group.Focusable = false;
            AutomationProperties.SetLabeledBy(group, new TextBlock { Text = "标签" });
            AutomationProperties.SetLabeledBy(group, null);
            AutomationProperties.SetName(group, "分组");
            AutomationProperties.SetAutomationId(group, "GroupId");
            Assert.Equal(4, sink.StructureOwners.Count(owner => ReferenceEquals(owner, group)));
            Assert.Contains(sink.Properties, item => ReferenceEquals(item.Owner, group) && item.Property == AutomationProperty.NameProperty);
            Assert.Contains(sink.Properties, item => ReferenceEquals(item.Owner, group) && item.Property == AutomationProperty.AutomationIdProperty);
        }
        finally { AutomationPeer.EventSink = previous; }
    }

    private sealed class RecordingSink : IAutomationEventSink
    {
        internal List<DependencyObject?> StructureOwners { get; } = [];
        internal List<(DependencyObject? Owner, AutomationProperty Property)> Properties { get; } = [];
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId)
        { if (eventId == AutomationEvents.StructureChanged) StructureOwners.Add(peer.Owner); }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
            => Properties.Add((peer.Owner, property));
        public void OnFocusChanged(AutomationPeer peer) { }
    }

    private sealed class Fixture : IDisposable
    {
        internal DisplayedTestWindow Window { get; } = new() { TitleBarStyle = WindowTitleBarStyle.Native, Width = 260, Height = 180 };
        private readonly MacOSAccessibilityTree _tree;
        internal Fixture() => _tree = new(Window);
        internal void SetContent(UIElement content) { Window.Content = content; Layout(); }
        internal void Layout() { Window.Measure(new Size(260, 180)); Window.Arrange(new Rect(0, 0, 260, 180)); }
        internal MacOSAXRequest Request(ulong id, MacOSAXOperation operation, int index = 0, double x = 0, double y = 0)
        {
            var request = new MacOSAXRequest { NodeId = id, Operation = operation, Index = index, X = x, Y = y };
            Assert.True(_tree.Handle(ref request)); return request;
        }
        internal bool TryRequest(ulong id, MacOSAXOperation operation, out MacOSAXRequest request)
        {
            request = new MacOSAXRequest { NodeId = id, Operation = operation }; return _tree.Handle(ref request);
        }
        internal List<ulong> Children(ulong id)
        {
            var request = Request(id, MacOSAXOperation.BeginChildren);
            try
            {
                var ids = new ulong[request.ChildCount];
                fixed (ulong* buffer = ids)
                {
                    request.Operation = MacOSAXOperation.ReadChildren;
                    request.Text = (char*)buffer;
                    request.TextCapacity = ids.Length;
                    Assert.True(_tree.Handle(ref request));
                }
                return ids.ToList();
            }
            finally
            {
                request.Operation = MacOSAXOperation.ReleaseChildren;
                Assert.True(_tree.Handle(ref request));
            }
        }
        internal string Text(ulong id, MacOSAXString property)
        {
            char* buffer = stackalloc char[256];
            var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.String, Index = (int)property, Text = buffer, TextCapacity = 256 };
            Assert.True(_tree.Handle(ref request)); return new string(buffer, 0, request.TextCount);
        }
        internal bool Action(ulong id, MacOSAXAction action)
        {
            var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Action, Index = (int)action }; return _tree.Handle(ref request);
        }
        public void Dispose() { _tree.Dispose(); Keyboard.Focus(null); Window.Close(); }
    }
}
