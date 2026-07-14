using System.Collections.Generic;
using Jalium.UI.Automation;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using IExpandCollapseProvider = Jalium.UI.Automation.Provider.IExpandCollapseProvider;
using IInvokeProvider = Jalium.UI.Automation.Provider.IInvokeProvider;
using IRangeValueProvider = Jalium.UI.Automation.Provider.IRangeValueProvider;
using IScrollItemProvider = Jalium.UI.Automation.Provider.IScrollItemProvider;
using IScrollProvider = Jalium.UI.Automation.Provider.IScrollProvider;
using ISelectionItemProvider = Jalium.UI.Automation.Provider.ISelectionItemProvider;
using ISelectionProvider = Jalium.UI.Automation.Provider.ISelectionProvider;
using IToggleProvider = Jalium.UI.Automation.Provider.IToggleProvider;
using IValueProvider = Jalium.UI.Automation.Provider.IValueProvider;

namespace Jalium.UI.Automation.Peers;

#region Selector Controls

/// <summary>
/// Exposes ListBox types to UI Automation.
/// </summary>
public class ListBoxAutomationPeer : SelectorAutomationPeer
{
    /// <summary>
    /// Initializes a new instance of the ListBoxAutomationPeer class.
    /// </summary>
    public ListBoxAutomationPeer(ListBox owner) : base(owner)
    {
    }

    private ListBox ListBoxOwner => (ListBox)Owner;

    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new ListBoxItemAutomationPeer(item, this);

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
    public override AutomationPeer[] GetSelection()
    {
        // Simplified - return empty if no way to get container
        return Array.Empty<AutomationPeer>();
    }

    /// <inheritdoc />
    public override bool IsSelectionRequired => false;

    /// <inheritdoc />
    public override bool CanSelectMultiple => ListBoxOwner.SelectionMode != SelectionMode.Single;

    #endregion
}

/// <summary>
/// Exposes ListBoxItem types to UI Automation.
/// </summary>
public class ListBoxItemAutomationPeer : SelectorItemAutomationPeer, Jalium.UI.Automation.Provider.IScrollItemProvider
{
    /// <summary>
    /// Initializes a new instance of the ListBoxItemAutomationPeer class.
    /// </summary>
    public ListBoxItemAutomationPeer(ListBoxItem owner) : base(owner)
    {
    }

    public ListBoxItemAutomationPeer(object owner, SelectorAutomationPeer selectorAutomationPeer)
        : base(owner, selectorAutomationPeer)
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
    public override bool IsSelected => ItemOwner.IsSelected;

