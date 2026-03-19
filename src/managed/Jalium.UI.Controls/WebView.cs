using System.Drawing;
using System.Diagnostics;
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
public partial class WebView : FrameworkElement, IDisposable
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.WebViewAutomationPeer(this);
    }

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
    private readonly bool _isWindowlessComposition =
        !string.Equals(Environment.GetEnvironmentVariable("JALIUM_WEBVIEW2_COMPOSITION"), "disable", StringComparison.OrdinalIgnoreCase);
    private readonly bool _debugEnabled = IsDebugEnabled();
    private readonly string _debugInstanceId = Guid.NewGuid().ToString("N")[..8];

    private object? _compositionRootVisualTarget;
    private nint _compositionRootVisualHandle;
    private Interop.RenderTarget? _compositionVisualOwner;

    private Window? _parentWindow;
    private readonly Dispatcher _dispatcher;
    private readonly ScrollChangedEventHandler _scrollChangedHandler;
    private DispatcherTimer? _positionSyncTimer;
    private Rectangle _lastHostRect = Rectangle.Empty;
    private Rectangle _lastControllerBounds = Rectangle.Empty;
    private bool _isHostVisible;
    private int _visualAttachmentVersion;
    private string? _lastDebugPlacementSignature;
    private Rectangle _lastDebugPunchRect = Rectangle.Empty;

    private static readonly Jalium.UI.Media.SolidColorBrush s_debugFrameBrush =
        new(Media.Color.FromArgb(255, 255, 99, 71));
    private static readonly Jalium.UI.Media.SolidColorBrush s_debugTextBrush =
        new(Media.Color.FromArgb(255, 255, 255, 255));
    private static readonly Jalium.UI.Media.SolidColorBrush s_debugTextBackgroundBrush =
        new(Media.Color.FromArgb(210, 20, 20, 20));
    private static readonly Jalium.UI.Media.Pen s_debugFramePen =
        new(s_debugFrameBrush, 2);

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

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(WebView),
            new PropertyMetadata(null, OnSourceChanged));

    private static readonly DependencyPropertyKey CanGoBackPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanGoBack), typeof(bool), typeof(WebView),
            new PropertyMetadata(false));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanGoBackProperty = CanGoBackPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanGoForwardPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanGoForward), typeof(bool), typeof(WebView),
            new PropertyMetadata(false));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanGoForwardProperty = CanGoForwardPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ZoomFactorProperty =
        DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(WebView),
            new PropertyMetadata(1.0, OnZoomFactorChanged, CoerceZoomFactor));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
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

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanGoBack => (bool)GetValue(CanGoBackProperty)!;
    public bool CanGoForward => (bool)GetValue(CanGoForwardProperty)!;

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public double ZoomFactor
    {
        get => (double)GetValue(ZoomFactorProperty)!;
        set => SetValue(ZoomFactorProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
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
                    {
                        InvalidateArrange();

                        // The render target may have been swapped to a composition
                        // target during initialization.  DXGI FLIP_SEQUENTIAL uses
                        // two back buffers — force a full window invalidation so
                        // BOTH buffers are repainted, preventing ghost images in
                        // the sidebar or other areas outside the WebView dirty region.
                        _parentWindow?.RequestFullInvalidation();
                    }
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
        DebugLog("init", $"begin actual={ActualWidth:F1}x{ActualHeight:F1} comp={_isWindowlessComposition}");
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

        if (!_isWindowlessComposition)
        {
            SetInitializationError("WebView is configured to disable composition mode. This build requires composition mode.");
            return;
        }

        if (!TryAcquireCompositionRootVisualTarget(out var rootVisualTarget))
        {
            return;
        }

        try
        {
            var compositionController = await _environment.CreateCoreWebView2CompositionControllerAsync(_parentWindow.Handle);
            if (IsInitializationStale(attachmentVersion))
            {
                compositionController.Close();
                ReleaseCompositionResources();
                return;
            }

            _compositionController = compositionController;
            _compositionController.RootVisualTarget = rootVisualTarget!;
            _controller = _compositionController;
            _parentWindow.ForceRenderFrame();
        }
        catch
        {
            ReleaseCompositionResources();
            throw;
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

        // Re-compute host placement now that the render target may have been
        // swapped to composition mode (which triggers ForceRenderFrame → layout).
        // The hostRect captured before TryAcquireCompositionRootVisualTarget may
        // be stale or a (0,0,1,1) fallback that would strand the visual at the
        // top-left corner.
        if (!TryGetHostPlacement(out hostRect, out controllerBounds))
        {
            hostRect = new Rectangle(0, 0, 1, 1);
            controllerBounds = new Rectangle(0, 0, 1, 1);
        }

        UpdateCompositionVisualPlacement(hostRect, controllerBounds);
        // Bounds tells WebView2 the screen-relative position of the content
        // within the parent HWND.  This is used for popups, context menus and
        // other windowed features.  The DComp visual handles the actual
        // rendering position; Bounds only provides the coordinate reference.
        _controller.Bounds = new Rectangle(
            hostRect.X + controllerBounds.X,
            hostRect.Y + controllerBounds.Y,
            controllerBounds.Width, controllerBounds.Height);
        DebugLog("init", $"controller ready host={FormatRect(hostRect)} ctrl={FormatRect(controllerBounds)}");

        _controller.IsVisible = true;
        _controller.NotifyParentWindowPositionChanged();
        _isHostVisible = true;
        _lastHostRect = Rectangle.Empty;
        _lastControllerBounds = Rectangle.Empty;

        // Track parent window movement so composition bounds stay in sync.
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

        // The WebView2 browser process may update its DComp visual tree at any time
        // (page load, CSS animation, JavaScript DOM changes, video playback).
        // Those changes only become visible after the hosting app's DComp device commits,
        // which happens inside RenderTarget.EndDraw.  Invalidating the WebView element
        // ensures a frame render (and commit) occurs each tick so browser content stays
        // in sync.  The actual re-render is lightweight — just a clip-aware background
        // fill + transparent punch-through for the WebView area.
        if (_isInitialized && _controller != null)
        {
            InvalidateVisual();
        }
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
        controllerBounds = CalculateControllerBounds(rawPx, visiblePx);
        return true;
    }

    private Rect GetRectRelativeToParentWindow(FrameworkElement element)
    {
        if (_parentWindow == null)
            return Rect.Empty;

        var origin = GetVisualOffsetRelativeToAncestor(element, _parentWindow);
        if (double.IsNaN(origin.X) || double.IsNaN(origin.Y))
            return Rect.Empty;

        return new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static Jalium.UI.Point GetVisualOffsetRelativeToAncestor(Visual visual, Visual? ancestor)
    {
        double x = 0;
        double y = 0;

        Visual? current = visual;
        while (current != null && current != ancestor)
        {
            if (current is UIElement uiElement)
            {
                var bounds = uiElement.VisualBounds;
                var renderOffset = uiElement.RenderOffset;
                x += bounds.X + renderOffset.X;
                y += bounds.Y + renderOffset.Y;
            }

            current = current.VisualParent;
        }

        return new Jalium.UI.Point(x, y);
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

    internal static Rectangle CalculateControllerBounds(Rectangle rawPx, Rectangle visiblePx)
    {
        return new Rectangle(
            rawPx.X - visiblePx.X,
            rawPx.Y - visiblePx.Y,
            rawPx.Width,
            rawPx.Height);
    }

    private bool TryAcquireCompositionRootVisualTarget(out object? target)
    {
        target = null;
        ReleaseCompositionResources();

        if (_parentWindow?.RenderTarget == null)
        {
            SetInitializationError("Parent window render target is not ready for WebView composition.");
            return false;
        }

        if (!_parentWindow.RenderTarget.TryCreateWebViewCompositionVisual(out var visualHandle) || visualHandle == nint.Zero)
        {
            // Parent window may still use a non-composition render target; upgrade once and retry.
            if (!_parentWindow.EnsureCompositionRenderTargetForEmbeddedContent()
                || _parentWindow.RenderTarget == null
                || !_parentWindow.RenderTarget.TryCreateWebViewCompositionVisual(out visualHandle)
                || visualHandle == nint.Zero)
            {
                SetInitializationError("Failed to allocate composition visual for WebView. Ensure the window uses composition render target.");
                return false;
            }
        }

        _compositionRootVisualHandle = visualHandle;
        _compositionVisualOwner = _parentWindow.RenderTarget;
        _compositionRootVisualTarget = visualHandle;
        target = _compositionRootVisualTarget;
        return true;
    }

    private void ReleaseCompositionResources()
    {
        _compositionRootVisualTarget = null;

        if (_compositionVisualOwner != null && _compositionRootVisualHandle != nint.Zero)
        {
            try
            {
                _compositionVisualOwner.DestroyWebViewCompositionVisual(_compositionRootVisualHandle);
            }
            catch
            {
                // Ignore shutdown cleanup failures.
            }
        }

        _compositionRootVisualHandle = nint.Zero;
        _compositionVisualOwner = null;
    }

    private void UpdateHostWindowPosition(bool force = false)
    {
        if (_controller == null || _parentWindow == null)
            return;

        var attachedToWindow = IsAttachedToParentWindow();
        Rectangle hostRect;
        Rectangle controllerBounds;
        bool hasPlacement = TryGetHostPlacement(out hostRect, out controllerBounds);
        bool shouldBeVisible = attachedToWindow
            && Visibility == Visibility.Visible
            && hasPlacement;

        if (!shouldBeVisible)
        {
            if (_isHostVisible || force)
            {
                _controller.IsVisible = false;
                _isHostVisible = false;
            }
            DebugLogPlacement("hidden", hostRect, controllerBounds, attachedToWindow, hasPlacement, force);
            return;
        }

        if (!_isHostVisible)
        {
            _controller.IsVisible = true;
            _isHostVisible = true;
            force = true;
        }

        if (!force && hostRect == _lastHostRect && controllerBounds == _lastControllerBounds)
            return;

        UpdateCompositionVisualPlacement(hostRect, controllerBounds);

        // Bounds = position within parent HWND (for popup/screen coordinate calc).
        // DComp visual handles the rendering position separately.
        var positionedBounds = new Rectangle(
            hostRect.X + controllerBounds.X,
            hostRect.Y + controllerBounds.Y,
            controllerBounds.Width, controllerBounds.Height);
        if (force || _controller.Bounds != positionedBounds)
            _controller.Bounds = positionedBounds;

        _controller.NotifyParentWindowPositionChanged();
        _lastHostRect = hostRect;
        _lastControllerBounds = controllerBounds;
        DebugLogPlacement("update", hostRect, controllerBounds, attachedToWindow, hasPlacement, force);
    }

    private void UpdateCompositionVisualPlacement(Rectangle hostRect, Rectangle controllerBounds)
    {
        if (_compositionVisualOwner == null || _compositionRootVisualHandle == nint.Zero)
            return;

        _compositionVisualOwner.SetWebViewCompositionVisualPlacement(
            _compositionRootVisualHandle,
            hostRect,
            new System.Drawing.Point(controllerBounds.X, controllerBounds.Y));
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

        // Trigger a frame render so the DComp device commits and the browser's
        // newly-rendered content becomes visible through the transparent punch-through.
        InvalidateVisual();
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
        InvalidateVisual();
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

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint hCursor);

    #endregion

    #region Input Forwarding

    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (_isWindowlessComposition && drawingContext is Interop.RenderTargetDrawingContext renderTargetDrawingContext)
        {
            var punchRect = new Rect(0, 0, ActualWidth, ActualHeight);
            renderTargetDrawingContext.PunchTransparentRect(punchRect);

            if (_debugEnabled)
            {
                var roundedPunchRect = new Rectangle(
                    0,
                    0,
                    (int)Math.Round(ActualWidth),
                    (int)Math.Round(ActualHeight));
                if (roundedPunchRect != _lastDebugPunchRect)
                {
                    _lastDebugPunchRect = roundedPunchRect;
                    DebugLog("punch", $"local={FormatRect(roundedPunchRect)}");
                }
            }
        }
    }

    protected override void OnPostRender(object drawingContext)
    {
        base.OnPostRender(drawingContext);

        if (!_debugEnabled || drawingContext is not Media.DrawingContext dc)
            return;

        var localRect = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(null, s_debugFramePen, localRect);

        var overlayText = BuildDebugOverlayText();
        if (string.IsNullOrWhiteSpace(overlayText))
            return;

        dc.DrawRectangle(
            s_debugTextBackgroundBrush,
            null,
            new Rect(4, 4, Math.Max(180, ActualWidth - 8), Math.Min(52, Math.Max(24, ActualHeight - 8))));

        var formattedText = new Media.FormattedText(overlayText, "Consolas", 11)
        {
            Foreground = s_debugTextBrush,
            MaxTextWidth = Math.Max(1, ActualWidth - 12),
            MaxTextHeight = Math.Max(18, ActualHeight - 12)
        };
        dc.DrawText(formattedText, new Jalium.UI.Point(8, 8));
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (TrySendCompositionMouseInput(CoreWebView2MouseEventKind.Move, e, 0))
        {
            e.Handled = true;

            // Poll the browser's cursor synchronously after forwarding the move.
            // This only fires when the mouse is confirmed within the WebView bounds,
            // avoiding cursor conflicts with other framework elements.
            if (_compositionController != null)
            {
                var hr = Interop.BrowserInterop.GetCursor(
                    _compositionController.NativeHandle, out var cursorHandle);
                if (hr >= 0 && cursorHandle != nint.Zero)
                {
                    SetCursor(cursorHandle);
                }
            }
        }
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
        // WebView2 SendMouseInput expects the raw wheel delta (e.g. ±120),
        // matching GET_WHEEL_DELTA_WPARAM(wParam), NOT the shifted wParam format.
        var mouseData = unchecked((uint)(short)e.Delta);
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
            // WebView2 expects the raw XBUTTON identifier (1 or 2),
            // matching GET_XBUTTON_WPARAM(wParam), not the shifted wParam.
            return e.ChangedButton == MouseButton.XButton2 ? 2u : 1u;
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

    private static bool IsDebugEnabled()
    {
        var value = Environment.GetEnvironmentVariable("JALIUM_WEBVIEW_DEBUG");
        return !string.IsNullOrWhiteSpace(value)
               && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDebugLogPath()
    {
        return Path.Combine(GetWebViewUserDataFolder(), "webview-debug.log");
    }

    private void DebugLog(string stage, string message)
    {
        if (!_debugEnabled)
            return;

        var line = $"{DateTime.Now:O} [{_debugInstanceId}] {stage} {message}";
        Trace.WriteLine(line);

        try
        {
            var path = GetDebugLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Debug logging must never break rendering.
        }
    }

    private void DebugLogPlacement(
        string stage,
        Rectangle hostRect,
        Rectangle controllerBounds,
        bool attachedToWindow,
        bool hasPlacement,
        bool force)
    {
        if (!_debugEnabled)
            return;

        var signature =
            $"{stage}|{hostRect}|{controllerBounds}|{attachedToWindow}|{hasPlacement}|{force}|{_isHostVisible}|{ActualWidth:F1}|{ActualHeight:F1}|{_initError}";
        if (string.Equals(signature, _lastDebugPlacementSignature, StringComparison.Ordinal))
            return;

        _lastDebugPlacementSignature = signature;
        DebugLog(
            "place",
            $"stage={stage} host={FormatRect(hostRect)} ctrl={FormatRect(controllerBounds)} attached={attachedToWindow} placement={hasPlacement} force={force} visible={_isHostVisible} actual={ActualWidth:F1}x{ActualHeight:F1} dpi={_parentWindow?.DpiScale:F2}");
    }

    private string BuildDebugOverlayText()
    {
        if (!_debugEnabled)
            return string.Empty;

        var host = FormatRect(_lastHostRect);
        var controller = FormatRect(_lastControllerBounds);
        var initState = _isInitialized ? "init" : "cold";
        var visState = _isHostVisible ? "vis" : "hidden";
        return $"wv:{_debugInstanceId} {initState} {visState}\nhost={host} ctrl={controller} local={Math.Round(ActualWidth)}x{Math.Round(ActualHeight)}";
    }

    private static string FormatRect(Rectangle rect)
    {
        return $"[{rect.X},{rect.Y},{rect.Width}x{rect.Height}]";
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
        DebugLog("release", $"clearEnv={clearEnvironment} invalidateInit={invalidateInitialization}");
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

        ReleaseCompositionResources();
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
        DebugLog("error", _initError);
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

        if (ex is COMException platformComEx &&
            WebView2NativeHelpers.IsPlatformNotSupportedError(platformComEx.HResult))
        {
            SetInitializationError(
                "WebView is not supported on this platform yet. The cross-platform browser backend has not been connected.",
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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Uri? Source { get; }
    public bool IsNewDocument { get; }

    public WebViewSourceChangedEventArgs(Uri? source, bool isNewDocument)
    {
        Source = source;
        IsNewDocument = isNewDocument;
    }
}

#endregion

