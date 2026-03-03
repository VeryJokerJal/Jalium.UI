using System.Runtime.InteropServices;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Interop;
using Jalium.UI.Media;
#if DEBUG
using Jalium.UI.Controls.DevTools;
#endif

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a window in the Jalium.UI framework.
/// </summary>
public partial class Window : ContentControl, IWindowHost, ILayoutManagerHost
{
    private readonly LayoutManager _layoutManager = new();
    private double _dpiScale = 1.0;
    private Dispatcher? _dispatcher; // UI thread Dispatcher, captured in Show()
    private volatile bool _renderScheduled;   // True when a Dispatcher-based render is pending
    private bool _isRendering;       // True during RenderFrame execution
    private volatile bool _renderRequested;   // True if InvalidateWindow was called during rendering
    private volatile bool _dirtyBetweenFrames; // True if InvalidateWindow was blocked between frames
    private bool _isFirstLayout = true;
    private bool _fullInvalidation = true;  // First frame is always full
    // Maps dirty element → pre-layout bounds (captured when AddDirtyElement is called).
    // After UpdateLayout, we also compute post-layout bounds.
    // Both are submitted as dirty rects so vacated areas (FLIP_SEQUENTIAL) are repainted.
    private readonly Dictionary<UIElement, Rect> _dirtyElements = new();
    private readonly object _dirtyLock = new(); // Protects _dirtyElements from cross-thread access
    private int _appliedDwmTopMarginPhysical = -1;
    private bool _attemptedAutoWindowIcon;
    private const double DefaultTitleBarHeightDip = 32.0;

