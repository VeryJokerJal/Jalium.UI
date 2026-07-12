using System.Collections;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that can be used to present a collection of items.
/// </summary>
public partial class ItemsControl : Control, Jalium.UI.Markup.IAddChild
{
    private ItemsPresenter? _itemsPresenter;
    private Panel? _fallbackItemsHost;
    private ItemContainerGenerator? _itemContainerGenerator;
    private bool _generatorItemsChangedSubscribed;
    private static readonly bool s_useLegacyItemsPipeline = string.Equals(
        Environment.GetEnvironmentVariable("JALIUM_USE_LEGACY_ITEMS_PIPELINE"),
        "1",
        StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.GenericItemsControlAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the ItemsSource dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(ItemsControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>
    /// Identifies the ItemTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(ItemsControl),
            new PropertyMetadata(null, OnItemTemplateChanged));

    /// <summary>
    /// Identifies the ItemTemplateSelector dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty ItemTemplateSelectorProperty =
        DependencyProperty.Register(nameof(ItemTemplateSelector), typeof(DataTemplateSelector), typeof(ItemsControl),
            new PropertyMetadata(null, OnItemTemplateSelectorChanged));

    /// <summary>
    /// Identifies the ItemsPanel dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty ItemsPanelProperty =
        DependencyProperty.Register(nameof(ItemsPanel), typeof(ItemsPanelTemplate), typeof(ItemsControl),
            new PropertyMetadata(null, OnItemsPanelChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a collection used to generate the content of the ItemsControl.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the DataTemplate used to display each item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets the DataTemplateSelector used to display each item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public DataTemplateSelector? ItemTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ItemTemplateSelectorProperty);
        set => SetValue(ItemTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets the template that defines the panel that controls the layout of items.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
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
    /// Internal accessor for the items-host panel. Used by the DevTools inspector to
    /// map a right-clicked item container back to its item index for delete/undo
    /// (the generator map is only populated on the virtualizing path). Mirrors the
    /// protected <see cref="ItemsHost"/>.
    /// </summary>
    internal Panel? ItemsHostInternal => ItemsHost;

    /// <summary>
    /// Gets the ItemContainerGenerator associated with this control.
    /// </summary>
    public ItemContainerGenerator ItemContainerGenerator
    {
        get
        {
            EnsureItemContainerGenerator();
            return _itemContainerGenerator!;
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
        ((INotifyCollectionChanged)Items).CollectionChanged += OnItemsCollectionChanged;
        InitializeWpfParityState();
    }

    private ItemContainerGenerator EnsureItemContainerGenerator()
    {
        _itemContainerGenerator ??= new ItemContainerGenerator(this);
        if (!_generatorItemsChangedSubscribed)
        {
            _itemContainerGenerator.ItemsChanged += OnGeneratorItemsChanged;
            _generatorItemsChangedSubscribed = true;
        }

        return _itemContainerGenerator;
    }

    #endregion

    #region Template Support

    /// <summary>
    /// Called when the template is applied.
    /// </summary>
    public override void OnApplyTemplate()
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
        if (presenter.ItemsPanel != null && _itemContainerGenerator != null)
        {
            AttachGeneratorToPanel(presenter.ItemsPanel, _itemContainerGenerator);
        }
        RefreshItems();
    }

    #endregion

    #region Item Generation

    /// <summary>
    /// Creates the panel that will host the items.
    /// </summary>
    protected virtual Panel CreateItemsPanel()
    {
        if (ItemsPanel != null)
        {
            var panel = ItemsPanel.CreatePanel() as Panel;
            if (panel != null) return panel;
        }

        return new StackPanel { Orientation = Orientation.Vertical };
    }

    /// <summary>
    /// Creates an <see cref="ItemsPanelTemplate"/> that instantiates the specified panel type.
    /// </summary>
    protected static ItemsPanelTemplate CreateItemsPanelTemplate(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type panelType)
    {
        var template = new ItemsPanelTemplate { PanelType = panelType };
        template.Seal();
        return template;
    }

    /// <summary>
    /// Creates a container for the specified item.
    /// </summary>
    protected virtual FrameworkElement GetContainerForItem(object item)
    {
        if (GetContainerForItemOverride() is FrameworkElement container)
        {
            return container;
        }

        throw new InvalidOperationException("An item container must be a FrameworkElement.");
    }

    /// <summary>
    /// Determines if the specified item is (or is eligible to be) its own container.
    /// </summary>
    public bool IsItemItsOwnContainer(object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return IsItemItsOwnContainerOverride(item);
    }

    /// <summary>
    /// Determines whether an item is already its own generated container.
    /// </summary>
    protected virtual bool IsItemItsOwnContainerOverride(object item) => item is UIElement;

    /// <summary>
    /// Prepares the specified element to display the specified item.
    /// </summary>
    protected virtual void PrepareContainerForItem(FrameworkElement element, object item)
    {
        ApplyItemContainerStyleAndBindingGroup(element, item);

        // Items that are already their own container must not be assigned back into
        // their own Content property, otherwise controls like DockItem/StatusBarItem
        // end up parenting themselves as content visuals.
        if (ReferenceEquals(element, item))
        {
            return;
        }

        // Determine the template to use
        var template = ItemTemplate;
        if (template == null && ItemTemplateSelector != null)
        {
            template = ItemTemplateSelector.SelectTemplate(item, this);
        }

        template ??= GetDisplayMemberTemplate();

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
        if (_itemsControlInitializationDepth > 0)
        {
            _refreshItemsWhenInitializationCompletes = true;
            return;
        }

        UpdateItemsControlState();

        // Get the panel (either from ItemsPresenter or fallback)
        var panel = ItemsHost;

        // If no panel from template, create fallback
        if (panel == null && !HasTemplate)
        {
            _fallbackItemsHost = CreateItemsPanel();
            AddVisualChild(_fallbackItemsHost);
            panel = _fallbackItemsHost;
        }

        if (panel == null)
        {
            return;
        }

        // Also clear the old fallback panel if we switched to a template panel
        // This ensures items previously parented to the fallback are properly disconnected
        if (_fallbackItemsHost != null && _fallbackItemsHost != panel)
        {
            _fallbackItemsHost.Children.Clear();
            RemoveVisualChild(_fallbackItemsHost);
            _fallbackItemsHost = null;
        }

        // Mark the host panel and stamp the owner UNCONDITIONALLY (regardless of pipeline /
        // IsVirtualizing) so the panel reads virtualization attached properties off this control, so
        // ShouldUseVirtualizingPipeline and the panel's ShouldVirtualize() agree on the same source of
        // truth (this owner), and so GetItemsOwner/ItemsControlFromItemContainer resolve.
        panel.IsItemsHost = true;
        if (panel is VirtualizingPanel ownerStampPanel)
        {
            ownerStampPanel.ItemsOwner = this;
        }

        if (ShouldUseVirtualizingPipeline(panel))
        {
            panel.Children.Clear();
            var generator = EnsureItemContainerGenerator();
            generator.RemoveAll();
            AttachGeneratorToPanel(panel, generator);

            // Force the virtualizing panel to reset its cached item-count / height
            // index / realization window. Without this, when ItemsSource is supplied
            // by a Binding that resolves AFTER the first Measure pass (e.g. a Page
            // whose DataContext is assigned in code-behind after InitializeComponent),
            // the panel is left with state from the initial zero-item measure and
            // refuses to realize anything until the user scrolls.
            if (panel is VirtualizingPanel virtualizingPanel)
            {
                virtualizingPanel.NotifyItemsChanged(
                    generator,
                    new ItemsChangedEventArgs(
                        NotifyCollectionChangedAction.Reset,
                        new GeneratorPosition(-1, 0),
                        0,
                        0));
            }

            panel.InvalidateMeasure();
            InvalidateMeasure();
            return;
        }

        // Non-virtualizing path: materialize all containers.
        panel.Children.Clear();

        // Add items from ItemsSource or Items collection.
        // Batch-add to avoid per-item layout invalidation.
        var source = ItemsSource ?? Items;
        if (source != null)
        {
            panel.Children.BeginBatchUpdate();
            try
            {
                foreach (var item in source)
                {
                    AddItemToPanel(item);
                }
            }
            finally
            {
                panel.Children.EndBatchUpdate();
            }
        }

        InvalidateMeasure();
    }

    private bool ShouldUseVirtualizingPipeline(Panel panel)
    {
        // Read IsVirtualizing off THIS control (the owner) — the same source of truth the panel's
        // ShouldVirtualize() now uses (via GetOwner()). Reading it off the panel would split-brain:
        // setting VirtualizingPanel.IsVirtualizing=false on the control would leave this gate true
        // (panel default) while the panel measured non-virtualized against an empty Children.
        return !s_useLegacyItemsPipeline &&
               panel is VirtualizingPanel &&
               VirtualizingPanel.GetIsVirtualizing(this);
    }

    private void AttachGeneratorToPanel(Panel panel, ItemContainerGenerator generator)
    {
        if (panel is VirtualizingPanel virtualizingPanel)
        {
            virtualizingPanel.SetItemContainerGenerator(generator);
        }
    }

    private void AddItemToPanel(object item)
    {
        if (ItemsHost == null) return;

        FrameworkElement container;
        if (IsItemItsOwnContainer(item))
        {
            container = (FrameworkElement)item;
            PrepareContainerForItemOverride(container, item);
        }
        else
        {
            container = GetContainerForItem(item);
            PrepareContainerForItemOverride(container, item);
        }

        ItemsHost.Children.Add(container);
        SetAlternationIndex(container, ItemsHost.Children.Count - 1);
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

        return default(Size);
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
    protected override int VisualChildrenCount
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
    protected override Visual? GetVisualChild(int index)
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
    protected override void OnRender(DrawingContext drawingContext)
    {
        // ItemsControl itself doesn't render anything, the items panel does
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl itemsControl)
        {
            itemsControl.Items.SetItemsSource((IEnumerable?)e.NewValue);

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

            itemsControl.UpdateGroupingViewSubscription((IEnumerable?)e.NewValue);

            if (itemsControl._itemContainerGenerator != null)
            {
                itemsControl._itemContainerGenerator.OnCollectionChanged(
                    new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }

            itemsControl.OnItemsSourceChanged((IEnumerable?)e.OldValue, (IEnumerable?)e.NewValue);
            itemsControl.OnItemsChanged(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            itemsControl.UpdateItemsControlState();
            itemsControl.RefreshItems();
        }
    }

    private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl itemsControl)
        {
            itemsControl.OnItemTemplateChanged((DataTemplate?)e.OldValue, (DataTemplate?)e.NewValue);
            itemsControl.RefreshItems();
        }
    }

    private static void OnItemTemplateSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl ic)
        {
            ic.OnItemTemplateSelectorChanged((DataTemplateSelector?)e.OldValue, (DataTemplateSelector?)e.NewValue);
            ic.RefreshItems();
        }
    }

    private static void OnItemsPanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl itemsControl)
        {
            itemsControl.OnItemsPanelChanged((ItemsPanelTemplate?)e.OldValue, (ItemsPanelTemplate?)e.NewValue);
            itemsControl._fallbackItemsHost = null;
            if (itemsControl._itemsPresenter != null)
            {
                itemsControl._itemsPresenter.NotifyTemplateChanged(
                    (ItemsPanelTemplate?)e.OldValue,
                    (ItemsPanelTemplate?)e.NewValue);
            }
            itemsControl.RefreshItems();
        }
    }

    private void OnGeneratorItemsChanged(object sender, ItemsChangedEventArgs e)
    {
        if (ItemsHost is VirtualizingPanel virtualizingPanel && ShouldUseVirtualizingPipeline(virtualizingPanel))
        {
            if (virtualizingPanel.NotifyItemsChanged(sender, e))
            {
                virtualizingPanel.InvalidateMeasure();
                InvalidateMeasure();
            }
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnItemsChanged(e);
        UpdateItemsControlState();

        // Notify the generator of the change
        _itemContainerGenerator?.OnCollectionChanged(e);

        // Handle incremental updates for simple cases
        var panel = ItemsHost;
        if (panel == null)
        {
            RefreshItems();
            return;
        }

        if (ShouldUseVirtualizingPipeline(panel))
        {
            panel.InvalidateMeasure();
            InvalidateMeasure();
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

        UpdateAlternationIndices();
        InvalidateMeasure();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnItemsChanged(e);
        UpdateItemsControlState();

        if (ItemsSource == null)
        {
            _itemContainerGenerator?.OnCollectionChanged(e);

            var panel = ItemsHost;
            if (panel == null)
            {
                RefreshItems();
                return;
            }

            if (ShouldUseVirtualizingPipeline(panel))
            {
                panel.InvalidateMeasure();
                InvalidateMeasure();
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

            UpdateAlternationIndices();
            InvalidateMeasure();
        }
    }

    #endregion

    #region Internal Methods for ItemContainerGenerator

    /// <summary>
    /// Internal entry point for <see cref="RefreshItems"/> used by <see cref="Primitives.ItemsPresenter"/>.
    /// </summary>
    internal void RefreshItemsInternal() => RefreshItems();

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
        PrepareContainerForItemOverride(element, item);
    }

    /// <summary>
    /// When overridden in a derived class, undoes the effects of
    /// <see cref="PrepareContainerForItem"/>: clears the generated container's content (and, in
    /// derived selectors, its selection state) before it is recycled or discarded. It is NOT called
    /// for items that are their own container.
    /// </summary>
    /// <remarks>
    /// For the steady-state scroll-recycle path the content clear is redundant — the recycle-pop
    /// always re-prepares the container, so <see cref="PrepareContainerForItem"/> overwrites these
    /// values. It is correctness-bearing for the ORPHANED container case (an item removed, or a
    /// full reset, where the pooled container is never re-popped) so it does not alias the dead
    /// item.
    /// <para>
    /// Framework-level animation hygiene (stopping the container subtree's animations and hard-
    /// discarding the animated value layer back to base values) is performed by the generator's
    /// recycle choke point BEFORE this method runs, so overrides observe base values here. On top
    /// of that, the base implementation resets the visual-state DPs animations commonly leave a
    /// local value on (RenderTransform / Opacity / ClipToBounds / Visibility) via
    /// <see cref="DependencyObject.ClearValue(DependencyProperty)"/> only — never SetValue — so style/template values
    /// keep working. These local values are the symmetric counterpart of what Prepare-time code
    /// sets; a custom container that legitimately holds one of them as a constructor-set local
    /// value must override this method and skip base (the Prepare/Clear symmetry contract:
    /// controls that override <see cref="PrepareContainerForItem"/> and skip base must also
    /// override this method to skip the corresponding base branch and undo their own Prepare-set
    /// local state — see ListBox's IsSelected/ParentListBox handling).
    /// </para>
    /// </remarks>
    protected virtual void ClearContainerForItem(FrameworkElement element, object item)
    {
        if (ReferenceEquals(element, item))
        {
            // Own-container item: we never set Content on it, so there is nothing to undo.
            return;
        }

        ClearItemContainerStyleAndBindingGroup(element, item);

        switch (element)
        {
            case ContentPresenter contentPresenter:
                contentPresenter.ClearValue(ContentPresenter.ContentProperty);
                contentPresenter.ClearValue(ContentPresenter.ContentTemplateProperty);
                break;
            case ContentControl contentControl:
                contentControl.ClearValue(ContentControl.ContentProperty);
                contentControl.ClearValue(ContentControl.ContentTemplateProperty);
                contentControl.ClearValue(ContentControl.ContentTemplateSelectorProperty);
                break;
        }

        // Visual-state hygiene for pooled containers: a recycled container must not inherit the
        // previous item's leftover transform/opacity/clip/visibility local values (typically left
        // behind by per-container animation code). ClearValue only — back to base.
        element.ClearValue(UIElement.RenderTransformProperty);
        element.ClearValue(UIElement.OpacityProperty);
        element.ClearValue(UIElement.ClipToBoundsProperty);
        element.ClearValue(UIElement.VisibilityProperty);
    }

    /// <summary>
    /// Internal wrapper for <see cref="ClearContainerForItem"/> used by ItemContainerGenerator
    /// before a container is pushed into the recycle pool (or discarded). On the scroll-recycle
    /// path the container has already been removed from the panel's visual tree, so the
    /// content-clear's measure-invalidation walks up to no parent. On the collection-Reset path
    /// (generator OnReset recycles realized entries before the panel detaches them) the container
    /// is still attached, so the clear performs an O(realized) upward invalidation walk — bounded
    /// by the viewport and short-circuited by already-dirty ancestors.
    /// </summary>
    internal void ClearContainerForItemInternal(FrameworkElement element, object item)
    {
        ClearContainerForItemOverride(element, item);
    }

    /// <summary>
    /// Returns the <see cref="ItemsControl"/> that owns the given items-host panel, or <c>null</c> if
    /// the element is not an items host. (WPF parity.)
    /// </summary>
    public static ItemsControl? GetItemsOwner(DependencyObject element)
    {
        if (element is Panel { IsItemsHost: true } panel)
        {
            // Templated host: the panel is a child of the ItemsPresenter, which knows its owner.
            if (panel.VisualParent is ItemsPresenter presenter && presenter.Owner != null)
            {
                return presenter.Owner;
            }

            // Fallback host: the panel is parented directly to the owning ItemsControl.
            if (panel.VisualParent is ItemsControl owner)
            {
                return owner;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the <see cref="ItemsControl"/> that owns the given container element (generated or
    /// own-container), or <c>null</c>. (WPF parity.)
    /// </summary>
    public static ItemsControl? ItemsControlFromItemContainer(DependencyObject container)
    {
        if (container is UIElement { VisualParent: Panel panel })
        {
            return GetItemsOwner(panel);
        }

        return null;
    }

    /// <summary>
    /// Provides the routed selection-change hook shared by selector-derived controls and
    /// WPF-compatible item controls such as <see cref="DataGrid"/>.
    /// </summary>
    protected virtual void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    private void InsertItemToPanel(object item, int index)
    {
        if (ItemsHost == null) return;

        FrameworkElement container;
        if (IsItemItsOwnContainer(item))
        {
            container = (FrameworkElement)item;
            PrepareContainerForItemOverride(container, item);
        }
        else
        {
            container = GetContainerForItem(item);
            PrepareContainerForItemOverride(container, item);
        }

        if (index >= 0 && index <= ItemsHost.Children.Count)
        {
            ItemsHost.Children.Insert(index, container);
        }
        else
        {
            ItemsHost.Children.Add(container);
        }

        UpdateAlternationIndices();
    }

    #endregion
}

/// <summary>
/// Represents a collection of items in an ItemsControl.
/// </summary>
public sealed partial class ItemCollection : Jalium.UI.Data.CollectionView, IList<object>
{
    private readonly List<object> _items = new();
    private readonly ItemsControl _owner;

    internal ItemCollection(ItemsControl owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        InitializeCollectionView();
    }

    /// <summary>
    /// Gets or sets the item at the specified index.
    /// </summary>
    public object this[int index]
    {
        get => GetItemAt(index);
        set
        {
            VerifyWritable();
            var sourceIndex = GetDirectSourceIndex(index);
            var oldItem = _items[sourceIndex];
            _items[sourceIndex] = value;
            OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace, value, oldItem, index));
        }
    }

    /// <summary>
    /// Gets the number of items in the collection.
    /// </summary>
    public override int Count => GetViewCount();

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => _owner.ItemsSource != null;

    /// <summary>
    /// Adds an item to the collection.
    /// </summary>
    public int Add(object item)
    {
        VerifyWritable();
        _items.Add(item);
        var sourceIndex = _items.Count - 1;
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, sourceIndex));
        return sourceIndex;
    }

    void ICollection<object>.Add(object item) => Add(item);

    /// <summary>
    /// Clears all items from the collection.
    /// </summary>
    public void Clear()
    {
        VerifyWritable();
        _items.Clear();
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Determines whether the collection contains a specific item.
    /// </summary>
    public override bool Contains(object item) => ViewContains(item);

    /// <summary>
    /// Copies the collection to an array.
    /// </summary>
    public void CopyTo(object[] array, int arrayIndex) => CopyViewTo(array, arrayIndex);

    /// <summary>
    /// Copies the effective view to the specified array.
    /// </summary>
    public void CopyTo(Array array, int index) => CopyViewTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    IEnumerator<object> IEnumerable<object>.GetEnumerator() => GetTypedViewEnumerator();

    /// <summary>
    /// Determines the index of a specific item in the collection.
    /// </summary>
    public override int IndexOf(object item) => ViewIndexOf(item);

    /// <summary>
    /// Inserts an item at the specified index.
    /// </summary>
    public void Insert(int index, object item)
    {
        VerifyWritable();
        _items.Insert(index, item);
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, index));
    }

    /// <summary>
    /// Removes the first occurrence of a specific item from the collection.
    /// </summary>
    public void Remove(object item)
    {
        RemoveDirectItem(item);
    }

    bool ICollection<object>.Remove(object item) => RemoveDirectItem(item);

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        VerifyWritable();
        var sourceIndex = GetDirectSourceIndex(index);
        var item = _items[sourceIndex];
        _items.RemoveAt(sourceIndex);
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, item, index));
    }

    /// <summary>
    /// Adds multiple items to the collection, firing a single Reset notification.
    /// </summary>
    public void AddRange(IList<object> items)
    {
        VerifyWritable();
        if (items.Count == 0)
        {
            return;
        }

        _items.AddRange(items);
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    private bool RemoveDirectItem(object item)
    {
        VerifyWritable();
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, item, index));
        return true;
    }

    private void VerifyWritable()
    {
        if (_owner.ItemsSource != null)
        {
            throw new InvalidOperationException(
                "Items cannot be changed while ItemsSource is in use.");
        }
    }
}