    /// <inheritdoc />
    public override AutomationPeer SelectionContainer
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
    public override void Select()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public override void AddToSelection()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public override void RemoveFromSelection()
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
public partial class ComboBoxAutomationPeer : SelectorAutomationPeer,
    Jalium.UI.Automation.Provider.IExpandCollapseProvider,
    Jalium.UI.Automation.Provider.IValueProvider
{
    /// <summary>
    /// Initializes a new instance of the ComboBoxAutomationPeer class.
    /// </summary>
    public ComboBoxAutomationPeer(ComboBox owner) : base(owner)
    {
    }

    private ComboBox ComboBoxOwner => (ComboBox)Owner;

    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new SelectorItemAutomationPeer(item, this);

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

        if (patternInterface == PatternInterface.Value)
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
    public override AutomationPeer[] GetSelection()
    {
        return Array.Empty<AutomationPeer>();
    }

    /// <inheritdoc />
    public override bool IsSelectionRequired => false;

    /// <inheritdoc />
    public override bool CanSelectMultiple => false;

    string Jalium.UI.Automation.Provider.IValueProvider.Value =>
        ComboBoxOwner.IsEditable
            ? ComboBoxOwner.Text ?? string.Empty
            : ComboBoxOwner.SelectedItem?.ToString() ?? ComboBoxOwner.Text ?? string.Empty;

    bool Jalium.UI.Automation.Provider.IValueProvider.IsReadOnly => !ComboBoxOwner.IsEditable;

    void Jalium.UI.Automation.Provider.IValueProvider.SetValue(string val)
    {
        if (!ComboBoxOwner.IsEditable)
            throw new InvalidOperationException("The ComboBox is not editable.");
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled ComboBox.");
        ArgumentNullException.ThrowIfNull(val);
        ComboBoxOwner.Text = val;
    }

    #endregion
}

#endregion

#region TreeView

/// <summary>
/// Exposes TreeView types to UI Automation.
/// </summary>
public class TreeViewAutomationPeer : ItemsControlAutomationPeer,
    Jalium.UI.Automation.Provider.ISelectionProvider
{
    /// <summary>
    /// Initializes a new instance of the TreeViewAutomationPeer class.
    /// </summary>
    public TreeViewAutomationPeer(TreeView owner) : base(owner)
    {
    }

    private TreeView TreeViewOwner => (TreeView)Owner;

    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new TreeViewDataItemAutomationPeer(item, this);

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
    Jalium.UI.Automation.Provider.IRawElementProviderSimple[] Jalium.UI.Automation.Provider.ISelectionProvider.GetSelection()
    {
        object? selectedItem = TreeViewOwner.SelectedItem;
        return selectedItem is null
            ? []
            : [ProviderFromPeer(FindOrCreateItemAutomationPeer(selectedItem))];
    }

    bool Jalium.UI.Automation.Provider.ISelectionProvider.IsSelectionRequired => false;
    bool Jalium.UI.Automation.Provider.ISelectionProvider.CanSelectMultiple => false;

    #endregion
}

/// <summary>
/// Exposes TreeViewItem types to UI Automation.
/// </summary>
public class TreeViewItemAutomationPeer : ItemsControlAutomationPeer,
    Jalium.UI.Automation.Provider.IExpandCollapseProvider,
    Jalium.UI.Automation.Provider.ISelectionItemProvider,
    Jalium.UI.Automation.Provider.IScrollItemProvider
{
    /// <summary>
    /// Initializes a new instance of the TreeViewItemAutomationPeer class.
    /// </summary>
    public TreeViewItemAutomationPeer(TreeViewItem owner) : base(owner)
    {
    }

    private TreeViewItem ItemOwner => (TreeViewItem)Owner;

    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new TreeViewDataItemAutomationPeer(item, this);

    protected internal override ItemAutomationPeer FindOrCreateItemAutomationPeer(object item) => base.FindOrCreateItemAutomationPeer(item);

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
    private AutomationPeer? SelectionContainerPeer
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
            return null;
        }
    }

    bool Jalium.UI.Automation.Provider.ISelectionItemProvider.IsSelected => ItemOwner.IsSelected;
    Jalium.UI.Automation.Provider.IRawElementProviderSimple? Jalium.UI.Automation.Provider.ISelectionItemProvider.SelectionContainer =>
        SelectionContainerPeer is AutomationPeer peer ? ProviderFromPeer(peer) : null;
    void Jalium.UI.Automation.Provider.ISelectionItemProvider.Select() => ItemOwner.IsSelected = true;
    void Jalium.UI.Automation.Provider.ISelectionItemProvider.AddToSelection() => ItemOwner.IsSelected = true;
    void Jalium.UI.Automation.Provider.ISelectionItemProvider.RemoveFromSelection() => ItemOwner.IsSelected = false;

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
public class SliderAutomationPeer : RangeBaseAutomationPeer
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

    protected override Point GetClickablePointCore() => new(double.NaN, double.NaN);

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.RangeValue)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region IRangeValueProvider

    /// <inheritdoc />
    public override double Value => SliderOwner.Value;

    /// <inheritdoc />
    public override double Minimum => SliderOwner.Minimum;

    /// <inheritdoc />
    public override double Maximum => SliderOwner.Maximum;

    /// <inheritdoc />
    public override double SmallChange => SliderOwner.SmallChange;

    /// <inheritdoc />
    public override double LargeChange => SliderOwner.LargeChange;

    /// <inheritdoc />
    public override bool IsReadOnly => !IsEnabled();

    /// <inheritdoc />
    public override void SetValue(double value)
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
public class ProgressBarAutomationPeer : RangeBaseAutomationPeer, Jalium.UI.Automation.Provider.IRangeValueProvider
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
    public override double Value => ProgressBarOwner.Value;

    /// <inheritdoc />
    public override double Minimum => ProgressBarOwner.Minimum;

    /// <inheritdoc />
    public override double Maximum => ProgressBarOwner.Maximum;

    /// <inheritdoc />
    public override double SmallChange => 0;

    /// <inheritdoc />
    public override double LargeChange => 0;

    /// <inheritdoc />
    public override bool IsReadOnly => true;

    /// <inheritdoc />
    public override void SetValue(double value)
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
public class TabControlAutomationPeer : SelectorAutomationPeer, Jalium.UI.Automation.Provider.ISelectionProvider
{
    /// <summary>
    /// Initializes a new instance of the TabControlAutomationPeer class.
    /// </summary>
    public TabControlAutomationPeer(TabControl owner) : base(owner)
    {
    }

