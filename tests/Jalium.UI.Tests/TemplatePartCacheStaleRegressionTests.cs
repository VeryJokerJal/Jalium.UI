using System.Collections.ObjectModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression tests for the pre-existing gap adjacent to the ItemsControl template-lifecycle
/// fix (2026-07-15, see <see cref="ItemsControlTemplateLifecycleRegressionTests"/>): controls
/// that cache template PART references in fields (populated from GetTemplateChild in
/// OnApplyTemplate) never released those references when the template tree was discarded.
///
/// Control.ApplyTemplateCore only re-runs OnApplyTemplate (which reassigns the cached fields)
/// when a NEW non-null template is applied. When the template is cleared to null the fields keep
/// pointing at the discarded, detached tree — every guarded write path (RefreshRows,
/// AddItemWithChildren, the expand/collapse animation, the dropdown builder …) sees a non-null
/// cache, passes its `if (_part == null) return;` guard and silently mutates an invisible tree.
///
/// The fix is a per-control override of Control.OnTemplateContentClearing (the single teardown
/// choke point) that nulls the control's own cached PART fields, so those guards fire correctly.
/// Each test below drives the real teardown path (Template = null) and asserts the field is
/// released — red before the override exists, green after.
/// </summary>
public sealed class TemplatePartCacheStaleRegressionTests
{
    private static readonly Size ViewportSize = new(800, 600);
    private static readonly Rect ViewportRect = new(0, 0, 800, 600);

    private static void RunLayout(UIElement element)
    {
        element.Measure(ViewportSize);
        element.Arrange(ViewportRect);
    }

    /// <summary>Reads a private instance field, searching the whole type hierarchy.</summary>
    private static object? GetPrivateField(object target, string fieldName)
    {
        for (Type? t = target.GetType(); t != null; t = t.BaseType)
        {
            var field = t.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                return field.GetValue(target);
            }
        }

