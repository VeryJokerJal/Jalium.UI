using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSCollapsedTreeAccessibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackChildRowsNeverBecomeHeaderContent(bool expanded)
    {
        using var fixture = new BranchFixture(templated: false);
        fixture.Parent.IsExpanded = expanded; fixture.Layout();
        var peer = Assert.IsType<TreeViewItemAutomationPeer>(fixture.Parent.GetAutomationPeer());
        Assert.Empty(peer.GetHeaderChildren());
        Assert.Equal(expanded ? 1 : 0, fixture.Info(fixture.ParentId).ChildCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollapsingABranchRejectsItsRetainedChildMetadataAndActions(bool templated)
    {
        using var fixture = new BranchFixture(templated);
        var child = fixture.ChildId;
        Assert.True(fixture.TryInfo(child));
        fixture.Parent.IsExpanded = false; fixture.Layout();
        var retainedInfo = fixture.TryInfo(child);
        var retainedPress = fixture.Action(child, MacOSAXAction.Press);
        Assert.False(retainedInfo,
            $"retainedInfo={retainedInfo}, retainedPress={retainedPress}, childSelected={fixture.Child.IsSelected}");
        Assert.False(retainedPress);
        Assert.False(fixture.Child.IsSelected);
        fixture.Parent.IsExpanded = true; fixture.Layout();
        // Expanding realizes children again; a retired realization must not revive.
        var reopened = fixture.CurrentChildId();
        Assert.True(fixture.TryInfo(reopened));
        Assert.True(fixture.Action(reopened, MacOSAXAction.Press));
        Assert.True(fixture.Child.IsSelected);
        if (reopened != child) Assert.False(fixture.TryInfo(child));
    }

    private sealed class BranchFixture : IDisposable
    {
        private readonly DisplayedTestWindow _window;
        private readonly MacOSAccessibilityTree _tree;
        internal TreeViewItem Parent { get; }
        internal TreeViewItem Child { get; }
        internal ulong ParentId { get; }
        internal ulong ChildId { get; }

        internal BranchFixture(bool templated)
        {
            Parent = new TreeViewItem { Header = "父节点", OverridesDefaultStyle = true };
            Child = new TreeViewItem { Header = "子节点", Height = 32, OverridesDefaultStyle = true };
            if (templated)
            {
                var template = new ControlTemplate(typeof(TreeViewItem));
                template.SetVisualTree(() =>
                {
                    var header = new ContentPresenter { Height = 32 };
                    header.SetTemplateBinding(ContentPresenter.ContentProperty, HeaderedItemsControl.HeaderProperty);
                    return new StackPanel { Children = { header, new ItemsPresenter { Name = "PART_ItemsHost" } } };
                });
                Parent.Template = template;
            }
            Parent.Items.Add(Child); Parent.IsExpanded = true;
            var outline = new TreeView { Height = 180, OverridesDefaultStyle = true };
            outline.Items.Add(Parent);
            _window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Content = outline };
            Layout(); _tree = new MacOSAccessibilityTree(_window);
            Assert.True(_tree.TryGetId(Parent.GetAutomationPeer()!, out var parentId)); ParentId = parentId;
            Assert.True(_tree.TryGetId(Child.GetAutomationPeer()!, out var childId)); ChildId = childId;
        }

        internal void Layout()
        {
            _window.Measure(new Size(360, 240)); _window.Arrange(new Rect(0, 0, 360, 240)); _window.UpdateLayout();
        }
        internal MacOSAXRequest Info(ulong id)
        {
            var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Info };
            Assert.True(_tree.Handle(ref request)); return request;
        }
        internal bool TryInfo(ulong id)
        {
            var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Info };
            return _tree.Handle(ref request);
        }
        internal ulong CurrentChildId()
        {
            Assert.True(_tree.TryGetId(Child.GetAutomationPeer()!, out var id)); return id;
        }
        internal bool Action(ulong id, MacOSAXAction action)
        {
            var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Action, Index = (int)action };
            return _tree.Handle(ref request);
        }
        public void Dispose() { _tree.Dispose(); _window.Close(); }
    }
}
