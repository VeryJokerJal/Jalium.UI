using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class TreeSelectorTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void TreeSelector_Defaults_AreSingleSelectWithoutCheckBoxes()
    {
        var selector = new TreeSelector();

        Assert.Equal(SelectionMode.Single, selector.SelectionMode);
        Assert.False(selector.ShowCheckBoxes);
        Assert.Equal(TreeSelectorCheckCascadeMode.None, selector.CheckCascadeMode);
        Assert.Empty(selector.SelectedItems);
        Assert.Empty(selector.CheckedItems);
        Assert.Null(selector.SelectedItem);
    }

    [Fact]
    public void TreeSelectorItem_AddedToSelector_InheritsParentReference()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector();
            var rootItem = new TreeSelectorItem { Header = "Root" };
            selector.Items.Add(rootItem);

            var host = new Grid { Width = 320, Height = 200 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 200));
            host.Arrange(new Rect(0, 0, 320, 200));

            Assert.Same(selector, rootItem.ParentSelector);
            Assert.Equal(0, rootItem.Level);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeSelectorItem_NestedItems_PropagateLevelAndParent()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector();
            var root = new TreeSelectorItem { Header = "Root", IsExpanded = true };
            var child = new TreeSelectorItem { Header = "Child", IsExpanded = true };
            var grandchild = new TreeSelectorItem { Header = "Grandchild" };

            child.Items.Add(grandchild);
            root.Items.Add(child);
            selector.Items.Add(root);

            var host = new Grid { Width = 320, Height = 240 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));

            Assert.Equal(0, root.Level);
            Assert.Equal(1, child.Level);
            Assert.Equal(2, grandchild.Level);
            Assert.Same(selector, grandchild.ParentSelector);
            Assert.Same(child, grandchild.ParentItem);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeSelector_HandleItemActivated_SingleMode_SelectsOnlyClickedItem()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector();
            var a = new TreeSelectorItem { Header = "A" };
            var b = new TreeSelectorItem { Header = "B" };
            selector.Items.Add(a);
            selector.Items.Add(b);

            var host = new Grid { Width = 320, Height = 200 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 200));
            host.Arrange(new Rect(0, 0, 320, 200));

            selector.HandleItemActivated(a, isCtrlPressed: false, isShiftPressed: false);
            Assert.True(a.IsSelected);
            Assert.False(b.IsSelected);
            Assert.Equal("A", selector.SelectedItem);

            selector.HandleItemActivated(b, isCtrlPressed: false, isShiftPressed: false);
            Assert.False(a.IsSelected);
            Assert.True(b.IsSelected);
            Assert.Equal("B", selector.SelectedItem);
            Assert.Single(selector.SelectedItems);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeSelector_HandleItemActivated_MultipleMode_TogglesIndividually()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector { SelectionMode = SelectionMode.Multiple };
            var a = new TreeSelectorItem { Header = "A" };
            var b = new TreeSelectorItem { Header = "B" };
            var c = new TreeSelectorItem { Header = "C" };
            selector.Items.Add(a);
            selector.Items.Add(b);
            selector.Items.Add(c);

            var host = new Grid { Width = 320, Height = 200 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 200));
            host.Arrange(new Rect(0, 0, 320, 200));

            selector.HandleItemActivated(a, isCtrlPressed: false, isShiftPressed: false);
            selector.HandleItemActivated(c, isCtrlPressed: false, isShiftPressed: false);
            Assert.True(a.IsSelected);
            Assert.False(b.IsSelected);
            Assert.True(c.IsSelected);
            Assert.Equal(2, selector.SelectedItems.Count);

            // Activating an already-selected item toggles it off.
            selector.HandleItemActivated(a, isCtrlPressed: false, isShiftPressed: false);
            Assert.False(a.IsSelected);
            Assert.Single(selector.SelectedItems);
            Assert.Equal("C", selector.SelectedItem);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeSelector_SelectAll_OnlyAffectsMultipleAndExtended()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector { SelectionMode = SelectionMode.Single };
            selector.Items.Add(new TreeSelectorItem { Header = "A" });
            selector.Items.Add(new TreeSelectorItem { Header = "B" });

            var host = new Grid { Width = 320, Height = 200 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 200));
            host.Arrange(new Rect(0, 0, 320, 200));

            selector.SelectAll();
            Assert.Empty(selector.SelectedItems);

            selector.SelectionMode = SelectionMode.Multiple;
            selector.SelectAll();
            Assert.Equal(2, selector.SelectedItems.Count);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeSelector_UnselectAll_ClearsSelection()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector { SelectionMode = SelectionMode.Multiple };
            var a = new TreeSelectorItem { Header = "A" };
            selector.Items.Add(a);

            var host = new Grid { Width = 320, Height = 200 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 200));
            host.Arrange(new Rect(0, 0, 320, 200));

            selector.HandleItemActivated(a, isCtrlPressed: false, isShiftPressed: false);
            Assert.Single(selector.SelectedItems);

            selector.UnselectAll();
            Assert.Empty(selector.SelectedItems);
            Assert.False(a.IsSelected);
            Assert.Null(selector.SelectedItem);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeSelectorItem_CheckCascade_SetsAllDescendantsTrue()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector
            {
                ShowCheckBoxes = true,
                CheckCascadeMode = TreeSelectorCheckCascadeMode.Cascade
            };
            var root = new TreeSelectorItem { Header = "Root" };
            var c1 = new TreeSelectorItem { Header = "C1" };
            var c2 = new TreeSelectorItem { Header = "C2" };
            var gc = new TreeSelectorItem { Header = "GC" };
            c1.Items.Add(gc);
            root.Items.Add(c1);
            root.Items.Add(c2);
            selector.Items.Add(root);

            var host = new Grid { Width = 320, Height = 240 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));

            root.IsChecked = true;

            Assert.True(c1.IsChecked);
            Assert.True(c2.IsChecked);
            Assert.True(gc.IsChecked);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeSelectorItem_CheckCascade_ParentBecomesIndeterminate_WhenChildrenDisagree()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector
            {
                ShowCheckBoxes = true,
                CheckCascadeMode = TreeSelectorCheckCascadeMode.Cascade
            };
            var root = new TreeSelectorItem { Header = "Root" };
            var c1 = new TreeSelectorItem { Header = "C1" };
            var c2 = new TreeSelectorItem { Header = "C2" };
            root.Items.Add(c1);
            root.Items.Add(c2);
            selector.Items.Add(root);

            var host = new Grid { Width = 320, Height = 240 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));

            // Check only c1 — the parent should become indeterminate (null).
            c1.IsChecked = true;

            Assert.Null(root.IsChecked);
            Assert.True(c1.IsChecked);
            Assert.False(c2.IsChecked);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeSelector_RegisteredInXamlTypeRegistry()
    {
        // <Style TargetType="TreeSelector"> / <Style TargetType="TreeSelectorItem">
        // require both types to resolve via XamlTypeRegistry.GetType.
        Assert.Equal(typeof(TreeSelector), Jalium.UI.Markup.XamlTypeRegistry.GetType("TreeSelector"));
        Assert.Equal(typeof(TreeSelectorItem), Jalium.UI.Markup.XamlTypeRegistry.GetType("TreeSelectorItem"));
    }

    [Fact]
    public void TreeSelector_Defaults_AreDropDownClosedAndNotSearching()
    {
        var selector = new TreeSelector();

        Assert.False(selector.IsDropDownOpen);
        Assert.False(selector.IsSearchEnabled);
        Assert.Equal(string.Empty, selector.SearchText);
        Assert.Equal(string.Empty, selector.PlaceholderText);
        Assert.Equal(" / ", selector.PathSeparator);
        Assert.Equal(320.0, selector.MaxDropDownHeight);
    }

    [Fact]
    public void TreeSelector_SingleModeSelection_AutoClosesDropDown()
    {
        var selector = new TreeSelector { IsDropDownOpen = true };
        var item = new TreeSelectorItem { Header = "A" };
        selector.Items.Add(item);

        selector.HandleItemActivated(item, isCtrlPressed: false, isShiftPressed: false);

        Assert.False(selector.IsDropDownOpen);
        Assert.Equal("A", selector.SelectedItem);
    }

    [Fact]
    public void TreeSelector_MultipleModeSelection_KeepsDropDownOpen()
    {
        var selector = new TreeSelector
        {
            SelectionMode = SelectionMode.Multiple,
            IsDropDownOpen = true
        };
        var a = new TreeSelectorItem { Header = "A" };
        var b = new TreeSelectorItem { Header = "B" };
        selector.Items.Add(a);
        selector.Items.Add(b);

        selector.HandleItemActivated(a, isCtrlPressed: false, isShiftPressed: false);
        selector.HandleItemActivated(b, isCtrlPressed: false, isShiftPressed: false);

        Assert.True(selector.IsDropDownOpen);
        Assert.Equal(2, selector.SelectedItems.Count);
    }

    [Fact]
    public void TreeSelector_RemoveFromSelection_DeselectsAndUpdatesContainer()
    {
        var selector = new TreeSelector { SelectionMode = SelectionMode.Multiple };
        var a = new TreeSelectorItem { Header = "A" };
        var b = new TreeSelectorItem { Header = "B" };
        selector.Items.Add(a);
        selector.Items.Add(b);

        selector.HandleItemActivated(a, isCtrlPressed: false, isShiftPressed: false);
        selector.HandleItemActivated(b, isCtrlPressed: false, isShiftPressed: false);
        Assert.Equal(2, selector.SelectedItems.Count);

        selector.RemoveFromSelection("A");

        Assert.Single(selector.SelectedItems);
        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);
        Assert.Equal("B", selector.SelectedItem);
    }

    [Fact]
    public void TreeSelector_DropDownOpenedAndClosedEvents_FireOnToggle()
    {
        var selector = new TreeSelector();
        int openedCount = 0, closedCount = 0;
        selector.DropDownOpened += (_, _) => openedCount++;
        selector.DropDownClosed += (_, _) => closedCount++;

        selector.IsDropDownOpen = true;
        Assert.Equal(1, openedCount);
        Assert.Equal(0, closedCount);

        selector.IsDropDownOpen = false;
        Assert.Equal(1, openedCount);
        Assert.Equal(1, closedCount);
    }

    [Fact]
    public void TreeSelector_SearchText_HidesNonMatchingItems()
    {
        var selector = new TreeSelector { IsSearchEnabled = true };
        var east = new TreeSelectorItem { Header = "East" };
        var shanghai = new TreeSelectorItem { Header = "Shanghai" };
        var pudong = new TreeSelectorItem { Header = "Pudong" };
        var xuhui = new TreeSelectorItem { Header = "Xuhui" };
        shanghai.Items.Add(pudong);
        shanghai.Items.Add(xuhui);
        east.Items.Add(shanghai);
        var west = new TreeSelectorItem { Header = "West" };
        west.Items.Add(new TreeSelectorItem { Header = "Chengdu" });
        selector.Items.Add(east);
        selector.Items.Add(west);

        selector.SearchText = "pud";

        // Pudong (and its ancestors East / Shanghai, because they contain a matching descendant) stay visible.
        Assert.Equal(Visibility.Visible, east.Visibility);
        Assert.Equal(Visibility.Visible, shanghai.Visibility);
        Assert.Equal(Visibility.Visible, pudong.Visibility);
        // Xuhui does not match — even though its parent does.
        Assert.Equal(Visibility.Collapsed, xuhui.Visibility);
        // The whole West subtree has no match → hidden.
        Assert.Equal(Visibility.Collapsed, west.Visibility);

        // Clearing the search restores everything.
        selector.SearchText = string.Empty;
        Assert.Equal(Visibility.Visible, west.Visibility);
        Assert.Equal(Visibility.Visible, xuhui.Visibility);
    }

    [Fact]
    public void TreeSelector_BuildPathString_JoinsAncestorHeadersWithSeparator()
    {
        var selector = new TreeSelector { PathSeparator = " / " };
        var root = new TreeSelectorItem { Header = "East", IsExpanded = true };
        var shanghai = new TreeSelectorItem { Header = "Shanghai", IsExpanded = true };
        var pudong = new TreeSelectorItem { Header = "Pudong" };
        shanghai.Items.Add(pudong);
        root.Items.Add(shanghai);
        selector.Items.Add(root);

        selector.HandleItemActivated(pudong, isCtrlPressed: false, isShiftPressed: false);

        var method = typeof(TreeSelector).GetMethod("BuildPathString",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = (string)method!.Invoke(selector, new[] { (object?)"Pudong" })!;

        Assert.Equal("East / Shanghai / Pudong", result);
    }

    [Fact]
    public void TreeSelector_SelectionChangedEvent_FiresWithDelta()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector { SelectionMode = SelectionMode.Multiple };
            var a = new TreeSelectorItem { Header = "A" };
            var b = new TreeSelectorItem { Header = "B" };
            selector.Items.Add(a);
            selector.Items.Add(b);

            var host = new Grid { Width = 320, Height = 200 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 200));
            host.Arrange(new Rect(0, 0, 320, 200));

            object[]? capturedAdded = null;
            object[]? capturedRemoved = null;
            selector.SelectionChanged += (_, e) =>
            {
                capturedAdded = e.AddedItems.Cast<object>().ToArray();
                capturedRemoved = e.RemovedItems.Cast<object>().ToArray();
            };

            selector.HandleItemActivated(a, isCtrlPressed: false, isShiftPressed: false);
            Assert.NotNull(capturedAdded);
            Assert.Single(capturedAdded!);
            Assert.Equal("A", capturedAdded![0]);
            Assert.Empty(capturedRemoved!);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeSelectorItem_AutomationPeer_ExposesExpandCollapseAndToggle()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var selector = new TreeSelector { ShowCheckBoxes = true };
            var item = new TreeSelectorItem { Header = "Root" };
            item.Items.Add(new TreeSelectorItem { Header = "Child" });
            selector.Items.Add(item);

            var host = new Grid { Width = 320, Height = 200 };
            host.Children.Add(selector);
            host.Measure(new Size(320, 200));
            host.Arrange(new Rect(0, 0, 320, 200));

            var peer = new Jalium.UI.Automation.Peers.TreeSelectorItemAutomationPeer(item);
            Assert.Equal(Jalium.UI.Automation.ExpandCollapseState.Collapsed, peer.ExpandCollapseState);

            peer.Expand();
            Assert.True(item.IsExpanded);
            Assert.Equal(Jalium.UI.Automation.ExpandCollapseState.Expanded, peer.ExpandCollapseState);

            peer.Toggle();
            Assert.True(item.IsChecked);

            peer.Toggle();
            Assert.False(item.IsChecked);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static T? GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(instance) as T;
    }
}
