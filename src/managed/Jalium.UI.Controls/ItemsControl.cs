using System.Collections;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that can be used to present a collection of items.
/// </summary>
public class ItemsControl : Control
{
    private ItemsPresenter? _itemsPresenter;
    private Panel? _fallbackItemsHost;
    private ItemContainerGenerator? _itemContainerGenerator;

    #region Dependency Properties

    /// <summary>
    /// Identifies the ItemsSource dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(ItemsControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>
    /// Identifies the ItemTemplate dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(ItemsControl),
            new PropertyMetadata(null, OnItemTemplateChanged));

    /// <summary>
    /// Identifies the ItemTemplateSelector dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemTemplateSelectorProperty =
        DependencyProperty.Register(nameof(ItemTemplateSelector), typeof(DataTemplateSelector), typeof(ItemsControl),
            new PropertyMetadata(null, OnItemTemplateSelectorChanged));

    /// <summary>
    /// Identifies the ItemsPanel dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemsPanelProperty =
        DependencyProperty.Register(nameof(ItemsPanel), typeof(ItemsPanelTemplate), typeof(ItemsControl),
            new PropertyMetadata(null, OnItemsPanelChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a collection used to generate the content of the ItemsControl.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the DataTemplate used to display each item.
    /// </summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets the DataTemplateSelector used to display each item.
    /// </summary>
    public DataTemplateSelector? ItemTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ItemTemplateSelectorProperty);
        set => SetValue(ItemTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets the template that defines the panel that controls the layout of items.
    /// </summary>
    public ItemsPanelTemplate? ItemsPanel
    {
        get => (ItemsPanelTemplate?)GetValue(ItemsPanelProperty);
        set => SetValue(ItemsPanelProperty, value);
    }

    /// <summary>
    /// Gets the collection used to generate the content of the control.
    /// </summary>
    public ItemCollection Items { get; }

    /// <summary>
    /// Gets the panel that hosts the items.
    /// </summary>
    protected Panel? ItemsHost => _itemsPresenter?.ItemsPanel ?? _fallbackItemsHost;

    /// <summary>
    /// Gets the ItemContainerGenerator associated with this control.
    /// </summary>
    public ItemContainerGenerator ItemContainerGenerator
    {
        get
        {
            _itemContainerGenerator ??= new ItemContainerGenerator(this);
            return _itemContainerGenerator;
        }
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemsControl"/> class.
    /// </summary>
    public ItemsControl()
    {
        Items = new ItemCollection(this);
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    #endregion

    #region Template Support

    /// <summary>
    /// Called when the template is applied.
    /// </summary>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // ItemsPresenter will call SetItemsPresenter when it attaches
    }

    /// <summary>
    /// Sets the ItemsPresenter for this control (called by ItemsPresenter).
    /// </summary>
    internal void SetItemsPresenter(ItemsPresenter presenter)
    {
        _itemsPresenter = presenter;
        RefreshItems();
    }

    #endregion

    #region Item Generation

    /// <summary>
    /// Creates the panel that will host the items.
    /// </summary>
    protected virtual Panel CreateItemsPanel()
    {
        return new StackPanel { Orientation = Orientation.Vertical };
    }

    /// <summary>
    /// Creates a container for the specified item.
    /// </summary>
    protected virtual FrameworkElement GetContainerForItem(object item)
    {
        return new ContentPresenter();
    }

    /// <summary>
    /// Determines if the specified item is (or is eligible to be) its own container.
    /// </summary>
    protected virtual bool IsItemItsOwnContainer(object item)
    {
        return item is UIElement;
    }

    /// <summary>
    /// Prepares the specified element to display the specified item.
    /// </summary>
    protected virtual void PrepareContainerForItem(FrameworkElement element, object item)
    {
        // Determine the template to use
        var template = ItemTemplate;
        if (template == null && ItemTemplateSelector != null)
        {
            template = ItemTemplateSelector.SelectTemplate(item, this);
        }

        if (element is ContentPresenter presenter)
        {
            presenter.Content = item;
            presenter.ContentTemplate = template;
        }
        else if (element is ContentControl contentControl)
        {
            contentControl.Content = item;
            contentControl.ContentTemplate = template;
        }
    }

    /// <summary>
    /// Refreshes all items in the control.
    /// </summary>
    protected virtual void RefreshItems()
    {
        // Get the panel (either from ItemsPresenter or fallback)
        var panel = ItemsHost;

        // If no panel from template, create fallback
        if (panel == null && !HasTemplate)
        {
            _fallbackItemsHost = CreateItemsPanel();
            AddVisualChild(_fallbackItemsHost);
            panel = _fallbackItemsHost;
        }

        if (panel == null) return;

        // Clear existing items from current panel
        panel.Children.Clear();

        // Also clear the old fallback panel if we switched to a template panel
        // This ensures items previously parented to the fallback are properly disconnected
        if (_fallbackItemsHost != null && _fallbackItemsHost != panel)
        {
            _fallbackItemsHost.Children.Clear();
            RemoveVisualChild(_fallbackItemsHost);
            _fallbackItemsHost = null;
        }

        // Add items from ItemsSource or Items collection
        var source = ItemsSource ?? Items;
        if (source != null)
        {
            foreach (var item in source)
            {
                AddItemToPanel(item);
            }
        }

        InvalidateMeasure();
    }

    private void AddItemToPanel(object item)
    {
        if (ItemsHost == null) return;

        FrameworkElement container;
        if (IsItemItsOwnContainer(item))
        {
            container = (FrameworkElement)item;
        }
        else
        {
            container = GetContainerForItem(item);
            PrepareContainerForItem(container, item);
        }

        ItemsHost.Children.Add(container);
    }

    #endregion

    #region Layout

    /// <summary>
    /// Gets whether this control has a template (either explicitly set or from style).
    /// </summary>
    private bool HasTemplate => Template != null;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // If we have a template, let Control handle it (this will also apply the template)
        if (HasTemplate)
        {
            return base.MeasureOverride(availableSize);
        }

        // Fallback: direct items host rendering (no template)
        if (_fallbackItemsHost == null)
        {
            RefreshItems();
        }

        if (_fallbackItemsHost != null)
        {
            _fallbackItemsHost.Measure(availableSize);
            return _fallbackItemsHost.DesiredSize;
        }

        return Size.Empty;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // If we have a template, let Control handle it
        if (HasTemplate)
        {
            return base.ArrangeOverride(finalSize);
        }

        // Fallback: direct items host rendering
        if (_fallbackItemsHost != null)
        {
            _fallbackItemsHost.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            // If we have a template, let Control handle it
            if (HasTemplate)
            {
                return base.VisualChildrenCount;
            }

            // Fallback: direct items host rendering
            return _fallbackItemsHost != null ? 1 : 0;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        // If we have a template, let Control handle it
        if (HasTemplate)
        {
            return base.GetVisualChild(index);
        }

        // Fallback: direct items host rendering
        if (index == 0 && _fallbackItemsHost != null)
            return _fallbackItemsHost;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        // ItemsControl itself doesn't render anything, the items panel does
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl itemsControl)
        {
            // Unsubscribe from old collection
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= itemsControl.OnSourceCollectionChanged;
            }

            // Subscribe to new collection
            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += itemsControl.OnSourceCollectionChanged;
            }

            itemsControl.RefreshItems();
        }
    }

    private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl itemsControl)
        {
            itemsControl.RefreshItems();
        }
    }

    private static void OnItemTemplateSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl ic)
        {
            ic.RefreshItems();
        }
    }

    private static void OnItemsPanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl itemsControl)
        {
            itemsControl._fallbackItemsHost = null;
            itemsControl._itemsPresenter = null;
            itemsControl.RefreshItems();
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Notify the generator of the change
        _itemContainerGenerator?.OnCollectionChanged(e);

        // Handle incremental updates for simple cases
        var panel = ItemsHost;
        if (panel == null)
        {
            RefreshItems();
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems != null:
                int insertIndex = e.NewStartingIndex;
                foreach (var item in e.NewItems)
                {
                    if (item != null)
                    {
                        InsertItemToPanel(item, insertIndex);
                        insertIndex++;
                    }
                }
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                for (int i = e.OldItems.Count - 1; i >= 0; i--)
                {
                    int removeIndex = e.OldStartingIndex + i;
                    if (removeIndex >= 0 && removeIndex < panel.Children.Count)
                    {
                        panel.Children.RemoveAt(removeIndex);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Replace when e.NewItems != null:
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    int replaceIndex = e.NewStartingIndex + i;
                    if (replaceIndex >= 0 && replaceIndex < panel.Children.Count && e.NewItems[i] != null)
                    {
                        panel.Children.RemoveAt(replaceIndex);
                        InsertItemToPanel(e.NewItems[i]!, replaceIndex);
                    }
                }
                break;

            default:
                // Reset, Move, or complex changes: full refresh
                RefreshItems();
                break;
        }

        InvalidateMeasure();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ItemsSource == null)
        {
            _itemContainerGenerator?.OnCollectionChanged(e);

            var panel = ItemsHost;
            if (panel == null)
            {
                RefreshItems();
                return;
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems != null:
                    int insertIndex = e.NewStartingIndex;
                    foreach (var item in e.NewItems)
                    {
                        if (item != null)
                        {
                            InsertItemToPanel(item, insertIndex);
                            insertIndex++;
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                    for (int i = e.OldItems.Count - 1; i >= 0; i--)
                    {
                        int removeIndex = e.OldStartingIndex + i;
                        if (removeIndex >= 0 && removeIndex < panel.Children.Count)
                        {
                            panel.Children.RemoveAt(removeIndex);
                        }
                    }
                    break;

                default:
                    RefreshItems();
                    break;
            }

            InvalidateMeasure();
        }
    }

    #endregion

    #region Internal Methods for ItemContainerGenerator

    /// <summary>
    /// Public wrapper for IsItemItsOwnContainer used by ItemContainerGenerator.
    /// </summary>
    internal bool IsItemItsOwnContainerPublic(object item) => IsItemItsOwnContainer(item);

    /// <summary>
    /// Public wrapper for GetContainerForItem used by ItemContainerGenerator.
    /// </summary>
    internal FrameworkElement GetContainerForItemPublic(object item) => GetContainerForItem(item);

    /// <summary>
    /// Internal wrapper for PrepareContainerForItem used by ItemContainerGenerator.
    /// </summary>
    internal void PrepareContainerForItemInternal(FrameworkElement element, object item)
    {
        PrepareContainerForItem(element, item);
    }

    private void InsertItemToPanel(object item, int index)
    {
        if (ItemsHost == null) return;

        FrameworkElement container;
        if (IsItemItsOwnContainer(item))
        {
            container = (FrameworkElement)item;
        }
        else
        {
            container = GetContainerForItem(item);
            PrepareContainerForItem(container, item);
        }

        if (index >= 0 && index <= ItemsHost.Children.Count)
        {
            ItemsHost.Children.Insert(index, container);
        }
        else
        {
            ItemsHost.Children.Add(container);
        }
    }

    #endregion
}

/// <summary>
/// Represents a collection of items in an ItemsControl.
/// </summary>
public sealed class ItemCollection : IList<object>, INotifyCollectionChanged
{
    private readonly List<object> _items = new();
    private readonly ItemsControl _owner;

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    internal ItemCollection(ItemsControl owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Gets or sets the item at the specified index.
    /// </summary>
    public object this[int index]
    {
        get => _items[index];
        set
        {
            var oldItem = _items[index];
            _items[index] = value;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace, value, oldItem, index));
        }
    }

    /// <summary>
    /// Gets the number of items in the collection.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Adds an item to the collection.
    /// </summary>
    public void Add(object item)
    {
        _items.Add(item);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, _items.Count - 1));
    }

    /// <summary>
    /// Clears all items from the collection.
    /// </summary>
    public void Clear()
    {
        _items.Clear();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Determines whether the collection contains a specific item.
    /// </summary>
    public bool Contains(object item) => _items.Contains(item);

    /// <summary>
    /// Copies the collection to an array.
    /// </summary>
    public void CopyTo(object[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();

    /// <summary>
    /// Determines the index of a specific item in the collection.
    /// </summary>
    public int IndexOf(object item) => _items.IndexOf(item);

    /// <summary>
    /// Inserts an item at the specified index.
    /// </summary>
    public void Insert(int index, object item)
    {
        _items.Insert(index, item);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, index));
    }

    /// <summary>
    /// Removes the first occurrence of a specific item from the collection.
    /// </summary>
    public bool Remove(object item)
    {
        var index = _items.IndexOf(item);
        if (index >= 0)
        {
            _items.RemoveAt(index);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove, item, index));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, item, index));
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
