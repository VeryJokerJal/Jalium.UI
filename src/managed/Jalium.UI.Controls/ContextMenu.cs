using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a context menu that appears on right-click.
/// Uses a <see cref = "Popup"/> to display its content at the correct screen position.
/// </summary>
[TemplatePart(Name = "PART_Popup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_PopupBorder", Type = typeof(Border))]
[TemplatePart(Name = "PART_ScrollHost", Type = typeof(MenuPopupScrollHost))]
public class ContextMenu : MenuBase
{
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.ContextMenuAutomationPeer(this);
#region Static Brushes
    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(ThemeColors.ControlBackground);
    private static readonly SolidColorBrush s_defaultBorderBrush = new(ThemeColors.ControlBorder);
#endregion
#region Animation Constants
    private const double PopupOpenDurationMs = 250;
    private const double PopupCloseDurationMs = 150;
    private const double PopupSlideOffsetY = -6;
    private static readonly BackEase s_popupOpenEase = new()
    {
        EasingMode = EasingMode.EaseOut,
        Amplitude = 0.85
    };
    private static readonly CubicEase s_popupCloseEase = new()
    {
        EasingMode = EasingMode.EaseInOut
    };
#endregion
#region Dependency Properties
    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(ContextMenu), new PropertyMetadata(false, OnIsOpenChanged));
    /// <summary>
    /// Identifies the PlacementTarget dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementTargetProperty = DependencyProperty.Register(nameof(PlacementTarget), typeof(UIElement), typeof(ContextMenu), new PropertyMetadata(null));
    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(nameof(Placement), typeof(PlacementMode), typeof(ContextMenu), new PropertyMetadata(PlacementMode.MousePoint));
    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(ContextMenu), new PropertyMetadata(0.0));
    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(ContextMenu), new PropertyMetadata(0.0));
    /// <summary>
    /// Identifies the StaysOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty StaysOpenProperty = DependencyProperty.Register(nameof(StaysOpen), typeof(bool), typeof(ContextMenu), new PropertyMetadata(false));
    /// <summary>
    /// Identifies the CustomPopupPlacementCallback dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CustomPopupPlacementCallbackProperty = DependencyProperty.Register(nameof(CustomPopupPlacementCallback), typeof(CustomPopupPlacementCallback), typeof(ContextMenu), new PropertyMetadata(null));
    /// <summary>
    /// Identifies the HasDropShadow dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HasDropShadowProperty = DependencyProperty.Register(nameof(HasDropShadow), typeof(bool), typeof(ContextMenu), new PropertyMetadata(false));
    /// <summary>
    /// Identifies the PlacementRectangle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementRectangleProperty = DependencyProperty.Register(nameof(PlacementRectangle), typeof(Rect), typeof(ContextMenu), new PropertyMetadata(Rect.Empty));
#endregion
    private static readonly DependencyPropertyKey PopupBackgroundPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PopupBackground), typeof(Brush), typeof(ContextMenu), new PropertyMetadata(null));
    public static readonly DependencyProperty PopupBackgroundProperty = PopupBackgroundPropertyKey.DependencyProperty;
    public Brush? PopupBackground => (Brush? )GetValue(PopupBackgroundProperty);

    private static readonly DependencyPropertyKey PopupBorderBrushPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PopupBorderBrush), typeof(Brush), typeof(ContextMenu), new PropertyMetadata(null));
    public static readonly DependencyProperty PopupBorderBrushProperty = PopupBorderBrushPropertyKey.DependencyProperty;
    public Brush? PopupBorderBrush => (Brush? )GetValue(PopupBorderBrushProperty);

#region Routed Events
    /// <summary>
    /// Identifies the Opened routed event.
    /// </summary>
    public static readonly RoutedEvent OpenedEvent = EventManager.RegisterRoutedEvent(nameof(Opened), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ContextMenu));
    /// <summary>
    /// Identifies the Closed routed event.
    /// </summary>
    public static readonly RoutedEvent ClosedEvent = EventManager.RegisterRoutedEvent(nameof(Closed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ContextMenu));
    /// <summary>
    /// Occurs when the context menu is opened.
    /// </summary>
    public event RoutedEventHandler Opened { add => AddHandler(OpenedEvent, value); remove => RemoveHandler(OpenedEvent, value); }

    /// <summary>
    /// Occurs when the context menu is closed.
    /// </summary>
    public event RoutedEventHandler Closed { add => AddHandler(ClosedEvent, value); remove => RemoveHandler(ClosedEvent, value); }