        throw new InvalidOperationException($"Field '{fieldName}' not found on {target.GetType().Name}.");
    }

    private static ControlTemplate TemplateWithRoot(Type targetType, Func<FrameworkElement> factory)
    {
        var template = new ControlTemplate(targetType);
        template.SetVisualTree(factory);
        return template;
    }

    /// <summary>
    /// Applies <paramref name="template"/>, confirms <paramref name="fieldName"/> was cached,
    /// then clears the template and asserts the cache was released instead of pinning the dead tree.
    /// </summary>
    private static void AssertCachedPartReleasedOnTemplateClear(
        Control control, ControlTemplate template, string fieldName)
    {
        control.Template = template;
        RunLayout(control);

        Assert.NotNull(GetPrivateField(control, fieldName));

        control.Template = null;
        RunLayout(control);

        Assert.Null(GetPrivateField(control, fieldName));
    }

    // ---------------------------------------------------------------------------------------
    // Per-control cached-PART release on template clear.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void DataGrid_ClearingTemplate_ReleasesRowsHostCache()
    {
        var dataGrid = new DataGrid { AutoGenerateColumns = false };
        var template = TemplateWithRoot(typeof(DataGrid),
            () => new StackPanel { Name = "PART_RowsHost" });

        AssertCachedPartReleasedOnTemplateClear(dataGrid, template, "_rowsHost");
    }

    [Fact]
    public void TreeDataGrid_ClearingTemplate_ReleasesRowsHostCache()
    {
        var treeDataGrid = new TreeDataGrid();
        var template = TemplateWithRoot(typeof(TreeDataGrid),
            () => new StackPanel { Name = "PART_RowsHost" });

        AssertCachedPartReleasedOnTemplateClear(treeDataGrid, template, "_rowsHost");
    }

    [Fact]
    public void TreeViewItem_ClearingTemplate_ReleasesItemsHostCache()
    {
        var item = new TreeViewItem { Header = "Root", IsExpanded = true };
        item.Items.Add(new TreeViewItem { Header = "Child" });
        var template = TemplateWithRoot(typeof(TreeViewItem),
            () => new Border { Name = "PART_ItemsHost" });

        AssertCachedPartReleasedOnTemplateClear(item, template, "_itemsHost");
    }

    [Fact]
    public void TreeSelectorItem_ClearingTemplate_ReleasesItemsHostCache()
    {
        var item = new TreeSelectorItem { Header = "Root", IsExpanded = true };
        item.Items.Add(new TreeSelectorItem { Header = "Child" });
        var template = TemplateWithRoot(typeof(TreeSelectorItem),
            () => new Border { Name = "PART_ItemsHost" });

        AssertCachedPartReleasedOnTemplateClear(item, template, "_itemsHost");
    }

    [Fact]
    public void NavigationViewItem_ClearingTemplate_ReleasesChildrenPanelCache()
    {
        var item = new NavigationViewItem { Content = "Item" };
        var template = TemplateWithRoot(typeof(NavigationViewItem),
            () => new StackPanel { Name = "PART_ChildrenPanel" });

        AssertCachedPartReleasedOnTemplateClear(item, template, "_childrenPanel");
    }

    [Fact]
    public void NavigationView_ClearingTemplate_ReleasesMenuItemsHostCache()
    {
        var navView = new NavigationView();
        var template = TemplateWithRoot(typeof(NavigationView), () =>
        {
            var grid = new Grid();
            grid.Children.Add(new StackPanel { Name = "PART_MenuItemsHost" });
            grid.Children.Add(new StackPanel { Name = "PART_FooterItemsHost" });
            return grid;
        });

        AssertCachedPartReleasedOnTemplateClear(navView, template, "_menuItemsPanel");
    }

    [Fact]
    public void AutoCompleteBox_ClearingTemplate_ReleasesDropDownItemsHostCache()
    {
        var box = new AutoCompleteBox();
        var template = TemplateWithRoot(typeof(AutoCompleteBox), () =>
        {
            var grid = new Grid();
            grid.Children.Add(new StackPanel { Name = "PART_DropDownItemsHost" });
            grid.Children.Add(new Popup { Name = "PART_Popup" });
            return grid;
        });

        AssertCachedPartReleasedOnTemplateClear(box, template, "_dropDownItemsPanel");
    }

    // ---------------------------------------------------------------------------------------
    // Behavioural leak check: a stateful write after the template is cleared must not resurface
    // in the discarded PART. Proves the user-visible half of the bug, not just the field state.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void DataGrid_RowRefreshAfterTemplateClear_DoesNotLeakIntoDiscardedRowsHost()
    {
        var rows = new ObservableCollection<string> { "a", "b" };
        var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = rows };
        var template = TemplateWithRoot(typeof(DataGrid),
            () => new StackPanel { Name = "PART_RowsHost" });

        dataGrid.Template = template;
        RunLayout(dataGrid);

        var discardedRowsHost = Assert.IsAssignableFrom<Panel>(GetPrivateField(dataGrid, "_rowsHost")!);
        int childCountWhileLive = discardedRowsHost.Children.Count;

        dataGrid.Template = null;
        RunLayout(dataGrid);

        // A data change now drives RefreshRows. With the cache released the guard trips and the
        // write is a no-op; without the fix the new rows realize into the detached, invisible host.
        rows.Add("c");
        RunLayout(dataGrid);

        Assert.Equal(childCountWhileLive, discardedRowsHost.Children.Count);
    }

    // ---------------------------------------------------------------------------------------
    // Zombie ItemsPresenter re-registration window.
    //
    // When a template is replaced before its ItemsPresenter is ever measured, the presenter is
    // never registered (AttachToOwner runs from MeasureOverride) so the OnTemplateContentClearing
    // membership test (it only inspects the registered presenter) cannot retire it, and nothing
    // clears its TemplatedParent. A later stray measure of that discarded presenter walked up
    // TemplatedParent, found the control and hijacked the live registration through
    // SetItemsPresenter — pointing ItemsHost at the dead tree and (for item-is-own-container
    // items) reparenting a live container into the dead panel.
    //
    // The fix marks each presenter with its template-instance root during SetTemplatedParentRecursive
    // and rejects the attach when that root is no longer the control's current template root.
    // ---------------------------------------------------------------------------------------

    private static ControlTemplate PresenterTemplate(Action<ItemsPresenter> capture)
    {
        var template = new ControlTemplate(typeof(ListBox));
        template.SetVisualTree(() =>
        {
            var presenter = new ItemsPresenter();
            capture(presenter);
            return presenter;
        });
        return template;
    }

    [Fact]
    public void NeverMeasuredDiscardedPresenter_CannotHijackLiveRegistration()
    {
        var listBox = new ListBox();
        listBox.Items.Add("item 0");
        listBox.Items.Add("item 1");
        listBox.Items.Add("item 2");

        ItemsPresenter? p1 = null;
        listBox.Template = PresenterTemplate(p => p1 = p); // eager apply; p1 never measured

        ItemsPresenter? p2 = null;
        listBox.Template = PresenterTemplate(p => p2 = p); // discards t1; p1 escapes retirement

        RunLayout(listBox);

        // p2 is the live host.
        Assert.NotNull(p2);
        Assert.Same(p2!.ItemsPanel, listBox.ItemsHostInternal);
        Assert.Equal(3, listBox.ItemsHostInternal!.Children.Count);

        // A stray measure of the discarded, never-registered p1 must not steal the registration.
        Assert.NotNull(p1);
        p1!.Measure(ViewportSize);

        Assert.Same(p2.ItemsPanel, listBox.ItemsHostInternal);
        Assert.Equal(3, listBox.ItemsHostInternal.Children.Count);
    }

    [Fact]
    public void NeverMeasuredDiscardedPresenter_WithOwnContainerItems_DoesNotReparentOrThrow()
    {
        var listBox = new ListBox();
        var liveContainer0 = new ListBoxItem { Content = "own 0" };
        var liveContainer1 = new ListBoxItem { Content = "own 1" };
        listBox.Items.Add(liveContainer0);
        listBox.Items.Add(liveContainer1);

        ItemsPresenter? p1 = null;
        listBox.Template = PresenterTemplate(p => p1 = p);

        ItemsPresenter? p2 = null;
        listBox.Template = PresenterTemplate(p => p2 = p);

        RunLayout(listBox);

        var liveHost = listBox.ItemsHostInternal;
        Assert.NotNull(liveHost);
        Assert.Same(p2!.ItemsPanel, liveHost);

        // The zombie measure must neither throw ("logical child already has a parent") nor pull the
        // live containers into the discarded panel.
        Assert.NotNull(p1);
        var exception = Record.Exception(() => p1!.Measure(ViewportSize));

        Assert.Null(exception);
        Assert.Same(p2.ItemsPanel, listBox.ItemsHostInternal);
        Assert.Same(liveHost, listBox.ItemsHostInternal);

        // The live containers stay parented under the live host, not the discarded panel.
        Assert.Same(liveHost, liveContainer0.Parent);
        Assert.Same(liveHost, liveContainer1.Parent);
    }

    // ---------------------------------------------------------------------------------------
    // Second wave: the same stateful cached-PART hazard on the sibling controls surfaced by the
    // adversarial completeness pass (ComboBox / Expander / ToggleSwitch / PropertyGrid /
    // TreeSelector). MenuItem is intentionally absent — it caches no template PART (submenu popup
    // is programmatic).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ComboBox_ClearingTemplate_ReleasesPopupCache()
    {
        var comboBox = new ComboBox();
        var template = TemplateWithRoot(typeof(ComboBox), () =>
        {
            var grid = new Grid();
            grid.Children.Add(new ToggleButton { Name = "PART_ToggleButton" });
            grid.Children.Add(new TextBox { Name = "PART_EditableTextBox" });
            grid.Children.Add(new Popup { Name = "PART_Popup" });
            return grid;
        });

        AssertCachedPartReleasedOnTemplateClear(comboBox, template, "_popup");
    }

    [Fact]
    public void Expander_ClearingTemplate_ReleasesContentBorderCache()
    {
        var expander = new Expander { Header = "H" };
        var template = TemplateWithRoot(typeof(Expander), () =>
        {
            var grid = new Grid();
            grid.Children.Add(new Border { Name = "PART_HeaderBorder" });
            grid.Children.Add(new Border { Name = "PART_ContentBorder" });
            return grid;
        });

        AssertCachedPartReleasedOnTemplateClear(expander, template, "_contentBorder");
    }

    [Fact]
    public void ToggleSwitch_ClearingTemplate_ReleasesSwitchThumbCache()
    {
        var toggle = new ToggleSwitch();
        var template = TemplateWithRoot(typeof(ToggleSwitch), () =>
        {
            var grid = new Grid();
            grid.Children.Add(new Border { Name = "PART_SwitchTrack" });
            grid.Children.Add(new Jalium.UI.Shapes.Ellipse { Name = "PART_SwitchThumb" });
            return grid;
        });

        AssertCachedPartReleasedOnTemplateClear(toggle, template, "_switchThumb");
    }

    [Fact]
    public void ToggleSwitch_ClearingTemplate_StopsSpringAnimationSubscription()
    {
        var toggle = new ToggleSwitch();
        var template = TemplateWithRoot(typeof(ToggleSwitch), () =>
        {
            var grid = new Grid();
            grid.Children.Add(new Border { Name = "PART_SwitchTrack" });
            grid.Children.Add(new Jalium.UI.Shapes.Ellipse { Name = "PART_SwitchThumb" });
            return grid;
        });

        toggle.Template = template;
        RunLayout(toggle);

        // Drive a state change so the spring animation subscribes to CompositionTarget.Rendering.
        toggle.IsOn = true;
        Assert.True((bool)GetPrivateField(toggle, "_springSubscribed")!);

        // Clearing the template must detach the global compositor tick, not leave it pinned.
        toggle.Template = null;
        RunLayout(toggle);

        Assert.False((bool)GetPrivateField(toggle, "_springSubscribed")!);
    }

    [Fact]
    public void PropertyGrid_ClearingTemplate_ReleasesPropertiesPanelCache()
    {
        var grid = new PropertyGrid();
        var template = TemplateWithRoot(typeof(PropertyGrid),
            () => new StackPanel { Name = "PART_PropertiesPanel" });

        AssertCachedPartReleasedOnTemplateClear(grid, template, "_propertiesPanel");
    }

    [Fact]
    public void PropertyGrid_AfterTemplateClear_RebuildsFallbackInsteadOfRenderingBlank()
    {
        var grid = new PropertyGrid();
        var template = TemplateWithRoot(typeof(PropertyGrid),
            () => new StackPanel { Name = "PART_PropertiesPanel" });

        grid.Template = template;
        RunLayout(grid);
        Assert.NotNull(GetPrivateField(grid, "_propertiesPanel"));

        grid.Template = null;
        // A property refresh now runs EnsurePropertiesPanel. With the stale _propertiesPanel
        // released it rebuilds the programmatic fallback tree; without the fix it short-circuits
        // on the stale reference and the control stays blank (_fallbackRoot never built).
        grid.SelectedObject = new { Name = "value", Count = 3 };
        RunLayout(grid);

        Assert.NotNull(GetPrivateField(grid, "_fallbackRoot"));
    }

    [Fact]
    public void TreeSelector_ClearingTemplate_ReleasesTagsPanelCache()
    {
        var selector = new TreeSelector();
        var template = TemplateWithRoot(typeof(TreeSelector), () =>
        {
            var grid = new Grid();
            grid.Children.Add(new Border { Name = "PART_TriggerBorder" });
            grid.Children.Add(new StackPanel { Name = "PART_TagsPanel" });
            grid.Children.Add(new Popup { Name = "PART_Popup" });
            return grid;
        });

        AssertCachedPartReleasedOnTemplateClear(selector, template, "_tagsPanel");
    }

    // ---------------------------------------------------------------------------------------
    // Test-gap closure: a third consecutive template swap (A→B→C) leaves two never-measured
    // discarded presenters; neither may hijack the live registration.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ThreeConsecutiveTemplateSwaps_NoDiscardedPresenterHijacksRegistration()
    {
        var listBox = new ListBox();
        listBox.Items.Add("item 0");
        listBox.Items.Add("item 1");
        listBox.Items.Add("item 2");

        ItemsPresenter? p1 = null, p2 = null, p3 = null;
        listBox.Template = PresenterTemplate(p => p1 = p);
        listBox.Template = PresenterTemplate(p => p2 = p);
        listBox.Template = PresenterTemplate(p => p3 = p);

        RunLayout(listBox);

        Assert.NotNull(p3);
        Assert.Same(p3!.ItemsPanel, listBox.ItemsHostInternal);
        Assert.Equal(3, listBox.ItemsHostInternal!.Children.Count);

        // Both earlier discarded presenters are zombies; measuring either must be inert.
        p1!.Measure(ViewportSize);
        p2!.Measure(ViewportSize);

        Assert.Same(p3.ItemsPanel, listBox.ItemsHostInternal);
        Assert.Equal(3, listBox.ItemsHostInternal.Children.Count);
    }
}
