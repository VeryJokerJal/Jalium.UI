using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
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
public class ToastNotificationItem : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ToastNotificationItemAutomationPeer(this);
    }

    // Cached brushes per-severity (matching screenshot colors)
    private static readonly SolidColorBrush s_whiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_infoBgBrush = new(Color.FromRgb(0, 120, 212));    // Blue
    private static readonly SolidColorBrush s_infoIconBrush = new(Color.White);
    private static readonly SolidColorBrush s_successBgBrush = new(Color.FromRgb(16, 185, 129)); // Green
    private static readonly SolidColorBrush s_successIconBrush = new(Color.White);
    private static readonly SolidColorBrush s_warningBgBrush = new(Color.FromRgb(245, 158, 11)); // Orange
    private static readonly SolidColorBrush s_warningIconBrush = new(Color.White);
    private static readonly SolidColorBrush s_errorBgBrush = new(Color.FromRgb(220, 38, 38));    // Red
    private static readonly SolidColorBrush s_errorIconBrush = new(Color.White);

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
            new PropertyMetadata(null, OnLayoutPropertyChanged));

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

    private const double IconSize = 20;
    private const double IconMargin = 12;
    private const double CloseButtonSize = 32;
    private new const double MinHeight = 56;
    private Rect _closeButtonRect;
    private DispatcherTimer? _autoDismissTimer;

    // Fade animation state
    private DispatcherTimer? _fadeTimer;
    private double _currentOpacity = 1.0;
    private bool _isFadingIn;
    private bool _isFadingOut;

    #endregion

    #region Constructor

    public ToastNotificationItem()
    {
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));

        Loaded += OnLoaded;
    }

    #endregion

    #region Template Parts

    private Border? _rootBorder;
    private Button? _closeButton;

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _rootBorder = GetTemplateChild("RootBorder") as Border;
        _closeButton = GetTemplateChild("PART_CloseButton") as Button;

        if (_closeButton != null)
        {
            _closeButton.Click += (s, e) =>
            {
                RaiseEvent(new RoutedEventArgs(CloseButtonClickEvent, this));
                Dismiss();
            };
        }
    }

    #endregion

    #region Lifecycle

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Start fade-in animation
        StartFadeIn();

        // Start auto-dismiss timer
        if (IsAutoDismissEnabled)
        {
            StartAutoDismissTimer();
        }
    }

    #endregion

    #region Auto-Dismiss

    private void StartAutoDismissTimer()
    {
        StopAutoDismissTimer();

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
        if (_isFadingOut)
            return;

        StopAutoDismissTimer();
        StartFadeOut();
    }

    #endregion

    #region Fade Animations

    private void StartFadeIn()
    {
        _isFadingIn = true;
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
                _isFadingIn = false;
                _fadeTimer?.Stop();
                _fadeTimer = null;
            }
            Opacity = _currentOpacity;
            InvalidateVisual();
        };
        _fadeTimer.Start();
        Opacity = 0;
    }

    private void StartFadeOut()
    {
        _isFadingOut = true;
        _isFadingIn = false;

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
                IsOpen = false;
            }
            Opacity = _currentOpacity;
            InvalidateVisual();
        };
        _fadeTimer.Start();
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            var position = e.GetPosition(this);

            if (IsClosable && _closeButtonRect.Contains(position))
            {
                RaiseEvent(new RoutedEventArgs(CloseButtonClickEvent, this));
                Dismiss();
                e.Handled = true;
            }
        }
    }

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

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!IsOpen)
        {
            return Size.Empty;
        }

        var padding = Padding;
        var width = availableSize.Width;
        var height = MinHeight;

        if (!string.IsNullOrEmpty(Title))
        {
            var titleFormatted = new FormattedText(Title, FontFamily ?? FrameworkElement.DefaultFontFamilyName, (FontSize > 0 ? FontSize : 14) + 2);
            titleFormatted.FontWeight = 600;
            Interop.TextMeasurement.MeasureText(titleFormatted);
            height = Math.Max(height, titleFormatted.Height + padding.TotalHeight + 8);
        }

        if (!string.IsNullOrEmpty(Message))
        {
            var messageFormatted = new FormattedText(Message, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14);
            Interop.TextMeasurement.MeasureText(messageFormatted);

            if (!string.IsNullOrEmpty(Title))
            {
                height += messageFormatted.Height + 2;
            }
            else
            {
                height = Math.Max(height, messageFormatted.Height + padding.TotalHeight);
            }
        }

        return new Size(width, height);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (_rootBorder != null)
            return;

        if (drawingContext is not DrawingContext dc || !IsOpen)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var cornerRadius = CornerRadius;

        // Get severity brushes
        var (bgBrush, iconBrush) = GetSeverityBrushes();

        // Draw background
        var background = Background ?? bgBrush;
        dc.DrawRoundedRectangle(background, null, rect, cornerRadius);

        var currentX = padding.Left + 12;

        // Draw icon
        if (IsIconVisible)
        {
            var iconY = !string.IsNullOrEmpty(Title) && !string.IsNullOrEmpty(Message)
                ? padding.Top + 10
                : (rect.Height - IconSize) / 2;
            DrawIcon(dc, currentX, iconY, iconBrush.Color);
            currentX += IconSize + IconMargin;
        }

        // Draw title and message
        var textBrush = Foreground ?? s_whiteBrush;
        var textStartX = currentX;
        var currentY = padding.Top + 8;

        if (!string.IsNullOrEmpty(Title))
        {
            var titleFormatted = new FormattedText(Title, FontFamily ?? FrameworkElement.DefaultFontFamilyName, (FontSize > 0 ? FontSize : 14) + 2)
            {
                Foreground = textBrush,
                FontWeight = 700
            };
            Interop.TextMeasurement.MeasureText(titleFormatted);
            dc.DrawText(titleFormatted, new Point(textStartX, currentY));
            currentY += titleFormatted.Height + 2;
        }

        if (!string.IsNullOrEmpty(Message))
        {
            var messageFormatted = new FormattedText(Message, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 13)
            {
                Foreground = textBrush
            };
            Interop.TextMeasurement.MeasureText(messageFormatted);

            if (string.IsNullOrEmpty(Title))
            {
                currentY = (rect.Height - messageFormatted.Height) / 2;
            }

            dc.DrawText(messageFormatted, new Point(textStartX, currentY));
        }

        // Draw close button
        if (IsClosable)
        {
            var closeX = rect.Width - CloseButtonSize - padding.Right - 4;
            var closeY = (rect.Height - CloseButtonSize) / 2;
            _closeButtonRect = new Rect(closeX, closeY, CloseButtonSize, CloseButtonSize);

            DrawCloseButton(dc, closeX, closeY, (textBrush as SolidColorBrush)?.Color ?? Color.White);
        }
    }

    private Pen? _iconPen;
    private Color _iconPenColor;
    private Pen? _closePen;
    private Color _closePenColor;

    private void DrawIcon(DrawingContext dc, double x, double y, Color color)
    {
        if (_iconPen == null || _iconPenColor != color)
        {
            _iconPenColor = color;
            _iconPen = new Pen(new SolidColorBrush(color), 2);
        }
        var iconPen = _iconPen;
        var centerX = x + IconSize / 2;
        var centerY = y + IconSize / 2;
        var radius = IconSize / 2 - 2;

        // Draw circle
        dc.DrawEllipse(null, iconPen, new Point(centerX, centerY), radius, radius);

        switch (Severity)
        {
            case ToastSeverity.Information:
                // "i" icon
                dc.DrawLine(iconPen, new Point(centerX, centerY - 4), new Point(centerX, centerY - 3));
                dc.DrawLine(iconPen, new Point(centerX, centerY - 1), new Point(centerX, centerY + 4));
                break;

            case ToastSeverity.Success:
                // Checkmark
                dc.DrawLine(iconPen, new Point(centerX - 4, centerY), new Point(centerX - 1, centerY + 3));
                dc.DrawLine(iconPen, new Point(centerX - 1, centerY + 3), new Point(centerX + 4, centerY - 3));
                break;

            case ToastSeverity.Warning:
                // "!" icon
                dc.DrawLine(iconPen, new Point(centerX, centerY - 4), new Point(centerX, centerY + 1));
                dc.DrawLine(iconPen, new Point(centerX, centerY + 3), new Point(centerX, centerY + 4));
                break;

            case ToastSeverity.Error:
                // "X" icon
                dc.DrawLine(iconPen, new Point(centerX - 3, centerY - 3), new Point(centerX + 3, centerY + 3));
                dc.DrawLine(iconPen, new Point(centerX + 3, centerY - 3), new Point(centerX - 3, centerY + 3));
                break;
        }
    }

    private void DrawCloseButton(DrawingContext dc, double x, double y, Color color)
    {
        var closeColor = Color.FromArgb(180, color.R, color.G, color.B);
        if (_closePen == null || _closePenColor != closeColor)
        {
            _closePenColor = closeColor;
            _closePen = new Pen(new SolidColorBrush(closeColor), 1.5);
        }
        var closePen = _closePen;

        var centerX = x + CloseButtonSize / 2;
        var centerY = y + CloseButtonSize / 2;
        var size = 5;

        dc.DrawLine(closePen, new Point(centerX - size, centerY - size), new Point(centerX + size, centerY + size));
        dc.DrawLine(closePen, new Point(centerX + size, centerY - size), new Point(centerX - size, centerY + size));
    }

    private (SolidColorBrush Background, SolidColorBrush Icon) GetSeverityBrushes()
    {
        return Severity switch
        {
            ToastSeverity.Information => (
                ResolveSolidColorBrush("ToastInformationBackground", s_infoBgBrush),
                ResolveSolidColorBrush("ToastInformationIcon", s_infoIconBrush)),
            ToastSeverity.Success => (
                ResolveSolidColorBrush("ToastSuccessBackground", s_successBgBrush),
                ResolveSolidColorBrush("ToastSuccessIcon", s_successIconBrush)),
            ToastSeverity.Warning => (
                ResolveSolidColorBrush("ToastWarningBackground", s_warningBgBrush),
                ResolveSolidColorBrush("ToastWarningIcon", s_warningIconBrush)),
            ToastSeverity.Error => (
                ResolveSolidColorBrush("ToastErrorBackground", s_errorBgBrush),
                ResolveSolidColorBrush("ToastErrorIcon", s_errorIconBrush)),
            _ => (
                ResolveSolidColorBrush("ToastInformationBackground", s_infoBgBrush),
                ResolveSolidColorBrush("ToastInformationIcon", s_infoIconBrush))
        };
    }

    private SolidColorBrush ResolveSolidColorBrush(string resourceKey, SolidColorBrush fallback)
    {
        return TryFindResource(resourceKey) as SolidColorBrush ?? fallback;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotificationItem toast)
        {
            toast.InvalidateMeasure();
            toast.InvalidateVisual();

            if ((bool)e.NewValue == false)
            {
                toast.StopAutoDismissTimer();
                toast.RaiseEvent(new RoutedEventArgs(ClosedEvent, toast));
            }
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotificationItem toast)
        {
            toast.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotificationItem toast)
        {
            toast._iconPen = null; // Reset cached pen on severity change
            toast.InvalidateVisual();
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
            if ((bool)e.NewValue)
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
