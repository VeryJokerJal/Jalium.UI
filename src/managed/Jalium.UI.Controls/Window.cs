using System.Runtime.InteropServices;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
#if DEBUG
using Jalium.UI.Controls.DevTools;
#endif

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a window in the Jalium.UI framework.
/// </summary>
public partial class Window : ContentControl, IWindowHost
{
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
        get => (WindowState)(GetValue(WindowStateProperty) ?? WindowState.Normal);
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

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the window is loaded.
    /// </summary>
    public event EventHandler? Loaded;

    /// <summary>
    /// Occurs when the window is closing.
    /// </summary>
    public event EventHandler? Closing;

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

    public Window()
    {
        Background = new SolidColorBrush(Color.White);
        Width = 800;
        Height = 600;

        // Create custom title bar by default
        CreateTitleBar();
    }

    private void CreateTitleBar()
    {
        if (TitleBarStyle != WindowTitleBarStyle.Custom)
        {
            return;
        }

        TitleBar = new TitleBar
        {
            Title = Title
        };

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
        // Get cursor position in screen coordinates
        int screenX = (short)(lParam.ToInt64() & 0xFFFF);
        int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        // Check if window is maximized
        bool isMaximized = IsZoomed(Handle);

        int x, y, windowWidth, windowHeight;

        if (isMaximized)
        {
            // When maximized, use monitor work area for coordinates
            // because GetWindowRect includes invisible borders but content is rendered at work area
            var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
            MONITORINFO monitorInfo = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                x = screenX - monitorInfo.rcWork.left;
                y = screenY - monitorInfo.rcWork.top;
                windowWidth = monitorInfo.rcWork.right - monitorInfo.rcWork.left;
                windowHeight = monitorInfo.rcWork.bottom - monitorInfo.rcWork.top;
            }
            else
            {
                return HTCLIENT;
            }
        }
        else
        {
            // Normal window: use DwmGetWindowAttribute to get actual visible bounds
            // GetWindowRect includes invisible borders (shadow), but our content is rendered at visible bounds
            if (DwmGetWindowAttribute(Handle, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frameRect, Marshal.SizeOf<RECT>()) == 0)
            {
                x = screenX - frameRect.left;
                y = screenY - frameRect.top;
                windowWidth = frameRect.right - frameRect.left;
                windowHeight = frameRect.bottom - frameRect.top;
            }
            else
            {
                // Fallback to GetWindowRect if DWM call fails
                if (!GetWindowRect(Handle, out RECT windowRect))
                {
                    return HTCLIENT;
                }
                x = screenX - windowRect.left;
                y = screenY - windowRect.top;
                windowWidth = windowRect.right - windowRect.left;
                windowHeight = windowRect.bottom - windowRect.top;
            }
        }

        var titleBarHeight = TitleBar?.DesiredSize.Height ?? 32;

        // Check title bar buttons FIRST (before resize borders)
        // Return the appropriate hit test value for each button type
        if (y < titleBarHeight)
        {
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
        }

        // Skip resize borders when maximized - maximized windows cannot be resized
        if (!isMaximized)
        {
            const int resizeBorder = 6;

            // Check resize borders
            bool isLeft = x < resizeBorder;
            bool isRight = x >= windowWidth - resizeBorder;
            bool isTop = y < resizeBorder;
            bool isBottom = y >= windowHeight - resizeBorder;

            // Corner resize
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

            // Edge resize
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

        // Check if in title bar area (caption, not buttons - buttons already handled above)
        if (y < titleBarHeight)
        {
            return HTCAPTION;
        }

        return HTCLIENT;
    }

