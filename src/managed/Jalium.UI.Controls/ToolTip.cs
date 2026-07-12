using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a tooltip that displays information about an element.
/// </summary>
public class ToolTip : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ToolTipAutomationPeer(this);
    }

    #region Static Brushes

    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_defaultBorderBrush = new(Color.FromRgb(70, 70, 70));
    private static readonly SolidColorBrush s_defaultForegroundBrush = new(Color.FromRgb(240, 240, 240));

    #endregion

    private Popup? _popup;
    private DispatcherTimer? _showTimer;
    private DispatcherTimer? _hideTimer;

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(ToolTip),
            new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(PlacementMode), typeof(ToolTip),
            new PropertyMetadata(PlacementMode.Mouse));

    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(ToolTip),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(ToolTip),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the InitialShowDelay dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty InitialShowDelayProperty =
        DependencyProperty.Register(nameof(InitialShowDelay), typeof(int), typeof(ToolTip),
            new PropertyMetadata(400));

    /// <summary>
    /// Identifies the ShowDuration dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowDurationProperty =
        DependencyProperty.Register(nameof(ShowDuration), typeof(int), typeof(ToolTip),
            new PropertyMetadata(int.MaxValue));

    public static readonly DependencyProperty CustomPopupPlacementCallbackProperty =
        DependencyProperty.Register(
            nameof(CustomPopupPlacementCallback),
            typeof(CustomPopupPlacementCallback),
            typeof(ToolTip),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HasDropShadowProperty =
        ToolTipService.HasDropShadowProperty.AddOwner(typeof(ToolTip));

    public static readonly DependencyProperty PlacementRectangleProperty =
        ToolTipService.PlacementRectangleProperty.AddOwner(typeof(ToolTip));

    public static readonly DependencyProperty PlacementTargetProperty =
        ToolTipService.PlacementTargetProperty.AddOwner(typeof(ToolTip));

    public static readonly DependencyProperty ShowsToolTipOnKeyboardFocusProperty =
        ToolTipService.ShowsToolTipOnKeyboardFocusProperty.AddOwner(typeof(ToolTip));

    public static readonly DependencyProperty StaysOpenProperty =
        DependencyProperty.Register(nameof(StaysOpen), typeof(bool), typeof(ToolTip), new PropertyMetadata(true));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the tooltip is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets how the tooltip is positioned.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public PlacementMode Placement
    {
        get => (PlacementMode)GetValue(PlacementProperty)!;
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal offset from the placement position.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty)!;
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset from the placement position.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty)!;
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the time in milliseconds before the tooltip is shown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public int InitialShowDelay
    {
        get => (int)GetValue(InitialShowDelayProperty)!;
        set => SetValue(InitialShowDelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the time in milliseconds the tooltip remains visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public int ShowDuration
    {
        get => (int)GetValue(ShowDurationProperty)!;
        set => SetValue(ShowDurationProperty, value);
    }

    public CustomPopupPlacementCallback CustomPopupPlacementCallback
    {
        get => (CustomPopupPlacementCallback)GetValue(CustomPopupPlacementCallbackProperty)!;
        set => SetValue(CustomPopupPlacementCallbackProperty, value);
    }

    public bool HasDropShadow
    {
        get => (bool)(GetValue(HasDropShadowProperty) ?? false);
        set => SetValue(HasDropShadowProperty, value);
    }

    public Rect PlacementRectangle
    {
        get => (Rect)(GetValue(PlacementRectangleProperty) ?? Rect.Empty);
        set => SetValue(PlacementRectangleProperty, value);
    }

    public bool? ShowsToolTipOnKeyboardFocus
    {
        get => (bool?)GetValue(ShowsToolTipOnKeyboardFocusProperty);
        set => SetValue(ShowsToolTipOnKeyboardFocusProperty, value);
    }

    public bool StaysOpen
    {
        get => (bool)(GetValue(StaysOpenProperty) ?? true);
        set => SetValue(StaysOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the element relative to which the tooltip is positioned.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public UIElement? PlacementTarget
    {
        get => (UIElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the tooltip is opened.
    /// </summary>
    public static readonly RoutedEvent OpenedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(Opened),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(ToolTip));

    public event RoutedEventHandler Opened
    {
        add => AddHandler(OpenedEvent, value);
        remove => RemoveHandler(OpenedEvent, value);
    }

    /// <summary>
    /// Occurs when the tooltip is closed.
    /// </summary>
    public static readonly RoutedEvent ClosedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(Closed),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(ToolTip));

    public event RoutedEventHandler Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    #endregion

    static ToolTip()
    {
        // Register show/hide delegates with FrameworkElement.
        // MouseEnter/MouseLeave subscriptions are already handled in Core (OnToolTipPropertyChanged),
        // so there's no timing issue 闁?even if this static constructor runs late,
        // the delegates will be set before the user actually hovers.
        FrameworkElement.ToolTipShowRequested = OnToolTipShowRequested;
        FrameworkElement.ToolTipHideRequested = OnToolTipHideRequested;
    }

    private static void OnToolTipShowRequested(FrameworkElement element, RoutedEventArgs e)
    {
        var toolTipValue = element.ToolTip;
        if (toolTipValue != null)
        {
            var position = Point.Zero;
            if (e is Input.MouseEventArgs mouseArgs)
            {
                position = mouseArgs.Position;
            }
            ToolTipService.ShowToolTip(element, toolTipValue, position);
        }
    }

    private static void OnToolTipHideRequested(UIElement element)
    {
        ToolTipService.HideToolTip(element);
    }

    public ToolTip()
    {
        // ToolTip has a default ControlTemplate (from theme) with Border + ContentPresenter.
        // Content must be managed by the template's ContentPresenter, NOT directly by ContentControl.
        // Without this, ContentControl.AddVisualChild(Content) conflicts with ContentPresenter.AddVisualChild(Content).
        UseTemplateContentManagement();

        // Default styling 闁?overridden by theme implicit style when available
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolTip toolTip)
        {
            if ((bool)e.NewValue!)
            {
                toolTip.OpenToolTip();
            }
            else
            {
                toolTip.CloseToolTip();
            }
        }
    }

    private void OpenToolTip()
    {
        // Always create a fresh Popup to avoid stale state from previous show
        if (_popup != null)
        {
            _popup.IsOpen = false;
        }

        _popup = new Popup
        {
            Child = this,
            PlacementTarget = PlacementTarget,
            Placement = Placement,
            PlacementRectangle = PlacementRectangle,
            CustomPopupPlacementCallback = CustomPopupPlacementCallback,
            HorizontalOffset = HorizontalOffset + (PlacementRectangle.IsEmpty ? 12 : 0),
            VerticalOffset = VerticalOffset + (PlacementRectangle.IsEmpty ? 20 : 0),
            StaysOpen = StaysOpen,
            AllowsTransparency = true,
            PopupAnimation = SystemParameters.ToolTipPopupAnimation,
            ShouldConstrainToRootBounds = true, // Force overlay mode (simpler, avoids external window issues)
            IsHitTestVisible = false // Prevent tooltip overlay from stealing mouse events
        };

        _popup.IsOpen = true;
        var openedArgs = new RoutedEventArgs(OpenedEvent, this);
        OnOpened(openedArgs);

        // Start auto-hide timer
        StartHideTimer();
    }

    private void CloseToolTip()
    {
        StopTimers();

        if (_popup != null)
        {
            _popup.IsOpen = false;
        }

        var closedArgs = new RoutedEventArgs(ClosedEvent, this);
        OnClosed(closedArgs);
    }

    protected virtual void OnOpened(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    protected virtual void OnClosed(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    internal void StartShowTimer(Point mousePosition)
    {
        StopTimers();

        _showTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(InitialShowDelay) };
        _showTimer.Tick += (_, _) =>
        {
            _showTimer?.Stop(); // One-shot
            IsOpen = true;
        };
        _showTimer.Start();
    }

    private void StartHideTimer()
    {
        // int.MaxValue means "don't auto-hide" (matches WPF 4.8.1+ default)
        if (ShowDuration == int.MaxValue) return;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShowDuration) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop(); // One-shot
            IsOpen = false;
        };
        _hideTimer.Start();
    }

    internal void StopTimers()
    {
        if (_showTimer != null)
        {
            _showTimer.Stop();
            _showTimer = null;
        }
        if (_hideTimer != null)
        {
            _hideTimer.Stop();
            _hideTimer = null;
        }
    }

    // Layout and rendering are handled by the ControlTemplate (Border + ContentPresenter).
    // No custom MeasureOverride, ArrangeOverride, or OnRender needed.
}
/// <summary>
/// Provides static methods and attached properties for managing tooltips.
/// </summary>
public static class ToolTipService
{
    private static ToolTip? _currentToolTip;
    private static UIElement? _currentOwner;

    static ToolTipService()
    {
        // Press-and-hold on touch surfaces the tooltip, just as mouse hover does.
        // WindowInputDispatcher emits SystemGesture.HoldEnter ~500 ms after a
        // stationary touch contact; we handle it at class-handler scope so
        // every element with a ToolTip attached picks it up.
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.StylusSystemGestureEvent,
            new RoutedEventHandler(OnStylusSystemGestureClassHandler),
            handledEventsToo: false);
    }

    private static void OnStylusSystemGestureClassHandler(object sender, RoutedEventArgs e)
    {
        if (e.Handled || sender is not UIElement owner) return;
        if (e is not Input.StylusSystemGestureEventArgs gestureArgs) return;
        if (gestureArgs.SystemGesture != Input.SystemGesture.HoldEnter) return;
        // Only touch-initiated holds reveal the tooltip; pen and mouse use existing paths.
        object? toolTip = GetToolTip(owner);
        if (toolTip == null) return;
        var position = gestureArgs.StylusDevice.GetPosition(null);
        ShowToolTip(owner, toolTip, position);
        e.Handled = true;
    }

    #region Attached Properties

    /// <summary>Identifies the ToolTip attached dependency property.</summary>
    /// <remarks>
    /// Shares storage with <see cref="FrameworkElement.ToolTipProperty"/> via AddOwner so that
    /// <c>ToolTipService.SetToolTip(element, value)</c> and <c>element.ToolTip = value</c> are
    /// equivalent, and both trigger the MouseEnter/MouseLeave subscription that drives the
    /// tooltip popup. Registering a separate DP would silently swallow values set via the
    /// attached-property API.
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ToolTipProperty =
        FrameworkElement.ToolTipProperty.AddOwner(typeof(ToolTipService));

    /// <summary>Identifies the HorizontalOffset attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.RegisterAttached("HorizontalOffset", typeof(double), typeof(ToolTipService),
            new PropertyMetadata(0.0));

    /// <summary>Identifies the VerticalOffset attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached("VerticalOffset", typeof(double), typeof(ToolTipService),
            new PropertyMetadata(0.0));

    /// <summary>Identifies the HasDropShadow attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty HasDropShadowProperty =
        DependencyProperty.RegisterAttached("HasDropShadow", typeof(bool), typeof(ToolTipService),
            new PropertyMetadata(false));

    /// <summary>Identifies the PlacementTarget attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.RegisterAttached("PlacementTarget", typeof(UIElement), typeof(ToolTipService),
            new PropertyMetadata(null));

    /// <summary>Identifies the PlacementRectangle attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementRectangleProperty =
        DependencyProperty.RegisterAttached("PlacementRectangle", typeof(Rect), typeof(ToolTipService),
            new PropertyMetadata(Rect.Empty));

    /// <summary>Identifies the Placement attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.RegisterAttached("Placement", typeof(PlacementMode), typeof(ToolTipService),
            new PropertyMetadata(PlacementMode.Mouse));

    /// <summary>Identifies the ShowOnDisabled attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowOnDisabledProperty =
        DependencyProperty.RegisterAttached("ShowOnDisabled", typeof(bool), typeof(ToolTipService),
            new PropertyMetadata(false));

    /// <summary>Identifies the IsEnabled attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ToolTipService),
            new PropertyMetadata(true));

    /// <summary>Identifies the IsOpen attached dependency property (read-only).</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.RegisterAttached("IsOpen", typeof(bool), typeof(ToolTipService),
            new PropertyMetadata(false));

    /// <summary>Identifies the ShowDuration attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowDurationProperty =
        DependencyProperty.RegisterAttached("ShowDuration", typeof(int), typeof(ToolTipService),
            new PropertyMetadata(int.MaxValue));

    /// <summary>Identifies the InitialShowDelay attached dependency property.</summary>
    /// <remarks>
    /// Default matches <see cref="ToolTip.InitialShowDelayProperty"/> (400 ms). Keeping the
    /// two in sync means setting <c>ToolTipService.InitialShowDelay</c> on an element with no
    /// explicit value produces the same hover behaviour as leaving the instance property alone.
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty InitialShowDelayProperty =
        DependencyProperty.RegisterAttached("InitialShowDelay", typeof(int), typeof(ToolTipService),
            new PropertyMetadata(400));

    /// <summary>Identifies the BetweenShowDelay attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty BetweenShowDelayProperty =
        DependencyProperty.RegisterAttached("BetweenShowDelay", typeof(int), typeof(ToolTipService),
            new PropertyMetadata(100));

    /// <summary>Identifies the ShowsToolTipOnKeyboardFocus attached dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty ShowsToolTipOnKeyboardFocusProperty =
        DependencyProperty.RegisterAttached("ShowsToolTipOnKeyboardFocus", typeof(bool?), typeof(ToolTipService),
            new PropertyMetadata(null));

    #endregion

    #region Routed Events

    /// <summary>Identifies the ToolTipOpening routed event.</summary>
    public static readonly RoutedEvent ToolTipOpeningEvent =
        FrameworkElement.ToolTipOpeningEvent.AddOwner(typeof(ToolTipService));

    /// <summary>Identifies the ToolTipClosing routed event.</summary>
    public static readonly RoutedEvent ToolTipClosingEvent =
        FrameworkElement.ToolTipClosingEvent.AddOwner(typeof(ToolTipService));

    #endregion

    #region Get/Set Methods

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static object? GetToolTip(DependencyObject element) => element.GetValue(ToolTipProperty);
    public static void SetToolTip(DependencyObject element, object? value) => element.SetValue(ToolTipProperty, value);
    public static double GetHorizontalOffset(DependencyObject element) => (double)element.GetValue(HorizontalOffsetProperty)!;
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetHorizontalOffset(DependencyObject element, double value) => element.SetValue(HorizontalOffsetProperty, value);
    public static double GetVerticalOffset(DependencyObject element) => (double)element.GetValue(VerticalOffsetProperty)!;
    public static void SetVerticalOffset(DependencyObject element, double value) => element.SetValue(VerticalOffsetProperty, value);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static bool GetHasDropShadow(DependencyObject element) => (bool)element.GetValue(HasDropShadowProperty)!;
    public static void SetHasDropShadow(DependencyObject element, bool value) => element.SetValue(HasDropShadowProperty, value);
    public static UIElement? GetPlacementTarget(DependencyObject element) => (UIElement?)element.GetValue(PlacementTargetProperty);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetPlacementTarget(DependencyObject element, UIElement? value) => element.SetValue(PlacementTargetProperty, value);
    public static Rect GetPlacementRectangle(DependencyObject element) => (Rect)element.GetValue(PlacementRectangleProperty)!;
    public static void SetPlacementRectangle(DependencyObject element, Rect value) => element.SetValue(PlacementRectangleProperty, value);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static PlacementMode GetPlacement(DependencyObject element) => (PlacementMode)element.GetValue(PlacementProperty)!;
    public static void SetPlacement(DependencyObject element, PlacementMode value) => element.SetValue(PlacementProperty, value);
    public static bool GetShowOnDisabled(DependencyObject element) => (bool)element.GetValue(ShowOnDisabledProperty)!;
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetShowOnDisabled(DependencyObject element, bool value) => element.SetValue(ShowOnDisabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty)!;
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static bool GetIsOpen(DependencyObject element) => (bool)element.GetValue(IsOpenProperty)!;
    public static int GetShowDuration(DependencyObject element) => (int)element.GetValue(ShowDurationProperty)!;
    public static void SetShowDuration(DependencyObject element, int value) => element.SetValue(ShowDurationProperty, value);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static int GetInitialShowDelay(DependencyObject element) => (int)element.GetValue(InitialShowDelayProperty)!;
    public static void SetInitialShowDelay(DependencyObject element, int value) => element.SetValue(InitialShowDelayProperty, value);
    public static int GetBetweenShowDelay(DependencyObject element) => (int)element.GetValue(BetweenShowDelayProperty)!;
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetBetweenShowDelay(DependencyObject element, int value) => element.SetValue(BetweenShowDelayProperty, value);
    public static bool? GetShowsToolTipOnKeyboardFocus(DependencyObject element) => (bool?)element.GetValue(ShowsToolTipOnKeyboardFocusProperty);
    public static void SetShowsToolTipOnKeyboardFocus(DependencyObject element, bool? value) => element.SetValue(ShowsToolTipOnKeyboardFocusProperty, value);

    public static void AddToolTipOpeningHandler(DependencyObject element, ToolTipEventHandler handler)
    {
        if (element is UIElement uie) uie.AddHandler(ToolTipOpeningEvent, handler);
    }
    public static void RemoveToolTipOpeningHandler(DependencyObject element, ToolTipEventHandler handler)
    {
        if (element is UIElement uie) uie.RemoveHandler(ToolTipOpeningEvent, handler);
    }
    public static void AddToolTipClosingHandler(DependencyObject element, ToolTipEventHandler handler)
    {
        if (element is UIElement uie) uie.AddHandler(ToolTipClosingEvent, handler);
    }
    public static void RemoveToolTipClosingHandler(DependencyObject element, ToolTipEventHandler handler)
    {
        if (element is UIElement uie) uie.RemoveHandler(ToolTipClosingEvent, handler);
    }

    #endregion

    /// <summary>
    /// Cleans up all active tooltip timers. Called during application shutdown.
    /// </summary>
    internal static void Cleanup()
    {
        if (_currentToolTip != null)
        {
            _currentToolTip.StopTimers();
            _currentToolTip = null;
            _currentOwner = null;
        }
    }

    /// <summary>
    /// Shows a tooltip for the specified element.
    /// </summary>
    public static void ShowToolTip(UIElement owner, object content, Point mousePosition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);

        var openingArgs = new ToolTipEventArgs(ToolTipOpeningEvent)
        {
            Source = owner,
        };
        owner.RaiseEvent(openingArgs);
        if (openingArgs.Handled)
        {
            return;
        }

        // Close any existing tooltip
        HideToolTip(_currentOwner);

        _currentOwner = owner;

        // Create or get the tooltip
        if (content is ToolTip existingToolTip)
        {
            _currentToolTip = existingToolTip;
        }
        else
        {
            _currentToolTip = new ToolTip();

            // If content is a string, wrap it in TextBlock
            if (content is string text)
            {
                _currentToolTip.Content = new TextBlock { Text = text };
            }
            else if (content is UIElement uiContent)
            {
                _currentToolTip.Content = uiContent;
            }

            // Propagate ToolTipService attached values from the owner so the
            // attached-property API (e.g. ToolTipService.SetInitialShowDelay)
            // actually drives behaviour. A freshly constructed ToolTip would
            // otherwise always fall back to its own DP defaults, silently
            // dropping per-element overrides like a zero-delay hint.
            ApplyOwnerAttachedSettings(owner, _currentToolTip);
        }

        _currentToolTip.PlacementTarget = owner;
        _currentToolTip.StartShowTimer(mousePosition);
    }

    private static void ApplyOwnerAttachedSettings(UIElement owner, ToolTip toolTip)
    {
        toolTip.InitialShowDelay = GetInitialShowDelay(owner);
        toolTip.ShowDuration = GetShowDuration(owner);
        toolTip.Placement = GetPlacement(owner);
        toolTip.HorizontalOffset = GetHorizontalOffset(owner);
        toolTip.VerticalOffset = GetVerticalOffset(owner);
    }

    /// <summary>
    /// Hides the tooltip for the specified element.
    /// </summary>
    public static void HideToolTip(UIElement? owner)
    {
        if (owner != null && owner == _currentOwner && _currentToolTip != null)
        {
            var closingArgs = new ToolTipEventArgs(ToolTipClosingEvent)
            {
                Source = owner,
            };
            owner.RaiseEvent(closingArgs);

            _currentToolTip.StopTimers();
            _currentToolTip.IsOpen = false;
            _currentToolTip = null;
            _currentOwner = null;
        }
    }
}