#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets a value indicating whether the context menu is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty)!; set => SetValue(IsOpenProperty, value); }

    /// <summary>
    /// Gets or sets the element relative to which the context menu is positioned.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public UIElement? PlacementTarget { get => (UIElement? )GetValue(PlacementTargetProperty); set => SetValue(PlacementTargetProperty, value); }

    /// <summary>
    /// Gets or sets the placement mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public PlacementMode Placement { get => (PlacementMode)GetValue(PlacementProperty)!; set => SetValue(PlacementProperty, value); }

    /// <summary>
    /// Gets or sets the horizontal offset from the placement target.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty)!; set => SetValue(HorizontalOffsetProperty, value); }

    /// <summary>
    /// Gets or sets the vertical offset from the placement target.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double VerticalOffset { get => (double)GetValue(VerticalOffsetProperty)!; set => SetValue(VerticalOffsetProperty, value); }

    /// <summary>
    /// Gets or sets a value indicating whether the menu stays open when clicked outside.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool StaysOpen { get => (bool)GetValue(StaysOpenProperty)!; set => SetValue(StaysOpenProperty, value); }

    /// <summary>
    /// Gets or sets the callback used when <see cref = "Placement"/> is
    /// <see cref = "PlacementMode.Custom"/>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public CustomPopupPlacementCallback? CustomPopupPlacementCallback { get => (CustomPopupPlacementCallback? )GetValue(CustomPopupPlacementCallbackProperty); set => SetValue(CustomPopupPlacementCallbackProperty, value); }

    /// <summary>
    /// Gets or sets whether the popup should render a drop shadow.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool HasDropShadow { get => (bool)GetValue(HasDropShadowProperty)!; set => SetValue(HasDropShadowProperty, value); }

    /// <summary>
    /// Gets or sets the rectangle relative to the placement target used to position the popup.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public Rect PlacementRectangle { get => (Rect)(GetValue(PlacementRectangleProperty) ?? Rect.Empty); set => SetValue(PlacementRectangleProperty, value); }

#endregion
#region Private Fields
    private bool _notifiedOpen;
    private Popup? _popup;
    private Border? _popupBorder;
    private MenuPopupScrollHost? _popupScrollHost;
    private readonly List<PopupEntry> _popupEntries = new();
    private readonly HashSet<UIElement> _logicalItems = new(ReferenceEqualityComparer.Instance);
    private FrameworkElement? _contextOwner;
    private sealed record PopupEntry(object Item, UIElement Container);
    private bool _restoreOpen;
    private PlacementMode _savedPlacement;
    private double _savedHorizontalOffset, _savedVerticalOffset;
    private bool _synchronizingPopup;
    private DispatcherTimer? _popupAnimTimer;
