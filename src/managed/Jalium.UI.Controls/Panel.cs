using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Base class for panel controls that host child elements.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Children")]
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

    #region IsItemsHost Property

    /// <summary>
    /// Identifies the IsItemsHost dependency property.
    /// </summary>
    public static readonly DependencyProperty IsItemsHostProperty =
        DependencyProperty.Register(nameof(IsItemsHost), typeof(bool), typeof(Panel),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.NotDataBindable,
                OnIsItemsHostChanged));

    /// <summary>
    /// Gets or sets a value indicating whether this panel is the items host of an
    /// <see cref="ItemsControl"/>. Set by the framework when the panel is installed as the items host;
    /// enables <see cref="ItemsControl.GetItemsOwner"/> and
    /// <see cref="ItemsControl.ItemsControlFromItemContainer"/>.
    /// </summary>
    [System.ComponentModel.Bindable(false)]
    [System.ComponentModel.Category("Behavior")]
    public bool IsItemsHost
    {
        get => (bool)(GetValue(IsItemsHostProperty) ?? false);
        set => SetValue(IsItemsHostProperty, value);
    }

    private static void OnIsItemsHostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Panel panel)
        {
            panel.OnIsItemsHostChanged(e.OldValue is true, e.NewValue is true);
        }
    }

    /// <summary>
    /// Called when the panel starts or stops acting as the items host for an
    /// <see cref="ItemsControl"/>.
    /// </summary>
    protected virtual void OnIsItemsHostChanged(bool oldIsItemsHost, bool newIsItemsHost)
    {
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
        if (!_zOrderDirty && (_zOrderMap == null || _zOrderMap.Length == Children.Count))
            return;

        try
        {
            var children = Children;
            var count = children.Count;

            // Child order is already the stable Z-order whenever ZIndex values are
            // nondecreasing. This is by far the most common case (all values are zero),
            // so avoid allocating sorting buffers for every panel in the visual tree.
            var previousZIndex = int.MinValue;
            var requiresSort = false;
            for (int i = 0; i < count; i++)
            {
                var zIndex = GetZIndex(children[i]);
                if (zIndex < previousZIndex)
                {
                    requiresSort = true;
                    break;
                }

                previousZIndex = zIndex;
            }

            if (!requiresSort)
            {
                _zOrderMap = null;
                _zOrderDirty = false;
                return;
            }

            if (_zOrderMap == null || _zOrderMap.Length != count)
                _zOrderMap = new int[count];

            var map = _zOrderMap;

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
    protected override Visual? GetVisualChild(int index)
    {
        var children = Children;
        var count = children.Count;

        if (count == 0 || index < 0 || index >= count)
            return null;

        EnsureZOrderMap();

        var map = _zOrderMap;
        if (map == null)
            return (uint)index < (uint)children.Count ? children[index] : null;

        if ((uint)index >= (uint)map.Length)
            return null;

        var mapped = map[index];
        if ((uint)mapped >= (uint)children.Count)
            return null;

        return children[mapped];
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => Children.Count;

    #endregion

    /// <summary>
    /// Gets the collection of child elements.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Content)]
    public UIElementCollection Children => InternalChildren;

    private UIElementCollection? _children;

    /// <summary>
    /// Allows a visual-only host (for example a popup items panel) to retain an element's
    /// existing logical owner while temporarily presenting it in this panel.
    /// </summary>
    internal bool PreserveExistingLogicalParents { get; set; }

    /// <summary>
    /// Gets the collection used internally to store this panel's child elements.
    /// </summary>
    protected internal UIElementCollection InternalChildren =>
        _children ??= CreateUIElementCollection(this);

    /// <summary>
    /// Creates the collection used to store this panel's visual children.
    /// </summary>
    protected virtual UIElementCollection CreateUIElementCollection(FrameworkElement logicalParent) =>
        new(this, logicalParent);

    /// <summary>
    /// Gets whether this panel exposes a meaningful logical scrolling orientation.
    /// </summary>
    protected internal virtual bool HasLogicalOrientation => false;

    /// <summary>
    /// Gets the logical scrolling orientation used by this panel.
    /// </summary>
    protected internal virtual Orientation LogicalOrientation => Orientation.Vertical;

    /// <summary>
    /// Gets whether this panel exposes a meaningful logical scrolling orientation.
    /// </summary>
    public bool HasLogicalOrientationPublic => HasLogicalOrientation;

    /// <summary>
    /// Gets the logical scrolling orientation used by this panel.
    /// </summary>
    public Orientation LogicalOrientationPublic => LogicalOrientation;

    /// <summary>
    /// Returns whether the child collection should be serialized as panel content.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool ShouldSerializeChildren() =>
        !IsItemsHost && _children is { Count: > 0 };

    /// <summary>
    /// Initializes a new instance of the <see cref="Panel"/> class.
    /// </summary>
    protected Panel()
    {
    }

    /// <inheritdoc />
    protected internal override System.Collections.IEnumerator LogicalChildren =>
        IsItemsHost
            ? Enumerable.Empty<object>().GetEnumerator()
            : base.LogicalChildren;

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Background == null)
        {
            return;
        }
        var dc = drawingContext;

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
public class UIElementCollection : System.Collections.IList
{
    private readonly List<UIElement> _items = new();
    private readonly UIElement _parent;
    private readonly Panel? _panelParent;
    private readonly FrameworkElement? _logicalParent;
    private int _batchUpdateCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="UIElementCollection"/> class.
    /// </summary>
    public UIElementCollection(Panel parent)
        : this(parent, parent)
    {
    }

    /// <summary>
    /// Initializes a collection with separate visual and logical parents.
    /// </summary>
    public UIElementCollection(UIElement visualParent, FrameworkElement? logicalParent)
    {
        ArgumentNullException.ThrowIfNull(visualParent);
        _parent = visualParent;
        _panelParent = visualParent as Panel;
        _logicalParent = logicalParent;
    }

    /// <summary>
    /// Gets the number of elements in the collection.
    /// </summary>
    public virtual int Count => _items.Count;

    /// <summary>Gets or sets the number of elements the backing store can hold without growing.</summary>
    public virtual int Capacity
    {
        get => _items.Capacity;
        set => _items.Capacity = value;
    }

    /// <summary>Gets whether access to the collection is synchronized.</summary>
    public virtual bool IsSynchronized => ((System.Collections.ICollection)_items).IsSynchronized;

    /// <summary>Gets the synchronization object exposed by the collection contract.</summary>
    public virtual object SyncRoot => ((System.Collections.ICollection)_items).SyncRoot;

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
            _panelParent?.EndVisualChildBatch();
            _parent.InvalidateMeasure();
        }
    }

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    public virtual UIElement this[int index]
    {
        get => _items[index];
        set
        {
            var oldItem = _items[index];
            if (oldItem != value)
            {
                RemoveVisualChild(oldItem);
                ClearLogicalParent(oldItem);
                PrepareIncomingChild(value);
                _items[index] = value;
                SetLogicalParent(value);
                if (IsBatchUpdating)
                    AddVisualChild(value, batch: true);
                else
                    AddVisualChild(value, batch: false);
                if (!IsBatchUpdating) _parent.InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Adds an element to the collection.
    /// </summary>
    public virtual int Add(UIElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!PrepareIncomingChild(item)) return _items.IndexOf(item); // already our child — idempotent no-op
        _items.Add(item);
        SetLogicalParent(item);

        if (IsBatchUpdating)
        {
            AddVisualChild(item, batch: true);
        }
        else
        {
            AddVisualChild(item, batch: false);
            _parent.InvalidateMeasure();
        }

        return _items.Count - 1;
    }

    /// <summary>
    /// Ensures <paramref name="item"/> is safe to add as a child of the owning
    /// panel. Handles two concurrency hazards that have hit the realization
    /// pipelines in practice:
    ///
    /// 1. <em>Idempotent add</em> — if the element is already a visual child of
    ///    this panel, we simply ensure the backing <c>_items</c> list contains it
    ///    and tell the caller to skip. Guards against double-population paths
    ///    (VSP realize + synchronous RefreshItems in the same layout pass).
    ///
    /// 2. <em>Automatic reparent</em> — if the element is parented to a
    ///    different panel, detach it first. This mirrors WPF's logical-tree
    ///    reparent semantics and keeps transient container handoffs between
    ///    virtualizing panels from throwing "Visual already has a parent".
    ///    The logical half matters just as much as the visual one: a container
    ///    handed off between two items hosts (e.g. a retired fallback panel and
    ///    its replacement created from the control template) still carries the
    ///    old host as its logical parent, and the subsequent SetLogicalParent
    ///    would throw "The logical child already has a parent". Migrate the
    ///    logical link here so the handoff is complete before re-parenting.
    ///
    /// Returns <c>true</c> when the caller should proceed to add; <c>false</c>
    /// when the collection already contains the element.
    /// </summary>
    private bool PrepareIncomingChild(UIElement item)
    {
        var currentParent = item.VisualParent;
        if (ReferenceEquals(currentParent, _parent))
        {
            // Already our child. Resynchronise _items if it somehow lost the
            // entry and tell the caller this call is a no-op.
            if (!_items.Contains(item)) _items.Add(item);
            return false;
        }
        if (currentParent != null)
        {
            item.DetachFromVisualParent();
        }

        // Automatic reparent, logical half: release the element from a stale logical
        // parent so SetLogicalParent can claim it for this collection's parent. Only a
        // DIFFERENT previous owner is released — when the element already belongs to
        // this collection's logical parent, AddLogicalChild is an idempotent no-op and
        // Loaded state must not be cycled. Two hosts deliberately keep the child's
        // original logical parent and must not trigger the migration: panels that opt
        // into PreserveExistingLogicalParents (MenuPopupScrollHost — releasing here
        // would both break the menu's logical tree and defeat the SetLogicalParent
        // guard), and collections constructed without a logical parent
        // (ToolBarOverflowPanel — overflow items logically stay with the ToolBar, and
        // SetLogicalParent is a no-op for them anyway).
        if (_logicalParent != null &&
            _panelParent?.PreserveExistingLogicalParents != true &&
            item is FrameworkElement element &&
            element.LogicalParentInternal is { } oldLogicalParent &&
            !ReferenceEquals(oldLogicalParent, _logicalParent))
        {
            oldLogicalParent.RemoveLogicalChild(item);
        }

        return true;
    }

    /// <summary>
    /// Clears all elements from the collection.
    /// </summary>
    public virtual void Clear()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            _items.RemoveAt(i);
            RemoveVisualChild(item);
            ClearLogicalParent(item);
        }
        // WPF parity: once the children have been detached en masse, let a virtualizing panel
        // drop its realized bookkeeping. External Children.Clear() callers (ItemsControl
        // RefreshItems, fallback teardown, app code) would otherwise leave _realizedContainers
        // pointing at detached zombie containers. Idempotent by contract: the panels' own
        // ClearRealizedContainers also funnels through here after clearing their maps.
        (_panelParent as VirtualizingPanel)?.OnClearChildrenInternal();
        if (!IsBatchUpdating) _parent.InvalidateMeasure();
    }

    /// <summary>
    /// Determines whether the collection contains a specific element.
    /// </summary>
    public virtual bool Contains(UIElement item) => _items.Contains(item);

    /// <summary>
    /// Copies the elements to an array.
    /// </summary>
    public virtual void CopyTo(UIElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>Copies the elements to a one-dimensional array.</summary>
    public virtual void CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_items).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public virtual System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();

    int System.Collections.IList.Add(object? value)
    {
        UIElement element = Cast(value);
        Add(element);
        return IndexOf(element);
    }

    bool System.Collections.IList.Contains(object? value) =>
        value is UIElement element && Contains(element);

    int System.Collections.IList.IndexOf(object? value) =>
        value is UIElement element ? IndexOf(element) : -1;

    void System.Collections.IList.Insert(int index, object? value) =>
        Insert(index, Cast(value));

    bool System.Collections.IList.IsFixedSize => false;

    bool System.Collections.IList.IsReadOnly => false;

    void System.Collections.IList.Remove(object? value)
    {
        if (value is UIElement element)
        {
            Remove(element);
        }
    }

    object? System.Collections.IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    /// <summary>
    /// Adds multiple elements to the collection, invalidating measure only once.
    /// </summary>
    public void AddRange(IList<UIElement> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        int added = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!PrepareIncomingChild(item)) continue; // already a child — skip
            _items.Add(item);
            SetLogicalParent(item);
            AddVisualChild(item, batch: true);
            added++;
        }

        if (added > 0)
        {
            _panelParent?.EndVisualChildBatch();
            _parent.InvalidateMeasure();
        }
    }

    /// <summary>
    /// Returns the index of a specific element.
    /// </summary>
    public virtual int IndexOf(UIElement item) => _items.IndexOf(item);

    /// <summary>
    /// Inserts an element at the specified index.
    /// </summary>
    public virtual void Insert(int index, UIElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!PrepareIncomingChild(item)) return; // already our child — idempotent no-op
        _items.Insert(index, item);
        SetLogicalParent(item);

        if (IsBatchUpdating)
        {
            AddVisualChild(item, batch: true);
        }
        else
        {
            AddVisualChild(item, batch: false);
            _parent.InvalidateMeasure();
        }
    }

    /// <summary>
    /// Removes a specific element from the collection.
    /// </summary>
    public virtual void Remove(UIElement item)
    {
        RemoveCore(item);
    }

    private bool RemoveCore(UIElement item)
    {
        if (_items.Remove(item))
        {
            RemoveVisualChild(item);
            ClearLogicalParent(item);
            if (!IsBatchUpdating) _parent.InvalidateMeasure();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes the element at the specified index.
    /// </summary>
    public virtual void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        RemoveVisualChild(item);
        ClearLogicalParent(item);
        if (!IsBatchUpdating) _parent.InvalidateMeasure();
    }

    /// <summary>Removes up to <paramref name="count"/> elements starting at the given index.</summary>
    public virtual void RemoveRange(int index, int count)
    {
        if (index < 0 || index > _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        count = Math.Min(count, _items.Count - index);
        if (count == 0)
        {
            return;
        }

        UIElement[] removed = _items.GetRange(index, count).ToArray();
        _items.RemoveRange(index, count);
        foreach (UIElement element in removed)
        {
            RemoveVisualChild(element);
            ClearLogicalParent(element);
        }

        if (!IsBatchUpdating)
        {
            _parent.InvalidateMeasure();
        }
    }

    /// <summary>Associates an element with the logical parent of this collection.</summary>
    protected void SetLogicalParent(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_panelParent?.PreserveExistingLogicalParents == true &&
            element is FrameworkElement { Parent: not null })
        {
            return;
        }

        _logicalParent?.AddLogicalChild(element);
    }

    /// <summary>Clears the element's association with this collection's logical parent.</summary>
    protected void ClearLogicalParent(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _logicalParent?.RemoveLogicalChild(element);
    }

    private static UIElement Cast(object? value)
    {
        if (value == null)
        {
            throw new ArgumentException("UIElementCollection does not accept null elements.", nameof(value));
        }

        return value as UIElement
            ?? throw new ArgumentException("UIElementCollection accepts only UIElement values.", nameof(value));
    }

    private void AddVisualChild(UIElement child, bool batch)
    {
        if (_panelParent != null)
        {
            if (batch)
            {
                _panelParent.AddVisualChildBatch(child);
            }
            else
            {
                _panelParent.AddVisualChildInternal(child);
            }
            return;
        }

        _parent.InternalAddVisualChild(child);
    }

    private void RemoveVisualChild(UIElement child)
    {
        if (_panelParent != null)
        {
            _panelParent.RemoveVisualChildInternal(child);
        }
        else
        {
            _parent.InternalRemoveVisualChild(child);
        }
    }
}