    private TitleBarButton? GetTitleBarButtonAtPoint(Point point, double windowWidth = 0)
    {
        if (TitleBar == null)
        {
            return null;
        }

        var titleBarHeight = TitleBar.DesiredSize.Height;
        if (point.Y >= titleBarHeight)
        {
            return null;
        }

        var buttonWidth = 46.0;
        // Use provided window width, or fall back to Width property
        var titleBarWidth = windowWidth > 0 ? windowWidth : Width;
        double buttonX = titleBarWidth;

        // Check close button (rightmost)
        if (TitleBar.ShowCloseButton)
        {
            buttonX -= buttonWidth;
            if (point.X >= buttonX && point.X < buttonX + buttonWidth)
            {
                return GetTitleBarButtonByKind(TitleBarButtonKind.Close);
            }
        }

        // Check maximize button
        if (TitleBar.ShowMaximizeButton)
        {
            buttonX -= buttonWidth;
            if (point.X >= buttonX && point.X < buttonX + buttonWidth)
            {
                return GetTitleBarButtonByKind(TitleBar.IsMaximized ? TitleBarButtonKind.Restore : TitleBarButtonKind.Maximize);
            }
        }

        // Check minimize button
        if (TitleBar.ShowMinimizeButton)
        {
            buttonX -= buttonWidth;
            if (point.X >= buttonX && point.X < buttonX + buttonWidth)
            {
                return GetTitleBarButtonByKind(TitleBarButtonKind.Minimize);
            }
        }

        return null;
    }

    private TitleBarButton? GetTitleBarButtonByKind(TitleBarButtonKind kind)
    {
        if (TitleBar == null)
        {
            return null;
        }

        for (int i = 0; i < TitleBar.VisualChildrenCount; i++)
        {
            if (TitleBar.GetVisualChild(i) is TitleBarButton button)
            {
                if (button.Kind == kind || (kind == TitleBarButtonKind.Restore && button.Kind == TitleBarButtonKind.Maximize))
                {
                    return button;
                }
            }
        }
        return null;
    }

    private TitleBarButton? _hoveredTitleBarButton;
    private TitleBarButton? _pressedTitleBarButton;

    private bool OnNcMouseMove(nint wParam, nint lParam)
    {
        // Get cursor position in screen coordinates
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        // Convert to client coordinates
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        var button = GetTitleBarButtonAtPoint(new Point(pt.X, pt.Y));
        UpdateTitleBarButtonHover(button);

        // Track non-client mouse leave
        TRACKMOUSEEVENT tme = new()
        {
            cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
            dwFlags = TME_LEAVE | TME_NONCLIENT,
            hwndTrack = Handle,
            dwHoverTime = 0
        };
        _ = TrackMouseEvent(ref tme);

        // Always return false to let Windows handle the message
        // This allows Snap Layout flyout on Windows 11 to appear
        // We still update our visual hover state above
        return false;
    }

    private void OnNcMouseLeave()
    {
        UpdateTitleBarButtonHover(null);
        _pressedTitleBarButton = null;
    }

    private bool OnNcLButtonDown(nint wParam, nint lParam)
    {
        _ = (int)wParam.ToInt64();

        // Get cursor position
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        var button = GetTitleBarButtonAtPoint(new Point(pt.X, pt.Y));
        if (button != null)
        {
            _pressedTitleBarButton = button;
            button.SetIsPressed(true);
            InvalidateWindow();
            return true; // Handled
        }

        return false; // Let Windows handle it (for dragging)
    }

    private bool OnNcLButtonUp(nint wParam, nint lParam)
    {
        // Get cursor position
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        var button = GetTitleBarButtonAtPoint(new Point(pt.X, pt.Y));

        if (_pressedTitleBarButton != null)
        {
            _pressedTitleBarButton.SetIsPressed(false);

            // If released on the same button, trigger click
            if (button == _pressedTitleBarButton)
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
            InvalidateWindow();
            return true;
        }

        return false;
    }