    private TabControl TabControlOwner => (TabControl)Owner;

    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new TabItemAutomationPeer(item, this);

    protected override Point GetClickablePointCore() => new(double.NaN, double.NaN);

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
    public override AutomationPeer[] GetSelection()
    {
        return Array.Empty<AutomationPeer>();
    }

    /// <inheritdoc />
    public override bool IsSelectionRequired => true;

    /// <inheritdoc />
    public override bool CanSelectMultiple => false;

    #endregion
}

/// <summary>
/// Exposes TabItem types to UI Automation.
/// </summary>
public class TabItemAutomationPeer : SelectorItemAutomationPeer, Jalium.UI.Automation.Provider.ISelectionItemProvider
{
    /// <summary>
    /// Initializes a new instance of the TabItemAutomationPeer class.
    /// </summary>
    public TabItemAutomationPeer(TabItem owner) : base(owner)
    {
    }

    public TabItemAutomationPeer(object owner, TabControlAutomationPeer tabControlAutomationPeer)
        : base(owner, tabControlAutomationPeer)
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
    public override bool IsSelected => ItemOwner.IsSelected;

    /// <inheritdoc />
    public override AutomationPeer SelectionContainer
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
    public override void Select()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public override void AddToSelection()
    {
        ItemOwner.IsSelected = true;
    }

    /// <inheritdoc />
    public override void RemoveFromSelection()
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

    protected override string GetAccessKeyCore()
    {
        if (MenuItemOwner.Header is AccessText accessText && accessText.AccessKey != '\0')
            return accessText.AccessKey.ToString();

        if (MenuItemOwner.Header is not string header)
            return base.GetAccessKeyCore();

        for (int index = 0; index < header.Length - 1; index++)
        {
            if (header[index] != '_')
                continue;
            if (header[index + 1] == '_')
            {
                index++;
                continue;
            }

            return header[index + 1].ToString();
        }

        return base.GetAccessKeyCore();
    }

    protected override int GetPositionInSetCore()
    {
        ItemsControl? parent = GetParentItemsControl();
        if (parent is null)
            return base.GetPositionInSetCore();

        int index = parent.ItemContainerGenerator.IndexFromContainer(MenuItemOwner);
        if (index < 0)
            index = parent.Items.IndexOf(MenuItemOwner);
        return index < 0 ? base.GetPositionInSetCore() : index + 1;
    }

    protected override int GetSizeOfSetCore() =>
        GetParentItemsControl()?.Items.Count ?? base.GetSizeOfSetCore();

