using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Interop;
using Jalium.UI.Interop.Win32;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using Jalium.UI.Controls.Platform;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Lightweight native popup window for rendering Popup content outside the parent window bounds.
/// Uses WS_POPUP | WS_EX_NOACTIVATE so keyboard input stays with the parent window.
/// Implements IWindowHost/ILayoutManagerHost so child elements can find their host.
/// </summary>
internal sealed partial class PopupWindow : Decorator, IWindowHost, ILayoutManagerHost, IDisposable
{
    private nint _hwnd;
    private IPlatformWindow? _platformWindow;
    private readonly Window _parentWindow;
    private RenderTarget? _renderTarget;
    private RenderTargetDrawingContext? _drawingContext;
    private readonly LayoutManager _layoutManager = new();
    private readonly Dispatcher _dispatcher;

    private int _renderState; // Bitfield: 1=Scheduled, 2=Rendering, 4=Requested, 8=DirtyBetween
    private const int RenderFlag_Scheduled = 1;
    private const int RenderFlag_Rendering = 2;
    private const int RenderFlag_Requested = 4;
    private const int RenderFlag_DirtyBetween = 8;
    private bool _renderRecoveryInProgress;
    private int _renderRecoveryAttempts;
    private const int MaxRenderRecoveryAttempts = 3;
    private DispatcherTimer? _renderRecoveryRetryTimer;
    private readonly object _renderLifecycleGate = new();
    private DispatcherOperation? _scheduledRenderOperation;
    private int _renderLifecycleGeneration;
    private bool _disposed;
    private bool _frameStartingSubscribed;
    private bool _registeredAsRenderable;
    private int _screenX;
    private int _screenY;
    private int _width;
    private int _height;
    private const int RenderRecoveryRetryDelayMs = 120;

    // Mouse tracking
    private UIElement? _lastMouseOverElement;
    private bool _isMouseTracking;
    private MouseButtonStates _platformMouseButtons = MouseButtonStates.AllReleased;
    private const uint MousePointerId = 1;
    private readonly Dictionary<uint, UIElement?> _activePointerTargets = [];
    private readonly Dictionary<uint, PointerPoint> _lastPointerPoints = [];
    private readonly Dictionary<uint, StylusDevice> _activeStylusDevices = [];
    private readonly Dictionary<uint, PointerManipulationSession> _activeManipulationSessions = [];
    private uint? _primaryPlatformTouchPointerId;
    private readonly RealTimeStylus _realTimeStylus;

    // Static WndProc delegate and window class
    private static WndProcDelegate? _wndProcDelegate;
    private static bool _popupClassRegistered;
    private static readonly Dictionary<nint, PopupWindow> _popupWindows = [];

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

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

    LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

    internal nint Handle => _hwnd;

    /// <summary>
    /// 拥有此外飞弹窗的顶层 <see cref="Window"/>。
    /// <para>
    /// 级联弹窗（在一个已经外飞成独立 <see cref="PopupWindow"/> 的菜单里再打开子菜单）需要它来把
    /// 窗口本地坐标换算回正确的窗口原点：子菜单的 PlacementTarget 位于父菜单的 PopupWindow 内，
    /// 向上走 VisualParent 不会遇到 <see cref="Window"/>，必须经由本属性解析到真正的宿主窗口。
    /// 否则从非主窗口打开的子菜单会按主窗口原点做 ClientToScreen，导致整体偏移。
    /// </para>
    /// <para>
    /// 因为每个 PopupWindow 都以真正的顶层 Window 作为 parent（见 Popup.OpenAsExternalWindow，其
    /// <c>_parentWindow</c> 由 Popup.GetParentWindow 解析而来），所以即便多层嵌套，OwnerWindow 也始终
    /// 指向真正的顶层窗口，单跳解析即可。
    /// </para>
    /// </summary>
    internal Window OwnerWindow => _parentWindow;

    internal PopupWindow(Window parentWindow, PopupRoot popupRoot)
    {
        _parentWindow = parentWindow;
        _dispatcher = Dispatcher.CurrentDispatcher
            ?? throw new InvalidOperationException("PopupWindow requires a current Dispatcher.");
        _realTimeStylus = new RealTimeStylus(this);

        // Set PopupRoot as child in the Decorator visual tree
        // This ensures PopupRoot 鈫?GetWindowHost() walks up to this PopupWindow
        Child = popupRoot;
    }

    internal void Show(int screenX, int screenY, int width, int height)
    {
        _screenX = screenX;
        _screenY = screenY;
        _width = width;
        _height = height;
        UpdateRootBoundsForHitTest();

        if (PlatformFactory.IsWindows)
        {
            RegisterPopupWindowClass();

            _hwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP,
                PopupWindowClassName,
                "",
                WS_POPUP,
                screenX, screenY, width, height,
                _parentWindow.Handle,
                nint.Zero,
                GetModuleHandle(null),
                nint.Zero);
        }
        else
        {
            // Native Linux popup coordinates are parent-relative physical
            // pixels.  Popup.WindowLocalToScreen intentionally supplies that
            // coordinate space on non-Windows hosts, so the same values work
            // for X11 override-redirect and xdg_positioner.
            const uint Borderless = 1u << 0;
            const uint Topmost = 1u << 6;
            const uint Popup = 1u << 7;
            const uint Transparent = 1u << 8;
            const uint PopupGrab = 1u << 9;
            uint style = Borderless | Topmost | Popup | Transparent;
            if (Child is PopupRoot { IsLightDismiss: true })
                style |= PopupGrab;

            _platformWindow = PlatformFactory.CreateWindow(
                string.Empty, screenX, screenY, width, height,
                style, _parentWindow.Handle)
                ?? throw new InvalidOperationException("Failed to create native popup window.");
            _platformWindow.SetEventHandler(OnPlatformEvent);
            _hwnd = _platformWindow.NativeHandle;
        }

        if (_hwnd == nint.Zero)
            throw new InvalidOperationException("Failed to create popup window.");

        _popupWindows[_hwnd] = this;
        SubscribeFrameStarting();

        // Create composition render target for per-pixel alpha transparency.
        // Uses CreateSwapChainForComposition + DirectComposition (WinUI 3 / Avalonia approach).
        // WS_EX_NOREDIRECTIONBITMAP tells DWM not to allocate a redirection surface;
        // DirectComposition provides content directly to DWM compositor.
        try
        {
            EnsureRenderTarget();
        }
        catch (RenderPipelineException ex) when (IsRecoverableRenderPipelineException(ex))
        {
            ScheduleRenderRecoveryRetry();
        }

        // Show without activating. xdg_popup and X11 override-redirect carry
        // the same no-activate semantics in the native backend.
        if (_platformWindow != null)
            _platformWindow.Show();
        else
            _ = ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        UpdateRenderableRegistration(visible: true);

