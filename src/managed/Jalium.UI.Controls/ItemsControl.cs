using System.Collections;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Data;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that can be used to present a collection of items.
/// </summary>
public partial class ItemsControl : Control, Jalium.UI.Markup.IAddChild, IContainItemStorage
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
        // Defence in depth alongside the primary guard in ItemsPresenter.AttachToOwner (its only
        // caller): never let a presenter from a template instance its host has already discarded
        // overwrite the live registration. Mirror AttachToOwner's guard exactly — compare against
        // the presenter's own templated host, and fail open when it is not a Control (a
        // data-template-authored or Page-hosted presenter carries no template-instance root to
        // match, so it must never be rejected here).
        if (presenter.TemplateInstanceRoot != null &&
            presenter.TemplatedParent is Control templatedHost &&
            !ReferenceEquals(presenter.TemplateInstanceRoot, templatedHost.TemplateRootInternal))
        {
            return;
        }

        _itemsPresenter = presenter;
        if (presenter.ItemsPanel != null && _itemContainerGenerator != null)
        {
            AttachGeneratorToPanel(presenter.ItemsPanel, _itemContainerGenerator);
        }
        RefreshItems();
    }

    /// <summary>
    /// Releases the ItemsPresenter when the template tree containing it is discarded
    /// (Template replaced at runtime, theme switch, or Template cleared). Without this,
    /// <see cref="ItemsHost"/> keeps resolving to the panel inside the discarded tree:
    /// items realize into a detached, invisible panel (blank list once the template is
    /// cleared, because the fallback host is never built), and the still-armed old panel
    /// can replay a stale queued measure that steals containers from the replacement
    /// panel through the shared generator — the same zombie-measure mechanism
    /// <see cref="RetireItemsHostPanel"/> exists to close.
    /// </summary>
    internal override void OnTemplateContentClearing()
    {
        base.OnTemplateContentClearing();

        // Only a presenter owned by THIS control's template is affected. TemplatedParent
        // is the membership test that matches SetTemplatedParentRecursive: it covers
        // presenters reached through Popup/Border/ContentControl hops (which are not pure
        // visual descendants of the template root), while a nested ItemsControl's own
        // presenter has that nested control as its TemplatedParent and is left alone.
        if (_itemsPresenter != null && ReferenceEquals(_itemsPresenter.TemplatedParent, this))
        {
            // invalidateMeasure: false — the presenter leaves the tree together with the
            // discarded template; queueing it for layout would only let a zombie measure
            // re-create a panel on the dead presenter.
            _itemsPresenter.InvalidatePanel(invalidateMeasure: false);
            _itemsPresenter = null;
        }
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
        // This ensures items previously parented to the fallback are properly disconnected.
        // Clear the field before detaching — RemoveVisualChild re-syncs the cached
        // visual-children count from the overridden property, which reads _fallbackItemsHost.
        if (_fallbackItemsHost != null && _fallbackItemsHost != panel)
        {
            var fallback = _fallbackItemsHost;
            RetireItemsHostPanel(fallback);
            _fallbackItemsHost = null;
            RemoveVisualChild(fallback);
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

    /// <summary>
    /// Fully decommissions an items-host panel that is being replaced (fallback panel superseded
    /// by a template panel, or a panel discarded on ItemsPanel template change). Clearing the
    /// children releases their visual and logical parents; dropping the items-host flag and the
    /// generator/owner references disarms the panel so a stale InvalidateMeasure queued before the
    /// swap cannot make the retired panel realize containers again. Without this, a "zombie"
    /// measure on the retired panel pulls live containers out of the shared ItemContainerGenerator
    /// and re-parents them away from the active panel (historically crashing with "The logical
    /// child already has a parent" when the tab hosting the list was selected the first time).
    /// </summary>
    internal static void RetireItemsHostPanel(Panel panel)
    {
        panel.Children.Clear();

        // Disarm BEFORE dropping the items-host flag: IsItemsHost=false raises
        // OnIsItemsHostChanged synchronously, and an override that forces layout from
        // that callback would otherwise observe a retired panel whose generator is
        // still live — exactly the zombie-realize window this method exists to close.
        if (panel is VirtualizingPanel virtualizingPanel)
        {
            virtualizingPanel.SetItemContainerGenerator(null);
            virtualizingPanel.ItemsOwner = null;
        }

        panel.IsItemsHost = false;
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
            if (itemsControl._fallbackItemsHost != null)
            {
                // Field cleared before detaching — see the matching note in RefreshItems.
                var fallback = itemsControl._fallbackItemsHost;
                RetireItemsHostPanel(fallback);
                itemsControl._fallbackItemsHost = null;
                itemsControl.RemoveVisualChild(fallback);
            }
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

    #region ItemStorage

    private readonly Dictionary<object, Dictionary<DependencyProperty, object?>> _storedItemValues = new();

    void IContainItemStorage.StoreItemValue(object item, DependencyProperty dp, object value)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(dp);
        if (!_storedItemValues.TryGetValue(item, out Dictionary<DependencyProperty, object?>? values))
        {
            values = new Dictionary<DependencyProperty, object?>();
            _storedItemValues.Add(item, values);
        }

        values[dp] = value;
    }

    object? IContainItemStorage.ReadItemValue(object item, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(dp);
        return _storedItemValues.TryGetValue(item, out Dictionary<DependencyProperty, object?>? values)
            && values.TryGetValue(dp, out object? value)
                ? value
                : DependencyProperty.UnsetValue;
    }

    void IContainItemStorage.ClearItemValue(object item, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(dp);
        if (!_storedItemValues.TryGetValue(item, out Dictionary<DependencyProperty, object?>? values))
        {
            return;
        }

        values.Remove(dp);
        if (values.Count == 0)
        {
            _storedItemValues.Remove(item);
        }
    }

    void IContainItemStorage.ClearValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        foreach (Dictionary<DependencyProperty, object?> values in _storedItemValues.Values)
        {
            values.Remove(dp);
        }

        foreach (object item in _storedItemValues
            .Where(static pair => pair.Value.Count == 0)
            .Select(static pair => pair.Key)
            .ToArray())
        {
            _storedItemValues.Remove(item);
        }
    }

    void IContainItemStorage.Clear() => _storedItemValues.Clear();

    #endregion

    #region WpfParity

    private static readonly DependencyPropertyKey AlternationIndexPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "AlternationIndex",
            typeof(int),
            typeof(ItemsControl),
            new PropertyMetadata(0));

    private static readonly DependencyPropertyKey HasItemsPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasItems),
            typeof(bool),
            typeof(ItemsControl),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsGroupingPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsGrouping),
            typeof(bool),
            typeof(ItemsControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty AlternationCountProperty =
        DependencyProperty.Register(
            nameof(AlternationCount),
            typeof(int),
            typeof(ItemsControl),
            new PropertyMetadata(0, OnAlternationCountPropertyChanged),
            static value => value is int count && count >= 0);

    public static readonly DependencyProperty AlternationIndexProperty =
        AlternationIndexPropertyKey.DependencyProperty;

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(ItemsControl),
            new PropertyMetadata(string.Empty, OnDisplayMemberPathPropertyChanged));

    public static readonly DependencyProperty GroupStyleSelectorProperty =
        DependencyProperty.Register(
            nameof(GroupStyleSelector),
            typeof(GroupStyleSelector),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnGroupStyleSelectorPropertyChanged));

    public static readonly DependencyProperty HasItemsProperty = HasItemsPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsGroupingProperty = IsGroupingPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsTextSearchCaseSensitiveProperty =
        DependencyProperty.Register(
            nameof(IsTextSearchCaseSensitive),
            typeof(bool),
            typeof(ItemsControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsTextSearchEnabledProperty =
        DependencyProperty.Register(
            nameof(IsTextSearchEnabled),
            typeof(bool),
            typeof(ItemsControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ItemBindingGroupProperty =
        DependencyProperty.Register(
            nameof(ItemBindingGroup),
            typeof(BindingGroup),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnItemBindingGroupPropertyChanged));

    public static readonly DependencyProperty ItemContainerStyleProperty =
        DependencyProperty.Register(
            nameof(ItemContainerStyle),
            typeof(Style),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnItemContainerStylePropertyChanged));

    public static readonly DependencyProperty ItemContainerStyleSelectorProperty =
        DependencyProperty.Register(
            nameof(ItemContainerStyleSelector),
            typeof(StyleSelector),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnItemContainerStyleSelectorPropertyChanged));

    public static readonly DependencyProperty ItemStringFormatProperty =
        DependencyProperty.Register(
            nameof(ItemStringFormat),
            typeof(string),
            typeof(ItemsControl),
            new PropertyMetadata(null, OnItemStringFormatPropertyChanged));

    private readonly ObservableCollection<GroupStyle> _groupStyle = new();
    private ICollectionView? _observedGroupingView;
    private DataTemplate? _displayMemberTemplate;
    private int _itemsControlInitializationDepth;
    private bool _refreshItemsWhenInitializationCompletes;

    public int AlternationCount
    {
        get => (int)(GetValue(AlternationCountProperty) ?? 0);
        set => SetValue(AlternationCountProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty) ?? string.Empty;
        set => SetValue(DisplayMemberPathProperty, value ?? string.Empty);
    }

    public ObservableCollection<GroupStyle> GroupStyle => _groupStyle;

    public GroupStyleSelector? GroupStyleSelector
    {
        get => (GroupStyleSelector?)GetValue(GroupStyleSelectorProperty);
        set => SetValue(GroupStyleSelectorProperty, value);
    }

    public bool HasItems => (bool)(GetValue(HasItemsProperty) ?? false);

    public bool IsGrouping => (bool)(GetValue(IsGroupingProperty) ?? false);

    public bool IsTextSearchCaseSensitive
    {
        get => (bool)(GetValue(IsTextSearchCaseSensitiveProperty) ?? false);
        set => SetValue(IsTextSearchCaseSensitiveProperty, value);
    }

    public bool IsTextSearchEnabled
    {
        get => (bool)(GetValue(IsTextSearchEnabledProperty) ?? false);
        set => SetValue(IsTextSearchEnabledProperty, value);
    }

    public BindingGroup? ItemBindingGroup
    {
        get => (BindingGroup?)GetValue(ItemBindingGroupProperty);
        set => SetValue(ItemBindingGroupProperty, value);
    }

    public Style? ItemContainerStyle
    {
        get => (Style?)GetValue(ItemContainerStyleProperty);
        set => SetValue(ItemContainerStyleProperty, value);
    }

    public StyleSelector? ItemContainerStyleSelector
    {
        get => (StyleSelector?)GetValue(ItemContainerStyleSelectorProperty);
        set => SetValue(ItemContainerStyleSelectorProperty, value);
    }

    public string? ItemStringFormat
    {
        get => (string?)GetValue(ItemStringFormatProperty);
        set => SetValue(ItemStringFormatProperty, value);
    }

    public static int GetAlternationIndex(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)(element.GetValue(AlternationIndexProperty) ?? 0);
    }

    public static DependencyObject? ContainerFromElement(ItemsControl itemsControl, DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        ArgumentNullException.ThrowIfNull(element);

        DependencyObject? current = element;
        while (current != null && !ReferenceEquals(current, itemsControl))
        {
            if (itemsControl._itemContainerGenerator?.IndexFromContainer(current) >= 0)
            {
                return current;
            }

            if (current is Visual visual && visual.VisualParent is Panel panel &&
                ReferenceEquals(GetItemsOwner(panel), itemsControl))
            {
                return current;
            }

            current = current switch
            {
                FrameworkElement frameworkElement => frameworkElement.Parent,
                Visual visualParentSource => visualParentSource.VisualParent,
                _ => null,
            };
        }

        return null;
    }

    public DependencyObject? ContainerFromElement(DependencyObject element) =>
        ContainerFromElement(this, element);

    public override void BeginInit()
    {
        base.BeginInit();
        _itemsControlInitializationDepth++;
    }

    public override void EndInit()
    {
        if (_itemsControlInitializationDepth > 0)
        {
            _itemsControlInitializationDepth--;
        }

        base.EndInit();
        if (_itemsControlInitializationDepth == 0 && _refreshItemsWhenInitializationCompletes)
        {
            _refreshItemsWhenInitializationCompletes = false;
            RefreshItems();
        }
    }

    public bool ShouldSerializeGroupStyle() => _groupStyle.Count > 0;

    public bool ShouldSerializeItems() => ItemsSource == null && Items.Count > 0;

    protected virtual void AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Items.Add(value);
    }

    protected virtual void AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!string.IsNullOrWhiteSpace(text))
        {
            AddChild(text);
        }
    }

    void Jalium.UI.Markup.IAddChild.AddChild(object value) => AddChild(value);

    void Jalium.UI.Markup.IAddChild.AddText(string text) => AddText(text);

    protected virtual DependencyObject GetContainerForItemOverride() => new ContentPresenter();

    protected virtual void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            throw new InvalidOperationException("An item container must be a FrameworkElement.");
        }

        PrepareContainerForItem(frameworkElement, item);
    }

    protected virtual void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is FrameworkElement frameworkElement)
        {
            ClearContainerForItem(frameworkElement, item);
        }
    }

    protected virtual bool ShouldApplyItemContainerStyle(DependencyObject container, object item)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(item);
        return container is FrameworkElement frameworkElement &&
               ReferenceEquals(frameworkElement.ReadLocalValue(FrameworkElement.StyleProperty), DependencyProperty.UnsetValue);
    }

    protected virtual void OnAlternationCountChanged(int oldAlternationCount, int newAlternationCount)
    {
        UpdateAlternationIndices();
    }

    protected virtual void OnDisplayMemberPathChanged(string oldDisplayMemberPath, string newDisplayMemberPath) =>
        ResetDisplayMemberTemplateAndRefresh();

    protected virtual void OnGroupStyleSelectorChanged(
        GroupStyleSelector? oldGroupStyleSelector,
        GroupStyleSelector? newGroupStyleSelector) => RefreshItems();

    protected virtual void OnItemBindingGroupChanged(
        BindingGroup? oldItemBindingGroup,
        BindingGroup? newItemBindingGroup) => RefreshItems();

    protected virtual void OnItemContainerStyleChanged(
        Style? oldItemContainerStyle,
        Style? newItemContainerStyle) => RefreshItems();

    protected virtual void OnItemContainerStyleSelectorChanged(
        StyleSelector? oldItemContainerStyleSelector,
        StyleSelector? newItemContainerStyleSelector) => RefreshItems();

    protected virtual void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
    }

    protected virtual void OnItemsPanelChanged(
        ItemsPanelTemplate? oldItemsPanel,
        ItemsPanelTemplate? newItemsPanel)
    {
    }

    protected virtual void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
    }

    protected virtual void OnItemStringFormatChanged(string? oldItemStringFormat, string? newItemStringFormat) =>
        ResetDisplayMemberTemplateAndRefresh();

    protected virtual void OnItemTemplateChanged(DataTemplate? oldItemTemplate, DataTemplate? newItemTemplate)
    {
    }

    protected virtual void OnItemTemplateSelectorChanged(
        DataTemplateSelector? oldItemTemplateSelector,
        DataTemplateSelector? newItemTemplateSelector)
    {
    }

    private void InitializeWpfParityState()
    {
        _groupStyle.CollectionChanged += OnGroupStyleCollectionChanged;
        UpdateItemsControlState();
    }

    private void OnGroupStyleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateItemsControlState();
        RefreshItems();
    }

    private void UpdateGroupingViewSubscription(IEnumerable? source)
    {
        if (_observedGroupingView != null)
        {
            _observedGroupingView.GroupDescriptions.CollectionChanged -= OnGroupingDescriptionChanged;
            _observedGroupingView = null;
        }

        if (source != null)
        {
            _observedGroupingView = source as ICollectionView ?? CollectionViewSource.GetDefaultView(source);
            _observedGroupingView.GroupDescriptions.CollectionChanged += OnGroupingDescriptionChanged;
        }
    }

    private void OnGroupingDescriptionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateItemsControlState();
        RefreshItems();
    }

    private DataTemplate? GetDisplayMemberTemplate()
    {
        if (string.IsNullOrEmpty(DisplayMemberPath) && string.IsNullOrEmpty(ItemStringFormat))
        {
            return null;
        }

        return _displayMemberTemplate ??= CreateDisplayMemberTemplate();
    }

    private DataTemplate CreateDisplayMemberTemplate()
    {
        var path = DisplayMemberPath;
        var stringFormat = ItemStringFormat;
        var template = new DataTemplate();
        template.SetVisualTree(() =>
        {
            var text = new TextBlock();
            var binding = string.IsNullOrEmpty(path) ? new Binding() : new Binding(path);
            binding.StringFormat = stringFormat;
            text.SetBinding(TextBlock.TextProperty, binding);
            return text;
        });
        return template;
    }

    private void ResetDisplayMemberTemplateAndRefresh()
    {
        _displayMemberTemplate = null;
        RefreshItems();
    }

    private void UpdateItemsControlState()
    {
        SetValue(HasItemsPropertyKey, GetEffectiveItemCount() > 0);
        SetValue(IsGroupingPropertyKey, GetIsEffectivelyGrouping());
        UpdateAlternationIndices();
    }

    private int GetEffectiveItemCount()
    {
        if (ItemsSource == null)
        {
            return Items.Count;
        }

        if (ItemsSource is ICollection collection)
        {
            return collection.Count;
        }

        var enumerator = ItemsSource.GetEnumerator();
        try
        {
            var count = 0;
            while (enumerator.MoveNext())
            {
                count++;
            }

            return count;
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private bool GetIsEffectivelyGrouping()
    {
        if (ItemsSource == null)
        {
            return false;
        }

        ICollectionView view = ItemsSource as ICollectionView ?? CollectionViewSource.GetDefaultView(ItemsSource);
        return view.GroupDescriptions.Count > 0 || view.Groups is { Count: > 0 };
    }

    private Style? SelectContainerStyle(object item, DependencyObject container) =>
        ItemContainerStyleSelector?.SelectStyle(item, container) ?? ItemContainerStyle;

    private void ApplyItemContainerStyleAndBindingGroup(FrameworkElement element, object item)
    {
        var style = SelectContainerStyle(item, element);
        if (style != null && ShouldApplyItemContainerStyle(element, item))
        {
            element.Style = style;
        }

        if (ItemBindingGroup != null &&
            ReferenceEquals(element.ReadLocalValue(FrameworkElement.BindingGroupProperty), DependencyProperty.UnsetValue))
        {
            element.BindingGroup = ItemBindingGroup;
        }

        var index = _itemContainerGenerator?.IndexFromContainer(element) ?? -1;
        if (index < 0 && ItemsHost != null)
        {
            index = ItemsHost.Children.IndexOf(element);
        }

        SetAlternationIndex(element, index);
    }

    private void ClearItemContainerStyleAndBindingGroup(FrameworkElement element, object item)
    {
        var selectedStyle = SelectContainerStyle(item, element);
        if (selectedStyle != null && ReferenceEquals(element.Style, selectedStyle))
        {
            element.ClearValue(FrameworkElement.StyleProperty);
        }

        if (ItemBindingGroup != null && ReferenceEquals(element.BindingGroup, ItemBindingGroup))
        {
            element.ClearValue(FrameworkElement.BindingGroupProperty);
        }

        element.ClearValue(AlternationIndexPropertyKey);
    }

    internal void SetAlternationIndex(DependencyObject container, int index)
    {
        var value = AlternationCount > 0 && index >= 0 ? index % AlternationCount : 0;
        container.SetValue(AlternationIndexPropertyKey, value);
    }

    private void UpdateAlternationIndices()
    {
        var count = GetEffectiveItemCount();
        for (var index = 0; index < count; index++)
        {
            if (_itemContainerGenerator?.ContainerFromIndex(index) is DependencyObject generated)
            {
                SetAlternationIndex(generated, index);
            }
        }

        if (ItemsHost != null)
        {
            for (var index = 0; index < ItemsHost.Children.Count; index++)
            {
                SetAlternationIndex(ItemsHost.Children[index], index);
            }
        }
    }

    private static void OnAlternationCountPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnAlternationCountChanged((int)(e.OldValue ?? 0), (int)(e.NewValue ?? 0));
    }

    private static void OnDisplayMemberPathPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnDisplayMemberPathChanged((string?)e.OldValue ?? string.Empty, (string?)e.NewValue ?? string.Empty);
    }

    private static void OnGroupStyleSelectorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnGroupStyleSelectorChanged((GroupStyleSelector?)e.OldValue, (GroupStyleSelector?)e.NewValue);
    }

    private static void OnItemBindingGroupPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnItemBindingGroupChanged((BindingGroup?)e.OldValue, (BindingGroup?)e.NewValue);
    }

    private static void OnItemContainerStylePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnItemContainerStyleChanged((Style?)e.OldValue, (Style?)e.NewValue);
    }

    private static void OnItemContainerStyleSelectorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnItemContainerStyleSelectorChanged((StyleSelector?)e.OldValue, (StyleSelector?)e.NewValue);
    }

    private static void OnItemStringFormatPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ItemsControl)d;
        control.OnItemStringFormatChanged((string?)e.OldValue, (string?)e.NewValue);
    }

    #endregion
}
