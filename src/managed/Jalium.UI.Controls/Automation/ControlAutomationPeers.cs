using System.Collections.Generic;
using Jalium.UI.Automation;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls.Automation;

#region Selector Controls

/// <summary>
/// Exposes ListBox types to UI Automation.
/// </summary>
public sealed class ListBoxAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    /// <summary>
    /// Initializes a new instance of the ListBoxAutomationPeer class.
    /// </summary>
    public ListBoxAutomationPeer(ListBox owner) : base(owner)
    {
    }

    private ListBox ListBoxOwner => (ListBox)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.List;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(ListBox);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Selection)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region ISelectionProvider

    /// <inheritdoc />
    public AutomationPeer[] GetSelection()
    {
        // Simplified - return empty if no way to get container
        return Array.Empty<AutomationPeer>();
    }

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    public bool CanSelectMultiple => ListBoxOwner.SelectionMode != SelectionMode.Single;

    #endregion
}

/// <summary>
/// Exposes ListBoxItem types to UI Automation.
/// </summary>
public sealed class ListBoxItemAutomationPeer : FrameworkElementAutomationPeer, ISelectionItemProvider, IScrollItemProvider
{
    /// <summary>
    /// Initializes a new instance of the ListBoxItemAutomationPeer class.
    /// </summary>
    public ListBoxItemAutomationPeer(ListBoxItem owner) : base(owner)
    {
    }

    private ListBoxItem ItemOwner => (ListBoxItem)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.ListItem;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(ListBoxItem);
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        var content = ItemOwner.Content;
        if (content is string text)
            return text;
        return content?.ToString() ?? base.GetNameCore();
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.SelectionItem)
            return this;

        if (patternInterface == PatternInterface.ScrollItem)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region ISelectionItemProvider

    /// <inheritdoc />
    public bool IsSelected => ItemOwner.IsSelected;

    /// <inheritdoc />
    public AutomationPeer SelectionContainer
    {
        get
        {
            var listBox = ItemOwner.ParentListBox;
            if (listBox != null)
                return listBox.GetAutomationPeer() ?? new ListBoxAutomationPeer(listBox);
            return null!;
        }
    }

    /// <inheritdoc />
    public void Select()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public void AddToSelection()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        ItemOwner.IsSelected = false;
    }

    #endregion

    #region IScrollItemProvider

    /// <inheritdoc />
    public void ScrollIntoView()
    {
        ItemOwner.BringIntoView();
    }

    #endregion
}

/// <summary>
/// Exposes ComboBox types to UI Automation.
/// </summary>
public sealed class ComboBoxAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider, ISelectionProvider
{
    /// <summary>
    /// Initializes a new instance of the ComboBoxAutomationPeer class.
    /// </summary>
    public ComboBoxAutomationPeer(ComboBox owner) : base(owner)
    {
    }

    private ComboBox ComboBoxOwner => (ComboBox)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.ComboBox;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(ComboBox);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.ExpandCollapse)
            return this;

        if (patternInterface == PatternInterface.Selection)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region IExpandCollapseProvider

    /// <inheritdoc />
    public ExpandCollapseState ExpandCollapseState =>
        ComboBoxOwner.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <inheritdoc />
    public void Expand()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot expand a disabled ComboBox.");
        ComboBoxOwner.IsDropDownOpen = true;
    }

    /// <inheritdoc />
    public void Collapse()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot collapse a disabled ComboBox.");
        ComboBoxOwner.IsDropDownOpen = false;
    }

    #endregion

    #region ISelectionProvider

    /// <inheritdoc />
    public AutomationPeer[] GetSelection()
    {
        return Array.Empty<AutomationPeer>();
    }

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    public bool CanSelectMultiple => false;

    #endregion
}

#endregion

#region TreeView