#endregion
#region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref = "ContextMenu"/> class.
    /// </summary>
    static ContextMenu() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ContextMenu), new PropertyMetadata(typeof(ContextMenu)));
    public ContextMenu()
    {
    }

    private protected override bool UsesFallbackItemsHost => false;

    protected override void RefreshItems()
    {
        base.RefreshItems();
        if (_popupScrollHost == null || _synchronizingPopup)
            return;
        foreach (var entry in _popupEntries.ToArray())
            if (!ReferenceEquals(entry.Item, entry.Container) && entry.Container is FrameworkElement element)
                PrepareContainerForItem(element, entry.Item);
        PopulatePopup();
    }

    protected internal override System.Collections.IEnumerator LogicalChildren => _logicalItems.GetEnumerator();

    protected override void OnDataContextChanged(object? oldValue, object? newValue)
    {
        base.OnDataContextChanged(oldValue, newValue);
        foreach (var item in _logicalItems.OfType<FrameworkElement>().ToArray())
            ReactivateBindingsRecursive(item);
    }

    public override void OnApplyTemplate()
    {
        ReleasePopupParts();
        base.OnApplyTemplate();
        _popup = GetTemplateChild("PART_Popup") as Popup;
        _popupBorder = GetTemplateChild("PART_PopupBorder") as Border;
        _popupScrollHost = GetTemplateChild("PART_ScrollHost") as MenuPopupScrollHost;
        if (_popup != null)
            _popup.Closed += OnPopupClosed;
        UpdatePopupChrome();
        UpdatePopupSettings();
        PopulatePopup();
        if (IsOpen && _popup != null)
        {
            _popup.Placement = _restoreOpen ? _savedPlacement : Placement;
            _popup.HorizontalOffset = _restoreOpen ? _savedHorizontalOffset : HorizontalOffset;
            _popup.VerticalOffset = _restoreOpen ? _savedVerticalOffset : VerticalOffset;
            _popup.IsOpen = true;
            _popupScrollHost?.PrepareForOpen();
        }

        if (_popup != null)
            _restoreOpen = false;
        UpdateVisualState();
    }

    internal override void OnTemplateContentClearing()
    {
        ReleasePopupParts();
        base.OnTemplateContentClearing();
    }

    private void ReleasePopupParts()
    {
        _popupAnimTimer?.Stop();
        _popupAnimTimer = null;
        if (_popup != null)
        {
            _restoreOpen = IsOpen;
            _savedPlacement = _popup.Placement;
            _savedHorizontalOffset = _popup.HorizontalOffset;
            _savedVerticalOffset = _popup.VerticalOffset;
            _popup.Closed -= OnPopupClosed;
            _popup.IsOpen = false;
        }

        _popupScrollHost?.Items.Clear();
        _popup = null;
        _popupBorder = null;
        _popupScrollHost = null;
    }

    private void UpdateVisualState() => VisualStateManager.GoToState(this, IsOpen ? "Open" : "Closed", false);
    private void UpdateContextOwner()
    {
        var previousContext = DataContext;
        var owner = PlacementTarget as FrameworkElement;
        if (ReferenceEquals(owner, _contextOwner))
            return;
        if (_contextOwner != null)
        {
            _contextOwner.DataContextChanged -= OnOwnerDataContextChanged;
            if (ReferenceEquals(LogicalParentInternal, _contextOwner))
                _contextOwner.RemoveLogicalChild(this);
            _contextOwner = null;
        }

        if (owner == null || LogicalParentInternal != null)
            return;
        for (Visual? node = owner; node != null; node = (node as FrameworkElement)?.LogicalParentInternal as Visual ?? node.VisualParent)
            if (ReferenceEquals(node, this))
                return;
        owner.AddLogicalChild(this);
        _contextOwner = owner;
        owner.DataContextChanged += OnOwnerDataContextChanged;
        if (GetValueSourceInternal(DataContextProperty).BaseValueSource is BaseValueSource.Default or BaseValueSource.Inherited && !ReferenceEquals(previousContext, DataContext))
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(DataContextProperty, previousContext, DataContext));
    }

    private void OnOwnerDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _contextOwner) && GetValueSourceInternal(DataContextProperty).BaseValueSource is BaseValueSource.Default or BaseValueSource.Inherited)
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(DataContextProperty, e.OldValue, DataContext));
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue)
        {
            SetCurrentValue(IsOpenProperty, false);
            CloseImmediately();
        }
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        foreach (var item in _logicalItems.ToArray())
            if (!Items.Cast<object>().Any(model => ReferenceEquals(model, item)) && !_popupEntries.Any(entry => ReferenceEquals(entry.Container, item) && Items.Cast<object>().Any(model => ReferenceEquals(model, entry.Item))))
            {
                RemoveLogicalChild(item);
                _logicalItems.Remove(item);
            }

        foreach (var item in Items.OfType<UIElement>())
        {
            AddLogicalChild(item);
            _logicalItems.Add(item);
        }

        foreach (var entry in _popupEntries)
            if (!Items.Cast<object>().Any(item => ReferenceEquals(item, entry.Item)) && entry.Container is MenuItem old && ReferenceEquals(old.OwnerMenu, this))
                old.OwnerMenu = null;
        foreach (var item in Items.OfType<MenuItem>())
            item.OwnerMenu = this;
        PopulatePopup();
    }

#endregion
    /// <inheritdoc/>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == PlacementTargetProperty)
            UpdateContextOwner();
        if (e.Property == BackgroundProperty || e.Property == BorderBrushProperty || e.Property == BorderThicknessProperty || e.Property == PaddingProperty || e.Property == CornerRadiusProperty)
        {
            UpdatePopupChrome();
        }

        if (e.Property == PlacementProperty || e.Property == HorizontalOffsetProperty || e.Property == VerticalOffsetProperty || e.Property == PlacementTargetProperty || e.Property == StaysOpenProperty || e.Property == CustomPopupPlacementCallbackProperty || e.Property == HasDropShadowProperty || e.Property == PlacementRectangleProperty)
        {
            UpdatePopupSettings();
        }
    }

