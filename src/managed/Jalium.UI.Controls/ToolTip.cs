using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a tooltip that displays information about an element.
/// </summary>
public sealed class ToolTip : ContentControl
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_defaultBorderBrush = new(Color.FromRgb(70, 70, 70));
    private static readonly SolidColorBrush s_defaultForegroundBrush = new(Color.FromRgb(240, 240, 240));

    #endregion

    private Popup? _popup;
    private UIElement? _placementTarget;
    private DispatcherTimer? _showTimer;
    private DispatcherTimer? _hideTimer;

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(ToolTip),
            new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(PlacementMode), typeof(ToolTip),
            new PropertyMetadata(PlacementMode.Mouse));

    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(ToolTip),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(ToolTip),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the InitialShowDelay dependency property.
    /// </summary>
    public static readonly DependencyProperty InitialShowDelayProperty =
        DependencyProperty.Register(nameof(InitialShowDelay), typeof(int), typeof(ToolTip),
            new PropertyMetadata(400));

    /// <summary>
    /// Identifies the ShowDuration dependency property.
    /// </summary>
    public static readonly DependencyProperty ShowDurationProperty =
        DependencyProperty.Register(nameof(ShowDuration), typeof(int), typeof(ToolTip),
            new PropertyMetadata(int.MaxValue));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the tooltip is open.
    /// </summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets how the tooltip is positioned.
    /// </summary>
    public PlacementMode Placement
    {
        get => (PlacementMode)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal offset from the placement position.
    /// </summary>
    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset from the placement position.
    /// </summary>
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the time in milliseconds before the tooltip is shown.
    /// </summary>
    public int InitialShowDelay
    {
        get => (int)GetValue(InitialShowDelayProperty);
        set => SetValue(InitialShowDelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the time in milliseconds the tooltip remains visible.
    /// </summary>
    public int ShowDuration
    {
        get => (int)GetValue(ShowDurationProperty);
        set => SetValue(ShowDurationProperty, value);
    }

    /// <summary>
    /// Gets or sets the element relative to which the tooltip is positioned.
    /// </summary>
    public UIElement? PlacementTarget
    {
        get => _placementTarget;
        set => _placementTarget = value;
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the tooltip is opened.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>
    /// Occurs when the tooltip is closed.
    /// </summary>
    public event EventHandler? Closed;

    #endregion

    static ToolTip()
    {
        // Register show/hide delegates with FrameworkElement.
        // MouseEnter/MouseLeave subscriptions are already handled in Core (OnToolTipPropertyChanged),
        // so there's no timing issue — even if this static constructor runs late,
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

        // Default styling — overridden by theme implicit style when available
        Background = s_defaultBackgroundBrush;
        BorderBrush = s_defaultBorderBrush;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 4, 8, 4);
        Foreground = s_defaultForegroundBrush;
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolTip toolTip)
        {
            if ((bool)e.NewValue)
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
        System.Diagnostics.Debug.WriteLine($"[ToolTip] OpenToolTip: placementTarget={_placementTarget?.GetType().Name}");

        // Always create a fresh Popup to avoid stale state from previous show
        if (_popup != null)
        {
            _popup.IsOpen = false;
        }

        _popup = new Popup
        {
            Child = this,
            PlacementTarget = _placementTarget,
            Placement = Placement,
            HorizontalOffset = HorizontalOffset + 12, // Offset to the right of cursor
            VerticalOffset = VerticalOffset + 20, // Offset below cursor image (~20 DIPs for standard arrow cursor)
            StaysOpen = true, // ToolTip closing is managed by ToolTipService, not light-dismiss
            ShouldConstrainToRootBounds = true, // Force overlay mode (simpler, avoids external window issues)
            IsHitTestVisible = false // Prevent tooltip overlay from stealing mouse events
        };

        _popup.IsOpen = true;
        System.Diagnostics.Debug.WriteLine($"[ToolTip] Popup.IsOpen set to true");
        Opened?.Invoke(this, EventArgs.Empty);

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

        Closed?.Invoke(this, EventArgs.Empty);
    }

    internal void StartShowTimer(Point mousePosition)
    {
        StopTimers();

        System.Diagnostics.Debug.WriteLine($"[ToolTip] StartShowTimer: delay={InitialShowDelay}ms, content={Content?.GetType().Name}");

        _showTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(InitialShowDelay) };
        _showTimer.Tick += (_, _) =>
        {
            _showTimer?.Stop(); // One-shot
            System.Diagnostics.Debug.WriteLine($"[ToolTip] ShowTimer fired, setting IsOpen=true");
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

    #region Attached Properties

    /// <summary>Identifies the ToolTip attached dependency property.</summary>
    public static readonly DependencyProperty ToolTipProperty =
        DependencyProperty.RegisterAttached("ToolTip", typeof(object), typeof(ToolTipService),
            new PropertyMetadata(null));

    /// <summary>Identifies the HorizontalOffset attached dependency property.</summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.RegisterAttached("HorizontalOffset", typeof(double), typeof(ToolTipService),
            new PropertyMetadata(0.0));

    /// <summary>Identifies the VerticalOffset attached dependency property.</summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached("VerticalOffset", typeof(double), typeof(ToolTipService),
            new PropertyMetadata(0.0));

    /// <summary>Identifies the HasDropShadow attached dependency property.</summary>
    public static readonly DependencyProperty HasDropShadowProperty =
        DependencyProperty.RegisterAttached("HasDropShadow", typeof(bool), typeof(ToolTipService),
            new PropertyMetadata(false));

    /// <summary>Identifies the PlacementTarget attached dependency property.</summary>
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.RegisterAttached("PlacementTarget", typeof(UIElement), typeof(ToolTipService),
            new PropertyMetadata(null));

    /// <summary>Identifies the PlacementRectangle attached dependency property.</summary>
    public static readonly DependencyProperty PlacementRectangleProperty =
        DependencyProperty.RegisterAttached("PlacementRectangle", typeof(Rect), typeof(ToolTipService),
            new PropertyMetadata(Rect.Empty));

    /// <summary>Identifies the Placement attached dependency property.</summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.RegisterAttached("Placement", typeof(PlacementMode), typeof(ToolTipService),
            new PropertyMetadata(PlacementMode.Mouse));

    /// <summary>Identifies the ShowOnDisabled attached dependency property.</summary>
    public static readonly DependencyProperty ShowOnDisabledProperty =
        DependencyProperty.RegisterAttached("ShowOnDisabled", typeof(bool), typeof(ToolTipService),
            new PropertyMetadata(false));

    /// <summary>Identifies the IsEnabled attached dependency property.</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ToolTipService),
            new PropertyMetadata(true));

    /// <summary>Identifies the IsOpen attached dependency property (read-only).</summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.RegisterAttached("IsOpen", typeof(bool), typeof(ToolTipService),
            new PropertyMetadata(false));

    /// <summary>Identifies the ShowDuration attached dependency property.</summary>
    public static readonly DependencyProperty ShowDurationProperty =
        DependencyProperty.RegisterAttached("ShowDuration", typeof(int), typeof(ToolTipService),
            new PropertyMetadata(int.MaxValue));

    /// <summary>Identifies the InitialShowDelay attached dependency property.</summary>
    public static readonly DependencyProperty InitialShowDelayProperty =
        DependencyProperty.RegisterAttached("InitialShowDelay", typeof(int), typeof(ToolTipService),
            new PropertyMetadata(1000));

    /// <summary>Identifies the BetweenShowDelay attached dependency property.</summary>
    public static readonly DependencyProperty BetweenShowDelayProperty =
        DependencyProperty.RegisterAttached("BetweenShowDelay", typeof(int), typeof(ToolTipService),
            new PropertyMetadata(100));

    /// <summary>Identifies the ShowsToolTipOnKeyboardFocus attached dependency property.</summary>
    public static readonly DependencyProperty ShowsToolTipOnKeyboardFocusProperty =
        DependencyProperty.RegisterAttached("ShowsToolTipOnKeyboardFocus", typeof(bool?), typeof(ToolTipService),
            new PropertyMetadata(null));

    #endregion

    #region Routed Events

    /// <summary>Identifies the ToolTipOpening routed event.</summary>
    public static readonly RoutedEvent ToolTipOpeningEvent =
        new RoutedEvent("ToolTipOpening", RoutingStrategy.Direct, typeof(ToolTipEventHandler), typeof(ToolTipService));

    /// <summary>Identifies the ToolTipClosing routed event.</summary>
    public static readonly RoutedEvent ToolTipClosingEvent =
        new RoutedEvent("ToolTipClosing", RoutingStrategy.Direct, typeof(ToolTipEventHandler), typeof(ToolTipService));

    #endregion

    #region Get/Set Methods

    public static object? GetToolTip(DependencyObject element) => element.GetValue(ToolTipProperty);
    public static void SetToolTip(DependencyObject element, object? value) => element.SetValue(ToolTipProperty, value);
    public static double GetHorizontalOffset(DependencyObject element) => (double)element.GetValue(HorizontalOffsetProperty)!;
    public static void SetHorizontalOffset(DependencyObject element, double value) => element.SetValue(HorizontalOffsetProperty, value);
    public static double GetVerticalOffset(DependencyObject element) => (double)element.GetValue(VerticalOffsetProperty)!;
    public static void SetVerticalOffset(DependencyObject element, double value) => element.SetValue(VerticalOffsetProperty, value);
    public static bool GetHasDropShadow(DependencyObject element) => (bool)element.GetValue(HasDropShadowProperty)!;
    public static void SetHasDropShadow(DependencyObject element, bool value) => element.SetValue(HasDropShadowProperty, value);
    public static UIElement? GetPlacementTarget(DependencyObject element) => (UIElement?)element.GetValue(PlacementTargetProperty);
    public static void SetPlacementTarget(DependencyObject element, UIElement? value) => element.SetValue(PlacementTargetProperty, value);
    public static Rect GetPlacementRectangle(DependencyObject element) => (Rect)element.GetValue(PlacementRectangleProperty)!;
    public static void SetPlacementRectangle(DependencyObject element, Rect value) => element.SetValue(PlacementRectangleProperty, value);
    public static PlacementMode GetPlacement(DependencyObject element) => (PlacementMode)element.GetValue(PlacementProperty)!;
    public static void SetPlacement(DependencyObject element, PlacementMode value) => element.SetValue(PlacementProperty, value);
    public static bool GetShowOnDisabled(DependencyObject element) => (bool)element.GetValue(ShowOnDisabledProperty)!;
    public static void SetShowOnDisabled(DependencyObject element, bool value) => element.SetValue(ShowOnDisabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty)!;
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsOpen(DependencyObject element) => (bool)element.GetValue(IsOpenProperty)!;
    public static int GetShowDuration(DependencyObject element) => (int)element.GetValue(ShowDurationProperty)!;
    public static void SetShowDuration(DependencyObject element, int value) => element.SetValue(ShowDurationProperty, value);
    public static int GetInitialShowDelay(DependencyObject element) => (int)element.GetValue(InitialShowDelayProperty)!;
    public static void SetInitialShowDelay(DependencyObject element, int value) => element.SetValue(InitialShowDelayProperty, value);
    public static int GetBetweenShowDelay(DependencyObject element) => (int)element.GetValue(BetweenShowDelayProperty)!;
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
        }

        _currentToolTip.PlacementTarget = owner;
        _currentToolTip.StartShowTimer(mousePosition);
    }

    /// <summary>
    /// Hides the tooltip for the specified element.
    /// </summary>
    public static void HideToolTip(UIElement? owner)
    {
        if (owner == _currentOwner && _currentToolTip != null)
        {
            _currentToolTip.StopTimers();
            _currentToolTip.IsOpen = false;
            _currentToolTip = null;
            _currentOwner = null;
        }
    }
}

/// <summary>
/// Represents the method that handles routed events related to tooltip operations.
/// </summary>
public delegate void ToolTipEventHandler(object sender, ToolTipEventArgs e);

/// <summary>
/// Provides data for tooltip events.
/// </summary>
public sealed class ToolTipEventArgs : RoutedEventArgs
{
    public ToolTipEventArgs() { }
    public ToolTipEventArgs(RoutedEvent routedEvent) : base(routedEvent) { }
}