/// <summary>
/// Exposes TreeView types to UI Automation.
/// </summary>
public sealed class TreeViewAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    /// <summary>
    /// Initializes a new instance of the TreeViewAutomationPeer class.
    /// </summary>
    public TreeViewAutomationPeer(TreeView owner) : base(owner)
    {
    }

    private TreeView TreeViewOwner => (TreeView)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Tree;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(TreeView);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Selection)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region ISelectionProvider

    /// <inheritdoc />
    public AutomationPeer[] GetSelection()
    {
        return Array.Empty<AutomationPeer>();
    }

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    public bool CanSelectMultiple => false;

    #endregion
}

/// <summary>
/// Exposes TreeViewItem types to UI Automation.
/// </summary>
public sealed class TreeViewItemAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider, ISelectionItemProvider, IScrollItemProvider
{
    /// <summary>
    /// Initializes a new instance of the TreeViewItemAutomationPeer class.
    /// </summary>
    public TreeViewItemAutomationPeer(TreeViewItem owner) : base(owner)
    {
    }

    private TreeViewItem ItemOwner => (TreeViewItem)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.TreeItem;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(TreeViewItem);
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        var header = ItemOwner.Header;
        if (header is string text)
            return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.ExpandCollapse && ItemOwner.HasItems)
            return this;

        if (patternInterface == PatternInterface.SelectionItem)
            return this;

        if (patternInterface == PatternInterface.ScrollItem)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region IExpandCollapseProvider

    /// <inheritdoc />
    public ExpandCollapseState ExpandCollapseState
    {
        get
        {
            if (!ItemOwner.HasItems)
                return ExpandCollapseState.LeafNode;
            return ItemOwner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        }
    }

    /// <inheritdoc />
    public void Expand()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot expand a disabled TreeViewItem.");
        ItemOwner.IsExpanded = true;
    }

    /// <inheritdoc />
    public void Collapse()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot collapse a disabled TreeViewItem.");
        ItemOwner.IsExpanded = false;
    }

    #endregion

    #region ISelectionItemProvider

    /// <inheritdoc />
    public bool IsSelected => ItemOwner.IsSelected;

    /// <inheritdoc />
    public AutomationPeer SelectionContainer
    {
        get
        {
            // Walk up to find TreeView
            var parent = ItemOwner.VisualParent;
            while (parent != null)
            {
                if (parent is TreeView treeView)
                    return treeView.GetAutomationPeer() ?? new TreeViewAutomationPeer(treeView);
                parent = (parent as FrameworkElement)?.VisualParent;
            }
            return null!;
        }
    }

    /// <inheritdoc />
    public void Select()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public void AddToSelection()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        ItemOwner.IsSelected = false;
    }

    #endregion

    #region IScrollItemProvider

    /// <inheritdoc />
    public void ScrollIntoView()
    {
        ItemOwner.BringIntoView();
    }

    #endregion
}

#endregion

#region Range Controls

/// <summary>
/// Exposes Slider types to UI Automation.
/// </summary>
public sealed class SliderAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    /// <summary>
    /// Initializes a new instance of the SliderAutomationPeer class.
    /// </summary>
    public SliderAutomationPeer(Slider owner) : base(owner)
    {
    }

    private Slider SliderOwner => (Slider)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Slider;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(Slider);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.RangeValue)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region IRangeValueProvider

    /// <inheritdoc />
    public double Value => SliderOwner.Value;

    /// <inheritdoc />
    public double Minimum => SliderOwner.Minimum;

    /// <inheritdoc />
    public double Maximum => SliderOwner.Maximum;

    /// <inheritdoc />
    public double SmallChange => SliderOwner.SmallChange;

    /// <inheritdoc />
    public double LargeChange => SliderOwner.LargeChange;

    /// <inheritdoc />
    public bool IsReadOnly => !IsEnabled();

    /// <inheritdoc />
    public void SetValue(double value)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled Slider.");

        if (value < Minimum || value > Maximum)
            throw new ArgumentOutOfRangeException(nameof(value));

        SliderOwner.Value = value;
    }

    #endregion
}