#region Item Generation
    /// <inheritdoc/>
    protected override FrameworkElement GetContainerForItem(object item) => base.GetContainerForItem(item);
    /// <inheritdoc/>
    protected override bool IsItemItsOwnContainerOverride(object item) => item is MenuItem or Separator;
    /// <inheritdoc/>
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        if (element is MenuItem menuItem)
        {
            menuItem.OwnerMenu = this;
            if (!ReferenceEquals(element, item))
            {
                menuItem.Header ??= item;
                menuItem.HeaderTemplate = ResolveItemTemplate(item);
                menuItem.HeaderStringFormat = ItemStringFormat;
            }
        }

        if (!ReferenceEquals(element, item))
        {
            element.DataContext = item;
            var style = ItemContainerStyle ?? ItemContainerStyleSelector?.SelectStyle(item, this);
            if (style != null)
                element.Style = style;
        }
    }

#endregion
#region Opening/Closing
    /// <summary>
    /// Opens the context menu at the specified position.
    /// </summary>
    /// <param name = "position">The position to open at.</param>
    public void Open(Point position)
    {
        if (!IsEnabled)
            return;
        EnsurePopup();
        if (_popup == null)
        {
            _restoreOpen = true;
            _savedPlacement = PlacementMode.Absolute;
            _savedHorizontalOffset = position.X + HorizontalOffset;
            _savedVerticalOffset = position.Y + VerticalOffset;
            SetCurrentValue(IsOpenProperty, true);
            return;
        }

        // Use Absolute placement with explicit offsets for the given position
        _popup!.Placement = PlacementMode.Absolute;
        _popup.HorizontalOffset = position.X;
        _popup.VerticalOffset = position.Y;
        SetCurrentValue(IsOpenProperty, true);
    }

    protected override void OnEscapePressed()
    {
        base.OnEscapePressed();
        Close();
    }

    /// <summary>
    /// Closes the context menu.
    /// </summary>
    public void Close()
    {
        SetCurrentValue(IsOpenProperty, false);
    }

    // Window-manager operations require all popup input grabs to be released
    // before handoff. Logical Closed normally precedes the fade-out animation.
    internal void CloseImmediately()
    {
        SetCurrentValue(IsOpenProperty, false);
        _popupAnimTimer?.Stop();
        _popupAnimTimer = null;
        if (_popupBorder != null)
        {
            _popupBorder.Opacity = 1;
            _popupBorder.RenderOffset = default;
        }

        FinalizePopupClose();
    }

    /// <inheritdoc/>
    internal override void DismissMenu()
    {
        if (IsOpen)
        {
            IsOpen = false;
        }
    }

