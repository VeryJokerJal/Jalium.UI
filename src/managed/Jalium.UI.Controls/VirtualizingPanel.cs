using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a framework for Panel elements that virtualize their child data collection.
/// This is an abstract class.
/// </summary>
public abstract class VirtualizingPanel : Panel
{
    private ItemContainerGenerator? _itemContainerGenerator;

    /// <summary>
    /// Gets the ItemContainerGenerator associated with this panel.
    /// </summary>
    public IItemContainerGenerator? ItemContainerGenerator => _itemContainerGenerator;

    /// <summary>
    /// Gets the framework generator used by built-in virtualizing panels.
    /// </summary>
    private protected ItemContainerGenerator? Generator => _itemContainerGenerator;

    /// <summary>
    /// Associates the panel with its owning items generator.
    /// </summary>
    internal void SetItemContainerGenerator(ItemContainerGenerator? generator)
    {
        _itemContainerGenerator = generator;
    }

    /// <summary>
    /// The owning <see cref="ItemsControl"/> for a templated items host. Stamped unconditionally by
    /// ItemsControl when the panel is its items host (regardless of pipeline / IsVirtualizing), so the
    /// virtualization attached properties set on the CONTROL (e.g. &lt;ListBox VirtualizingPanel.ScrollUnit=...&gt;)
    /// govern the panel. Null for a code-only panel used directly.
    /// </summary>
    internal ItemsControl? ItemsOwner { get; set; }

    /// <summary>
    /// Returns the element the virtualization attached properties should be read from: the owning
    /// ItemsControl when templated, otherwise the panel itself (code-only usage).
    /// </summary>
    private protected DependencyObject GetOwner() => (DependencyObject?)ItemsOwner ?? this;

    #region Attached Properties

    /// <summary>
    /// Identifies the IsVirtualizing attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsVirtualizingProperty =
        DependencyProperty.RegisterAttached("IsVirtualizing", typeof(bool), typeof(VirtualizingPanel),
            new PropertyMetadata(true, OnVirtualizationPropertyChanged));

    /// <summary>
    /// Identifies the VirtualizationMode attached property.
    /// </summary>
    /// <remarks>
    /// Deliberate Jalium deviation from WPF: the default is <see cref="VirtualizationMode.Recycling"/>
    /// (WPF defaults to <see cref="VirtualizationMode.Standard"/>). Recycling is the better default for
    /// the vast majority of lists. All WPF virtualization behaviors are implemented regardless of this
    /// default; see VirtualizationPipelineTests for the assertion that locks this choice.
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty VirtualizationModeProperty =
        DependencyProperty.RegisterAttached("VirtualizationMode", typeof(VirtualizationMode), typeof(VirtualizingPanel),
            new PropertyMetadata(VirtualizationMode.Recycling, OnVirtualizationPropertyChanged));

    /// <summary>
    /// Identifies the CacheLength attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CacheLengthProperty =
        DependencyProperty.RegisterAttached("CacheLength", typeof(VirtualizationCacheLength), typeof(VirtualizingPanel),
            new PropertyMetadata(new VirtualizationCacheLength(1.0), OnVirtualizationPropertyChanged), ValidateCacheLength);

    private static bool ValidateCacheLength(object? value)
    {
        if (value is not VirtualizationCacheLength cacheLength)
        {
            return false;
        }

        // Reject negative or NaN cache edges; the (1.0, 1.0) default passes.
        return cacheLength.CacheBeforeViewport >= 0
            && cacheLength.CacheAfterViewport >= 0
            && !double.IsNaN(cacheLength.CacheBeforeViewport)
            && !double.IsNaN(cacheLength.CacheAfterViewport);
    }

    /// <summary>
    /// Identifies the CacheLengthUnit attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CacheLengthUnitProperty =
        DependencyProperty.RegisterAttached("CacheLengthUnit", typeof(VirtualizationCacheLengthUnit), typeof(VirtualizingPanel),
            new PropertyMetadata(VirtualizationCacheLengthUnit.Page, OnVirtualizationPropertyChanged));

    /// <summary>
    /// Identifies the ScrollUnit attached property.
    /// </summary>
    /// <remarks>
    /// Deliberate Jalium deviation from WPF: the default is <see cref="ScrollUnit.Pixel"/>
    /// (WPF defaults to <see cref="ScrollUnit.Item"/>). Pixel scrolling yields smoother behavior for
    /// variable-height content. Item-unit scrolling is still fully supported; see
    /// VirtualizationPipelineTests for the assertion that locks this choice.
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ScrollUnitProperty =
        DependencyProperty.RegisterAttached("ScrollUnit", typeof(ScrollUnit), typeof(VirtualizingPanel),
            new PropertyMetadata(ScrollUnit.Pixel, OnVirtualizationPropertyChanged));