    /// <summary>
    /// Gets the layout manager for this window.
    /// </summary>
    LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Window),
            new PropertyMetadata("Window", OnTitleChanged));

    /// <summary>
    /// Identifies the WindowState dependency property.
    /// </summary>
    public static readonly DependencyProperty WindowStateProperty =
        DependencyProperty.Register(nameof(WindowState), typeof(WindowState), typeof(Window),
            new PropertyMetadata(WindowState.Normal, OnWindowStateChanged));

    /// <summary>
    /// Identifies the TitleBarStyle dependency property.
    /// </summary>
    public static readonly DependencyProperty TitleBarStyleProperty =
        DependencyProperty.Register(nameof(TitleBarStyle), typeof(WindowTitleBarStyle), typeof(Window),
            new PropertyMetadata(WindowTitleBarStyle.Custom, OnTitleBarStyleChanged));

    /// <summary>
    /// Identifies the SystemBackdrop dependency property.
    /// </summary>
    public static readonly DependencyProperty SystemBackdropProperty =
        DependencyProperty.Register(nameof(SystemBackdrop), typeof(WindowBackdropType), typeof(Window),
            new PropertyMetadata(WindowBackdropType.None, OnSystemBackdropChanged));

    /// <summary>
    /// Identifies the Topmost dependency property.
    /// </summary>
    public static readonly DependencyProperty TopmostProperty =
        DependencyProperty.Register(nameof(Topmost), typeof(bool), typeof(Window),
            new PropertyMetadata(false, OnTopmostChanged));

    /// <summary>
    /// Identifies the SizeToContent dependency property.
    /// </summary>
    public static readonly DependencyProperty SizeToContentProperty =
        DependencyProperty.Register(nameof(SizeToContent), typeof(SizeToContent), typeof(Window),
            new PropertyMetadata(SizeToContent.Manual));

    /// <summary>
    /// Identifies the ResizeMode dependency property.
    /// </summary>
    public static readonly DependencyProperty ResizeModeProperty =
        DependencyProperty.Register(nameof(ResizeMode), typeof(ResizeMode), typeof(Window),
            new PropertyMetadata(ResizeMode.CanResize));

    /// <summary>
    /// Identifies the WindowStyle dependency property.
    /// </summary>
    public static readonly DependencyProperty WindowStyleProperty =
        DependencyProperty.Register(nameof(WindowStyle), typeof(WindowStyle), typeof(Window),
            new PropertyMetadata(WindowStyle.SingleBorderWindow));

    /// <summary>
    /// Identifies the LeftWindowCommands dependency property.
    /// </summary>
    public static readonly DependencyProperty LeftWindowCommandsProperty =
        DependencyProperty.Register(nameof(LeftWindowCommands), typeof(FrameworkElement), typeof(Window),
            new PropertyMetadata(null, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the RightWindowCommands dependency property.
    /// </summary>
    public static readonly DependencyProperty RightWindowCommandsProperty =
        DependencyProperty.Register(nameof(RightWindowCommands), typeof(FrameworkElement), typeof(Window),
            new PropertyMetadata(null, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowIcon dependency property.
    /// </summary>
    public static readonly DependencyProperty IsShowIconProperty =
        DependencyProperty.Register(nameof(IsShowIcon), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowTitle dependency property.
    /// </summary>
    public static readonly DependencyProperty IsShowTitleProperty =
        DependencyProperty.Register(nameof(IsShowTitle), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowTitleBar dependency property.
    /// </summary>
    public static readonly DependencyProperty IsShowTitleBarProperty =
        DependencyProperty.Register(nameof(IsShowTitleBar), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowMinimizeButton dependency property.
    /// </summary>
    public static readonly DependencyProperty IsShowMinimizeButtonProperty =
        DependencyProperty.Register(nameof(IsShowMinimizeButton), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowMaximizeButton dependency property.
    /// </summary>
    public static readonly DependencyProperty IsShowMaximizeButtonProperty =
        DependencyProperty.Register(nameof(IsShowMaximizeButton), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowCloseButton dependency property.
    /// </summary>
    public static readonly DependencyProperty IsShowCloseButtonProperty =
        DependencyProperty.Register(nameof(IsShowCloseButton), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the TitleBarHeight dependency property.
    /// </summary>
    public static readonly DependencyProperty TitleBarHeightProperty =
        DependencyProperty.Register(nameof(TitleBarHeight), typeof(double), typeof(Window),
            new PropertyMetadata(DefaultTitleBarHeightDip, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the WindowIcon dependency property.
    /// </summary>
    public static readonly DependencyProperty WindowIconProperty =
        DependencyProperty.Register(nameof(WindowIcon), typeof(ImageSource), typeof(Window),
            new PropertyMetadata(null, OnWindowTitleBarPresentationChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    public string Title
    {
        get => (string)(GetValue(TitleProperty) ?? "Window");
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the window state.
    /// </summary>
    public WindowState WindowState
    {
        get => (WindowState)GetValue(WindowStateProperty)!;
        set => SetValue(WindowStateProperty, value);
    }

    /// <summary>
    /// Gets or sets the title bar style.
    /// </summary>
    public WindowTitleBarStyle TitleBarStyle
    {
        get => (WindowTitleBarStyle)(GetValue(TitleBarStyleProperty) ?? WindowTitleBarStyle.Custom);
        set => SetValue(TitleBarStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the system backdrop type for the window.
    /// This blurs content behind the window (desktop, other applications) using Windows DWM APIs.
    /// Requires Windows 11 22H2+ for Mica and Acrylic effects.
    /// </summary>
    public WindowBackdropType SystemBackdrop
    {
        get => (WindowBackdropType)(GetValue(SystemBackdropProperty) ?? WindowBackdropType.None);
        set => SetValue(SystemBackdropProperty, value);
    }

    /// <summary>
    /// Gets the native window handle.
    /// </summary>
    public nint Handle { get; private set; }

    /// <summary>
    /// Gets the render target for this window.
    /// </summary>
    public RenderTarget? RenderTarget { get; private set; }

    /// <summary>
    /// Gets the DPI scale factor for this window (1.0 = 96 DPI = 100%).
    /// </summary>
    public double DpiScale => _dpiScale;

    /// <summary>
    /// Gets the overlay layer for hosting popup content within the window's visual tree.
    /// </summary>
    internal OverlayLayer OverlayLayer { get; private set; }

    /// <summary>
    /// Gets or sets the TaskbarItemInfo object that provides taskbar integration features.
    /// </summary>
    public TaskbarItemInfo? TaskbarItemInfo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this window appears on top of all other windows.
    /// </summary>
    public bool Topmost
    {
        get => (bool)GetValue(TopmostProperty)!;
        set => SetValue(TopmostProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether a window automatically sizes itself to fit the size of its content.
    /// </summary>
    public SizeToContent SizeToContent
    {
        get => (SizeToContent)GetValue(SizeToContentProperty)!;
        set => SetValue(SizeToContentProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether a window can be resized.
    /// </summary>
    public ResizeMode ResizeMode
    {
        get => (ResizeMode)GetValue(ResizeModeProperty)!;
        set => SetValue(ResizeModeProperty, value);
    }

    /// <summary>
    /// Gets or sets a window's border style.
    /// </summary>
    public WindowStyle WindowStyle
    {
        get => (WindowStyle)GetValue(WindowStyleProperty)!;
        set => SetValue(WindowStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the content rendered on the left side of the title bar.
    /// </summary>
    public FrameworkElement? LeftWindowCommands
    {
        get => (FrameworkElement?)GetValue(LeftWindowCommandsProperty);
        set => SetValue(LeftWindowCommandsProperty, value);
    }

    /// <summary>
    /// Gets or sets the content rendered on the right side of the title bar.
    /// </summary>
    public FrameworkElement? RightWindowCommands
    {
        get => (FrameworkElement?)GetValue(RightWindowCommandsProperty);
        set => SetValue(RightWindowCommandsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the title bar icon is visible.
    /// </summary>
    public bool IsShowIcon
    {
        get => (bool)GetValue(IsShowIconProperty)!;
        set => SetValue(IsShowIconProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the title text is visible.
    /// </summary>
    public bool IsShowTitle
    {
        get => (bool)GetValue(IsShowTitleProperty)!;
        set => SetValue(IsShowTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the custom title bar is visible.
    /// </summary>
    public bool IsShowTitleBar
    {
        get => (bool)GetValue(IsShowTitleBarProperty)!;
        set => SetValue(IsShowTitleBarProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the minimize button is visible.
    /// </summary>
    public bool IsShowMinimizeButton
    {
        get => (bool)GetValue(IsShowMinimizeButtonProperty)!;
        set => SetValue(IsShowMinimizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the maximize button is visible.
    /// </summary>
    public bool IsShowMaximizeButton
    {
        get => (bool)GetValue(IsShowMaximizeButtonProperty)!;
        set => SetValue(IsShowMaximizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the close button is visible.
    /// </summary>
    public bool IsShowCloseButton
    {
        get => (bool)GetValue(IsShowCloseButtonProperty)!;
        set => SetValue(IsShowCloseButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets the height, in DIPs, of the custom title bar.
    /// </summary>
    public double TitleBarHeight
    {
        get => (double)GetValue(TitleBarHeightProperty)!;
        set => SetValue(TitleBarHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon displayed in the custom title bar.
    /// </summary>
    public ImageSource? WindowIcon
    {
        get => (ImageSource?)GetValue(WindowIconProperty);
        set => SetValue(WindowIconProperty, value);
    }

    /// <summary>
    /// Gets or sets the window that owns this window.
    /// </summary>
    public Window? Owner { get; set; }

    /// <summary>
    /// Gets or sets the dialog result value, which is the return value of the ShowDialog method.
    /// </summary>
    public bool? DialogResult { get; set; }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the window is loaded.
    /// </summary>
    public event EventHandler? Loaded;

    /// <summary>
    /// Occurs when the window is closing. Set Cancel to true to prevent closing.
    /// </summary>
    public event EventHandler<System.ComponentModel.CancelEventArgs>? Closing;

    /// <summary>
    /// Occurs when the window is closed.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Occurs when the window location changes.
    /// </summary>
    public event EventHandler? LocationChanged;

    #endregion


    /// <summary>
    /// Gets the title bar control. Only available when TitleBarStyle is Custom.
    /// </summary>
    public TitleBar? TitleBar { get; private set; }

    private MouseButton? _suppressMouseUpButton;
    private const uint MousePointerId = 1;
    private readonly Dictionary<uint, UIElement?> _activePointerTargets = [];
    private readonly Dictionary<uint, PointerPoint> _lastPointerPoints = [];
    private readonly Dictionary<uint, PointerStylusDevice> _activeStylusDevices = [];
    private readonly Dictionary<uint, PointerManipulationSession> _activeManipulationSessions = [];
    private readonly RealTimeStylus _realTimeStylus;

    /// <summary>
    /// External popup windows that are currently open and owned by this window.
    /// Used for light-dismiss coordination.
    /// </summary>
    internal List<Popup> ActiveExternalPopups { get; } = [];

    public Window()
    {
        Background = new SolidColorBrush(Color.White);
        Width = 800;
        Height = 600;

        // Create overlay layer for popup hosting (must be created before title bar)
        OverlayLayer = new OverlayLayer();
        AddVisualChild(OverlayLayer);

        _realTimeStylus = new RealTimeStylus(this);

        CreateTitleBar();
    }

    /// <summary>
    /// Recursively applies implicit styles to this window and all descendant elements
    /// that don't yet have a style applied. This ensures elements created before the
    /// theme was loaded (e.g., TitleBar) get properly styled before the window is shown.
    /// </summary>
    private void EnsureImplicitStyles()
    {
        ApplyImplicitStylesRecursive(this);

        static void ApplyImplicitStylesRecursive(Visual visual)
        {
            if (visual is FrameworkElement fe)
            {
                fe.ApplyImplicitStyleIfNeeded();
            }

            for (int i = 0; i < visual.VisualChildrenCount; i++)
            {
                var child = visual.GetVisualChild(i);
                if (child != null)
                    ApplyImplicitStylesRecursive(child);
            }
        }
    }

    private void CreateTitleBar()
    {
        if (TitleBarStyle != WindowTitleBarStyle.Custom)
        {
            return;
        }

        TitleBar = new TitleBar();
        ApplyTitleBarPresentation();

        TitleBar.MinimizeClicked += OnTitleBarMinimizeClicked;
        TitleBar.MaximizeRestoreClicked += OnTitleBarMaximizeRestoreClicked;
        TitleBar.CloseClicked += OnTitleBarCloseClicked;

        AddVisualChild(TitleBar);
    }

    private void RemoveTitleBar()
    {
        if (TitleBar != null)
        {
            TitleBar.MinimizeClicked -= OnTitleBarMinimizeClicked;
            TitleBar.MaximizeRestoreClicked -= OnTitleBarMaximizeRestoreClicked;
            TitleBar.CloseClicked -= OnTitleBarCloseClicked;

            RemoveVisualChild(TitleBar);
            TitleBar = null;
        }
    }

    private void ApplyTitleBarPresentation()
    {
        EnsureAutoWindowIcon();

        if (TitleBar == null)
        {
            return;
        }

        TitleBar.Height = GetEffectiveTitleBarHeightDip();
        TitleBar.Title = Title;
        TitleBar.IsMaximized = WindowState == WindowState.Maximized;
        TitleBar.ShowMinimizeButton = IsShowMinimizeButton;
        TitleBar.ShowMaximizeButton = IsShowMaximizeButton;
        TitleBar.ShowCloseButton = IsShowCloseButton;
        TitleBar.LeftWindowCommands = LeftWindowCommands;
        TitleBar.RightWindowCommands = RightWindowCommands;
        TitleBar.IsShowIcon = IsShowIcon;
        TitleBar.IsShowTitle = IsShowTitle;
        TitleBar.WindowIcon = WindowIcon;
        TitleBar.Visibility = IsShowTitleBar ? Visibility.Visible : Visibility.Collapsed;

        if (!IsShowTitleBar)
        {
            ClearTitleBarInteractionState();
        }
    }

    private void ClearTitleBarInteractionState()
    {
        UpdateTitleBarButtonHover(null);
        if (_pressedTitleBarButton != null)
        {
            _pressedTitleBarButton.SetIsPressed(false);
            _pressedTitleBarButton = null;
        }
    }

    private void EnsureAutoWindowIcon()
    {
        if (WindowIcon != null || _attemptedAutoWindowIcon)
        {
            return;
        }

        _attemptedAutoWindowIcon = true;
        var icon = TryCreateDefaultWindowIcon();
        if (icon != null)
        {
            SetValue(WindowIconProperty, icon);
        }
    }

    private static ImageSource? TryCreateDefaultWindowIcon()
    {
        System.Drawing.Icon? icon = null;
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && System.IO.File.Exists(processPath))
            {
                icon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            }

            icon ??= (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
            using var bitmap = icon.ToBitmap();
            using var stream = new System.IO.MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return BitmapImage.FromBytes(stream.ToArray());
        }
        catch
        {
            return null;
        }
        finally
        {
            icon?.Dispose();
        }
    }

    private void OnTitleBarMinimizeClicked(object? sender, EventArgs e)
    {
        if (Handle != nint.Zero)
        {
            _ = ShowWindow(Handle, SW_MINIMIZE);
        }
    }

    private void OnTitleBarMaximizeRestoreClicked(object? sender, EventArgs e)
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            _ = ShowWindow(Handle, SW_RESTORE);
            WindowState = WindowState.Normal;
        }
        else
        {
            _ = ShowWindow(Handle, SW_MAXIMIZE);
            WindowState = WindowState.Maximized;
        }
    }

    private void OnTitleBarCloseClicked(object? sender, EventArgs e)
    {
        Close();
    }

    private int HandleNcHitTest(nint lParam)
    {
        // Get cursor position in screen coordinates (physical pixels)
        int screenX = (short)(lParam.ToInt64() & 0xFFFF);
        int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        // Check if window is maximized
        bool isMaximized = IsZoomed(Handle);

        // Convert screen coordinates to client-area coordinates (physical pixels).
        POINT pt = new() { X = screenX, Y = screenY };
        _ = ScreenToClient(Handle, ref pt);

        // Convert to DIPs for comparison with layout values.
        double x = pt.X / _dpiScale;
        double y = pt.Y / _dpiScale;

        return ComputeNcHitTestFromClientDip(x, y, isMaximized);
    }

    // Extracted for deterministic tests without a live HWND.
    internal int ComputeNcHitTestFromClientDip(double x, double y, bool isMaximized)
    {
        double windowWidth = Width;
        double windowHeight = Height;

        var titleBarHeight = GetCurrentTitleBarHeightDip();
        bool canResize = !isMaximized &&
            (ResizeMode == ResizeMode.CanResize || ResizeMode == ResizeMode.CanResizeWithGrip);
        const int resizeBorder = 6;

        bool isLeft = false;
        bool isRight = false;
        bool isTop = false;
        bool isBottom = false;

        if (canResize)
        {
            isLeft = x < resizeBorder;
            isRight = x >= windowWidth - resizeBorder;
            isTop = y < resizeBorder;
            isBottom = y >= windowHeight - resizeBorder;

            if (isTop && isLeft)
            {
                return HTTOPLEFT;
            }

            if (isTop && isRight)
            {
                return HTTOPRIGHT;
            }

            if (isBottom && isLeft)
            {
                return HTBOTTOMLEFT;
            }

            if (isBottom && isRight)
            {
                return HTBOTTOMRIGHT;
            }

            if (isLeft)
            {
                return HTLEFT;
            }

            if (isRight)
            {
                return HTRIGHT;
            }

            if (isTop)
            {
                return HTTOP;
            }

            if (isBottom)
            {
                return HTBOTTOM;
            }
        }

        if (!IsTitleBarVisible())
        {
            return HTCLIENT;
        }

        var button = GetTitleBarButtonAtPoint(new Point(x, y), windowWidth);
        if (button != null)
        {
            return button.Kind switch
            {
                TitleBarButtonKind.Minimize => HTMINBUTTON,
                TitleBarButtonKind.Maximize or TitleBarButtonKind.Restore => HTMAXBUTTON,
                TitleBarButtonKind.Close => HTCLOSE,
                _ => HTCLIENT
            };
        }

        if (IsTitleBarWindowCommandsHit(new Point(x, y)))
        {
            return HTCLIENT;
        }

        if (y < titleBarHeight)
        {
            return HTCAPTION;
        }

        return HTCLIENT;
    }

    private bool IsTitleBarVisible()
    {
        return TitleBarStyle == WindowTitleBarStyle.Custom &&
               IsShowTitleBar &&
               TitleBar != null &&
               TitleBar.Visibility == Visibility.Visible;
    }

    private double GetEffectiveTitleBarHeightDip()
    {
        double height = TitleBarHeight;
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
        {
            return DefaultTitleBarHeightDip;
        }

        return height;
    }

    private double GetCurrentTitleBarHeightDip()
    {
        if (!IsTitleBarVisible())
        {
            return 0;
        }

        if (TitleBar == null)
        {
            return GetEffectiveTitleBarHeightDip();
        }

        return GetElementHeightDip(TitleBar, GetEffectiveTitleBarHeightDip());
    }

    private bool IsTitleBarWindowCommandsHit(Point point)
    {
        if (!IsTitleBarVisible() || TitleBar == null)
        {
            return false;
        }

        var titleBarBounds = TitleBar.VisualBounds;
        var localPoint = new Point(point.X - titleBarBounds.X, point.Y - titleBarBounds.Y);
        return TitleBar.IsPointInWindowCommands(localPoint);
    }

    private TitleBarButton? GetTitleBarButtonAtPoint(Point point, double windowWidth = 0)
    {
        if (!IsTitleBarVisible() || TitleBar == null)
        {
            return null;
        }

        var titleBarBounds = TitleBar.VisualBounds;

        // Convert to TitleBar-local coordinates so they can be compared with button VisualBounds.
        var localPoint = new Point(point.X - titleBarBounds.X, point.Y - titleBarBounds.Y);

        var closeButton = GetTitleBarButtonByKind(TitleBarButtonKind.Close);
        if (IsTitleBarButtonHit(localPoint, closeButton))
        {
            return closeButton;
        }

        var maximizeButton = GetTitleBarButtonByKind(TitleBar.IsMaximized ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize);
        if (IsTitleBarButtonHit(localPoint, maximizeButton))
        {
            return maximizeButton;
        }

        var minimizeButton = GetTitleBarButtonByKind(TitleBarButtonKind.Minimize);
        if (IsTitleBarButtonHit(localPoint, minimizeButton))
        {
            return minimizeButton;
        }

        // Fallback for cases before first arrange: use current button widths,
        // not a hardcoded value, so hit-test math stays aligned with layout.
        var titleBarWidth = windowWidth > 0
            ? windowWidth
            : (TitleBar.ActualWidth > 0 ? TitleBar.ActualWidth : Width);

        return GetTitleBarButtonByWidthFallback(localPoint, titleBarWidth, closeButton, maximizeButton, minimizeButton);
    }

    private static bool IsTitleBarButtonHit(Point localPoint, TitleBarButton? button)
    {
        if (button == null || button.Visibility != Visibility.Visible)
        {
            return false;
        }

        var bounds = button.VisualBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        // Use the largest known dimension so styled Width/Height can expand NC hit-testing
        // even when layout is temporarily constrained to native caption metrics.
        double width = Math.Max(bounds.Width, GetTitleBarButtonHitWidth(button));
        double height = Math.Max(bounds.Height, GetTitleBarButtonHitHeight(button));

        return localPoint.X >= bounds.X &&
               localPoint.X < bounds.X + width &&
               localPoint.Y >= bounds.Y &&
               localPoint.Y < bounds.Y + height;
    }

    private TitleBarButton? GetTitleBarButtonByWidthFallback(
        Point localPoint,
        double titleBarWidth,
        TitleBarButton? closeButton,
        TitleBarButton? maximizeButton,
        TitleBarButton? minimizeButton)
    {
        double buttonX = titleBarWidth;

        if (TitleBar!.ShowCloseButton && closeButton != null)
        {
            var closeWidth = GetTitleBarButtonHitWidth(closeButton);
            var closeHeight = GetTitleBarButtonHitHeight(closeButton);
            buttonX -= closeWidth;
            if (localPoint.X >= buttonX && localPoint.X < buttonX + closeWidth &&
                localPoint.Y >= 0 && localPoint.Y < closeHeight)
            {
                return closeButton;
            }
        }

        if (TitleBar.ShowMaximizeButton && maximizeButton != null)
        {
            var maximizeWidth = GetTitleBarButtonHitWidth(maximizeButton);
            var maximizeHeight = GetTitleBarButtonHitHeight(maximizeButton);
            buttonX -= maximizeWidth;
            if (localPoint.X >= buttonX && localPoint.X < buttonX + maximizeWidth &&
                localPoint.Y >= 0 && localPoint.Y < maximizeHeight)
            {
                return maximizeButton;
            }
        }

        if (TitleBar.ShowMinimizeButton && minimizeButton != null)
        {
            var minimizeWidth = GetTitleBarButtonHitWidth(minimizeButton);
            var minimizeHeight = GetTitleBarButtonHitHeight(minimizeButton);
            buttonX -= minimizeWidth;
            if (localPoint.X >= buttonX && localPoint.X < buttonX + minimizeWidth &&
                localPoint.Y >= 0 && localPoint.Y < minimizeHeight)
            {
                return minimizeButton;
            }
        }

        return null;
    }

    private static double GetTitleBarButtonHitWidth(TitleBarButton button)
    {
        double width = 0;

        if (button.ActualWidth > 0)
        {
            width = Math.Max(width, button.ActualWidth);
        }

        if (button.DesiredSize.Width > 0)
        {
            width = Math.Max(width, button.DesiredSize.Width);
        }

        if (!double.IsNaN(button.Width) && button.Width > 0)
        {
            width = Math.Max(width, button.Width);
        }

        return width > 0 ? width : 46.0;
    }

    private static double GetTitleBarButtonHitHeight(TitleBarButton button)
    {
        double height = 0;

        if (button.ActualHeight > 0)
        {
            height = Math.Max(height, button.ActualHeight);
        }

        if (button.DesiredSize.Height > 0)
        {
            height = Math.Max(height, button.DesiredSize.Height);
        }

        if (!double.IsNaN(button.Height) && button.Height > 0)
        {
            height = Math.Max(height, button.Height);
        }

        return height > 0 ? height : DefaultTitleBarHeightDip;
    }

    private TitleBarButton? GetTitleBarButtonByKind(TitleBarButtonKind kind)
    {
        return TitleBar?.GetButtonByKind(kind);
    }

    private TitleBarButton? GetTitleBarButtonByNcHit(int hitTest)
    {
        return hitTest switch
        {
            HTMINBUTTON => GetTitleBarButtonByKind(TitleBarButtonKind.Minimize),
            HTMAXBUTTON => GetTitleBarButtonByKind(TitleBar?.IsMaximized == true ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize),
            HTCLOSE => GetTitleBarButtonByKind(TitleBarButtonKind.Close),
            _ => null
        };
    }

    private static bool IsNcHitMatchingButtonKind(int hitTest, TitleBarButtonKind kind)
    {
        return (hitTest == HTMINBUTTON && kind == TitleBarButtonKind.Minimize) ||
               (hitTest == HTMAXBUTTON && (kind == TitleBarButtonKind.Maximize || kind == TitleBarButtonKind.Restore)) ||
               (hitTest == HTCLOSE && kind == TitleBarButtonKind.Close);
    }

    private static bool IsCaptionButtonNcHit(int hitTest)
    {
        return hitTest == HTMINBUTTON || hitTest == HTMAXBUTTON || hitTest == HTCLOSE;
    }

    private TitleBarButton? _hoveredTitleBarButton;
    private TitleBarButton? _pressedTitleBarButton;

    private void OnNcMouseMove(nint wParam, nint lParam)
    {
        _ = wParam;

        if (!IsTitleBarVisible())
        {
            UpdateTitleBarButtonHover(null);
            return;
        }

        // Get cursor position in screen coordinates (physical pixels)
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        // Convert to client coordinates (physical) then to DIPs
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale));
        UpdateTitleBarButtonHover(button);

        // Track non-client mouse leave
        TRACKMOUSEEVENT tme = new()
        {
            cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
            dwFlags = TME_LEAVE | TME_HOVER | TME_NONCLIENT,
            hwndTrack = Handle,
            dwHoverTime = HOVER_DEFAULT
        };
        _ = TrackMouseEvent(ref tme);

        // Let DWM/DefWindowProc continue processing NC mouse move for Snap state machine.
    }

    private void OnNcMouseLeave()
    {
        UpdateTitleBarButtonHover(null);
    }

    private bool TryInjectSnapProxyNcMouseMessage(uint msg, nint lParam)
    {
        _ = msg;
        _ = lParam;
        // Disabled: synthetic NC proxy routing proved unstable
        // (button click regressions and resize jitter) and did not reliably
        // improve Snap flyout behavior across custom caption geometries.
        return false;
    }

    private bool TryBuildMaxButtonProxyLParam(
        nint lParam,
        out nint proxyLParam,
        out (int x, int y) realScreenPoint,
        out (int x, int y) proxyScreenPoint)
    {
        proxyLParam = nint.Zero;
        realScreenPoint = default;
        proxyScreenPoint = default;

        if (!ShouldUseWin11SnapNcRouting() || Handle == nint.Zero)
        {
            return false;
        }

        realScreenPoint = UnpackScreenPointFromLParam(lParam);
        if (!TryGetCustomMaxButtonScreenBounds(out var customMaxRect) ||
            !ContainsPoint(customMaxRect, realScreenPoint))
        {
            return false;
        }

        if (!TryGetDwmMaxButtonBounds(out var dwmMaxRect) ||
            !TryBuildMaxButtonProxyScreenPoint(realScreenPoint, customMaxRect, dwmMaxRect, out proxyScreenPoint))
        {
            return false;
        }

        proxyLParam = PackScreenPointToLParam(proxyScreenPoint);
        return true;
    }

    private bool TryGetDwmMaxButtonBounds(out (int left, int top, int right, int bottom) dwmMaxRect)
    {
        dwmMaxRect = default;
        if (!TryGetDwmCaptionButtonBounds(Handle, out var captionBounds))
        {
            return false;
        }

        bool showMinimize = TitleBar?.ShowMinimizeButton ?? true;
        bool showMaximize = TitleBar?.ShowMaximizeButton ?? true;
        bool showClose = TitleBar?.ShowCloseButton ?? true;
        return TryGetDwmMaxButtonBounds(captionBounds, showMinimize, showMaximize, showClose, out dwmMaxRect);
    }

    private bool TryGetCustomMaxButtonScreenBounds(out (int left, int top, int right, int bottom) customMaxRect)
    {
        customMaxRect = default;
        if (Handle == nint.Zero || TitleBarStyle != WindowTitleBarStyle.Custom || TitleBar == null)
        {
            return false;
        }

        var maxButton = GetTitleBarButtonByKind(TitleBar.IsMaximized ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize);
        if (!TryGetCustomMaxButtonClientBoundsDip(maxButton, out var customMaxClientRectDip))
        {
            return false;
        }

        POINT clientOrigin = new() { X = 0, Y = 0 };
        if (!ClientToScreen(Handle, ref clientOrigin))
        {
            return false;
        }

        return TryGetCustomMaxButtonScreenBounds(customMaxClientRectDip, _dpiScale, (clientOrigin.X, clientOrigin.Y), out customMaxRect);
    }

    private bool TryGetCustomMaxButtonClientBoundsDip(
        TitleBarButton? maxButton,
        out (double left, double top, double right, double bottom) clientRect)
    {
        clientRect = default;
        if (TitleBar == null || maxButton == null || maxButton.Visibility != Visibility.Visible)
        {
            return false;
        }

        var titleBarBounds = TitleBar.VisualBounds;
        var buttonBounds = maxButton.VisualBounds;
        double left;
        double top;
        double width;
        double height;

        if (buttonBounds.Width > 0 && buttonBounds.Height > 0)
        {
            left = titleBarBounds.X + buttonBounds.X;
            top = titleBarBounds.Y + buttonBounds.Y;
            width = buttonBounds.Width;
            height = buttonBounds.Height;
        }
        else
        {
            double titleBarWidth = TitleBar.ActualWidth > 0
                ? TitleBar.ActualWidth
                : (TitleBar.DesiredSize.Width > 0 ? TitleBar.DesiredSize.Width : Width);
            if (titleBarWidth <= 0)
            {
                return false;
            }

            double buttonX = titleBarBounds.X + titleBarWidth;
            if (TitleBar.ShowCloseButton)
            {
                var closeButton = GetTitleBarButtonByKind(TitleBarButtonKind.Close);
                if (closeButton != null && closeButton.Visibility == Visibility.Visible)
                {
                    buttonX -= GetTitleBarButtonHitWidth(closeButton);
                }
            }

            width = GetTitleBarButtonHitWidth(maxButton);
            height = GetTitleBarButtonHitHeight(maxButton);
            buttonX -= width;
            left = buttonX;
            top = titleBarBounds.Y;
        }

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        clientRect = (left, top, left + width, top + height);
        return true;
    }

    private static (int x, int y) UnpackScreenPointFromLParam(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        return (x, y);
    }

    private static nint PackScreenPointToLParam((int x, int y) point)
    {
        ushort x = unchecked((ushort)(short)point.x);
        ushort y = unchecked((ushort)(short)point.y);
        int packed = x | (y << 16);
        return new nint(packed);
    }

    private static bool ContainsPoint((int left, int top, int right, int bottom) rect, (int x, int y) point)
    {
        return point.x >= rect.left && point.x < rect.right && point.y >= rect.top && point.y < rect.bottom;
    }

    private bool OnNcLButtonDown(nint wParam, nint lParam)
    {
        if (!IsTitleBarVisible())
        {
            return false;
        }

        // Use actual pointer position instead of relying on wParam hit-test.
        // During/after resize, NC hit-test values can drift while the button
        // geometry is still correct, which leads to hover working but clicks ignored.
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        int hitTest = (int)wParam.ToInt64();
        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale)) ??
                     GetTitleBarButtonByNcHit(hitTest);
        if (button == null)
        {
            // Not on a custom caption button: let Windows handle caption drag/resize.
            return false;
        }

        _pressedTitleBarButton = button;
        button.SetIsPressed(true);  // triggers InvalidateVisual() → dirty rect
        return true;
    }

    private bool OnNcLButtonUp(nint wParam, nint lParam)
    {
        if (!IsTitleBarVisible())
        {
            return false;
        }

        // Get cursor position (physical pixels) → client → DIPs
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        int hitTest = (int)wParam.ToInt64();
        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale)) ??
                     GetTitleBarButtonByNcHit(hitTest);

        if (_pressedTitleBarButton != null)
        {
            _pressedTitleBarButton.SetIsPressed(false);

            // If released on the same button, trigger click
            bool isReleaseOnPressedButton = button == _pressedTitleBarButton ||
                                            (button == null && IsNcHitMatchingButtonKind(hitTest, _pressedTitleBarButton.Kind));
            if (isReleaseOnPressedButton)
            {
                switch (_pressedTitleBarButton.Kind)
                {
                    case TitleBarButtonKind.Minimize:
                        TitleBar?.RaiseMinimizeClicked();
                        break;
                    case TitleBarButtonKind.Maximize:
                    case TitleBarButtonKind.Restore:
                        TitleBar?.RaiseMaximizeRestoreClicked();
                        break;
                    case TitleBarButtonKind.Close:
                        TitleBar?.RaiseCloseClicked();
                        break;
                }
            }

            _pressedTitleBarButton = null;
            // SetIsPressed(false) above already triggers InvalidateVisual() → dirty rect
            return true;
        }

        return false;
    }

    private bool OnNcLButtonDblClk(nint wParam, nint lParam)
    {
        if (!IsTitleBarVisible())
        {
            return false;
        }

        int hitTest = (int)wParam.ToInt64();

        // Get cursor position (physical pixels) → client → DIPs
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        // If over a button, don't handle double-click as caption maximize.
        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale));
        if (button != null)
        {
            return true;
        }

        if (hitTest != HTCAPTION)
        {
            return false;
        }

        // Double-click on title bar (caption area) to maximize/restore
        if (hitTest == HTCAPTION && TitleBar != null && TitleBar.ShowMaximizeButton)
        {
            TitleBar.RaiseMaximizeRestoreClicked();
            return true;
        }

        return false;
    }

    private void UpdateTitleBarButtonHover(TitleBarButton? newHoveredButton)
    {
        if (_hoveredTitleBarButton == newHoveredButton)
        {
            return;
        }

        _hoveredTitleBarButton?.SetIsMouseOver(false);  // triggers InvalidateVisual() → dirty rect

        _hoveredTitleBarButton = newHoveredButton;

        _hoveredTitleBarButton?.SetIsMouseOver(true);  // triggers InvalidateVisual() → dirty rect
    }

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            int count = base.VisualChildrenCount;
            if (TitleBar != null) count++;
            count++; // OverlayLayer is always present
            return count;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        // Order: ContentElement(s) → TitleBar → OverlayLayer (last = rendered on top, hit-tested first)
        int baseCount = base.VisualChildrenCount;

        if (index < baseCount)
        {
            return base.GetVisualChild(index);
        }

        int extra = index - baseCount;

        if (TitleBar != null)
        {
            if (extra == 0) return TitleBar;
            if (extra == 1) return OverlayLayer;
        }
        else
        {
            if (extra == 0) return OverlayLayer;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double titleBarHeight = 0;

        // Measure title bar
        if (IsTitleBarVisible() && TitleBar != null)
        {
            double effectiveTitleBarHeight = GetEffectiveTitleBarHeightDip();
            TitleBar.Measure(new Size(availableSize.Width, effectiveTitleBarHeight));
            titleBarHeight = GetElementHeightDip(TitleBar, effectiveTitleBarHeight);
        }

        // Measure content with remaining space
        var contentElement = ContentElement;
        if (contentElement != null)
        {
            Size contentAvailable = new(
                availableSize.Width,
                Math.Max(0, availableSize.Height - titleBarHeight));
            contentElement.Measure(contentAvailable);
        }

        // Measure overlay layer with full window size (it doesn't consume space)
        OverlayLayer.Measure(availableSize);

        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double titleBarHeight = 0;

        // Arrange title bar at top
        if (IsTitleBarVisible() && TitleBar != null)
        {
            titleBarHeight = GetCurrentTitleBarHeightDip();
            Rect titleBarRect = new(0, 0, finalSize.Width, titleBarHeight);
            TitleBar.Arrange(titleBarRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Arrange content below title bar
        var contentElement = ContentElement;
        if (contentElement is FrameworkElement contentFe)
        {
            Rect contentRect = new(
                0,
                titleBarHeight,
                finalSize.Width,
                Math.Max(0, finalSize.Height - titleBarHeight));
            contentFe.Arrange(contentRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Arrange overlay layer over the full window area
        OverlayLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));

        // Keep DWM non-client hover tracking region aligned with current title bar/button geometry.
        UpdateCustomTitleBarFrameMargins();

        return finalSize;
    }

    #endregion

    /// <summary>
    /// Shows the window.
    /// </summary>
    public void Show()
    {
        // Ensure implicit styles are applied to the entire visual tree.
        // This handles the case where elements (e.g., TitleBar) were created in the
        // Window constructor BEFORE the theme was loaded by the Xaml module initializer.
        // In non-AOT mode, the theme loads lazily when XamlReader is first accessed
        // (during InitializeComponent), but TitleBar is created earlier in Window().
        EnsureImplicitStyles();

        _dispatcher = Dispatcher.CurrentDispatcher;
        CompositionTarget.FrameStarting += OnFrameStarting;
        EnsureHandle();

        // Detect monitor refresh rate and update CompositionTarget for adaptive frame rate
        var refreshRate = DetectMonitorRefreshRate();
        CompositionTarget.UpdateRefreshRate(refreshRate);

        _ = ShowWindow(Handle, SW_SHOW);

        // For custom title bar, trigger frame change after show to remove native title bar
        // This preserves animations while hiding the native caption
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER);
        }

        _ = UpdateWindow(Handle);

        // Fail fast if the first frame cannot be rendered.
        ForceRenderFrame();

        Loaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hides the window.
    /// </summary>
    public void Hide()
    {
        if (Handle != nint.Zero)
        {
            _ = ShowWindow(Handle, SW_HIDE);
        }
    }

    private bool _isModal;

    /// <summary>
    /// Opens a window and returns only when the newly opened window is closed.
    /// </summary>
    public bool? ShowDialog()
    {
        DialogResult = null;

        // Disable owner window to make this dialog truly modal
        nint ownerHandle = Owner?.Handle ?? nint.Zero;
        if (ownerHandle != nint.Zero)
        {
            EnableWindow(ownerHandle, false);
        }

        // Show the window
        Show();

        // Run a nested message loop until the window is closed
        _isModal = true;
        try
        {
            // Process Win32 messages until this window is closed
            while (_isModal && Handle != nint.Zero)
            {
                if (GetMessage(out MSG msg, nint.Zero, 0, 0) > 0)
                {
                    _ = TranslateMessage(ref msg);
                    _ = DispatchMessage(ref msg);
                }
                else
                {
                    break;
                }

                // Check if window was closed during message processing
                if (Handle == nint.Zero)
                {
                    _isModal = false;
                    break;
                }
            }
        }
        finally
        {
            _isModal = false;

            // Re-enable and activate owner window
            if (ownerHandle != nint.Zero)
            {
                EnableWindow(ownerHandle, true);
                SetForegroundWindow(ownerHandle);
            }
        }

        return DialogResult;
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    private bool _isClosing;
    private bool _isSyncingWindowState;

    public void Close()
    {
        if (_isClosing) return;
        _isClosing = true;

        // Exit modal loop if ShowDialog is waiting
        _isModal = false;

        CompositionTarget.FrameStarting -= OnFrameStarting;

        var closingArgs = new System.ComponentModel.CancelEventArgs();
        Closing?.Invoke(this, closingArgs);
        if (closingArgs.Cancel)
        {
            _isClosing = false;
            CompositionTarget.FrameStarting += OnFrameStarting;
            return;
        }

        // Close all external popup windows
        foreach (var popup in ActiveExternalPopups.ToList())
            popup.IsOpen = false;
        ActiveExternalPopups.Clear();

        // Release the drawing context reference. Do NOT call ClearCache() here —
        // native text format/brush destruction during shutdown can cause StackOverflowException
        // when the DWrite factory or D3D12 device is being torn down concurrently.
        // The finalizer for cached objects already skips native destruction (safe no-op).
        _drawingContext = null;

        // Dispose render target
        RenderTarget?.Dispose();
        RenderTarget = null;

        if (Handle != nint.Zero)
        {
            var handle = Handle;
            Handle = nint.Zero;
            // Remove from window map and destroy
            _windows.Remove(handle);
            _ = DestroyWindow(handle);

            // Let Application decide whether to shut down based on ShutdownMode
            if (Application.Current is { } app)
            {
                app.OnWindowClosed(this, _windows.Count);
            }
            else if (_windows.Count == 0)
            {
                // No Application instance — fall back to quit when no windows remain
                PostQuitMessage(0);
            }
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureHandle()
    {
        if (Handle != nint.Zero)
        {
            return;
        }

        // Register window class if needed
        RegisterWindowClass();

        // Determine window style based on TitleBarStyle.
        // Keep standard caption style bits; custom visual caption is removed via NCCALCSIZE.
        uint dwStyle = GetWindowStyleForTitleBarStyle(TitleBarStyle);

        uint dwExStyle = TitleBarStyle == WindowTitleBarStyle.Custom
            ? WS_EX_APPWINDOW
            : 0;

        // WS_EX_NOREDIRECTIONBITMAP enables DWM system backdrop (Mica/Acrylic).
        // The window visual is provided via DirectComposition composition swap chain.
        if (SystemBackdrop != WindowBackdropType.None)
        {
            dwExStyle |= WS_EX_NOREDIRECTIONBITMAP;
        }

        // Query system DPI for initial window sizing (before HWND exists)
        uint systemDpi = GetDpiForSystem();
        _dpiScale = systemDpi / 96.0;

        // CreateWindowEx takes physical pixel dimensions.
        // Width/Height are in DIPs — scale to physical pixels.
        int physicalWidth = (int)(Width * _dpiScale);
        int physicalHeight = (int)(Height * _dpiScale);

        // Create the window
        Handle = CreateWindowEx(
            dwExStyle,
            WindowClassName,
            Title,
            dwStyle,
            CW_USEDEFAULT, CW_USEDEFAULT,
            physicalWidth, physicalHeight,
            nint.Zero,
            nint.Zero,
            GetModuleHandle(null),
            nint.Zero);

        if (Handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create window.");
        }

        // Store reference for message handling
        _windows[Handle] = this;

        // Refine DPI from actual window monitor (may differ from system DPI)
        uint windowDpi = GetDpiForWindow(Handle);
        if (windowDpi != 0 && windowDpi != systemDpi)
        {
            _dpiScale = windowDpi / 96.0;
            physicalWidth = (int)(Width * _dpiScale);
            physicalHeight = (int)(Height * _dpiScale);
            _ = SetWindowPos(Handle, nint.Zero, 0, 0, physicalWidth, physicalHeight,
                SWP_NOMOVE | SWP_NOZORDER | SWP_NOOWNERZORDER);
        }

        // For custom title bar, enable DWM effects for rounded corners and animations.
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            EnableRoundedCorners();
            // Ensure NC metrics are applied immediately so custom NCCALCSIZE
            // semantics are active on the first displayed frame.
            _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
        }

        // Create render target for this window
        EnsureRenderTarget();

        // Apply system backdrop after render target is ready
        if (SystemBackdrop != WindowBackdropType.None)
        {
            ApplySystemBackdrop(SystemBackdrop);
        }
    }

    private void EnableRoundedCorners()
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        // DWMWA_WINDOW_CORNER_PREFERENCE = 33
        // DWMWCP_ROUND = 2 (rounded corners)
        int cornerPreference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        // Extend frame into client area covering the effective title bar/button hit-test height.
        UpdateCustomTitleBarFrameMargins();
    }

    private void UpdateCustomTitleBarFrameMargins()
    {
        if (Handle == nint.Zero || TitleBarStyle != WindowTitleBarStyle.Custom)
        {
            return;
        }

        int titleBarPhysical = GetCustomTitleBarTopMarginPhysical();
        if (_appliedDwmTopMarginPhysical == titleBarPhysical)
        {
            return;
        }

        MARGINS margins = new() { Left = 0, Right = 0, Top = titleBarPhysical, Bottom = 0 };
        _ = DwmExtendFrameIntoClientArea(Handle, ref margins);
        _appliedDwmTopMarginPhysical = titleBarPhysical;
    }

    private int GetCustomTitleBarTopMarginPhysical()
    {
        if (!IsTitleBarVisible())
        {
            return 0;
        }

        double titleBarHeightDip = GetEffectiveTitleBarHeightDip();
        if (TitleBar != null)
        {
            titleBarHeightDip = GetElementHeightDip(TitleBar, titleBarHeightDip);

            // DWM hover tracking must include oversized caption buttons as well,
            // otherwise Snap only appears inside the old top margin.
            foreach (var button in TitleBar.EnumerateButtons())
            {
                if (button.Visibility == Visibility.Visible)
                {
                    titleBarHeightDip = Math.Max(titleBarHeightDip, GetElementHeightDip(button, titleBarHeightDip));
                }
            }
        }

        return Math.Max((int)Math.Ceiling(titleBarHeightDip * _dpiScale), 1);
    }

    private static double GetElementHeightDip(FrameworkElement element, double fallback)
    {
        if (element.ActualHeight > 0)
        {
            return element.ActualHeight;
        }

        if (element.DesiredSize.Height > 0)
        {
            return element.DesiredSize.Height;
        }

        if (!double.IsNaN(element.Height) && element.Height > 0)
        {
            return element.Height;
        }

        return fallback;
    }

    private void ApplySystemBackdrop(WindowBackdropType backdropType)
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        if (backdropType == WindowBackdropType.None)
        {
            // Disable system backdrop
            int none = DWMSBT_NONE;
            _ = DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));

            // Restore frame extension covering title bar for Snap Layout support
            if (TitleBarStyle == WindowTitleBarStyle.Custom)
            {
                UpdateCustomTitleBarFrameMargins();
            }

            RequestFullInvalidation();
            InvalidateWindow();
            return;
        }

        // DwmExtendFrameIntoClientArea is already called by EnableRoundedCorners()
        // with title-bar-height margins. We do NOT use {-1,-1,-1,-1} because that
        // causes DWM to draw its own system caption buttons on top of our custom
        // title bar. With WS_EX_NOREDIRECTIONBITMAP + composition swap chain, the
        // DWM system backdrop covers the entire window regardless of the margins.

        // Step 1: Set dark mode for proper Mica tint (dark theme)
        int useDarkMode = 1;
        _ = DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

        // Step 3: Set the DWM system backdrop type (Windows 11 22H2+)
        int dwmBackdropType = backdropType switch
        {
            WindowBackdropType.Mica => DWMSBT_MAINWINDOW,
            WindowBackdropType.MicaAlt => DWMSBT_TABBEDWINDOW,
            WindowBackdropType.Acrylic => DWMSBT_TRANSIENTWINDOW,
            _ => DWMSBT_AUTO
        };
        int result = DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref dwmBackdropType, sizeof(int));

        if (result != 0)
        {
            // DWM system backdrop not supported (Windows 10 or older Windows 11).
            // Fallback: SetWindowCompositionAttribute for Acrylic blur.
            ApplyAccentPolicy(backdropType);
        }

        RequestFullInvalidation();
        InvalidateWindow();
    }

    /// <summary>
    /// Fallback for Windows 10: applies Acrylic blur via SetWindowCompositionAttribute.
    /// </summary>
    private void ApplyAccentPolicy(WindowBackdropType backdropType)
    {
        if (Handle == nint.Zero) return;

        int accentState = backdropType == WindowBackdropType.None
            ? ACCENT_DISABLED
            : ACCENT_ENABLE_ACRYLICBLURBEHIND;

        // GradientColor in ABGR format: alpha in high byte
        // Dark tint with ~80% opacity
        uint gradientColor = 0xCC1A1A1A; // ABGR: A=0xCC, B=0x1A, G=0x1A, R=0x1A

        var accent = new ACCENT_POLICY
        {
            AccentState = accentState,
            AccentFlags = 2, // ACCENT_FLAG_DRAW_ALL_BORDERS
            GradientColor = gradientColor,
            AnimationId = 0
        };

        int accentSize = Marshal.SizeOf<ACCENT_POLICY>();
        nint accentPtr = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = accentPtr,
                DataSize = accentSize
            };
            _ = SetWindowCompositionAttribute(Handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    private void UpdateWindowStyle()
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        long style = GetWindowLong(Handle, GWL_STYLE);
        long exStyle = GetWindowLong(Handle, GWL_EXSTYLE);

        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            // Keep WS_CAPTION so the OS preserves native caption semantics
            // (Snap, system menu, NC button behavior). We remove the visual
            // caption in WM_NCCALCSIZE instead.
            style |= WS_CAPTION;
            exStyle |= WS_EX_APPWINDOW;
            EnableRoundedCorners();
        }
        else
        {
            style |= WS_CAPTION;
        }

        _ = SetWindowLong(Handle, GWL_STYLE, style);
        _ = SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

        // Force window to redraw with new style
        _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER);
    }

    private static uint GetWindowStyleForTitleBarStyle(WindowTitleBarStyle titleBarStyle)
    {
        if (titleBarStyle == WindowTitleBarStyle.Custom)
        {
            // Keep the standard overlapped styles (including WS_CAPTION).
            // Custom rendering still removes the visible caption via NCCALCSIZE.
            return WS_OVERLAPPEDWINDOW;
        }

        return WS_OVERLAPPEDWINDOW;
    }

    private void EnsureRenderTarget()
    {
        if (RenderTarget != null)
        {
            return;
        }

        try
        {
            var context = RenderContext.Current;
            if (context == null || !context.IsValid)
            {
                context = new RenderContext(RenderBackend.D3D12);
            }

            // Swap chain uses physical pixel dimensions
            int physicalWidth = (int)(Width * _dpiScale);
            int physicalHeight = (int)(Height * _dpiScale);

            if (SystemBackdrop != WindowBackdropType.None)
            {
                // Composition swap chain with premultiplied alpha for DWM backdrop transparency.
                RenderTarget = context.CreateRenderTargetForComposition(Handle, physicalWidth, physicalHeight);
            }
            else
            {
                RenderTarget = context.CreateRenderTarget(Handle, physicalWidth, physicalHeight);
            }

            // Set D2D DPI so DIP coordinates map correctly to physical pixels
            float dpi = (float)(_dpiScale * 96.0);
            RenderTarget.SetDpi(dpi, dpi);
        }
        catch
        {
            throw;
        }
    }

    /// <summary>
    /// Ensures this window uses a composition render target that can host external visuals (for WebView composition controller).
    /// Recreates the render target if needed.
    /// </summary>
    internal bool EnsureCompositionRenderTargetForEmbeddedContent()
    {
        if (Handle == nint.Zero)
            return false;

        EnsureRenderTarget();
        if (RenderTarget == null || !RenderTarget.IsValid)
            return false;

        // Fast path: already supports composition child visuals.
        if (RenderTarget.TryCreateWebViewCompositionVisual(out var existingVisual) && existingVisual != nint.Zero)
        {
            RenderTarget.DestroyWebViewCompositionVisual(existingVisual);
            return true;
        }

        try
        {
            var context = RenderContext.Current;
            if (context == null || !context.IsValid)
            {
                context = new RenderContext(RenderBackend.D3D12);
            }

            // Ensure composition-compatible window style for composition swap chain hosting.
            long exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            if ((exStyle & WS_EX_NOREDIRECTIONBITMAP) == 0)
            {
                _ = SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_NOREDIRECTIONBITMAP);
                _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
                    SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
            }

            var oldRenderTarget = RenderTarget;
            RenderTarget = null;
            oldRenderTarget?.Dispose();

            int widthDip = ActualWidth > 0 ? (int)Math.Ceiling(ActualWidth) : (int)Math.Ceiling(Width);
            int heightDip = ActualHeight > 0 ? (int)Math.Ceiling(ActualHeight) : (int)Math.Ceiling(Height);
            int physicalWidth = Math.Max(1, (int)Math.Ceiling(widthDip * _dpiScale));
            int physicalHeight = Math.Max(1, (int)Math.Ceiling(heightDip * _dpiScale));

            RenderTarget = context.CreateRenderTargetForComposition(Handle, physicalWidth, physicalHeight);

            float dpi = (float)(_dpiScale * 96.0);
            RenderTarget.SetDpi(dpi, dpi);

            // Rebind drawing context to the new native render target instance.
            _drawingContext = null;
            ForceRenderFrame();

            if (RenderTarget.TryCreateWebViewCompositionVisual(out var visualAfterSwitch) && visualAfterSwitch != nint.Zero)
            {
                RenderTarget.DestroyWebViewCompositionVisual(visualAfterSwitch);
                return true;
            }
        }
        catch (Exception ex)
        {
            _ = ex;
        }

        return false;
    }

    #region Property Changed Callbacks

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window)
        {
            if (window.Handle != nint.Zero)
            {
                _ = SetWindowText(window.Handle, (string?)e.NewValue ?? "");
            }

            window.ApplyTitleBarPresentation();
        }
    }

    private static void OnWindowStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && e.NewValue is WindowState newState)
        {
            window.ApplyTitleBarPresentation();

            // Sync the native window state when set programmatically.
            // Skip if we're already syncing from WM_SIZE to avoid infinite loop.
            if (!window._isSyncingWindowState && window.Handle != nint.Zero)
            {
                var cmd = newState switch
                {
                    WindowState.Maximized => SW_MAXIMIZE,
                    WindowState.Minimized => SW_MINIMIZE,
                    _ => SW_RESTORE
                };
                _ = ShowWindow(window.Handle, cmd);
            }
        }
    }

    private static void OnTitleBarStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window)
        {
            window.RemoveTitleBar();

            if (e.NewValue is WindowTitleBarStyle windowTitleBarStyle)
            {
                if (windowTitleBarStyle == WindowTitleBarStyle.Custom)
                {
                    window.CreateTitleBar();
                }
            }

            // Update window style if already created
            if (window.Handle != nint.Zero)
            {
                window.UpdateWindowStyle();
            }

            window.ApplyTitleBarPresentation();
            window.InvalidateMeasure();
            window.UpdateCustomTitleBarFrameMargins();
        }
    }

    private static void OnSystemBackdropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && e.NewValue is WindowBackdropType backdropType)
        {
            window.ApplySystemBackdrop(backdropType);
        }
    }

    private static void OnTopmostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && window.Handle != nint.Zero)
        {
            // SetWindowPos with HWND_TOPMOST / HWND_NOTOPMOST
            bool topmost = e.NewValue is bool b && b;
            nint insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
            _ = SetWindowPos(window.Handle, insertAfter, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private static void OnWindowTitleBarPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
        {
            return;
        }

        if (e.Property == WindowIconProperty && e.NewValue == null)
        {
            window._attemptedAutoWindowIcon = false;
        }

        window.ApplyTitleBarPresentation();
        window.InvalidateMeasure();
        window.UpdateCustomTitleBarFrameMargins();
    }

    #endregion

    #region Native Window Management

    private const string WindowClassName = "JaliumUIWindow";
    private static bool _classRegistered;
    private static readonly Dictionary<nint, Window> _windows = [];
    private static WndProcDelegate? _wndProcDelegate;
    private bool _isSizing; // True during drag resize

    // Cursor cache - stores loaded cursor handles to avoid repeated LoadCursor calls
    private static readonly Dictionary<CursorType, nint> _cursorCache = [];

    /// <summary>
    /// Gets the Windows cursor handle for a CursorType.
    /// </summary>
    private static nint GetCursorHandle(CursorType cursorType)
    {
        if (_cursorCache.TryGetValue(cursorType, out var handle))
        {
            return handle;
        }

        nint cursorId = cursorType switch
        {
            CursorType.Arrow => IDC_ARROW,
            CursorType.IBeam => IDC_IBEAM,
            CursorType.Wait => IDC_WAIT,
            CursorType.Cross => IDC_CROSS,
            CursorType.UpArrow => IDC_UPARROW,
            CursorType.SizeNWSE => IDC_SIZENWSE,
            CursorType.SizeNESW => IDC_SIZENESW,
            CursorType.SizeWE => IDC_SIZEWE,
            CursorType.SizeNS => IDC_SIZENS,
            CursorType.SizeAll => IDC_SIZEALL,
            CursorType.No => IDC_NO,
            CursorType.Hand => IDC_HAND,
            CursorType.AppStarting => IDC_APPSTARTING,
            CursorType.Help => IDC_HELP,
            CursorType.None => nint.Zero, // Will hide cursor
            _ => IDC_ARROW
        };

        handle = cursorId != nint.Zero ? LoadCursor(nint.Zero, cursorId) : nint.Zero;
        _cursorCache[cursorType] = handle;
        return handle;
    }

    /// <summary>
    /// Handles WM_SETCURSOR by finding the element under the cursor and setting the appropriate cursor.
    /// </summary>
    private bool OnSetCursor(nint lParam)
    {
        // Only handle if the cursor is in the client area
        int hitTest = (short)(lParam.ToInt64() & 0xFFFF);
        if (hitTest != HTCLIENT_SETCURSOR)
        {
            return false; // Let Windows handle non-client area cursors
        }

        // Get the current mouse position
        if (!GetCursorPos(out POINT screenPt))
        {
            return false;
        }

        _ = ScreenToClient(Handle, ref screenPt);
        // Convert physical client pixels to DIPs
        Point clientPos = new(screenPt.X / _dpiScale, screenPt.Y / _dpiScale);

        // Find the element under the cursor
        var hitResult = HitTest(clientPos);
        var element = hitResult?.VisualHit;

        // Walk up the visual tree to find the first element with a non-null Cursor
        Cursor? cursor = null;
        while (element != null)
        {
            if (element is FrameworkElement fe && fe.Cursor != null)
            {
                cursor = fe.Cursor;
                break;
            }
            element = element.VisualParent;
        }

        // Set the cursor
        nint cursorHandle;
        if (cursor != null)
        {
            cursorHandle = GetCursorHandle(cursor.CursorType);
        }
        else
        {
            cursorHandle = GetCursorHandle(CursorType.Arrow);
        }

        if (cursorHandle != nint.Zero)
        {
            _ = SetCursor(cursorHandle);
            return true;
        }

        return false;
    }

#if DEBUG
    private DevToolsWindow? _devToolsWindow;
    internal DevToolsOverlay? DevToolsOverlay { get; set; }

    /// <summary>
    /// Gets whether this window can open DevTools.
    /// Override to return false in windows that should not open DevTools (e.g., DevToolsWindow).
    /// </summary>
    protected virtual bool CanOpenDevTools => true;
#endif

    internal static (int left, int top, int right, int bottom) ComputeCustomNcCalcSizeRect(
        (int left, int top, int right, int bottom) originalRect,
        (int left, int top, int right, int bottom) defClientRect,
        bool isMaximized,
        (int left, int top, int right, int bottom)? workAreaRect)
    {
        if (isMaximized && workAreaRect.HasValue)
        {
            return workAreaRect.Value;
        }

        if (!IsValidRect(defClientRect))
        {
            return originalRect;
        }

        // ControlzEx-style NCCALCSIZE behavior:
        // keep DefWindowProc's side/bottom frame math, but restore original top
        // so the visual system caption is removed while preserving NC semantics.
        return (defClientRect.left, originalRect.top, defClientRect.right, defClientRect.bottom);
    }

    internal static bool TryGetDwmCaptionButtonBounds(
        nint hwnd,
        out (int left, int top, int right, int bottom) captionBounds)
    {
        captionBounds = default;
        if (hwnd == nint.Zero)
        {
            return false;
        }

        int hr = DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>());
        if (hr != 0)
        {
            return false;
        }

        // DWMWA_CAPTION_BUTTON_BOUNDS is window-relative; convert to screen coordinates
        // so it can be mapped against WM_NC* lParam points (also screen coordinates).
        if (!GetWindowRect(hwnd, out RECT windowRect))
        {
            return false;
        }

        var bounds = (
            rect.left + windowRect.left,
            rect.top + windowRect.top,
            rect.right + windowRect.left,
            rect.bottom + windowRect.top);

        if (!IsValidRect(bounds))
        {
            return false;
        }

        captionBounds = bounds;
        return true;
    }

    internal static bool TryGetDwmMaxButtonBounds(
        (int left, int top, int right, int bottom) captionBounds,
        bool showMinimizeButton,
        bool showMaximizeButton,
        bool showCloseButton,
        out (int left, int top, int right, int bottom) maxButtonBounds)
    {
        maxButtonBounds = default;
        if (!showMaximizeButton || !IsValidRect(captionBounds))
        {
            return false;
        }

        int visibleCount = (showMinimizeButton ? 1 : 0) + (showMaximizeButton ? 1 : 0) + (showCloseButton ? 1 : 0);
        if (visibleCount <= 0)
        {
            return false;
        }

        double totalWidth = captionBounds.right - captionBounds.left;
        double buttonWidth = totalWidth / visibleCount;
        if (buttonWidth <= 0)
        {
            return false;
        }

        double maxLeft = showMinimizeButton
            ? captionBounds.left + buttonWidth
            : captionBounds.left;
        double maxRight = maxLeft + buttonWidth;

        int left = Math.Clamp((int)Math.Floor(maxLeft), captionBounds.left, captionBounds.right - 1);
        int right = Math.Clamp((int)Math.Ceiling(maxRight), left + 1, captionBounds.right);
        maxButtonBounds = (left, captionBounds.top, right, captionBounds.bottom);
        return IsValidRect(maxButtonBounds);
    }

    internal static bool TryGetCustomMaxButtonScreenBounds(
        (double left, double top, double right, double bottom) customMaxClientRectDip,
        double dpiScale,
        (int x, int y) clientOriginScreenPoint,
        out (int left, int top, int right, int bottom) customMaxScreenRect)
    {
        customMaxScreenRect = default;
        if (dpiScale <= 0)
        {
            return false;
        }

        double widthDip = customMaxClientRectDip.right - customMaxClientRectDip.left;
        double heightDip = customMaxClientRectDip.bottom - customMaxClientRectDip.top;
        if (widthDip <= 0 || heightDip <= 0)
        {
            return false;
        }

        int left = clientOriginScreenPoint.x + (int)Math.Floor(customMaxClientRectDip.left * dpiScale);
        int top = clientOriginScreenPoint.y + (int)Math.Floor(customMaxClientRectDip.top * dpiScale);
        int right = clientOriginScreenPoint.x + (int)Math.Ceiling(customMaxClientRectDip.right * dpiScale);
        int bottom = clientOriginScreenPoint.y + (int)Math.Ceiling(customMaxClientRectDip.bottom * dpiScale);
        customMaxScreenRect = (left, top, right, bottom);
        return IsValidRect(customMaxScreenRect);
    }

    internal static bool TryBuildMaxButtonProxyScreenPoint(
        (int x, int y) realScreenPoint,
        (int left, int top, int right, int bottom) customMaxRect,
        (int left, int top, int right, int bottom) dwmMaxRect,
        out (int x, int y) proxyScreenPoint)
    {
        proxyScreenPoint = default;
        if (!IsValidRect(customMaxRect) || !IsValidRect(dwmMaxRect))
        {
            return false;
        }

        int safeLeft = dwmMaxRect.left + 1;
        int safeTop = dwmMaxRect.top + 1;
        int safeRight = dwmMaxRect.right - 2;
        int safeBottom = dwmMaxRect.bottom - 2;
        if (safeRight < safeLeft)
        {
            safeLeft = dwmMaxRect.left;
            safeRight = dwmMaxRect.right - 1;
        }

        if (safeBottom < safeTop)
        {
            safeTop = dwmMaxRect.top;
            safeBottom = dwmMaxRect.bottom - 1;
        }

        if (safeRight < safeLeft || safeBottom < safeTop)
        {
            return false;
        }

        double customWidth = customMaxRect.right - customMaxRect.left;
        if (customWidth <= 0)
        {
            return false;
        }

        double normalizedX = (realScreenPoint.x - customMaxRect.left) / customWidth;
        normalizedX = Math.Clamp(normalizedX, 0.0, 1.0);
        int proxyX = safeLeft + (int)Math.Round((safeRight - safeLeft) * normalizedX, MidpointRounding.AwayFromZero);
        proxyX = Math.Clamp(proxyX, safeLeft, safeRight);
        int proxyY = safeTop + ((safeBottom - safeTop) / 2);
        proxyScreenPoint = (proxyX, proxyY);
        return true;
    }

    private static bool IsValidRect((int left, int top, int right, int bottom) rect)
    {
        return rect.right > rect.left && rect.bottom > rect.top;
    }

    private bool ShouldUseWin11SnapNcRouting()
    {
        return IsTitleBarVisible() &&
               IsShowMaximizeButton &&
               OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    }

    private static bool IsSnapRelevantNcMessage(uint msg)
    {
        return msg == WM_NCHITTEST ||
               msg == WM_NCMOUSEMOVE ||
               msg == WM_NCMOUSEHOVER ||
               msg == WM_NCMOUSELEAVE ||
               msg == WM_NCLBUTTONDOWN ||
               msg == WM_NCLBUTTONUP ||
               msg == WM_NCLBUTTONDBLCLK;
    }

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private void RegisterWindowClass()
    {
        if (_classRegistered)
        {
            return;
        }

        _wndProcDelegate = WndProc;

        WNDCLASSEX wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0008, // CS_DBLCLKS: receive WM_*BUTTONDBLCLK messages
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(nint.Zero, IDC_ARROW),
            hbrBackground = nint.Zero, // No background brush - we handle all painting
            lpszClassName = WindowClassName
        };

        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
        {
            throw new InvalidOperationException("Failed to register window class.");
        }

        _classRegistered = true;
    }

    protected virtual nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        Window? window = null;
        if (!_windows.TryGetValue(hWnd, out window))
        {
            // Unit tests invoke WndProc via reflection with hWnd = 0.
            // In that path, process against this instance directly.
            if (hWnd == nint.Zero)
            {
                window = this;
            }
            else
            {
                return DefWindowProc(hWnd, msg, wParam, lParam);
            }
        }

        if (window != null)
        {
            switch (msg)
            {
                case WM_CLOSE:
                    // Route through Close() so Closing event can cancel
                    window.Close();
                    return nint.Zero;

                case WM_DESTROY:
                    // Just clean up the window map; quit logic is handled by
                    // Close() → Application.OnWindowClosed() based on ShutdownMode.
                    // Do NOT call PostQuitMessage here — it would kill the app
                    // when closing any window in a multi-window scenario.
                    _ = _windows.Remove(hWnd);
                    return nint.Zero;

                case WM_NCCALCSIZE:
                    // For custom title bar:
                    // 1) call DefWindowProc first to keep native NC contract intact
                    // 2) in normal state, use full original rect as client area
                    // 3) in maximized state, clamp to monitor work area
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        // Per WM_NCCALCSIZE contract, return 0 when wParam == FALSE.
                        if (wParam == nint.Zero)
                        {
                            return nint.Zero;
                        }

                        // Save pre-DefWindowProc rect before NC calculations mutate it.
                        var ncParams = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(lParam);
                        var originalRect = (ncParams.rgrc0.left, ncParams.rgrc0.top, ncParams.rgrc0.right, ncParams.rgrc0.bottom);

                        // Let DefWindowProc compute default non-client metrics first.
                        var defResult = DefWindowProc(hWnd, msg, wParam, lParam);
                        if (defResult != nint.Zero)
                        {
                            return defResult;
                        }

                        // Re-read DefWindowProc-computed rect and only apply minimal fixups.
                        ncParams = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(lParam);
                        var defClientRect = (ncParams.rgrc0.left, ncParams.rgrc0.top, ncParams.rgrc0.right, ncParams.rgrc0.bottom);
                        var maximizedWorkArea = ((int left, int top, int right, int bottom)?)null;
                        bool isMaximized = IsZoomed(hWnd);

                        if (isMaximized)
                        {
                            // When maximized, adjust to work area to respect the taskbar.
                            var monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                            MONITORINFO monitorInfo = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                            if (GetMonitorInfo(monitor, ref monitorInfo))
                            {
                                maximizedWorkArea = (monitorInfo.rcWork.left, monitorInfo.rcWork.top, monitorInfo.rcWork.right, monitorInfo.rcWork.bottom);
                            }
                        }

                        var computedRect = ComputeCustomNcCalcSizeRect(originalRect, defClientRect, isMaximized, maximizedWorkArea);
                        ncParams.rgrc0.left = computedRect.left;
                        ncParams.rgrc0.top = computedRect.top;
                        ncParams.rgrc0.right = computedRect.right;
                        ncParams.rgrc0.bottom = computedRect.bottom;

                        Marshal.StructureToPtr(ncParams, lParam, false);
                        return nint.Zero;
                    }
                    break;

                case WM_NCHITTEST:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        var customHitResult = window.HandleNcHitTest(lParam);

                        if (window.ShouldUseWin11SnapNcRouting() &&
                            DwmDefWindowProc(hWnd, msg, wParam, lParam, out nint dwmHitResult))
                        {
                            // Keep custom caption-button hit-testing in charge so styled
                            // button dimensions can enlarge WM_NCHITTEST regions.
                            if (IsCaptionButtonNcHit(customHitResult))
                            {
                                return customHitResult;
                            }

                            if (dwmHitResult != nint.Zero)
                            {
                                return dwmHitResult;
                            }
                        }

                        if (customHitResult != HTNOWHERE)
                        {
                            return customHitResult;
                        }
                    }
                    break;

                case WM_NCMOUSEMOVE:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        window.OnNcMouseMove(wParam, lParam);
                        if (window.ShouldUseWin11SnapNcRouting())
                        {
                            _ = DwmDefWindowProc(hWnd, msg, wParam, lParam, out _);
                        }
                    }
                    break;

                case WM_NCMOUSEHOVER:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.ShouldUseWin11SnapNcRouting())
                        {
                            _ = DwmDefWindowProc(hWnd, msg, wParam, lParam, out _);
                        }
                    }
                    break;

                case WM_NCMOUSELEAVE:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        window.OnNcMouseLeave();
                        if (window.ShouldUseWin11SnapNcRouting())
                        {
                            _ = DwmDefWindowProc(hWnd, msg, wParam, lParam, out _);
                        }
                    }
                    break;

                case WM_NCLBUTTONDOWN:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.OnNcLButtonDown(wParam, lParam))
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCLBUTTONUP:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.OnNcLButtonUp(wParam, lParam))
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCLBUTTONDBLCLK:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.OnNcLButtonDblClk(wParam, lParam))
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCRBUTTONDOWN:
                case WM_NCRBUTTONUP:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.ShouldUseWin11SnapNcRouting())
                        {
                            break;
                        }

                        // Only suppress right-click when over a TitleBarButton
                        // Allow system menu on caption area (normal Windows behavior)
                        int ncRbX = (short)(lParam.ToInt64() & 0xFFFF);
                        int ncRbY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        POINT ncRbPt = new() { X = ncRbX, Y = ncRbY };
                        _ = ScreenToClient(hWnd, ref ncRbPt);
                        // Convert physical client pixels to DIPs
                        if (window.GetTitleBarButtonAtPoint(new Point(ncRbPt.X / window._dpiScale, ncRbPt.Y / window._dpiScale)) != null)
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_SIZE:
                    int sizeType = (int)wParam.ToInt64();
                    int width = (int)(lParam.ToInt64() & 0xFFFF);
                    int height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    // Synchronize WindowState with the actual window state
                    // This handles system-forced state changes (Win+Down, taskbar click, etc.)
                    // Use _isSyncingWindowState to prevent OnWindowStateChanged from
                    // calling ShowWindow again (which would cause infinite WM_SIZE loop).
                    window._isSyncingWindowState = true;
                    try
                    {
                        switch (sizeType)
                        {
                            case SIZE_MAXIMIZED:
                                if (window.WindowState != WindowState.Maximized)
                                {
                                    window.WindowState = WindowState.Maximized;
                                }
                                break;
                            case SIZE_RESTORED:
                                if (window.WindowState != WindowState.Normal)
                                {
                                    window.WindowState = WindowState.Normal;
                                }
                                break;
                            case SIZE_MINIMIZED:
                                if (window.WindowState != WindowState.Minimized)
                                {
                                    window.WindowState = WindowState.Minimized;
                                }
                                return nint.Zero; // finally block ensures _isSyncingWindowState is reset
                        }
                    }
                    finally
                    {
                        window._isSyncingWindowState = false;
                    }

                    window.OnSizeChanged(width, height);

                    // For maximize/restore, post a deferred repaint message
                    if (sizeType is SIZE_MAXIMIZED or SIZE_RESTORED)
                    {
                        _ = PostMessage(hWnd, WM_APP_REPAINT, nint.Zero, nint.Zero);
                    }
                    return nint.Zero;

                case WM_MOVING:
                    break; // Allow default processing

                case WM_MOVE:
                    window.LocationChanged?.Invoke(window, EventArgs.Empty);
                    // Re-detect refresh rate (window may have moved to a different monitor)
                    CompositionTarget.UpdateRefreshRate(window.DetectMonitorRefreshRate());
                    return nint.Zero;

                case WM_DISPLAYCHANGE:
                    // Display settings changed (resolution, refresh rate, etc.)
                    CompositionTarget.UpdateRefreshRate(window.DetectMonitorRefreshRate());
                    return nint.Zero;

                case WM_DPICHANGED:
                {
                    // Per-monitor DPI change (window moved to different DPI monitor)
                    uint newDpi = (uint)((wParam.ToInt64() >> 16) & 0xFFFF);
                    window._dpiScale = newDpi / 96.0;

                    // Update DPI BEFORE SetWindowPos: SetWindowPos triggers WM_SIZE
                    // synchronously, which calls Resize() → CreateSnapshotResources().
                    // Snapshot bitmaps bake DPI into their D2D1_BITMAP_PROPERTIES1,
                    // so dpiX_/dpiY_ must already reflect the new DPI at that point.
                    window.RenderTarget?.SetDpi((float)newDpi, (float)newDpi);

                    // Windows provides a suggested window rect at the new DPI
                    var suggestedRect = Marshal.PtrToStructure<RECT>(lParam);
                    _ = SetWindowPos(hWnd, nint.Zero,
                        suggestedRect.left, suggestedRect.top,
                        suggestedRect.right - suggestedRect.left,
                        suggestedRect.bottom - suggestedRect.top,
                        SWP_NOZORDER | SWP_NOACTIVATE);

                    // Keep DWM extended-frame hover region in sync with new DPI.
                    window.UpdateCustomTitleBarFrameMargins();

                    // Re-detect refresh rate (different monitor may have different rate)
                    CompositionTarget.UpdateRefreshRate(window.DetectMonitorRefreshRate());

                    return nint.Zero;
                }

                case WM_APP_REPAINT:
                    // Deferred repaint after size change
                    _ = RedrawWindow(hWnd, nint.Zero, nint.Zero, RDW_INVALIDATE | RDW_UPDATENOW);
                    return nint.Zero;

                case WM_SIZING:
                    // IMPORTANT: do not derive layout size from WM_SIZING RECT.
                    // WM_SIZING provides outer window bounds (includes non-client frame),
                    // while our layout uses client-area size (from WM_SIZE). Mixing these
                    // two sources causes width oscillation during drag resize and makes
                    // title bar content appear to shift left/right.
                    if (window._isSizing)
                    {
                        _ = RedrawWindow(hWnd, nint.Zero, nint.Zero, RDW_INVALIDATE | RDW_UPDATENOW);
                    }
                    break;

                case WM_ENTERSIZEMOVE:
                    window._isSizing = true;
                    // Disable VSync during resize for faster frame updates
                    window.RenderTarget?.SetVSyncEnabled(false);
                    return nint.Zero;

                case WM_EXITSIZEMOVE:
                    window._isSizing = false;
                    // Re-enable VSync after resize
                    window.RenderTarget?.SetVSyncEnabled(true);
                    // Do final resize to ensure correct buffer size (physical pixels)
                    int finalPhysW = (int)(window.Width * window._dpiScale);
                    int finalPhysH = (int)(window.Height * window._dpiScale);
                    window.RenderTarget?.Resize(finalPhysW, finalPhysH);
                    // Force a final repaint with correct buffer size
                    _ = RedrawWindow(hWnd, nint.Zero, nint.Zero, RDW_INVALIDATE | RDW_UPDATENOW);
                    return nint.Zero;

                case WM_ERASEBKGND:
                    // Return 1 to tell Windows we've handled background erase
                    // This prevents flickering during resize
                    return 1;

                case WM_PAINT:
                    window.OnPaint();
                    return nint.Zero;

                // Keyboard input
                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                    window.OnKeyDown(wParam, lParam);
                    return nint.Zero;

                case WM_KEYUP:
                case WM_SYSKEYUP:
                    window.OnKeyUp(wParam, lParam);
                    return nint.Zero;

                case WM_CHAR:
                    window.OnChar(wParam, lParam);
                    return nint.Zero;

                // IME input
                case WM_IME_STARTCOMPOSITION:
                    window.OnImeStartComposition();
                    return nint.Zero;

                case WM_IME_ENDCOMPOSITION:
                    window.OnImeEndComposition();
                    return nint.Zero;

                case WM_IME_COMPOSITION:
                    if (window.OnImeComposition(lParam))
                    {
                        return nint.Zero;
                    }

                    break;

                case WM_IME_CHAR:
                    // IME character - let it fall through to default processing
                    // or handle specially if needed
                    break;

                // Cursor
                case WM_SETCURSOR:
                    if (window.OnSetCursor(lParam))
                    {
                        return 1; // Return TRUE to indicate we handled the message
                    }
                    break;

                // Mouse input
                case Win32PointerInterop.WM_POINTERDOWN:
                case Win32PointerInterop.WM_POINTERUPDATE:
                case Win32PointerInterop.WM_POINTERUP:
                    window.OnPointerMessage(msg, wParam, lParam);
                    return nint.Zero;

                case Win32PointerInterop.WM_POINTERWHEEL:
                case Win32PointerInterop.WM_POINTERHWHEEL:
                    window.OnPointerWheel(wParam, lParam);
                    return nint.Zero;

                case Win32PointerInterop.WM_POINTERCAPTURECHANGED:
                    window.OnPointerCaptureChanged(wParam);
                    return nint.Zero;

                case WM_MOUSEMOVE:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseMove(wParam, lParam);
                    return nint.Zero;

                case WM_LBUTTONDOWN:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseButtonDown(MouseButton.Left, wParam, lParam, clickCount: 1);
                    return nint.Zero;

                case WM_LBUTTONDBLCLK:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseButtonDown(MouseButton.Left, wParam, lParam, clickCount: 2);
                    return nint.Zero;

                case WM_LBUTTONUP:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseButtonUp(MouseButton.Left, wParam, lParam);
                    return nint.Zero;

                case WM_RBUTTONDOWN:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseButtonDown(MouseButton.Right, wParam, lParam, clickCount: 1);
                    return nint.Zero;

                case WM_RBUTTONDBLCLK:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseButtonDown(MouseButton.Right, wParam, lParam, clickCount: 2);
                    return nint.Zero;

                case WM_RBUTTONUP:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseButtonUp(MouseButton.Right, wParam, lParam);
                    return nint.Zero;

                case WM_MBUTTONDOWN:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseButtonDown(MouseButton.Middle, wParam, lParam, clickCount: 1);
                    return nint.Zero;

                case WM_MBUTTONDBLCLK:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseButtonDown(MouseButton.Middle, wParam, lParam, clickCount: 2);
                    return nint.Zero;

                case WM_MBUTTONUP:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseButtonUp(MouseButton.Middle, wParam, lParam);
                    return nint.Zero;

                case WM_MOUSEWHEEL:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    window.OnMouseWheel(wParam, lParam);
                    return nint.Zero;

                case WM_MOUSELEAVE:
                    window.OnMouseLeave();
                    return nint.Zero;

                case WM_CAPTURECHANGED:
                    // Native capture was lost (another window took it, or system released it).
                    // Sync managed state — don't call ReleaseCapture again since it's already gone.
                    UIElement.OnNativeCaptureChanged();
                    window.ClearMousePressedChain();
                    return nint.Zero;

                case WM_ACTIVATE:
                    // When parent window is deactivated, close light-dismiss external popups
                    // unless the new foreground window is one of our popup windows
                    int activateState = (int)(wParam.ToInt64() & 0xFFFF);
                    if (activateState == WA_INACTIVE && window.ActiveExternalPopups.Count > 0)
                    {
                        nint newForeground = lParam; // lParam = handle of window being activated
                        if (!PopupWindow.IsPopupWindow(newForeground))
                        {
                            var popupsToClose = window.ActiveExternalPopups
                                .Where(p => !p.StaysOpen).ToList();
                            foreach (var popup in popupsToClose)
                                popup.IsOpen = false;
                        }
                    }
                    if (activateState == WA_INACTIVE)
                    {
                        window.ClearPressedChains();
                    }
                    break;
            }
        }

        if (hWnd == nint.Zero)
        {
            return nint.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnMouseLeave()
    {
        // Clear title bar button hover state when mouse leaves the window
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            ClearTitleBarInteractionState();
        }

        ClearMousePressedChain();

        // Clear general mouse over state
        if (_lastMouseOverElement != null)
        {
            RaiseMouseLeaveChain(_lastMouseOverElement, null, Environment.TickCount);
            _lastMouseOverElement = null;
        }
    }

    /// <summary>
    /// Handles window size changes. Width/height are physical pixels from WM_SIZE.
    /// </summary>
    private void OnSizeChanged(int physicalWidth, int physicalHeight)
    {
        if (physicalWidth <= 0 || physicalHeight <= 0)
        {
            return;
        }

        // Convert physical pixels to DIPs for layout
        Width = physicalWidth / _dpiScale;
        Height = physicalHeight / _dpiScale;

        // Always use WM_SIZE client dimensions as the single source of truth.
        // This keeps layout and swapchain size stable during drag resize.
        RenderTarget?.Resize(physicalWidth, physicalHeight);
        InvalidateMeasure();
    }

    private RenderTargetDrawingContext? _drawingContext;
    private UIElement? _lastMouseOverElement;
    private readonly List<UIElement> _mousePressedChain = [];
    private readonly List<UIElement> _keyboardPressedChain = [];
    private bool _keyboardPressActive;

    /// <summary>
    /// WM_PAINT handler. Used for OS-initiated repaints (window uncovered, initial show, resize).
    /// Validates the update region via BeginPaint/EndPaint and delegates to RenderFrame.
    /// </summary>
    private void OnPaint()
    {
        PAINTSTRUCT ps = new();
        _ = BeginPaint(Handle, out ps);
        RenderFrame();
        EndPaint(Handle, ref ps);
    }

    /// <summary>
    /// Processes a scheduled render from the Dispatcher queue.
    /// This is the primary render path — called via Dispatcher.BeginInvokeCritical
    /// after InvalidateMeasure/InvalidateArrange/InvalidateVisual.
    ///
    /// WPF-style: rendering is a Dispatcher operation, not WM_PAINT.
    /// When DispatcherTimer ticks (animations) call BeginInvoke(RaiseTick),
    /// the tick handler invalidates elements which calls BeginInvokeCritical(ProcessRender).
    /// ProcessQueue drains all items in FIFO order, so ProcessRender runs
    /// immediately after all ticks in the same batch — no WM_PAINT starvation.
    /// </summary>
    private void ProcessRender()
    {
        _renderScheduled = false;
        if (Handle == nint.Zero) return;
        RenderFrame();
    }

    /// <summary>
    /// Core rendering logic shared by both Dispatcher-based and WM_PAINT paths.
    /// Performs layout, submits dirty rects, and renders the visual tree.
    /// </summary>
    private void RenderFrame()
    {
        if (_isRendering) return;
        _isRendering = true;
        _renderRequested = false;

        try
        {
            if (RenderTarget != null && RenderTarget.IsValid)
            {
                // Perform layout before rendering (queue-based: only dirty elements)
                UpdateLayout();

                // Dirty rendering strategy:
                // - Dirty elements drive SCHEDULING (whether to render at all)
                // - When idle, no render → GPU idle
                // - When rendering, always full Clear + full tree render + full Present
                //
                // Present1 partial dirty rects are NOT used because with FLIP_SEQUENTIAL,
                // we Clear the entire back buffer each frame, so telling DWM only partial
                // rects changed causes cumulative stale content on screen.
                // The GPU savings come from not rendering at all when idle, not from
                // partial DWM compositing.
                lock (_dirtyLock)
                {
                    _dirtyElements.Clear();
                    _fullInvalidation = false;
                }
                RenderTarget.SetFullInvalidation();

                RenderTarget.BeginDraw();

                // Always clear and render the full window.
                // No D2D clip — the full back buffer is redrawn every dirty frame.
                // Present1 dirty rects handle DWM-level optimization.
                if (Background is SolidColorBrush solidBrush)
                {
                    var color = solidBrush.Color;
                    RenderTarget.Clear(color.ScR, color.ScG, color.ScB, color.ScA);
                }
                else
                {
                    RenderTarget.Clear(1.0f, 1.0f, 1.0f, 1.0f);
                }

                var context = RenderContext.Current;
                if (context != null)
                {
                    _drawingContext ??= new RenderTargetDrawingContext(RenderTarget, context);
                    _drawingContext.Offset = Point.Zero;
                    Render(_drawingContext);

#if DEBUG
                    // Draw DevTools highlight overlay if active
                    DevToolsOverlay?.DrawOverlay(_drawingContext);
#endif
                }

                // Also call legacy OnRender for backwards compatibility
                OnRender(RenderTarget);

                RenderTarget.EndDraw();

                // Trim caches to prevent memory from growing unbounded
                _drawingContext?.TrimCacheIfNeeded();
            }
        }
        catch (Exception ex)
        {
            LogRenderFailure(ex, "RenderFrame");
            throw;
        }
        finally
        {
            _isRendering = false;
        }

        // If something requested a render during our rendering
        // (e.g., UpdateLayout triggered further invalidation),
        // schedule another render cycle.
        // When CompositionTarget is active, clear the flag — the next frame will
        // render all dirty elements. Don't call InvalidateWindow (it would be
        // blocked by the IsActive check anyway).
        if (_renderRequested)
        {
            _renderRequested = false;
            if (!CompositionTarget.IsActive)
            {
                InvalidateWindow();
            }
        }
    }

    private sealed class PointerManipulationSession
    {
        public PointerManipulationSession(UIElement target, Point origin, int timestamp)
        {
            Target = target;
            Origin = origin;
            LastPoint = origin;
            LastTimestamp = timestamp;
            CumulativeTranslation = Vector.Zero;
            LastVelocity = Vector.Zero;
        }

        public UIElement Target { get; }
        public Point Origin { get; }
        public Point LastPoint { get; set; }
        public int LastTimestamp { get; set; }
        public Vector CumulativeTranslation { get; set; }
        public Vector LastVelocity { get; set; }
    }

    private void LogRenderFailure(Exception exception, string fallbackStage)
    {
        _ = exception;
        _ = fallbackStage;
    }

    /// <summary>
    /// Updates the layout of all elements in this window.
    /// Uses LayoutManager for queue-based processing: only dirty elements are re-measured/re-arranged.
    /// </summary>
    private void UpdateLayout()
    {
        Size availableSize = new(Width, Height);

        if (_isFirstLayout)
        {
            _isFirstLayout = false;
            Measure(availableSize);
            Arrange(new Rect(0, 0, availableSize.Width, availableSize.Height));
            return;
        }

        _layoutManager.UpdateLayout(this, availableSize);
    }

    /// <summary>
    /// Detects the refresh rate of the monitor displaying this window.
    /// </summary>
    /// <returns>The refresh rate in Hz (e.g., 60, 120, 144), or 60 as fallback.</returns>
    private int DetectMonitorRefreshRate()
    {
        if (Handle == nint.Zero) return 60;

        var hMonitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
        MONITORINFOEX monitorInfoEx = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };

        if (GetMonitorInfoEx(hMonitor, ref monitorInfoEx))
        {
            DEVMODE devMode = new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(monitorInfoEx.szDevice, ENUM_CURRENT_SETTINGS, ref devMode))
            {
                return (int)devMode.dmDisplayFrequency;
            }
        }

        return 60;
    }

    /// <summary>
    /// Called by CompositionTarget.FrameStarting at the start of each frame,
    /// BEFORE animation handlers run. If any InvalidateWindow calls were blocked
    /// between frames (hover changes, mouse tracking, property updates), this
    /// ensures a render is scheduled for the current frame so those dirty elements
    /// get painted along with the animation updates.
    /// </summary>
    private void OnFrameStarting()
    {
        if (_dirtyBetweenFrames)
        {
            _dirtyBetweenFrames = false;
            if (!_renderScheduled)
            {
                _renderScheduled = true;
                _dispatcher?.BeginInvokeCritical(ProcessRender);
            }
        }
    }

    /// <summary>
    /// Schedules a render via Dispatcher.BeginInvokeCritical (WPF-style).
    /// Implements IWindowHost.InvalidateWindow.
    ///
    /// Unlike InvalidateRect → WM_PAINT (which is low-priority and gets starved
    /// by posted messages from DispatcherTimer), this enqueues a render directly
    /// in the Dispatcher queue. ProcessQueue drains all items, so the render
    /// runs right after animation ticks in the same batch.
    ///
    /// iGPU optimization: when CompositionTarget is active (animations running),
    /// only allow renders triggered during the Rendering event phase.
    /// Mouse/interaction-triggered renders between frames are suppressed —
    /// dirty elements are batched into the next CompositionTarget frame.
    /// This prevents render storms on slow GPUs (200ms render + immediate
    /// mouse render + immediate timer render = frozen UI).
    /// </summary>
    public void InvalidateWindow()
    {
        if (Handle == nint.Zero) return;

        // During rendering, don't schedule — just flag for re-render after current frame
        if (_isRendering)
        {
            _renderRequested = true;
            return;
        }

        // When the centralized frame timer is active, only allow renders triggered
        // during CompositionTarget.Rendering (animation handlers). Between frames,
        // mouse drags / property changes just mark elements dirty via AddDirtyElement —
        // they'll be rendered in the next animation frame via FrameStarting.
        // This ensures exactly ONE render per frame interval, leaving gaps for
        // the message pump to process input.
        if (CompositionTarget.IsActive && !CompositionTarget.IsInRenderingPhase)
        {
            _dirtyBetweenFrames = true;
            return;
        }

        if (!_renderScheduled)
        {
            _renderScheduled = true;
            // Use stored UI thread Dispatcher — NOT Dispatcher.CurrentDispatcher,
            // which returns null on thread-pool threads (System.Threading.Timer callbacks,
            // Storyboard ticks, etc.) and would silently drop the render request.
            _dispatcher?.BeginInvokeCritical(ProcessRender);
        }
    }

    /// <summary>
    /// Adds a dirty element for partial rendering via native dirty rects.
    /// </summary>
    public void AddDirtyElement(UIElement element)
    {
        // Thread-safe: background threads (System.Threading.Timer callbacks from
        // ProgressBar, Storyboard, caret timers) call InvalidateVisual → AddDirtyElement.
        lock (_dirtyLock)
        {
            // Only capture pre-layout bounds on first registration per frame.
            // This preserves the true "old" position before UpdateLayout moves things.
            if (!_dirtyElements.ContainsKey(element))
            {
                _dirtyElements[element] = element.GetScreenBounds();
            }
        }
    }

    /// <summary>
    /// Requests a full invalidation of the window (e.g., after layout changes).
    /// </summary>
    public void RequestFullInvalidation()
    {
        _fullInvalidation = true;
    }

    /// <summary>
    /// Calls Win32 SetCapture so the window receives mouse messages even when the cursor is outside.
    /// </summary>
    public void SetNativeCapture()
    {
        if (Handle != nint.Zero)
            SetCapture(Handle);
    }

    /// <summary>
    /// Calls Win32 ReleaseCapture to stop capturing mouse messages outside the window.
    /// </summary>
    public void ReleaseNativeCapture()
    {
        _ = ReleaseCapture();
    }

    /// <summary>
    /// Forces an immediate synchronous render of the window.
    /// Used for offline frame-by-frame rendering (e.g., video production).
    /// </summary>
    public void ForceRenderFrame()
    {
        RequestFullInvalidation();
        RenderFrame();
    }

    /// <summary>
    /// Called to render the window content.
    /// </summary>
    /// <param name="renderTarget">The render target to draw on.</param>
    protected virtual void OnRender(RenderTarget renderTarget)
    {
        // Base implementation renders nothing
        // Derived classes can override to add custom rendering
    }

    #endregion

    #region Input Handling

    private void OnKeyDown(nint wParam, nint lParam)
    {
        Key key = (Key)(int)wParam;
        var modifiers = GetModifierKeys();
        bool isRepeat = ((lParam.ToInt64() >> 30) & 1) != 0;
        int timestamp = Environment.TickCount;

#if DEBUG
        // F12 opens DevTools (only in DEBUG builds)
        // Skip if this window cannot open DevTools (e.g., DevToolsWindow itself)
        if (key == Key.F12 && !isRepeat && CanOpenDevTools)
        {
            ToggleDevTools();
            return;
        }

        // Ctrl+Shift+C activates element picker (opens DevTools if not open)
        if (key == Key.C && !isRepeat && CanOpenDevTools &&
            (modifiers & ModifierKeys.Control) != 0 && (modifiers & ModifierKeys.Shift) != 0)
        {
            OpenDevTools();
            _devToolsWindow?.ActivatePicker();
            return;
        }
#endif

        // Route keyboard events to the focused element, or the window if no element has focus
        var target = Keyboard.FocusedElement as UIElement ?? this;

        if (!isRepeat && (key == Key.Space || key == Key.Enter))
        {
            ActivateKeyboardPressedChain(target);
        }

        // Raise tunnel event (PreviewKeyDown)
        KeyEventArgs tunnelArgs = new(PreviewKeyDownEvent, key, modifiers, isDown: true, isRepeat, timestamp);
        target.RaiseEvent(tunnelArgs);

        // Raise bubble event (KeyDown) if not handled
        if (!tunnelArgs.Handled)
        {
            KeyEventArgs bubbleArgs = new(KeyDownEvent, key, modifiers, isDown: true, isRepeat, timestamp);
            target.RaiseEvent(bubbleArgs);

            // Auto Tab/Shift+Tab focus navigation (if not handled by any control)
            if (!bubbleArgs.Handled && key == Key.Tab)
            {
                var reverse = (modifiers & ModifierKeys.Shift) != 0;
                if (target is UIElement focusedElement)
                {
                    KeyboardNavigation.MoveFocus(focusedElement, reverse);
                }
            }

            // IsDefault (Enter) / IsCancel (Escape) button handling
            if (!bubbleArgs.Handled && !isRepeat)
            {
                if (key == Key.Enter)
                {
                    var defaultButton = FindButton(this, b => b.IsDefault);
                    defaultButton?.PerformClick();
                }
                else if (key == Key.Escape)
                {
                    var cancelButton = FindButton(this, b => b.IsCancel);
                    cancelButton?.PerformClick();
                }
            }
        }
    }

    private static Button? FindButton(UIElement root, Func<Button, bool> predicate)
    {
        if (root is Button btn && predicate(btn))
            return btn;

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is UIElement child)
            {
                var found = FindButton(child, predicate);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

#if DEBUG
    /// <summary>
    /// Toggles the DevTools window for this window.
    /// Press F12 to open/close DevTools in DEBUG builds.
    /// </summary>
    public void ToggleDevTools()
    {
        if (_devToolsWindow != null)
        {
            // Close existing DevTools
            _devToolsWindow.CloseDevTools();
            _devToolsWindow = null;
            DevToolsOverlay = null;
        }
        else
        {
            // Open new DevTools
            _devToolsWindow = new DevToolsWindow(this);
            _devToolsWindow.Closed += (_, _) =>
            {
                _devToolsWindow = null;
                DevToolsOverlay = null;
            };
            _devToolsWindow.Show();
        }
    }

    /// <summary>
    /// Opens the DevTools window for this window.
    /// </summary>
    public void OpenDevTools()
    {
        if (_devToolsWindow == null)
        {
            ToggleDevTools();
        }
    }

    /// <summary>
    /// Closes the DevTools window for this window.
    /// </summary>
    public void CloseDevTools()
    {
        if (_devToolsWindow != null)
        {
            _devToolsWindow.CloseDevTools();
            _devToolsWindow = null;
            DevToolsOverlay = null;
        }
    }
#endif

    private void OnKeyUp(nint wParam, nint lParam)
    {
        Key key = (Key)(int)wParam;
        var modifiers = GetModifierKeys();
        int timestamp = Environment.TickCount;

        // Route keyboard events to the focused element, or the window if no element has focus
        var target = Keyboard.FocusedElement as UIElement ?? this;

        // Raise tunnel event (PreviewKeyUp)
        KeyEventArgs tunnelArgs = new(PreviewKeyUpEvent, key, modifiers, isDown: false, isRepeat: false, timestamp);
        target.RaiseEvent(tunnelArgs);

        // Raise bubble event (KeyUp) if not handled
        if (!tunnelArgs.Handled)
        {
            KeyEventArgs bubbleArgs = new(KeyUpEvent, key, modifiers, isDown: false, isRepeat: false, timestamp);
            target.RaiseEvent(bubbleArgs);
        }

        if (key == Key.Space || key == Key.Enter)
        {
            ClearKeyboardPressedChain();
        }
    }

    private void OnChar(nint wParam, nint lParam)
    {
        char c = (char)(int)wParam;
        if (char.IsControl(c) && c != '\r' && c != '\t')
        {
            return; // Skip control chars except Enter/Tab
        }

        string text = c.ToString();
        int timestamp = Environment.TickCount;

        // Route text input to the focused element, or the window if no element has focus
        var target = Keyboard.FocusedElement as UIElement ?? this;

        // Raise tunnel event (PreviewTextInput)
        TextCompositionEventArgs tunnelArgs = new(PreviewTextInputEvent, text, timestamp);
        target.RaiseEvent(tunnelArgs);

        // Raise bubble event (TextInput) if not handled
        if (!tunnelArgs.Handled)
        {
            TextCompositionEventArgs bubbleArgs = new(TextInputEvent, text, timestamp);
            target.RaiseEvent(bubbleArgs);
        }
    }

    #region IME Handling

    private void OnImeStartComposition()
    {
        InputMethod.StartComposition();

        // Position the IME composition window near the caret
        UpdateImeCompositionWindow();
    }

    private void OnImeEndComposition()
    {
        InputMethod.EndComposition();
    }

    private bool OnImeComposition(nint lParam)
    {
        var hImc = ImmNativeMethods.ImmGetContext(Handle);
        if (hImc == nint.Zero)
        {
            return false;
        }

        try
        {
            int flags = (int)lParam;

            // Check for result string (final committed text)
            if ((flags & ImmNativeMethods.GCS_RESULTSTR) != 0)
            {
                string resultStr = GetCompositionString(hImc, ImmNativeMethods.GCS_RESULTSTR);
                if (!string.IsNullOrEmpty(resultStr))
                {
                    // Send the committed text as TextInput
                    var target = Keyboard.FocusedElement as UIElement ?? this;
                    TextCompositionEventArgs args = new(TextInputEvent, resultStr, Environment.TickCount);
                    target.RaiseEvent(args);
                    InputMethod.EndComposition(resultStr);
                }
            }

            // Check for composition string (in-progress text)
            if ((flags & ImmNativeMethods.GCS_COMPSTR) != 0)
            {
                string compStr = GetCompositionString(hImc, ImmNativeMethods.GCS_COMPSTR);
                int cursor = 0;

                if ((flags & ImmNativeMethods.GCS_CURSORPOS) != 0)
                {
                    cursor = ImmNativeMethods.ImmGetCompositionString(hImc, ImmNativeMethods.GCS_CURSORPOS, null, 0);
                }

                InputMethod.UpdateComposition(compStr, cursor);
            }

            return true;
        }
        finally
        {
            _ = ImmNativeMethods.ImmReleaseContext(Handle, hImc);
        }
    }

    private static string GetCompositionString(nint hImc, int dwIndex)
    {
        int len = ImmNativeMethods.ImmGetCompositionString(hImc, dwIndex, null, 0);
        if (len <= 0)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[len];
        _ = ImmNativeMethods.ImmGetCompositionString(hImc, dwIndex, buffer, len);

        // IME returns UTF-16LE encoded string
        return System.Text.Encoding.Unicode.GetString(buffer);
    }

    /// <summary>
    /// Updates the IME composition window position to be near the caret.
    /// </summary>
    public void UpdateImeCompositionWindow()
    {
        var target = Keyboard.FocusedElement as UIElement;
        if (target == null || target is not IImeSupport imeSupport)
        {
            return;
        }

        var hImc = ImmNativeMethods.ImmGetContext(Handle);
        if (hImc == nint.Zero)
        {
            return;
        }

        try
        {
            // Convert focused element local caret position (DIPs) to client-area physical pixels.
            Point caretPosDip = imeSupport.GetImeCaretPosition();
            if (target is FrameworkElement frameworkElement)
            {
                var targetOriginDip = frameworkElement.TransformToAncestor(null);
                caretPosDip = new Point(targetOriginDip.X + caretPosDip.X, targetOriginDip.Y + caretPosDip.Y);
            }

            int caretX = (int)Math.Round(caretPosDip.X * _dpiScale);
            int caretY = (int)Math.Round(caretPosDip.Y * _dpiScale);

            ImmNativeMethods.COMPOSITIONFORM form = new()
            {
                dwStyle = ImmNativeMethods.CFS_POINT,
                ptCurrentPos = new ImmNativeMethods.POINT { x = caretX, y = caretY }
            };

            _ = ImmNativeMethods.ImmSetCompositionWindow(hImc, ref form);

            // Also set candidate window position
            ImmNativeMethods.CANDIDATEFORM candidate = new()
            {
                dwIndex = 0,
                dwStyle = ImmNativeMethods.CFS_CANDIDATEPOS,
                ptCurrentPos = new ImmNativeMethods.POINT { x = caretX, y = caretY }
            };

            _ = ImmNativeMethods.ImmSetCandidateWindow(hImc, ref candidate);
        }
        finally
        {
            _ = ImmNativeMethods.ImmReleaseContext(Handle, hImc);
        }
    }

    #endregion

    private void OnMouseMove(nint wParam, nint lParam)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int timestamp = Environment.TickCount;

        // Check for title bar button hover (for custom title bar)
        if (IsTitleBarVisible())
        {
            var titleBarButton = GetTitleBarButtonAtPoint(position);
            UpdateTitleBarButtonHover(titleBarButton);

            // Track mouse leave for client area
            TRACKMOUSEEVENT tme = new()
            {
                cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE,
                hwndTrack = Handle,
                dwHoverTime = 0
            };
            _ = TrackMouseEvent(ref tme);
        }

        // If an element has captured the mouse, it receives all mouse events
        // Otherwise, find the target element via hit testing
        var captured = UIElement.MouseCapturedElement;
        UIElement? hitElement = HitTest(position)?.VisualHit as UIElement;
        var target = captured ?? hitElement ?? this;

        // Track mouse over state and raise MouseEnter/MouseLeave events
        var newMouseOverElement = hitElement;
        if (newMouseOverElement != _lastMouseOverElement)
        {
            // Raise MouseLeave for the old element and its ancestors that are no longer under the mouse
            if (_lastMouseOverElement != null)
            {
                RaiseMouseLeaveChain(_lastMouseOverElement, newMouseOverElement, timestamp);
            }

            // Raise MouseEnter for the new element and its ancestors that weren't previously under the mouse
            if (newMouseOverElement != null)
            {
                RaiseMouseEnterChain(newMouseOverElement, _lastMouseOverElement, timestamp);
            }

            _lastMouseOverElement = newMouseOverElement;
        }

        // Raise tunnel event (PreviewMouseMove)
        MouseEventArgs tunnelArgs = new(
            PreviewMouseMoveEvent, position,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event (MouseMove) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseEventArgs bubbleArgs = new(
                MouseMoveEvent, position,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        PointerPoint pointerPoint = CreateMousePointerPoint(
            position,
            left, middle, right, xButton1, xButton2,
            modifiers,
            timestamp,
            PointerUpdateKind.Other);
        _activePointerTargets[MousePointerId] = target;
        _lastPointerPoints[MousePointerId] = pointerPoint;

        if (sourceCanceled)
        {
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
        }
        else if (!sourceHandled)
        {
            RaisePointerMovePipeline(target, pointerPoint, modifiers, timestamp);
        }
    }

    private void RaiseMouseLeaveChain(UIElement oldElement, UIElement? newElement, int timestamp)
    {
        // Build the ancestor chain of the new element for comparison
        HashSet<UIElement> newAncestors = [];
        Visual? current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                _ = newAncestors.Add(uiElement);
            }

            current = current.VisualParent;
        }

        // Raise MouseLeave for elements that are no longer under the mouse
        current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (newAncestors.Contains(uiElement))
                {
                    break; // Stop at common ancestor
                }

                uiElement.SetIsMouseOver(false);
                RoutedEventArgs args = new(MouseLeaveEvent, uiElement);
                uiElement.RaiseEvent(args);
            }
            current = current.VisualParent;
        }
    }

    private void RaiseMouseEnterChain(UIElement newElement, UIElement? oldElement, int timestamp)
    {
        // Build the ancestor chain of the old element for comparison
        HashSet<UIElement> oldAncestors = [];
        Visual? current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                _ = oldAncestors.Add(uiElement);
            }

            current = current.VisualParent;
        }

        // Collect elements that need MouseEnter (in reverse order, from ancestor to descendant)
        List<UIElement> enterElements = [];
        current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (oldAncestors.Contains(uiElement))
                {
                    break; // Stop at common ancestor
                }

                enterElements.Add(uiElement);
            }
            current = current.VisualParent;
        }

        // Raise MouseEnter from ancestor to descendant
        for (int i = enterElements.Count - 1; i >= 0; i--)
        {
            var uiElement = enterElements[i];
            uiElement.SetIsMouseOver(true);
            RoutedEventArgs args = new(MouseEnterEvent, uiElement);
            uiElement.RaiseEvent(args);
        }
    }

    private static void BuildAncestorChain(UIElement start, List<UIElement> chain)
    {
        chain.Clear();

        UIElement? current = start;
        while (current != null)
        {
            chain.Add(current);
            current = current.VisualParent as UIElement;
        }
    }

    private static void ApplyPressedState(List<UIElement> chain, bool isPressed)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            chain[i].SetIsPressed(isPressed);
        }
    }

    private void ActivateMousePressedChain(UIElement target)
    {
        ClearMousePressedChain();
        BuildAncestorChain(target, _mousePressedChain);
        ApplyPressedState(_mousePressedChain, isPressed: true);
    }

    private void ClearMousePressedChain()
    {
        if (_mousePressedChain.Count == 0)
        {
            return;
        }

        ApplyPressedState(_mousePressedChain, isPressed: false);
        _mousePressedChain.Clear();
    }

    private void ActivateKeyboardPressedChain(UIElement target)
    {
        ClearKeyboardPressedChain();
        BuildAncestorChain(target, _keyboardPressedChain);
        ApplyPressedState(_keyboardPressedChain, isPressed: true);
        _keyboardPressActive = true;
    }

    private void ClearKeyboardPressedChain()
    {
        if (_keyboardPressedChain.Count == 0 && !_keyboardPressActive)
        {
            return;
        }

        ApplyPressedState(_keyboardPressedChain, isPressed: false);
        _keyboardPressedChain.Clear();
        _keyboardPressActive = false;
    }

    private void ClearPressedChains()
    {
        ClearMousePressedChain();
        ClearKeyboardPressedChain();
    }

    private void OnMouseButtonDown(MouseButton button, nint wParam, nint lParam, int clickCount = 1)
    {
        var position = GetMousePosition(lParam);

        // Check light dismiss via OverlayLayer — clicks outside popups close them
        if (OverlayLayer.TryHandleLightDismiss(position))
        {
            _suppressMouseUpButton = button;
            return;
        }

        // Light dismiss for external popup windows (rendered outside the parent window)
        if (ActiveExternalPopups.Count > 0)
        {
            var popupsToClose = ActiveExternalPopups.Where(p => !p.StaysOpen).ToList();
            foreach (var popup in popupsToClose)
                popup.IsOpen = false;
            if (popupsToClose.Count > 0)
            {
                _suppressMouseUpButton = button;
                return;
            }
        }

        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int timestamp = Environment.TickCount;

        // Handle title bar button press (for custom title bar)
        if (IsTitleBarVisible() && button == MouseButton.Left)
        {
            var titleBarButton = GetTitleBarButtonAtPoint(position);
            if (titleBarButton != null)
            {
                ClearMousePressedChain();
                _pressedTitleBarButton = titleBarButton;
                titleBarButton.SetIsPressed(true);  // triggers InvalidateVisual() → dirty rect
                return; // Handled
            }
        }

        // If an element has captured the mouse, it receives all mouse events
        // Otherwise, find the target element via hit testing
        var captured = UIElement.MouseCapturedElement;
        var target = captured ?? HitTest(position)?.VisualHit as UIElement ?? this;

        if (button == MouseButton.Left)
        {
            ActivateMousePressedChain(target);
        }

        // Update button state for the pressed button
        var currentState = MouseButtonState.Pressed;

        // Raise tunnel event (PreviewMouseDown)
        MouseButtonEventArgs tunnelArgs = new(
            PreviewMouseDownEvent, position, button, currentState, clickCount,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event (MouseDown) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                MouseDownEvent, position, button, currentState, clickCount,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        PointerPoint pointerPoint = CreateMousePointerPoint(
            position,
            left, middle, right, xButton1, xButton2,
            modifiers,
            timestamp,
            MapMouseButtonToPointerUpdateKind(button, isPressed: true));
        _activePointerTargets[MousePointerId] = target;
        _lastPointerPoints[MousePointerId] = pointerPoint;

        if (sourceCanceled)
        {
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
        }
        else if (!sourceHandled)
        {
            RaisePointerDownPipeline(target, pointerPoint, modifiers, timestamp);
        }
    }

    private void OnMouseButtonUp(MouseButton button, nint wParam, nint lParam)
    {
        if (_suppressMouseUpButton == button)
        {
            _suppressMouseUpButton = null;
            if (button == MouseButton.Left)
            {
                ClearMousePressedChain();
            }
            return;
        }

        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int timestamp = Environment.TickCount;

        // Handle title bar button release (for custom title bar)
        if (IsTitleBarVisible() && button == MouseButton.Left && _pressedTitleBarButton != null)
        {
            var titleBarButton = GetTitleBarButtonAtPoint(position);
            _pressedTitleBarButton.SetIsPressed(false);

            // If released on the same button, trigger click
            if (titleBarButton == _pressedTitleBarButton)
            {
                switch (_pressedTitleBarButton.Kind)
                {
                    case TitleBarButtonKind.Minimize:
                        TitleBar?.RaiseMinimizeClicked();
                        break;
                    case TitleBarButtonKind.Maximize:
                    case TitleBarButtonKind.Restore:
                        TitleBar?.RaiseMaximizeRestoreClicked();
                        break;
                    case TitleBarButtonKind.Close:
                        TitleBar?.RaiseCloseClicked();
                        break;
                }
            }

            _pressedTitleBarButton = null;
            // SetIsPressed(false) above already triggers InvalidateVisual() → dirty rect
            ClearMousePressedChain();
            return; // Handled
        }

        // If an element has captured the mouse, it receives all mouse events
        // Otherwise, find the target element via hit testing
        var captured = UIElement.MouseCapturedElement;
        var target = captured ?? HitTest(position)?.VisualHit as UIElement ?? this;

        var currentState = MouseButtonState.Released;

        // Raise tunnel event (PreviewMouseUp)
        MouseButtonEventArgs tunnelArgs = new(
            PreviewMouseUpEvent, position, button, currentState, clickCount: 1,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event (MouseUp) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                MouseUpEvent, position, button, currentState, clickCount: 1,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        PointerPoint pointerPoint = CreateMousePointerPoint(
            position,
            left, middle, right, xButton1, xButton2,
            modifiers,
            timestamp,
            MapMouseButtonToPointerUpdateKind(button, isPressed: false));
        _lastPointerPoints[MousePointerId] = pointerPoint;

        if (sourceCanceled)
        {
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
        }
        else if (!sourceHandled)
        {
            RaisePointerUpPipeline(target, pointerPoint, modifiers, timestamp);
        }

        if (button == MouseButton.Left)
        {
            ClearMousePressedChain();
        }

        _activePointerTargets.Remove(MousePointerId);
    }

    // Light dismiss is now handled by OverlayLayer.TryHandleLightDismiss()

    private void OnMouseWheel(nint wParam, nint lParam)
    {
        // WM_MOUSEWHEEL lParam contains SCREEN coordinates (physical pixels).
        // Extract raw physical coords → ScreenToClient → convert to DIPs.
        int screenX = (short)(lParam.ToInt64() & 0xFFFF);
        int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = screenX, Y = screenY };
        _ = ScreenToClient(Handle, ref pt);
        // ScreenToClient returns physical client pixels → convert to DIPs
        Point position = new(pt.X / _dpiScale, pt.Y / _dpiScale);

        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        int timestamp = Environment.TickCount;

        // If an element has captured the mouse, it receives all mouse events
        // Otherwise, find the target element via hit testing
        var captured = UIElement.MouseCapturedElement;
        var target = captured ?? HitTest(position)?.VisualHit as UIElement ?? this;

        // Raise tunnel event (PreviewMouseWheel)
        MouseWheelEventArgs tunnelArgs = new(
            PreviewMouseWheelEvent, position, delta,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event (MouseWheel) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseWheelEventArgs bubbleArgs = new(
                MouseWheelEvent, position, delta,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        PointerPoint pointerPoint = CreateMousePointerPoint(
            position,
            left, middle, right, xButton1, xButton2,
            modifiers,
            timestamp,
            PointerUpdateKind.Other,
            mouseWheelDelta: delta);
        _lastPointerPoints[MousePointerId] = pointerPoint;

        if (sourceCanceled)
        {
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
        }
        else if (!sourceHandled)
        {
            RaisePointerWheelPipeline(target, pointerPoint, modifiers, timestamp);
        }
    }

    private void OnPointerMessage(uint msg, nint wParam, nint lParam)
    {
        if (!Win32PointerInterop.TryGetPointerData(Handle, wParam, _dpiScale, out var pointerData))
            return;

        // Mouse pointer goes through the existing WM_MOUSE promotion path.
        if (pointerData.Kind == Win32PointerKind.Mouse)
            return;

        bool isDown = msg == Win32PointerInterop.WM_POINTERDOWN;
        bool isUp = msg == Win32PointerInterop.WM_POINTERUP;
        int timestamp = Environment.TickCount;

        var captured = UIElement.MouseCapturedElement;
        var hitTarget = HitTest(pointerData.Position)?.VisualHit as UIElement;
        var fallbackTarget = captured ?? hitTarget ?? this;
        var target = isDown
            ? fallbackTarget
            : (_activePointerTargets.TryGetValue(pointerData.PointerId, out var existingTarget) ? existingTarget ?? fallbackTarget : fallbackTarget);

        _activePointerTargets[pointerData.PointerId] = target;
        _lastPointerPoints[pointerData.PointerId] = pointerData.Point;

        bool sourceHandled = false;
        bool sourceCanceled = pointerData.IsCanceled;

        if (pointerData.Kind == Win32PointerKind.Touch)
        {
            DispatchTouchSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        }
        else if (pointerData.Kind == Win32PointerKind.Pen)
        {
            DispatchStylusSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        }

        if (sourceCanceled)
        {
            CancelManipulationSession(pointerData.PointerId, timestamp);
            RaisePointerCancelPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            CleanupPointerSession(pointerData.PointerId);
            return;
        }

        DispatchManipulationPipeline(target, pointerData, isDown, isUp, sourceHandled, timestamp);

        if (!sourceHandled)
        {
            if (isDown)
            {
                RaisePointerDownPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            }
            else if (isUp)
            {
                RaisePointerUpPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            }
            else
            {
                RaisePointerMovePipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            }
        }

        if (isUp)
        {
            CleanupPointerSession(pointerData.PointerId);
        }
    }

    private void OnPointerWheel(nint wParam, nint lParam)
    {
        if (!Win32PointerInterop.TryGetPointerData(Handle, wParam, _dpiScale, out var pointerData))
            return;

        // Mouse wheel is already handled by WM_MOUSEWHEEL.
        if (pointerData.Kind == Win32PointerKind.Mouse)
            return;

        int timestamp = Environment.TickCount;
        var target = _activePointerTargets.TryGetValue(pointerData.PointerId, out var existingTarget)
            ? existingTarget ?? this
            : (HitTest(pointerData.Position)?.VisualHit as UIElement ?? this);

        if (pointerData.IsCanceled)
        {
            RaisePointerCancelPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            CleanupPointerSession(pointerData.PointerId);
            return;
        }

        RaisePointerWheelPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
    }

    private void OnPointerCaptureChanged(nint wParam)
    {
        uint pointerId = Win32PointerInterop.GetPointerId(wParam);

        if (_activePointerTargets.TryGetValue(pointerId, out var target) && target != null)
        {
            if (!_lastPointerPoints.TryGetValue(pointerId, out var point))
            {
                point = new PointerPoint(
                    pointerId,
                    new Point(0, 0),
                    PointerDeviceType.Touch,
                    false,
                    new PointerPointProperties(),
                    (ulong)Environment.TickCount);
            }

            CancelManipulationSession(pointerId, Environment.TickCount);
            RaisePointerCancelPipeline(target, point, ModifierKeys.None, Environment.TickCount);
        }

        CleanupPointerSession(pointerId);
    }

    private void DispatchTouchSourcePipeline(
        UIElement target,
        Win32PointerData pointerData,
        bool isDown,
        bool isUp,
        int timestamp,
        ref bool sourceHandled,
        ref bool sourceCanceled)
    {
        TouchDevice touchDevice = isDown
            ? Touch.RegisterTouchPoint((int)pointerData.PointerId, pointerData.Position, target)
            : Touch.GetDevice((int)pointerData.PointerId) ?? Touch.RegisterTouchPoint((int)pointerData.PointerId, pointerData.Position, target);

        touchDevice.UpdatePosition(pointerData.Position);
        touchDevice.DirectlyOver = target;

        RoutedEvent previewEvent = isDown ? PreviewTouchDownEvent : (isUp ? PreviewTouchUpEvent : PreviewTouchMoveEvent);
        RoutedEvent bubbleEvent = isDown ? TouchDownEvent : (isUp ? TouchUpEvent : TouchMoveEvent);

        TouchEventArgs previewArgs = new(touchDevice, timestamp) { RoutedEvent = previewEvent };
        target.RaiseEvent(previewArgs);

        sourceHandled |= previewArgs.Handled;
        sourceCanceled |= previewArgs.Cancel;

        if (!previewArgs.Handled)
        {
            TouchEventArgs bubbleArgs = new(touchDevice, timestamp) { RoutedEvent = bubbleEvent };
            target.RaiseEvent(bubbleArgs);
            sourceHandled |= bubbleArgs.Handled;
            sourceCanceled |= bubbleArgs.Cancel;
        }

        if (isUp || sourceCanceled)
        {
            touchDevice.Deactivate();
            Touch.UnregisterTouchPoint((int)pointerData.PointerId);
        }
    }

    private void DispatchStylusSourcePipeline(
        UIElement target,
        Win32PointerData pointerData,
        bool isDown,
        bool isUp,
        int timestamp,
        ref bool sourceHandled,
        ref bool sourceCanceled)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new PointerStylusDevice((int)pointerData.PointerId);
            _activeStylusDevices[pointerData.PointerId] = stylusDevice;
        }

        Tablet.CurrentStylusDevice = stylusDevice;

        var properties = pointerData.Point.Properties;
        stylusDevice.UpdateState(
            pointerData.Position,
            pointerData.StylusPoints,
            inAir: !pointerData.Point.IsInContact,
            inverted: properties.IsInverted,
            inRange: pointerData.IsInRange,
            barrelPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            directlyOver: target);

        StylusInputAction inputAction = ResolveStylusInputAction(isDown, isUp, pointerData.Point.IsInContact);
        RealTimeStylusProcessResult processResult = _realTimeStylus.Process(
            pointerData.PointerId,
            target,
            inputAction,
            stylusDevice.GetStylusPoints(target),
            timestamp,
            inAir: !pointerData.Point.IsInContact,
            inRange: pointerData.IsInRange,
            barrelButtonPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            inverted: properties.IsInverted,
            pointerCanceled: pointerData.IsCanceled);

        stylusDevice.UpdateState(
            pointerData.Position,
            processResult.RawStylusInput.GetStylusPoints(),
            inAir: !pointerData.Point.IsInContact,
            inverted: properties.IsInverted,
            inRange: pointerData.IsInRange,
            barrelPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            directlyOver: target);

        RaiseStylusExtendedEvents(target, stylusDevice, timestamp, inputAction, processResult);

        RoutedEvent previewEvent = isDown ? PreviewStylusDownEvent : (isUp ? PreviewStylusUpEvent : PreviewStylusMoveEvent);
        RoutedEvent bubbleEvent = isDown ? StylusDownEvent : (isUp ? StylusUpEvent : StylusMoveEvent);

        StylusEventArgs previewArgs = CreateStylusEventArgs(stylusDevice, timestamp, previewEvent, isDown);
        target.RaiseEvent(previewArgs);

        sourceHandled |= previewArgs.Handled;
        sourceCanceled |= previewArgs.Cancel || processResult.Canceled;

        if (!previewArgs.Handled && !processResult.Canceled)
        {
            StylusEventArgs bubbleArgs = CreateStylusEventArgs(stylusDevice, timestamp, bubbleEvent, isDown);
            target.RaiseEvent(bubbleArgs);
            sourceHandled |= bubbleArgs.Handled;
            sourceCanceled |= bubbleArgs.Cancel;
        }

        _realTimeStylus.QueueProcessedCallbacks(processResult);

        if (isUp || sourceCanceled || processResult.SessionEnded)
        {
            _activeStylusDevices.Remove(pointerData.PointerId);
            if (ReferenceEquals(Tablet.CurrentStylusDevice, stylusDevice))
            {
                Tablet.CurrentStylusDevice = null;
            }
        }
    }

    private static StylusInputAction ResolveStylusInputAction(bool isDown, bool isUp, bool isInContact)
    {
        if (isDown)
        {
            return StylusInputAction.Down;
        }

        if (isUp)
        {
            return StylusInputAction.Up;
        }

        return isInContact ? StylusInputAction.Move : StylusInputAction.InAirMove;
    }

    private static StylusEventArgs CreateStylusEventArgs(StylusDevice stylusDevice, int timestamp, RoutedEvent routedEvent, bool isDown)
    {
        StylusEventArgs args = isDown
            ? new StylusDownEventArgs(stylusDevice, timestamp)
            : new StylusEventArgs(stylusDevice, timestamp);
        args.RoutedEvent = routedEvent;
        return args;
    }

    private static StylusButton? GetBarrelButton(StylusDevice stylusDevice)
    {
        foreach (var button in stylusDevice.StylusButtons)
        {
            if (button.Name.Equals("Barrel", StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        return stylusDevice.StylusButtons.Count > 0 ? stylusDevice.StylusButtons[0] : null;
    }

    private static void RaiseStylusSimpleEvent(UIElement target, StylusDevice stylusDevice, int timestamp, RoutedEvent routedEvent)
    {
        var args = new StylusEventArgs(stylusDevice, timestamp) { RoutedEvent = routedEvent };
        target.RaiseEvent(args);
    }

    private static void RaiseStylusSystemGestureEvent(UIElement target, StylusDevice stylusDevice, int timestamp, SystemGesture gesture)
    {
        var args = new StylusSystemGestureEventArgs(stylusDevice, timestamp, gesture)
        {
            RoutedEvent = StylusSystemGestureEvent
        };
        target.RaiseEvent(args);
    }

    private static void RaiseStylusButtonEvent(UIElement target, StylusDevice stylusDevice, int timestamp, RoutedEvent routedEvent)
    {
        StylusButton? button = GetBarrelButton(stylusDevice);
        if (button == null)
        {
            return;
        }

        var args = new StylusButtonEventArgs(stylusDevice, timestamp, button)
        {
            RoutedEvent = routedEvent
        };
        target.RaiseEvent(args);
    }

    private static void RaiseStylusExtendedEvents(
        UIElement target,
        StylusDevice stylusDevice,
        int timestamp,
        StylusInputAction inputAction,
        RealTimeStylusProcessResult processResult)
    {
        if (processResult.LeftElement && processResult.PreviousTarget != null)
        {
            RaiseStylusSimpleEvent(processResult.PreviousTarget, stylusDevice, timestamp, StylusLeaveEvent);
        }

        if (processResult.EnteredElement)
        {
            RaiseStylusSimpleEvent(target, stylusDevice, timestamp, StylusEnterEvent);
        }

        if (processResult.EnteredRange)
        {
            RaiseStylusSimpleEvent(target, stylusDevice, timestamp, StylusInRangeEvent);
            RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoverEnter);
        }

        if (processResult.ExitedRange)
        {
            RaiseStylusSimpleEvent(processResult.PreviousTarget ?? target, stylusDevice, timestamp, StylusOutOfRangeEvent);
            RaiseStylusSystemGestureEvent(processResult.PreviousTarget ?? target, stylusDevice, timestamp, SystemGesture.HoverLeave);
        }

        if (processResult.BarrelButtonDown)
        {
            RaiseStylusButtonEvent(target, stylusDevice, timestamp, StylusButtonDownEvent);
        }

        if (processResult.BarrelButtonUp)
        {
            RaiseStylusButtonEvent(target, stylusDevice, timestamp, StylusButtonUpEvent);
        }

        switch (inputAction)
        {
            case StylusInputAction.Down:
                RaiseStylusSystemGestureEvent(
                    target,
                    stylusDevice,
                    timestamp,
                    stylusDevice.StylusButtons.Count > 0 && stylusDevice.StylusButtons[0].StylusButtonState == StylusButtonState.Down
                        ? SystemGesture.RightTap
                        : SystemGesture.Tap);
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoldEnter);
                break;

            case StylusInputAction.Move:
                RaiseStylusSystemGestureEvent(
                    target,
                    stylusDevice,
                    timestamp,
                    stylusDevice.StylusButtons.Count > 0 && stylusDevice.StylusButtons[0].StylusButtonState == StylusButtonState.Down
                        ? SystemGesture.RightDrag
                        : SystemGesture.Drag);
                break;

            case StylusInputAction.InAirMove:
                RaiseStylusSimpleEvent(target, stylusDevice, timestamp, StylusInAirMoveEvent);
                break;

            case StylusInputAction.Up:
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoldLeave);
                break;
        }
    }

    private void DispatchManipulationPipeline(
        UIElement target,
        Win32PointerData pointerData,
        bool isDown,
        bool isUp,
        bool sourceHandled,
        int timestamp)
    {
        PointerManipulationSession? existingSession = null;
        if (!isDown && !_activeManipulationSessions.TryGetValue(pointerData.PointerId, out existingSession))
            return;

        if (isDown)
        {
            if (sourceHandled || !target.IsManipulationEnabled)
                return;

            if (!RaiseManipulationStartingPipeline(target))
                return;

            RaiseManipulationStartedPipeline(target, pointerData.Point.Position, timestamp);
            _activeManipulationSessions[pointerData.PointerId] = new PointerManipulationSession(target, pointerData.Point.Position, timestamp);
            return;
        }

        if (existingSession == null)
            return;

        if (isUp)
        {
            RaiseManipulationInertiaStartingPipeline(existingSession, timestamp);
            RaiseManipulationCompletedPipeline(existingSession, isInertial: false, timestamp);
            _activeManipulationSessions.Remove(pointerData.PointerId);
            return;
        }

        if (sourceHandled)
            return;

        RaiseManipulationDeltaPipeline(existingSession, pointerData.Point.Position, timestamp);
    }

    private bool RaiseManipulationStartingPipeline(UIElement target)
    {
        ManipulationStartingEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationStartingEvent,
            ManipulationContainer = target,
            Mode = ManipulationModes.All,
            Cancel = false
        };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
            return false;

        if (!previewArgs.Handled)
        {
            ManipulationStartingEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationStartingEvent,
                ManipulationContainer = previewArgs.ManipulationContainer ?? target,
                Mode = previewArgs.Mode,
                Pivot = previewArgs.Pivot,
                IsSingleTouchEnabled = previewArgs.IsSingleTouchEnabled,
                Cancel = false
            };
            target.RaiseEvent(bubbleArgs);
            if (bubbleArgs.Cancel)
                return false;
        }

        return true;
    }

    private static void RaiseManipulationStartedPipeline(UIElement target, Point origin, int timestamp)
    {
        ManipulationStartedEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationStartedEvent,
            ManipulationContainer = target,
            ManipulationOrigin = origin
        };
        target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationStartedEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationStartedEvent,
                ManipulationContainer = target,
                ManipulationOrigin = origin
            };
            target.RaiseEvent(bubbleArgs);
        }
    }

    private static void RaiseManipulationDeltaPipeline(PointerManipulationSession session, Point currentPoint, int timestamp)
    {
        Vector deltaTranslation = currentPoint - session.LastPoint;
        int dt = Math.Max(1, timestamp - session.LastTimestamp);
        Vector velocity = new(deltaTranslation.X / dt, deltaTranslation.Y / dt);
        Vector cumulative = session.CumulativeTranslation + deltaTranslation;

        ManipulationDelta delta = CreateManipulationDelta(deltaTranslation);
        ManipulationDelta cumulativeDelta = CreateManipulationDelta(cumulative);
        ManipulationVelocities velocities = new()
        {
            LinearVelocity = velocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationDeltaEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationDeltaEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            DeltaManipulation = delta,
            CumulativeManipulation = cumulativeDelta,
            Velocities = velocities,
            IsInertial = false
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationDeltaEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationDeltaEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                DeltaManipulation = delta,
                CumulativeManipulation = cumulativeDelta,
                Velocities = velocities,
                IsInertial = false
            };
            session.Target.RaiseEvent(bubbleArgs);
        }

        session.LastPoint = currentPoint;
        session.LastTimestamp = timestamp;
        session.CumulativeTranslation = cumulative;
        session.LastVelocity = velocity;
    }

    private static void RaiseManipulationInertiaStartingPipeline(PointerManipulationSession session, int timestamp)
    {
        if (session.LastVelocity.Length <= 0.01)
            return;

        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastVelocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationInertiaStartingEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationInertiaStartingEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            InitialVelocities = velocities
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationInertiaStartingEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationInertiaStartingEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                InitialVelocities = velocities,
                TranslationBehavior = previewArgs.TranslationBehavior,
                RotationBehavior = previewArgs.RotationBehavior,
                ExpansionBehavior = previewArgs.ExpansionBehavior
            };
            session.Target.RaiseEvent(bubbleArgs);
        }
    }

    private static void RaiseManipulationCompletedPipeline(PointerManipulationSession session, bool isInertial, int timestamp)
    {
        ManipulationDelta total = CreateManipulationDelta(session.CumulativeTranslation);
        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastVelocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationCompletedEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationCompletedEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            TotalManipulation = total,
            FinalVelocities = velocities,
            IsInertial = isInertial
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationCompletedEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationCompletedEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                TotalManipulation = total,
                FinalVelocities = velocities,
                IsInertial = isInertial
            };
            session.Target.RaiseEvent(bubbleArgs);
        }
    }

    private void CancelManipulationSession(uint pointerId, int timestamp)
    {
        if (!_activeManipulationSessions.TryGetValue(pointerId, out var session))
            return;

        ManipulationBoundaryFeedbackEventArgs previewBoundary = new()
        {
            RoutedEvent = PreviewManipulationBoundaryFeedbackEvent,
            ManipulationContainer = session.Target,
            BoundaryFeedback = CreateManipulationDelta(Vector.Zero)
        };
        session.Target.RaiseEvent(previewBoundary);

        if (!previewBoundary.Handled)
        {
            ManipulationBoundaryFeedbackEventArgs bubbleBoundary = new()
            {
                RoutedEvent = ManipulationBoundaryFeedbackEvent,
                ManipulationContainer = session.Target,
                BoundaryFeedback = previewBoundary.BoundaryFeedback
            };
            session.Target.RaiseEvent(bubbleBoundary);
        }

        RaiseManipulationCompletedPipeline(session, isInertial: false, timestamp);
        _activeManipulationSessions.Remove(pointerId);
    }

    private static ManipulationDelta CreateManipulationDelta(Vector translation)
    {
        return new ManipulationDelta
        {
            Translation = translation,
            Rotation = 0,
            Scale = new Vector(1, 1),
            Expansion = Vector.Zero
        };
    }

    private void RaisePointerDownPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerDownEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerDownEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerDownEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerDownEvent };
            target.RaiseEvent(bubbleArgs);
            handled = handled || bubbleArgs.Handled || bubbleArgs.Cancel;
            if (bubbleArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
                return;
            }
        }

        if (!handled)
        {
            PointerPressedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerPressedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            }
        }
    }

    private void RaisePointerMovePipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerMoveEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerMoveEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerMoveEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerMoveEvent };
            target.RaiseEvent(bubbleArgs);
            handled = handled || bubbleArgs.Handled || bubbleArgs.Cancel;
            if (bubbleArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
                return;
            }
        }

        if (!handled)
        {
            PointerMovedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerMovedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            }
        }
    }

    private void RaisePointerUpPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerUpEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerUpEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerUpEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerUpEvent };
            target.RaiseEvent(bubbleArgs);
            handled = handled || bubbleArgs.Handled || bubbleArgs.Cancel;
            if (bubbleArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
                return;
            }
        }

        if (!handled)
        {
            PointerReleasedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerReleasedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            }
        }
    }

    private void RaisePointerCancelPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerCancelEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerCancelEvent };
        target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            PointerCancelEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerCancelEvent };
            target.RaiseEvent(bubbleArgs);
        }
    }

    private static void RaisePointerWheelPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerWheelChangedEventArgs args = new(point, modifiers, timestamp) { RoutedEvent = PointerEvents.PointerWheelChangedEvent };
        target.RaiseEvent(args);
    }

    private void CleanupPointerSession(uint pointerId)
    {
        _activePointerTargets.Remove(pointerId);
        _lastPointerPoints.Remove(pointerId);
        if (_activeStylusDevices.TryGetValue(pointerId, out var stylusDevice))
        {
            _activeStylusDevices.Remove(pointerId);
            if (ReferenceEquals(Tablet.CurrentStylusDevice, stylusDevice))
            {
                Tablet.CurrentStylusDevice = null;
            }
        }

        _realTimeStylus.CancelSession(pointerId);
        _activeManipulationSessions.Remove(pointerId);

        TouchDevice? touchDevice = Touch.GetDevice((int)pointerId);
        if (touchDevice != null)
        {
            touchDevice.Deactivate();
            Touch.UnregisterTouchPoint((int)pointerId);
        }
    }

    private static PointerPoint CreateMousePointerPoint(
        Point position,
        MouseButtonState left,
        MouseButtonState middle,
        MouseButtonState right,
        MouseButtonState xButton1,
        MouseButtonState xButton2,
        ModifierKeys modifiers,
        int timestamp,
        PointerUpdateKind updateKind,
        int mouseWheelDelta = 0)
    {
        PointerPointProperties properties = new()
        {
            IsLeftButtonPressed = left == MouseButtonState.Pressed,
            IsMiddleButtonPressed = middle == MouseButtonState.Pressed,
            IsRightButtonPressed = right == MouseButtonState.Pressed,
            IsXButton1Pressed = xButton1 == MouseButtonState.Pressed,
            IsXButton2Pressed = xButton2 == MouseButtonState.Pressed,
            MouseWheelDelta = mouseWheelDelta,
            PointerUpdateKind = updateKind,
            IsPrimary = true
        };

        bool isInContact = properties.IsLeftButtonPressed ||
                           properties.IsMiddleButtonPressed ||
                           properties.IsRightButtonPressed ||
                           properties.IsXButton1Pressed ||
                           properties.IsXButton2Pressed;

        return new PointerPoint(
            MousePointerId,
            position,
            PointerDeviceType.Mouse,
            isInContact,
            properties,
            (ulong)timestamp,
            0);
    }

    private static PointerUpdateKind MapMouseButtonToPointerUpdateKind(MouseButton button, bool isPressed)
    {
        return (button, isPressed) switch
        {
            (MouseButton.Left, true) => PointerUpdateKind.LeftButtonPressed,
            (MouseButton.Left, false) => PointerUpdateKind.LeftButtonReleased,
            (MouseButton.Right, true) => PointerUpdateKind.RightButtonPressed,
            (MouseButton.Right, false) => PointerUpdateKind.RightButtonReleased,
            (MouseButton.Middle, true) => PointerUpdateKind.MiddleButtonPressed,
            (MouseButton.Middle, false) => PointerUpdateKind.MiddleButtonReleased,
            (MouseButton.XButton1, true) => PointerUpdateKind.XButton1Pressed,
            (MouseButton.XButton1, false) => PointerUpdateKind.XButton1Released,
            (MouseButton.XButton2, true) => PointerUpdateKind.XButton2Pressed,
            (MouseButton.XButton2, false) => PointerUpdateKind.XButton2Released,
            _ => PointerUpdateKind.Other
        };
    }

    /// <summary>
    /// Extracts mouse position from lParam and converts from physical pixels to DIPs.
    /// For client-area messages (WM_MOUSEMOVE, WM_LBUTTONDOWN, etc.).
    /// </summary>
    private Point GetMousePosition(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        return new Point(x / _dpiScale, y / _dpiScale);
    }

    private static (MouseButtonState left, MouseButtonState middle, MouseButtonState right, MouseButtonState xButton1, MouseButtonState xButton2) GetMouseButtonStates(nint wParam)
    {
        int flags = (int)wParam.ToInt64();
        return (
            (flags & MK_LBUTTON) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_MBUTTON) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_RBUTTON) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_XBUTTON1) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_XBUTTON2) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released
        );
    }

    private static ModifierKeys GetModifierKeys()
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if ((GetKeyState(VK_SHIFT) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Shift;
        }

        if ((GetKeyState(VK_CONTROL) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Control;
        }

        if ((GetKeyState(VK_MENU) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Alt;
        }

        return modifiers;
    }

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);

    #endregion

    #region Win32 Interop

    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_EX_APPWINDOW = 0x00040000;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = new(-1);
    private static readonly nint HWND_NOTOPMOST = new(-2);
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;
    private const int SW_MINIMIZE = 6;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_MOVE = 0x0003;
    private const uint WM_SIZE = 0x0005;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_PAINT = 0x000F;

    // Hit test results
    private const int HTNOWHERE = 0;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTMINBUTTON = 8;
    private const int HTMAXBUTTON = 9;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int HTCLOSE = 20;

    // DWM window corner preference
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    // DWM system backdrop type (Windows 11 22H2+)
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;
    // DWM_SYSTEMBACKDROP_TYPE values
    private const int DWMSBT_AUTO = 0;           // Auto
    private const int DWMSBT_NONE = 1;           // None
    private const int DWMSBT_MAINWINDOW = 2;     // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4;   // Mica Alt
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // SetWindowCompositionAttribute (undocumented, Win10+ fallback for Acrylic)
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor; // ABGR format
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public nint Data;
        public int DataSize;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    private const uint WM_MOVING = 0x0216;
    private const uint WM_SIZING = 0x0214;
    private const uint WM_DPICHANGED = 0x02E0;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;
    private const uint CS_HREDRAW = 0x0002;
    private const uint CS_VREDRAW = 0x0001;
    private const int COLOR_WINDOW = 5;
    private const nint IDC_ARROW = 32512;
    private const nint IDC_IBEAM = 32513;
    private const nint IDC_WAIT = 32514;
    private const nint IDC_CROSS = 32515;
    private const nint IDC_UPARROW = 32516;
    private const nint IDC_SIZE = 32640;      // Same as IDC_SIZEALL
    private const nint IDC_SIZENWSE = 32642;
    private const nint IDC_SIZENESW = 32643;
    private const nint IDC_SIZEWE = 32644;
    private const nint IDC_SIZENS = 32645;
    private const nint IDC_SIZEALL = 32646;
    private const nint IDC_NO = 32648;
    private const nint IDC_HAND = 32649;
    private const nint IDC_APPSTARTING = 32650;
    private const nint IDC_HELP = 32651;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint WM_USER = 0x0400;
    private const uint WM_APP_REPAINT = WM_USER + 1;
    private const int SIZE_RESTORED = 0;
    private const int SIZE_MINIMIZED = 1;
    private const int SIZE_MAXIMIZED = 2;

    // Input messages
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_RBUTTONDBLCLK = 0x0206;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private const uint WM_MBUTTONDBLCLK = 0x0209;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint WM_XBUTTONDOWN = 0x020B;
    private const uint WM_XBUTTONUP = 0x020C;
    private const uint WM_MOUSELEAVE = 0x02A3;
    private const uint WM_CAPTURECHANGED = 0x0215;
    private const uint WM_ACTIVATE = 0x0006;
    private const uint WM_SETFOCUS = 0x0007;
    private const uint WM_KILLFOCUS = 0x0008;
    private const int WA_INACTIVE = 0;

    // Non-client mouse messages
    private const uint WM_NCMOUSEMOVE = 0x00A0;
    private const uint WM_NCMOUSEHOVER = 0x02A0;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_NCLBUTTONUP = 0x00A2;
    private const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    private const uint WM_NCRBUTTONDOWN = 0x00A4;
    private const uint WM_NCRBUTTONUP = 0x00A5;
    private const uint WM_NCMOUSELEAVE = 0x02A2;

    // TrackMouseEvent flags
    private const uint TME_HOVER = 0x00000001;
    private const uint TME_LEAVE = 0x00000002;
    private const uint TME_NONCLIENT = 0x00000010;
    private const uint HOVER_DEFAULT = 0xFFFFFFFF;

    // Cursor message
    private const uint WM_SETCURSOR = 0x0020;
    private const int HTCLIENT_SETCURSOR = 1;

    // IME messages
    private const uint WM_IME_STARTCOMPOSITION = 0x010D;
    private const uint WM_IME_ENDCOMPOSITION = 0x010E;
    private const uint WM_IME_COMPOSITION = 0x010F;
    private const uint WM_IME_SETCONTEXT = 0x0281;
    private const uint WM_IME_NOTIFY = 0x0282;
    private const uint WM_IME_CHAR = 0x0286;

    // Virtual key codes
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;  // Alt key

    // Mouse button state flags in wParam
    private const int MK_LBUTTON = 0x0001;
    private const int MK_RBUTTON = 0x0002;
    private const int MK_SHIFT = 0x0004;
    private const int MK_CONTROL = 0x0008;
    private const int MK_MBUTTON = 0x0010;
    private const int MK_XBUTTON1 = 0x0020;
    private const int MK_XBUTTON2 = 0x0040;

    // Monitor constants
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public nint hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[]? rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowText(nint hWnd, string lpString);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursor(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint hCursor);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? lpModuleName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RedrawWindow(nint hWnd, nint lprcUpdate, nint hrgnUpdate, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial long GetWindowLong(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial long SetWindowLong(nint hWnd, int nIndex, long dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DwmDefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam, out nint plResult);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoEx(nint hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NCCALCSIZE_PARAMS
    {
        public RECT rgrc0;
        public RECT rgrc1;
        public RECT rgrc2;
        public nint lppos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    #endregion
}

/// <summary>
/// Specifies the state of a window.
/// </summary>
public enum WindowState
{
    Normal,
    Minimized,
    Maximized
}

/// <summary>
/// Specifies the title bar style for a window.
/// </summary>
public enum WindowTitleBarStyle
{
    /// <summary>
    /// Use the native Windows title bar.
    /// </summary>
    Native,

    /// <summary>
    /// Use a custom title bar rendered by the application.
    /// </summary>
    Custom
}

/// <summary>
/// Specifies whether a window sizes itself to fit the size of its content.
/// </summary>
public enum SizeToContent
{
    /// <summary>
    /// The window does not automatically size itself to fit the size of its content.
    /// </summary>
    Manual,

    /// <summary>
    /// The window automatically sizes itself to fit the width of its content, but not the height.
    /// </summary>
    Width,

    /// <summary>
    /// The window automatically sizes itself to fit the height of its content, but not the width.
    /// </summary>
    Height,

    /// <summary>
    /// The window automatically sizes itself to fit both the width and height of its content.
    /// </summary>
    WidthAndHeight
}

/// <summary>
/// Specifies whether a window can be resized and whether it has minimize and maximize buttons.
/// </summary>
public enum ResizeMode
{
    /// <summary>
    /// A window cannot be resized. The Minimize and Maximize buttons are not displayed.
    /// </summary>
    NoResize,

    /// <summary>
    /// A window can only be minimized and restored. The Minimize button is displayed and enabled.
    /// </summary>
    CanMinimize,

    /// <summary>
    /// A window can be resized. The Minimize and Maximize buttons are displayed and enabled.
    /// </summary>
    CanResize,

    /// <summary>
    /// A window can be resized, with a resize grip displayed in the lower-right corner.
    /// </summary>
    CanResizeWithGrip
}

/// <summary>
/// Specifies the type of border that a Window has.
/// </summary>
public enum WindowStyle
{
    /// <summary>
    /// Only the client area is visible — the title bar and border are not shown.
    /// </summary>
    None,

    /// <summary>
    /// A window with a single border. This is the default value.
    /// </summary>
    SingleBorderWindow,

    /// <summary>
    /// A window with a 3-D border.
    /// </summary>
    ThreeDBorderWindow,

    /// <summary>
    /// A fixed tool window.
    /// </summary>
    ToolWindow
}