    private bool OnNcLButtonDblClk(nint wParam, nint lParam)
    {
        int hitTest = (int)wParam.ToInt64();

        // Get cursor position
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        // If over a button, don't handle double-click (let the button clicks work)
        var button = GetTitleBarButtonAtPoint(new Point(pt.X, pt.Y));
        if (button != null)
        {
            return true; // Prevent default handling
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

        _hoveredTitleBarButton?.SetIsMouseOver(false);

        _hoveredTitleBarButton = newHoveredButton;

        _hoveredTitleBarButton?.SetIsMouseOver(true);

        InvalidateWindow();
    }

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            int count = base.VisualChildrenCount;
            if (TitleBar != null)
            {
                count++;
            }

            return count;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        // Title bar is rendered LAST (on top) for backdrop blur to work correctly
        int baseCount = base.VisualChildrenCount;

        if (index < baseCount)
        {
            return base.GetVisualChild(index);
        }

        // TitleBar is the last child (rendered on top)
        if (TitleBar != null && index == baseCount)
        {
            return TitleBar;
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
        if (TitleBar != null)
        {
            TitleBar.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            titleBarHeight = TitleBar.DesiredSize.Height;
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

        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double titleBarHeight = 0;

        // Arrange title bar at top
        if (TitleBar != null)
        {
            titleBarHeight = TitleBar.DesiredSize.Height;
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

        return finalSize;
    }

    #endregion

    /// <summary>
    /// Shows the window.
    /// </summary>
    public void Show()
    {
        EnsureHandle();
        _ = ShowWindow(Handle, SW_SHOW);

        // For custom title bar, trigger frame change after show to remove native title bar
        // This preserves animations while hiding the native caption
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER);
        }

        _ = UpdateWindow(Handle);
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

    /// <summary>
    /// Closes the window.
    /// </summary>
    private bool _isClosing;

    public void Close()
    {
        if (_isClosing) return;
        _isClosing = true;

        Closing?.Invoke(this, EventArgs.Empty);

        // Clear drawing context first (it holds reference to RenderTarget)
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

            // If no more windows, quit the application
            if (_windows.Count == 0)
            {
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

        // Determine window style based on TitleBarStyle
        // Use WS_OVERLAPPEDWINDOW for both - we handle the non-client area in WM_NCCALCSIZE
        uint dwStyle = WS_OVERLAPPEDWINDOW;

        uint dwExStyle = TitleBarStyle == WindowTitleBarStyle.Custom
            ? WS_EX_APPWINDOW
            : 0;

        // Create the window
        Handle = CreateWindowEx(
            dwExStyle,
            WindowClassName,
            Title,
            dwStyle,
            CW_USEDEFAULT, CW_USEDEFAULT,
            (int)Width, (int)Height,
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

        // For custom title bar, enable DWM effects for rounded corners and animations
        // Keep WS_OVERLAPPEDWINDOW (including WS_CAPTION) - the native title bar will
        // disappear after the first resize/maximize due to WM_NCCALCSIZE handling
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            EnableRoundedCorners();
        }

        // Apply system backdrop if set
        if (SystemBackdrop != WindowBackdropType.None)
        {
            ApplySystemBackdrop(SystemBackdrop);
        }

        // Create render target for this window
        EnsureRenderTarget();
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

        // Extend frame into client area to preserve native animations
        // Using a small margin (1 pixel) enables DWM effects without filling the screen
        MARGINS margins = new() { Left = 0, Right = 0, Top = 1, Bottom = 0 };
        _ = DwmExtendFrameIntoClientArea(Handle, ref margins);
    }

    private void ApplySystemBackdrop(WindowBackdropType backdropType)
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        // Convert WindowBackdropType to DWM_SYSTEMBACKDROP_TYPE value
        int dwmBackdropType = backdropType switch
        {
            WindowBackdropType.None => DWMSBT_NONE,
            WindowBackdropType.Auto => DWMSBT_AUTO,
            WindowBackdropType.Mica => DWMSBT_MAINWINDOW,
            WindowBackdropType.Acrylic => DWMSBT_TRANSIENTWINDOW,
            WindowBackdropType.MicaAlt => DWMSBT_TABBEDWINDOW,
            _ => DWMSBT_NONE
        };

        // For Mica and Acrylic effects to work, we need to extend the frame into client area
        // This makes the client area transparent so the system backdrop shows through
        if (backdropType != WindowBackdropType.None)
        {
            // Extend frame into entire client area for full backdrop effect
            MARGINS margins = new() { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            _ = DwmExtendFrameIntoClientArea(Handle, ref margins);
        }
        else
        {
            // Reset to minimal frame extension when backdrop is disabled
            MARGINS margins = new() { Left = 0, Right = 0, Top = 1, Bottom = 0 };
            _ = DwmExtendFrameIntoClientArea(Handle, ref margins);
        }

        // Apply the system backdrop type
        int result = DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref dwmBackdropType, sizeof(int));

        if (result != 0)
        {
            System.Diagnostics.Debug.WriteLine($"DwmSetWindowAttribute failed with HRESULT: 0x{result:X8}. SystemBackdrop requires Windows 11 22H2+.");
        }

        // Force window to redraw
        InvalidateWindow();
    }

    private void UpdateWindowStyle()
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        long exStyle = GetWindowLong(Handle, GWL_EXSTYLE);

        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            // Keep WS_OVERLAPPEDWINDOW but add WS_EX_APPWINDOW
            exStyle |= WS_EX_APPWINDOW;
            EnableRoundedCorners();
        }

        _ = SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

        // Force window to redraw with new style
        _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER);
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
                // Try to create a default render context
                System.Diagnostics.Debug.WriteLine("Creating new RenderContext...");
                context = new RenderContext(RenderBackend.D3D12);
            }

            System.Diagnostics.Debug.WriteLine($"Creating RenderTarget for HWND: {Handle:X}, Size: {(int)Width}x{(int)Height}");
            RenderTarget = context.CreateRenderTarget(Handle, (int)Width, (int)Height);
            System.Diagnostics.Debug.WriteLine($"RenderTarget created successfully. IsValid: {RenderTarget?.IsValid}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create render target: {ex}");
            throw;
        }
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

            // Update title bar if using custom style
            _ = window.TitleBar?.Title = (string?)e.NewValue ?? "";
        }
    }

    private static void OnWindowStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && window.TitleBar != null&&e.NewValue is WindowState newState)
        {
            window.TitleBar.IsMaximized = newState == WindowState.Maximized;
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

            window.InvalidateMeasure();
        }
    }

    private static void OnSystemBackdropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && e.NewValue is WindowBackdropType backdropType)
        {
            window.ApplySystemBackdrop(backdropType);
        }
    }

    #endregion

    #region Native Window Management

    private const string WindowClassName = "JaliumUIWindow";
    private static bool _classRegistered;
    private static readonly Dictionary<nint, Window> _windows = [];
    private static WndProcDelegate? _wndProcDelegate;
    private bool _isSizing; // True during drag resize

