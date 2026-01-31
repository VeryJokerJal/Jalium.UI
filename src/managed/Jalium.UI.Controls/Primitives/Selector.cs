namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that allows the user to select items from among its child elements.
/// </summary>
public abstract class Selector : ItemsControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectedIndex dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(Selector),
            new PropertyMetadata(-1, OnSelectedIndexChanged));

    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(Selector),
            new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>
    /// Identifies the SelectedValue dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(nameof(SelectedValue), typeof(object), typeof(Selector),
            new PropertyMetadata(null, OnSelectedValueChanged));

    /// <summary>
    /// Identifies the SelectedValuePath dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedValuePathProperty =
        DependencyProperty.Register(nameof(SelectedValuePath), typeof(string), typeof(Selector),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the IsSynchronizedWithCurrentItem dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSynchronizedWithCurrentItemProperty =
        DependencyProperty.Register(nameof(IsSynchronizedWithCurrentItem), typeof(bool?), typeof(Selector),
            new PropertyMetadata(null));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the SelectionChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble,
            typeof(SelectionChangedEventHandler), typeof(Selector));

    /// <summary>
    /// Occurs when the selection changes.
    /// </summary>
    public event SelectionChangedEventHandler SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the index of the currently selected item.
    /// </summary>
    public int SelectedIndex
    {
        get => (int)(GetValue(SelectedIndexProperty) ?? -1);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected item.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the value of the currently selected item.
    /// </summary>
    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the path used to get the SelectedValue from the SelectedItem.
    /// </summary>
    public string SelectedValuePath
    {
        get => (string)(GetValue(SelectedValuePathProperty) ?? string.Empty);
        set => SetValue(SelectedValuePathProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Selector should synchronize with the current item in the Items property.
    /// </summary>
    public bool? IsSynchronizedWithCurrentItem
    {
        get => (bool?)GetValue(IsSynchronizedWithCurrentItemProperty);
        set => SetValue(IsSynchronizedWithCurrentItemProperty, value);
    }

    #endregion

    #region Fields

    private bool _isUpdatingSelection;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Selector"/> class.
    /// </summary>
    protected Selector()
    {
        Focusable = true;
    }

    #endregion

    #region Selection Methods

    /// <summary>
    /// Gets the number of items in the items source.
    /// </summary>
    protected int GetItemCount()
    {
        var source = ItemsSource ?? Items;
        if (source == null) return 0;

        if (source is System.Collections.ICollection collection)
            return collection.Count;

        var count = 0;
        foreach (var _ in source)
            count++;
        return count;
    }

    /// <summary>
    /// Gets the item at the specified index.
    /// </summary>
    protected object? GetItemAt(int index)
    {
        if (index < 0) return null;

        var source = ItemsSource ?? Items;
        if (source == null) return null;

        if (source is System.Collections.IList list && index < list.Count)
            return list[index];

        var currentIndex = 0;
        foreach (var item in source)
        {
            if (currentIndex == index)
                return item;
            currentIndex++;
        }

        return null;
    }

    /// <summary>
    /// Gets the index of the specified item.
    /// </summary>
    protected int GetIndexOf(object? item)
    {
        if (item == null) return -1;

        var source = ItemsSource ?? Items;
        if (source == null) return -1;

        if (source is System.Collections.IList list)
            return list.IndexOf(item);

        var index = 0;
        foreach (var obj in source)
        {
            if (Equals(obj, item))
                return index;
            index++;
        }

        return -1;
    }

    /// <summary>
    /// Called when the selection changes.
    /// </summary>
    protected virtual void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the SelectionChanged event.
    /// </summary>
    protected void RaiseSelectionChanged(object? removedItem, object? addedItem)
    {
        var removedItems = removedItem != null ? new[] { removedItem } : Array.Empty<object>();
        var addedItems = addedItem != null ? new[] { addedItem } : Array.Empty<object>();
        var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems, addedItems);
        OnSelectionChanged(args);
    }

    /// <summary>
    /// Updates the selection state of item containers.
    /// </summary>
    protected virtual void UpdateContainerSelection()
    {
        // Override in derived classes to update container selection state
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Selector selector && !selector._isUpdatingSelection)
        {
            selector._isUpdatingSelection = true;
            try
            {
                var newIndex = (int)(e.NewValue ?? -1);
                var newItem = selector.GetItemAt(newIndex);

                if (selector.SelectedItem != newItem)
                {
                    var oldItem = selector.SelectedItem;
                    selector.SelectedItem = newItem;
                    selector.UpdateContainerSelection();
                    selector.RaiseSelectionChanged(oldItem, newItem);
                }
            }
            finally
            {
                selector._isUpdatingSelection = false;
            }
        }
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Selector selector && !selector._isUpdatingSelection)
        {
            selector._isUpdatingSelection = true;
            try
            {
                var newItem = e.NewValue;
                var newIndex = selector.GetIndexOf(newItem);

                if (selector.SelectedIndex != newIndex)
                {
                    selector.SelectedIndex = newIndex;
                }

                selector.UpdateContainerSelection();
                selector.RaiseSelectionChanged(e.OldValue, e.NewValue);
            }
            finally
            {
                selector._isUpdatingSelection = false;
            }
        }
    }

    private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // SelectedValue is typically derived from SelectedItem using SelectedValuePath
        // For now, we'll leave this as a placeholder for future implementation
    }

    #endregion
}

/// <summary>
/// Specifies the selection behavior for a selector control.
/// </summary>
public enum SelectionMode
{
    /// <summary>
    /// The user can select only one item at a time.
    /// </summary>
    Single,

    /// <summary>
    /// The user can select multiple items without holding down a modifier key.
    /// </summary>
    Multiple,

    /// <summary>
    /// The user can select multiple contiguous items while holding down the SHIFT key,
    /// or non-contiguous items by holding down the CTRL key.
    /// </summary>
    Extended
}

/// <summary>
/// Provides data for the SelectionChanged event.
/// </summary>
public class SelectionChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the items that were unselected.
    /// </summary>
    public IList<object> RemovedItems { get; }

    /// <summary>
    /// Gets the items that were selected.
    /// </summary>
    public IList<object> AddedItems { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectionChangedEventArgs"/> class.
    /// </summary>
    public SelectionChangedEventArgs(RoutedEvent routedEvent, IList<object> removedItems, IList<object> addedItems)
        : base(routedEvent)
    {
        RemovedItems = removedItems;
        AddedItems = addedItems;
    }
}

/// <summary>
/// Represents the method that handles the SelectionChanged event.
/// </summary>
public delegate void SelectionChangedEventHandler(object sender, SelectionChangedEventArgs e);
