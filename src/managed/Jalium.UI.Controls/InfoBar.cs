using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays a message to the user with an optional close button.
/// Used for showing informational, success, warning, or error messages.
/// </summary>
public class InfoBar : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.InfoBarAutomationPeer(this);
    }

    // Cached brushes for OnRender (per-severity)
    private static readonly SolidColorBrush s_whiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_infoBgBrush = new(Color.FromRgb(45, 45, 55));
    private static readonly SolidColorBrush s_infoIconBrush = new(Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush s_successBgBrush = new(Color.FromRgb(35, 55, 40));
    private static readonly SolidColorBrush s_successIconBrush = new(Color.FromRgb(16, 185, 129));
    private static readonly SolidColorBrush s_warningBgBrush = new(Color.FromRgb(55, 50, 35));
    private static readonly SolidColorBrush s_warningIconBrush = new(Color.FromRgb(245, 158, 11));
    private static readonly SolidColorBrush s_errorBgBrush = new(Color.FromRgb(55, 35, 40));
    private static readonly SolidColorBrush s_errorIconBrush = new(Color.FromRgb(239, 68, 68));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(InfoBar),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Message dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(InfoBar),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Severity dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar),
            new PropertyMetadata(InfoBarSeverity.Informational, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(InfoBar),
            new PropertyMetadata(true, OnIsOpenChanged));

    /// <summary>
    /// Identifies the IsClosable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsClosableProperty =
        DependencyProperty.Register(nameof(IsClosable), typeof(bool), typeof(InfoBar),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsIconVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsIconVisibleProperty =
        DependencyProperty.Register(nameof(IsIconVisible), typeof(bool), typeof(InfoBar),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ActionButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ActionButtonProperty =
        DependencyProperty.Register(nameof(ActionButton), typeof(ButtonBase), typeof(InfoBar),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Closed routed event.
    /// </summary>
    public static readonly RoutedEvent ClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(Closed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(InfoBar));

    /// <summary>
    /// Identifies the CloseButtonClick routed event.
    /// </summary>
    public static readonly RoutedEvent CloseButtonClickEvent =
        EventManager.RegisterRoutedEvent(nameof(CloseButtonClick), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(InfoBar));

    /// <summary>
    /// Occurs when the InfoBar is closed.
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

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the title text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the message text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>
    /// Gets or sets the severity level of the InfoBar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty)!;
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the InfoBar is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the InfoBar shows a close button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty)!;
        set => SetValue(IsClosableProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the severity icon is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsIconVisible
    {
        get => (bool)GetValue(IsIconVisibleProperty)!;
        set => SetValue(IsIconVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional action button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ButtonBase? ActionButton
    {
        get => (ButtonBase?)GetValue(ActionButtonProperty);
        set => SetValue(ActionButtonProperty, value);
    }

    #endregion

    #region Private Fields

    private const double IconSize = 20;
    private const double IconMargin = 12;
    private const double CloseButtonSize = 32;
    private new const double MinHeight = 48;
    private Rect _closeButtonRect;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="InfoBar"/> class.
    /// </summary>
    public InfoBar()
    {
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
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
                IsOpen = false;
            };
        }
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            var position = e.GetPosition(this);

            // Check if click is on close button
            if (IsClosable && _closeButtonRect.Contains(position))
            {
                RaiseEvent(new RoutedEventArgs(CloseButtonClickEvent, this));
                IsOpen = false;
                e.Handled = true;
            }
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

        // Calculate text heights
        if (!string.IsNullOrEmpty(Title))
        {
            var titleFormatted = new FormattedText(Title, FontFamily ?? "Segoe UI", (FontSize > 0 ? FontSize : 14) + 2);
            titleFormatted.FontWeight = 600;
            Interop.TextMeasurement.MeasureText(titleFormatted);
            height = Math.Max(height, titleFormatted.Height + padding.TotalHeight);
        }

        if (!string.IsNullOrEmpty(Message))
        {
            var messageFormatted = new FormattedText(Message, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14);
            Interop.TextMeasurement.MeasureText(messageFormatted);

            if (!string.IsNullOrEmpty(Title))
            {
                height += messageFormatted.Height + 4;
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
        // If using template, let the template handle rendering
        if (_rootBorder != null)
        {
            return;
        }

        if (drawingContext is not DrawingContext dc || !IsOpen)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var cornerRadius = CornerRadius;

        // Get severity brushes (cached) and colors (for sub-methods that need Color)
        var (severityBgBrush, severityIconBrush) = GetSeverityBrushes();
        var (_, fgColor, iconColor) = GetSeverityColors();

        // Draw background
        var bgBrush = Background ?? severityBgBrush;
        dc.DrawRoundedRectangle(bgBrush, null, rect, cornerRadius);

        // Draw left accent bar (only left corners have radius)
        var accentRect = new Rect(0, 0, 4, rect.Height);
        var accentCornerRadius = new CornerRadius(cornerRadius.TopLeft, 0, 0, cornerRadius.BottomLeft);
        dc.DrawRoundedRectangle(severityIconBrush, null, accentRect, accentCornerRadius);

        var currentX = padding.Left + 4; // After accent bar

        // Draw icon
        if (IsIconVisible)
        {
            DrawIcon(dc, currentX, (rect.Height - IconSize) / 2, iconColor);
            currentX += IconSize + IconMargin;
        }

        // Draw title and message
        var textBrush = Foreground ?? s_whiteBrush;
        var currentY = padding.Top;

        if (!string.IsNullOrEmpty(Title))
        {
            var titleFormatted = new FormattedText(Title, FontFamily ?? "Segoe UI", (FontSize > 0 ? FontSize : 14) + 2)
            {
                Foreground = textBrush,
                FontWeight = 600
            };
            Interop.TextMeasurement.MeasureText(titleFormatted);
            dc.DrawText(titleFormatted, new Point(currentX, currentY));
            currentY += titleFormatted.Height + 4;
        }

        if (!string.IsNullOrEmpty(Message))
        {
            var messageFormatted = new FormattedText(Message, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14)
            {
                Foreground = textBrush
            };
            Interop.TextMeasurement.MeasureText(messageFormatted);

            if (string.IsNullOrEmpty(Title))
            {
                currentY = (rect.Height - messageFormatted.Height) / 2;
            }

            dc.DrawText(messageFormatted, new Point(currentX, currentY));
        }

        // Draw close button
        if (IsClosable)
        {
            var closeX = rect.Width - CloseButtonSize - padding.Right;
            var closeY = (rect.Height - CloseButtonSize) / 2;
            _closeButtonRect = new Rect(closeX, closeY, CloseButtonSize, CloseButtonSize);

            DrawCloseButton(dc, closeX, closeY, fgColor);
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

        // Draw icon content based on severity
        switch (Severity)
        {
            case InfoBarSeverity.Informational:
                // Draw "i" for info
                dc.DrawLine(iconPen, new Point(centerX, centerY - 4), new Point(centerX, centerY - 3));
                dc.DrawLine(iconPen, new Point(centerX, centerY - 1), new Point(centerX, centerY + 4));
                break;

            case InfoBarSeverity.Success:
                // Draw checkmark
                dc.DrawLine(iconPen, new Point(centerX - 4, centerY), new Point(centerX - 1, centerY + 3));
                dc.DrawLine(iconPen, new Point(centerX - 1, centerY + 3), new Point(centerX + 4, centerY - 3));
                break;

            case InfoBarSeverity.Warning:
                // Draw "!" for warning
                dc.DrawLine(iconPen, new Point(centerX, centerY - 4), new Point(centerX, centerY + 1));
                dc.DrawLine(iconPen, new Point(centerX, centerY + 3), new Point(centerX, centerY + 4));
                break;

            case InfoBarSeverity.Error:
                // Draw "X" for error
                dc.DrawLine(iconPen, new Point(centerX - 3, centerY - 3), new Point(centerX + 3, centerY + 3));
                dc.DrawLine(iconPen, new Point(centerX + 3, centerY - 3), new Point(centerX - 3, centerY + 3));
                break;
        }
    }

    private void DrawCloseButton(DrawingContext dc, double x, double y, Color color)
    {
        var closeColor = Color.FromArgb(128, color.R, color.G, color.B);
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
            InfoBarSeverity.Informational => (
                ResolveSolidColorBrush("InfoBarInformationalBackground", s_infoBgBrush),
                ResolveSolidColorBrush("InfoBarInfoBrush", s_infoIconBrush)),
            InfoBarSeverity.Success => (
                ResolveSolidColorBrush("InfoBarSuccessBackground", s_successBgBrush),
                ResolveSolidColorBrush("InfoBarSuccessBrush", s_successIconBrush)),
            InfoBarSeverity.Warning => (
                ResolveSolidColorBrush("InfoBarWarningBackground", s_warningBgBrush),
                ResolveSolidColorBrush("InfoBarWarningBrush", s_warningIconBrush)),
            InfoBarSeverity.Error => (
                ResolveSolidColorBrush("InfoBarErrorBackground", s_errorBgBrush),
                ResolveSolidColorBrush("InfoBarErrorBrush", s_errorIconBrush)),
            _ => (
                ResolveSolidColorBrush("InfoBarInformationalBackground", s_infoBgBrush),
                ResolveSolidColorBrush("InfoBarInfoBrush", s_infoIconBrush))
        };
    }

    private (Color Background, Color Foreground, Color Icon) GetSeverityColors()
    {
        return Severity switch
        {
            InfoBarSeverity.Informational => (
                ResolveColor("InfoBarInformationalBackground", Color.FromRgb(45, 45, 55)),
                ResolveColor("InfoBarForeground", Color.White),
                ResolveColor("InfoBarInfoBrush", Color.FromRgb(0, 120, 212))),

            InfoBarSeverity.Success => (
                ResolveColor("InfoBarSuccessBackground", Color.FromRgb(35, 55, 40)),
                ResolveColor("InfoBarForeground", Color.White),
                ResolveColor("InfoBarSuccessBrush", Color.FromRgb(16, 185, 129))),

            InfoBarSeverity.Warning => (
                ResolveColor("InfoBarWarningBackground", Color.FromRgb(55, 50, 35)),
                ResolveColor("InfoBarForeground", Color.White),
                ResolveColor("InfoBarWarningBrush", Color.FromRgb(245, 158, 11))),

            InfoBarSeverity.Error => (
                ResolveColor("InfoBarErrorBackground", Color.FromRgb(55, 35, 40)),
                ResolveColor("InfoBarForeground", Color.White),
                ResolveColor("InfoBarErrorBrush", Color.FromRgb(239, 68, 68))),

            _ => (
                ResolveColor("InfoBarInformationalBackground", Color.FromRgb(45, 45, 55)),
                ResolveColor("InfoBarForeground", Color.White),
                ResolveColor("InfoBarInfoBrush", Color.FromRgb(0, 120, 212)))
        };
    }

    private SolidColorBrush ResolveSolidColorBrush(string resourceKey, SolidColorBrush fallback)
    {
        return TryFindResource(resourceKey) as SolidColorBrush ?? fallback;
    }

    private Color ResolveColor(string resourceKey, Color fallback)
    {
        return (TryFindResource(resourceKey) as SolidColorBrush)?.Color ?? fallback;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InfoBar infoBar)
        {
            infoBar.InvalidateMeasure();
            infoBar.InvalidateVisual();

            if ((bool)e.NewValue == false)
            {
                infoBar.RaiseEvent(new RoutedEventArgs(ClosedEvent, infoBar));
            }
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InfoBar infoBar)
        {
            infoBar.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InfoBar infoBar)
        {
            infoBar.InvalidateVisual();
        }
    }

    #endregion
}

/// <summary>
/// Specifies the severity level of an InfoBar.
/// </summary>
public enum InfoBarSeverity
{
    /// <summary>
    /// Informational message.
    /// </summary>
    Informational,

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
