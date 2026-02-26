using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Jalium.UI.Input;
using Jalium.UI.Threading;
using Microsoft.Web.WebView2.Core;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that hosts web content using WebView2.
/// Defaults to a windowless composition host to avoid child HWND airspace issues.
/// </summary>
public sealed partial class WebView : FrameworkElement, IDisposable
{
    private bool _isInitialized;
    private bool _isInitializing;
    private bool _isNavigating;
    private string _documentTitle = string.Empty;
    private bool _disposed;
    private string? _initError;

    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2CompositionController? _compositionController;
    private CoreWebView2? _coreWebView2;
    private readonly bool _isWindowlessComposition = true;

    private IDCompositionDevice? _dcompDevice;
    private IDCompositionTarget? _dcompTarget;
    private IDCompositionVisual? _dcompVisual;

    // Embedded host window for WebView2 (child HWND)
    private nint _hostHwnd;
    private Window? _parentWindow;
    private readonly Dispatcher _dispatcher;
    private readonly ScrollChangedEventHandler _scrollChangedHandler;
    private DispatcherTimer? _positionSyncTimer;
    private Rectangle _lastHostRect = Rectangle.Empty;
    private Rectangle _lastControllerBounds = Rectangle.Empty;
    private bool _isHostVisible;
    private int _visualAttachmentVersion;