        // Trigger initial layout and render
        InvalidateMeasure();
        InvalidateWindow();
    }

    internal void Hide()
    {
        if (_platformWindow != null)
        {
            _platformWindow.Hide();
        }
        else if (_hwnd != nint.Zero)
        {
            _ = ShowWindow(_hwnd, SW_HIDE);
        }

        UpdateRenderableRegistration(visible: false);
    }

    internal void MoveTo(int screenX, int screenY, int width, int height)
    {
        if (_hwnd == nint.Zero) return;

        _screenX = screenX;
        _screenY = screenY;
        bool sizeChanged = width != _width || height != _height;
        _width = width;
        _height = height;
        UpdateRootBoundsForHitTest();

        if (_platformWindow != null)
        {
            if (sizeChanged)
                _platformWindow.Resize(width, height);
            _platformWindow.Move(screenX, screenY);
        }
        else
        {
            _ = SetWindowPos(_hwnd, HWND_TOPMOST, screenX, screenY, width, height,
                SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        if (sizeChanged && _renderTarget != null)
        {
            TryResizeRenderTarget(width, height, "MoveToResize");
        }

        InvalidateWindow();
    }

    /// <summary>
    /// Checks whether the given HWND belongs to a PopupWindow.
    /// Used by Window to distinguish popup activation from external app activation.
    /// </summary>
    internal static bool IsPopupWindow(nint hwnd) => _popupWindows.ContainsKey(hwnd);

    /// <summary>
    /// Snapshots the set of currently-open <see cref="PopupWindow"/> instances.
    /// The returned array is a copy — safe to iterate even if popups close
    /// (and unregister themselves) during traversal.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="Application"/> to broadcast application-resource changes:
    /// external popup surfaces are detached visual roots, so a theme swap has to
    /// reach them explicitly or their content keeps stale implicit styles.
    /// </remarks>
    internal static PopupWindow[] SnapshotOpenPopupWindows()
    {
        var result = new PopupWindow[_popupWindows.Count];
        var i = 0;
        foreach (var popup in _popupWindows.Values)
        {
            result[i++] = popup;
        }
        return result;
    }

    #region IWindowHost

    public void InvalidateWindow()
    {
        lock (_renderLifecycleGate)
        {
            if (!IsRenderLifecycleAlive()) return;

            if (HasRenderFlag(RenderFlag_Rendering))
            {
                SetRenderFlag(RenderFlag_Requested);
                Jalium.UI.Media.CompositionTarget.RequestFrame();
                return;
            }

            SetRenderFlag(RenderFlag_DirtyBetween);
            Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.POP_INVAL);
            Jalium.UI.Media.CompositionTarget.RequestFrame();
        }
    }

    private bool IsRenderLifecycleAlive() =>
        !Volatile.Read(ref _disposed) && _hwnd != nint.Zero;

    private bool IsRenderLifecycleCurrent(int generation) =>
        generation == Volatile.Read(ref _renderLifecycleGeneration) && IsRenderLifecycleAlive();

    private bool TrySetRenderFlag(int flag)
    {
        int prev, next;
        do
        {
            prev = Volatile.Read(ref _renderState);
            if ((prev & flag) != 0) return false;
            next = prev | flag;
        } while (Interlocked.CompareExchange(ref _renderState, next, prev) != prev);

        return true;
    }

    private void SetRenderFlag(int flag)
    {
        int prev, next;
        do
        {
            prev = Volatile.Read(ref _renderState);
            next = prev | flag;
        } while (Interlocked.CompareExchange(ref _renderState, next, prev) != prev);
    }

    private void ClearRenderFlag(int flag)
    {
        int prev, next;
        do
        {
            prev = Volatile.Read(ref _renderState);
            next = prev & ~flag;
        } while (Interlocked.CompareExchange(ref _renderState, next, prev) != prev);
    }

    private void UpdateRenderFlags(int setFlags, int clearFlags)
    {
        int prev, next;
        do
        {
            prev = Volatile.Read(ref _renderState);
            next = (prev | setFlags) & ~clearFlags;
        } while (Interlocked.CompareExchange(ref _renderState, next, prev) != prev);
    }

    private bool HasRenderFlag(int flag) => (Volatile.Read(ref _renderState) & flag) != 0;

    public void AddDirtyElement(UIElement element)
    {
        // PopupWindow always does full invalidation (small surface)
        InvalidateWindow();
    }

    public void RequestFullInvalidation()
    {
        InvalidateWindow();
    }

    public void SetNativeCapture()
    {
        if (_platformWindow == null && _hwnd != nint.Zero)
            SetCapture(_hwnd);
    }

    public void ReleaseNativeCapture()
    {
        if (_platformWindow == null)
            _ = ReleaseCapture();
    }

    #endregion

    #region Rendering

    private void EnsureRenderTarget(bool forceReplaceContext = false)
    {
        lock (_renderLifecycleGate)
        {
            EnsureRenderTargetLocked(forceReplaceContext);
        }
    }

    private void EnsureRenderTargetLocked(bool forceReplaceContext)
    {
        if (!IsRenderLifecycleAlive())
        {
            return;
        }

        if (!forceReplaceContext && _renderTarget != null && _renderTarget.IsValid)
        {
            return;
        }

        _drawingContext?.ClearCache();
        _drawingContext = null;
        _renderTarget?.Dispose();
        _renderTarget = null;

        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto, forceReplace: forceReplaceContext);
        _renderTarget = _platformWindow != null
            ? context.CreateRenderTarget(
                _platformWindow.GetSurface(), Math.Max(1, _width), Math.Max(1, _height))
            : context.CreateRenderTargetForComposition(
                _hwnd, Math.Max(1, _width), Math.Max(1, _height));

        // Match D2D DPI to the parent monitor scale.
        var dpi = (float)(_parentWindow.DpiScale * 96.0);
        _renderTarget.SetDpi(dpi, dpi);
    }

    private void ProcessRender(int generation)
    {
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.POP_PR);
        lock (_renderLifecycleGate)
        {
            _scheduledRenderOperation = null;
            ClearRenderFlag(RenderFlag_Scheduled | RenderFlag_DirtyBetween);
            if (!IsRenderLifecycleCurrent(generation)) return;
        }

        RenderFrameCore(generation);
    }

    // Kept parameterless for the focused render-failure tests and direct internal callers.
    private void RenderFrame() => RenderFrameCore(Volatile.Read(ref _renderLifecycleGeneration));

    private void RenderFrameCore(int generation)
    {
        lock (_renderLifecycleGate)
        {
            RenderFrameLocked(generation);
        }
    }

    private void RenderFrameLocked(int generation)
    {
        if (!IsRenderLifecycleCurrent(generation)) return;
        if (HasRenderFlag(RenderFlag_Rendering)) return;
        UpdateRenderFlags(RenderFlag_Rendering, RenderFlag_Requested);
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.POP_RF);

        try
        {
            if (_renderTarget == null || !_renderTarget.IsValid)
            {
                EnsureRenderTarget();
            }

            var renderTarget = _renderTarget;
            if (!IsRenderLifecycleCurrent(generation) || renderTarget == null || !renderTarget.IsValid)
            {
                return;
            }

            // Layout pass 鈥?_width/_height are physical pixels, layout uses DIPs
            var dpiScale = _parentWindow.DpiScale;
            var dipWidth = _width / dpiScale;
            var dipHeight = _height / dpiScale;

            // PopupWindow itself is the hit-test root. Keep non-empty bounds in sync with the HWND size,
            // otherwise FrameworkElement.HitTestCore short-circuits and mouse input never reaches children.
            SetVisualBounds(new Rect(0, 0, dipWidth, dipHeight));

            var child = Child;
            if (child != null)
                _layoutManager.UpdateLayout(child, new Size(dipWidth, dipHeight));

            // Ensure PopupRoot has been measured and arranged
            if (child != null && IsRenderLifecycleCurrent(generation) && ReferenceEquals(Child, child))
            {
                var constraint = new Size(dipWidth, dipHeight);
                child.Measure(constraint);
                if (!IsRenderLifecycleCurrent(generation) || !ReferenceEquals(Child, child))
                {
                    return;
                }

                child.Arrange(new Rect(0, 0, dipWidth, dipHeight));
            }

            // Layout/render callbacks can synchronously close the popup. Re-check the lifecycle and
            // target identity before every native draw session so a re-entrant close cannot leave us
            // calling into a released handle.
            if (!IsCurrentRenderTarget(generation, renderTarget))
            {
                return;
            }

            renderTarget.SetFullInvalidation();
            // 合成 / 可等待 swapchain 的 BeginDraw 在 GPU 仍在 present 上一帧时返回 InvalidState（D3D12 正常背压，
            // 见 RenderTarget.TryBeginDraw 注释 + Window 主循环用法）。用 TryBeginDraw 跳过本帧并稍后重画，
            // 绝不能在原生 WM_PAINT / dispatcher-critical 回调里抛 RenderPipelineException —— 异常穿过原生回调会
            // 触发 0xC000041D（用户回调期间未处理异常）进程级崩溃。
            if (!renderTarget.TryBeginDraw())
            {
                Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.POP_BEGINFAIL);
                ScheduleRenderAfterRecovery();
                return;
            }
            bool drawSessionActive = true;

            try
            {
                // Clear with transparent background
                renderTarget.Clear(0f, 0f, 0f, 0f);

                var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
                child = Child;
                if (child != null)
                {
                    _drawingContext ??= new RenderTargetDrawingContext(renderTarget, context);
                    // Same per-frame contract as Window's frame paths: drain
                    // orphaned retained layers AND reset the effect-capture cull
                    // override on this POOLED context. Without this, a render
                    // exception that unwound past an open BeginEffectCapture last
                    // frame (the catch below keeps _drawingContext alive) would
                    // pin CurrentClipBounds to a stale capture rect and cull the
                    // popup's content against it on every subsequent frame.
                    _drawingContext.DrainPendingRetainedLayers();
                    _drawingContext.Offset = Point.Zero;
                    child.Render(_drawingContext);
                }

                if (!IsCurrentRenderTarget(generation, renderTarget))
                {
                    return;
                }

                renderTarget.EndDraw();
                drawSessionActive = false;
                Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.POP_PRESENT);
                _renderRecoveryAttempts = 0;
            }
            finally
            {
                // If an exception interrupted rendering, ensure the draw session is
                // closed so the next frame can start a fresh BeginDraw.  Without this
                // the _isDrawing flag stays true and subsequent BeginDraw calls no-op,
                // leaving the partially-drawn error frame permanently on screen.
                if (drawSessionActive && IsCurrentRenderTarget(generation, renderTarget))
                {
                    try { renderTarget.EndDraw(); } catch { }
                }
            }

            _drawingContext?.TrimCacheIfNeeded();
        }
        catch (RenderPipelineException ex)
        {
            // 本方法运行在原生 WM_PAINT / dispatcher-critical 回调内：异常【绝不能】逃逸到原生边界
            // （会触发 0xC000041D 进程级崩溃）。一律走恢复 / 重排 —— 连旧代码特判重抛的 Begin 阶段
            // 可恢复失败也走恢复，不再 throw。
            if (TryRecoverFromRenderPipelineFailure(ex, "RenderFrame"))
            {
                return;
            }

            if (IsRecoverableRenderPipelineException(ex))
            {
                ScheduleRenderRecoveryRetry();
                return;
            }

            // 不可恢复也只记录并放弃本帧：popup 渲染失败不应拖垮整个应用。
            LogRenderFailure(ex, "RenderFrame");
        }
        catch (Exception ex)
        {
            // Schedule a full re-render so the stale error frame gets cleared.
            ScheduleRenderAfterRecovery();
            LogRenderFailure(ex, "RenderFrame");
        }
        finally
        {
            ClearRenderFlag(RenderFlag_Rendering);
        }

        if (IsRenderLifecycleCurrent(generation) && HasRenderFlag(RenderFlag_Requested))
        {
            ClearRenderFlag(RenderFlag_Requested);
            InvalidateWindow();
        }
    }

    private bool IsCurrentRenderTarget(int generation, RenderTarget renderTarget) =>
        IsRenderLifecycleCurrent(generation) &&
        ReferenceEquals(_renderTarget, renderTarget) &&
        renderTarget.IsValid;

    private void TryResizeRenderTarget(int width, int height, string stage)
    {
        lock (_renderLifecycleGate)
        {
            TryResizeRenderTargetLocked(width, height, stage);
        }
    }

    private void TryResizeRenderTargetLocked(int width, int height, string stage)
    {
        if (!IsRenderLifecycleAlive())
        {
            return;
        }

        var renderTarget = _renderTarget;
        if (renderTarget == null || !renderTarget.IsValid)
        {
            return;
        }

        try
        {
            // Busy is unreachable here: popup windows render inline (never a render
            // thread), so Resize takes the same-thread close path and returns Ok.
            // Discard the result — these transient overlays are full-invalidated on
            // each show, so a (hypothetical) deferred resize would self-heal.
            _ = renderTarget.Resize(width, height);
        }
        catch (RenderPipelineException ex)
        {
            if (TryRecoverFromRenderPipelineFailure(ex, stage))
            {
                return;
            }

            if (IsRecoverableRenderPipelineException(ex))
            {
                ScheduleRenderRecoveryRetry();
                return;
            }

            LogRenderFailure(ex, stage);
        }
    }

    private static bool IsRecoverableRenderPipelineException(RenderPipelineException exception)
        => exception.Result is JaliumResult.DeviceLost
            or JaliumResult.InvalidState
            or JaliumResult.ResourceCreationFailed
            || (exception.Result == JaliumResult.Unknown &&
                string.Equals(exception.Stage, "Create", StringComparison.OrdinalIgnoreCase));

    private bool TryRecoverFromRenderPipelineFailure(RenderPipelineException exception, string stage)
    {
        if (!IsRecoverableRenderPipelineException(exception) ||
            _hwnd == nint.Zero ||
            _disposed ||
            _renderRecoveryInProgress ||
            _renderRecoveryAttempts >= MaxRenderRecoveryAttempts)
        {
            if (_renderRecoveryAttempts >= MaxRenderRecoveryAttempts)
            {
                LogRenderFailure(exception, $"{stage}:MaxRetriesExceeded");
                _renderRecoveryAttempts = 0;
            }
            return false;
        }

        _renderRecoveryAttempts++;

        _renderRecoveryInProgress = true;
        try
        {
            // Retained GPU layers bake textures on the failed device — evict
            // the popup tree's handles and destroy them through the OLD render
            // target before it is replaced (mirrors Window's device-lost
            // recovery; stale handles would otherwise reach the new device).
            Visual.ReleaseRetainedLayersRecursive(this);
            _drawingContext?.DrainPendingRetainedLayers();

            _drawingContext?.ClearCache();
            _drawingContext = null;

            bool forceReplaceContext = exception.Result == JaliumResult.DeviceLost ||
                string.Equals(exception.Stage, "Create", StringComparison.OrdinalIgnoreCase);
            EnsureRenderTarget(forceReplaceContext);

            ScheduleRenderAfterRecovery();
            return true;
        }
        catch (RenderPipelineException recoveryException) when (IsRecoverableRenderPipelineException(recoveryException))
        {
            LogRenderFailure(recoveryException, $"{stage}:Recover");
            return false;
        }
        catch (Exception recoveryException)
        {
            LogRenderFailure(recoveryException, $"{stage}:Recover");
            return false;
        }
        finally
        {
            _renderRecoveryInProgress = false;
        }
    }

    private void ScheduleRenderAfterRecovery()
    {
        ScheduleProcessRender();
        Jalium.UI.Media.CompositionTarget.RequestFrame();
    }

    private void ScheduleRenderRecoveryRetry()
    {
        if (_hwnd == nint.Zero || _disposed)
        {
            return;
        }

        _renderRecoveryRetryTimer ??= CreateRenderRecoveryRetryTimer();
        if (!_renderRecoveryRetryTimer.IsEnabled)
        {
            _renderRecoveryRetryTimer.Start();
        }
    }

    private DispatcherTimer CreateRenderRecoveryRetryTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RenderRecoveryRetryDelayMs)
        };
        timer.Tick += OnRenderRecoveryRetryTimerTick;
        return timer;
    }

    private void OnRenderRecoveryRetryTimerTick(object? sender, EventArgs e)
    {
        _renderRecoveryRetryTimer?.Stop();

        ScheduleProcessRender();
        Jalium.UI.Media.CompositionTarget.RequestFrame();
    }

    private void ScheduleProcessRender()
    {
        lock (_renderLifecycleGate)
        {
            if (!IsRenderLifecycleAlive())
            {
                return;
            }

            if (TrySetRenderFlag(RenderFlag_Scheduled))
            {
                int generation = _renderLifecycleGeneration;
                _scheduledRenderOperation = _dispatcher.BeginInvokeCritical(
                    () => ProcessRender(generation));
            }
        }
    }

    private void SubscribeFrameStarting()
    {
        if (_frameStartingSubscribed)
        {
            return;
        }

        Jalium.UI.Media.CompositionTarget.FrameStarting += OnFrameStarting;
        _frameStartingSubscribed = true;
    }

    private void UnsubscribeFrameStarting()
    {
        if (!_frameStartingSubscribed)
        {
            return;
        }

        Jalium.UI.Media.CompositionTarget.FrameStarting -= OnFrameStarting;
        _frameStartingSubscribed = false;
    }

    private void UpdateRenderableRegistration(bool visible)
    {
        // External popups own a presentable HWND/composition surface. Count them
        // directly so popup animations keep ticking even when the parent window
        // is not the surface that is currently requesting frames.
        bool shouldBe = visible && _hwnd != nint.Zero && !_disposed;
        if (shouldBe == _registeredAsRenderable)
        {
            return;
        }

        _registeredAsRenderable = shouldBe;
        if (shouldBe)
        {
            Jalium.UI.Media.CompositionTarget.NotifyRenderableWindowAdded();
        }
        else
        {
            Jalium.UI.Media.CompositionTarget.NotifyRenderableWindowRemoved();
        }
    }

    private void OnFrameStarting()
    {
        if (_hwnd == nint.Zero || _disposed || !HasRenderFlag(RenderFlag_DirtyBetween))
        {
            return;
        }

        ClearRenderFlag(RenderFlag_DirtyBetween);
        ScheduleProcessRender();
    }

    private void StopRenderRecoveryRetry()
    {
        if (_renderRecoveryRetryTimer == null)
        {
            return;
        }

        _renderRecoveryRetryTimer.Stop();
        _renderRecoveryRetryTimer.Tick -= OnRenderRecoveryRetryTimerTick;
        _renderRecoveryRetryTimer = null;
    }

    private void LogRenderFailure(Exception exception, string fallbackStage)
    {
        _ = exception;
        _ = fallbackStage;
    }

    #endregion

    #region WndProc and Input Routing

    private static void RegisterPopupWindowClass()
    {
        if (_popupClassRegistered) return;

        _wndProcDelegate = PopupWndProc;

        WNDCLASSEX wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(nint.Zero, IDC_ARROW),
            hbrBackground = nint.Zero,
            lpszClassName = PopupWindowClassName
        };

        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
            throw new InvalidOperationException("Failed to register popup window class.");

        _popupClassRegistered = true;
    }

    private static nint PopupWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (_popupWindows.TryGetValue(hWnd, out var popupWindow))
        {
            switch (msg)
            {
                case WM_DESTROY:
                    _ = _popupWindows.Remove(hWnd);
                    popupWindow.OnNativeDestroyed(hWnd);
                    return nint.Zero;

                case WM_ERASEBKGND:
                    return 1;

                case WM_PAINT:
                    popupWindow.OnPaint();
                    return nint.Zero;

                case WM_NCHITTEST:
                    // Composition-backed popup HWNDs can otherwise be treated like
                    // transparent surfaces by the OS, so force client hit testing
                    // and let the managed visual tree decide which child is interactive.
                    return HTCLIENT;

                case WM_MOUSEACTIVATE:
                    return MA_NOACTIVATE;

                case Win32PointerInterop.WM_POINTERDOWN:
                case Win32PointerInterop.WM_POINTERUPDATE:
                case Win32PointerInterop.WM_POINTERUP:
                    popupWindow.OnPointerMessage(msg, wParam, lParam);
                    return nint.Zero;

                case Win32PointerInterop.WM_POINTERWHEEL:
                case Win32PointerInterop.WM_POINTERHWHEEL:
                    popupWindow.OnPointerWheel(wParam, lParam);
                    return nint.Zero;

                case Win32PointerInterop.WM_POINTERCAPTURECHANGED:
                    popupWindow.OnPointerCaptureChanged(wParam);
                    return nint.Zero;

                case WM_MOUSEMOVE:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    popupWindow.OnMouseMove(wParam, lParam);
                    return nint.Zero;

                case WM_LBUTTONDOWN:
                    popupWindow.OnMouseButtonDown(MouseButton.Left, wParam, lParam);
                    return nint.Zero;

                case WM_LBUTTONUP:
                    popupWindow.OnMouseButtonUp(MouseButton.Left, wParam, lParam);
                    return nint.Zero;

                case WM_RBUTTONDOWN:
                    popupWindow.OnMouseButtonDown(MouseButton.Right, wParam, lParam);
                    return nint.Zero;

                case WM_RBUTTONUP:
                    popupWindow.OnMouseButtonUp(MouseButton.Right, wParam, lParam);
                    return nint.Zero;

                case WM_MBUTTONDOWN:
                    popupWindow.OnMouseButtonDown(MouseButton.Middle, wParam, lParam);
                    return nint.Zero;

                case WM_MBUTTONUP:
                    popupWindow.OnMouseButtonUp(MouseButton.Middle, wParam, lParam);
                    return nint.Zero;

                case WM_MOUSEWHEEL:
                    popupWindow.OnMouseWheel(wParam, lParam);
                    return nint.Zero;

                case WM_MOUSELEAVE:
                    popupWindow.OnMouseLeave();
                    return nint.Zero;

                case WM_CAPTURECHANGED:
                    UIElement.OnNativeCaptureChanged();
                    return nint.Zero;

                case WM_SETCURSOR:
                    if (popupWindow.OnSetCursor(lParam))
                        return 1;
                    break;
            }
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnPlatformEvent(PlatformEvent evt)
    {
        if (_disposed || _platformWindow == null)
            return;

        switch (evt.Type)
        {
            case PlatformEventType.Paint:
                RenderFrame();
                break;

            case PlatformEventType.Resize:
            {
                if (evt.Width <= 0 || evt.Height <= 0)
                    break;
                bool sizeChanged = evt.Width != _width || evt.Height != _height;
                _width = evt.Width;
                _height = evt.Height;
                UpdateRootBoundsForHitTest();
                if (sizeChanged && _renderTarget != null)
                    TryResizeRenderTarget(_width, _height, "PlatformPopupResize");
                InvalidateMeasure();
                InvalidateWindow();
                break;
            }

            case PlatformEventType.Move:
                _screenX = evt.X;
                _screenY = evt.Y;
                break;

            case PlatformEventType.CloseRequested:
                // xdg_popup.popup_done is a protocol-mandated dismissal.  Set
                // IsOpen rather than tearing the host down in isolation so the
                // Popup detaches its root and raises Closed exactly once.
                if (Child is PopupRoot root && root.OwnerPopup.IsOpen)
                    root.OwnerPopup.IsOpen = false;
                else
                    Dispose();
                break;

            case PlatformEventType.MouseEnter:
                _isMouseTracking = true;
                break;

            case PlatformEventType.MouseLeave:
                OnMouseLeave();
                break;

            case PlatformEventType.MouseMove:
            {
                Point position = PlatformPosition(evt.MouseX, evt.MouseY);
                OnMouseMove(
                    BuildMouseWParam(_platformMouseButtons),
                    PackClientPoint(evt.MouseX, evt.MouseY),
                    MapPlatformModifiers(evt.Modifiers),
                    useNativeTracking: false);
                break;
            }

            case PlatformEventType.MouseDown:
            {
                if (Child is PopupRoot { IsLightDismiss: true } dismissRoot &&
                    (evt.MouseX < 0 || evt.MouseY < 0 ||
                     evt.MouseX >= _width || evt.MouseY >= _height))
                {
                    dismissRoot.OwnerPopup.IsOpen = false;
                    break;
                }
                MouseButton button = MapPlatformMouseButton(evt.Button);
                _platformMouseButtons = _platformMouseButtons.WithButton(
                    button, MouseButtonState.Pressed);
                OnMouseButtonDown(
                    button,
                    BuildMouseWParam(_platformMouseButtons),
                    PackClientPoint(evt.MouseX, evt.MouseY),
                    MapPlatformModifiers(evt.Modifiers),
                    Math.Max(1, evt.ClickCount));
                break;
            }

            case PlatformEventType.MouseUp:
            {
                MouseButton button = MapPlatformMouseButton(evt.Button);
                _platformMouseButtons = _platformMouseButtons.WithButton(
                    button, MouseButtonState.Released);
                OnMouseButtonUp(
                    button,
                    BuildMouseWParam(_platformMouseButtons),
                    PackClientPoint(evt.MouseX, evt.MouseY),
                    MapPlatformModifiers(evt.Modifiers),
                    Math.Max(1, evt.ClickCount));
                break;
            }

            case PlatformEventType.MouseWheel:
                OnMouseWheel(
                    BuildMouseWParam(_platformMouseButtons),
                    nint.Zero,
                    PlatformPosition(evt.MouseX, evt.MouseY),
                    MapPlatformModifiers(evt.Modifiers),
                    (int)Math.Round(evt.WheelDeltaY * 120.0f));
                break;

            case PlatformEventType.PointerDown:
            case PlatformEventType.PointerMove:
            case PlatformEventType.PointerUp:
            case PlatformEventType.PointerCancel:
                OnPlatformPointerEvent(evt);
                break;

            case PlatformEventType.KeyDown:
                if (evt.KeyCode == 0x1B &&
                    Child is PopupRoot { IsLightDismiss: true } escapeRoot)
                {
                    escapeRoot.OwnerPopup.IsOpen = false;
                    break;
                }
                _parentWindow.HandleExternalPopupPlatformEvent(evt);
                break;

            case PlatformEventType.KeyUp:
            case PlatformEventType.CharInput:
            case PlatformEventType.CompositionStart:
            case PlatformEventType.CompositionUpdate:
            case PlatformEventType.CompositionEnd:
                _parentWindow.HandleExternalPopupPlatformEvent(evt);
                break;
        }
    }

    private Point PlatformPosition(float x, float y)
    {
        double dpiScale = _parentWindow.DpiScale > 0 ? _parentWindow.DpiScale : 1.0;
        return new Point(x / dpiScale, y / dpiScale);
    }

    private static MouseButton MapPlatformMouseButton(int button) => button switch
    {
        0 => MouseButton.Left,
        1 => MouseButton.Right,
        2 => MouseButton.Middle,
        3 => MouseButton.XButton1,
        4 => MouseButton.XButton2,
        _ => MouseButton.Left,
    };

    private static ModifierKeys MapPlatformModifiers(int modifiers)
    {
        ModifierKeys result = ModifierKeys.None;
        if ((modifiers & 0x01) != 0) result |= ModifierKeys.Shift;
        if ((modifiers & 0x02) != 0) result |= ModifierKeys.Control;
        if ((modifiers & 0x04) != 0) result |= ModifierKeys.Alt;
        if ((modifiers & 0x08) != 0) result |= ModifierKeys.Windows;
        return result;
    }

    private static nint PackClientPoint(float x, float y)
    {
        int px = Math.Clamp((int)Math.Round(x), short.MinValue, short.MaxValue);
        int py = Math.Clamp((int)Math.Round(y), short.MinValue, short.MaxValue);
        long packed = (ushort)(short)px | ((long)(ushort)(short)py << 16);
        return (nint)packed;
    }

    private static nint BuildMouseWParam(MouseButtonStates states)
    {
        long flags = 0;
        if (states.Left == MouseButtonState.Pressed) flags |= MK_LBUTTON;
        if (states.Right == MouseButtonState.Pressed) flags |= MK_RBUTTON;
        if (states.Middle == MouseButtonState.Pressed) flags |= MK_MBUTTON;
        if (states.XButton1 == MouseButtonState.Pressed) flags |= MK_XBUTTON1;
        if (states.XButton2 == MouseButtonState.Pressed) flags |= MK_XBUTTON2;
        return (nint)flags;
    }

    private void OnPaint()
    {
        var ps = new PAINTSTRUCT();
        _ = BeginPaint(_hwnd, out ps);
        RenderFrame();
        EndPaint(_hwnd, ref ps);
    }

    private void OnMouseMove(
        nint wParam,
        nint lParam,
        ModifierKeys? platformModifiers = null,
        bool useNativeTracking = true)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = platformModifiers ?? GetModifierKeys();
        int timestamp = Environment.TickCount;

        // Track mouse leave
        if (useNativeTracking && !_isMouseTracking)
        {
            TRACKMOUSEEVENT tme = new()
            {
                cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE,
                hwndTrack = _hwnd,
                dwHoverTime = 0
            };
            _ = TrackMouseEvent(ref tme);
            _isMouseTracking = true;
        }

        UIElement? hitElement = HitTest(position)?.VisualHit as UIElement;
        var buttons = new MouseButtonStates
        {
            Left = left,
            Middle = middle,
            Right = right,
            XButton1 = xButton1,
            XButton2 = xButton2,
        };
        Mouse.UpdateState(position, hitElement, buttons);
        var target = Mouse.GetMouseTarget(hitElement) ?? (UIElement)this;

        // Track mouse over state and raise MouseEnter/MouseLeave chains
        var newMouseOverElement = hitElement;
        if (newMouseOverElement != _lastMouseOverElement)
        {
            if (_lastMouseOverElement != null)
            {
                RaiseMouseLeaveChain(_lastMouseOverElement, newMouseOverElement);
            }

            if (newMouseOverElement != null)
            {
                RaiseMouseEnterChain(newMouseOverElement, _lastMouseOverElement);
            }

            _lastMouseOverElement = newMouseOverElement;
        }

        // Raise tunnel event
        MouseEventArgs tunnelArgs = new(
            PreviewMouseMoveEvent, position,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event
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

    private void OnMouseButtonDown(
        MouseButton button,
        nint wParam,
        nint lParam,
        ModifierKeys? platformModifiers = null,
        int clickCount = 1)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = platformModifiers ?? GetModifierKeys();
        int timestamp = Environment.TickCount;

        var hitElement = HitTest(position)?.VisualHit as UIElement;
        UpdateMouseOverState(hitElement);
        var buttons = new MouseButtonStates
        {
            Left = left,
            Middle = middle,
            Right = right,
            XButton1 = xButton1,
            XButton2 = xButton2,
        };
        Mouse.UpdateState(position, hitElement, buttons);
        Mouse.RaiseOutsideCapturedElementEvent(
            true, hitElement, position, button, MouseButtonState.Pressed, clickCount,
            buttons, modifiers, timestamp);
        var target = Mouse.GetMouseTarget(hitElement) ?? (UIElement)this;

        // Raise tunnel event
        MouseButtonEventArgs tunnelArgs = new(
            PreviewMouseDownEvent, position, button, MouseButtonState.Pressed, clickCount,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event
        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                MouseDownEvent, position, button, MouseButtonState.Pressed, clickCount,
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

    private void OnMouseButtonUp(
        MouseButton button,
        nint wParam,
        nint lParam,
        ModifierKeys? platformModifiers = null,
        int clickCount = 1)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = platformModifiers ?? GetModifierKeys();
        int timestamp = Environment.TickCount;

        var hitElement = HitTest(position)?.VisualHit as UIElement;
        UpdateMouseOverState(hitElement);
        var buttons = new MouseButtonStates
        {
            Left = left,
            Middle = middle,
            Right = right,
            XButton1 = xButton1,
            XButton2 = xButton2,
        };
        Mouse.UpdateState(position, hitElement, buttons);
        Mouse.RaiseOutsideCapturedElementEvent(
            false, hitElement, position, button, MouseButtonState.Released, clickCount,
            buttons, modifiers, timestamp);
        var target = Mouse.GetMouseTarget(hitElement) ?? (UIElement)this;

        // Raise tunnel event
        MouseButtonEventArgs tunnelArgs = new(
            PreviewMouseUpEvent, position, button, MouseButtonState.Released, clickCount,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event
        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                MouseUpEvent, position, button, MouseButtonState.Released, clickCount,
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

        _activePointerTargets.Remove(MousePointerId);
    }

    private void UpdateMouseOverState(UIElement? newMouseOverElement)
    {
        if (newMouseOverElement == _lastMouseOverElement)
        {
            return;
        }

        if (_lastMouseOverElement != null)
        {
            RaiseMouseLeaveChain(_lastMouseOverElement, newMouseOverElement);
        }

        if (newMouseOverElement != null)
        {
            RaiseMouseEnterChain(newMouseOverElement, _lastMouseOverElement);
        }

        _lastMouseOverElement = newMouseOverElement;
    }

    private void OnMouseWheel(
        nint wParam,
        nint lParam,
        Point? platformPosition = null,
        ModifierKeys? platformModifiers = null,
        int? platformDelta = null)
    {
        // WM_MOUSEWHEEL lParam contains SCREEN coordinates (physical pixels)
        Point position;
        if (platformPosition is { } suppliedPosition)
        {
            position = suppliedPosition;
        }
        else
        {
            int screenX = (short)(lParam.ToInt64() & 0xFFFF);
            int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            POINT pt = new() { X = screenX, Y = screenY };
            _ = ScreenToClient(_hwnd, ref pt);
            var dpiScale = _parentWindow.DpiScale;
            position = new Point(pt.X / dpiScale, pt.Y / dpiScale);
        }

        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = platformModifiers ?? GetModifierKeys();
        int delta = platformDelta ?? (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        int timestamp = Environment.TickCount;

        var hitElement = HitTest(position)?.VisualHit as UIElement;
        var buttons = new MouseButtonStates
        {
            Left = left,
            Middle = middle,
            Right = right,
            XButton1 = xButton1,
            XButton2 = xButton2,
        };
        Mouse.UpdateState(position, hitElement, buttons);
        var target = Mouse.GetMouseTarget(hitElement) ?? (UIElement)this;

        MouseWheelEventArgs tunnelArgs = new(
            PreviewMouseWheelEvent, position, delta,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

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
        if (!Win32PointerInterop.TryGetPointerData(_hwnd, wParam, _parentWindow.DpiScale, out var pointerData))
            return;

        if (pointerData.Kind == PointerInputKind.Mouse)
            return;

        bool isDown = msg == Win32PointerInterop.WM_POINTERDOWN;
        bool isUp = msg == Win32PointerInterop.WM_POINTERUP;
        DispatchPointerInput(pointerData, isDown, isUp);
    }

    private void OnPlatformPointerEvent(PlatformEvent evt)
    {
        PointerInputKind kind = evt.PointerType switch
        {
            1 => PointerInputKind.Touch,
            2 => PointerInputKind.Pen,
            _ => PointerInputKind.Mouse,
        };
        if (kind == PointerInputKind.Mouse)
            return;

        bool isDown = evt.Type == PlatformEventType.PointerDown;
        bool isUp = evt.Type == PlatformEventType.PointerUp;
        bool isCanceled = evt.Type == PlatformEventType.PointerCancel;
        Point position = PlatformPosition(evt.PointerX, evt.PointerY);
        ModifierKeys modifiers = MapPlatformModifiers(evt.Modifiers);
        bool isTouch = kind == PointerInputKind.Touch;

        if (isTouch && isDown && _primaryPlatformTouchPointerId == null)
            _primaryPlatformTouchPointerId = evt.PointerId;

        float pressure = Math.Clamp(evt.Pressure, 0.0f, 1.0f);
        if (!isUp && !isCanceled && pressure <= 0.0f)
            pressure = kind == PointerInputKind.Touch ? 1.0f : 0.5f;

        PointerPointProperties properties = new()
        {
            IsLeftButtonPressed = !isUp && !isCanceled,
            Pressure = pressure,
            XTilt = evt.TiltX,
            YTilt = evt.TiltY,
            Twist = evt.Twist,
            PointerUpdateKind = isDown
                ? PointerUpdateKind.LeftButtonPressed
                : isUp || isCanceled
                    ? PointerUpdateKind.LeftButtonReleased
                    : PointerUpdateKind.Other,
            IsPrimary = isTouch && _primaryPlatformTouchPointerId == evt.PointerId,
        };
        PointerPoint point = new(
            evt.PointerId,
            position,
            isTouch ? PointerDeviceType.Touch : PointerDeviceType.Pen,
            !isUp && !isCanceled,
            properties,
            (ulong)Environment.TickCount,
            0);
        StylusPointCollection stylusPoints =
            [new StylusPoint(position.X, position.Y, pressure)];
        PointerInputData pointerData = new(
            evt.PointerId,
            kind,
            point,
            position,
            modifiers,
            IsInRange: !isCanceled,
            IsCanceled: isCanceled,
            stylusPoints);

        DispatchPointerInput(pointerData, isDown, isUp);

        if (isTouch && (isUp || isCanceled) &&
            _primaryPlatformTouchPointerId == evt.PointerId)
        {
            _primaryPlatformTouchPointerId = null;
        }
    }

    private void DispatchPointerInput(
        PointerInputData pointerData,
        bool isDown,
        bool isUp)
    {
        int timestamp = Environment.TickCount;

        var captured = UIElement.MouseCapturedElement;
        var hitTarget = HitTest(pointerData.Position)?.VisualHit as UIElement;
        var fallbackTarget = captured ?? hitTarget ?? (UIElement)this;
        var target = isDown
            ? fallbackTarget
            : (_activePointerTargets.TryGetValue(pointerData.PointerId, out var existingTarget) ? existingTarget ?? fallbackTarget : fallbackTarget);

        _activePointerTargets[pointerData.PointerId] = target;
        _lastPointerPoints[pointerData.PointerId] = pointerData.Point;

        bool sourceHandled = false;
        bool sourceCanceled = pointerData.IsCanceled;

        if (pointerData.Kind == PointerInputKind.Touch)
        {
            DispatchTouchSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        }
        else if (pointerData.Kind == PointerInputKind.Pen)
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
        if (!Win32PointerInterop.TryGetPointerData(_hwnd, wParam, _parentWindow.DpiScale, out var pointerData))
            return;

        if (pointerData.Kind == PointerInputKind.Mouse)
            return;

        int timestamp = Environment.TickCount;
        var target = _activePointerTargets.TryGetValue(pointerData.PointerId, out var existingTarget)
            ? existingTarget ?? (UIElement)this
            : (HitTest(pointerData.Position)?.VisualHit as UIElement ?? (UIElement)this);

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
        PointerInputData pointerData,
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
        touchDevice.SetDirectlyOver(target);

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
            touchDevice.DeactivateForManager();
            Touch.UnregisterTouchPoint((int)pointerData.PointerId);
        }
    }

    private void DispatchStylusSourcePipeline(
        UIElement target,
        PointerInputData pointerData,
        bool isDown,
        bool isUp,
        int timestamp,
        ref bool sourceHandled,
        ref bool sourceCanceled)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new StylusDevice((int)pointerData.PointerId);
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
        PointerInputData pointerData,
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
            Mode = ManipulationModes.All
        };
        target.RaiseEvent(previewArgs);
        if (previewArgs.CancelRequested)
            return false;

        if (!previewArgs.Handled)
        {
            ManipulationStartingEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationStartingEvent,
                ManipulationContainer = previewArgs.ManipulationContainer ?? target,
                Mode = previewArgs.Mode,
                Pivot = previewArgs.Pivot,
                IsSingleTouchEnabled = previewArgs.IsSingleTouchEnabled
            };
            target.RaiseEvent(bubbleArgs);
            if (bubbleArgs.CancelRequested)
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
            touchDevice.DeactivateForManager();
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

    private void OnMouseLeave()
    {
        _isMouseTracking = false;
        Mouse.OnMouseLeaveWindow();

        if (_lastMouseOverElement != null)
        {
            RaiseMouseLeaveChain(_lastMouseOverElement, null);
            _lastMouseOverElement = null;
        }
    }

    private void RaiseMouseLeaveChain(UIElement oldElement, UIElement? newElement)
    {
        // Build ancestor set of new element
        HashSet<UIElement> newAncestors = [];
        Visual? current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
                _ = newAncestors.Add(uiElement);
            current = current.VisualParent;
        }

        // Raise MouseLeave for elements no longer under the mouse
        current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (newAncestors.Contains(uiElement))
                    break; // Stop at common ancestor

                uiElement.SetIsMouseOver(false);
                MouseEventArgs args = new(MouseLeaveEvent) { Source = uiElement };
                uiElement.RaiseEvent(args);
            }
            current = current.VisualParent;
        }
    }

    private void RaiseMouseEnterChain(UIElement newElement, UIElement? oldElement)
    {
        // Build ancestor set of old element
        HashSet<UIElement> oldAncestors = [];
        Visual? current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
                _ = oldAncestors.Add(uiElement);
            current = current.VisualParent;
        }

        // Collect elements that need MouseEnter (ancestor to descendant order)
        List<UIElement> enterElements = [];
        current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (oldAncestors.Contains(uiElement))
                    break; // Stop at common ancestor

                enterElements.Add(uiElement);
            }
            current = current.VisualParent;
        }

        // Raise MouseEnter from ancestor to descendant
        for (int i = enterElements.Count - 1; i >= 0; i--)
        {
            var uiElement = enterElements[i];
            uiElement.SetIsMouseOver(true);
            MouseEventArgs args = new(MouseEnterEvent) { Source = uiElement };
            uiElement.RaiseEvent(args);
        }
    }

    private bool OnSetCursor(nint lParam)
    {
        int hitTest = (short)(lParam.ToInt64() & 0xFFFF);
        if (hitTest != HTCLIENT) return false;

        if (Mouse.OverrideCursor is { } overrideCursor)
        {
            var overrideHandle = GetCursorHandle(overrideCursor.CursorType);
            if (overrideHandle != nint.Zero)
            {
                Mouse.SetCursor(overrideCursor);
                _ = SetCursor(overrideHandle);
                return true;
            }
        }

        // Get current cursor position and find element under it
        if (GetCursorPos(out var cursorPt))
        {
            POINT clientPt = new() { X = cursorPt.X, Y = cursorPt.Y };
            _ = ScreenToClient(_hwnd, ref clientPt);
            var dpiScale = _parentWindow.DpiScale;
            Point position = new(clientPt.X / dpiScale, clientPt.Y / dpiScale);

            var hitResult = HitTest(position);
            if (hitResult?.VisualHit is FrameworkElement fe)
            {
                var cursor = FrameworkElement.ResolveEffectiveCursor(fe);
                if (cursor != null)
                {
                    var cursorHandle = GetCursorHandle(cursor.CursorType);
                    if (cursorHandle != nint.Zero)
                    {
                        Mouse.SetCursor(cursor);
                        _ = SetCursor(cursorHandle);
                        return true;
                    }
                }
            }
        }

        // Default arrow
        Mouse.SetCursor(Cursors.Arrow);
        _ = SetCursor(LoadCursor(nint.Zero, IDC_ARROW));
        return true;
    }

    private static nint GetCursorHandle(CursorType cursorType)
    {
        nint cursorId = cursorType switch
        {
            CursorType.Arrow => IDC_ARROW,
            CursorType.IBeam => IDC_IBEAM,
            CursorType.Wait => IDC_WAIT,
            CursorType.Cross => IDC_CROSS,
            CursorType.Hand => IDC_HAND,
            CursorType.SizeNS => IDC_SIZENS,
            CursorType.SizeWE => IDC_SIZEWE,
            CursorType.SizeNWSE => IDC_SIZENWSE,
            CursorType.SizeNESW => IDC_SIZENESW,
            CursorType.SizeAll => IDC_SIZEALL,
            CursorType.No => IDC_NO,
            CursorType.Help => IDC_HELP,
            CursorType.AppStarting => IDC_APPSTARTING,
            CursorType.UpArrow => IDC_UPARROW,
            _ => IDC_ARROW,
        };
        return LoadCursor(nint.Zero, cursorId);
    }

    #endregion

    #region Helpers

    private void UpdateRootBoundsForHitTest()
    {
        var dpiScale = _parentWindow.DpiScale <= 0 ? 1.0 : _parentWindow.DpiScale;
        SetVisualBounds(new Rect(0, 0, _width / dpiScale, _height / dpiScale));
    }

    internal Rect GetBoundsInParentWindowDips()
    {
        var dpiScale = _parentWindow.DpiScale <= 0 ? 1.0 : _parentWindow.DpiScale;

        if (_platformWindow != null)
        {
            // The Linux popup ABI stores x/y in parent-relative physical
            // pixels on both X11 and Wayland.
            return new Rect(
                _screenX / dpiScale,
                _screenY / dpiScale,
                _width / dpiScale,
                _height / dpiScale);
        }

        if (_hwnd == nint.Zero || _parentWindow.Handle == nint.Zero)
        {
            // In tests or before the native popup is shown, fall back to the tracked
            // screen position as if it were already relative to the parent window.
            return new Rect(_screenX / dpiScale, _screenY / dpiScale, _width / dpiScale, _height / dpiScale);
        }

        var parentClientPoint = new POINT
        {
            X = _screenX,
            Y = _screenY
        };
        _ = ScreenToClient(_parentWindow.Handle, ref parentClientPoint);

        return new Rect(
            parentClientPoint.X / dpiScale,
            parentClientPoint.Y / dpiScale,
            _width / dpiScale,
            _height / dpiScale);
    }

    private Point GetMousePosition(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var dpiScale = _parentWindow.DpiScale;
        return new Point(x / dpiScale, y / dpiScale);
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
        if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) modifiers |= ModifierKeys.Shift;
        if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) modifiers |= ModifierKeys.Control;
        if ((GetKeyState(VK_MENU) & 0x8000) != 0) modifiers |= ModifierKeys.Alt;
        return modifiers;
    }

    // Local P/Invoke restored during the Win32 interop split: GetKeyState was
    // previously pulled in via `using static Win32Methods` but that shared
    // declaration was removed when GDI/input imports were partitioned. Matches
    // the per-consumer LibraryImport pattern now used by Window.cs / Keyboard.cs.
    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);

    #endregion

    #region IDisposable

    public void Dispose()
        => DisposeCore(destroyNativeWindow: true, destroyedHwnd: nint.Zero);

    /// <summary>
    /// Completes managed teardown when the HWND is destroyed externally (for
    /// example, when Win32 destroys an owned popup together with its parent).
    /// This path must not call DestroyWindow again from inside WM_DESTROY.
    /// </summary>
    private void OnNativeDestroyed(nint destroyedHwnd)
        => DisposeCore(destroyNativeWindow: false, destroyedHwnd);

    private void DisposeCore(bool destroyNativeWindow, nint destroyedHwnd)
    {
        DispatcherOperation? scheduledRender;
        nint hwndToDestroy;
        lock (_renderLifecycleGate)
        {
            // Ignore a stale WM_DESTROY for a handle that no longer belongs to
            // this instance. HWND values can be reused by the OS.
            if (destroyedHwnd != nint.Zero && _hwnd != destroyedHwnd)
            {
                return;
            }

            if (_disposed)
            {
                return;
            }

            // Invalidate every callback captured by an earlier lifecycle before touching the native
            // surface. Abort removes a still-pending operation from the dispatcher queue; the
            // generation check remains the backstop when it was already dequeued/executing.
            _disposed = true;
            unchecked { _renderLifecycleGeneration++; }
            scheduledRender = _scheduledRenderOperation;
            _scheduledRenderOperation = null;
            Interlocked.Exchange(ref _renderState, 0);

            // Detach the HWND before releasing the lifecycle gate.  Explicit
            // disposal removes the dictionary entry before DestroyWindow, so
            // the synchronous WM_DESTROY cannot recursively enter this object.
            hwndToDestroy = _hwnd;
            _hwnd = nint.Zero;
            if (hwndToDestroy != nint.Zero)
            {
                _ = _popupWindows.Remove(hwndToDestroy);
            }
        }

        _ = scheduledRender?.Abort();

        UpdateRenderableRegistration(visible: false);
        UnsubscribeFrameStarting();
        StopRenderRecoveryRetry();

        // Remove child before destroying window
        Child = null;
        _lastMouseOverElement = null;

        // PopupWindow owns this RTS instance just like Window owns its RTS.
        // Tear its worker down on both explicit close and external WM_DESTROY;
        // Dispose is idempotent, so duplicate native notifications stay safe.
        try { _realTimeStylus.Dispose(); }
        catch { /* never let input-thread teardown escape window cleanup */ }

        lock (_renderLifecycleGate)
        {
            _drawingContext?.ClearBitmapCache();
            _drawingContext = null;

            if (_renderTarget != null)
            {
                _renderTarget.Dispose();
                _renderTarget = null;
            }

        }

        IPlatformWindow? platformWindow = _platformWindow;
        _platformWindow = null;
        if (platformWindow != null)
        {
            platformWindow.SetEventHandler(null);
            platformWindow.Dispose();
        }
        else if (destroyNativeWindow && hwndToDestroy != nint.Zero)
        {
            _ = DestroyWindow(hwndToDestroy);
        }

        GC.SuppressFinalize(this);
    }

    ~PopupWindow()
    {
        Dispose();
    }

    #endregion

    #region Win32 Interop

    private const string PopupWindowClassName = "JaliumPopupWindow";

    // MK_LBUTTON/RBUTTON/MBUTTON and all other WS_/SW_/SWP_/HWND_/WM_/MA_/HTCLIENT/MK_/TME_/
    // VK_/IDC_ constants now come from Win32Constants (issue #151).


    #endregion
}


