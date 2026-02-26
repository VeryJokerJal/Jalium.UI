using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a custom title bar control for windows.
/// </summary>
public sealed class TitleBar : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TitleBar),
            new PropertyMetadata("", OnTitleChanged));

    /// <summary>
    /// Identifies the IsMaximized dependency property.
    /// </summary>
    public static readonly DependencyProperty IsMaximizedProperty =
        DependencyProperty.Register(nameof(IsMaximized), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(false, OnIsMaximizedChanged));

    /// <summary>
    /// Identifies the ShowMinimizeButton dependency property.
    /// </summary>
    public static readonly DependencyProperty ShowMinimizeButtonProperty =
        DependencyProperty.Register(nameof(ShowMinimizeButton), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnButtonVisibilityChanged));

    /// <summary>
    /// Identifies the ShowMaximizeButton dependency property.
    /// </summary>
    public static readonly DependencyProperty ShowMaximizeButtonProperty =
        DependencyProperty.Register(nameof(ShowMaximizeButton), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnButtonVisibilityChanged));

    /// <summary>
    /// Identifies the ShowCloseButton dependency property.
    /// </summary>
    public static readonly DependencyProperty ShowCloseButtonProperty =
        DependencyProperty.Register(nameof(ShowCloseButton), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnButtonVisibilityChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the title displayed in the title bar.
    /// </summary>
    public string Title
    {
        get => (string)(GetValue(TitleProperty) ?? "");
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the window is maximized.
    /// </summary>
    public bool IsMaximized
    {
        get => (bool)GetValue(IsMaximizedProperty)!;
        set => SetValue(IsMaximizedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the minimize button.
    /// </summary>
    public bool ShowMinimizeButton
    {
        get => (bool)GetValue(ShowMinimizeButtonProperty)!;
        set => SetValue(ShowMinimizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the maximize button.
    /// </summary>
    public bool ShowMaximizeButton
    {
        get => (bool)GetValue(ShowMaximizeButtonProperty)!;
        set => SetValue(ShowMaximizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the close button.
    /// </summary>
    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty)!;
        set => SetValue(ShowCloseButtonProperty, value);
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

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleBar"/> class.
    /// </summary>
    public TitleBar()
    {
        Height = 32;
        Focusable = false;

        // Set default backdrop effect (Gaussian blur)
        BackdropEffect = new BlurEffect(20f);

        CreateButtons();

        // Register for mouse events
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnTitleBarMouseDown));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnTitleBarMouseUp));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnTitleBarMouseMove));
    }

    private void CreateButtons()
    {
        _minimizeButton = new TitleBarButton
        {
            Kind = TitleBarButtonKind.Minimize
        };
        _minimizeButton.Click += (s, e) => MinimizeClicked?.Invoke(this, EventArgs.Empty);
        AddVisualChild(_minimizeButton);

        _maximizeButton = new TitleBarButton
        {
            Kind = TitleBarButtonKind.Maximize
        };
        _maximizeButton.Click += (s, e) => MaximizeRestoreClicked?.Invoke(this, EventArgs.Empty);
        AddVisualChild(_maximizeButton);

        _closeButton = new TitleBarButton
        {
            Kind = TitleBarButtonKind.Close
        };
        _closeButton.Click += (s, e) => CloseClicked?.Invoke(this, EventArgs.Empty);
        AddVisualChild(_closeButton);

        UpdateButtonVisibility();
    }

    #endregion

    #region Visual Tree

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            int count = 0;
            if (_minimizeButton != null && ShowMinimizeButton) count++;
            if (_maximizeButton != null && ShowMaximizeButton) count++;
            if (_closeButton != null && ShowCloseButton) count++;
            return count;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        var buttons = new List<TitleBarButton?>();
        if (_minimizeButton != null && ShowMinimizeButton) buttons.Add(_minimizeButton);
        if (_maximizeButton != null && ShowMaximizeButton) buttons.Add(_maximizeButton);
        if (_closeButton != null && ShowCloseButton) buttons.Add(_closeButton);

        if (index >= 0 && index < buttons.Count)
            return buttons[index];

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    #region Property Changed

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TitleBar titleBar)
        {
            titleBar.InvalidateVisual();
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

    private void UpdateMaximizeButtonKind()
    {
        if (_maximizeButton != null)
        {
            _maximizeButton.Kind = IsMaximized ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize;
        }
    }

    private void UpdateButtonVisibility()
    {
        if (_minimizeButton != null)
            _minimizeButton.Visibility = ShowMinimizeButton ? Visibility.Visible : Visibility.Collapsed;
        if (_maximizeButton != null)
            _maximizeButton.Visibility = ShowMaximizeButton ? Visibility.Visible : Visibility.Collapsed;
        if (_closeButton != null)
            _closeButton.Visibility = ShowCloseButton ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = Height;

        // Measure buttons
        _minimizeButton?.Measure(availableSize);
        _maximizeButton?.Measure(availableSize);
        _closeButton?.Measure(availableSize);

        return new Size(availableSize.Width, double.IsInfinity(height) ? 32 : height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Arrange buttons from right to left
        double buttonX = finalSize.Width;
        double buttonY = 0;

        // Close button (rightmost)
        if (_closeButton != null && ShowCloseButton)
        {
            buttonX -= _closeButton.Width;
            var closeRect = new Rect(buttonX, buttonY, _closeButton.Width, finalSize.Height);
            _closeButton.Arrange(closeRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Maximize button
        if (_maximizeButton != null && ShowMaximizeButton)
        {
            buttonX -= _maximizeButton.Width;
            var maxRect = new Rect(buttonX, buttonY, _maximizeButton.Width, finalSize.Height);
            _maximizeButton.Arrange(maxRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Minimize button
        if (_minimizeButton != null && ShowMinimizeButton)
        {
            buttonX -= _minimizeButton.Width;
            var minRect = new Rect(buttonX, buttonY, _minimizeButton.Width, finalSize.Height);
            _minimizeButton.Arrange(minRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    #endregion

    #region Input Handling

    private void OnTitleBarMouseDown(object sender, RoutedEventArgs e)
    {
        if (e is not MouseButtonEventArgs args)
            return;

        if (args.ChangedButton != MouseButton.Left)
            return;

        // Handle double-click for maximize/restore
        // Note: Window dragging is handled by WM_NCHITTEST in Window class
        if (args.ClickCount == 2 && ShowMaximizeButton)
        {
            MaximizeRestoreClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnTitleBarMouseUp(object sender, RoutedEventArgs e)
    {
        // Window dragging is handled by WM_NCHITTEST
    }

    private void OnTitleBarMouseMove(object sender, RoutedEventArgs e)
    {
        // Window dragging is handled by WM_NCHITTEST
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);

        // Draw backdrop effect (blur behind)
        if (BackdropEffect != null && BackdropEffect.HasEffect)
        {
            dc.DrawBackdropEffect(rect, BackdropEffect, CornerRadius);
        }

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        // Draw title text
        if (!string.IsNullOrEmpty(Title) && Foreground != null)
        {
            var fontMetrics = TextMeasurement.GetFontMetrics(FontFamily, FontSize);
            var formattedText = new FormattedText(Title, FontFamily, FontSize)
            {
                Foreground = Foreground
            };

            // Position title with left padding
            var textX = 12.0;
            var textY = (rect.Height - fontMetrics.LineHeight) / 2;

            dc.DrawText(formattedText, new Point(textX, textY));
        }
    }

    #endregion

}