    private ItemsControl? GetParentItemsControl()
    {
        ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(MenuItemOwner);
        if (parent is not null && !ReferenceEquals(parent, MenuItemOwner))
            return parent;

        Visual? current = MenuItemOwner.VisualParent;
        while (current is not null)
        {
            if (current is ItemsControl itemsControl && !ReferenceEquals(itemsControl, MenuItemOwner))
                return itemsControl;
            current = current.VisualParent;
        }

        return null;
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
public class WindowAutomationPeer : FrameworkElementAutomationPeer
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

    protected override int GetPositionInSetCore()
    {
        if (Owner.VisualParent is Panel panel)
            return panel.Children.IndexOf(Owner) + 1;
        return base.GetPositionInSetCore();
    }

    protected override int GetSizeOfSetCore() => Owner.VisualParent is Panel panel
        ? panel.Children.Count
        : base.GetSizeOfSetCore();

    /// <inheritdoc />
    protected override bool IsKeyboardFocusableCore()
    {
        return WindowOwner.IsEnabled && WindowOwner.Visibility == Visibility.Visible;
    }

    /// <inheritdoc />
    protected override bool HasKeyboardFocusCore()
    {
        return WindowOwner.IsActive || base.HasKeyboardFocusCore();
    }
}

#endregion

#region DataGrid

/// <summary>
/// Exposes DataGrid types to UI Automation.
/// </summary>
public sealed partial class DataGridAutomationPeer : ItemsControlAutomationPeer,
    Jalium.UI.Automation.Provider.IGridProvider,
    Jalium.UI.Automation.Provider.ISelectionProvider,
    Jalium.UI.Automation.Provider.ITableProvider
{
    private readonly Dictionary<object, Dictionary<DataGridColumn, DataGridCellItemAutomationPeer>> _cellPeers =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DataGridColumn, DataGridHeaderItemAutomationPeer> _columnHeaderPeers = [];
    private readonly Dictionary<object, DataGridHeaderItemAutomationPeer> _rowHeaderPeers =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Initializes a new instance of the DataGridAutomationPeer class.
    /// </summary>
    public DataGridAutomationPeer(DataGrid owner) : base(owner)
    {
    }

    private DataGrid DataGridOwner => (DataGrid)Owner;

    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new DataGridItemAutomationPeer(item, this);

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
        if (patternInterface is PatternInterface.Selection or PatternInterface.Grid or PatternInterface.Table)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region ISelectionProvider

    /// <inheritdoc />
    public AutomationPeer[] GetSelection()
    {
        return DataGridOwner.SelectedItems
            .Cast<object>()
            .Select(FindOrCreateItemAutomationPeer)
            .Cast<AutomationPeer>()
            .ToArray();
    }

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    public bool CanSelectMultiple =>
        DataGridOwner.SelectionMode == DataGridSelectionMode.Extended;

    Jalium.UI.Automation.Provider.IRawElementProviderSimple[] Jalium.UI.Automation.Provider.ISelectionProvider.GetSelection() =>
        GetSelection().Select(ProviderFromPeer).ToArray();
    bool Jalium.UI.Automation.Provider.ISelectionProvider.IsSelectionRequired => IsSelectionRequired;
    bool Jalium.UI.Automation.Provider.ISelectionProvider.CanSelectMultiple => CanSelectMultiple;

    int Jalium.UI.Automation.Provider.IGridProvider.RowCount => DataGridOwner.Items.Count;
    int Jalium.UI.Automation.Provider.IGridProvider.ColumnCount => DataGridOwner.Columns.Count;
    Jalium.UI.Automation.Provider.IRawElementProviderSimple? Jalium.UI.Automation.Provider.IGridProvider.GetItem(int row, int column)
    {
        if ((uint)row >= (uint)DataGridOwner.Items.Count || (uint)column >= (uint)DataGridOwner.Columns.Count)
            throw new ArgumentOutOfRangeException(
                row < 0 || row >= DataGridOwner.Items.Count ? nameof(row) : nameof(column));

        object item = DataGridOwner.Items[row];
        DataGridColumn dataGridColumn = DataGridOwner.Columns[column];
        return ProviderFromPeer(GetCellPeer(item, dataGridColumn));
    }

    RowOrColumnMajor Jalium.UI.Automation.Provider.ITableProvider.RowOrColumnMajor => RowOrColumnMajor.RowMajor;
    Jalium.UI.Automation.Provider.IRawElementProviderSimple[] Jalium.UI.Automation.Provider.ITableProvider.GetColumnHeaders()
    {
        if (!DataGridOwner.HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column))
            return [];

        return DataGridOwner.Columns
            .Select(column => ProviderFromPeer(GetColumnHeaderPeer(column)))
            .ToArray();
    }