/// <summary>
/// Exposes ProgressBar types to UI Automation.
/// </summary>
public sealed class ProgressBarAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    /// <summary>
    /// Initializes a new instance of the ProgressBarAutomationPeer class.
    /// </summary>
    public ProgressBarAutomationPeer(ProgressBar owner) : base(owner)
    {
    }

    private ProgressBar ProgressBarOwner => (ProgressBar)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.ProgressBar;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(ProgressBar);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.RangeValue && !ProgressBarOwner.IsIndeterminate)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region IRangeValueProvider

    /// <inheritdoc />
    public double Value => ProgressBarOwner.Value;

    /// <inheritdoc />
    public double Minimum => ProgressBarOwner.Minimum;

    /// <inheritdoc />
    public double Maximum => ProgressBarOwner.Maximum;

    /// <inheritdoc />
    public double SmallChange => 0;

    /// <inheritdoc />
    public double LargeChange => 0;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public void SetValue(double value)
    {
        throw new InvalidOperationException("ProgressBar value cannot be set via automation.");
    }

    #endregion
}

#endregion

#region Tab Controls

/// <summary>
/// Exposes TabControl types to UI Automation.
/// </summary>
public sealed class TabControlAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    /// <summary>
    /// Initializes a new instance of the TabControlAutomationPeer class.
    /// </summary>
    public TabControlAutomationPeer(TabControl owner) : base(owner)
    {
    }

    private TabControl TabControlOwner => (TabControl)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Tab;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(TabControl);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Selection)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region ISelectionProvider

    /// <inheritdoc />
    public AutomationPeer[] GetSelection()
    {
        return Array.Empty<AutomationPeer>();
    }

    /// <inheritdoc />
    public bool IsSelectionRequired => true;

    /// <inheritdoc />
    public bool CanSelectMultiple => false;

    #endregion
}

/// <summary>
/// Exposes TabItem types to UI Automation.
/// </summary>
public sealed class TabItemAutomationPeer : FrameworkElementAutomationPeer, ISelectionItemProvider
{
    /// <summary>
    /// Initializes a new instance of the TabItemAutomationPeer class.
    /// </summary>
    public TabItemAutomationPeer(TabItem owner) : base(owner)
    {
    }

    private TabItem ItemOwner => (TabItem)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.TabItem;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(TabItem);
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        var header = ItemOwner.Header;
        if (header is string text)
            return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.SelectionItem)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region ISelectionItemProvider

    /// <inheritdoc />
    public bool IsSelected => ItemOwner.IsSelected;

    /// <inheritdoc />
    public AutomationPeer SelectionContainer
    {
        get
        {
            var parent = ItemOwner.VisualParent;
            while (parent != null)
            {
                if (parent is TabControl tabControl)
                    return tabControl.GetAutomationPeer() ?? new TabControlAutomationPeer(tabControl);
                parent = (parent as FrameworkElement)?.VisualParent;
            }
            return null!;
        }
    }

    /// <inheritdoc />
    public void Select()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public void AddToSelection()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        throw new InvalidOperationException("Cannot deselect a TabItem without selecting another.");
    }

    #endregion
}

#endregion

#region Menu Controls

/// <summary>
/// Exposes Menu types to UI Automation.
/// </summary>
public sealed class MenuAutomationPeer : FrameworkElementAutomationPeer
{
    /// <summary>
    /// Initializes a new instance of the MenuAutomationPeer class.
    /// </summary>
    public MenuAutomationPeer(Menu owner) : base(owner)
    {
    }

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Menu;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(Menu);
    }
}

/// <summary>
/// Exposes MenuItem types to UI Automation.
/// </summary>
public sealed class MenuItemAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider, IInvokeProvider, IToggleProvider
{
    /// <summary>
    /// Initializes a new instance of the MenuItemAutomationPeer class.
    /// </summary>
    public MenuItemAutomationPeer(MenuItem owner) : base(owner)
    {
    }

