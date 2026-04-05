using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Base class for panel controls that host child elements.
/// </summary>
[ContentProperty("Children")]
public abstract class Panel : FrameworkElement
{
    #region Background Property

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(Panel),
            new PropertyMetadata(null, OnBackgroundChanged));

    /// <summary>
    /// Gets or sets the brush used to fill the panel's bounds.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    private static void OnBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Panel panel)
        {
            panel.InvalidateVisual();
        }
    }

    #endregion

    #region ZIndex Attached Property

    /// <summary>
    /// Identifies the ZIndex attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ZIndexProperty =
        DependencyProperty.RegisterAttached("ZIndex", typeof(int), typeof(Panel),
            new PropertyMetadata(0, OnZIndexChanged));

    /// <summary>
    /// Gets the ZIndex value for a UIElement.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static int GetZIndex(UIElement element) =>
        (int)(element.GetValue(ZIndexProperty) ?? 0);

    /// <summary>
    /// Sets the ZIndex value for a UIElement.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetZIndex(UIElement element, int value) =>
        element.SetValue(ZIndexProperty, value);

    private static void OnZIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element && element.VisualParent is Panel panel)
        {
            panel.InvalidateZOrder();
        }
    }

    #endregion

    #region ZOrder Sorting

    private int[]? _zOrderMap;
    private bool _zOrderDirty = true;

    private void InvalidateZOrder()
    {
        _zOrderDirty = true;
        InvalidateVisual();
    }

    private int[]? _zIndexValues;

    private void EnsureZOrderMap()
    {
        if (!_zOrderDirty && _zOrderMap != null && _zOrderMap.Length == Children.Count)
            return;

        try
        {
            var children = Children;
            var count = children.Count;
            var map = new int[count];

            // Pre-fetch all ZIndex values to avoid repeated DP reads during sort
            if (_zIndexValues == null || _zIndexValues.Length < count)
                _zIndexValues = new int[count];

            var zValues = _zIndexValues;
            for (int i = 0; i < count; i++)
            {
                map[i] = i;
                zValues[i] = GetZIndex(children[i]);
            }

            Array.Sort(map, (a, b) =>
            {
                var za = zValues[a];
                var zb = zValues[b];
                return za != zb ? za.CompareTo(zb) : a.CompareTo(b);
            });

            _zOrderMap = map;
            _zOrderDirty = false;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Children collection was modified during map construction
            // (e.g. rapid input triggering layout changes on another dispatcher frame).
            // Leave _zOrderDirty unchanged so the map is rebuilt on the next access.
            // GetVisualChild has bounds checks that safely handle a stale map.
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        var children = Children;
        var count = children.Count;

        if (count == 0 || index < 0 || index >= count)
            return null;

        EnsureZOrderMap();

        var map = _zOrderMap!;
        if ((uint)index >= (uint)map.Length)
            return null;

        var mapped = map[index];
        if ((uint)mapped >= (uint)children.Count)
            return null;

        return children[mapped];
    }

    /// <inheritdoc />
    public override int VisualChildrenCount => Children.Count;

    #endregion

    /// <summary>
    /// Gets the collection of child elements.
    /// </summary>
    public UIElementCollection Children { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Panel"/> class.
    /// </summary>
    protected Panel()
    {
        Children = new UIElementCollection(this);
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is not DrawingContext dc || Background == null)
        {
            return;
        }

        var renderSize = RenderSize;
        if (renderSize.Width <= 0 || renderSize.Height <= 0)
        {
            return;
        }

        dc.DrawRectangle(Background, null, new Rect(renderSize));
    }

    /// <inheritdoc />
    protected override HitTestResult? HitTestCore(Point point)
    {
        var result = base.HitTestCore(point);
        if (result?.VisualHit == this && Background == null)
        {
            return null;
        }

        return result;
    }

    /// <summary>
    /// Adds a child to the visual tree. Called by UIElementCollection.
    /// </summary>
    internal void AddVisualChildInternal(UIElement child)
    {
        AddVisualChild(child);
        InvalidateZOrder();
    }

    /// <summary>
    /// Adds a child to the visual tree without triggering z-order invalidation.
    /// Used during batch updates to avoid repeated <see cref="InvalidateVisual"/> calls.
    /// </summary>
    internal void AddVisualChildBatch(UIElement child)
    {
        AddVisualChild(child);
    }

    /// <summary>
    /// Finalizes a batch visual-child addition by invalidating z-order once.
    /// </summary>
    internal void EndVisualChildBatch()
    {
        InvalidateZOrder();
    }

    /// <summary>
    /// Removes a child from the visual tree. Called by UIElementCollection.
    /// </summary>
    internal void RemoveVisualChildInternal(UIElement child)
    {
        RemoveVisualChild(child);
        InvalidateZOrder();
    }
}