    Jalium.UI.Automation.Provider.IRawElementProviderSimple[] Jalium.UI.Automation.Provider.ITableProvider.GetRowHeaders()
    {
        if (!DataGridOwner.HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row))
            return [];

        return DataGridOwner.Items
            .Cast<object>()
            .Select(item => ProviderFromPeer(GetRowHeaderPeer(item)))
            .ToArray();
    }

    private DataGridCellItemAutomationPeer GetCellPeer(object item, DataGridColumn column)
    {
        if (!_cellPeers.TryGetValue(item, out Dictionary<DataGridColumn, DataGridCellItemAutomationPeer>? rowPeers))
        {
            rowPeers = [];
            _cellPeers.Add(item, rowPeers);
        }

        if (!rowPeers.TryGetValue(column, out DataGridCellItemAutomationPeer? peer))
        {
            peer = new DataGridCellItemAutomationPeer(item, column, this);
            rowPeers.Add(column, peer);
        }

        return peer;
    }

    private DataGridHeaderItemAutomationPeer GetColumnHeaderPeer(DataGridColumn column)
    {
        if (!_columnHeaderPeers.TryGetValue(column, out DataGridHeaderItemAutomationPeer? peer))
        {
            peer = new DataGridHeaderItemAutomationPeer(column.Header?.ToString() ?? string.Empty, this);
            _columnHeaderPeers.Add(column, peer);
        }

        return peer;
    }

    private DataGridHeaderItemAutomationPeer GetRowHeaderPeer(object item)
    {
        if (!_rowHeaderPeers.TryGetValue(item, out DataGridHeaderItemAutomationPeer? peer))
        {
            peer = new DataGridHeaderItemAutomationPeer(item.ToString() ?? string.Empty, this);
            _rowHeaderPeers.Add(item, peer);
        }

        return peer;
    }

    private sealed class DataGridCellItemAutomationPeer : AutomationPeer
    {
        private readonly object _item;
        private readonly DataGridColumn _column;
        private readonly DataGridAutomationPeer _parent;

        internal DataGridCellItemAutomationPeer(object item, DataGridColumn column, DataGridAutomationPeer parent)
        {
            _item = item;
            _column = column;
            _parent = parent;
        }

        public override object? GetPattern(PatternInterface patternInterface) => null;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;
        protected override string GetClassNameCore() => nameof(DataGridCell);
        protected override string GetNameCore() =>
            GetRealizedCell()?.Content?.ToString() ?? _column.GetCellValueInternal(_item)?.ToString() ?? string.Empty;
        protected override string GetAutomationIdCore() =>
            $"Cell_{_parent.DataGridOwner.Items.IndexOf(_item)}_{_parent.DataGridOwner.Columns.IndexOf(_column)}";
        protected override string GetHelpTextCore() => string.Empty;
        protected override string GetAcceleratorKeyCore() => string.Empty;
        protected override string GetAccessKeyCore() => string.Empty;
        protected override string GetItemStatusCore() => string.Empty;
        protected override string GetItemTypeCore() => "Cell";
        protected override string GetLocalizedControlTypeCore() => "cell";
        protected override Rect GetBoundingRectangleCore()
        {
            DataGridCell? cell = GetRealizedCell();
            return cell is null
                ? Rect.Empty
                : cell.MapLocalRectToScreen(new Rect(0, 0, cell.RenderSize.Width, cell.RenderSize.Height));
        }
        protected override Point GetClickablePointCore()
        {
            Rect bounds = GetBoundingRectangleCore();
            return bounds.IsEmpty
                ? new Point(double.NaN, double.NaN)
                : new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        }
        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
        protected override bool IsEnabledCore() => _parent.IsEnabled();
        protected override bool IsKeyboardFocusableCore() => GetRealizedCell()?.Focusable == true;
        protected override bool HasKeyboardFocusCore() => GetRealizedCell()?.IsKeyboardFocused == true;
        protected override bool IsOffscreenCore() => GetRealizedCell() is not { Visibility: Visibility.Visible };
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsPasswordCore() => false;
        protected override bool IsRequiredForFormCore() => false;
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override AutomationPeer GetParentCore() => _parent;
        protected override List<AutomationPeer> GetChildrenCore() => [];
        protected override void SetFocusCore() => (GetRealizedCell() as UIElement ?? _parent.DataGridOwner).Focus();

        private DataGridCell? GetRealizedCell()
        {
            int columnIndex = _parent.DataGridOwner.Columns.IndexOf(_column);
            return _parent.DataGridOwner.ItemContainerGenerator.ContainerFromItem(_item) is DataGridRow row &&
                   row.CellsByColumn.TryGetValue(columnIndex, out DataGridCell? cell)
                ? cell
                : null;
        }
    }

    private sealed class DataGridHeaderItemAutomationPeer : AutomationPeer
    {
        private readonly string _name;
        private readonly DataGridAutomationPeer _parent;

        internal DataGridHeaderItemAutomationPeer(string name, DataGridAutomationPeer parent)
        {
            _name = name;
            _parent = parent;
        }

        public override object? GetPattern(PatternInterface patternInterface) => null;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;
        protected override string GetClassNameCore() => "DataGridHeader";
        protected override string GetNameCore() => _name;
        protected override string GetAutomationIdCore() => _name;
        protected override string GetHelpTextCore() => string.Empty;
        protected override string GetAcceleratorKeyCore() => string.Empty;
        protected override string GetAccessKeyCore() => string.Empty;
        protected override string GetItemStatusCore() => string.Empty;
        protected override string GetItemTypeCore() => "Header";
        protected override string GetLocalizedControlTypeCore() => "header";
        protected override Rect GetBoundingRectangleCore() => Rect.Empty;
        protected override Point GetClickablePointCore() => new(double.NaN, double.NaN);
        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
        protected override bool IsEnabledCore() => _parent.IsEnabled();
        protected override bool IsKeyboardFocusableCore() => false;
        protected override bool HasKeyboardFocusCore() => false;
        protected override bool IsOffscreenCore() => _parent.IsOffscreen();
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsPasswordCore() => false;
        protected override bool IsRequiredForFormCore() => false;
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override AutomationPeer GetParentCore() => _parent;
        protected override List<AutomationPeer> GetChildrenCore() => [];
        protected override void SetFocusCore() { }
    }

    #endregion
}