#if DEBUG
    private DevToolsWindow? _devToolsWindow;
    internal DevToolsOverlay? DevToolsOverlay { get; set; }

    /// <summary>
    /// Gets whether this window can open DevTools.
    /// Override to return false in windows that should not open DevTools (e.g., DevToolsWindow).
    /// </summary>
    protected virtual bool CanOpenDevTools => true;
#endif

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
            style = 0, // No CS_HREDRAW | CS_VREDRAW to avoid full repaint on resize
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
        if (_windows.TryGetValue(hWnd, out var window))
        {
            switch (msg)
            {
                case WM_DESTROY:
                    _ = _windows.Remove(hWnd);
                    PostQuitMessage(0);
                    return nint.Zero;

                case WM_NCCALCSIZE:
                    // For custom title bar, remove the non-client area (the white border)
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom && wParam != nint.Zero)
                    {
                        // Read the proposed rect from lParam
                        var ncParams = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(lParam);

                        if (IsZoomed(hWnd))
                        {
                            // When maximized, adjust to work area to respect the taskbar
                            var monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                            MONITORINFO monitorInfo = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                            if (GetMonitorInfo(monitor, ref monitorInfo))
                            {
                                ncParams.rgrc0.left = monitorInfo.rcWork.left;
                                ncParams.rgrc0.top = monitorInfo.rcWork.top;
                                ncParams.rgrc0.right = monitorInfo.rcWork.right;
                                ncParams.rgrc0.bottom = monitorInfo.rcWork.bottom;
                            }
                        }
                        // For normal windows, rgrc0 already contains the window rect
                        // We don't modify it, so the entire window becomes the client area

                        Marshal.StructureToPtr(ncParams, lParam, false);

                        // Return 0 to use the entire window as client area
                        return nint.Zero;
                    }
                    break;

                case WM_NCHITTEST:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        var hitResult = window.HandleNcHitTest(lParam);
                        if (hitResult != HTNOWHERE)
                        {
                            return hitResult;
                        }
                    }
                    break;

                case WM_NCMOUSEMOVE:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        // Return 0 to prevent Windows from showing native behaviors (tooltips, snap layouts)
                        // when mouse is over title bar button areas
                        if (window.OnNcMouseMove(wParam, lParam))
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCMOUSELEAVE:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        window.OnNcMouseLeave();
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
                        // Handle double-click on title bar for maximize/restore
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
                        // Only suppress right-click when over a TitleBarButton
                        // Allow system menu on caption area (normal Windows behavior)
                        int x = (short)(lParam.ToInt64() & 0xFFFF);
                        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        POINT pt = new() { X = x, Y = y };
                        _ = ScreenToClient(hWnd, ref pt);
                        if (window.GetTitleBarButtonAtPoint(new Point(pt.X, pt.Y)) != null)
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
                            return nint.Zero;
                    }

                    window.OnSizeChanged(width, height);

                    // For maximize/restore, post a deferred repaint message
                    if (sizeType is SIZE_MAXIMIZED or SIZE_RESTORED)
                    {
                        _ = PostMessage(hWnd, WM_APP_REPAINT, nint.Zero, nint.Zero);
                    }
                    return nint.Zero;

                case WM_MOVE:
                    window.LocationChanged?.Invoke(window, EventArgs.Empty);
                    return nint.Zero;

                case WM_APP_REPAINT:
                    // Deferred repaint after size change
                    _ = RedrawWindow(hWnd, nint.Zero, nint.Zero, RDW_INVALIDATE | RDW_UPDATENOW);
                    return nint.Zero;

                case WM_ENTERSIZEMOVE:
                    window._isSizing = true;
                    // Disable VSync during resize for faster frame updates
                    window.RenderTarget?.SetVSyncEnabled(false);
                    return nint.Zero;

                case WM_EXITSIZEMOVE:
                    window._isSizing = false;
                    // Re-enable VSync after resize
                    window.RenderTarget?.SetVSyncEnabled(true);
                    // Do final resize to ensure correct buffer size
                    window.RenderTarget?.Resize((int)window.Width, (int)window.Height);
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

                // Window focus
                case WM_SETFOCUS:
                    window.OnWindowGotFocus();
                    return nint.Zero;

                case WM_KILLFOCUS:
                    window.OnWindowLostFocus();
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

                // Cursor handling
                case WM_SETCURSOR:
                    if (window.HandleSetCursor(lParam))
                    {
                        return 1; // Return TRUE to indicate we handled it
                    }
                    break;

                // Mouse input
                case WM_MOUSEMOVE:
                    window.OnMouseMove(wParam, lParam);
                    return nint.Zero;

                case WM_LBUTTONDOWN:
                    window.OnMouseButtonDown(MouseButton.Left, wParam, lParam);
                    return nint.Zero;

                case WM_LBUTTONUP:
                    window.OnMouseButtonUp(MouseButton.Left, wParam, lParam);
                    return nint.Zero;

                case WM_RBUTTONDOWN:
                    window.OnMouseButtonDown(MouseButton.Right, wParam, lParam);
                    return nint.Zero;

                case WM_RBUTTONUP:
                    window.OnMouseButtonUp(MouseButton.Right, wParam, lParam);
                    return nint.Zero;

                case WM_MBUTTONDOWN:
                    window.OnMouseButtonDown(MouseButton.Middle, wParam, lParam);
                    return nint.Zero;

                case WM_MBUTTONUP:
                    window.OnMouseButtonUp(MouseButton.Middle, wParam, lParam);
                    return nint.Zero;

                case WM_MOUSEWHEEL:
                    window.OnMouseWheel(wParam, lParam);
                    return nint.Zero;

                case WM_MOUSELEAVE:
                    window.OnMouseLeave();
                    return nint.Zero;
            }
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private bool HandleSetCursor(nint lParam)
    {
        // Check if cursor is in client area (LOWORD of lParam is hit test result)
        int hitTest = (short)(lParam.ToInt64() & 0xFFFF);
        const int HTCLIENT = 1;

        if (hitTest != HTCLIENT)
        {
            return false; // Let Windows handle non-client area
        }

        // Get mouse position in client coordinates
        POINT pt;
        GetCursorPos(out pt);
        ScreenToClient(Handle, ref pt);
        var position = new Point(pt.X, pt.Y);

        // Find the element under the cursor
        var hitResult = HitTest(position);
        var element = hitResult?.VisualHit as FrameworkElement;

        // Walk up the visual tree to find an element with a cursor set
        Cursor? cursor = null;
        while (element != null)
        {
            cursor = element.Cursor;
            if (cursor != null)
            {
                break;
            }
            element = element.VisualParent as FrameworkElement;
        }

        // Get the appropriate Windows cursor handle
        nint cursorHandle = GetCursorHandle(cursor);
        SetCursor(cursorHandle);

        return true;
    }

    private nint GetCursorHandle(Cursor? cursor)
    {
        if (cursor == null)
        {
            return LoadCursor(nint.Zero, IDC_ARROW);
        }

        nint cursorId = cursor.CursorType switch
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
            CursorType.None => nint.Zero, // Hidden cursor
            _ => IDC_ARROW
        };

        if (cursorId == nint.Zero)
        {
            return nint.Zero; // Will hide the cursor
        }

        return LoadCursor(nint.Zero, cursorId);
    }

    private void OnMouseLeave()
    {
        // Clear title bar button hover state when mouse leaves the window
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            UpdateTitleBarButtonHover(null);
            if (_pressedTitleBarButton != null)
            {
                _pressedTitleBarButton.SetIsPressed(false);
                _pressedTitleBarButton = null;
            }
        }

        // Clear general mouse over state
        if (_lastMouseOverElement != null)
        {
            RaiseMouseLeaveChain(_lastMouseOverElement, null, Environment.TickCount);
            _lastMouseOverElement = null;
        }
    }

    private void OnSizeChanged(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Width = width;
        Height = height;

        // Always resize immediately on size change
        RenderTarget?.Resize(width, height);

        if (!_isSizing)
        {
            // Not dragging (maximize/restore) - also invalidate measure for layout
            InvalidateMeasure();
        }
        else
        {
            // During drag resize: repaint immediately
            _ = RedrawWindow(Handle, nint.Zero, nint.Zero, RDW_INVALIDATE | RDW_UPDATENOW);
        }
    }

    private RenderTargetDrawingContext? _drawingContext;
    private UIElement? _lastMouseOverElement;
    private bool _isWindowFocused;
    private IInputElement? _lastFocusedElement;

    /// <summary>
    /// Called when the window receives Windows focus (WM_SETFOCUS).
    /// </summary>
    private void OnWindowGotFocus()
    {
        _isWindowFocused = true;

        // Restore focus to the previously focused element if any
        if (_lastFocusedElement != null && _lastFocusedElement is UIElement element)
        {
            if (element.Focusable && element.IsEnabled && element.Visibility == Visibility.Visible)
            {
                element.Focus();
            }
        }
    }

    /// <summary>
    /// Called when the window loses Windows focus (WM_KILLFOCUS).
    /// </summary>
    private void OnWindowLostFocus()
    {
        _isWindowFocused = false;

        // Remember the currently focused element for restoration later
        _lastFocusedElement = Keyboard.FocusedElement;

        // Note: We intentionally don't clear the internal focus here.
        // The element retains its IsKeyboardFocused state, but won't receive
        // keyboard input until the window regains focus.
    }

    private void OnPaint()
    {
        PAINTSTRUCT ps = new();
        var hdc = BeginPaint(Handle, out ps);

        try
        {
            if (RenderTarget != null && RenderTarget.IsValid)
            {
                // Perform layout before rendering
                UpdateLayout();

                RenderTarget.BeginDraw();

                // Clear with background color
                // When using system backdrop (Mica/Acrylic), DWM treats black (0,0,0) as transparent
                // in the extended frame area. Clear with transparent first, then draw semi-transparent overlay.
                if (SystemBackdrop != WindowBackdropType.None)
                {
                    // Clear with black/transparent to let DWM backdrop show through
                    RenderTarget.Clear(0.0f, 0.0f, 0.0f, 0.0f);

                    // If Background is set, draw a semi-transparent overlay
                    if (Background is SolidColorBrush bgBrush && bgBrush.Color.A > 0)
                    {
                        var bgColor = bgBrush.Color;
                        var renderContext = RenderContext.Current;
                        if (renderContext != null)
                        {
                            using var nativeBrush = renderContext.CreateSolidBrush(
                                bgColor.ScR, bgColor.ScG, bgColor.ScB, bgColor.ScA);
                            RenderTarget.FillRectangle(0, 0, (float)Width, (float)Height, nativeBrush);
                        }
                    }
                }
                else if (Background is SolidColorBrush solidBrush)
                {
                    var color = solidBrush.Color;
                    RenderTarget.Clear(color.ScR, color.ScG, color.ScB, color.ScA);
                }
                else
                {
                    RenderTarget.Clear(1.0f, 1.0f, 1.0f, 1.0f); // White default
                }

                // Get or create drawing context
                var context = RenderContext.Current;
                if (context != null)
                {
                    _drawingContext ??= new RenderTargetDrawingContext(RenderTarget, context);
                    _drawingContext.Offset = Point.Zero;

                    // Render the visual tree (this calls OnRender on all children)
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
            // In Debug mode, show what's happening
            System.Diagnostics.Debug.WriteLine($"Render error: {ex}");
#if DEBUG
            // Don't rethrow if window is closing (RenderTarget is null)
            if (RenderTarget != null)
            {
                throw;
            }
#endif
        }

        EndPaint(Handle, ref ps);
    }

    /// <summary>
    /// Updates the layout of all elements in this window.
    /// </summary>
    private void UpdateLayout()
    {
        // Measure pass - determine desired sizes
        Size availableSize = new(Width, Height);
        Measure(availableSize);

        // Arrange pass - position elements
        Rect finalRect = new(0, 0, Width, Height);
        Arrange(finalRect);
    }

    /// <summary>
    /// Invalidates the window, causing it to repaint.
    /// Implements IWindowHost.InvalidateWindow.
    /// </summary>
    public void InvalidateWindow()
    {
        if (Handle != nint.Zero)
        {
            _ = InvalidateRect(Handle, nint.Zero, false);
        }
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
#endif

        // Route keyboard events to the focused element, or the window if no element has focus
        var target = Keyboard.FocusedElement as UIElement ?? this;

        // Raise tunnel event (PreviewKeyDown)
        KeyEventArgs tunnelArgs = new(PreviewKeyDownEvent, key, modifiers, isDown: true, isRepeat, timestamp);
        target.RaiseEvent(tunnelArgs);

        // Raise bubble event (KeyDown) if not handled
        if (!tunnelArgs.Handled)
        {
            KeyEventArgs bubbleArgs = new(KeyDownEvent, key, modifiers, isDown: true, isRepeat, timestamp);
            target.RaiseEvent(bubbleArgs);
        }
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
        var target = Keyboard.FocusedElement;
        if (target == null)
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
            // Get caret position from the focused element
            Point caretPos = Point.Zero;
            if (target is IImeSupport imeSupport)
            {
                caretPos = imeSupport.GetImeCaretPosition();
            }

            // Convert to screen coordinates
            ImmNativeMethods.COMPOSITIONFORM form = new()
            {
                dwStyle = ImmNativeMethods.CFS_POINT,
                ptCurrentPos = new ImmNativeMethods.POINT { x = (int)caretPos.X, y = (int)caretPos.Y }
            };

            _ = ImmNativeMethods.ImmSetCompositionWindow(hImc, ref form);

            // Also set candidate window position
            ImmNativeMethods.CANDIDATEFORM candidate = new()
            {
                dwIndex = 0,
                dwStyle = ImmNativeMethods.CFS_CANDIDATEPOS,
                ptCurrentPos = new ImmNativeMethods.POINT { x = (int)caretPos.X, y = (int)caretPos.Y + 20 }
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
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
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

        // Raise bubble event (MouseMove) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseEventArgs bubbleArgs = new(
                MouseMoveEvent, position,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
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

    private void OnMouseButtonDown(MouseButton button, nint wParam, nint lParam)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int timestamp = Environment.TickCount;

        // Handle title bar button press (for custom title bar)
        if (TitleBarStyle == WindowTitleBarStyle.Custom && button == MouseButton.Left)
        {
            var titleBarButton = GetTitleBarButtonAtPoint(position);
            if (titleBarButton != null)
            {
                _pressedTitleBarButton = titleBarButton;
                titleBarButton.SetIsPressed(true);
                InvalidateWindow();
                return; // Handled
            }
        }

        // If an element has captured the mouse, it receives all mouse events
        // Otherwise, find the target element via hit testing
        var captured = UIElement.MouseCapturedElement;
        var target = captured ?? HitTest(position)?.VisualHit as UIElement ?? this;

        // Update button state for the pressed button
        var currentState = MouseButtonState.Pressed;

        // Raise tunnel event (PreviewMouseDown)
        MouseButtonEventArgs tunnelArgs = new(
            PreviewMouseDownEvent, position, button, currentState, clickCount: 1,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        // Raise bubble event (MouseDown) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                MouseDownEvent, position, button, currentState, clickCount: 1,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
        }
    }

    private void OnMouseButtonUp(MouseButton button, nint wParam, nint lParam)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int timestamp = Environment.TickCount;

        // Handle title bar button release (for custom title bar)
        if (TitleBarStyle == WindowTitleBarStyle.Custom && button == MouseButton.Left && _pressedTitleBarButton != null)
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
            InvalidateWindow();
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

        // Raise bubble event (MouseUp) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                MouseUpEvent, position, button, currentState, clickCount: 1,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
        }
    }

    private void OnMouseWheel(nint wParam, nint lParam)
    {
        // WM_MOUSEWHEEL lParam contains SCREEN coordinates, need to convert to client coordinates
        var screenPos = GetMousePosition(lParam);
        POINT pt = new() { X = (int)screenPos.X, Y = (int)screenPos.Y };
        _ = ScreenToClient(Handle, ref pt);
        Point position = new(pt.X, pt.Y);

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

        // Raise bubble event (MouseWheel) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseWheelEventArgs bubbleArgs = new(
                MouseWheelEvent, position, delta,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
        }
    }

    private static Point GetMousePosition(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        return new Point(x, y);
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
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;
    private const int SW_MINIMIZE = 6;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
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
    // DWM_SYSTEMBACKDROP_TYPE values
    private const int DWMSBT_AUTO = 0;           // Auto
    private const int DWMSBT_NONE = 1;           // None
    private const int DWMSBT_MAINWINDOW = 2;     // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4;   // Mica Alt
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
    private const nint IDC_SIZE = 32640;
    private const nint IDC_SIZENWSE = 32642;
    private const nint IDC_SIZENESW = 32643;
    private const nint IDC_SIZEWE = 32644;
    private const nint IDC_SIZENS = 32645;
    private const nint IDC_SIZEALL = 32646;
    private const nint IDC_NO = 32648;
    private const nint IDC_HAND = 32649;
    private const nint IDC_APPSTARTING = 32650;
    private const nint IDC_HELP = 32651;
    private const uint WM_SETCURSOR = 0x0020;
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
    private const uint WM_SETFOCUS = 0x0007;
    private const uint WM_KILLFOCUS = 0x0008;

    // Non-client mouse messages
    private const uint WM_NCMOUSEMOVE = 0x00A0;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_NCLBUTTONUP = 0x00A2;
    private const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    private const uint WM_NCRBUTTONDOWN = 0x00A4;
    private const uint WM_NCRBUTTONUP = 0x00A5;
    private const uint WM_NCMOUSELEAVE = 0x02A2;

    // TrackMouseEvent flags
    private const uint TME_LEAVE = 0x00000002;
    private const uint TME_NONCLIENT = 0x00000010;

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

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NCCALCSIZE_PARAMS
    {
        public RECT rgrc0;
        public RECT rgrc1;
        public RECT rgrc2;
        public nint lppos;
    }

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