    /// <summary>
    /// Identifies the IsContainerVirtualizable attached property. When set to <c>false</c> on a
    /// container, a virtualizing panel keeps that container realized (never virtualizes it away).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsContainerVirtualizableProperty =
        DependencyProperty.RegisterAttached("IsContainerVirtualizable", typeof(bool), typeof(VirtualizingPanel),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsVirtualizingWhenGrouping attached property. Enables virtualization while the
    /// items owner is grouping. Coerced to <c>false</c> whenever IsVirtualizing is <c>false</c>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsVirtualizingWhenGroupingProperty =
        DependencyProperty.RegisterAttached("IsVirtualizingWhenGrouping", typeof(bool), typeof(VirtualizingPanel),
            new PropertyMetadata(false, OnVirtualizationPropertyChanged, CoerceIsVirtualizingWhenGrouping));

    private static object? CoerceIsVirtualizingWhenGrouping(DependencyObject d, object? baseValue)
    {
        // Grouping virtualization is only meaningful when the element is virtualizing at all.
        return GetIsVirtualizing(d) && (bool)(baseValue ?? false);
    }

    /// <summary>
    /// Re-measures the affected items host when a virtualization attached property changes at runtime,
    /// so toggling e.g. VirtualizingPanel.ScrollUnit on a live ItemsControl takes effect. When set on
    /// the owning ItemsControl, invalidates its items host (and the ItemsPresenter parent); when set on
    /// a code-only panel, invalidates the panel directly.
    /// </summary>
    private static void OnVirtualizationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl itemsControl)
        {
            var host = itemsControl.ItemsHostInternal;
            if (host != null)
            {
                host.InvalidateMeasure();
                if (host.VisualParent is ItemsPresenter presenter)
                {
                    presenter.InvalidateMeasure();
                }
            }

            itemsControl.InvalidateMeasure();
        }
        else if (d is VirtualizingPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    #endregion

    #region Attached Property Accessors

    /// <summary>Gets whether virtualization is enabled for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static bool GetIsVirtualizing(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsVirtualizingProperty) ?? true);
    }