#endregion
#region Popup Management
    /// <summary>
    /// Ensures the internal <see cref = "Popup"/> is created and configured.
    /// </summary>
    private void EnsurePopup()
    {
        ApplyImplicitStyleIfNeeded();
        UpdateDefaultStyle();
        ApplyTemplate();
        UpdatePopupChrome();
    }

    private void PopulatePopup()
    {
        if (_popupScrollHost == null || _synchronizingPopup)
            return;
        _synchronizingPopup = true;
        try
        {
            var previous = _popupEntries.ToList();
            var next = new List<PopupEntry>();
            foreach (var item in Items)
            {
                var existing = previous.FirstOrDefault(entry => ReferenceEquals(entry.Item, item) || item is ValueType && Equals(entry.Item, item));
                var element = item as UIElement ?? existing?.Container;
                if (element == null)
                {
                    var generated = GetContainerForItem(item);
                    PrepareContainerForItem(generated, item);
                    element = generated;
                }

                if (existing != null)
                    previous.Remove(existing);
                if (element is MenuItem hosted)
                    hosted.OwnerMenu = this;
                AddLogicalChild(element);
                _logicalItems.Add(element);
                next.Add(new PopupEntry(item, element));
            }

            foreach (var entry in previous)
            {
                if (entry.Container is MenuItem removed && ReferenceEquals(removed.OwnerMenu, this))
                    removed.OwnerMenu = null;
                if (!next.Any(current => ReferenceEquals(current.Container, entry.Container)))
                {
                    RemoveLogicalChild(entry.Container);
                    _logicalItems.Remove(entry.Container);
                }
            }

            var items = _popupScrollHost.Items;
            items.BeginBatchUpdate();
            try
            {
                for (int i = items.Count - 1; i >= 0; i--)
                    if (!next.Any(entry => ReferenceEquals(entry.Container, items[i])))
                        items.RemoveAt(i);
                for (int i = 0; i < next.Count; i++)
                {
                    var element = next[i].Container;
                    if (i < items.Count && ReferenceEquals(items[i], element))
                        continue;
                    int oldIndex = items.IndexOf(element);
                    if (oldIndex >= 0)
                        items.RemoveAt(oldIndex);
                    else
                        element.DetachFromVisualParent();
                    items.Insert(i, element);
                }
            }
            finally
            {
                items.EndBatchUpdate();
            }

            _popupEntries.Clear();
            _popupEntries.AddRange(next);
            foreach (var entry in next)
                if (entry.Container is FrameworkElement element)
                    ReactivateBindingsRecursive(element);
        }
        finally
        {
            _synchronizingPopup = false;
        }
    }

    private void UpdatePopupChrome()
    {
        SetValue(PopupBackgroundPropertyKey, ResolvePopupBackgroundBrush());
        SetValue(PopupBorderBrushPropertyKey, ResolvePopupBorderBrush());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 二级及以后的子菜单是 <see cref = "MenuItem"/> 自己的弹出框，它们照这里给的造型建外框，
    /// 于是整条链跟着 ContextMenu 的 Style（圆角 14 / 内边距 5）走，而不是各写各的常量。
    /// </remarks>
    internal override MenuPopupChrome? GetPopupChrome() => BuildPopupChrome();
    private MenuPopupChrome BuildPopupChrome()
    {
        return new MenuPopupChrome(CornerRadius, Padding, BorderThickness, ResolvePopupBackgroundBrush(), ResolvePopupBorderBrush());
    }

    private Brush ResolvePopupBackgroundBrush()
    {
        return Background ?? TryFindResource("MenuFlyoutPresenterBackground") as Brush ?? TryFindResource("SurfaceBackground") as Brush ?? s_defaultBackgroundBrush;
    }

    private Brush ResolvePopupBorderBrush()
    {
        return BorderBrush ?? TryFindResource("MenuFlyoutPresenterBorderBrush") as Brush ?? TryFindResource("ControlBorder") as Brush ?? s_defaultBorderBrush;
    }

    private void UpdatePopupSettings()
    {
        if (_popup == null)
            return;
        _popup.PlacementTarget = PlacementTarget;
        _popup.StaysOpen = StaysOpen;
        _popup.CustomPopupPlacementCallback = CustomPopupPlacementCallback;
        _popup.PlacementRectangle = PlacementRectangle;
        _popup.AllowsTransparency = HasDropShadow;
        if (_popup.IsOpen)
        {
            _popup.Placement = Placement;
            _popup.HorizontalOffset = HorizontalOffset;
            _popup.VerticalOffset = VerticalOffset;
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _popup) && IsOpen)
        {
            SetCurrentValue(IsOpenProperty, false);
        }
    }

#endregion
#region Layout
    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        // The ContextMenu itself should not take any layout space.
        // All visual content is displayed via the Popup.
        return default(Size);
    }