#endregion

#region RangeSlider

/// <summary>
/// Exposes RangeSlider types to UI Automation as a value-based control whose
/// <see cref="IValueProvider.Value"/> string encodes both range bounds.
/// </summary>
public sealed class RangeSliderAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
{
    /// <summary>
    /// Initializes a new instance of the RangeSliderAutomationPeer class.
    /// </summary>
    public RangeSliderAutomationPeer(RangeSlider owner) : base(owner)
    {
    }

    private RangeSlider RangeSliderOwner => (RangeSlider)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Slider;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(RangeSlider);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Value)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region IValueProvider

    /// <inheritdoc />
    public string Value => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"{RangeSliderOwner.RangeStart}..{RangeSliderOwner.RangeEnd}");

    /// <inheritdoc />
    public bool IsReadOnly => !IsEnabled();

    /// <inheritdoc />
    public void SetValue(string value)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled RangeSlider.");
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be a 'start..end' pair.", nameof(value));

        var parts = value.Split("..", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            throw new ArgumentException("Value must be a 'start..end' pair.", nameof(value));

        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var start) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var end))
        {
            throw new ArgumentException("Value parts must be numeric.", nameof(value));
        }

        if (start > end)
            (start, end) = (end, start);

        // Setting the upper bound first guarantees the start coercion has the new headroom available.
        RangeSliderOwner.RangeEnd = end;
        RangeSliderOwner.RangeStart = start;
    }

    #endregion
}

