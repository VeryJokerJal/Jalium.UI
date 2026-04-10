using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a custom title bar control for windows.
/// </summary>
public class TitleBar : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Controls.Automation.TitleBarAutomationPeer(this);

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TitleBar),
            new PropertyMetadata("", OnTitleChanged));

    /// <summary>
    /// Identifies the IsMaximized dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsMaximizedProperty =
        DependencyProperty.Register(nameof(IsMaximized), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(false, OnIsMaximizedChanged));

    /// <summary>
    /// Identifies the ShowMinimizeButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowMinimizeButtonProperty =
        DependencyProperty.Register(nameof(ShowMinimizeButton), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnButtonVisibilityChanged));

    /// <summary>
    /// Identifies the ShowMaximizeButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowMaximizeButtonProperty =
        DependencyProperty.Register(nameof(ShowMaximizeButton), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnButtonVisibilityChanged));

    /// <summary>
    /// Identifies the ShowCloseButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowCloseButtonProperty =
        DependencyProperty.Register(nameof(ShowCloseButton), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnButtonVisibilityChanged));

    /// <summary>
    /// Identifies the LeftWindowCommands dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LeftWindowCommandsProperty =
        DependencyProperty.Register(nameof(LeftWindowCommands), typeof(FrameworkElement), typeof(TitleBar),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    /// <summary>
    /// Identifies the RightWindowCommands dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RightWindowCommandsProperty =
        DependencyProperty.Register(nameof(RightWindowCommands), typeof(FrameworkElement), typeof(TitleBar),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    /// <summary>
    /// Identifies the IsShowIcon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowIconProperty =
        DependencyProperty.Register(nameof(IsShowIcon), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnPresentationPropertyChanged));

    /// <summary>
    /// Identifies the IsShowTitle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowTitleProperty =
        DependencyProperty.Register(nameof(IsShowTitle), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnPresentationPropertyChanged));

    /// <summary>
    /// Identifies the WindowIcon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty WindowIconProperty =
        DependencyProperty.Register(nameof(WindowIcon), typeof(ImageSource), typeof(TitleBar),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the title displayed in the title bar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Title
    {
        get => (string)(GetValue(TitleProperty) ?? "");
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the window is maximized.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsMaximized
    {
        get => (bool)GetValue(IsMaximizedProperty)!;
        set => SetValue(IsMaximizedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the minimize button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool ShowMinimizeButton
    {
        get => (bool)GetValue(ShowMinimizeButtonProperty)!;
        set => SetValue(ShowMinimizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the maximize button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool ShowMaximizeButton
    {
        get => (bool)GetValue(ShowMaximizeButtonProperty)!;
        set => SetValue(ShowMaximizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the close button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty)!;
        set => SetValue(ShowCloseButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets the content rendered on the left side of the title bar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public FrameworkElement? LeftWindowCommands
    {
        get => (FrameworkElement?)GetValue(LeftWindowCommandsProperty);
        set => SetValue(LeftWindowCommandsProperty, value);
    }

    /// <summary>
    /// Gets or sets the content rendered on the right side of the title bar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public FrameworkElement? RightWindowCommands
    {
        get => (FrameworkElement?)GetValue(RightWindowCommandsProperty);
        set => SetValue(RightWindowCommandsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to display the window icon.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowIcon
    {
        get => (bool)GetValue(IsShowIconProperty)!;
        set => SetValue(IsShowIconProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to display the window title text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowTitle
    {
        get => (bool)GetValue(IsShowTitleProperty)!;
        set => SetValue(IsShowTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon displayed in the title bar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public ImageSource? WindowIcon
    {
        get => (ImageSource?)GetValue(WindowIconProperty);
        set => SetValue(WindowIconProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the minimize button is clicked.
    /// </summary>
    public event EventHandler? MinimizeClicked;

    /// <summary>
    /// Occurs when the maximize/restore button is clicked.
    /// </summary>
    public event EventHandler? MaximizeRestoreClicked;

    /// <summary>
    /// Occurs when the close button is clicked.
    /// </summary>
    public event EventHandler? CloseClicked;

    /// <summary>
    /// Raises the MinimizeClicked event.
    /// </summary>
    internal void RaiseMinimizeClicked() => MinimizeClicked?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raises the MaximizeRestoreClicked event.
    /// </summary>
    internal void RaiseMaximizeRestoreClicked() => MaximizeRestoreClicked?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raises the CloseClicked event.
    /// </summary>
    internal void RaiseCloseClicked() => CloseClicked?.Invoke(this, EventArgs.Empty);

    #endregion

    #region Fields

    private TitleBarButton? _minimizeButton;
    private TitleBarButton? _maximizeButton;
    private TitleBarButton? _closeButton;
    private readonly TitleBarButton _fallbackMinimizeButton;
    private readonly TitleBarButton _fallbackMaximizeButton;
    private readonly TitleBarButton _fallbackCloseButton;
    private FrameworkElement? _leftWindowCommandsHost;
    private FrameworkElement? _rightWindowCommandsHost;
    private FrameworkElement? _windowIconHost;
    private FrameworkElement? _titleTextHost;
    private bool _templateLookupAttempted;
    private bool _hasTemplateButtons;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleBar"/> class.
    /// </summary>
    public TitleBar()
    {
        Focusable = false;

        // Set default backdrop effect (Gaussian blur)
        BackdropEffect = new BlurEffect(20f);

        _fallbackMinimizeButton = new TitleBarButton { Kind = TitleBarButtonKind.Minimize };
        _fallbackMaximizeButton = new TitleBarButton { Kind = TitleBarButtonKind.Maximize };
        _fallbackCloseButton = new TitleBarButton { Kind = TitleBarButtonKind.Close };

        _fallbackMinimizeButton.Click += OnMinimizeButtonClick;
        _fallbackMaximizeButton.Click += OnMaximizeButtonClick;
        _fallbackCloseButton.Click += OnCloseButtonClick;

        // Register for mouse events
        AddHandler(MouseDownEvent, new Input.MouseButtonEventHandler(OnTitleBarMouseDown));
        AddHandler(MouseUpEvent, new Input.MouseButtonEventHandler(OnTitleBarMouseUp));
        AddHandler(MouseMoveEvent, new Input.MouseEventHandler(OnTitleBarMouseMove));

        UpdateMaximizeButtonKind();
        UpdateButtonVisibility();
    }

    #endregion

    #region Template Parts

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        DetachButtonHandlers();

        base.OnApplyTemplate();

        _templateLookupAttempted = true;
        _minimizeButton = GetTemplateChild("PART_MinimizeButton") as TitleBarButton;
        _maximizeButton = GetTemplateChild("PART_MaximizeButton") as TitleBarButton;
        _closeButton = GetTemplateChild("PART_CloseButton") as TitleBarButton;
        _hasTemplateButtons = _minimizeButton != null || _maximizeButton != null || _closeButton != null;
        _leftWindowCommandsHost = GetTemplateChild("PART_LeftWindowCommandsHost") as FrameworkElement;
        _rightWindowCommandsHost = GetTemplateChild("PART_RightWindowCommandsHost") as FrameworkElement;
        _windowIconHost = GetTemplateChild("PART_WindowIconHost") as FrameworkElement;
        _titleTextHost = GetTemplateChild("PART_TitleText") as FrameworkElement;

        if (_minimizeButton != null)
        {
            _minimizeButton.Kind = TitleBarButtonKind.Minimize;
            _minimizeButton.Click += OnMinimizeButtonClick;
        }

        if (_maximizeButton != null)
        {
            _maximizeButton.Kind = IsMaximized ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize;
            _maximizeButton.Click += OnMaximizeButtonClick;
        }

        if (_closeButton != null)
        {
            _closeButton.Kind = TitleBarButtonKind.Close;
            _closeButton.Click += OnCloseButtonClick;
        }

        UpdatePresentationElements();
        UpdateButtonVisibility();
    }

    private void DetachButtonHandlers()
    {
        if (_minimizeButton != null)
        {
            _minimizeButton.Click -= OnMinimizeButtonClick;
        }

        if (_maximizeButton != null)
        {
            _maximizeButton.Click -= OnMaximizeButtonClick;
        }

        if (_closeButton != null)
        {
            _closeButton.Click -= OnCloseButtonClick;
        }
    }

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
    {
        MinimizeClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnMaximizeButtonClick(object sender, RoutedEventArgs e)
    {
        MaximizeRestoreClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        CloseClicked?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Property Changed

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TitleBar titleBar)
        {
            titleBar.InvalidateMeasure();
        }
    }

    private static void OnIsMaximizedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TitleBar titleBar)
        {
            titleBar.UpdateMaximizeButtonKind();
        }
    }

    private static void OnButtonVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TitleBar titleBar)
        {
            titleBar.UpdateButtonVisibility();
            titleBar.InvalidateMeasure();
        }
    }

    private static void OnPresentationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TitleBar titleBar)
        {
            titleBar.UpdatePresentationElements();
            titleBar.InvalidateMeasure();
        }
    }

    private void UpdateMaximizeButtonKind()
    {
        EnsureButtonsInitialized();

        _fallbackMaximizeButton.Kind = IsMaximized ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize;

        if (_maximizeButton != null)
        {
            _maximizeButton.Kind = IsMaximized ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize;
        }
    }

    private void UpdateButtonVisibility()
    {
        EnsureButtonsInitialized();

        _fallbackMinimizeButton.Visibility = ShowMinimizeButton ? Visibility.Visible : Visibility.Collapsed;
        _fallbackMaximizeButton.Visibility = ShowMaximizeButton ? Visibility.Visible : Visibility.Collapsed;
        _fallbackCloseButton.Visibility = ShowCloseButton ? Visibility.Visible : Visibility.Collapsed;

        if (_minimizeButton != null)
            _minimizeButton.Visibility = ShowMinimizeButton ? Visibility.Visible : Visibility.Collapsed;
        if (_maximizeButton != null)
            _maximizeButton.Visibility = ShowMaximizeButton ? Visibility.Visible : Visibility.Collapsed;
        if (_closeButton != null)
            _closeButton.Visibility = ShowCloseButton ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePresentationElements()
    {
        EnsureButtonsInitialized();

        if (_leftWindowCommandsHost != null)
        {
            _leftWindowCommandsHost.Visibility = LeftWindowCommands != null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_rightWindowCommandsHost != null)
        {
            _rightWindowCommandsHost.Visibility = RightWindowCommands != null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_windowIconHost != null)
        {
            _windowIconHost.Visibility = IsShowIcon && WindowIcon != null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_titleTextHost != null)
        {
            _titleTextHost.Visibility = IsShowTitle
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void EnsureButtonsInitialized()
    {
        if (_hasTemplateButtons || _templateLookupAttempted)
        {
            return;
        }

        _templateLookupAttempted = true;
        if (Template != null)
        {
            ApplyTemplate();
        }
    }

    internal TitleBarButton? GetButtonByKind(TitleBarButtonKind kind)
    {
        EnsureButtonsInitialized();

        return kind switch
        {
            TitleBarButtonKind.Minimize => _minimizeButton ?? _fallbackMinimizeButton,
            TitleBarButtonKind.Maximize or TitleBarButtonKind.Restore => _maximizeButton ?? _fallbackMaximizeButton,
            TitleBarButtonKind.Close => _closeButton ?? _fallbackCloseButton,
            _ => null
        };
    }

    internal IEnumerable<TitleBarButton> EnumerateButtons()
    {
        EnsureButtonsInitialized();

        if (_hasTemplateButtons)
        {
            if (_minimizeButton != null)
                yield return _minimizeButton;

            if (_maximizeButton != null)
                yield return _maximizeButton;

            if (_closeButton != null)
                yield return _closeButton;
            yield break;
        }

        yield return _fallbackMinimizeButton;
        yield return _fallbackMaximizeButton;
        yield return _fallbackCloseButton;
    }

    internal bool IsPointInWindowCommands(Point localPoint)
    {
        EnsureButtonsInitialized();
        return IsElementHit(localPoint, _leftWindowCommandsHost) ||
               IsElementHit(localPoint, _rightWindowCommandsHost);
    }

    private static bool IsElementHit(Point localPoint, FrameworkElement? element)
    {
        if (element == null || element.Visibility != Visibility.Visible)
        {
            return false;
        }

        var bounds = element.VisualBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        return localPoint.X >= bounds.X &&
               localPoint.X < bounds.X + bounds.Width &&
               localPoint.Y >= bounds.Y &&
               localPoint.Y < bounds.Y + bounds.Height;
    }

    #endregion

    #region Input Handling

    private void OnTitleBarMouseDown(object sender, Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        // Handle double-click for maximize/restore
        // Note: Window dragging is handled by WM_NCHITTEST in Window class
        if (e.ClickCount == 2 && ShowMaximizeButton)
        {
            MaximizeRestoreClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnTitleBarMouseUp(object sender, Input.MouseButtonEventArgs e)
    {
        // Window dragging is handled by WM_NCHITTEST
    }

    private void OnTitleBarMouseMove(object sender, Input.MouseEventArgs e)
    {
        // Window dragging is handled by WM_NCHITTEST
    }

    #endregion
}
