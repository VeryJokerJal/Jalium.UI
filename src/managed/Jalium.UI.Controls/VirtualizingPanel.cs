using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a framework for Panel elements that virtualize their child data collection.
/// This is an abstract class.
/// </summary>
public abstract class VirtualizingPanel : Panel
{
    /// <summary>
    /// Gets the ItemContainerGenerator associated with this panel.
    /// </summary>
    public ItemContainerGenerator? ItemContainerGenerator { get; internal set; }

    #region Attached Properties

    /// <summary>
    /// Identifies the IsVirtualizing attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsVirtualizingProperty =
        DependencyProperty.RegisterAttached("IsVirtualizing", typeof(bool), typeof(VirtualizingPanel),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the VirtualizationMode attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty VirtualizationModeProperty =
        DependencyProperty.RegisterAttached("VirtualizationMode", typeof(VirtualizationMode), typeof(VirtualizingPanel),
            new PropertyMetadata(VirtualizationMode.Recycling));

    /// <summary>
    /// Identifies the CacheLength attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CacheLengthProperty =
        DependencyProperty.RegisterAttached("CacheLength", typeof(VirtualizationCacheLength), typeof(VirtualizingPanel),
            new PropertyMetadata(new VirtualizationCacheLength(1.0)));

    /// <summary>
    /// Identifies the CacheLengthUnit attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CacheLengthUnitProperty =
        DependencyProperty.RegisterAttached("CacheLengthUnit", typeof(VirtualizationCacheLengthUnit), typeof(VirtualizingPanel),
            new PropertyMetadata(VirtualizationCacheLengthUnit.Page));

    /// <summary>
    /// Identifies the ScrollUnit attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ScrollUnitProperty =
        DependencyProperty.RegisterAttached("ScrollUnit", typeof(ScrollUnit), typeof(VirtualizingPanel),
            new PropertyMetadata(ScrollUnit.Pixel));

    #endregion

    #region Attached Property Accessors

    /// <summary>Gets whether virtualization is enabled for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static bool GetIsVirtualizing(DependencyObject element) =>
        (bool)(element.GetValue(IsVirtualizingProperty) ?? true);

    /// <summary>Sets whether virtualization is enabled for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetIsVirtualizing(DependencyObject element, bool value) =>
        element.SetValue(IsVirtualizingProperty, value);

    /// <summary>Gets the virtualization mode for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static VirtualizationMode GetVirtualizationMode(DependencyObject element) =>
        (VirtualizationMode)(element.GetValue(VirtualizationModeProperty) ?? VirtualizationMode.Recycling);

    /// <summary>Sets the virtualization mode for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetVirtualizationMode(DependencyObject element, VirtualizationMode value) =>
        element.SetValue(VirtualizationModeProperty, value);

    /// <summary>Gets the cache length for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static VirtualizationCacheLength GetCacheLength(DependencyObject element) =>
        (VirtualizationCacheLength)(element.GetValue(CacheLengthProperty) ?? new VirtualizationCacheLength(1.0));

    /// <summary>Sets the cache length for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetCacheLength(DependencyObject element, VirtualizationCacheLength value) =>
        element.SetValue(CacheLengthProperty, value);

    /// <summary>Gets the cache length unit for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static VirtualizationCacheLengthUnit GetCacheLengthUnit(DependencyObject element) =>
        (VirtualizationCacheLengthUnit)(element.GetValue(CacheLengthUnitProperty) ?? VirtualizationCacheLengthUnit.Page);

    /// <summary>Sets the cache length unit for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetCacheLengthUnit(DependencyObject element, VirtualizationCacheLengthUnit value) =>
        element.SetValue(CacheLengthUnitProperty, value);

    /// <summary>Gets the scroll unit for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static ScrollUnit GetScrollUnit(DependencyObject element) =>
        (ScrollUnit)(element.GetValue(ScrollUnitProperty) ?? ScrollUnit.Pixel);

    /// <summary>Sets the scroll unit for the given element.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetScrollUnit(DependencyObject element, ScrollUnit value) =>
        element.SetValue(ScrollUnitProperty, value);

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

    internal void NotifyItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        OnItemsChanged(sender, args);
    }

    /// <summary>
    /// Called when the collection of child elements is cleared by the base Panel class.
    /// </summary>
    internal virtual void OnClearChildren()
    {
    }

    #endregion

    /// <summary>
    /// Brings the item at the specified index into view.
    /// </summary>
    public void BringIndexIntoView(int index)
    {
        BringIndexIntoViewOverride(index);
    }

    /// <summary>
    /// When overridden in a derived class, generates items and brings the specified index into view.
    /// </summary>
    protected virtual void BringIndexIntoViewOverride(int index)
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