/// <summary>
/// Collection of UI elements for a panel.
/// </summary>
public sealed class UIElementCollection : IList<UIElement>
{
    private readonly List<UIElement> _items = new();
    private readonly Panel _parent;
    private int _batchUpdateCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="UIElementCollection"/> class.
    /// </summary>
    public UIElementCollection(Panel parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Gets the number of elements in the collection.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets a value indicating whether a batch update is in progress.
    /// </summary>
    public bool IsBatchUpdating => _batchUpdateCount > 0;

    /// <summary>
    /// Begins a batch update. While active, <see cref="Add"/>, <see cref="Insert"/>,
    /// <see cref="Remove"/>, <see cref="RemoveAt"/>, and <see cref="Clear"/> will defer
    /// layout invalidation until <see cref="EndBatchUpdate"/> is called.
    /// Calls may be nested; only the outermost <see cref="EndBatchUpdate"/> triggers invalidation.
    /// </summary>
    public void BeginBatchUpdate()
    {
        _batchUpdateCount++;
    }

    /// <summary>
    /// Ends a batch update. When the outermost batch ends, a single
    /// <see cref="UIElement.InvalidateMeasure"/> is triggered on the parent panel.
    /// </summary>
    public void EndBatchUpdate()
    {
        if (_batchUpdateCount <= 0) return;

        _batchUpdateCount--;
        if (_batchUpdateCount == 0)
        {
            _parent.EndVisualChildBatch();
            _parent.InvalidateMeasure();
        }
    }

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    public UIElement this[int index]
    {
        get => _items[index];
        set
        {
            var oldItem = _items[index];
            if (oldItem != value)
            {
                _parent.RemoveVisualChildInternal(oldItem);
                _items[index] = value;
                if (IsBatchUpdating)
                    _parent.AddVisualChildBatch(value);
                else
                    _parent.AddVisualChildInternal(value);
                if (!IsBatchUpdating) _parent.InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Adds an element to the collection.
    /// </summary>
    public void Add(UIElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);

        if (IsBatchUpdating)
        {
            _parent.AddVisualChildBatch(item);
        }
        else
        {
            _parent.AddVisualChildInternal(item);
            _parent.InvalidateMeasure();
        }
    }

    /// <summary>
    /// Clears all elements from the collection.
    /// </summary>
    public void Clear()
    {
        foreach (var item in _items)
        {
            _parent.RemoveVisualChildInternal(item);
        }
        _items.Clear();
        if (!IsBatchUpdating) _parent.InvalidateMeasure();
    }

    /// <summary>
    /// Determines whether the collection contains a specific element.
    /// </summary>
    public bool Contains(UIElement item) => _items.Contains(item);

    /// <summary>
    /// Copies the elements to an array.
    /// </summary>
    public void CopyTo(UIElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<UIElement> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Adds multiple elements to the collection, invalidating measure only once.
    /// </summary>
    public void AddRange(IList<UIElement> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            _items.Add(item);
            _parent.AddVisualChildBatch(item);
        }

        _parent.EndVisualChildBatch();
        _parent.InvalidateMeasure();
    }

    /// <summary>
    /// Returns the index of a specific element.
    /// </summary>
    public int IndexOf(UIElement item) => _items.IndexOf(item);

    /// <summary>
    /// Inserts an element at the specified index.
    /// </summary>
    public void Insert(int index, UIElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);

        if (IsBatchUpdating)
        {
            _parent.AddVisualChildBatch(item);
        }
        else
        {
            _parent.AddVisualChildInternal(item);
            _parent.InvalidateMeasure();
        }
    }

    /// <summary>
    /// Removes a specific element from the collection.
    /// </summary>
    public bool Remove(UIElement item)
    {
        if (_items.Remove(item))
        {
            _parent.RemoveVisualChildInternal(item);
            if (!IsBatchUpdating) _parent.InvalidateMeasure();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes the element at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        _parent.RemoveVisualChildInternal(item);
        if (!IsBatchUpdating) _parent.InvalidateMeasure();
    }
}