    private MenuItem MenuItemOwner => (MenuItem)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.MenuItem;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(MenuItem);
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        var header = MenuItemOwner.Header;
        if (header is string text)
            return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.ExpandCollapse && MenuItemOwner.HasItems)
            return this;

        if (patternInterface == PatternInterface.Invoke && !MenuItemOwner.HasItems)
            return this;

        if (patternInterface == PatternInterface.Toggle && MenuItemOwner.IsCheckable)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region IExpandCollapseProvider

    /// <inheritdoc />
    public ExpandCollapseState ExpandCollapseState
    {
        get
        {
            if (!MenuItemOwner.HasItems)
                return ExpandCollapseState.LeafNode;
            return MenuItemOwner.IsSubmenuOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        }
    }

    /// <inheritdoc />
    public void Expand()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot expand a disabled MenuItem.");
        MenuItemOwner.IsSubmenuOpen = true;
    }

    /// <inheritdoc />
    public void Collapse()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot collapse a disabled MenuItem.");
        MenuItemOwner.IsSubmenuOpen = false;
    }

    #endregion

    #region IInvokeProvider

    /// <inheritdoc />
    public void Invoke()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot invoke a disabled MenuItem.");

        // Raise the Click event
        MenuItemOwner.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, MenuItemOwner));
        RaiseAutomationEvent(AutomationEvents.InvokePatternOnInvoked);
    }

    #endregion

    #region IToggleProvider

    /// <inheritdoc />
    public ToggleState ToggleState =>
        MenuItemOwner.IsChecked ? ToggleState.On : ToggleState.Off;

    /// <inheritdoc />
    public void Toggle()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot toggle a disabled MenuItem.");
        MenuItemOwner.IsChecked = !MenuItemOwner.IsChecked;
    }

    #endregion
}

#endregion

#region Scroll Controls

/// <summary>
/// Exposes ScrollViewer types to UI Automation.
/// </summary>
public sealed class ScrollViewerAutomationPeer : FrameworkElementAutomationPeer, IScrollProvider
{
    /// <summary>
    /// Initializes a new instance of the ScrollViewerAutomationPeer class.
    /// </summary>
    public ScrollViewerAutomationPeer(ScrollViewer owner) : base(owner)
    {
    }

