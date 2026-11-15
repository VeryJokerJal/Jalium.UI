using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the severity level of a toast notification.
/// </summary>
public enum ToastSeverity
{
    /// <summary>
    /// Informational message.
    /// </summary>
    Information,

    /// <summary>
    /// Success message.
    /// </summary>
    Success,

    /// <summary>
    /// Warning message.
    /// </summary>
    Warning,

    /// <summary>
    /// Error message.
    /// </summary>
    Error
}

/// <summary>
/// Specifies the position of the toast notification host within its parent.
/// </summary>
public enum ToastPosition
{
    /// <summary>
    /// Top-right corner (default).
    /// </summary>
    TopRight,

    /// <summary>
    /// Top-left corner.
    /// </summary>
    TopLeft,

    /// <summary>
    /// Top-center.
    /// </summary>
    TopCenter,

    /// <summary>
    /// Bottom-right corner.
    /// </summary>
    BottomRight,

    /// <summary>
    /// Bottom-left corner.
    /// </summary>
    BottomLeft,

    /// <summary>
    /// Bottom-center.
    /// </summary>
    BottomCenter
}

/// <summary>
/// Represents a single in-app toast notification that displays a transient message
/// with severity indicator, optional title, message, action button, and close button.
/// Automatically dismisses after a configurable timeout.
/// </summary>
public partial class ToastNotificationItem : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ToastNotificationItemAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ToastNotificationItem),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Message dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(ToastNotificationItem),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Severity dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(ToastSeverity), typeof(ToastNotificationItem),
            new PropertyMetadata(ToastSeverity.Information, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(ToastNotificationItem),
            new PropertyMetadata(true, OnIsOpenChanged));

    /// <summary>
    /// Identifies the IsClosable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsClosableProperty =
        DependencyProperty.Register(nameof(IsClosable), typeof(bool), typeof(ToastNotificationItem),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsIconVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsIconVisibleProperty =
        DependencyProperty.Register(nameof(IsIconVisible), typeof(bool), typeof(ToastNotificationItem),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ActionButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ActionButtonProperty =
        DependencyProperty.Register(nameof(ActionButton), typeof(ButtonBase), typeof(ToastNotificationItem),
            new PropertyMetadata(null, OnActionButtonChanged));

    /// <summary>
    /// Identifies the Duration dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(ToastNotificationItem),
            new PropertyMetadata(TimeSpan.FromSeconds(5), OnDurationChanged));

    /// <summary>
    /// Identifies the IsAutoDismissEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsAutoDismissEnabledProperty =
        DependencyProperty.Register(nameof(IsAutoDismissEnabled), typeof(bool), typeof(ToastNotificationItem),
            new PropertyMetadata(true, OnAutoDismissChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Closed routed event.
    /// </summary>
    public static readonly RoutedEvent ClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(Closed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ToastNotificationItem));

    /// <summary>
    /// Identifies the CloseButtonClick routed event.
    /// </summary>
    public static readonly RoutedEvent CloseButtonClickEvent =
        EventManager.RegisterRoutedEvent(nameof(CloseButtonClick), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ToastNotificationItem));

    /// <summary>
    /// Identifies the ActionButtonClick routed event.
    /// </summary>
    public static readonly RoutedEvent ActionButtonClickEvent =
        EventManager.RegisterRoutedEvent(nameof(ActionButtonClick), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ToastNotificationItem));

    /// <summary>
    /// Occurs when the toast is closed (dismissed).
    /// </summary>
    public event RoutedEventHandler Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    /// <summary>
    /// Occurs when the close button is clicked.
    /// </summary>
    public event RoutedEventHandler CloseButtonClick
    {
        add => AddHandler(CloseButtonClickEvent, value);
        remove => RemoveHandler(CloseButtonClickEvent, value);
    }

    /// <summary>
    /// Occurs when the action button is clicked.
    /// </summary>
    public event RoutedEventHandler ActionButtonClick
    {
        add => AddHandler(ActionButtonClickEvent, value);
        remove => RemoveHandler(ActionButtonClickEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the title text.
    /// </summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the message text.
    /// </summary>
    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>
    /// Gets or sets the severity level.
    /// </summary>
    public ToastSeverity Severity
    {
        get => (ToastSeverity)GetValue(SeverityProperty)!;
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the toast is visible.
    /// </summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the close button is visible.
    /// </summary>
    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty)!;
        set => SetValue(IsClosableProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the severity icon is visible.
    /// </summary>
    public bool IsIconVisible
    {
        get => (bool)GetValue(IsIconVisibleProperty)!;
        set => SetValue(IsIconVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional action button displayed in the toast.
    /// </summary>
    public ButtonBase? ActionButton
    {
        get => (ButtonBase?)GetValue(ActionButtonProperty);
        set => SetValue(ActionButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets the auto-dismiss duration. Default is 5 seconds.
    /// </summary>
    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty)!;
        set => SetValue(DurationProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the toast auto-dismisses after <see cref="Duration"/>.
    /// </summary>
    public bool IsAutoDismissEnabled
    {
        get => (bool)GetValue(IsAutoDismissEnabledProperty)!;
        set => SetValue(IsAutoDismissEnabledProperty, value);
    }

    #endregion

    #region Private Fields

    private DispatcherTimer? _autoDismissTimer;

    // Fade animation state
    private DispatcherTimer? _fadeTimer;
    private double _currentOpacity = 1.0;
    private bool _isFadingOut;

    #endregion

    #region Constructor

    public ToastNotificationItem()
    {
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UseTemplateContentManagement();
        DefaultStyleKey = typeof(ToastNotificationItem);
    }

    #endregion

    #region Lifecycle

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!IsOpen) return;
        // Start fade-in animation
        StartFadeIn();

        // Start auto-dismiss timer
        if (IsAutoDismissEnabled)
        {
            StartAutoDismissTimer();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAutoDismissTimer(); StopFadeTimer(); _isFadingOut = false;
    }

    #endregion

    #region Auto-Dismiss

    private void StartAutoDismissTimer()
    {
        StopAutoDismissTimer();
        if (!IsLoaded || !IsOpen || !IsAutoDismissEnabled || _isFadingOut || IsMouseOver) return;

        _autoDismissTimer = new DispatcherTimer
        {
            Interval = Duration
        };
        _autoDismissTimer.Tick += (s, e) =>
        {
            StopAutoDismissTimer();
            Dismiss();
        };
        _autoDismissTimer.Start();
    }

    private void StopAutoDismissTimer()
    {
        if (_autoDismissTimer != null)
        {
            _autoDismissTimer.Stop();
            _autoDismissTimer = null;
        }
    }

    /// <summary>
    /// Dismisses the toast with a fade-out animation.
    /// </summary>
    public void Dismiss()
    {
        if (!IsOpen || _isFadingOut)
            return;

        StopAutoDismissTimer();
        if (!IsLoaded)
        {
            SetCurrentValue(IsOpenProperty, false);
            return;
        }
        StartFadeOut();
    }

    #endregion

    private void StopFadeTimer()
    {
        _fadeTimer?.Stop(); _fadeTimer = null;
    }

    #region Fade Animations

    private void StartFadeIn()
    {
        _isFadingOut = false;
        _currentOpacity = 0.0;

        _fadeTimer?.Stop();
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _fadeTimer.Tick += (s, e) =>
        {
            _currentOpacity += 0.08;
            if (_currentOpacity >= 1.0)
            {
                _currentOpacity = 1.0;
                _fadeTimer?.Stop();
                _fadeTimer = null;
            }
            SetCurrentValue(OpacityProperty, _currentOpacity);
            InvalidateVisual();
        };
        _fadeTimer.Start();
        SetCurrentValue(OpacityProperty, 0d);
    }

    private void StartFadeOut()
    {
        _isFadingOut = true;

        _fadeTimer?.Stop();
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _fadeTimer.Tick += (s, e) =>
        {
            _currentOpacity -= 0.1;
            if (_currentOpacity <= 0.0)
            {
                _currentOpacity = 0.0;
                _isFadingOut = false;
                _fadeTimer?.Stop();
                _fadeTimer = null;
                SetCurrentValue(IsOpenProperty, false);
            }
            SetCurrentValue(OpacityProperty, _currentOpacity);
            InvalidateVisual();
        };
        _fadeTimer.Start();
    }

    #endregion

    #region Input Handling

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        // Pause auto-dismiss when hovering
        StopAutoDismissTimer();
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        // Resume auto-dismiss when mouse leaves
        if (IsAutoDismissEnabled && IsOpen && !_isFadingOut)
        {
            StartAutoDismissTimer();
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotificationItem toast)
        {
            toast.InvalidateMeasure();
            toast.InvalidateVisual();
            toast.UpdateVisualStates();

            if ((bool)e.NewValue! == false)
            {
                toast.StopAutoDismissTimer(); toast.StopFadeTimer(); toast._isFadingOut = false;
                toast.RaiseEvent(new RoutedEventArgs(ClosedEvent, toast));
            }
            else if (toast.IsLoaded)
            {
                toast.StartFadeIn(); toast.StartAutoDismissTimer();
            }
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotificationItem toast)
        {
            toast.InvalidateMeasure(); toast.UpdateVisualStates();
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotificationItem toast)
        {
            toast.InvalidateVisual(); toast.UpdateVisualStates();
        }
    }

    private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotificationItem toast && toast.IsAutoDismissEnabled && toast._autoDismissTimer != null)
        {
            toast.StartAutoDismissTimer();
        }
    }

    private static void OnAutoDismissChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotificationItem toast)
        {
            if ((bool)e.NewValue!)
            {
                toast.StartAutoDismissTimer();
            }
            else
            {
                toast.StopAutoDismissTimer();
            }
        }
    }

    #endregion
}