#endregion

#region TreeSelector

/// <summary>
/// Exposes TreeSelector types to UI Automation.
/// </summary>
public sealed class TreeSelectorAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    /// <summary>
    /// Initializes a new instance of the TreeSelectorAutomationPeer class.
    /// </summary>
    public TreeSelectorAutomationPeer(TreeSelector owner) : base(owner)
    {
    }

    private TreeSelector SelectorOwner => (TreeSelector)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Tree;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(TreeSelector);
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
    public AutomationPeer[] GetSelection() => Array.Empty<AutomationPeer>();

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    public bool CanSelectMultiple => SelectorOwner.SelectionMode != SelectionMode.Single;

    Jalium.UI.Automation.Provider.IRawElementProviderSimple[] ISelectionProvider.GetSelection() => [];

    #endregion
}

/// <summary>
/// Exposes TreeSelectorItem types to UI Automation.
/// </summary>
public sealed class TreeSelectorItemAutomationPeer : FrameworkElementAutomationPeer,
    IExpandCollapseProvider, ISelectionItemProvider, IToggleProvider, IScrollItemProvider
{
    /// <summary>
    /// Initializes a new instance of the TreeSelectorItemAutomationPeer class.
    /// </summary>
    public TreeSelectorItemAutomationPeer(TreeSelectorItem owner) : base(owner)
    {
    }

    private TreeSelectorItem ItemOwner => (TreeSelectorItem)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.TreeItem;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(TreeSelectorItem);
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        var header = ItemOwner.Header;
        if (header is string text) return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.ExpandCollapse && ItemOwner.HasItems)
            return this;

        if (patternInterface == PatternInterface.SelectionItem)
            return this;

        if (patternInterface == PatternInterface.Toggle &&
            (ItemOwner.ParentSelector?.ShowCheckBoxes ?? false))
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
            if (!ItemOwner.HasItems) return ExpandCollapseState.LeafNode;
            return ItemOwner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        }
    }

    /// <inheritdoc />
    public void Expand()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot expand a disabled TreeSelectorItem.");
        ItemOwner.IsExpanded = true;
    }

    /// <inheritdoc />
    public void Collapse()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot collapse a disabled TreeSelectorItem.");
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
            var selector = ItemOwner.ParentSelector;
            if (selector != null)
                return selector.GetAutomationPeer() ?? new TreeSelectorAutomationPeer(selector);
            return null!;
        }
    }

    Jalium.UI.Automation.Provider.IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer =>
        SelectionContainer is AutomationPeer peer ? ProviderFromPeer(peer) : null;

    /// <inheritdoc />
    public void Select()
    {
        ItemOwner.ParentSelector?.HandleItemActivated(ItemOwner, isCtrlPressed: false, isShiftPressed: false);
    }

    /// <inheritdoc />
    public void AddToSelection()
    {
        ItemOwner.ParentSelector?.HandleItemActivated(ItemOwner, isCtrlPressed: true, isShiftPressed: false);
    }

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        if (ItemOwner.IsSelected)
        {
            ItemOwner.ParentSelector?.HandleItemActivated(ItemOwner, isCtrlPressed: true, isShiftPressed: false);
        }
    }

    #endregion

    #region IToggleProvider

    /// <inheritdoc />
    public ToggleState ToggleState
    {
        get
        {
            return ItemOwner.IsChecked switch
            {
                true => ToggleState.On,
                false => ToggleState.Off,
                _ => ToggleState.Indeterminate
            };
        }
    }

    /// <inheritdoc />
    public void Toggle()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot toggle a disabled TreeSelectorItem.");
        ItemOwner.IsChecked = ItemOwner.IsChecked != true;
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