    public WebView()
    {
        _dispatcher = Dispatcher.CurrentDispatcher ?? Dispatcher.GetForCurrentThread();
        _scrollChangedHandler = OnAncestorScrollChanged;
        SizeChanged += OnSelfSizeChanged;
        AddHandler(ScrollViewer.ScrollChangedEvent, _scrollChangedHandler, true);

        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler), true);
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler), true);
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler), true);
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler), true);
    }

    #region Dependency Properties

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(WebView),
            new PropertyMetadata(null, OnSourceChanged));

    private static readonly DependencyPropertyKey CanGoBackPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanGoBack), typeof(bool), typeof(WebView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CanGoBackProperty = CanGoBackPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanGoForwardPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanGoForward), typeof(bool), typeof(WebView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CanGoForwardProperty = CanGoForwardPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ZoomFactorProperty =
        DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(WebView),
            new PropertyMetadata(1.0, OnZoomFactorChanged, CoerceZoomFactor));

    public static readonly DependencyProperty DefaultBackgroundColorProperty =
        DependencyProperty.Register(nameof(DefaultBackgroundColor), typeof(Media.Color), typeof(WebView),
            new PropertyMetadata(Media.Color.White, OnDefaultBackgroundColorChanged));

    #endregion

    #region Events

    public event EventHandler<WebViewNavigationStartingEventArgs>? NavigationStarting;
    public event EventHandler<WebViewNavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<WebViewNewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler<WebViewDocumentTitleChangedEventArgs>? DocumentTitleChanged;
    public event EventHandler<WebViewWebMessageReceivedEventArgs>? WebMessageReceived;
    public event EventHandler<WebViewContentLoadingEventArgs>? ContentLoading;
    public event EventHandler<WebViewSourceChangedEventArgs>? SourceChanged;
    public event EventHandler? CoreWebView2InitializationCompleted;

    #endregion

    #region CLR Properties

    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool CanGoBack => (bool)GetValue(CanGoBackProperty)!;
    public bool CanGoForward => (bool)GetValue(CanGoForwardProperty)!;

    public double ZoomFactor
    {
        get => (double)GetValue(ZoomFactorProperty)!;
        set => SetValue(ZoomFactorProperty, value);
    }

    public Media.Color DefaultBackgroundColor
    {
        get => (Media.Color)(GetValue(DefaultBackgroundColorProperty) ?? Media.Color.White);
        set => SetValue(DefaultBackgroundColorProperty, value);
    }

    public string DocumentTitle => _documentTitle;
    public bool IsWebViewInitialized => _isInitialized;
    public bool IsNavigating => _isNavigating;
    public CoreWebView2? CoreWebView2 => _coreWebView2;
    public string? InitializationError => _initError;

    #endregion

    #region Public Methods

    public void Navigate(Uri source) => Source = source;

    public void Navigate(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            Navigate(uri);
    }

    public void NavigateToString(string htmlContent)
    {
        if (_coreWebView2 != null)
        {
            _isNavigating = true;
            _coreWebView2.NavigateToString(htmlContent);
        }
    }

    public void GoBack()
    {
        if (CanGoBack) _coreWebView2?.GoBack();
    }

    public void GoForward()
    {
        if (CanGoForward) _coreWebView2?.GoForward();
    }

    public void Refresh() => _coreWebView2?.Reload();

    public void Stop()
    {
        _isNavigating = false;
        _coreWebView2?.Stop();
    }

    public Task<string> ExecuteScriptAsync(string script)
    {
        if (_coreWebView2 != null)
            return _coreWebView2.ExecuteScriptAsync(script);
        return Task.FromResult(string.Empty);
    }

    public void PostWebMessageAsString(string message) => _coreWebView2?.PostWebMessageAsString(message);
    public void PostWebMessageAsJson(string json) => _coreWebView2?.PostWebMessageAsJson(json);

    public async Task EnsureCoreWebView2Async()
    {
        if (_isInitialized || _isInitializing) return;
        _isInitializing = true;

        try
        {
            await InitializeWebView2Async();
            _isInitialized = _controller != null;
        }
        catch (Exception ex)
        {
            SetInitializationErrorFromException(ex);
        }
        finally
        {
            _isInitializing = false;
            CoreWebView2InitializationCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion

    #region Layout

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Auto-initialize when first laid out in the visual tree
        if (!_isInitialized && !_isInitializing && !_disposed && FindParentWindow() != null)
        {
            // Use Dispatcher to ensure WebView2 COM calls run on the UI thread
            _isInitializing = true;
            _dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await InitializeWebView2Async();
                    _isInitialized = _controller != null;
                    if (_isInitialized)
                        InvalidateArrange();
                }
                catch (Exception ex)
                {
                    SetInitializationErrorFromException(ex);
                }
                finally
                {
                    _isInitializing = false;
                    CoreWebView2InitializationCompleted?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        UpdateHostWindowPosition();
        return finalSize;
    }

    #endregion

    #region Host Window & WebView2 Initialization

    private async Task InitializeWebView2Async()
    {
        _initError = null;
        if (_controller != null && _coreWebView2 != null)
            return;

        var attachmentVersion = _visualAttachmentVersion;

        ReleaseControllerResources(clearEnvironment: false, invalidateInitialization: false);

        _parentWindow = FindParentWindow();
        if (_parentWindow == null || _parentWindow.Handle == nint.Zero)
        {
            SetInitializationError("Parent window not found.");
            return;
        }

        try
        {
            var browserVersion = CoreWebView2Environment.GetAvailableBrowserVersionString(null);
            if (string.IsNullOrWhiteSpace(browserVersion))
            {
                SetInitializationError("Microsoft Edge WebView2 Runtime is not available.");
                return;
            }
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException)
        {
            SetInitializationErrorFromException(ex);
            return;
        }

        var userDataFolder = GetWebViewUserDataFolder();
        Directory.CreateDirectory(userDataFolder);
        _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        if (IsInitializationStale(attachmentVersion))
        {
            ReleaseControllerResources(clearEnvironment: false, invalidateInitialization: false);
            return;
        }

        Rectangle hostRect;
        Rectangle controllerBounds;
        if (!TryGetHostPlacement(out hostRect, out controllerBounds))
        {
            hostRect = new Rectangle(0, 0, 1, 1);
            controllerBounds = new Rectangle(0, 0, 1, 1);
        }

        if (_isWindowlessComposition)
        {
            if (!TryCreateDirectCompositionTarget(_parentWindow.Handle))
                return;

            try
            {
                var compositionController = await _environment.CreateCoreWebView2CompositionControllerAsync(_parentWindow.Handle);
                if (IsInitializationStale(attachmentVersion))
                {
                    compositionController.Close();
                    ReleaseDirectCompositionResources();
                    return;
                }

                _compositionController = compositionController;
                _compositionController.RootVisualTarget = _dcompVisual!;
                _controller = _compositionController;
            }
            catch
            {
                ReleaseDirectCompositionResources();
                throw;
            }
        }
        else
        {
            if (UsesNoRedirectionBitmap(_parentWindow.Handle))
            {
                SetInitializationError(
                    "Embedded WebView requires a redirected parent HWND. Set Window.SystemBackdrop = WindowBackdropType.None before showing the window.");
                return;
            }

            RegisterHostWindowClass();
            _hostHwnd = CreateWindowEx(
                0,
                HostWindowClassName,
                "",
                WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                hostRect.X, hostRect.Y, hostRect.Width, hostRect.Height,
                _parentWindow.Handle,
                nint.Zero,
                GetModuleHandle(null),
                nint.Zero);

            if (_hostHwnd == nint.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                SetInitializationError($"Failed to create host window (Win32 error {error}).");
                return;
            }

            if (GetParent(_hostHwnd) != _parentWindow.Handle)
            {
                SetInitializationError("WebView host HWND is not attached to the expected parent window.");
                _ = DestroyWindow(_hostHwnd);
                _hostHwnd = nint.Zero;
                return;
            }

            _ = ShowWindow(_hostHwnd, SW_SHOW);
            _ = SetWindowPos(_hostHwnd, HWND_BOTTOM,
                hostRect.X, hostRect.Y, hostRect.Width, hostRect.Height,
                SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            var controller = await _environment.CreateCoreWebView2ControllerAsync(_hostHwnd);
            if (IsInitializationStale(attachmentVersion))
            {
                controller.Close();
                if (_hostHwnd != nint.Zero)
                {
                    _ = DestroyWindow(_hostHwnd);
                    _hostHwnd = nint.Zero;
                }

                return;
            }

            _controller = controller;
        }

        if (_controller == null)
        {
            SetInitializationError("Failed to create WebView2 controller.");
            return;
        }

        _coreWebView2 = _controller.CoreWebView2;

        // Wire up events
        _coreWebView2.NavigationStarting += OnCoreNavigationStarting;
        _coreWebView2.NavigationCompleted += OnCoreNavigationCompleted;
        _coreWebView2.DocumentTitleChanged += OnCoreDocumentTitleChanged;
        _coreWebView2.WebMessageReceived += OnCoreWebMessageReceived;
        _coreWebView2.NewWindowRequested += OnCoreNewWindowRequested;
        _coreWebView2.SourceChanged += OnCoreSourceChanged;
        _coreWebView2.ContentLoading += OnCoreContentLoading;
        _coreWebView2.ProcessFailed += OnCoreProcessFailed;
        _controller.ZoomFactorChanged += OnCoreZoomFactorChanged;

        // Apply initial settings
        _controller.ZoomFactor = ZoomFactor;
        var bg = DefaultBackgroundColor;
        _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.A, bg.R, bg.G, bg.B);

        if (_hostHwnd == nint.Zero)
        {
            _controller.Bounds = GetAbsoluteControllerBounds(hostRect, controllerBounds);
        }
        else
        {
            // Place the controller so clipped host window still maps to full control area.
            _controller.Bounds = controllerBounds;
        }

        _controller.IsVisible = true;
        _controller.NotifyParentWindowPositionChanged();
        _isHostVisible = true;
        _lastHostRect = Rectangle.Empty;
        _lastControllerBounds = Rectangle.Empty;

        // Track parent window movement so host window follows
        _parentWindow.LocationChanged -= OnParentWindowLocationChanged;
        _parentWindow.LocationChanged += OnParentWindowLocationChanged;
        _parentWindow.SizeChanged -= OnParentWindowSizeChanged;
        _parentWindow.SizeChanged += OnParentWindowSizeChanged;
        EnsurePositionSyncTimer();
        UpdateHostWindowPosition(force: true);

        // Navigate if Source was set before initialization
        if (Source != null)
        {
            _coreWebView2.Navigate(Source.AbsoluteUri);
        }
    }

    private void OnParentWindowLocationChanged(object? sender, EventArgs e)
    {
        UpdateHostWindowPosition();
    }

    private void OnParentWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHostWindowPosition();
    }

    private void OnSelfSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHostWindowPosition();
    }

    private void OnAncestorScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateHostWindowPosition();
    }

    private void OnPositionSyncTick(object? sender, EventArgs e)
    {
        UpdateHostWindowPosition();
    }

    private void EnsurePositionSyncTimer()
    {
        if (_positionSyncTimer != null)
            return;

        _positionSyncTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnPositionSyncTick,
            _dispatcher);
        _positionSyncTimer.Start();
    }

    private void StopPositionSyncTimer()
    {
        if (_positionSyncTimer == null)
            return;

        _positionSyncTimer.Stop();
        _positionSyncTimer.Tick -= OnPositionSyncTick;
        _positionSyncTimer = null;
    }

    private bool TryGetHostPlacement(out Rectangle hostRect, out Rectangle controllerBounds)
    {
        hostRect = Rectangle.Empty;
        controllerBounds = Rectangle.Empty;

        if (_parentWindow == null || _parentWindow.Handle == nint.Zero || ActualWidth <= 0 || ActualHeight <= 0)
            return false;

        if (!IsAttachedToParentWindow())
            return false;

        var rawDip = GetRectRelativeToParentWindow(this);
        if (rawDip.IsEmpty)
            return false;

        var visibleDip = ClipToVisibleAncestorBounds(rawDip);
        if (visibleDip.IsEmpty)
            return false;

        var dpi = _parentWindow.DpiScale;
        var rawPx = DipRectToPixelRect(rawDip, dpi);
        var visiblePx = DipRectToPixelRect(visibleDip, dpi);
        if (visiblePx.Width <= 0 || visiblePx.Height <= 0)
            return false;

        hostRect = visiblePx;
        controllerBounds = new Rectangle(
            rawPx.X - visiblePx.X,
            rawPx.Y - visiblePx.Y,
            rawPx.Width,
            rawPx.Height);
        return true;
    }

    private Rect GetRectRelativeToParentWindow(FrameworkElement element)
    {
        if (_parentWindow == null)
            return Rect.Empty;

        double x = 0;
        double y = 0;
        Visual? current = element;
        bool reachedParentWindow = false;

        while (current != null)
        {
            if (ReferenceEquals(current, _parentWindow))
            {
                reachedParentWindow = true;
                break;
            }

            if (current is UIElement ui)
            {
                var bounds = ui.VisualBounds;
                var ro = ui.RenderOffset;
                x += bounds.X + ro.X;
                y += bounds.Y + ro.Y;
            }

            current = current.VisualParent;
        }

        if (!reachedParentWindow)
            return Rect.Empty;

        return new Rect(x, y, element.ActualWidth, element.ActualHeight);
    }

    private Rect ClipToVisibleAncestorBounds(Rect rawRect)
    {
        if (_parentWindow == null)
            return Rect.Empty;

        var visible = rawRect;

        Visual? current = this;
        bool reachedParentWindow = false;
        while (current != null)
        {
            if (ReferenceEquals(current, _parentWindow))
            {
                reachedParentWindow = true;
                break;
            }

            if (current is UIElement ui)
            {
                if (ui.Visibility != Visibility.Visible)
                    return Rect.Empty;

                if (TryGetLayoutClipRect(ui, out var clipRect) && current is FrameworkElement fe)
                {
                    var origin = GetRectRelativeToParentWindow(fe);
                    var clipInParent = new Rect(
                        origin.X + clipRect.X,
                        origin.Y + clipRect.Y,
                        clipRect.Width,
                        clipRect.Height);
                    visible = visible.Intersect(clipInParent);
                    if (visible.IsEmpty)
                        return Rect.Empty;
                }
            }

            current = current.VisualParent;
        }

        if (!reachedParentWindow)
            return Rect.Empty;

        var windowWidth = _parentWindow.ActualWidth > 0 ? _parentWindow.ActualWidth : _parentWindow.Width;
        var windowHeight = _parentWindow.ActualHeight > 0 ? _parentWindow.ActualHeight : _parentWindow.Height;
        if (windowWidth <= 0 || windowHeight <= 0)
            return Rect.Empty;

        var windowBounds = new Rect(0, 0, windowWidth, windowHeight);
        return visible.Intersect(windowBounds);
    }

    private static bool TryGetLayoutClipRect(UIElement element, out Rect clipRect)
    {
        clipRect = Rect.Empty;
        var clip = element.GetLayoutClip();

        if (clip is Rect rect && !rect.IsEmpty)
        {
            clipRect = rect;
            return true;
        }

        if (clip is Media.Geometry geometry && !geometry.Bounds.IsEmpty)
        {
            clipRect = geometry.Bounds;
            return true;
        }

        return false;
    }

    private static Rectangle DipRectToPixelRect(Rect rect, double dpi)
    {
        var left = (int)Math.Floor(rect.X * dpi);
        var top = (int)Math.Floor(rect.Y * dpi);
        var right = (int)Math.Ceiling(rect.Right * dpi);
        var bottom = (int)Math.Ceiling(rect.Bottom * dpi);

        if (right <= left)
            right = left + 1;
        if (bottom <= top)
            bottom = top + 1;

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Rectangle GetAbsoluteControllerBounds(Rectangle hostRect, Rectangle controllerBounds)
    {
        return new Rectangle(
            hostRect.X + controllerBounds.X,
            hostRect.Y + controllerBounds.Y,
            controllerBounds.Width,
            controllerBounds.Height);
    }

    private bool TryCreateDirectCompositionTarget(nint parentHwnd)
    {
        ReleaseDirectCompositionResources();

        var iid = IID_IDCompositionDevice;
        var hr = DCompositionCreateDevice2(nint.Zero, in iid, out var dcompDevice);
        if (hr < 0 || dcompDevice == null)
        {
            SetInitializationError($"Failed to create DirectComposition device (HRESULT 0x{hr:X8}).");
            return false;
        }

        hr = dcompDevice.CreateTargetForHwnd(parentHwnd, topmost: false, out var dcompTarget);
        if (hr < 0 || dcompTarget == null)
        {
            Marshal.ReleaseComObject(dcompDevice);
            if ((uint)hr == DCOMPOSITION_ERROR_WINDOW_ALREADY_COMPOSED)
            {
                SetInitializationError(
                    "Failed to create DirectComposition target: parent HWND is already composed (0x88980800). " +
                    "Another WebView instance may still hold composition resources.");
            }
            else
            {
                SetInitializationError($"Failed to create DirectComposition target (HRESULT 0x{hr:X8}).");
            }
            return false;
        }

        hr = dcompDevice.CreateVisual(out var dcompVisual);
        if (hr < 0 || dcompVisual == null)
        {
            Marshal.ReleaseComObject(dcompTarget);
            Marshal.ReleaseComObject(dcompDevice);
            SetInitializationError($"Failed to create DirectComposition visual (HRESULT 0x{hr:X8}).");
            return false;
        }

        hr = dcompTarget.SetRoot(dcompVisual);
        if (hr < 0)
        {
            Marshal.ReleaseComObject(dcompVisual);
            Marshal.ReleaseComObject(dcompTarget);
            Marshal.ReleaseComObject(dcompDevice);
            SetInitializationError($"Failed to set DirectComposition root visual (HRESULT 0x{hr:X8}).");
            return false;
        }

        hr = dcompDevice.Commit();
        if (hr < 0)
        {
            Marshal.ReleaseComObject(dcompVisual);
            Marshal.ReleaseComObject(dcompTarget);
            Marshal.ReleaseComObject(dcompDevice);
            SetInitializationError($"Failed to commit DirectComposition changes (HRESULT 0x{hr:X8}).");
            return false;
        }

        _dcompDevice = dcompDevice;
        _dcompTarget = dcompTarget;
        _dcompVisual = dcompVisual;
        return true;
    }

    private void ReleaseDirectCompositionResources()
    {
        if (_dcompVisual != null)
        {
            Marshal.ReleaseComObject(_dcompVisual);
            _dcompVisual = null;
        }

        if (_dcompTarget != null)
        {
            Marshal.ReleaseComObject(_dcompTarget);
            _dcompTarget = null;
        }

        if (_dcompDevice != null)
        {
            Marshal.ReleaseComObject(_dcompDevice);
            _dcompDevice = null;
        }
    }

    private void UpdateHostWindowPosition(bool force = false)
    {
        if (_controller == null || _parentWindow == null)
            return;

        var attachedToWindow = IsAttachedToParentWindow();
        Rectangle rect;
        Rectangle controllerBounds;
        bool hasPlacement = TryGetHostPlacement(out rect, out controllerBounds);
        bool shouldBeVisible = attachedToWindow
            && Visibility == Visibility.Visible
            && hasPlacement;

        if (!shouldBeVisible)
        {
            if (_isHostVisible || force)
            {
                if (_hostHwnd != nint.Zero)
                    _ = ShowWindow(_hostHwnd, SW_HIDE);
                _controller.IsVisible = false;
                _isHostVisible = false;
            }
            return;
        }

        if (!_isHostVisible)
        {
            if (_hostHwnd != nint.Zero)
                _ = ShowWindow(_hostHwnd, SW_SHOW);
            _controller.IsVisible = true;
            _isHostVisible = true;
            force = true;
        }

        if (!force && rect == _lastHostRect && controllerBounds == _lastControllerBounds)
            return;

        if (_hostHwnd != nint.Zero)
        {
            // Keep current Z order; only move/resize the host window.
            _ = SetWindowPos(_hostHwnd, nint.Zero,
                rect.X, rect.Y, rect.Width, rect.Height,
                SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOZORDER);

            // Keep web content aligned with the unclipped control rect.
            if (force || controllerBounds != _lastControllerBounds)
                _controller.Bounds = controllerBounds;
        }
        else
        {
            // Windowless controller bounds are in parent-window client pixels.
            var absoluteBounds = GetAbsoluteControllerBounds(rect, controllerBounds);
            if (force || _controller.Bounds != absoluteBounds)
                _controller.Bounds = absoluteBounds;
        }

        _controller.NotifyParentWindowPositionChanged();
        _lastHostRect = rect;
        _lastControllerBounds = controllerBounds;
    }

    private bool IsAttachedToParentWindow()
    {
        if (_parentWindow == null)
            return false;

        Visual? current = this;
        while (current != null)
        {
            if (ReferenceEquals(current, _parentWindow))
                return true;

            current = current.VisualParent;
        }

        return false;
    }

    private static bool UsesNoRedirectionBitmap(nint hwnd)
    {
        const int GWL_EXSTYLE = -20;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        return (exStyle & (int)WS_EX_NOREDIRECTIONBITMAP) != 0;
    }

    #endregion

    #region Property Changed Handlers

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WebView webView)
            webView.OnSourceChanged((Uri?)e.NewValue);
    }

    private void OnSourceChanged(Uri? newSource)
    {
        if (newSource != null && _isInitialized && _coreWebView2 != null)
        {
            _isNavigating = true;
            _coreWebView2.Navigate(newSource.AbsoluteUri);
        }
    }

    private static void OnZoomFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WebView webView && webView._controller != null)
            webView._controller.ZoomFactor = (double)e.NewValue!;
    }

    private static object CoerceZoomFactor(DependencyObject d, object? value)
    {
        var zoom = (double)(value ?? 1.0);
        return Math.Clamp(zoom, 0.25, 4.0);
    }

    private static void OnDefaultBackgroundColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WebView webView && webView._controller != null)
        {
            var color = (Media.Color)e.NewValue!;
            webView._controller.DefaultBackgroundColor =
                System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }

    #endregion

    #region CoreWebView2 Event Handlers

    private void OnCoreNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _isNavigating = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            var args = new WebViewNavigationStartingEventArgs(uri, e.IsRedirected);
            NavigationStarting?.Invoke(this, args);
            if (args.Cancel) e.Cancel = true;
        }
    }

    private void OnCoreNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _isNavigating = false;
        UpdateNavigationState(_coreWebView2?.CanGoBack ?? false, _coreWebView2?.CanGoForward ?? false);
        NavigationCompleted?.Invoke(this,
            new WebViewNavigationCompletedEventArgs(e.IsSuccess, e.HttpStatusCode));
    }

    private void OnCoreDocumentTitleChanged(object? sender, object e)
    {
        _documentTitle = _coreWebView2?.DocumentTitle ?? string.Empty;
        DocumentTitleChanged?.Invoke(this, new WebViewDocumentTitleChangedEventArgs(_documentTitle));
    }

    private void OnCoreWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        WebMessageReceived?.Invoke(this,
            new WebViewWebMessageReceivedEventArgs(e.TryGetWebMessageAsString()));
    }

    private void OnCoreNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            var args = new WebViewNewWindowRequestedEventArgs(uri, e.IsUserInitiated);
            NewWindowRequested?.Invoke(this, args);
            if (args.Handled) e.Handled = true;
        }
    }

    private void OnCoreSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        Uri? sourceUri = null;
        if (_coreWebView2?.Source != null)
            Uri.TryCreate(_coreWebView2.Source, UriKind.Absolute, out sourceUri);
        SourceChanged?.Invoke(this,
            new WebViewSourceChangedEventArgs(sourceUri, e.IsNewDocument));
    }

    private void OnCoreContentLoading(object? sender, CoreWebView2ContentLoadingEventArgs e)
    {
        ContentLoading?.Invoke(this, new WebViewContentLoadingEventArgs(true));
    }

    private void OnCoreProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        SetInitializationError($"WebView2 process failed: {e.ProcessFailedKind}");
    }

    private void OnCoreZoomFactorChanged(object? sender, object e)
    {
        if (_controller != null)
            SetValue(ZoomFactorProperty, _controller.ZoomFactor);
    }

    #endregion

    #region Input Forwarding

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (TrySendCompositionMouseInput(CoreWebView2MouseEventKind.Move, e, 0))
            e.Handled = true;
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        var eventKind = GetMouseButtonEventKind(e, isDown: true);
        if (!eventKind.HasValue)
            return;

        var mouseData = GetMouseData(e, eventKind.Value);
        if (TrySendCompositionMouseInput(eventKind.Value, e, mouseData))
            e.Handled = true;
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        var eventKind = GetMouseButtonEventKind(e, isDown: false);
        if (!eventKind.HasValue)
            return;

        var mouseData = GetMouseData(e, eventKind.Value);
        if (TrySendCompositionMouseInput(eventKind.Value, e, mouseData))
            e.Handled = true;
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        var mouseData = ((uint)(ushort)(short)e.Delta) << 16;
        if (TrySendCompositionMouseInput(CoreWebView2MouseEventKind.Wheel, e, mouseData))
            e.Handled = true;
    }

    private bool TrySendCompositionMouseInput(
        CoreWebView2MouseEventKind eventKind,
        MouseEventArgs e,
        uint mouseData)
    {
        if (!_isWindowlessComposition || _compositionController == null || _parentWindow == null)
            return false;

        var pointDip = e.GetPosition(this);
        if (pointDip.X < 0 || pointDip.Y < 0 || pointDip.X > ActualWidth || pointDip.Y > ActualHeight)
            return false;

        var dpi = _parentWindow.DpiScale;
        var pointPx = new System.Drawing.Point(
            (int)Math.Round(pointDip.X * dpi),
            (int)Math.Round(pointDip.Y * dpi));
        var virtualKeys = GetMouseVirtualKeys(e);
        _compositionController.SendMouseInput(eventKind, virtualKeys, mouseData, pointPx);
        return true;
    }

    private static CoreWebView2MouseEventKind? GetMouseButtonEventKind(MouseButtonEventArgs e, bool isDown)
    {
        return e.ChangedButton switch
        {
            MouseButton.Left when isDown && e.ClickCount > 1 => CoreWebView2MouseEventKind.LeftButtonDoubleClick,
            MouseButton.Left when isDown => CoreWebView2MouseEventKind.LeftButtonDown,
            MouseButton.Left => CoreWebView2MouseEventKind.LeftButtonUp,
            MouseButton.Middle when isDown && e.ClickCount > 1 => CoreWebView2MouseEventKind.MiddleButtonDoubleClick,
            MouseButton.Middle when isDown => CoreWebView2MouseEventKind.MiddleButtonDown,
            MouseButton.Middle => CoreWebView2MouseEventKind.MiddleButtonUp,
            MouseButton.Right when isDown && e.ClickCount > 1 => CoreWebView2MouseEventKind.RightButtonDoubleClick,
            MouseButton.Right when isDown => CoreWebView2MouseEventKind.RightButtonDown,
            MouseButton.Right => CoreWebView2MouseEventKind.RightButtonUp,
            MouseButton.XButton1 or MouseButton.XButton2 when isDown && e.ClickCount > 1 => CoreWebView2MouseEventKind.XButtonDoubleClick,
            MouseButton.XButton1 or MouseButton.XButton2 when isDown => CoreWebView2MouseEventKind.XButtonDown,
            MouseButton.XButton1 or MouseButton.XButton2 => CoreWebView2MouseEventKind.XButtonUp,
            _ => null
        };
    }

    private static uint GetMouseData(MouseButtonEventArgs e, CoreWebView2MouseEventKind eventKind)
    {
        if (eventKind is CoreWebView2MouseEventKind.XButtonDown
            or CoreWebView2MouseEventKind.XButtonUp
            or CoreWebView2MouseEventKind.XButtonDoubleClick)
        {
            var xButton = e.ChangedButton == MouseButton.XButton2 ? 2u : 1u;
            return xButton << 16;
        }

        return 0;
    }

    private static CoreWebView2MouseEventVirtualKeys GetMouseVirtualKeys(MouseEventArgs e)
    {
        var keys = CoreWebView2MouseEventVirtualKeys.None;

        if (e.LeftButton == MouseButtonState.Pressed)
            keys |= CoreWebView2MouseEventVirtualKeys.LeftButton;
        if (e.RightButton == MouseButtonState.Pressed)
            keys |= CoreWebView2MouseEventVirtualKeys.RightButton;
        if (e.MiddleButton == MouseButtonState.Pressed)
            keys |= CoreWebView2MouseEventVirtualKeys.MiddleButton;
        if (e.XButton1 == MouseButtonState.Pressed)
            keys |= CoreWebView2MouseEventVirtualKeys.XButton1;
        if (e.XButton2 == MouseButtonState.Pressed)
            keys |= CoreWebView2MouseEventVirtualKeys.XButton2;
        if ((e.KeyboardModifiers & ModifierKeys.Shift) != 0)
            keys |= CoreWebView2MouseEventVirtualKeys.Shift;
        if ((e.KeyboardModifiers & ModifierKeys.Control) != 0)
            keys |= CoreWebView2MouseEventVirtualKeys.Control;

        return keys;
    }

    #endregion

    #region Navigation State

    private void UpdateNavigationState(bool canGoBack, bool canGoForward)
    {
        SetValue(CanGoBackPropertyKey.DependencyProperty, canGoBack);
        SetValue(CanGoForwardPropertyKey.DependencyProperty, canGoForward);
    }

    #endregion

    #region Visual Tree Integration

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        if (VisualParent != null)
        {
            // Re-added to visual tree: show host content if initialized.
            if (_isInitialized && _controller != null)
            {
                EnsurePositionSyncTimer();
                UpdateHostWindowPosition(force: true);
                InvalidateArrange(); // Recompute position
            }
            else if (!_disposed)
            {
                InvalidateArrange();
            }
        }
        else
        {
            // Removed from visual tree: release controller and composition resources immediately.
            StopPositionSyncTimer();
            ReleaseControllerResources(clearEnvironment: false, invalidateInitialization: true);
        }
    }

    #endregion

    #region Helpers

    private Window? FindParentWindow()
    {
        Visual? current = this;
        while (current != null)
        {
            if (current is Window window) return window;
            current = current.VisualParent;
        }
        return null;
    }

    private static string GetWebViewUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Jalium.UI", "WebView2");
    }

    private bool IsInitializationStale(int attachmentVersion)
    {
        return _disposed
            || attachmentVersion != _visualAttachmentVersion
            || VisualParent == null
            || _parentWindow == null
            || !IsAttachedToParentWindow();
    }

    private void ReleaseControllerResources(bool clearEnvironment, bool invalidateInitialization)
    {
        if (invalidateInitialization)
            _visualAttachmentVersion++;

        if (_coreWebView2 != null)
        {
            _coreWebView2.NavigationStarting -= OnCoreNavigationStarting;
            _coreWebView2.NavigationCompleted -= OnCoreNavigationCompleted;
            _coreWebView2.DocumentTitleChanged -= OnCoreDocumentTitleChanged;
            _coreWebView2.WebMessageReceived -= OnCoreWebMessageReceived;
            _coreWebView2.NewWindowRequested -= OnCoreNewWindowRequested;
            _coreWebView2.SourceChanged -= OnCoreSourceChanged;
            _coreWebView2.ContentLoading -= OnCoreContentLoading;
            _coreWebView2.ProcessFailed -= OnCoreProcessFailed;
            _coreWebView2 = null;
        }

        if (_controller != null)
        {
            _controller.ZoomFactorChanged -= OnCoreZoomFactorChanged;
            try
            {
                _controller.IsVisible = false;
            }
            catch
            {
                // Best-effort shutdown during tree detach.
            }

            try
            {
                _controller.Close();
            }
            catch
            {
                // Ignore shutdown failures.
            }

            _controller = null;
        }

        _compositionController = null;

        if (_parentWindow != null)
        {
            _parentWindow.LocationChanged -= OnParentWindowLocationChanged;
            _parentWindow.SizeChanged -= OnParentWindowSizeChanged;
            _parentWindow = null;
        }

        if (_hostHwnd != nint.Zero)
        {
            _ = DestroyWindow(_hostHwnd);
            _hostHwnd = nint.Zero;
        }

        ReleaseDirectCompositionResources();
        StopPositionSyncTimer();

        _isInitialized = false;
        _isNavigating = false;
        _isHostVisible = false;
        _lastHostRect = Rectangle.Empty;
        _lastControllerBounds = Rectangle.Empty;

        if (clearEnvironment)
            _environment = null;
    }

    private void SetInitializationError(string message, Exception? ex = null)
    {
        _isInitialized = false;
        _initError = ex == null
            ? message
            : $"{message} {ex.GetType().Name}: {ex.Message}";
    }

    private void SetInitializationErrorFromException(Exception ex)
    {
        if (ex is WebView2RuntimeNotFoundException)
        {
            SetInitializationError(
                "Microsoft Edge WebView2 Runtime is not installed. Install Runtime and restart the app.",
                ex);
            return;
        }

        if (ex is NotSupportedException notSupportedEx &&
            notSupportedEx.Message.Contains("Built-in COM has been disabled", StringComparison.OrdinalIgnoreCase))
        {
            SetInitializationError(
                "Built-in COM interop is disabled for this app. Enable BuiltInComInteropSupport or disable AOT for Debug.",
                ex);
            return;
        }

        if (ex is COMException comEx && (uint)comEx.HResult == 0x80070002)
        {
            SetInitializationError(
                "Microsoft Edge WebView2 Runtime is missing or inaccessible. Install Runtime and retry.",
                ex);
            return;
        }

        SetInitializationError("Failed to initialize WebView2.", ex);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            ReleaseControllerResources(clearEnvironment: true, invalidateInitialization: true);

            SizeChanged -= OnSelfSizeChanged;
            RemoveHandler(ScrollViewer.ScrollChangedEvent, _scrollChangedHandler);
            RemoveHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
            RemoveHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
            RemoveHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
            RemoveHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));
        }
    }

    ~WebView()
    {
        Dispose(false);
    }

    #endregion

    #region Win32 Interop

    private const string HostWindowClassName = "JaliumWebViewHost";

    private const uint WS_CHILD = 0x40000000;
    private const uint WS_CLIPCHILDREN = 0x02000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_NOZORDER = 0x0004;
    private static readonly nint HWND_BOTTOM = new nint(1);
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const uint DCOMPOSITION_ERROR_WINDOW_ALREADY_COMPOSED = 0x88980800;
    private static readonly Guid IID_IDCompositionDevice = new("C37EA93A-E7AA-450D-B16F-9746CB0407F3");

    private static bool _hostClassRegistered;
    private static WndProcDelegate? _wndProcDelegate;
    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    private static void RegisterHostWindowClass()
    {
        if (_hostClassRegistered) return;

        _wndProcDelegate = HostWndProc;

        WNDCLASSEX wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            hCursor = nint.Zero,
            hbrBackground = nint.Zero,
            lpszClassName = HostWindowClassName
        };

        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_CLASS_ALREADY_EXISTS)
                throw new InvalidOperationException($"Failed to register WebView host window class. Win32 error: {error}.");
        }

        _hostClassRegistered = true;
    }

    private static nint HostWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        // Let WebView2 handle all messages via its child HWND
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint GetParent(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(nint hWnd, int nIndex);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? lpModuleName);

    [DllImport("dcomp.dll", ExactSpelling = true)]
    private static extern int DCompositionCreateDevice2(
        nint renderingDevice,
        in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IDCompositionDevice? dcompositionDevice);

    [ComImport]
    [Guid("C37EA93A-E7AA-450D-B16F-9746CB0407F3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDCompositionDevice
    {
        [PreserveSig]
        int Commit();

        [PreserveSig]
        int WaitForCommitCompletion();

        [PreserveSig]
        int GetFrameStatistics(out DCOMPOSITION_FRAME_STATISTICS frameStatistics);

        [PreserveSig]
        int CreateTargetForHwnd(
            nint hwnd,
            [MarshalAs(UnmanagedType.Bool)] bool topmost,
            out IDCompositionTarget? target);

        [PreserveSig]
        int CreateVisual(out IDCompositionVisual? visual);
    }

    [ComImport]
    [Guid("EACDD04C-117E-4E17-88F4-D1B12B0E3D89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDCompositionTarget
    {
        [PreserveSig]
        int SetRoot(IDCompositionVisual? visual);
    }

    [ComImport]
    [Guid("4D93059D-097B-4651-9A60-F0F25116E2F1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDCompositionVisual
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DCOMPOSITION_FRAME_STATISTICS
    {
        public long lastFrameTime;
        public long currentCompositionRate;
        public long currentTime;
        public long timeFrequency;
        public long nextEstimatedFrameTime;
    }

    #endregion
}

#region Event Args

public sealed class WebViewNavigationStartingEventArgs : EventArgs
{
    public Uri Uri { get; }
    public bool IsRedirected { get; }
    public bool Cancel { get; set; }

    public WebViewNavigationStartingEventArgs(Uri uri, bool isRedirected)
    {
        Uri = uri;
        IsRedirected = isRedirected;
    }
}

public sealed class WebViewNavigationCompletedEventArgs : EventArgs
{
    public bool IsSuccess { get; }
    public int HttpStatusCode { get; }

    public WebViewNavigationCompletedEventArgs(bool isSuccess, int httpStatusCode)
    {
        IsSuccess = isSuccess;
        HttpStatusCode = httpStatusCode;
    }
}

public sealed class WebViewNewWindowRequestedEventArgs : EventArgs
{
    public Uri Uri { get; }
    public bool IsUserInitiated { get; }
    public bool Handled { get; set; }

    public WebViewNewWindowRequestedEventArgs(Uri uri, bool isUserInitiated)
    {
        Uri = uri;
        IsUserInitiated = isUserInitiated;
    }
}

public sealed class WebViewDocumentTitleChangedEventArgs : EventArgs
{
    public string Title { get; }

    public WebViewDocumentTitleChangedEventArgs(string title)
    {
        Title = title;
    }
}

public sealed class WebViewWebMessageReceivedEventArgs : EventArgs
{
    public string WebMessageAsString { get; }

    public WebViewWebMessageReceivedEventArgs(string message)
    {
        WebMessageAsString = message;
    }
}

public sealed class WebViewContentLoadingEventArgs : EventArgs
{
    public bool IsLoading { get; }

    public WebViewContentLoadingEventArgs(bool isLoading)
    {
        IsLoading = isLoading;
    }
}

public sealed class WebViewSourceChangedEventArgs : EventArgs
{
    public Uri? Source { get; }
    public bool IsNewDocument { get; }

    public WebViewSourceChangedEventArgs(Uri? source, bool isNewDocument)
    {
        Source = source;
        IsNewDocument = isNewDocument;
    }
}

#endregion