    private ScrollViewer ScrollViewerOwner => (ScrollViewer)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Pane;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(ScrollViewer);
    }

    /// <inheritdoc />
    protected override bool IsControlElementCore()
    {
        return false;
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Scroll)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region IScrollProvider

    /// <inheritdoc />
    public double HorizontalScrollPercent
    {
        get
        {
            if (!HorizontallyScrollable)
                return -1;
            var extent = ScrollViewerOwner.ExtentWidth - ScrollViewerOwner.ViewportWidth;
            return extent > 0 ? (ScrollViewerOwner.HorizontalOffset / extent) * 100 : 0;
        }
    }

    /// <inheritdoc />
    public double VerticalScrollPercent
    {
        get
        {
            if (!VerticallyScrollable)
                return -1;
            var extent = ScrollViewerOwner.ExtentHeight - ScrollViewerOwner.ViewportHeight;
            return extent > 0 ? (ScrollViewerOwner.VerticalOffset / extent) * 100 : 0;
        }
    }

    /// <inheritdoc />
    public double HorizontalViewSize
    {
        get
        {
            if (ScrollViewerOwner.ExtentWidth == 0)
                return 100;
            return (ScrollViewerOwner.ViewportWidth / ScrollViewerOwner.ExtentWidth) * 100;
        }
    }

    /// <inheritdoc />
    public double VerticalViewSize
    {
        get
        {
            if (ScrollViewerOwner.ExtentHeight == 0)
                return 100;
            return (ScrollViewerOwner.ViewportHeight / ScrollViewerOwner.ExtentHeight) * 100;
        }
    }

    /// <inheritdoc />
    public bool HorizontallyScrollable =>
        ScrollViewerOwner.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled &&
        ScrollViewerOwner.ExtentWidth > ScrollViewerOwner.ViewportWidth;

    /// <inheritdoc />
    public bool VerticallyScrollable =>
        ScrollViewerOwner.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled &&
        ScrollViewerOwner.ExtentHeight > ScrollViewerOwner.ViewportHeight;

    /// <inheritdoc />
    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        if (horizontalAmount != ScrollAmount.NoAmount && HorizontallyScrollable)
        {
            double offset = ScrollViewerOwner.HorizontalOffset;
            switch (horizontalAmount)
            {
                case ScrollAmount.LargeDecrement:
                    offset -= ScrollViewerOwner.ViewportWidth;
                    break;
                case ScrollAmount.SmallDecrement:
                    offset -= 16;
                    break;
                case ScrollAmount.LargeIncrement:
                    offset += ScrollViewerOwner.ViewportWidth;
                    break;
                case ScrollAmount.SmallIncrement:
                    offset += 16;
                    break;
            }
            ScrollViewerOwner.ScrollToHorizontalOffset(offset);
        }

        if (verticalAmount != ScrollAmount.NoAmount && VerticallyScrollable)
        {
            double offset = ScrollViewerOwner.VerticalOffset;
            switch (verticalAmount)
            {
                case ScrollAmount.LargeDecrement:
                    offset -= ScrollViewerOwner.ViewportHeight;
                    break;
                case ScrollAmount.SmallDecrement:
                    offset -= 16;
                    break;
                case ScrollAmount.LargeIncrement:
                    offset += ScrollViewerOwner.ViewportHeight;
                    break;
                case ScrollAmount.SmallIncrement:
                    offset += 16;
                    break;
            }
            ScrollViewerOwner.ScrollToVerticalOffset(offset);
        }
    }

    /// <inheritdoc />
    public void SetScrollPercent(double horizontalPercent, double verticalPercent)
    {
        if (horizontalPercent >= 0 && horizontalPercent <= 100 && HorizontallyScrollable)
        {
            var extent = ScrollViewerOwner.ExtentWidth - ScrollViewerOwner.ViewportWidth;
            ScrollViewerOwner.ScrollToHorizontalOffset(extent * horizontalPercent / 100);
        }

        if (verticalPercent >= 0 && verticalPercent <= 100 && VerticallyScrollable)
        {
            var extent = ScrollViewerOwner.ExtentHeight - ScrollViewerOwner.ViewportHeight;
            ScrollViewerOwner.ScrollToVerticalOffset(extent * verticalPercent / 100);
        }
    }

    #endregion
}

#endregion

#region Window

/// <summary>
/// Exposes Window types to UI Automation.
/// </summary>
public sealed class WindowAutomationPeer : FrameworkElementAutomationPeer
{
    /// <summary>
    /// Initializes a new instance of the WindowAutomationPeer class.
    /// </summary>
    public WindowAutomationPeer(Window owner) : base(owner)
    {
    }

    private Window WindowOwner => (Window)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Window;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(Window);
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        return WindowOwner.Title ?? base.GetNameCore();
    }
}

#endregion

#region DataGrid

/// <summary>
/// Exposes DataGrid types to UI Automation.
/// </summary>
public sealed class DataGridAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    /// <summary>
    /// Initializes a new instance of the DataGridAutomationPeer class.
    /// </summary>
    public DataGridAutomationPeer(DataGrid owner) : base(owner)
    {
    }

    private DataGrid DataGridOwner => (DataGrid)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.DataGrid;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(DataGrid);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Selection)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region ISelectionProvider

    /// <inheritdoc />
    public AutomationPeer[] GetSelection()
    {
        return Array.Empty<AutomationPeer>();
    }

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    public bool CanSelectMultiple =>
        DataGridOwner.SelectionMode == DataGridSelectionMode.Extended;

    #endregion
}

#endregion