#endregion
#region Property Changed Callbacks
    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContextMenu contextMenu)
        {
            var isOpen = (bool)e.NewValue!;
            contextMenu.UpdateVisualState();
            if (isOpen)
            {
                if (!contextMenu.IsEnabled)
                {
                    contextMenu.SetCurrentValue(IsOpenProperty, false);
                    return;
                }

                contextMenu.EnsurePopup();
                contextMenu.PopulatePopup();
                var popup = contextMenu._popup;
                if (popup == null)
                {
                    contextMenu._notifiedOpen = true;
                    contextMenu.OnOpened(new RoutedEventArgs(OpenedEvent, contextMenu));
                    return;
                }

                var wasExplicitPosition = popup.Placement == PlacementMode.Absolute;
                // Transfer placement properties to the popup.
                // When Open(Point) was called, it already set Absolute placement
                // and the offsets to the requested position — keep those.
                if (!wasExplicitPosition)
                {
                    popup.Placement = contextMenu.Placement;
                    popup.HorizontalOffset = contextMenu.HorizontalOffset;
                    popup.VerticalOffset = contextMenu.VerticalOffset;
                }
                else
                {
                    // Add the ContextMenu-level offsets on top of the explicit position
                    popup.HorizontalOffset += contextMenu.HorizontalOffset;
                    popup.VerticalOffset += contextMenu.VerticalOffset;
                }

                popup.PlacementTarget = contextMenu.PlacementTarget;
                popup.StaysOpen = contextMenu.StaysOpen;
                popup.CustomPopupPlacementCallback = contextMenu.CustomPopupPlacementCallback;
                popup.PlacementRectangle = contextMenu.PlacementRectangle;
                popup.AllowsTransparency = contextMenu.HasDropShadow;
                popup.IsOpen = true;
                contextMenu._popupScrollHost?.PrepareForOpen();
                contextMenu.AnimateOpen();
                contextMenu._notifiedOpen = true;
                contextMenu.OnOpened(new RoutedEventArgs(OpenedEvent, contextMenu));
            }
            else
            {
                if (contextMenu._notifiedOpen)
                {
                    contextMenu._notifiedOpen = false;
                    contextMenu.OnClosed(new RoutedEventArgs(ClosedEvent, contextMenu));
                }

                contextMenu.AnimateClose();
            }
        }
    }

    /// <summary>
    /// Raises the <see cref = "Opened"/> routed event.
    /// </summary>
    protected virtual void OnOpened(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref = "Closed"/> routed event.
    /// </summary>
    protected virtual void OnClosed(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

#endregion
#region Animation
    private void AnimateOpen()
    {
        _popupAnimTimer?.Stop();
        var target = _popupBorder;
        if (target == null)
            return;
        target.Opacity = 0;
        target.RenderOffset = new Point(0, PopupSlideOffsetY);
        _popup?.RequestHostRender();
        var startTick = Environment.TickCount64;
        _popupAnimTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
        };
        _popupAnimTimer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTick;
            var progress = Math.Clamp(elapsed / PopupOpenDurationMs, 0.0, 1.0);
            var eased = s_popupOpenEase.Ease(progress);
            target.Opacity = eased;
            target.RenderOffset = new Point(0, PopupSlideOffsetY * (1.0 - eased));
            if (progress >= 1.0)
            {
                target.Opacity = 1;
                target.RenderOffset = default;
                _popupAnimTimer?.Stop();
                _popupAnimTimer = null;
            }

            _popup?.RequestHostRender();
        };
        _popupAnimTimer.Start();
    }

    private void AnimateClose()
    {
        _popupAnimTimer?.Stop();
        // If the popup is already closed (e.g. via light-dismiss), finalize synchronously.
        if (_popup == null || _popupBorder == null || !_popup.IsOpen)
        {
            FinalizePopupClose();
            return;
        }

        var target = _popupBorder;
        var startOpacity = target.Opacity;
        var startOffsetY = target.RenderOffset.Y;
        var startTick = Environment.TickCount64;
        _popupAnimTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
        };
        _popupAnimTimer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTick;
            var progress = Math.Clamp(elapsed / PopupCloseDurationMs, 0.0, 1.0);
            var eased = s_popupCloseEase.Ease(progress);
            target.Opacity = startOpacity * (1.0 - eased);
            target.RenderOffset = new Point(0, startOffsetY + (PopupSlideOffsetY - startOffsetY) * eased);
            _popup?.RequestHostRender();
            if (progress >= 1.0)
            {
                _popupAnimTimer?.Stop();
                _popupAnimTimer = null;
                target.Opacity = 1;
                target.RenderOffset = default;
                FinalizePopupClose();
            }
        };
        _popupAnimTimer.Start();
    }

    private void FinalizePopupClose()
    {
        if (_popup == null)
            return;
        _popup.IsOpen = false;
        // Reset popup state so the next open uses the ContextMenu's own
        // Placement property instead of a stale Absolute from Open(Point).
        _popup.Placement = PlacementMode.MousePoint;
        _popup.HorizontalOffset = 0;
        _popup.VerticalOffset = 0;
    }
#endregion
} // Note: PlacementMode is defined in Popup.cs