    /// <summary>Sets whether virtualization is enabled for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetIsVirtualizing(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsVirtualizingProperty, value);
    }

    /// <summary>Gets the virtualization mode for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static VirtualizationMode GetVirtualizationMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (VirtualizationMode)(element.GetValue(VirtualizationModeProperty) ?? VirtualizationMode.Recycling);
    }

    /// <summary>Sets the virtualization mode for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetVirtualizationMode(DependencyObject element, VirtualizationMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(VirtualizationModeProperty, value);
    }

    /// <summary>Gets the cache length for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static VirtualizationCacheLength GetCacheLength(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (VirtualizationCacheLength)(element.GetValue(CacheLengthProperty) ?? new VirtualizationCacheLength(1.0));
    }

    /// <summary>Sets the cache length for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetCacheLength(DependencyObject element, VirtualizationCacheLength value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CacheLengthProperty, value);
    }

    /// <summary>Gets the cache length unit for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static VirtualizationCacheLengthUnit GetCacheLengthUnit(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (VirtualizationCacheLengthUnit)(element.GetValue(CacheLengthUnitProperty) ?? VirtualizationCacheLengthUnit.Page);
    }

    /// <summary>Sets the cache length unit for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetCacheLengthUnit(DependencyObject element, VirtualizationCacheLengthUnit value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CacheLengthUnitProperty, value);
    }

    /// <summary>Gets the scroll unit for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static ScrollUnit GetScrollUnit(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ScrollUnit)(element.GetValue(ScrollUnitProperty) ?? ScrollUnit.Pixel);
    }

    /// <summary>Sets the scroll unit for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetScrollUnit(DependencyObject element, ScrollUnit value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ScrollUnitProperty, value);
    }

    /// <summary>Gets whether the given container may be virtualized away.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static bool GetIsContainerVirtualizable(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsContainerVirtualizableProperty) ?? true);
    }

    /// <summary>Sets whether the given container may be virtualized away.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetIsContainerVirtualizable(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsContainerVirtualizableProperty, value);
    }

    /// <summary>Gets whether the element virtualizes while grouping.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static bool GetIsVirtualizingWhenGrouping(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsVirtualizingWhenGroupingProperty) ?? false);
    }

    /// <summary>Sets whether the element virtualizes while grouping.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetIsVirtualizingWhenGrouping(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsVirtualizingWhenGroupingProperty, value);
    }

    #endregion

    #region Protected Methods for Child Management

    /// <summary>
    /// Adds the specified UIElement to the Children collection of a VirtualizingPanel element.
    /// </summary>
    protected void AddInternalChild(UIElement child)
    {
        Children.Add(child);
    }

    /// <summary>
    /// Adds the specified UIElement to the Children collection at the specified index.
    /// </summary>
    protected void InsertInternalChild(int index, UIElement child)
    {
        Children.Insert(index, child);
    }

    /// <summary>
    /// Removes child elements from the Children collection.
    /// </summary>
    protected void RemoveInternalChildRange(int index, int range)
    {
        for (int i = range - 1; i >= 0; i--)
        {
            if (index + i < Children.Count)
            {
                Children.RemoveAt(index + i);
            }
        }
    }

    #endregion

    #region Virtual Callbacks

    /// <summary>
    /// Called when the Items collection that is associated with the ItemsControl changes.
    /// </summary>
    protected virtual void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
    }

    internal bool NotifyItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        OnItemsChanged(sender, args);
        return ShouldItemsChangeAffectLayout(true, args);
    }

    /// <summary>
    /// Called when the collection of child elements is cleared by the base Panel class
    /// (<c>Panel.UIElementCollection.Clear</c>). Invoked AFTER the children have been detached.
    /// Implementations must be idempotent: the panel's own ClearRealizedContainers also funnels
    /// through Children.Clear().
    /// </summary>
    protected virtual void OnClearChildren()
    {
    }

    /// <summary>
    /// Framework bridge used by <see cref="UIElementCollection"/> after it clears the panel's
    /// visual children.
    /// </summary>
    internal void OnClearChildrenInternal()
    {
        OnClearChildren();
    }

    #endregion

    #region Hierarchical Seams

    /// <summary>
    /// Gets whether this panel both hierarchically scrolls and virtualizes its children.
    /// Base panels return <c>false</c>; the hierarchical <see cref="VirtualizingStackPanel"/> engine
    /// overrides <see cref="CanHierarchicallyScrollAndVirtualizeCore"/>.
    /// </summary>
    public bool CanHierarchicallyScrollAndVirtualize => CanHierarchicallyScrollAndVirtualizeCore;

    /// <summary>
    /// When overridden in a derived class, returns whether the panel hierarchically scrolls and
    /// virtualizes. The base implementation returns <c>false</c>.
    /// </summary>
    protected virtual bool CanHierarchicallyScrollAndVirtualizeCore => false;

    /// <summary>
    /// Returns the offset of the given realized child along the scrolling axis, in the panel's
    /// coordinate space. Used by hierarchical scrolling to locate a descendant within the extent.
    /// </summary>
    public double GetItemOffset(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return GetItemOffsetCore(child);
    }

    /// <summary>
    /// When overridden in a derived class, returns the scrolling-axis offset of the given child.
    /// The base implementation returns <c>0</c>.
    /// </summary>
    protected virtual double GetItemOffsetCore(UIElement child) => 0;

    /// <summary>
    /// Returns whether an items change should invalidate this panel's layout.
    /// </summary>
    public bool ShouldItemsChangeAffectLayout(bool areItemChangesLocal, ItemsChangedEventArgs args)
    {
        return ShouldItemsChangeAffectLayoutCore(areItemChangesLocal, args);
    }

    /// <summary>
    /// When overridden, determines whether an items change affects layout. The base
    /// implementation returns <c>true</c>.
    /// </summary>
    protected virtual bool ShouldItemsChangeAffectLayoutCore(
        bool areItemChangesLocal,
        ItemsChangedEventArgs args) => true;

    #endregion

    /// <summary>
    /// Brings the item at the specified index into view.
    /// </summary>
    public void BringIndexIntoViewPublic(int index)
    {
        BringIndexIntoView(index);
    }

    /// <summary>
    /// When overridden in a derived class, generates items and brings the specified index into view.
    /// </summary>
    protected internal virtual void BringIndexIntoView(int index)
    {
    }
}

/// <summary>
/// Specifies the virtualization mode of a panel.
/// </summary>
public enum VirtualizationMode
{
    /// <summary>Create and discard containers.</summary>
    Standard,
    /// <summary>Reuse containers.</summary>
    Recycling
}

/// <summary>
/// Specifies the type of unit for the CacheLength property.
/// </summary>
public enum VirtualizationCacheLengthUnit
{
    /// <summary>Cache length is in pixels.</summary>
    Pixel,
    /// <summary>Cache length is in items.</summary>
    Item,
    /// <summary>Cache length is in pages.</summary>
    Page
}

/// <summary>
/// Specifies the unit of scrolling.
/// </summary>
public enum ScrollUnit
{
    /// <summary>Scroll by pixel.</summary>
    Pixel,
    /// <summary>Scroll by item.</summary>
    Item
}
