using System.Runtime.InteropServices;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Interop;
using Jalium.UI.Interop.Win32;
using Jalium.UI.Media;
using Jalium.UI.Rendering;
using Jalium.UI.Threading;
using Jalium.UI.Controls.DevTools;
using System.Diagnostics;
using RenderTargetDrawingContext = Jalium.UI.Interop.RenderTargetDrawingContext;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a window in the Jalium.UI framework.
/// </summary>
public partial class Window : ContentControl, IWindowHost, ILayoutManagerHost, IInputDispatcherHost, IAdornerLayerHost
{
    private static readonly bool ForceFullReplayForD3D12 = IsEnvironmentSwitchEnabled("JALIUM_D3D12_FORCE_FULL_REPLAY");
    private static readonly bool DebugRender = IsEnvironmentSwitchEnabled("JALIUM_DEBUG_RENDER");

    // RC2 逃生门：JALIUM_DIRTY_PROMOTE_LEGACY=1 恢复重写前的 promote 管线——
    // fold 后面积做判定 + ring 在 fold 时（present 前）推进 + promote 成功前
    // SeedDirtyHistoryFullWindow 全 ring 重播。刻意不加 readonly：环境变量在测试
    // 进程内不可变，测试需要就地开关（bool 写入原子，运行期只有测试改它）。
    internal static bool LegacyPromoteBehavior = IsEnvironmentSwitchEnabled("JALIUM_DIRTY_PROMOTE_LEGACY");

    // VSync 默认是否禁用（env 缓存，运行期不变）。EnsureRenderTarget 与 WM_EXITSIZEMOVE
    // 共用这唯一来源，避免魔法字符串重复，也保证 resize 结束后不会无视用户的 opt-out。
    private static readonly bool VSyncDisabledByEnv = IsEnvironmentSwitchEnabled("JALIUM_DISABLE_VSYNC");

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.WindowAutomationPeer(this);
    }

    /// <summary>
    /// Returns the <see cref="Window"/> that hosts the specified <see cref="DependencyObject"/>,
    /// by walking up the visual tree.
    /// </summary>
    /// <param name="dependencyObject">The element whose host window is to be found.</param>
    /// <returns>The hosting <see cref="Window"/>, or <c>null</c> if not found.</returns>
    public static Window? GetWindow(DependencyObject dependencyObject)
    {
        if (dependencyObject is Window w)
            return w;

        if (dependencyObject is Visual visual)
        {
            Visual? current = visual;
            while (current != null)
            {
                if (current is Window window)
                    return window;
                current = current.VisualParent;
            }
        }
        return null;
    }

    private readonly LayoutManager _layoutManager = new();
    private readonly WindowInputDispatcher _inputDispatcher;
    private double _dpiScale = 1.0;
    private Dispatcher? _dispatcher; // UI thread Dispatcher, captured in Show()

    // Android platform state
    private Thickness _safeAreaInsets;
    private bool _softKeyboardVisible;
    private double _softKeyboardHeight;
    private DeviceOrientation _deviceOrientation;
    // Render state machine — all flags packed into a single int for atomic access.
    // Prevents race conditions where multiple threads check-then-set individual bools.
    private int _renderState; // Bitfield of RenderStateFlags, accessed via Interlocked
    private const int RenderFlag_Scheduled      = 1 << 0; // A Dispatcher-based render is pending
    private const int RenderFlag_Rendering       = 1 << 1; // Inside RenderFrame execution
    private const int RenderFlag_Requested       = 1 << 2; // InvalidateWindow called during rendering
    private const int RenderFlag_DirtyBetween    = 1 << 3; // InvalidateWindow blocked between frames

    // The refresh-rate render cap was removed: rendering now rides the self-driven
    // CompositionTarget frame loop (InvalidateWindow -> RequestFrame), which is
    // already refresh-rate paced, so a hover burst coalesces to one render per tick
    // without a separate cap timer.

    /// <summary>Atomically sets a flag if it was not already set. Returns true if this call set it.</summary>
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

    /// <summary>Atomically sets a flag (unconditionally).</summary>
    private void SetRenderFlag(int flag)
    {
        int prev, next;
        do
        {
            prev = Volatile.Read(ref _renderState);
            next = prev | flag;
        } while (Interlocked.CompareExchange(ref _renderState, next, prev) != prev);
    }

    /// <summary>Atomically clears a flag.</summary>
    private void ClearRenderFlag(int flag)
    {
        int prev, next;
        do
        {
            prev = Volatile.Read(ref _renderState);
            next = prev & ~flag;
        } while (Interlocked.CompareExchange(ref _renderState, next, prev) != prev);
    }

    /// <summary>Reads whether a flag is currently set.</summary>
    private bool HasRenderFlag(int flag) => (Volatile.Read(ref _renderState) & flag) != 0;

    // ── Debug HUD ──
    private readonly RenderDebugHud _debugHud = new();
    private readonly DebugHudOverlay _debugHudOverlay = new();
    private sealed class RenderDebugHud
    {
        // ── Enabled flag (off by default, toggle with F3) ──
        public bool Enabled { get; set; }

        // ── Timing ──
        private readonly System.Diagnostics.Stopwatch _intervalSw = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _frameSw = new();
        private int _frameCount;
        private double _lastFrameMs, _worstFrameMs;
        private double _layoutMs, _renderMs, _presentMs;

        // ── Counters (accumulated per interval) ──
        private int _renderFrameCalls, _paintCalls, _processRenderCalls;
        private int _beginDrawFails, _resizeCount;
        private int _fullFrames, _partialFrames, _skippedFrames;
        private int _promotedFrames;      // Partial dirty region that exceeded the area threshold → promoted to full
        private int _capacityExceeded;    // Aggregator hit its soft capacity and performed a forced merge
        private int _dirtyElementCount;
        private int _dirtyRectCount;      // Rects submitted to the native RT on the most recent partial frame
        private double _dirtyCoverageRatio; // Real covered pixel ratio (0-1) of the most recent partial frame

        // ── Snapshot (displayed values, updated once per second) ──
        private double _dFps, _dWorstMs;
        private double _dLayoutMs, _dRenderMs, _dPresentMs;
        private int _dRenderFrameCalls, _dPaintCalls, _dProcessRenderCalls;
        private int _dBeginDrawFails, _dResizeCount;
        private int _dFullFrames, _dPartialFrames, _dSkippedFrames;
        private int _dPromotedFrames, _dCapacityExceeded;
        private int _dDirtyElements, _dDirtyRectCount;
        private double _dDirtyCoverageRatio;

        // ── State ──
        private string _renderPath = "—";
        private string _backendName = "?";
        private string _engineName = "?";
        private int _windowWidth, _windowHeight;
        private float _dpiScale;
        private Rect _dirtyRegion = Rect.Empty;
        private long _gcTotalBytes;
        private int _gcGen0, _gcGen1, _gcGen2;

        // ── Events ──
        public void OnRenderFrame() { _renderFrameCalls++; _frameSw.Restart(); }
        public void OnPaint() => _paintCalls++;
        public void OnProcessRender() => _processRenderCalls++;
        public void OnBeginFail() => _beginDrawFails++;
        public void OnResize() => _resizeCount++;
        public void OnSkipped() => _skippedFrames++;
        public void OnFull() { _fullFrames++; _renderPath = "Full"; }
        public void OnPartial() { _partialFrames++; _renderPath = "Partial"; }
        public void OnPromoted() { _promotedFrames++; _renderPath = "Promoted→Full"; }
        public void OnCapacityExceeded() => _capacityExceeded++;
        public void MarkLayout() => _layoutMs = _frameSw.Elapsed.TotalMilliseconds;
        public void MarkRender() => _renderMs = _frameSw.Elapsed.TotalMilliseconds;
        public void SetBackend(string b) => _backendName = b;
        public void SetEngine(string e) => _engineName = e;
        public void SetWindowSize(int w, int h) { _windowWidth = w; _windowHeight = h; }
        public void SetDpiScale(float s) => _dpiScale = s;
        public void SetDirtyInfo(int elementCount, Rect region) { _dirtyElementCount = elementCount; _dirtyRegion = region; }
        // 口径注意（排障时勿混读，两值取自不同阶段）：rectCount = fold(dirty-history)
        // 之后实际提交给本次 present 的 rect 数；coverageRatio = promote 判定所用面积
        // 比——默认为本帧 raw（fold 前）真实覆盖面积/窗口面积，JALIUM_DIRTY_PROMOTE_LEGACY=1
        // 或 flush 帧时为 fold/ring 重建后的口径。动画期间 Promote%（可由
        // PartialRenderSnapshot 的 promoted/(full+partial+promoted) 推导）应趋近 0。
        public void SetDirtyRegionStats(int rectCount, double coverageRatio)
        {
            _dirtyRectCount = rectCount;
            _dirtyCoverageRatio = coverageRatio;
        }

        /// <summary>
        /// 区间累计计数的直读版本（非 1s 快照）——测试钩子（HUD 关闭时累计器不会被
        /// FlushInterval 复位，单调可断言）。
        /// </summary>
        public (int full, int partial, int promoted, int skipped) CurrentCounters =>
            (_fullFrames, _partialFrames, _promotedFrames, _skippedFrames);

        public readonly Jalium.UI.Diagnostics.FrameHistory FrameHistory = new();

        public void OnEndDraw()
        {
            _presentMs = _frameSw.Elapsed.TotalMilliseconds;
            _lastFrameMs = _presentMs;
            if (_lastFrameMs > _worstFrameMs) _worstFrameMs = _lastFrameMs;
            _frameCount++;

            double layoutMs = _layoutMs;
            double renderMs = Math.Max(0, _renderMs - _layoutMs);
            double presentMs = Math.Max(0, _presentMs - _renderMs);
            FrameHistory.Push(new Jalium.UI.Diagnostics.FrameHistory.Sample(
                layoutMs, renderMs, presentMs, _presentMs, _dirtyElementCount));
        }

        private void FlushInterval()
        {
            if (_intervalSw.Elapsed.TotalSeconds < 1.0) return;

            _dFps = _frameCount / _intervalSw.Elapsed.TotalSeconds;
            _dWorstMs = _worstFrameMs;
            _dLayoutMs = _layoutMs;
            _dRenderMs = _renderMs;
            _dPresentMs = _presentMs;
            _dRenderFrameCalls = _renderFrameCalls;
            _dPaintCalls = _paintCalls;
            _dProcessRenderCalls = _processRenderCalls;
            _dBeginDrawFails = _beginDrawFails;
            _dResizeCount = _resizeCount;
            _dFullFrames = _fullFrames;
            _dPartialFrames = _partialFrames;
            _dSkippedFrames = _skippedFrames;
            _dPromotedFrames = _promotedFrames;
            _dCapacityExceeded = _capacityExceeded;
            _dDirtyElements = _dirtyElementCount;
            _dDirtyRectCount = _dirtyRectCount;
            _dDirtyCoverageRatio = _dirtyCoverageRatio;

            // Sample GC stats
            _gcTotalBytes = GC.GetTotalMemory(false);
            _gcGen0 = GC.CollectionCount(0);
            _gcGen1 = GC.CollectionCount(1);
            _gcGen2 = GC.CollectionCount(2);

            // Reset accumulators
            _frameCount = 0;
            _renderFrameCalls = 0;
            _paintCalls = 0;
            _processRenderCalls = 0;
            _beginDrawFails = 0;
            _resizeCount = 0;
            _fullFrames = 0;
            _partialFrames = 0;
            _skippedFrames = 0;
            _promotedFrames = 0;
            _capacityExceeded = 0;
            _worstFrameMs = 0;
            _intervalSw.Restart();
        }

        /// <summary>
        /// Readonly snapshot of the partial-render diagnostics.
        /// Exposed so DevTools / custom HUDs can render their own summary.
        /// </summary>
        public (int full, int partial, int promoted, int skipped, int capacityExceeded,
                int dirtyRects, double coverageRatio) PartialRenderSnapshot() =>
            (_dFullFrames, _dPartialFrames, _dPromotedFrames, _dSkippedFrames,
             _dCapacityExceeded, _dDirtyRectCount, _dDirtyCoverageRatio);

        public void UpdateOverlay(DebugHudOverlay overlay)
        {
            if (!Enabled) return;
            FlushInterval();

            double layoutMs = _dLayoutMs;
            double renderMs = Math.Max(0, _dRenderMs - _dLayoutMs);
            double presentMs = Math.Max(0, _dPresentMs - _dRenderMs);
            string dirtyStr = _dirtyRegion.IsEmpty ? "none"
                : $"{_dirtyRegion.X:F0},{_dirtyRegion.Y:F0} {_dirtyRegion.Width:F0}x{_dirtyRegion.Height:F0}";

            overlay.Update(
                _dFps, _dWorstMs,
                layoutMs, renderMs, presentMs, _dPresentMs,
                _renderPath, _backendName, _engineName,
                _dFullFrames, _dPartialFrames, _dSkippedFrames, _dBeginDrawFails,
                _dDirtyElements, dirtyStr,
                _windowWidth, _windowHeight, _dpiScale,
                _gcTotalBytes, _gcGen0, _gcGen1, _gcGen2,
                _dPromotedFrames, _dCapacityExceeded,
                _dDirtyRectCount, _dDirtyCoverageRatio);
        }
    }

    private bool _isFirstLayout = true;
    private bool _renderRecoveryInProgress;
    private DispatcherTimer? _renderRecoveryRetryTimer;
    private int _consecutiveRecoverableRenderFailures;
    private int _renderRecoveryRetryDelayMs = RenderRecoveryRetryInitialDelayMs;
    private RenderBackend _renderBackendOverride = RenderBackend.Auto;
    private bool _fullInvalidation = true;  // First frame is always full
    // FLIP_SEQUENTIAL with N buffers: buffer K's non-dirty area still has content
    // from frame K-N.  We must repaint the union of the last N-1 dirty regions
    // to cover all stale buffers.  With FrameCount=3, track 2 previous regions.
    // Store per-frame region snapshots (array of rects) instead of a single
    // bounding rect so that scattered small dirty patches across frames don't
    // ratchet the Union into a giant bounding box.
    private const int DirtyHistoryCount = 2;
    private readonly Rect[][] _dirtyHistory = new Rect[DirtyHistoryCount][];
    private int _dirtyHistoryIndex;
    // ── FLIP_SEQUENTIAL follow-up flush ──────────────────────────────────
    // A one-shot present (partial OR full) refreshes only the CURRENT swap-chain
    // back buffer; the other buffer(s) keep stale/blank pixels. The dirty-history
    // fold only repairs them on a SUBSEQUENT rendered frame, which the idle-frame-
    // skip (see RenderFrame) suppresses once activity settles — so the last change
    // shows through as intermittent blank nav text / page cards until a resize
    // forces a full present. After a successful non-empty present we arm this
    // counter with (swapBufferCount - 1) follow-up "flush" frames that re-present
    // the dirty-history union so every back buffer converges. Drained one-per-frame
    // in RenderFrame; a flush frame NEVER re-arms it (isFlushFrame guard), so it
    // decreases strictly to 0 — no render loop. UI-thread only (RenderFrame).
    private int _partialPresentsToFlush;
    // Live swap-chain back-buffer count, refreshed from the GPU stats query in
    // CompleteEndDrawOrHandleFailure. Defaults to kDefaultSwapBufferCount (2) so the
    // first arm is correct before any stats arrive; a JALIUM_SWAPCHAIN_BUFFERS=3
    // override is picked up after the first present so the flush still covers every
    // buffer.
    private int _lastSwapBufferCount = 2;
    private long _lastRenderTicks;          // Timestamp of last completed render (for rate-limiting)
    private Timer? _renderThrottleTimer;    // Deferred render when rate-limited or waiting for the GPU
    private long _suppressEscapeUntilTick;

    // ── Frame-latency-waitable scheduling ────────────────────────────────
    // When the underlying render target supports an OS-level frame-latency
    // waitable HANDLE (currently: D3D12 with FRAME_LATENCY_WAITABLE_OBJECT
    // flag), a background thread blocks on it and pokes the dispatcher
    // every time the swap chain releases a back buffer. ScheduleDeferredRender
    // is then a near-no-op — the pacer fires the next render exactly at the
    // moment the GPU pipeline can accept it, so the BeginDraw fast-fail
    // retry loop disappears. Falls back to timer-based scheduling when
    // _frameLatencyWaitable is zero (older platforms / non-D3D12 backends).
    private nint _frameLatencyWaitable;
    private Thread? _framePacerThread;
    private int _framePending; // 0 = idle, 1 = a frame is queued for the next waitable signal
    private const uint FRAME_PACER_TIMEOUT_MS = 1000;
    private const uint WAIT_OBJECT_0_CONST = 0;
    private const uint WAIT_TIMEOUT_CONST = 0x00000102;
    // Per-dirty-element state.  PreLayoutBounds captures where the element WAS
    // when AddDirtyElement was first called in this frame (before UpdateLayout).
    // PreciseLocalRects (optional) lets callers mark only a sub-rectangle of the
    // element dirty — caret blink, focus ring, progress-bar fill — instead of
    // the whole control. Post-layout bounds are computed at render time.
    private sealed class DirtyElementEntry
    {
        public Rect PreLayoutBounds;
        public List<Rect>? PreciseLocalRects;
        // RC4-a：本帧无法 present 该条目（bounds 为空且 layout 仍无效）时保留重试的
        // 次数；上限 3 防恒无效元素自旋（见 RetainOrDropFrameDirty）。
        public byte RetryCount;
        // RC4-c：注册时元素上一次真实被 present 覆盖的屏幕 AABB（UIElement.LastDirtyBounds
        // 快照）。与 PreLayoutBounds 一起提交，封住"先改值后失效 + 长空闲后不连续跳变"
        // 留下的残影（连续动画的上一帧位置本由 dirty-history fold 覆盖）。
        public Rect PrevPaintedBounds;
    }
    // 活动脏集（非 readonly：RenderFrame 以引用互换方式把它换成捕获集，见
    // SwapDirtyForFrame）。后台线程只经 AddDirtyElement/AddDirtyRect 在 _dirtyLock
    // 下触碰当前活动实例。
    private Dictionary<UIElement, DirtyElementEntry> _dirtyElements = new();
    // Free-floating dirty rects in window (screen) coordinates. Populated by
    // AddDirtyRect — used when an animation / compositor system knows a region
    // changed but doesn't own a single UIElement.
    private List<Rect> _dirtyFreeRects = new();
    // ── RC4-a 事务化消费：帧捕获容器 ──
    // RenderFrame 开帧时（锁内）与活动容器引用互换；此后到帧收尾为止由 UI 线程独占
    // （读不加锁），帧内新到的 AddDirtyElement 落入换上来的空活动集，不再被"计算后
    // Clear"竞窗吞掉。收尾三选一：present 成功 → DiscardFrameDirty；空 aggregator
    // 早退 → RetainOrDropFrameDirty；其余一切未 present 的退出（BeginDraw 重试、
    // EndDraw 失败、异常）→ finally 兜底 MergeBackFrameDirty。帧间不变量：两个捕获
    // 容器恒为空。
    private Dictionary<UIElement, DirtyElementEntry> _frameDirtyElements = new();
    private List<Rect> _frameDirtyFreeRects = new();
    // 每次 full-invalidation 请求自增；帧在 swap 时快照，DiscardFrameDirty 仅当序列
    // 未变才清 _fullInvalidation——帧中到达的 RequestFullInvalidation 不会被成功
    // present 的提交顺手吞掉。受 _dirtyLock 保护。
    private long _fullInvalidationSeq;
    private readonly object _dirtyLock = new(); // Protects _dirtyElements from cross-thread access
    private int _appliedDwmTopMarginPhysical = -1;
    private bool _attemptedAutoWindowIcon;
    private bool _contentRendered;
    private bool _isSyncingPosition;
    private Rect _restoreBounds;
    // Pre-fullscreen snapshot: bounds (screen px), WS style, and WindowState to restore.
    private bool _isFullScreen;
    private RECT _fullScreenSavedRect;
    private uint _fullScreenSavedStyle;
    private uint _fullScreenSavedExStyle;
    private WindowState _fullScreenPreviousState;
    private readonly List<Window> _ownedWindows = [];
    private readonly Jalium.UI.WindowCollection _ownedWindowCollection;
    private const double DefaultTitleBarHeightDip = 32.0;
    private const int GpuBusyRetryDelayMs = 1;
    private const int RenderRecoveryRetryInitialDelayMs = 120;
    private const int RenderRecoveryRetryMaxDelayMs = 2000;
    private const int DeviceLostBackendFallbackThreshold = 2;
    private const string D3D12ForceWarpEnvironmentVariable = "JALIUM_D3D12_FORCE_WARP";

    private static bool IsEnvironmentSwitchEnabled(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the layout manager for this window.
    /// </summary>
    LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Window),
            new PropertyMetadata("Window", OnTitleChanged));

    /// <summary>
    /// Identifies the WindowState dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty WindowStateProperty =
        DependencyProperty.Register(nameof(WindowState), typeof(WindowState), typeof(Window),
            new PropertyMetadata(WindowState.Normal, OnWindowStateChanged));

    /// <summary>
    /// Identifies the TitleBarStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TitleBarStyleProperty =
        DependencyProperty.Register(nameof(TitleBarStyle), typeof(WindowTitleBarStyle), typeof(Window),
            new PropertyMetadata(WindowTitleBarStyle.Custom, OnTitleBarStyleChanged));

    /// <summary>
    /// Identifies the <see cref="TitleBarStyleKey"/> dependency property.
    /// </summary>
    /// <remarks>
    /// When set, the value is used as a resource key to look up a <see cref="UI.Style"/>
    /// from the window's resource tree (via <see cref="FrameworkElement.TryFindResource"/>).
    /// The resolved style is applied to the custom <see cref="TitleBar"/>. Has no effect
    /// when <see cref="TitleBarStyle"/> is <see cref="WindowTitleBarStyle.Native"/>.
    /// If <see cref="CustomTitleBarStyle"/> is also set, that wins over the keyed lookup.
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TitleBarStyleKeyProperty =
        DependencyProperty.Register(nameof(TitleBarStyleKey), typeof(object), typeof(Window),
            new PropertyMetadata(null, OnTitleBarStyleResolutionChanged));

    /// <summary>
    /// Identifies the <see cref="CustomTitleBarStyle"/> dependency property.
    /// </summary>
    /// <remarks>
    /// Directly assigns a <see cref="UI.Style"/> to the custom <see cref="TitleBar"/>.
    /// Takes precedence over <see cref="TitleBarStyleKey"/>. Has no effect when
    /// <see cref="TitleBarStyle"/> is <see cref="WindowTitleBarStyle.Native"/>.
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CustomTitleBarStyleProperty =
        DependencyProperty.Register(nameof(CustomTitleBarStyle), typeof(Style), typeof(Window),
            new PropertyMetadata(null, OnTitleBarStyleResolutionChanged));

    /// <summary>
    /// Identifies the SystemBackdrop dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SystemBackdropProperty =
        DependencyProperty.Register(nameof(SystemBackdrop), typeof(WindowBackdropType), typeof(Window),
            new PropertyMetadata(WindowBackdropType.None, OnSystemBackdropChanged));

    /// <summary>
    /// Identifies the Topmost dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty TopmostProperty =
        DependencyProperty.Register(nameof(Topmost), typeof(bool), typeof(Window),
            new PropertyMetadata(false, OnTopmostChanged));

    /// <summary>
    /// Identifies the SizeToContent dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty SizeToContentProperty =
        DependencyProperty.Register(nameof(SizeToContent), typeof(SizeToContent), typeof(Window),
            new PropertyMetadata(SizeToContent.Manual));

    /// <summary>
    /// Identifies the ResizeMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ResizeModeProperty =
        DependencyProperty.Register(nameof(ResizeMode), typeof(ResizeMode), typeof(Window),
            new PropertyMetadata(ResizeMode.CanResize, OnResizeModeChanged));

    /// <summary>
    /// Identifies the WindowStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty WindowStyleProperty =
        DependencyProperty.Register(nameof(WindowStyle), typeof(WindowStyle), typeof(Window),
            new PropertyMetadata(WindowStyle.SingleBorderWindow, OnWindowStyleChanged));

    /// <summary>
    /// Identifies the LeftWindowCommands dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LeftWindowCommandsProperty =
        DependencyProperty.Register(nameof(LeftWindowCommands), typeof(FrameworkElement), typeof(Window),
            new PropertyMetadata(null, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the RightWindowCommands dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RightWindowCommandsProperty =
        DependencyProperty.Register(nameof(RightWindowCommands), typeof(FrameworkElement), typeof(Window),
            new PropertyMetadata(null, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowIcon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowIconProperty =
        DependencyProperty.Register(nameof(IsShowIcon), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowTitle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowTitleProperty =
        DependencyProperty.Register(nameof(IsShowTitle), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowTitleBar dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowTitleBarProperty =
        DependencyProperty.Register(nameof(IsShowTitleBar), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowMinimizeButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowMinimizeButtonProperty =
        DependencyProperty.Register(nameof(IsShowMinimizeButton), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowMaximizeButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowMaximizeButtonProperty =
        DependencyProperty.Register(nameof(IsShowMaximizeButton), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the IsShowCloseButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsShowCloseButtonProperty =
        DependencyProperty.Register(nameof(IsShowCloseButton), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the HasSystemMenu dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty HasSystemMenuProperty =
        DependencyProperty.Register(nameof(HasSystemMenu), typeof(bool), typeof(Window),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the TitleBarFontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TitleBarFontSizeProperty =
        DependencyProperty.Register(nameof(TitleBarFontSize), typeof(double), typeof(Window),
            new PropertyMetadata(14.0, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the TitleBarHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty TitleBarHeightProperty =
        DependencyProperty.Register(nameof(TitleBarHeight), typeof(double), typeof(Window),
            new PropertyMetadata(DefaultTitleBarHeightDip, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// Identifies the WindowIcon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty WindowIconProperty =
        DependencyProperty.Register(nameof(WindowIcon), typeof(ImageSource), typeof(Window),
            new PropertyMetadata(null, OnWindowTitleBarPresentationChanged));

    /// <summary>
    /// WPF-compatible alias for the historical <see cref="WindowIconProperty"/>.
    /// </summary>
    public static readonly DependencyProperty IconProperty = WindowIconProperty;

    /// <summary>
    /// Identifies the Left dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LeftProperty =
        DependencyProperty.Register(nameof(Left), typeof(double), typeof(Window),
            new PropertyMetadata(double.NaN, OnPositionChanged));

    /// <summary>
    /// Identifies the Top dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty TopProperty =
        DependencyProperty.Register(nameof(Top), typeof(double), typeof(Window),
            new PropertyMetadata(double.NaN, OnPositionChanged));

    /// <summary>
    /// Identifies the WindowStartupLocation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WindowStartupLocationProperty =
        DependencyProperty.Register(nameof(WindowStartupLocation), typeof(WindowStartupLocation), typeof(Window),
            new PropertyMetadata(WindowStartupLocation.Manual));

    /// <summary>
    /// Identifies the ShowInTaskbar dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowInTaskbarProperty =
        DependencyProperty.Register(nameof(ShowInTaskbar), typeof(bool), typeof(Window),
            new PropertyMetadata(true, OnShowInTaskbarChanged));

    /// <summary>
    /// Identifies the ShowActivated dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowActivatedProperty =
        DependencyProperty.Register(nameof(ShowActivated), typeof(bool), typeof(Window),
            new PropertyMetadata(true));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Title
    {
        get => (string)(GetValue(TitleProperty) ?? "Window");
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the window state.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public WindowState WindowState
    {
        get => (WindowState)GetValue(WindowStateProperty)!;
        set => SetValue(WindowStateProperty, value);
    }

    /// <summary>
    /// Gets or sets the title bar style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public WindowTitleBarStyle TitleBarStyle
    {
        get => (WindowTitleBarStyle)(GetValue(TitleBarStyleProperty) ?? WindowTitleBarStyle.Custom);
        set => SetValue(TitleBarStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets a resource key used to look up the <see cref="UI.Style"/> applied to
    /// the custom <see cref="TitleBar"/>. The key is resolved against this window's
    /// resource tree; common choices are a string key or <c>typeof(TitleBar)</c>.
    /// Ignored when <see cref="TitleBarStyle"/> is <see cref="WindowTitleBarStyle.Native"/>,
    /// and overridden when <see cref="CustomTitleBarStyle"/> is set.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public object? TitleBarStyleKey
    {
        get => GetValue(TitleBarStyleKeyProperty);
        set => SetValue(TitleBarStyleKeyProperty, value);
    }

    /// <summary>
    /// Gets or sets the <see cref="UI.Style"/> directly applied to the custom
    /// <see cref="TitleBar"/>. Takes precedence over <see cref="TitleBarStyleKey"/>.
    /// Ignored when <see cref="TitleBarStyle"/> is <see cref="WindowTitleBarStyle.Native"/>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? CustomTitleBarStyle
    {
        get => (Style?)GetValue(CustomTitleBarStyleProperty);
        set => SetValue(CustomTitleBarStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the system backdrop type for the window.
    /// This blurs content behind the window (desktop, other applications) using Windows DWM APIs.
    /// Requires Windows 11 22H2+ for Mica and Acrylic effects.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
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
    /// The cross-platform window implementation (null on Windows, which uses
    /// the existing Win32 code path directly).
    /// </summary>
    private IPlatformWindow? _platformWindow;

    internal uint BeginPlatformDrag(ReadOnlySpan<NativeDragDataItem> items, uint allowedEffects) =>
        _platformWindow is NativePlatformWindow native
            ? native.BeginDrag(items, allowedEffects)
            : 0;

    internal void SetPlatformDragEffect(ulong sessionId, uint effect)
    {
        if (_platformWindow is NativePlatformWindow native)
            native.SetDragEffect(sessionId, effect);
    }

    /// <summary>
    /// Gets the render target for this window.
    /// </summary>
    public RenderTarget? RenderTarget { get; private set; }

    /// <summary>
    /// Gets the DPI scale factor for this window (1.0 = 96 DPI = 100%).
    /// </summary>
    public new double DpiScale => _dpiScale;

    /// <summary>
    /// Gets the safe area insets (in DIPs) for notch/cutout/status bar avoidance on mobile.
    /// </summary>
    public Thickness SafeAreaInsets => _safeAreaInsets;

    /// <summary>
    /// Gets whether the soft keyboard is currently visible.
    /// </summary>
    public bool IsSoftKeyboardVisible => _softKeyboardVisible;

    /// <summary>
    /// Gets the soft keyboard height in DIPs (0 when hidden).
    /// </summary>
    public double SoftKeyboardHeight => _softKeyboardVisible ? _softKeyboardHeight : 0;

    /// <summary>
    /// Gets the current device orientation.
    /// </summary>
    public DeviceOrientation DeviceOrientation => _deviceOrientation;

    /// <summary>Raised when safe area insets change.</summary>
    public event EventHandler? SafeAreaInsetsChanged;

    /// <summary>Raised when soft keyboard visibility or height changes.</summary>
    public event EventHandler? SoftKeyboardVisibilityChanged;

    /// <summary>Raised when device orientation changes.</summary>
    public event EventHandler? OrientationChanged;

    /// <summary>
    /// Gets the overlay layer for hosting popup content within the window's visual tree.
    /// </summary>
    internal OverlayLayer OverlayLayer { get; private set; }

    /// <summary>
    /// Gets the adorner layer that hosts keyboard focus indicators and other decorations
    /// targeting elements in this window. Positioned above all content but below popups.
    /// </summary>
    public AdornerLayer? AdornerLayer { get; private set; }

    /// <summary>
    /// Gets or sets the TaskbarItemInfo object that provides taskbar integration features.
    /// </summary>
    public Jalium.UI.Shell.TaskbarItemInfo? TaskbarItemInfo
    {
        get => (Jalium.UI.Shell.TaskbarItemInfo?)GetValue(TaskbarItemInfoProperty);
        set => SetValue(TaskbarItemInfoProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this window appears on top of all other windows.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool Topmost
    {
        get => (bool)GetValue(TopmostProperty)!;
        set => SetValue(TopmostProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether a window automatically sizes itself to fit the size of its content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public SizeToContent SizeToContent
    {
        get => (SizeToContent)GetValue(SizeToContentProperty)!;
        set => SetValue(SizeToContentProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether a window can be resized.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ResizeMode ResizeMode
    {
        get => (ResizeMode)GetValue(ResizeModeProperty)!;
        set => SetValue(ResizeModeProperty, value);
    }

    /// <summary>
    /// Gets or sets a window's border style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public WindowStyle WindowStyle
    {
        get => (WindowStyle)GetValue(WindowStyleProperty)!;
        set => SetValue(WindowStyleProperty, value);
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
    /// Gets or sets whether the title bar icon is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowIcon
    {
        get => (bool)GetValue(IsShowIconProperty)!;
        set => SetValue(IsShowIconProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the title text is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowTitle
    {
        get => (bool)GetValue(IsShowTitleProperty)!;
        set => SetValue(IsShowTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the custom title bar is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowTitleBar
    {
        get => (bool)GetValue(IsShowTitleBarProperty)!;
        set => SetValue(IsShowTitleBarProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the minimize button is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowMinimizeButton
    {
        get => (bool)GetValue(IsShowMinimizeButtonProperty)!;
        set => SetValue(IsShowMinimizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the maximize button is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowMaximizeButton
    {
        get => (bool)GetValue(IsShowMaximizeButtonProperty)!;
        set => SetValue(IsShowMaximizeButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the close button is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsShowCloseButton
    {
        get => (bool)GetValue(IsShowCloseButtonProperty)!;
        set => SetValue(IsShowCloseButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the system menu (right-click menu on title bar) is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool HasSystemMenu
    {
        get => (bool)GetValue(HasSystemMenuProperty)!;
        set => SetValue(HasSystemMenuProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size of the custom title bar text, in DIPs.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double TitleBarFontSize
    {
        get => (double)GetValue(TitleBarFontSizeProperty)!;
        set => SetValue(TitleBarFontSizeProperty, value);
    }

    public double TitleBarHeight
    {
        get => (double)GetValue(TitleBarHeightProperty)!;
        set => SetValue(TitleBarHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon displayed in the custom title bar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public ImageSource? WindowIcon
    {
        get => (ImageSource?)GetValue(WindowIconProperty);
        set => SetValue(WindowIconProperty, value);
    }

    /// <summary>Gets or sets the window icon.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the window's left edge, in DIPs, relative to the desktop.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Left
    {
        get => (double)GetValue(LeftProperty)!;
        set => SetValue(LeftProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the window's top edge, in DIPs, relative to the desktop.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Top
    {
        get => (double)GetValue(TopProperty)!;
        set => SetValue(TopProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the window when first shown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public WindowStartupLocation WindowStartupLocation
    {
        get => (WindowStartupLocation)GetValue(WindowStartupLocationProperty)!;
        set => SetValue(WindowStartupLocationProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the window has a task bar button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool ShowInTaskbar
    {
        get => (bool)GetValue(ShowInTaskbarProperty)!;
        set => SetValue(ShowInTaskbarProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether a window is activated when first shown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool ShowActivated
    {
        get => (bool)GetValue(ShowActivatedProperty)!;
        set => SetValue(ShowActivatedProperty, value);
    }

    /// <summary>
    /// Gets or sets the opacity of the window (0.0 to 1.0).
    /// When <see cref="AllowsTransparency"/> is <c>true</c>, this controls the native window opacity.
    /// </summary>
    public override double Opacity
    {
        get => base.Opacity;
        set
        {
            base.Opacity = value;
            ApplyLayeredWindowOpacity(value);
        }
    }

    /// <summary>
    /// Gets the size and location of a window before being either minimized or maximized.
    /// </summary>
    public Rect RestoreBounds => _restoreBounds;

    /// <summary>
    /// Gets a collection of windows that are owned by this window.
    /// </summary>
    public Jalium.UI.WindowCollection OwnedWindows => _ownedWindowCollection;

    /// <summary>
    /// Gets or sets a value that indicates whether the window allows transparency.
    /// Must be set before the window is shown.
    /// </summary>
    /// <remarks>
    /// 设为 <c>true</c> 时窗口走 DirectComposition 渲染路径（<c>WS_EX_NOREDIRECTIONBITMAP</c>），
    /// 半透明 / 透明的 <see cref="UIElement.Background"/> 会被真实合成到桌面：
    /// <list type="bullet">
    /// <item><see cref="UIElement.Background"/>=半透明色 → 保留 alpha，DWM 直接合成下方桌面 / 应用</item>
    /// <item><see cref="UIElement.Background"/>=null → ClearBackground 走 <c>Clear(0,0,0,0)</c> 透明路径</item>
    /// </list>
    /// 设为 <c>false</c> 时，若 Window 因内嵌 WebView 等被动进入合成路径，框架会把半透明
    /// <see cref="UIElement.Background"/>（A&lt;255 的 SolidColorBrush 或 null）强制改成不透明，
    /// 避免 PREMULTIPLIED swap chain 与桌面合成时的"鬼影"伪影（侧边栏文字重影、桌面内容透过控件可见）。
    /// </remarks>
    public bool AllowsTransparency
    {
        get => (bool)(GetValue(AllowsTransparencyProperty) ?? false);
        set => SetValue(AllowsTransparencyProperty, value);
    }

    /// <summary>
    /// Gets or sets the window that owns this window.
    /// </summary>
    public Window? Owner
    {
        get => _owner;
        set
        {
            if (_owner == value) return;
            _owner?.RemoveOwnedWindow(this);
            _owner = value;
            _owner?.AddOwnedWindow(this);
        }
    }
    private Window? _owner;

    /// <summary>
    /// Gets or sets the dialog result value, which is the return value of the ShowDialog method.
    /// </summary>
    public bool? DialogResult { get; set; }

    #endregion

    #region Events

    public override event SizeChangedEventHandler? SizeChanged;
    public event EventHandler<System.ComponentModel.CancelEventArgs>? Closing;
    public event EventHandler? Closed;
    public event EventHandler? LocationChanged;
    public event EventHandler? Activated;
    public event EventHandler? Deactivated;
    public event EventHandler? StateChanged;
    public event EventHandler? ContentRendered;
    public event EventHandler? SourceInitialized;
    public event DpiChangedEventHandler DpiChanged
    {
        add => AddHandler(DpiChangedEvent, value);
        remove => RemoveHandler(DpiChangedEvent, value);
    }
    public event EventHandler? SystemSettingsChanged;
    public event EventHandler<SessionEndingCancelEventArgs>? SessionEnding;
    public event EventHandler? Shown;
    public event EventHandler? Hiding;

    public bool IsActive => (bool)(GetValue(IsActiveProperty) ?? false);

    #endregion

    #region Event Virtual Methods

    protected virtual void OnActivated(EventArgs e) => Activated?.Invoke(this, e);
    protected virtual void OnDeactivated(EventArgs e) => Deactivated?.Invoke(this, e);
    protected virtual void OnStateChanged(EventArgs e)
    {
        // Pause / resume the global frame timer when this window goes
        // minimized / restored — see UpdateRenderableRegistration.
        UpdateRenderableRegistration();
        StateChanged?.Invoke(this, e);
    }
    protected virtual void OnContentRendered(EventArgs e) => ContentRendered?.Invoke(this, e);
    protected virtual void OnSourceInitialized(EventArgs e) => SourceInitialized?.Invoke(this, e);
    protected virtual void OnLocationChanged(EventArgs e) => LocationChanged?.Invoke(this, e);
    protected virtual void OnClosing(System.ComponentModel.CancelEventArgs e) => Closing?.Invoke(this, e);
    protected virtual void OnClosed(EventArgs e) => Closed?.Invoke(this, e);
    protected virtual void OnDpiChanged(DpiChangedEventArgs e)
    {
        NotifyDpiChangedRecursive(e.OldDpi, e.NewDpi);
        e.RoutedEvent = DpiChangedEvent;
        RaiseEvent(e);
    }
    protected virtual void OnSizeChanged(SizeChangedEventArgs e) => SizeChanged?.Invoke(this, e);
    protected virtual void OnSystemSettingsChanged(EventArgs e) => SystemSettingsChanged?.Invoke(this, e);
    protected virtual void OnSessionEnding(SessionEndingCancelEventArgs e) => SessionEnding?.Invoke(this, e);
    protected virtual void OnShown(EventArgs e) => Shown?.Invoke(this, e);
    protected virtual void OnHiding(EventArgs e) => Hiding?.Invoke(this, e);
    protected virtual bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat) => false;
    protected virtual bool OnPreviewWindowKeyUp(Key key, ModifierKeys modifiers) => false;
    protected virtual bool OnPreviewWindowMouseDown(MouseButton button, Point position, int clickCount) => false;
    protected virtual bool OnPreviewWindowMouseUp(MouseButton button, Point position) => false;
    protected virtual bool OnPreviewWindowMouseMove(Point position) => false;
    protected virtual bool OnPreviewWindowMouseWheel(int delta, Point position) => false;

    #endregion

    #region Base Class Overrides

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) => base.OnPropertyChanged(e);
    internal override Geometry? GetLayoutClip() => null;
    public override string ToString() => $"Window: \"{Title}\" ({Width:F0}x{Height:F0})";

    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        InvalidateMeasure();
        RequestFullInvalidation();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        InvalidateMeasure();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (Handle != nint.Zero)
            EnableWindow(Handle, newValue);
    }

    protected override void OnDataContextChanged(object? oldValue, object? newValue)
    {
        base.OnDataContextChanged(oldValue, newValue);
        if (TitleBar != null)
            TitleBar.DataContext = newValue;
    }

    protected override void OnResourcesChanged()
    {
        base.OnResourcesChanged();
        if (Handle != nint.Zero)
        {
            EnsureImplicitStyles();
            // Re-resolve TitleBarStyleKey against the (potentially) new resource scope
            // so theme swaps pick up updated styles even after the explicit assignment.
            ResolveAndApplyTitleBarStyle();
            InvalidateMeasure();
            RequestFullInvalidation();
        }
    }

    protected override void OnIsMouseOverChanged(bool oldValue, bool newValue)
    {
        base.OnIsMouseOverChanged(oldValue, newValue);
        if (!newValue && TitleBarStyle == WindowTitleBarStyle.Custom)
            ClearTitleBarInteractionState();
    }

    protected override void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
    {
        base.OnIsKeyboardFocusWithinChanged(isFocusWithin);
        RequestFullInvalidation();
    }

    public override Visibility Visibility
    {
        get => base.Visibility;
        set
        {
            base.Visibility = value;
            if (Handle == nint.Zero) return;
            if (_platformWindow != null)
            {
                if (value == Visibility.Visible) _platformWindow.Show();
                else _platformWindow.Hide();
            }
            else
            {
                _ = ShowWindow(Handle, value == Visibility.Visible
                    ? (ShowActivated ? SW_SHOW : SW_SHOWNA)
                    : SW_HIDE);
            }

            // Keep the global frame timer in step with the surface's
            // visibility — see UpdateRenderableRegistration.
            _nativeWindowHidden = value != Visibility.Visible;
            UpdateRenderableRegistration();
        }
    }

    protected override void OnVisualChildrenChanged(Visual? visualAdded, Visual? visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        InvalidateMeasure();
    }

    protected override void OnIsKeyboardFocusedChanged(bool isFocused)
    {
        base.OnIsKeyboardFocusedChanged(isFocused);
        RequestFullInvalidation();
    }

    protected override void OnIsFocusedChanged(bool oldValue, bool newValue)
    {
        base.OnIsFocusedChanged(oldValue, newValue);
        RequestFullInvalidation();
    }

    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
            ClearTitleBarInteractionState();
    }

    protected override void OnBackdropEffectChanged(IBackdropEffect? oldValue, IBackdropEffect? newValue)
    {
        base.OnBackdropEffectChanged(oldValue, newValue);
        RequestFullInvalidation();
    }

    protected override void OnEffectChanged(object? oldValue, object? newValue)
    {
        base.OnEffectChanged(oldValue, newValue);
        RequestFullInvalidation();
    }

    #endregion

    /// <summary>
    /// Gets the title bar control. Only available when TitleBarStyle is Custom.
    /// </summary>
    public TitleBar? TitleBar { get; private set; }

    private const uint MousePointerId = 1;
    private readonly Dictionary<uint, UIElement?> _activePointerTargets = [];
    private readonly Dictionary<uint, PointerPoint> _lastPointerPoints = [];
    private readonly Dictionary<uint, StylusDevice> _activeStylusDevices = [];
    private readonly Dictionary<uint, PointerManipulationSession> _activeManipulationSessions = [];
    private readonly RealTimeStylus _realTimeStylus;

    /// <summary>
    /// External popup windows that are currently open and owned by this window.
    /// Used for light-dismiss coordination.
    /// </summary>
    internal List<Popup> ActiveExternalPopups { get; } = [];

    /// <summary>
    /// Gets or sets the active modal content dialog hosted by this window (Popup mode only).
    /// </summary>
    internal ContentDialog? ActiveContentDialog { get; set; }

    /// <summary>
    /// Tracks in-place content dialogs that are currently open in this window.
    /// Multiple in-place dialogs can coexist because they each occupy their own
    /// position in the visual tree.
    /// </summary>
    internal List<ContentDialog> ActiveInPlaceDialogs { get; } = [];

    public Window()
    {
        _inputDispatcher = new WindowInputDispatcher(this);
        _ownedWindowCollection = new Jalium.UI.WindowCollection(() => _ownedWindows);

        if (PlatformFactory.IsWindows || PlatformFactory.IsLinux)
            DragDropPlatform.EnsureInitialized();

        Width = 800;
        Height = 600;

        // Create adorner layer first so it sits below the popup layer in the visual order.
        // Adorners (including focus visuals) paint above content but must remain below
        // popups like dropdowns and ContextMenus.
        AdornerLayer = new AdornerLayer();
        AddVisualChild(AdornerLayer);

        // Create overlay layer for popup hosting (must be created before title bar)
        OverlayLayer = new OverlayLayer();
        AddVisualChild(OverlayLayer);

        // Debug HUD overlay (F3 to toggle, rendered as a normal control in the overlay layer)
        OverlayLayer.Children.Add(_debugHudOverlay);

        // Ensure keyboard focus visuals materialize as adorners whenever focus moves.
        FocusVisualManager.EnsureInitialized();

        _realTimeStylus = new RealTimeStylus(this);
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnWindowKeyboardFocusChanged), handledEventsToo: true);
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnWindowKeyboardFocusChanged), handledEventsToo: true);

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
        ResolveAndApplyTitleBarStyle();
        ApplyTitleBarPresentation();

        TitleBar.MinimizeClicked += OnTitleBarMinimizeClicked;
        TitleBar.MaximizeRestoreClicked += OnTitleBarMaximizeRestoreClicked;
        TitleBar.CloseClicked += OnTitleBarCloseClicked;

        AddVisualChild(TitleBar);
    }

    private void RemoveTitleBar()
    {
        var titleBar = TitleBar;
        if (titleBar == null)
        {
            return;
        }

        titleBar.MinimizeClicked -= OnTitleBarMinimizeClicked;
        titleBar.MaximizeRestoreClicked -= OnTitleBarMaximizeRestoreClicked;
        titleBar.CloseClicked -= OnTitleBarCloseClicked;

        // Visual.RemoveVisualChild synchronizes DependencyObject's historical public
        // VisualChildrenCount compatibility field from this Window's virtual count.
        // Clear the state first so that synchronization no longer counts the title bar
        // that is being removed; otherwise callers see one phantom child afterwards.
        TitleBar = null;
        RemoveVisualChild(titleBar);
    }

    private void ApplyTitleBarPresentation()
    {
        EnsureAutoWindowIcon();
        
        if (TitleBar == null)
        {
            return;
        }

        TitleBar.Height = GetEffectiveTitleBarHeightDip();
        TitleBar.FontSize = TitleBarFontSize;
        TitleBar.Title = Title;
        TitleBar.IsMaximized = WindowState == WindowState.Maximized;
        // Derive the caption buttons from ResizeMode as well (WPF parity):
        // NoResize hides both minimize and maximize; CanMinimize hides maximize
        // (WPF grays it — this framework's TitleBar has no disabled state, so it is
        // collapsed instead). CanResize/CanResizeWithGrip show both.
        TitleBar.ShowMinimizeButton = IsShowMinimizeButton && ResizeMode != ResizeMode.NoResize;
        TitleBar.ShowMaximizeButton = IsShowMaximizeButton && CanUserResize;
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
        _inputDispatcher.ClearTitleBarInteractionState();
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
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath) || !System.IO.File.Exists(processPath))
            {
                return null;
            }

            var pngBytes = Helpers.IconHelper.ExtractProcessIconAsPng(processPath);
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return null;
            }

            return BitmapImage.FromBytes(pngBytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True when <see cref="ResizeMode"/> permits the user to resize the window
    /// (and therefore to maximize it) — i.e. <see cref="ResizeMode.CanResize"/> or
    /// <see cref="ResizeMode.CanResizeWithGrip"/>. Mirrors WPF, where NoResize and
    /// CanMinimize forbid both edge resize and maximize.
    /// </summary>
    private bool CanUserResize =>
        ResizeMode == ResizeMode.CanResize || ResizeMode == ResizeMode.CanResizeWithGrip;

    private void OnTitleBarMinimizeClicked(object? sender, EventArgs e)
    {
        if (Handle != nint.Zero)
        {
            WindowState = WindowState.Minimized;
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
            WindowState = WindowState.Normal;
        }
        else
        {
            // ResizeMode governs the user-facing maximize affordance (WPF parity):
            // a NoResize / CanMinimize window cannot be maximized from the title bar
            // (button click or caption double-click). Programmatic WindowState changes
            // are unaffected — this only gates the interactive path.
            if (!CanUserResize)
            {
                return;
            }
            WindowState = WindowState.Maximized;
        }
    }

    private void OnTitleBarCloseClicked(object? sender, EventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Shows the system menu (right-click menu) at the specified screen coordinates.
    /// </summary>
    private void ShowSystemMenu(int screenX, int screenY)
    {
        if (!HasSystemMenu || Handle == nint.Zero)
            return;

        nint hMenu = GetSystemMenu(Handle, false);
        if (hMenu == nint.Zero)
            return;

        // Update menu item states to match current window state
        bool isMaximized = WindowState == WindowState.Maximized;
        bool isMinimized = WindowState == WindowState.Minimized;
        bool canResize = ResizeMode == ResizeMode.CanResize || ResizeMode == ResizeMode.CanResizeWithGrip;
        bool canMinimize = ResizeMode != ResizeMode.NoResize;

        EnableMenuItem(hMenu, SC_RESTORE, MF_BYCOMMAND | (isMaximized || isMinimized ? MF_ENABLED : MF_GRAYED));
        EnableMenuItem(hMenu, SC_MOVE, MF_BYCOMMAND | (isMaximized ? MF_GRAYED : MF_ENABLED));
        EnableMenuItem(hMenu, SC_SIZE, MF_BYCOMMAND | (isMaximized || !canResize ? MF_GRAYED : MF_ENABLED));
        EnableMenuItem(hMenu, SC_MINIMIZE, MF_BYCOMMAND | (canMinimize ? MF_ENABLED : MF_GRAYED));
        EnableMenuItem(hMenu, SC_MAXIMIZE, MF_BYCOMMAND | (!isMaximized && canResize ? MF_ENABLED : MF_GRAYED));

        // Set the default menu item (double-click action)
        SetMenuDefaultItem(hMenu, isMaximized ? SC_RESTORE : SC_MAXIMIZE, 0);

        int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_LEFTBUTTON, screenX, screenY, 0, Handle, nint.Zero);
        if (cmd != 0)
        {
            PostMessage(Handle, WM_SYSCOMMAND, (nint)cmd, nint.Zero);
        }
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

    private void OnNcMouseMove(nint wParam, nint lParam)
    {
        _ = wParam;

        if (!IsTitleBarVisible())
        {
            _inputDispatcher.UpdateTitleBarButtonHover(null);
            return;
        }

        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale));
        _inputDispatcher.UpdateTitleBarButtonHover(button);

        // Only request TME_LEAVE so the button hover state can be cleared when
        // the cursor exits the NC area. Do NOT request TME_HOVER here — that
        // would continually reset the DefWindowProc-owned hover timer that
        // Windows 11 uses to arm the Snap Layouts flyout. DefWindowProc will
        // register its own hover tracking via the standard message flow.
        TRACKMOUSEEVENT tme = new()
        {
            cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
            dwFlags = TME_LEAVE | TME_NONCLIENT,
            hwndTrack = Handle,
            dwHoverTime = HOVER_DEFAULT
        };
        _ = TrackMouseEvent(ref tme);
    }

    private void OnNcMouseLeave()
    {
        _inputDispatcher.UpdateTitleBarButtonHover(null);
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

        _inputDispatcher.PressedTitleBarButtonField = button;
        button.SetIsPressed(true);
        return true;
    }

    private bool OnNcLButtonUp(nint wParam, nint lParam)
    {
        if (!IsTitleBarVisible())
            return false;

        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = x, Y = y };
        _ = ScreenToClient(Handle, ref pt);

        int hitTest = (int)wParam.ToInt64();
        var button = GetTitleBarButtonAtPoint(new Point(pt.X / _dpiScale, pt.Y / _dpiScale)) ??
                     GetTitleBarButtonByNcHit(hitTest);

        var pressedButton = _inputDispatcher.PressedTitleBarButtonField;
        if (pressedButton != null)
        {
            pressedButton.SetIsPressed(false);

            bool isReleaseOnPressedButton = button == pressedButton ||
                                            (button == null && IsNcHitMatchingButtonKind(hitTest, pressedButton.Kind));
            if (isReleaseOnPressedButton)
            {
                switch (pressedButton.Kind)
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

            _inputDispatcher.PressedTitleBarButtonField = null;
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

        // Get cursor position (physical pixels) 鈫?client 鈫?DIPs
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
        _inputDispatcher.UpdateTitleBarButtonHover(newHoveredButton);
    }

    #region Visual Children

    /// <inheritdoc />
    protected override int VisualChildrenCount
    {
        get
        {
            int count = base.VisualChildrenCount;
            if (TitleBar != null) count++;
            count++; // AdornerLayer is always present
            count++; // OverlayLayer is always present
            return count;
        }
    }

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        // Order: ContentElement(s) → TitleBar → AdornerLayer → OverlayLayer
        // (last = rendered on top, hit-tested first). Adorners paint above content but
        // below popups so that dropdowns and context menus naturally cover focus rects.
        int baseCount = base.VisualChildrenCount;

        if (index < baseCount)
        {
            return base.GetVisualChild(index);
        }

        int extra = index - baseCount;

        if (TitleBar != null)
        {
            if (extra == 0) return TitleBar;
            if (extra == 1) return AdornerLayer;
            if (extra == 2) return OverlayLayer;
        }
        else
        {
            if (extra == 0) return AdornerLayer;
            if (extra == 1) return OverlayLayer;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double titleBarHeight = 0;

        // Apply safe area insets and soft keyboard on mobile platforms
        double safeLeft = _safeAreaInsets.Left;
        double safeTop = _safeAreaInsets.Top;
        double safeRight = _safeAreaInsets.Right;
        double safeBottom = _safeAreaInsets.Bottom;
        if (_softKeyboardVisible && _softKeyboardHeight > safeBottom)
            safeBottom = _softKeyboardHeight;

        double contentWidth = Math.Max(0, availableSize.Width - safeLeft - safeRight);
        double contentHeight = Math.Max(0, availableSize.Height - safeTop - safeBottom);

        // Measure title bar
        if (IsTitleBarVisible() && TitleBar != null)
        {
            double effectiveTitleBarHeight = GetEffectiveTitleBarHeightDip();
            TitleBar.Measure(new Size(contentWidth, effectiveTitleBarHeight));
            titleBarHeight = GetElementHeightDip(TitleBar, effectiveTitleBarHeight);
        }

        // Measure content with remaining space
        var contentElement = ContentElement;
        if (contentElement != null)
        {
            Size contentAvailable = new(
                contentWidth,
                Math.Max(0, contentHeight - titleBarHeight));
            contentElement.Measure(contentAvailable);
        }

        // Measure adorner and overlay layers with full window size (they don't consume space)
        AdornerLayer?.Measure(availableSize);
        OverlayLayer?.Measure(availableSize);

        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double titleBarHeight = 0;

        // Apply safe area insets and soft keyboard on mobile platforms
        double safeLeft = _safeAreaInsets.Left;
        double safeTop = _safeAreaInsets.Top;
        double safeRight = _safeAreaInsets.Right;
        double safeBottom = _safeAreaInsets.Bottom;
        if (_softKeyboardVisible && _softKeyboardHeight > safeBottom)
            safeBottom = _softKeyboardHeight;

        double contentWidth = Math.Max(0, finalSize.Width - safeLeft - safeRight);
        double contentHeight = Math.Max(0, finalSize.Height - safeTop - safeBottom);

        // Arrange title bar at top (offset by safe area)
        if (IsTitleBarVisible() && TitleBar != null)
        {
            titleBarHeight = GetCurrentTitleBarHeightDip();
            Rect titleBarRect = new(safeLeft, safeTop, contentWidth, titleBarHeight);
            TitleBar.Arrange(titleBarRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Arrange content below title bar (offset by safe area)
        var contentElement = ContentElement;
        if (contentElement is FrameworkElement contentFe)
        {
            Rect contentRect = new(
                safeLeft,
                safeTop + titleBarHeight,
                contentWidth,
                Math.Max(0, contentHeight - titleBarHeight));
            contentFe.Arrange(contentRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Arrange adorner and overlay layers over the full window area. AdornerLayer is
        // forced to re-arrange every Window arrange pass because its adorners track
        // descendants whose positions can change (e.g. scrolling, animations) without
        // altering the AdornerLayer's own final rect. Without this invalidation, the
        // framework's "same rect, already valid → skip" short-circuit would leave focus
        // rings stranded at their old locations.
        AdornerLayer!.InvalidateArrange();
        AdornerLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        OverlayLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));

        // Keep DWM non-client hover tracking region aligned with current title bar/button geometry.
        UpdateCustomTitleBarFrameMargins();

        return finalSize;
    }

    #endregion

    /// <summary>
    /// Shows the window.
    /// </summary>
    public virtual void Show()
    {
        // Ensure implicit styles are applied to the entire visual tree.
        // This handles the case where elements (e.g., TitleBar) were created in the
        // Window constructor BEFORE the theme was loaded by the Xaml module initializer.
        // In non-AOT mode, the theme loads lazily when XamlReader is first accessed
        // (during InitializeComponent), but TitleBar is created earlier in Window().
        EnsureImplicitStyles();

        _dispatcher = Dispatcher.CurrentDispatcher;
        CompositionTarget.FrameStarting += OnFrameStarting;

        // Capture desired state before EnsureHandle, because Win32 calls inside
        // EnsureHandle (SetWindowPos for DPI / frame-change) can trigger WM_SIZE
        // which resets WindowState back to Normal.
        var desiredState = WindowState;

        EnsureHandle();

        // Detect monitor refresh rate and update CompositionTarget for adaptive frame rate
        var refreshRate = DetectMonitorRefreshRate();
        CompositionTarget.UpdateRefreshRate(refreshRate);

        // Apply startup location before showing
        ApplyWindowStartupLocation();

        var showCmd = desiredState switch
        {
            WindowState.Maximized => SW_MAXIMIZE,
            WindowState.Minimized => SW_MINIMIZE,
            WindowState.FullScreen => ShowActivated ? SW_SHOW : SW_SHOWNA,
            _ => ShowActivated ? SW_SHOW : SW_SHOWNA
        };

        // Restore the desired state in case EnsureHandle's WM_SIZE overwrote it.
        if (WindowState != desiredState)
        {
            _isSyncingWindowState = true;
            try { WindowState = desiredState; }
            finally { _isSyncingWindowState = false; }
        }
        // Two-phase startup display:
        //   1) Pre-size the swap chain to the target monitor for Maximized/FullScreen
        //      so we don't pay a second full-render after ShowWindow's WM_SIZE.
        //   2) Present a single ClearBackground frame to the swap chain BEFORE the
        //      HWND becomes visible — the back-buffer is no longer the
        //      uninitialized memory that DWM commonly shows as white during the
        //      window-visible-but-content-not-yet-rendered gap.
        //   3) ShowWindow — DWM picks up the cleared back-buffer immediately, so
        //      the user sees the window appear with its final background color
        //      (no white flash, no delayed window).
        //   4) ForceRenderFrame — the full first frame is rendered synchronously
        //      AFTER the window is on-screen.  The user perceives "window opens
        //      instantly, content paints a moment later" instead of "window
        //      doesn't appear for 400 ms" or "window opens white for 500 ms".
        PrepareInitialRenderTargetSize(desiredState);

        PaintInitialBackgroundFrame();

        if (_platformWindow != null)
        {
            _platformWindow.Show();
            if (desiredState == WindowState.Maximized || desiredState == WindowState.FullScreen)
                _platformWindow.SetState(WindowState.Maximized);
        }
        else
        {
            _ = ShowWindow(Handle, showCmd);
            // Fullscreen needs a second step on Win32: strip the frame + resize
            // to cover the monitor. Done AFTER ShowWindow so the HWND has valid
            // window rect / monitor assignment.  The pre-show background frame
            // above already covered the target monitor dimensions, so
            // EnterFullScreen's WM_SIZE arrives at the correct size.
            if (desiredState == WindowState.FullScreen)
            {
                EnterFullScreen();
            }
        }

        // Now that the surface is on-screen, register with CompositionTarget so
        // its frame timer thread can run. ShowWindow with SW_SHOW after a hidden
        // HWND may not produce a WindowState change (Normal → Normal is a no-op
        // for OnStateChanged), so we cannot rely on the state-changed path here.
        _nativeWindowHidden = false;
        UpdateRenderableRegistration();

        // First full frame — runs AFTER the window is visible so the user
        // perceives an instant "window opens" rather than a delayed launch.
        // The UI thread blocks here for the visual tree's measure/arrange
        // and full render, but the window is already on screen showing the
        // pre-show background frame, so the wait reads as "content appearing"
        // rather than "app stuck loading".
        TryRenderInitialFrame();

        // SWP_FRAMECHANGED for custom title bar was already applied in EnsureHandle
        // (combined with DPI adjustment), so no additional call is needed here.
        // Removing the duplicate saves a DWM roundtrip (~10-50ms).

        SetLoadedState(true);

        if (!_contentRendered)
        {
            _contentRendered = true;
            OnContentRendered(EventArgs.Empty);
        }

        OnShown(EventArgs.Empty);

        if (OperatingSystem.IsLinux())
            Automation.AtSpi.AtSpiAccessibilityBridge.NotifyWindowCreated(this);
    }

    /// <summary>
    /// Aligns the render target with the monitor work / display area before the
    /// pre-show first frame is rendered, so Maximized/FullScreen startup paints
    /// directly at the final dimensions instead of paying a second full-render
    /// after ShowWindow's WM_SIZE arrives.  No-op for Normal/Minimized startup
    /// and for the cross-platform PlatformWindow path.
    /// </summary>
    private void PrepareInitialRenderTargetSize(WindowState desiredState)
    {
        if (_platformWindow != null || Handle == nint.Zero)
        {
            return;
        }

        if (desiredState != WindowState.Maximized && desiredState != WindowState.FullScreen)
        {
            return;
        }

        if (RenderTarget == null || !RenderTarget.IsValid)
        {
            return;
        }

        var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
        if (monitor == nint.Zero)
        {
            return;
        }

        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi))
        {
            return;
        }

        // FullScreen always covers the entire monitor.  A borderless
        // (WindowStyle.None) Maximized window now also covers the entire monitor to
        // match WPF (enforced via the WM_GETMINMAXINFO handler); a bordered
        // Maximized window fills the work area (excludes taskbar).  Pre-sizing to
        // the same rect the maximized HWND will end up at avoids a second full
        // render after ShowWindow's WM_SIZE.  Mirrors the rcWork / rcMonitor split
        // used by EnterFullScreen.
        bool coversFullMonitor = desiredState == WindowState.FullScreen
            || (desiredState == WindowState.Maximized && WindowStyle == WindowStyle.None);
        var rc = coversFullMonitor ? mi.rcMonitor : mi.rcWork;
        int targetPhysicalWidth = rc.right - rc.left;
        int targetPhysicalHeight = rc.bottom - rc.top;
        if (targetPhysicalWidth <= 0 || targetPhysicalHeight <= 0)
        {
            return;
        }

        int currentPhysicalWidth = (int)(Width * _dpiScale);
        int currentPhysicalHeight = (int)(Height * _dpiScale);
        if (currentPhysicalWidth == targetPhysicalWidth && currentPhysicalHeight == targetPhysicalHeight)
        {
            return;
        }

        // Reuse the standard WM_SIZE handler so logical Width/Height, the swap-chain
        // resize, RequestFullInvalidation and InvalidateMeasure all stay in sync —
        // identical to what would happen post-ShowWindow, just performed while the
        // HWND is still hidden.
        OnSizeChanged(targetPhysicalWidth, targetPhysicalHeight);
    }

    /// <summary>
    /// Presents a single ClearBackground frame to the swap chain while the HWND
    /// is still hidden.  This puts a known background color into the back-buffer
    /// before DWM ever shows the window, so the moment ShowWindow makes the
    /// HWND visible the compositor picks up that cleared buffer instead of the
    /// uninitialized memory that drivers commonly display as a white flash.
    /// Cheap (single Clear + Present, ~3-10 ms) compared to the full first
    /// frame, so it does not delay window visibility appreciably.  The full
    /// visual tree render still happens via <see cref="TryRenderInitialFrame"/>
    /// after ShowWindow.
    /// </summary>
    private void PaintInitialBackgroundFrame()
    {
        if (RenderTarget is null || !RenderTarget.IsValid)
        {
            return;
        }

        // EnsureHandle creates the target before Show() reaches this pre-show
        // clear, and target creation may already have started the render worker.
        // Never let this UI-thread Begin/End overlap that worker's replay. Stop
        // and join it for the one startup frame, then restore normal ownership.
        bool restartRenderThread = _renderThread != null;
        if (restartRenderThread && !StopRenderThread())
        {
            return;
        }

        try
        {
            lock (_renderTargetUseGate)
            {
                var renderTarget = RenderTarget;
                if (renderTarget is null || !renderTarget.IsValid || !renderTarget.TryBeginDraw())
                {
                    return;
                }

                try
                {
                    ClearBackground(renderTarget);
                }
                finally
                {
                    _ = renderTarget.TryEndDraw();
                }
            }
        }
        catch (RenderPipelineException)
        {
            // Best-effort — if the platform refuses Present before window is
            // shown (rare on Win32, possible on some cross-platform surfaces),
            // fall through silently.  ShowWindow + the post-show first frame
            // will still paint correctly; user just sees the legacy uninitialized
            // back-buffer for the brief instant before the full first frame.
        }
        finally
        {
            if (restartRenderThread)
            {
                StartRenderThreadIfSupported();
            }
        }
    }

    /// <summary>
    /// Renders the first full visual-tree frame.  Called AFTER ShowWindow so the
    /// window is already on-screen (showing the pre-show ClearBackground frame
    /// painted by <see cref="PaintInitialBackgroundFrame"/>) — the synchronous
    /// layout + render + Present blocks the UI thread, but to the user this
    /// reads as "content appearing" rather than "the app froze on launch".
    /// Wrapped for the cross-platform PlatformWindow path because some
    /// surfaces (e.g. unmapped X11, unattached Android SurfaceView) may have
    /// edge-case Present failures.
    /// </summary>
    private void TryRenderInitialFrame()
    {
        if (_platformWindow != null)
        {
            try
            {
                ForceRenderFrame();
            }
            catch (RenderPipelineException ex)
            {
                Console.Error.WriteLine($"[Show] post-show ForceRenderFrame failed on platform window: {ex.Message}");
            }
        }
        else
        {
            ForceRenderFrame();
        }
    }

    /// <summary>
    /// Hides the window.
    /// </summary>
    public virtual void Hide()
    {
        OnHiding(EventArgs.Empty);

        if (Handle != nint.Zero)
        {
            if (_platformWindow != null)
                _platformWindow.Hide();
            else
                _ = ShowWindow(Handle, SW_HIDE);
        }

        // WPF-style Hide() does not mutate Visibility, so flag the native
        // hidden state directly and let UpdateRenderableRegistration pick it
        // up. Otherwise the timer would keep ticking against an HWND that
        // DWM is no longer painting.
        _nativeWindowHidden = true;
        UpdateRenderableRegistration();
    }

    /// <summary>
    /// Attempts to bring the window to the foreground and activates it.
    /// </summary>
    /// <returns><c>true</c> if the window was successfully activated.</returns>
    public virtual bool Activate()
    {
        if (Handle == nint.Zero)
        {
            return false;
        }

        if (_platformWindow != null)
        {
            if (WindowState == WindowState.Minimized)
                _platformWindow.SetState(WindowState.Normal);
            _platformWindow.Show();
            // Activate() unhides the surface on the cross-platform path —
            // mirror that for the renderable-count check.
            _nativeWindowHidden = false;
            UpdateRenderableRegistration();
            return true;
        }

        // Win32 path
        if (WindowState == WindowState.Minimized)
        {
            _ = ShowWindow(Handle, SW_RESTORE);
        }

        return SetForegroundWindow(Handle);
    }

    /// <summary>
    /// Allows a window to be dragged by a mouse with its left button down over an exposed area of the window's client area.
    /// </summary>
    public virtual void DragMove()
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        // Release mouse capture so the system can take over
        UIElement.ForceReleaseMouseCapture();

        if (_platformWindow != null)
        {
            // Cross-platform: drag not directly supported by native platform lib yet
            // TODO: Implement platform-specific drag move
            return;
        }

        _ = ReleaseCapture();
        // Send WM_NCLBUTTONDOWN with HTCAPTION to initiate a window drag
        _ = SendMessage(Handle, WM_NCLBUTTONDOWN, (nint)HTCAPTION, nint.Zero);
    }

    /// <summary>
    /// Centers the window on the screen of the current monitor.
    /// </summary>
    public void CenterOnScreen()
    {
        if (Handle == nint.Zero || _platformWindow != null) return;
        var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(monitor, ref monitorInfo))
        {
            int screenW = monitorInfo.rcWork.right - monitorInfo.rcWork.left;
            int screenH = monitorInfo.rcWork.bottom - monitorInfo.rcWork.top;
            int winW = (int)(Width * _dpiScale);
            int winH = (int)(Height * _dpiScale);
            int x = monitorInfo.rcWork.left + (screenW - winW) / 2;
            int y = monitorInfo.rcWork.top + (screenH - winH) / 2;
            _ = SetWindowPos(Handle, nint.Zero, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Centers the window relative to its owner window.
    /// </summary>
    public void CenterOnOwner()
    {
        if (Handle == nint.Zero || _platformWindow != null) return;
        nint ownerHwnd = Owner?.Handle ?? nint.Zero;
        if (ownerHwnd == nint.Zero) return;
        if (GetWindowRect(ownerHwnd, out RECT ownerRect))
        {
            int ownerW = ownerRect.right - ownerRect.left;
            int ownerH = ownerRect.bottom - ownerRect.top;
            int winW = (int)(Width * _dpiScale);
            int winH = (int)(Height * _dpiScale);
            int x = ownerRect.left + (ownerW - winW) / 2;
            int y = ownerRect.top + (ownerH - winH) / 2;
            _ = SetWindowPos(Handle, nint.Zero, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    private bool _isModal;

    /// <summary>
    /// Opens a window and returns only when the newly opened window is closed.
    /// </summary>
    public virtual bool? ShowDialog()
    {
        DialogResult = null;

        if (_platformWindow != null)
        {
            // Cross-platform modal dialog: show window and block via Dispatcher
            Show();
            _isModal = true;
            try
            {
                while (_isModal && Handle != nint.Zero)
                {
                    // Poll platform events + process dispatcher queue
                    Interop.NativeMethods.PlatformPollEvents();
                    _dispatcher?.ProcessQueue();
                    Thread.Sleep(1);
                }
            }
            finally
            {
                _isModal = false;
            }
            return DialogResult;
        }

        // Win32 modal dialog path
        nint ownerHandle = DialogOwnerResolver.Resolve(Owner?.Handle ?? nint.Zero);
        if (ownerHandle == Handle)
        {
            ownerHandle = nint.Zero;
        }

        if (ownerHandle != nint.Zero)
        {
            EnableWindow(ownerHandle, false);
        }

        Show();

        _isModal = true;
        try
        {
            // Input-first nested modal pump (see Dispatcher.RunModalLoop): same
            // "run while modal and the window lives" condition as the old bare
            // GetMessage loop, but a posted dispatcher wake no longer outranks
            // hardware input, and WM_QUIT is re-posted so the app's main loop exits.
            var dispatcher = _dispatcher ?? Dispatcher.MainDispatcher ?? Dispatcher.GetForCurrentThread();
            dispatcher.RunModalLoop(() => _isModal && Handle != nint.Zero);
        }
        finally
        {
            _isModal = false;

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
    private readonly object _renderLifecycleGate = new();
    private int _renderLifecycleGeneration;
    private bool _managedTeardownStarted;
    private bool _managedTeardownPending;
    private nint _pendingTeardownNativeHandle;
    private bool _pendingTeardownNativeDestroyed;
    private bool _managedTeardownCompleted;
    private bool _isSyncingWindowState;
    private bool _registeredAsRenderable;
    // Tracks whether the native HWND has been driven to a hidden state
    // (SW_HIDE) outside the Visibility DP path. WPF-style Hide() does not
    // mutate Visibility, so we cannot rely on the DP alone to know whether
    // the surface is presentable.
    private bool _nativeWindowHidden;

    /// <summary>
    /// Synchronises this window's renderability state with
    /// <see cref="CompositionTarget"/>'s visible-window count. The frame timer
    /// thread only runs when at least one window is renderable, so an app
    /// whose only window is minimized (or hidden, or being closed) drops
    /// to ~0% CPU instead of paying the dispatcher cost of frames that no
    /// surface can present.
    /// </summary>
    /// <remarks>
    /// Idempotent — invoked from every transition that can flip visibility:
    /// <c>Show</c>, <c>Hide</c>, the <c>Visibility</c> setter, <c>Close</c>
    /// (and its cancel path), <c>WM_DESTROY</c>, and <c>OnStateChanged</c>.
    /// The internal "did the answer change" check makes redundant calls free.
    /// Must be called on the UI thread because <c>CompositionTarget</c>'s
    /// counter touches non-thread-safe state.
    /// </remarks>
    private void UpdateRenderableRegistration()
    {
        bool shouldBe = Handle != nint.Zero
                        && !_isClosing
                        && !_nativeWindowHidden
                        && WindowState != WindowState.Minimized
                        && base.Visibility == Visibility.Visible;

        if (shouldBe == _registeredAsRenderable) return;

        _registeredAsRenderable = shouldBe;
        if (shouldBe)
            CompositionTarget.NotifyRenderableWindowAdded();
        else
            CompositionTarget.NotifyRenderableWindowRemoved();
    }

    public virtual void Close()
    {
        if (_isClosing || _managedTeardownCompleted) return;
        _isClosing = true;

        // Exit modal loop if ShowDialog is waiting
        _isModal = false;

        CompositionTarget.FrameStarting -= OnFrameStarting;
        // Decrement renderable count now — closing windows cannot present
        // frames even before WM_DESTROY tears the HWND down.
        UpdateRenderableRegistration();

        var closingArgs = new System.ComponentModel.CancelEventArgs();
        OnClosing(closingArgs);
        if (closingArgs.Cancel)
        {
            _isClosing = false;
            CompositionTarget.FrameStarting += OnFrameStarting;
            // Cancelled — restore the renderable registration we just dropped.
            UpdateRenderableRegistration();
            return;
        }

        CompleteManagedTeardown(nativeHandle: Handle, nativeDestroyed: false);
    }

    /// <summary>
    /// Completes the managed half of window destruction. This path is shared by
    /// <see cref="Close"/> and WM_DESTROY so an HWND destroyed outside Close still
    /// releases every renderer-owned resource. When <paramref name="nativeDestroyed"/>
    /// is true, the native handle is already gone and must never be destroyed again.
    /// </summary>
    private void CompleteManagedTeardown(nint nativeHandle, bool nativeDestroyed)
    {
        nint effectiveNativeHandle;
        bool effectiveNativeDestroyed;
        lock (_renderLifecycleGate)
        {
            if (_managedTeardownCompleted)
            {
                return;
            }

            if (!_managedTeardownStarted)
            {
                _managedTeardownStarted = true;
                unchecked { _renderLifecycleGeneration++; }
            }

            _isClosing = true;
            _isModal = false;
            if (nativeHandle != nint.Zero)
            {
                _pendingTeardownNativeHandle = nativeHandle;
            }
            _pendingTeardownNativeDestroyed |= nativeDestroyed;

            // User layout/render callbacks can close the window re-entrantly.
            // Invalidate their lifecycle token now, but defer native resource
            // release until RenderFrame's finally block has ended any draw.
            if (HasRenderFlag(RenderFlag_Rendering))
            {
                _managedTeardownPending = true;
                return;
            }

            _managedTeardownPending = false;
            _managedTeardownCompleted = true;
            effectiveNativeHandle = _pendingTeardownNativeHandle;
            effectiveNativeDestroyed = _pendingTeardownNativeDestroyed;
        }

        CompleteManagedTeardownCore(effectiveNativeHandle, effectiveNativeDestroyed);
    }

    private void CompleteManagedTeardownCore(nint nativeHandle, bool nativeDestroyed)
    {

        CompositionTarget.FrameStarting -= OnFrameStarting;
        UpdateRenderableRegistration();

        StopRenderRecoveryRetry();

        var throttleTimer = Interlocked.Exchange(ref _renderThrottleTimer, null);
        try { throttleTimer?.Dispose(); }
        catch { /* queued callbacks also observe _isClosing */ }
        ClearRenderFlag(RenderFlag_Scheduled | RenderFlag_Requested | RenderFlag_DirtyBetween);

        if (ActiveContentDialog != null)
        {
            ActiveContentDialog.OnHostWindowClosed();
            ActiveContentDialog = null;
        }

        foreach (var inPlaceDialog in ActiveInPlaceDialogs.ToList())
        {
            inPlaceDialog.OnHostWindowClosed();
        }
        ActiveInPlaceDialogs.Clear();

        // Close all external popup windows
        foreach (var popup in ActiveExternalPopups.ToList())
            popup.IsOpen = false;
        ActiveExternalPopups.Clear();

        // Close all owned windows
        foreach (var owned in _ownedWindows.ToList())
            owned.Close();
        _ownedWindows.Clear();

        // Detach from owner
        _owner?.RemoveOwnedWindow(this);
        _owner = null;

        // Stop the render thread FIRST — it owns _drawingContext + RenderTarget and
        // must be joined before either is cleared/disposed below (else use-after-free
        // on the swap chain / drawing context mid-present).
        bool renderThreadStopped = StopRenderThread();
        if (renderThreadStopped)
        {
            ReleaseRenderResourcesAfterRenderThreadStopped();
        }
        else if (_renderThread is { } stalledRenderThread)
        {
            // A present/TDR can exceed the bounded UI-thread join. Keep the
            // swap chain alive and retain the Thread reference until it really
            // exits; freeing it while that thread is native is a use-after-free.
            ScheduleDeferredRenderResourceRelease(stalledRenderThread);
        }

        var managedHandle = Handle;
        Handle = nint.Zero;

        if (managedHandle != nint.Zero)
        {
            _windows.Remove(managedHandle);
        }
        if (nativeHandle != nint.Zero && nativeHandle != managedHandle)
        {
            _windows.Remove(nativeHandle);
        }

        var handleToRelease = nativeHandle != nint.Zero ? nativeHandle : managedHandle;
        if (handleToRelease != nint.Zero)
        {
            if (PlatformFactory.IsWindows)
                OleDropTarget.RevokeWindow(handleToRelease, nativeWindowAlive: !nativeDestroyed);
            else if (PlatformFactory.IsLinux)
                LinuxDropTarget.RevokeWindow(this);
        }

        if (handleToRelease != nint.Zero && !nativeDestroyed)
        {
            if (OperatingSystem.IsLinux())
                Automation.AtSpi.AtSpiAccessibilityBridge.NotifyWindowDestroyed(this);

            if (_platformWindow != null)
            {
                _platformWindow.SetEventHandler(null);
                _platformWindow.Dispose();
                _platformWindow = null;
            }
            else
            {
                _ = DestroyWindow(handleToRelease);
            }

            // Let Application decide whether to shut down based on ShutdownMode
            if (Jalium.UI.Application.Current is { } app)
            {
                app.OnWindowClosed(this, _windows.Count);
            }
            else if (_windows.Count == 0)
            {
                // No Application instance — fall back to quit when no windows remain
                if (PlatformFactory.IsWindows)
                    PostQuitMessage(0);
                else
                    PlatformFactory.QuitMessageLoop(0);
            }
        }

        OnClosed(EventArgs.Empty);
        SetLoadedState(false);

        // Tear down the RTS background thread so it doesn't outlive the window.
        try { _realTimeStylus?.Dispose(); }
        catch { /* never let teardown failures escape Close */ }
    }

    private void CompletePendingManagedTeardown()
    {
        nint nativeHandle;
        bool nativeDestroyed;
        lock (_renderLifecycleGate)
        {
            if (!_managedTeardownPending || _managedTeardownCompleted ||
                HasRenderFlag(RenderFlag_Rendering))
            {
                return;
            }

            nativeHandle = _pendingTeardownNativeHandle;
            nativeDestroyed = _pendingTeardownNativeDestroyed;
        }

        CompleteManagedTeardown(nativeHandle, nativeDestroyed);
    }

    private bool IsRenderLifecycleCurrent(int generation, RenderTarget? target = null)
    {
        if (_managedTeardownStarted || _isClosing ||
            generation != Volatile.Read(ref _renderLifecycleGeneration))
        {
            return false;
        }

        return target == null ||
            (ReferenceEquals(RenderTarget, target) && target.IsValid);
    }

    private bool AbortRenderFrameIfLifecycleChanged(int generation, RenderTarget target)
    {
        if (IsRenderLifecycleCurrent(generation, target))
        {
            return false;
        }

        // Teardown is deferred while RenderFlag_Rendering is held, so this
        // target is still alive. Close its draw session before RenderFrame's
        // finally block releases the lifecycle flag and drains teardown.
        if (target.IsDrawing)
        {
            try { _ = target.TryEndDraw(); }
            catch { }
        }
        return true;
    }

    private void OnNativeDestroyed(nint nativeHandle)
    {
        CompleteManagedTeardown(nativeHandle, nativeDestroyed: true);
    }

    private void ApplyLayeredWindowOpacity(double opacity)
    {
        if (!AllowsTransparency || Handle == nint.Zero)
            return;
        byte alpha = (byte)(Math.Clamp(opacity, 0.0, 1.0) * 255);
        _ = SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
    }

    private void CaptureRestoreBounds()
    {
        if (_platformWindow != null)
        {
            // Cross-platform: capture current size as restore bounds
            _restoreBounds = new Rect(Left, Top, Width, Height);
            return;
        }
        if (Handle != nint.Zero && GetWindowRect(Handle, out RECT rect))
        {
            double dpi = _dpiScale;
            _restoreBounds = new Rect(
                rect.left / dpi,
                rect.top / dpi,
                (rect.right - rect.left) / dpi,
                (rect.bottom - rect.top) / dpi);
        }
    }

    private void AddOwnedWindow(Window child)
    {
        if (!_ownedWindows.Contains(child))
            _ownedWindows.Add(child);
    }

    private void RemoveOwnedWindow(Window child) => _ownedWindows.Remove(child);

    private void ApplyWindowStartupLocation()
    {
        if (Handle == nint.Zero)
        {
            return;
        }

        // Cross-platform: skip Win32 monitor-based positioning
        if (_platformWindow != null)
        {
            // On Linux/Android, startup location is handled by the window manager
            // or defaults to (0,0). No monitor info APIs available.
            return;
        }

        switch (WindowStartupLocation)
        {
            case WindowStartupLocation.CenterScreen:
            {
                var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
                var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    int screenW = monitorInfo.rcWork.right - monitorInfo.rcWork.left;
                    int screenH = monitorInfo.rcWork.bottom - monitorInfo.rcWork.top;
                    int winW = (int)(Width * _dpiScale);
                    int winH = (int)(Height * _dpiScale);
                    int x = monitorInfo.rcWork.left + (screenW - winW) / 2;
                    int y = monitorInfo.rcWork.top + (screenH - winH) / 2;
                    _ = SetWindowPos(Handle, nint.Zero, x, y, 0, 0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                }
                break;
            }
            case WindowStartupLocation.CenterOwner:
            {
                if (Owner?.Handle is nint ownerHwnd and not 0)
                {
                    if (GetWindowRect(ownerHwnd, out RECT ownerRect))
                    {
                        int ownerW = ownerRect.right - ownerRect.left;
                        int ownerH = ownerRect.bottom - ownerRect.top;
                        int winW = (int)(Width * _dpiScale);
                        int winH = (int)(Height * _dpiScale);
                        int x = ownerRect.left + (ownerW - winW) / 2;
                        int y = ownerRect.top + (ownerH - winH) / 2;
                        _ = SetWindowPos(Handle, nint.Zero, x, y, 0, 0,
                            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    }
                }
                break;
            }
            // WindowStartupLocation.Manual: use Left/Top as-is (already applied via OnPositionChanged)
        }
    }

    private void EnsureHandle()
    {
        if (Handle != nint.Zero)
        {
            return;
        }

        if (!PlatformFactory.IsWindows)
        {
            EnsureHandleCrossPlatform();
            return;
        }

        // ---- Windows code path (Win32) ----
        // Register window class if needed
        RegisterWindowClass();

        // Determine window style based on WindowStyle/ResizeMode/TitleBarStyle.
        // For custom title bar we keep standard caption style bits and remove the
        // visible caption via NCCALCSIZE; for WindowStyle=None we use WS_POPUP so
        // the window has no native frame or caption at all.
        uint dwStyle;
        if (WindowStyle == WindowStyle.None)
        {
            dwStyle = ComputeWin32WindowStyle(WindowStyle.None, ResizeMode);
        }
        else
        {
            // Standard overlapped frame. Strip the resize/max/min bits that the
            // current ResizeMode forbids so NoResize / CanMinimize are honored at
            // creation (WPF parity). A bare WS_OVERLAPPEDWINDOW would always leave
            // the window edge-resizable with an enabled maximize box regardless of
            // ResizeMode, because UpdateWindowStyle() does not run on the create path.
            dwStyle = WS_OVERLAPPEDWINDOW;
            if (!CanUserResize)
            {
                dwStyle &= ~(WS_THICKFRAME | WS_MAXIMIZEBOX);
            }
            if (ResizeMode == ResizeMode.NoResize)
            {
                dwStyle &= ~WS_MINIMIZEBOX;
            }
        }

        uint dwExStyle = TitleBarStyle == WindowTitleBarStyle.Custom
            ? WS_EX_APPWINDOW
            : 0;

        if (!ShowInTaskbar)
        {
            dwExStyle |= WS_EX_TOOLWINDOW;
            dwExStyle &= ~WS_EX_APPWINDOW;
        }

        if (AllowsTransparency)
        {
            // Use WS_EX_NOREDIRECTIONBITMAP so the HWND has no redirection
            // surface and the render backend can present through DirectComposition
            // for real per-pixel transparency. WS_EX_LAYERED would only support
            // uniform GDI alpha and does not composite D3D12 swap chains, which
            // made layered fullscreen windows appear click-through.
            dwExStyle |= WS_EX_NOREDIRECTIONBITMAP;
        }

        // Query system DPI for initial window sizing (before HWND exists)
        uint systemDpi = GetDpiForSystem();
        _dpiScale = systemDpi / 96.0;
        FrameworkElement.LayoutDpiScale = _dpiScale;

        // CreateWindowEx takes physical pixel dimensions.
        // Width/Height are in DIPs — scale to physical pixels.
        int physicalWidth = (int)(Width * _dpiScale);
        int physicalHeight = (int)(Height * _dpiScale);

        PrepareTaskbarRelaunchIdentity();

        // Create the window
        // Use Left/Top if set, otherwise default placement
        int x = double.IsNaN(Left) ? CW_USEDEFAULT : (int)(Left * _dpiScale);
        int y = double.IsNaN(Top) ? CW_USEDEFAULT : (int)(Top * _dpiScale);

        Handle = CreateWindowEx(
            dwExStyle,
            WindowClassName,
            Title,
            dwStyle,
            x, y,
            physicalWidth, physicalHeight,
            Owner?.Handle ?? nint.Zero,
            nint.Zero,
            GetModuleHandle(null),
            nint.Zero);

        if (Handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create window.");
        }

        ApplyTaskbarRelaunchProperties();

        // Store reference for message handling
        _windows[Handle] = this;

        // Refine DPI from actual window monitor (may differ from system DPI).
        // For custom title bar, also apply SWP_FRAMECHANGED so NCCALCSIZE semantics
        // are active on the first displayed frame. Combining these into one SetWindowPos
        // call avoids an extra DWM roundtrip (~10-50ms).
        uint windowDpi = GetDpiForWindow(Handle);
        bool needsDpiAdjust = windowDpi != 0 && windowDpi != systemDpi;
        bool isCustomTitleBar = TitleBarStyle == WindowTitleBarStyle.Custom;
        bool isBorderless = WindowStyle == WindowStyle.None;
        // SWP_FRAMECHANGED is required for BOTH custom title bar and WindowStyle=None
        // so WM_NCCALCSIZE runs through our handler *after* _windows[Handle] has been
        // populated — the NCCALCSIZE fired during CreateWindowEx happens before that,
        // so the frame would otherwise stay at its DWM default (visible top strip).
        bool needsFrameChanged = isCustomTitleBar || isBorderless;

        if (isCustomTitleBar)
        {
            EnableRoundedCorners();
        }

        if (needsDpiAdjust || needsFrameChanged)
        {
            if (needsDpiAdjust)
            {
                _dpiScale = windowDpi / 96.0;
                FrameworkElement.LayoutDpiScale = _dpiScale;
                physicalWidth = (int)(Width * _dpiScale);
                physicalHeight = (int)(Height * _dpiScale);
            }

            uint flags = SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE;
            if (needsFrameChanged)
                flags |= SWP_FRAMECHANGED;
            if (!needsDpiAdjust)
                flags |= SWP_NOMOVE | SWP_NOSIZE;

            _ = SetWindowPos(Handle, nint.Zero,
                0, 0,
                needsDpiAdjust ? physicalWidth : 0,
                needsDpiAdjust ? physicalHeight : 0,
                needsDpiAdjust ? (flags | SWP_NOMOVE) : flags);
        }

        // Create render target for this window.
        // During GPU switching this can fail transiently; defer to render-loop recovery.
        try
        {
            EnsureRenderTarget();
        }
        catch (RenderPipelineException ex) when (IsRecoverableRenderPipelineException(ex))
        {
            ScheduleRenderRecoveryRetry();
        }

        // Apply system backdrop after render target is ready
        if (SystemBackdrop != WindowBackdropType.None)
        {
            ApplySystemBackdrop(SystemBackdrop);
        }

        // Register OLE drop target for external drag-and-drop (e.g. files from Explorer)
        OleDropTarget.RegisterWindow(this);

        UpdateInputMethodAssociation();

        OnSourceInitialized(EventArgs.Empty);
    }

    /// <summary>
    /// Cross-platform window creation path (Linux X11 / Android).
    /// Uses the jalium.native.platform library for window management.
    /// </summary>
    private void EnsureHandleCrossPlatform()
    {
        // Initialize platform if needed
        PlatformFactory.InitializePlatform();
        Dispatcher.EnsureNativeWake();

        // Map window style
        uint style = 0;
        if (TitleBarStyle == WindowTitleBarStyle.Custom)
            style |= 0x01; // JALIUM_WINDOW_STYLE_BORDERLESS
        else
            style |= 0x04 | 0x08; // TITLEBAR | CLOSABLE

        if (ResizeMode != ResizeMode.NoResize && ResizeMode != ResizeMode.CanMinimize)
            style |= 0x02; // RESIZABLE

        style |= 0x10 | 0x20; // MINIMIZABLE | MAXIMIZABLE

        if (Topmost)
            style |= 0x40; // TOPMOST

        if (AllowsTransparency)
            style |= 0x100; // TRANSPARENT

        // DPI
        _dpiScale = NativeMethods.PlatformGetSystemDpiScale();
        FrameworkElement.LayoutDpiScale = _dpiScale;

        int physicalWidth = (int)(Width * _dpiScale);
        int physicalHeight = (int)(Height * _dpiScale);
        int x = double.IsNaN(Left) ? -1 : (int)(Left * _dpiScale);
        int y = double.IsNaN(Top) ? -1 : (int)(Top * _dpiScale);

        _platformWindow = PlatformFactory.CreateWindow(
            Title ?? string.Empty, x, y, physicalWidth, physicalHeight,
            style, Owner?.Handle ?? nint.Zero);

        if (_platformWindow == null)
            throw new InvalidOperationException("Failed to create platform window.");

        Handle = _platformWindow.NativeHandle;

        if (Handle == nint.Zero)
            throw new InvalidOperationException("Platform window returned null handle.");

        _windows[Handle] = this;

        // Connect platform event handler for input/resize/paint routing
        _platformWindow.SetEventHandler(OnPlatformEvent);

        // On Android/Linux the native window (OS surface) determines the actual size.
        // Always use the native surface dimensions on these platforms — any default
        // Window Width/Height (e.g. 800×600) is meaningless on a full-screen device.
        // On desktop platforms (macOS/other) only override when dimensions are missing.
        bool alwaysUseNativeSize = PlatformFactory.IsAndroid || PlatformFactory.IsLinux;
        if (alwaysUseNativeSize || physicalWidth <= 0 || physicalHeight <= 0 || double.IsNaN(Width) || double.IsNaN(Height))
        {
            int nativeW = _platformWindow.GetWidth();
            int nativeH = _platformWindow.GetHeight();
            if (nativeW > 0 && nativeH > 0)
            {
                physicalWidth = nativeW;
                physicalHeight = nativeH;
                if (_dpiScale > 0)
                {
                    Width = physicalWidth / _dpiScale;
                    Height = physicalHeight / _dpiScale;
                }
            }
        }

        // Create render target
        try
        {
            EnsureRenderTarget();
            Console.Error.WriteLine($"[EnsureHandleCrossPlatform] RenderTarget={RenderTarget != null} size={physicalWidth}x{physicalHeight}");
        }
        catch (RenderPipelineException ex) when (IsRecoverableRenderPipelineException(ex))
        {
            Console.Error.WriteLine($"[EnsureHandleCrossPlatform] RenderPipelineException: {ex.Message}");
            ScheduleRenderRecoveryRetry();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[EnsureHandleCrossPlatform] InvalidOperationException: {ex.Message}");
            // Rendering backend unavailable (e.g., on first Android launch before surface is ready).
            // Schedule recovery so rendering retries once the surface is established.
            ScheduleRenderRecoveryRetry();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[EnsureHandleCrossPlatform] UNEXPECTED: {ex}");
            ScheduleRenderRecoveryRetry();
        }

        OnSourceInitialized(EventArgs.Empty);
    }

    /// <summary>
    /// Handles platform events from the native platform library (Linux/Android).
    /// Maps cross-platform events to the same internal handlers used by WndProc on Windows.
    /// </summary>
    private void OnPlatformEvent(PlatformEvent evt)
    {
        switch (evt.Type)
        {
            case PlatformEventType.CloseRequested:
                Close();
                break;

            case PlatformEventType.Destroyed:
                if (OperatingSystem.IsLinux())
                    Automation.AtSpi.AtSpiAccessibilityBridge.NotifyWindowDestroyed(this);
                _ = _windows.Remove(Handle);
                // External destruction (system shutdown / native teardown) may
                // skip Close() — make sure we still pull out of the renderable
                // count so the frame timer can wind down.
                _isClosing = true;
                UpdateRenderableRegistration();
                break;

            case PlatformEventType.Resize:
                OnSizeChanged(evt.Width, evt.Height);
                break;

            case PlatformEventType.Paint:
                RenderFrame();
                break;

            case PlatformEventType.FocusGained:
                if (!IsActive)
                {
                    SetIsActive(true);
                    OnActivated(EventArgs.Empty);
                }
                Application.Current?.SetPlatformActivationState(true);
                if (OperatingSystem.IsLinux())
                    Automation.AtSpi.AtSpiAccessibilityBridge.NotifyWindowActivated(this, active: true);
                break;

            case PlatformEventType.FocusLost:
                if (IsActive)
                {
                    SetIsActive(false);
                    OnDeactivated(EventArgs.Empty);
                }
                Application.Current?.SetPlatformActivationState(false);
                if (OperatingSystem.IsLinux())
                    Automation.AtSpi.AtSpiAccessibilityBridge.NotifyWindowActivated(this, active: false);
                _inputDispatcher.ClearMousePressedChain();
                break;

            case PlatformEventType.MouseMove:
            {
                var position = new Point(evt.MouseX / _dpiScale, evt.MouseY / _dpiScale);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                _inputDispatcher.HandleMouseMove(position, MouseButtonStates.AllReleased, modifiers, Environment.TickCount);
                break;
            }

            case PlatformEventType.MouseDown:
            {
                var position = new Point(evt.MouseX / _dpiScale, evt.MouseY / _dpiScale);
                var button = MapPlatformMouseButton(evt.Button);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                var buttons = MouseButtonStates.AllReleased.WithButton(button, MouseButtonState.Pressed);
                _inputDispatcher.HandleMouseDown(button, position, buttons, modifiers, evt.ClickCount, Environment.TickCount);
                break;
            }

            case PlatformEventType.MouseUp:
            {
                var position = new Point(evt.MouseX / _dpiScale, evt.MouseY / _dpiScale);
                var button = MapPlatformMouseButton(evt.Button);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                _inputDispatcher.HandleMouseUp(button, position, MouseButtonStates.AllReleased, modifiers, Environment.TickCount);
                break;
            }

            case PlatformEventType.MouseWheel:
            {
                var position = new Point(evt.MouseX / _dpiScale, evt.MouseY / _dpiScale);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                int delta = (int)(evt.WheelDeltaY * 120); // Match Win32 WHEEL_DELTA
                _inputDispatcher.HandleMouseWheel(position, delta, MouseButtonStates.AllReleased, modifiers, Environment.TickCount);
                break;
            }

            case PlatformEventType.KeyDown:
            {
                Key key = KeyInterop.KeyFromVirtualKey(evt.KeyCode);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                bool isRepeat = evt.IsRepeat != 0;
                _inputDispatcher.HandleKeyDown(key, modifiers, isRepeat, Environment.TickCount);
                break;
            }

            case PlatformEventType.KeyUp:
            {
                Key key = KeyInterop.KeyFromVirtualKey(evt.KeyCode);
                var modifiers = MapPlatformModifiers(evt.Modifiers);
                _inputDispatcher.HandleKeyUp(key, modifiers, Environment.TickCount);
                break;
            }

            case PlatformEventType.CharInput:
            {
                string? text = NativePlatformWindow.CodepointToText(evt.Codepoint);
                if (text != null)
                    _inputDispatcher.HandleCharInput(text, Environment.TickCount);
                break;
            }

            case PlatformEventType.CompositionStart:
                if (!InputMethod.IsComposing)
                    InputMethod.StartComposition();
                break;

            case PlatformEventType.CompositionUpdate:
                if (!InputMethod.IsComposing)
                    InputMethod.StartComposition();
                InputMethod.UpdateComposition(
                    evt.CompositionText ?? string.Empty,
                    Math.Max(0, evt.CompositionCursor));
                break;

            case PlatformEventType.CompositionEnd:
            {
                string result = evt.CompositionText ?? string.Empty;
                InputMethod.EndComposition(result.Length == 0 ? null : result);
                if (result.Length != 0)
                    _inputDispatcher.HandleCharInput(result, Environment.TickCount);
                break;
            }

            case PlatformEventType.DpiChanged:
            {
                _dpiScale = evt.DpiX / 96.0;
                FrameworkElement.LayoutDpiScale = _dpiScale;
                int physicalWidth = (int)(Width * _dpiScale);
                int physicalHeight = (int)(Height * _dpiScale);
                RenderTarget?.SetDpi((float)evt.DpiX, (float)evt.DpiY);
                TryResizeRenderTarget(physicalWidth, physicalHeight, "DpiChanged");
                RequestFullInvalidation();
                InvalidateMeasure();
                break;
            }

            case PlatformEventType.MouseLeave:
                _inputDispatcher.HandleMouseLeave();
                break;

            case PlatformEventType.Move:
            {
                _isSyncingPosition = true;
                try
                {
                    Left = evt.X / _dpiScale;
                    Top = evt.Y / _dpiScale;
                }
                finally
                {
                    _isSyncingPosition = false;
                }
                OnLocationChanged(EventArgs.Empty);
                CompositionTarget.UpdateRefreshRate(DetectMonitorRefreshRate());
                break;
            }

            case PlatformEventType.StateChanged:
            {
                _isSyncingWindowState = true;
                try
                {
                    var newState = evt.NewState switch
                    {
                        1 => WindowState.Minimized,
                        2 => WindowState.Maximized,
                        _ => WindowState.Normal,
                    };
                    if (WindowState != newState)
                    {
                        WindowState = newState;
                    }
                }
                finally
                {
                    _isSyncingWindowState = false;
                }
                break;
            }

            case PlatformEventType.PointerCancel:
            {
                var pointerData = BuildPointerInputData(evt);
                _inputDispatcher.HandlePointerCancel(pointerData, Environment.TickCount);
                break;
            }

            case PlatformEventType.AppPause:
                CompositionTarget.SuspendRendering();
                break;

            case PlatformEventType.AppResume:
                CompositionTarget.ResumeRendering();
                RequestFullInvalidation();
                InvalidateWindow();
                break;

            case PlatformEventType.AppDestroy:
                Application.Current?.Shutdown();
                break;

            case PlatformEventType.LowMemory:
                // FIX: _drawingContext caches are render-thread-owned while _rtActive
                // (PresentCaptureOnRenderThread reads/writes them); draining first
                // avoids a two-thread Dictionary mutation + native bitmap use-after-free.
                // FIX #5 consistency: honor a failed park — skip the trim rather
                // than mutate caches under an in-flight present (same contract as
                // resize/recovery/SIZEMOVE callers).
                if (_rtActive)
                {
                    if (RequestRenderThreadIdle())
                    {
                        _drawingContext?.ClearBitmapCache();
                    }
                    ResumeRenderThread();
                }
                else
                {
                    _drawingContext?.ClearBitmapCache();
                }
                break;

            case PlatformEventType.SafeAreaChanged:
            {
                // Convert physical pixel insets to DIPs
                var insets = new Thickness(
                    evt.SafeAreaLeft / _dpiScale,
                    evt.SafeAreaTop / _dpiScale,
                    evt.SafeAreaRight / _dpiScale,
                    evt.SafeAreaBottom / _dpiScale);
                if (_safeAreaInsets != insets)
                {
                    _safeAreaInsets = insets;
                    SafeAreaInsetsChanged?.Invoke(this, EventArgs.Empty);
                    InvalidateMeasure();
                }
                break;
            }

            case PlatformEventType.KeyboardChanged:
            {
                bool visible = evt.KeyboardVisible != 0;
                double heightDip = evt.KeyboardHeightPx / _dpiScale;
                if (_softKeyboardVisible != visible || _softKeyboardHeight != heightDip)
                {
                    _softKeyboardVisible = visible;
                    _softKeyboardHeight = heightDip;
                    SoftKeyboardVisibilityChanged?.Invoke(this, EventArgs.Empty);
                    InvalidateMeasure();
                }
                break;
            }

            case PlatformEventType.OrientationChanged:
            {
                var newOrientation = (DeviceOrientation)evt.Orientation;
                if (_deviceOrientation != newOrientation)
                {
                    _deviceOrientation = newOrientation;
                    OrientationChanged?.Invoke(this, EventArgs.Empty);
                }
                break;
            }

            case PlatformEventType.DragEnter:
            case PlatformEventType.DragOver:
            case PlatformEventType.DragLeave:
            case PlatformEventType.Drop:
            case PlatformEventType.DragFinished:
                LinuxDropTarget.ProcessEvent(this, evt);
                break;

            case PlatformEventType.Quit:
                Application.Current?.Shutdown();
                break;

            case PlatformEventType.PointerDown:
            case PlatformEventType.PointerUp:
            case PlatformEventType.PointerMove:
            {
                var pointerData = BuildPointerInputData(evt);
                bool isDown = evt.Type == PlatformEventType.PointerDown;
                bool isUp = evt.Type == PlatformEventType.PointerUp;
                _inputDispatcher.HandlePointerInput(pointerData, isDown, isUp, Environment.TickCount);
                break;
            }
        }
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
        var result = ModifierKeys.None;
        if ((modifiers & 0x01) != 0) result |= ModifierKeys.Shift;
        if ((modifiers & 0x02) != 0) result |= ModifierKeys.Control;
        if ((modifiers & 0x04) != 0) result |= ModifierKeys.Alt;
        if ((modifiers & 0x08) != 0) result |= ModifierKeys.Windows;
        return result;
    }

    private PointerInputData BuildPointerInputData(Platform.PlatformEvent evt)
    {
        var position = new Point(evt.PointerX / _dpiScale, evt.PointerY / _dpiScale);
        var modifiers = MapPlatformModifiers(evt.Modifiers);
        bool isDown = evt.Type == Platform.PlatformEventType.PointerDown;
        bool isUp = evt.Type == Platform.PlatformEventType.PointerUp;
        bool isTouch = evt.PointerType == 1;

        var deviceType = isTouch ? PointerDeviceType.Touch
            : evt.PointerType == 0 ? PointerDeviceType.Mouse
            : PointerDeviceType.Pen;
        float pressure = evt.Pressure > 0 ? evt.Pressure : (isDown || !isUp ? 0.5f : 0f);
        var kind = evt.PointerType switch
        {
            0 => PointerInputKind.Mouse,
            1 => PointerInputKind.Touch,
            2 => PointerInputKind.Pen,
            _ => PointerInputKind.Unknown
        };

        var properties = new PointerPointProperties
        {
            IsLeftButtonPressed = isDown || !isUp,
            Pressure = pressure,
            XTilt = evt.TiltX,
            YTilt = evt.TiltY,
            Twist = evt.Twist,
            PointerUpdateKind = isDown ? PointerUpdateKind.LeftButtonPressed
                : isUp ? PointerUpdateKind.LeftButtonReleased
                : PointerUpdateKind.Other,
            IsPrimary = isTouch
        };

        var pointerPoint = new PointerPoint(
            evt.PointerId, position, deviceType,
            isDown || !isUp, properties, (ulong)Environment.TickCount, 0);

        var stylusPoints = new StylusPointCollection(
            [new StylusPoint(position.X, position.Y, pressure)]);

        return new PointerInputData(
            evt.PointerId, kind, pointerPoint, position, modifiers,
            IsInRange: true, IsCanceled: false, stylusPoints);
    }

    /// <summary>
    /// Tracks the first pointer that went down so we can synthesize mouse events
    /// for backward compatibility (controls that only handle Mouse* events).
    /// </summary>
    private uint? _primaryTouchPointerId;

    /// <summary>
    /// Handles PointerDown/Up/Move from the cross-platform path (Android, Linux).
    /// Routes through the full Touch → Stylus → Manipulation → Pointer pipeline,
    /// matching the behavior of <see cref="OnPointerMessage"/> on Win32.
    /// </summary>
    private void OnCrossPlatformPointerEvent(PlatformEvent evt)
    {
        // Mouse pointer type: route through the existing mouse event handlers.
        if (evt.PointerType == 0) // PointerTypeMouse
        {
            var fakeEvt = evt;
            fakeEvt.MouseX = evt.PointerX;
            fakeEvt.MouseY = evt.PointerY;
            fakeEvt.Modifiers = evt.Modifiers;
            switch (evt.Type)
            {
                case PlatformEventType.PointerDown:
                    fakeEvt.Type = PlatformEventType.MouseDown;
                    fakeEvt.Button = 0; // Left
                    fakeEvt.ClickCount = 1;
                    OnPlatformEvent(fakeEvt);
                    break;
                case PlatformEventType.PointerUp:
                    fakeEvt.Type = PlatformEventType.MouseUp;
                    fakeEvt.Button = 0;
                    OnPlatformEvent(fakeEvt);
                    break;
                case PlatformEventType.PointerMove:
                    fakeEvt.Type = PlatformEventType.MouseMove;
                    OnPlatformEvent(fakeEvt);
                    break;
            }
            return;
        }

        // Touch or Pen pointer: full pointer pipeline.
        bool isDown = evt.Type == PlatformEventType.PointerDown;
        bool isUp = evt.Type == PlatformEventType.PointerUp;
        int timestamp = Environment.TickCount;
        var position = new Point(evt.PointerX / _dpiScale, evt.PointerY / _dpiScale);
        var modifiers = MapPlatformModifiers(evt.Modifiers);
        bool isTouch = evt.PointerType == 1; // PointerTypeTouch
        bool isPen = evt.PointerType == 2;   // PointerTypePen

        // Build PointerPoint with correct device type.
        var deviceType = isTouch ? PointerDeviceType.Touch : PointerDeviceType.Pen;
        float pressure = evt.Pressure > 0 ? evt.Pressure : (isDown || !isUp ? 0.5f : 0f);
        bool isPrimary = isTouch && (_primaryTouchPointerId == null || _primaryTouchPointerId == evt.PointerId);

        var properties = new PointerPointProperties
        {
            IsLeftButtonPressed = isDown || !isUp,
            Pressure = pressure,
            XTilt = evt.TiltX,
            YTilt = evt.TiltY,
            Twist = evt.Twist,
            PointerUpdateKind = isDown ? PointerUpdateKind.LeftButtonPressed
                : isUp ? PointerUpdateKind.LeftButtonReleased
                : PointerUpdateKind.Other,
            IsPrimary = isPrimary
        };

        var pointerPoint = new PointerPoint(
            evt.PointerId,
            position,
            deviceType,
            isDown || !isUp, // isInContact
            properties,
            (ulong)timestamp,
            0);

        // Build StylusPointCollection for the stylus pipeline.
        var stylusPoints = new StylusPointCollection(
            new[] { new StylusPoint(position.X, position.Y, pressure) });

        var pointerData = new PointerInputData(
            evt.PointerId,
            isTouch ? PointerInputKind.Touch : PointerInputKind.Pen,
            pointerPoint,
            position,
            modifiers,
            IsInRange: true,
            IsCanceled: false,
            stylusPoints);

        // Track primary touch pointer for mouse synthesis.
        if (isTouch && isDown && _primaryTouchPointerId == null)
            _primaryTouchPointerId = evt.PointerId;

        // Hit test and target resolution.
        var captured = UIElement.MouseCapturedElement;
        var hitTarget = HitTestElement(position, "pointer-route");
        var fallbackTarget = captured ?? hitTarget ?? this;
        var target = isDown
            ? fallbackTarget
            : (_activePointerTargets.TryGetValue(evt.PointerId, out var existingTarget)
                ? existingTarget ?? fallbackTarget : fallbackTarget);

        _activePointerTargets[evt.PointerId] = target;
        _lastPointerPoints[evt.PointerId] = pointerPoint;

        // Dispatch source-level events (Touch or Stylus).
        bool sourceHandled = false;
        bool sourceCanceled = false;

        if (isTouch)
        {
            DispatchTouchSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        }
        else if (isPen)
        {
            DispatchStylusSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        }

        if (sourceCanceled)
        {
            CancelManipulationSession(evt.PointerId, timestamp);
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
            CleanupPointerSession(evt.PointerId);
            if (isTouch && _primaryTouchPointerId == evt.PointerId)
                _primaryTouchPointerId = null;
            return;
        }

        // Manipulation pipeline.
        DispatchManipulationPipeline(target, pointerData, isDown, isUp, sourceHandled, timestamp);

        // Pointer events.
        if (!sourceHandled)
        {
            if (isDown)
                RaisePointerDownPipeline(target, pointerPoint, modifiers, timestamp);
            else if (isUp)
                RaisePointerUpPipeline(target, pointerPoint, modifiers, timestamp);
            else
                RaisePointerMovePipeline(target, pointerPoint, modifiers, timestamp);
        }

        // Synthesize mouse events for the primary touch pointer so that
        // controls handling only Mouse* events (Button, ScrollViewer, etc.) work.
        if (isTouch && _primaryTouchPointerId == evt.PointerId && !sourceHandled)
        {
            SynthesizeMouseFromTouch(evt, position, modifiers, isDown, isUp, hitTarget, timestamp);
        }

        if (isUp)
        {
            CleanupPointerSession(evt.PointerId);
            if (isTouch && _primaryTouchPointerId == evt.PointerId)
                _primaryTouchPointerId = null;
        }
    }

    /// <summary>
    /// Handles PointerCancel from the cross-platform path.
    /// Cancels any active touch/manipulation session and raises pointer cancel events.
    /// </summary>
    private void OnCrossPlatformPointerCancel(PlatformEvent evt)
    {
        int timestamp = Environment.TickCount;
        var position = new Point(evt.PointerX / _dpiScale, evt.PointerY / _dpiScale);
        var modifiers = MapPlatformModifiers(evt.Modifiers);

        if (_activePointerTargets.TryGetValue(evt.PointerId, out var target) && target != null)
        {
            if (!_lastPointerPoints.TryGetValue(evt.PointerId, out var point))
            {
                var deviceType = evt.PointerType == 1 ? PointerDeviceType.Touch : PointerDeviceType.Pen;
                point = new PointerPoint(
                    evt.PointerId, position, deviceType, false,
                    new PointerPointProperties(), (ulong)timestamp);
            }

            // Deactivate touch device if this was a touch pointer.
            if (evt.PointerType == 1) // Touch
            {
                var touchDevice = Touch.GetDevice((int)evt.PointerId);
                if (touchDevice != null)
                {
                    touchDevice.DeactivateForManager();
                    Touch.UnregisterTouchPoint((int)evt.PointerId);
                }
                _activeStylusDevices.Remove(evt.PointerId);
            }

            CancelManipulationSession(evt.PointerId, timestamp);
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
        }

        CleanupPointerSession(evt.PointerId);

        if (_primaryTouchPointerId == evt.PointerId)
        {
            _primaryTouchPointerId = null;
            // Synthesize mouse leave so controls reset hover state.
            _inputDispatcher.HandleMouseLeave();
        }
    }

    /// <summary>
    /// Synthesizes mouse events from the primary touch pointer so that controls
    /// that only handle Mouse* events continue to work on touch platforms.
    /// </summary>
    private void SynthesizeMouseFromTouch(
        PlatformEvent evt, Point position, ModifierKeys modifiers,
        bool isDown, bool isUp, UIElement? hitTarget, int timestamp)
    {
        var buttons = new MouseButtonStates
        {
            Left = isUp ? MouseButtonState.Released : MouseButtonState.Pressed
        };

        // Suppress mouse→pointer promotion: pointer events were already dispatched
        // directly from the touch pipeline with the correct PointerDeviceType.Touch.
        _inputDispatcher.SuppressMouseToPointerPromotion = true;
        try
        {
            if (isDown)
            {
                _inputDispatcher.HandleMouseDown(
                    MouseButton.Left, position, buttons, modifiers, clickCount: 1, timestamp);
            }
            else if (isUp)
            {
                _inputDispatcher.HandleMouseUp(MouseButton.Left, position, buttons, modifiers, timestamp);
            }
            else
            {
                _inputDispatcher.HandleMouseMove(position, buttons, modifiers, timestamp);
            }
        }
        finally
        {
            _inputDispatcher.SuppressMouseToPointerPromotion = false;
        }
    }

    private void EnableRoundedCorners()
    {
        if (Handle == nint.Zero || !PlatformFactory.IsWindows)
        {
            return;
        }

        // DWMWA_WINDOW_CORNER_PREFERENCE = 33
        // DWMWCP_ROUND = 2 (rounded corners)
        int cornerPreference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        // Enable dark mode so DWM-owned UI (title bar, system menu, scrollbars) uses dark theme.
        int useDarkMode = 1;
        _ = DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

        // Enable dark mode for popup menus (TrackPopupMenu) via undocumented uxtheme APIs.
        _ = SetPreferredAppMode(PreferredAppMode.ForceDark);
        _ = AllowDarkModeForWindow(Handle, true);
        FlushMenuThemes();

        // Set DWM caption/border color to dark to prevent white flash during resize.
        // DWMWA_CAPTION_COLOR = 35, DWMWA_BORDER_COLOR = 34
        // COLORREF format: 0x00BBGGRR
        int darkColor = 0x00282828; // #282828 in BGR
        _ = DwmSetWindowAttribute(Handle, 35, ref darkColor, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 34, ref darkColor, sizeof(int));

        // Extend frame into client area covering the effective title bar/button hit-test height.
        UpdateCustomTitleBarFrameMargins();
    }

    private void UpdateCustomTitleBarFrameMargins()
    {
        if (!PlatformFactory.IsWindows || Handle == nint.Zero || TitleBarStyle != WindowTitleBarStyle.Custom)
        {
            return;
        }

        int topMarginPhysical = GetCustomTitleBarTopMarginPhysical();
        if (_appliedDwmTopMarginPhysical == topMarginPhysical)
        {
            return;
        }

        MARGINS margins = new() { Left = 0, Right = 0, Top = topMarginPhysical, Bottom = 0 };
        _ = DwmExtendFrameIntoClientArea(Handle, ref margins);
        _appliedDwmTopMarginPhysical = topMarginPhysical;
    }

    // DWM uses the Top margin to decide where the caption frame starts and
    // whether to arm the Windows 11 Snap Layouts flyout over the maximize
    // button. If we pass 0/1 here the flyout stops appearing entirely; if
    // we pass the full custom title-bar height DWM still anchors the caption
    // button rect to the system height at the top of that region. So mirror
    // the title-bar height (or at least enough to cover it) to keep DWM's
    // caption semantics aligned with our custom chrome.
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
        if (Handle == nint.Zero || !PlatformFactory.IsWindows)
        {
            return; // System backdrops (Mica/Acrylic) only available on Windows
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
        // with title-bar-height margins. We intentionally avoid {-1,-1,-1,-1} here
        // because that makes DWM draw its own caption visuals over a custom title bar.

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
        if (Handle == nint.Zero || _platformWindow != null)
        {
            return; // Win32 window styles not applicable on cross-platform
        }

        // While in fullscreen we don't want to re-apply the user's chosen frame
        // styles; they are re-applied on ExitFullScreen from the saved snapshot.
        if (_isFullScreen)
        {
            return;
        }

        long style = GetWindowLong(Handle, GWL_STYLE);
        long exStyle = GetWindowLong(Handle, GWL_EXSTYLE);

        // Always clear the bits we manage so transitions between WindowStyle
        // values drop the previously-applied frame cleanly.
        const uint FrameMask = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
        style &= ~(long)FrameMask;

        if (WindowStyle == WindowStyle.None)
        {
            // Borderless popup. Do NOT set WS_CAPTION — the OS must not draw
            // a caption bar. Edge resize relies on WS_THICKFRAME when allowed.
            if (ResizeMode == ResizeMode.CanResize || ResizeMode == ResizeMode.CanResizeWithGrip)
            {
                style |= WS_THICKFRAME;
            }
        }
        else if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            // Keep WS_CAPTION so the OS preserves native caption semantics
            // (Snap, system menu, NC button behavior). We remove the visual
            // caption in WM_NCCALCSIZE instead.
            style |= WS_CAPTION | WS_SYSMENU;
            if (ResizeMode != ResizeMode.NoResize)
            {
                style |= WS_MINIMIZEBOX;
            }
            if (CanUserResize)
            {
                // Only a resizable window gets the sizing border + maximize box.
                // CanMinimize keeps just WS_MINIMIZEBOX (the OS grays the max box);
                // it must NOT get WS_THICKFRAME or WS_MAXIMIZEBOX.
                style |= WS_THICKFRAME | WS_MAXIMIZEBOX;
            }
            // Respect ShowInTaskbar here too: unconditionally forcing WS_EX_APPWINDOW
            // would drag a no-taskbar window back into the taskbar whenever this runs
            // (e.g. on a live ResizeMode / WindowStyle change).
            if (ShowInTaskbar)
            {
                exStyle |= WS_EX_APPWINDOW;
                exStyle &= ~(long)WS_EX_TOOLWINDOW;
            }
            else
            {
                exStyle |= WS_EX_TOOLWINDOW;
                exStyle &= ~(long)WS_EX_APPWINDOW;
            }
            EnableRoundedCorners();
        }
        else
        {
            style |= WS_CAPTION | WS_SYSMENU;
            if (ResizeMode != ResizeMode.NoResize)
            {
                style |= WS_MINIMIZEBOX;
            }
            if (CanUserResize)
            {
                style |= WS_THICKFRAME | WS_MAXIMIZEBOX;
            }
        }

        _ = SetWindowLong(Handle, GWL_STYLE, style);
        _ = SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

        // Force window to redraw with new style
        _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Enters fullscreen mode: saves current bounds/style, removes the frame,
    /// and resizes the window to cover the containing monitor (including taskbar).
    /// </summary>
    private void EnterFullScreen()
    {
        if (_isFullScreen || Handle == nint.Zero || _platformWindow != null)
        {
            return;
        }

        // Snapshot current style + bounds so we can restore on exit.
        _fullScreenSavedStyle = (uint)GetWindowLong(Handle, GWL_STYLE);
        _fullScreenSavedExStyle = (uint)GetWindowLong(Handle, GWL_EXSTYLE);
        if (!GetWindowRect(Handle, out _fullScreenSavedRect))
        {
            return;
        }

        var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi))
        {
            return;
        }

        _isFullScreen = true;

        // Strip frame bits — leave WS_VISIBLE / WS_CLIPSIBLINGS / WS_CLIPCHILDREN untouched.
        const uint FrameMask = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_OVERLAPPEDWINDOW;
        long newStyle = (long)_fullScreenSavedStyle & ~(long)FrameMask;
        newStyle |= (long)WS_POPUP;
        _ = SetWindowLong(Handle, GWL_STYLE, newStyle);

        var rc = mi.rcMonitor;
        _ = SetWindowPos(
            Handle,
            HWND_TOP,
            rc.left,
            rc.top,
            rc.right - rc.left,
            rc.bottom - rc.top,
            SWP_FRAMECHANGED | SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Exits fullscreen mode, restoring the style and bounds captured by
    /// <see cref="EnterFullScreen"/>.
    /// </summary>
    private void ExitFullScreen()
    {
        if (!_isFullScreen || Handle == nint.Zero || _platformWindow != null)
        {
            return;
        }

        _isFullScreen = false;

        _ = SetWindowLong(Handle, GWL_STYLE, _fullScreenSavedStyle);
        _ = SetWindowLong(Handle, GWL_EXSTYLE, _fullScreenSavedExStyle);

        var rc = _fullScreenSavedRect;
        _ = SetWindowPos(
            Handle,
            nint.Zero,
            rc.left,
            rc.top,
            rc.right - rc.left,
            rc.bottom - rc.top,
            SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOOWNERZORDER);
    }

    private static uint GetWindowStyleForTitleBarStyle(WindowTitleBarStyle titleBarStyle)
    {
        // Legacy helper retained for compatibility. Defers to the richer computation
        // that considers WindowStyle/ResizeMode below.
        return ComputeWin32WindowStyle(WindowStyle.SingleBorderWindow, ResizeMode.CanResize);
    }

    /// <summary>
    /// Computes the Win32 WS_* style bits corresponding to the given
    /// <paramref name="windowStyle"/> and <paramref name="resizeMode"/>.
    /// For <see cref="WindowStyle.None"/> the result is a borderless popup
    /// (no caption, no system menu, no min/max buttons). When resizing is
    /// permitted, <c>WS_THICKFRAME</c> is included so the window can be
    /// resized via its edges.
    /// </summary>
    private static uint ComputeWin32WindowStyle(WindowStyle windowStyle, ResizeMode resizeMode)
    {
        if (windowStyle == WindowStyle.None)
        {
            uint style = WS_POPUP;
            if (resizeMode == ResizeMode.CanResize || resizeMode == ResizeMode.CanResizeWithGrip)
            {
                // WS_THICKFRAME lets the OS handle edge resize + Aero Snap.
                style |= WS_THICKFRAME;
            }
            return style;
        }

        // Borders + caption + sys menu + optional min/max buttons.
        uint baseStyle = WS_POPUP | WS_CAPTION | WS_SYSMENU;
        switch (resizeMode)
        {
            case ResizeMode.NoResize:
                break;
            case ResizeMode.CanMinimize:
                baseStyle |= WS_MINIMIZEBOX;
                break;
            case ResizeMode.CanResize:
            case ResizeMode.CanResizeWithGrip:
            default:
                baseStyle |= WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
                break;
        }
        return baseStyle;
    }

    private bool ShouldUseCompositionRenderTarget()
    {
        if (Handle == nint.Zero)
        {
            return false;
        }

        long exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
        return (exStyle & WS_EX_NOREDIRECTIONBITMAP) != 0;
    }

    private static bool IsBackendAvailable(RenderBackend backend)
        => backend != RenderBackend.Auto && NativeMethods.IsBackendAvailable(backend) != 0;

    private static ReadOnlySpan<RenderBackend> GetBackendFallbackOrder()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return [RenderBackend.D3D12, RenderBackend.Software];
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return [RenderBackend.Metal, RenderBackend.Software];
        return [RenderBackend.Vulkan, RenderBackend.Software];
    }

    private static int GetBackendFallbackIndex(RenderBackend backend)
    {
        var order = GetBackendFallbackOrder();
        for (int i = 0; i < order.Length; i++)
        {
            if (order[i] == backend)
            {
                return i;
            }
        }

        return -1;
    }

    private bool TryAdvanceRenderBackendFallback(RenderBackend failedBackend)
    {
        var order = GetBackendFallbackOrder();
        int index = GetBackendFallbackIndex(failedBackend);
        if (index < 0)
        {
            index = 0;
        }

        for (int i = index + 1; i < order.Length; i++)
        {
            var candidate = order[i];
            if (candidate != failedBackend && IsBackendAvailable(candidate))
            {
                _renderBackendOverride = candidate;
                return true;
            }
        }

        return false;
    }

    private void EnableD3D12WarpFallback()
    {
        _renderBackendOverride = RenderBackend.D3D12;
        Environment.SetEnvironmentVariable(D3D12ForceWarpEnvironmentVariable, "1");
    }

    private void EnsureRenderTarget(bool forceNewContext = false)
    {
        if (RenderTarget != null)
        {
            return;
        }

        // Swap chain uses physical pixel dimensions — bail if the window hasn't
        // been sized yet (0×0 swap chains are rejected by DXGI).
        int physicalWidth = (int)(Width * _dpiScale);
        int physicalHeight = (int)(Height * _dpiScale);
        if (physicalWidth <= 0 || physicalHeight <= 0 || Handle == nint.Zero)
        {
            Console.Error.WriteLine($"[EnsureRenderTarget] BAIL: pw={physicalWidth} ph={physicalHeight} handle=0x{Handle:X}");
            return;
        }

        var requestedBackend = _renderBackendOverride != RenderBackend.Auto
            ? _renderBackendOverride
            : RenderBackend.Auto;

        var context = RenderContext.GetOrCreateCurrent(requestedBackend, forceReplace: forceNewContext);

        try
        {
            if (_platformWindow != null)
            {
                // Cross-platform path: use surface descriptor from platform window
                var surface = _platformWindow.GetSurface();
                RenderTarget = context.CreateRenderTarget(surface, physicalWidth, physicalHeight);
            }
            else
            {
                // Win32 path: use HWND-based render target
                bool useComposition = ShouldUseCompositionRenderTarget();
                if (useComposition)
                {
                    RenderTarget = context.CreateRenderTargetForComposition(Handle, physicalWidth, physicalHeight);
                }
                else
                {
                    RenderTarget = context.CreateRenderTarget(Handle, physicalWidth, physicalHeight);
                }
            }
        }
        catch (Exception ex) when (context.GpuPreference != GpuPreference.Auto)
        {
            Console.Error.WriteLine($"[EnsureRenderTarget] GPU fallback: {ex.Message}");
            // The preferred GPU couldn't create a render target. Fall back to default GPU.
            context = RenderContext.GetOrCreateCurrent(requestedBackend, GpuPreference.Auto, forceReplace: true);
            RenderTarget = CreateRenderTargetForPlatform(context, physicalWidth, physicalHeight);
        }
        catch (Exception ex) when (requestedBackend != RenderBackend.Auto && TryAdvanceRenderBackendFallback(requestedBackend))
        {
            Console.Error.WriteLine($"[EnsureRenderTarget] backend fallback: {ex.Message}");
            var fallbackContext = RenderContext.GetOrCreateCurrent(_renderBackendOverride, GpuPreference.Auto, forceReplace: true);
            RenderTarget = CreateRenderTargetForPlatform(fallbackContext, physicalWidth, physicalHeight);
        }

        // Set D2D DPI so DIP coordinates map correctly to physical pixels
        float dpi = (float)(_dpiScale * 96.0);
        RenderTarget?.SetDpi(dpi, dpi);

        // Apply the app-selected path anti-aliasing quality (app.UsePathAntiAliasing).
        // Here so it covers both initial creation and device-lost recovery (both flow
        // through EnsureRenderTarget). The native side clamps to a supported sample
        // count. Filled paths (stencil-then-cover MSAA) dominate the per-present GPU
        // cost on weak GPUs, so this is the biggest single quality/perf lever.
        RenderTarget?.SetPathMsaaSampleCount(Jalium.UI.Hosting.RenderQualityOptions.Current.ResolvePathMsaaSampleCount());

        // VSync default: ON. With FRAME_LATENCY_WAITABLE_OBJECT +
        // SetMaximumFrameLatency(1) the swap chain already aligns CPU pacing
        // to vsync, and Present(1) lets the BeginDraw waitable wait collapse
        // to ~0 ms (buffer is genuinely ready when CPU returns from Present)
        // instead of the ~100 ms waitable-timeout pattern observed when
        // Present(0) is used. The matching DisableVSync override on
        // WM_ENTERSIZEMOVE keeps resizes snappy.
        // Opt-out via JALIUM_DISABLE_VSYNC=1 for benchmark / animation cases.
        RenderTarget?.SetVSyncEnabled(!VSyncDisabledByEnv);

        StartFramePacerIfSupported();
        StartRenderThreadIfSupported();
        // After StartRenderThreadIfSupported — render-thread mode keeps native
        // pacing (it is the waitable's sole consumer there); this no-ops then.
        StartExternalPresentPacingIfSupported();
    }

    /// <summary>
    /// Creates a render target using the appropriate method for the current platform.
    /// </summary>
    private RenderTarget CreateRenderTargetForPlatform(RenderContext context, int width, int height)
    {
        if (_platformWindow != null)
        {
            var surface = _platformWindow.GetSurface();
            return context.CreateRenderTarget(surface, width, height);
        }

        if (ShouldUseCompositionRenderTarget())
            return context.CreateRenderTargetForComposition(Handle, width, height);

        return context.CreateRenderTarget(Handle, width, height);
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
            try
            {
                RenderTarget.DestroyWebViewCompositionVisual(existingVisual);
                return true;
            }
            catch (RenderPipelineException)
            {
                // Device lost between probe-create and probe-destroy (e.g. GPU
                // switch). Fall through to the full rebuild path below, which
                // recreates the composition target on the recovered device. The
                // leaked probe visual is reclaimed when the old RenderTarget is
                // disposed there.
            }
        }

        try
        {
            // FIX #2: a composition (DComp) swap chain is incompatible with the
            // render thread (StartRenderThreadIfSupported refuses composition), so
            // a window transitioning to composition must stop+join the render
            // thread BEFORE disposing the RenderTarget it owns.
            if (!StopRenderThread())
            {
                return false;
            }

            var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);

            // Dispose the old render target BEFORE changing the window style.
            // SetWindowPos(SWP_FRAMECHANGED) can trigger a synchronous WM_PAINT,
            // which would call RenderFrame with the old non-composition swap chain
            // on a window that now has WS_EX_NOREDIRECTIONBITMAP — the old swap
            // chain's Present fails, dirty state is lost, and the stale frame
            // from before the transition stays on screen permanently.
            // Nulling the render target first causes the intermediate RenderFrame
            // to early-return (RenderTarget == null check at the top).
            ReleaseDrawingContextBeforeRenderTargetDisposal(clearAllCaches: true);

            var oldRenderTarget = RenderTarget;
            RenderTarget = null;
            // The swap wait targets the old swap chain's waitable HANDLE —
            // unregister before Dispose closes it. (The DComp replacement
            // target never re-arms pacing: StartExternalPresentPacingIfSupported
            // skips composition swap chains.)
            StopExternalPresentPacing();
            oldRenderTarget?.Dispose();

            // Now safe to change window style — any WM_PAINT during SetWindowPos
            // will see RenderTarget == null and skip rendering.
            long exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            if ((exStyle & WS_EX_NOREDIRECTIONBITMAP) == 0)
            {
                _ = SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_NOREDIRECTIONBITMAP);
                _ = SetWindowPos(Handle, nint.Zero, 0, 0, 0, 0,
                    SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
            }

            int widthDip = ActualWidth > 0 ? (int)Math.Ceiling(ActualWidth) : (int)Math.Ceiling(Width);
            int heightDip = ActualHeight > 0 ? (int)Math.Ceiling(ActualHeight) : (int)Math.Ceiling(Height);
            int physicalWidth = Math.Max(1, (int)Math.Ceiling(widthDip * _dpiScale));
            int physicalHeight = Math.Max(1, (int)Math.Ceiling(heightDip * _dpiScale));

            RenderTarget = context.CreateRenderTargetForComposition(Handle, physicalWidth, physicalHeight);

            float dpi = (float)(_dpiScale * 96.0);
            RenderTarget.SetDpi(dpi, dpi);

            // Composition swap chains use DXGI_ALPHA_MODE_PREMULTIPLIED, so
            // any semi-transparent window Background becomes truly transparent
            // (DWM blends with the desktop).  When there is no system backdrop
            // this is almost certainly unintentional and causes ghost-image
            // artifacts (sidebar text doubled, desktop bleeding through).
            // Force the background to fully opaque to prevent the bleed-through.
            //
            // AllowsTransparency=true 表示调用方明确想要真透明背景（接受 ghost-image
            // 风险），跳过强制不透明逻辑——典型场景是浮动 / 拖拽指示器 window 故意要透明
            // 给用户看到下方内容。
            if (!AllowsTransparency &&
                SystemBackdrop == WindowBackdropType.None &&
                Background is Media.SolidColorBrush bgBrush &&
                bgBrush.Color.A < 255)
            {
                var c = bgBrush.Color;
                Background = new Media.SolidColorBrush(Media.Color.FromArgb(255, c.R, c.G, c.B));
            }

            ForceRenderFrame();

            if (RenderTarget.TryCreateWebViewCompositionVisual(out var visualAfterSwitch) && visualAfterSwitch != nint.Zero)
            {
                RenderTarget.DestroyWebViewCompositionVisual(visualAfterSwitch);
                return true;
            }
        }
        catch (Exception ex)
        {
            LogRenderFailure(ex, "EnsureCompositionRenderTargetForEmbeddedContent");

            // Ensure a full re-render is scheduled so the window doesn't stay
            // stuck with stale content from before the render target swap.
            lock (_dirtyLock)
            {
                MarkFullInvalidationLocked();
            }
            if (RenderTarget != null && RenderTarget.IsValid)
            {
                ScheduleRenderAfterRecovery();
            }
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
                if (window._platformWindow != null)
                    window._platformWindow.SetTitle((string?)e.NewValue ?? "");
                else
                    _ = SetWindowText(window.Handle, (string?)e.NewValue ?? "");
            }

            window.ApplyTitleBarPresentation();
        }
    }

    private static void OnWindowStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && e.NewValue is WindowState newState)
        {
            var oldState = e.OldValue is WindowState os ? os : WindowState.Normal;

            // Capture restore bounds when leaving Normal state
            if (oldState == WindowState.Normal && newState != WindowState.Normal
                && window.Handle != nint.Zero)
            {
                window.CaptureRestoreBounds();
            }

            window.ApplyTitleBarPresentation();

            // Sync the native window state when set programmatically.
            // Skip if we're already syncing from WM_SIZE to avoid infinite loop.
            if (!window._isSyncingWindowState && window.Handle != nint.Zero)
            {
                if (window._platformWindow != null)
                {
                    // Cross-platform backend: fullscreen not yet supported — fall
                    // back to maximized so the request still produces a reasonable
                    // visual result.
                    var mapped = newState == WindowState.FullScreen
                        ? WindowState.Maximized
                        : newState;
                    window._platformWindow.SetState(mapped);
                }
                else
                {
                    // Leaving fullscreen: restore pre-fullscreen frame + bounds first.
                    if (oldState == WindowState.FullScreen && newState != WindowState.FullScreen)
                    {
                        window.ExitFullScreen();
                    }

                    if (newState == WindowState.FullScreen)
                    {
                        window._fullScreenPreviousState = oldState == WindowState.FullScreen
                            ? WindowState.Normal
                            : oldState;
                        // Ensure the window is visible and in a restored state before
                        // capturing bounds/style for fullscreen.
                        if (oldState == WindowState.Minimized)
                        {
                            _ = ShowWindow(window.Handle, SW_RESTORE);
                        }
                        else if (oldState == WindowState.Maximized)
                        {
                            _ = ShowWindow(window.Handle, SW_RESTORE);
                        }
                        window.EnterFullScreen();
                        _ = ShowWindow(window.Handle, SW_SHOW);
                    }
                    else
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

            window.OnStateChanged(EventArgs.Empty);
        }
    }

    private static void OnWindowStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && window.Handle != nint.Zero)
        {
            window.UpdateWindowStyle();
            window.ApplyTitleBarPresentation();
            window.InvalidateMeasure();
        }
    }

    private static void OnResizeModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
        {
            return;
        }

        // ResizeMode is a live dependency property (WPF parity): a change after the
        // window exists must re-apply the Win32 frame bits (WS_THICKFRAME /
        // WS_MINIMIZEBOX / WS_MAXIMIZEBOX) and re-derive the custom title-bar buttons.
        // UpdateWindowStyle() self-guards the pre-handle, cross-platform
        // (_platformWindow) and fullscreen cases, so this is safe to call
        // unconditionally; ApplyTitleBarPresentation() no-ops when there is no TitleBar.
        window.UpdateWindowStyle();
        window.ApplyTitleBarPresentation();
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

    private static void OnTitleBarStyleResolutionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window)
        {
            window.ResolveAndApplyTitleBarStyle();
        }
    }

    /// <summary>
    /// Resolves the effective <see cref="Style"/> for the custom <see cref="TitleBar"/>
    /// from <see cref="CustomTitleBarStyle"/> (preferred) or <see cref="TitleBarStyleKey"/>
    /// (looked up via <see cref="FrameworkElement.TryFindResource"/>), and assigns it to
    /// <see cref="Controls.TitleBar.Style"/>. Clearing both restores the implicit style.
    /// </summary>
    private void ResolveAndApplyTitleBarStyle()
    {
        if (TitleBar == null)
        {
            return;
        }

        Style? resolved = CustomTitleBarStyle;

        if (resolved == null)
        {
            var key = TitleBarStyleKey;
            if (key != null)
            {
                resolved = TryFindResource(key) as Style;
            }
        }

        // Setting Style to null re-engages implicit-style lookup in FrameworkElement.OnStyleChanged.
        if (!ReferenceEquals(TitleBar.Style, resolved))
        {
            TitleBar.Style = resolved;
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

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && window.Handle != nint.Zero && !window._isSyncingPosition)
        {
            int x = double.IsNaN(window.Left) ? 0 : (int)(window.Left * window._dpiScale);
            int y = double.IsNaN(window.Top) ? 0 : (int)(window.Top * window._dpiScale);
            if (double.IsNaN(window.Left) || double.IsNaN(window.Top))
            {
                return;
            }

            if (window._platformWindow != null)
            {
                window._platformWindow.Move(x, y);
            }
            else
            {
                uint flags = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE;
                _ = SetWindowPos(window.Handle, nint.Zero, x, y, 0, 0, flags);
            }
        }
    }

    private static void OnShowInTaskbarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && window.Handle != nint.Zero)
        {
            bool show = e.NewValue is true;
            long exStyle = GetWindowLong(window.Handle, GWL_EXSTYLE);
            if (show)
            {
                exStyle &= ~(long)WS_EX_TOOLWINDOW;
                exStyle |= WS_EX_APPWINDOW;
            }
            else
            {
                exStyle |= WS_EX_TOOLWINDOW;
                exStyle &= ~(long)WS_EX_APPWINDOW;
            }
            _ = SetWindowLong(window.Handle, GWL_EXSTYLE, exStyle);
            // Force the shell to re-evaluate taskbar presence
            _ = SetWindowPos(window.Handle, nint.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
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

    internal static Window? TryGetOpenWindow(nint handle)
    {
        return handle != nint.Zero && _windows.TryGetValue(handle, out var window)
            ? window
            : null;
    }

    /// <summary>
    /// Snapshots the set of currently-open <see cref="Window"/> instances.
    /// The returned array is a copy — safe to iterate without holding any
    /// lock and resilient to windows opening/closing during traversal.
    /// </summary>
    /// <remarks>
    /// Used by the idle-resource reclaimer to walk every active render
    /// target and ask its backend to drop accumulated caches. The list is
    /// snapshotted intentionally: callers that want a live view need to
    /// re-snapshot each frame.
    /// </remarks>
    internal static Window[] SnapshotOpenWindows()
    {
        // _windows is mutated only on the UI thread (WM_NCCREATE / WM_DESTROY
        // run inside the message pump); the reclaimer is also on the UI
        // thread, so a plain ToArray() is race-free in practice. We still
        // copy to a fresh array so iteration cannot be invalidated by a
        // window closing later in the same call chain (e.g. a reclaim
        // implementation that destroys a popup).
        var result = new Window[_windows.Count];
        var i = 0;
        foreach (var w in _windows.Values)
        {
            result[i++] = w;
        }
        return result;
    }

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

        var element = HitTestElement(clientPos, "cursor");

        // Walk up the visual tree to find the first element with a non-null Cursor
        var cursor = Mouse.OverrideCursor ?? ResolveCursor(element);

        // Set the cursor
        Cursor effectiveCursor = cursor ?? Cursors.Arrow;
        Mouse.SetCursor(effectiveCursor);
        nint cursorHandle = GetCursorHandle(effectiveCursor.CursorType);

        if (cursorHandle != nint.Zero)
        {
            _ = SetCursor(cursorHandle);
            return true;
        }

        return false;
    }

    private static Cursor? ResolveCursor(UIElement? element)
    {
        // A disabled element shows the standard arrow — neither its own Cursor
        // nor any inherited/ancestor cursor applies. IsEnabled is the effective
        // value (walks the parent chain); an enabled hit element guarantees the
        // whole ancestor chain is enabled too. Returning null makes OnSetCursor
        // fall back to CursorType.Arrow.
        if (element is { IsEnabled: false })
            return null;

        while (element != null)
        {
            if (element is FrameworkElement fe)
            {
                return FrameworkElement.ResolveEffectiveCursor(fe);
            }

            element = element.VisualParent as UIElement;
        }

        return null;
    }

    private DevToolsWindow? _devToolsWindow;
    internal DevToolsOverlay? DevToolsOverlay { get; set; }

    /// <summary>
    /// Gets whether this window can open DevTools.
    /// Default: reads <see cref="Jalium.UI.Hosting.DeveloperToolsOptions.EnableDevTools"/>
    /// from the application's DI container — apps must call
    /// <c>app.UseDevTools()</c> on the built <see cref="JaliumApp"/> to opt in.
    /// Without that call F12 is inert. Subclasses (e.g. <c>DevToolsWindow</c>)
    /// can still override to force a stricter policy (hard-disable regardless
    /// of the service flag).
    /// </summary>
    protected virtual bool CanOpenDevTools
        => Jalium.UI.Hosting.DeveloperToolsResolver.IsDevToolsEnabled;

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
               CanUserResize &&
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

        _wndProcDelegate = StaticWndProc;

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

    private static nint StaticWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (_windows.TryGetValue(hWnd, out var window))
        {
            return window.WndProc(hWnd, msg, wParam, lParam);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    protected virtual nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            return WndProcCore(hWnd, msg, wParam, lParam);
        }
        catch (Exception)
        {
            // Never allow managed exceptions to escape the native window procedure.
            // If they do, the OS callback chain can become unstable and future
            // messages may appear to stop reaching the window entirely.
            return hWnd == nint.Zero
                ? nint.Zero
                : DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2050:CorrectnessOfComInteropCannotBeGuaranteed",
        Justification = "The WM_GETOBJECT path passes an IRawElementProviderSimple to the UiaReturnRawElementProvider P/Invoke for native UIA interop. That COM interface is preserved by the <type fullname=\"Jalium.UI.Controls.Automation.Uia.IRawElementProviderSimple\" preserve=\"all\" /> entry in Jalium.UI.Managed/ILLink.Descriptors.xml (as documented on UiaNativeMethods), so the trimmer keeps the vtable members the runtime calls through this P/Invoke.")]
    private nint WndProcCore(nint hWnd, uint msg, nint wParam, nint lParam)
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

        if (Jalium.UI.Diagnostics.HoverTrace.Enabled)
        {
            switch (msg)
            {
                case WM_NCMOUSEMOVE: Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.NCMM); break;
                case WM_NCHITTEST: Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.NCHT); break;
                case WM_SETCURSOR: Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.SETCUR); break;
                case WM_MOUSEMOVE: Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.MM); break;
                case WM_NCMOUSEHOVER: Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.NCHOVER); break;
                case WM_PAINT: Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.PAINT); break;
            }
        }

        if (window != null)
        {
            switch (msg)
            {
                case WM_GETMINMAXINFO:
                {
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

                    // A borderless (WindowStyle.None / WS_POPUP) window would, when
                    // maximized, otherwise follow the system default that leaves the
                    // taskbar uncovered.  To match WPF — where Maximized +
                    // WindowStyle.None fills the entire monitor — override the
                    // maximized size/position to the full monitor rect (rcMonitor).
                    // It is computed for the monitor the window is on so multi-monitor
                    // setups maximize onto the correct display; ptMaxPosition is
                    // expressed relative to that monitor's origin, so (0,0) is its
                    // top-left corner.  Bordered windows are left untouched here, so
                    // their maximize keeps respecting the taskbar (work area).
                    if (window.WindowStyle == WindowStyle.None)
                    {
                        var maximizeMonitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                        MONITORINFO maximizeMonitorInfo = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                        if (maximizeMonitor != nint.Zero && GetMonitorInfo(maximizeMonitor, ref maximizeMonitorInfo))
                        {
                            var rcMonitor = maximizeMonitorInfo.rcMonitor;
                            mmi.ptMaxPosition.X = 0;
                            mmi.ptMaxPosition.Y = 0;
                            mmi.ptMaxSize.X = rcMonitor.right - rcMonitor.left;
                            mmi.ptMaxSize.Y = rcMonitor.bottom - rcMonitor.top;
                        }
                    }

                    double dpi = window._dpiScale;
                    double minW = window.MinWidth;
                    double minH = window.MinHeight;
                    double maxW = window.MaxWidth;
                    double maxH = window.MaxHeight;
                    if (!double.IsNaN(minW) && minW > 0)
                        mmi.ptMinTrackSize.X = (int)(minW * dpi);
                    if (!double.IsNaN(minH) && minH > 0)
                        mmi.ptMinTrackSize.Y = (int)(minH * dpi);
                    if (!double.IsInfinity(maxW) && maxW > 0)
                        mmi.ptMaxTrackSize.X = (int)(maxW * dpi);
                    if (!double.IsInfinity(maxH) && maxH > 0)
                        mmi.ptMaxTrackSize.Y = (int)(maxH * dpi);
                    Marshal.StructureToPtr(mmi, lParam, true);
                    return nint.Zero;
                }

                case WM_GETOBJECT:
                {
                    if ((int)lParam == -25 /* UiaRootObjectId */)
                    {
                        // A real UIA client (Narrator, Inspect, an automation test
                        // harness, or a translation/OCR tool that scans window text such
                        // as Qwen/Tongyi) is asking for the root provider. Hand it off to
                        // the bridge, which marshals the provider defensively: under
                        // NativeAOT (no built-in COM interop, no registered ComWrappers)
                        // the marshal cannot succeed, and the bridge latches that state
                        // and returns 0 rather than letting NotSupportedException escape.
                        // It also arms the event sink ONLY after a marshal actually
                        // succeeds — never a tick earlier — so a failed marshal here can
                        // no longer leave a live sink that crashes a later layout pass,
                        // and UIAutomationCore.dll is still not forced to load on startup.
                        var peer = window.GetAutomationPeer();
                        if (peer != null)
                        {
                            var result = Automation.Uia.UiaAccessibilityBridge.TryGetRootProvider(
                                peer, hWnd, wParam, lParam);
                            if (result > 0)
                                return result;
                        }
                    }
                    break;
                }

                case WM_CLOSE:
                    // Route through Close() so Closing event can cancel
                    window.Close();
                    return nint.Zero;

                case WM_DESTROY:
                    Automation.Uia.UiaAccessibilityBridge.NotifyWindowDestroyed(hWnd);
                    // Native destruction can bypass Close. Release the same managed
                    // resources, but do not recursively destroy the HWND or post quit.
                    window.OnNativeDestroyed(hWnd);
                    return nint.Zero;

                case WM_NCCALCSIZE:
                    // WindowStyle=None: swallow the entire non-client area so there's
                    // no DWM-drawn frame strip (otherwise WS_THICKFRAME leaves a thin
                    // white bar at the top on Win11). Edge resize is still available
                    // via WM_NCHITTEST below.
                    if (window.WindowStyle == WindowStyle.None)
                    {
                        if (wParam == nint.Zero)
                        {
                            return nint.Zero;
                        }
                        // Returning 0 with the rect unchanged tells Windows the entire
                        // window rect is client area.
                        return nint.Zero;
                    }
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
                    // WindowStyle=None (with a native title bar style): provide edge
                    // resize hit-testing ourselves since DefWindowProc won't — the
                    // frame area has been swallowed by WM_NCCALCSIZE.
                    if (window.WindowStyle == WindowStyle.None
                        && window.TitleBarStyle != WindowTitleBarStyle.Custom)
                    {
                        var hit = window.HandleNcHitTest(lParam);
                        return hit == HTNOWHERE ? HTCLIENT : hit;
                    }
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        // Let DWM first have a chance to resolve the NC hit. This is
                        // what primes the Windows 11 Snap Layouts state machine — DWM
                        // uses this pass to notice HTMAXBUTTON in subsequent NC mouse
                        // messages and arm the flyout timer. Without this call, the
                        // flyout never appears even when we return HTMAXBUTTON. If
                        // DWM claims the hit (typical for caption resize handles),
                        // honor its result; otherwise fall through to our custom
                        // button / caption / client logic.
                        if (DwmDefWindowProc(hWnd, msg, wParam, lParam, out nint dwmHit) && dwmHit != nint.Zero)
                        {
                            return dwmHit;
                        }

                        var customHitResult = window.HandleNcHitTest(lParam);
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
                        // Let DefWindowProc handle NC hover tracking so Windows 11 can
                        // arm the Snap Layouts flyout timer. Do not swallow this message.
                    }
                    break;

                case WM_NCMOUSEHOVER:
                    // Let DefWindowProc forward this to DWM/Shell for Snap Layouts.
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
                        if (window.OnNcLButtonDblClk(wParam, lParam))
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCRBUTTONDOWN:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        if (window.ShouldUseWin11SnapNcRouting())
                        {
                            break;
                        }

                        int ncRbDownX = (short)(lParam.ToInt64() & 0xFFFF);
                        int ncRbDownY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        POINT ncRbDownPt = new() { X = ncRbDownX, Y = ncRbDownY };
                        _ = ScreenToClient(hWnd, ref ncRbDownPt);
                        if (window.GetTitleBarButtonAtPoint(new Point(ncRbDownPt.X / window._dpiScale, ncRbDownPt.Y / window._dpiScale)) != null)
                        {
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_NCRBUTTONUP:
                    if (window.TitleBarStyle == WindowTitleBarStyle.Custom)
                    {
                        int ncRbX = (short)(lParam.ToInt64() & 0xFFFF);
                        int ncRbY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        POINT ncRbPt = new() { X = ncRbX, Y = ncRbY };
                        _ = ScreenToClient(hWnd, ref ncRbPt);
                        // Suppress right-click on title bar buttons
                        if (window.GetTitleBarButtonAtPoint(new Point(ncRbPt.X / window._dpiScale, ncRbPt.Y / window._dpiScale)) != null)
                        {
                            return nint.Zero;
                        }

                        // Show system menu on caption area right-click
                        int ncHitTest = (int)wParam.ToInt64();
                        if (ncHitTest == HTCAPTION || ncHitTest == HTSYSMENU)
                        {
                            window.ShowSystemMenu(ncRbX, ncRbY);
                            return nint.Zero;
                        }
                    }
                    break;

                case WM_SIZE:
                    int sizeType = (int)wParam.ToInt64();
                    int width = (int)(lParam.ToInt64() & 0xFFFF);
                    int height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                    if (ResizeTraceEnabled) Console.Error.WriteLine($"[resize-trace] WM_SIZE {width}x{height} type={sizeType} isSizing={window._isSizing}");

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
                                // While in fullscreen we intentionally resize to the
                                // monitor bounds, which produces a SIZE_RESTORED
                                // message. Do NOT drop the FullScreen state in that case.
                                if (!window._isFullScreen && window.WindowState != WindowState.Normal)
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
                {
                    // Sync Left/Top from native window rect (outer bounds)
                    if (GetWindowRect(hWnd, out RECT windowRect))
                    {
                        window._isSyncingPosition = true;
                        try
                        {
                            window.Left = windowRect.left / window._dpiScale;
                            window.Top = windowRect.top / window._dpiScale;
                        }
                        finally
                        {
                            window._isSyncingPosition = false;
                        }
                    }
                    window.OnLocationChanged(EventArgs.Empty);
                    // Re-detect refresh rate (window may have moved to a different monitor)
                    CompositionTarget.UpdateRefreshRate(window.DetectMonitorRefreshRate());
                    return nint.Zero;
                }

                case WM_DISPLAYCHANGE:
                    // Display settings changed (resolution, refresh rate, etc.)
                    CompositionTarget.UpdateRefreshRate(window.DetectMonitorRefreshRate());
                    return nint.Zero;

                case WM_DPICHANGED:
                {
                    // Per-monitor DPI change (window moved to different DPI monitor)
                    var oldDpiScale = window._dpiScale;
                    uint newDpi = (uint)((wParam.ToInt64() >> 16) & 0xFFFF);
                    window._dpiScale = newDpi / 96.0;
                    FrameworkElement.LayoutDpiScale = window._dpiScale;

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

                    window.OnDpiChanged(new DpiChangedEventArgs(
                        new DpiScale(oldDpiScale, oldDpiScale),
                        new DpiScale(window._dpiScale, window._dpiScale)));

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
                    // Disable VSync during resize for faster frame updates.
                    // FIX #2: drain the render thread around the swap-chain param
                    // change — Present is not safe against a concurrent sync-interval
                    // mutation from another thread. (No-op when the render thread is off.)
                    // FIX #5 consistency: honor a failed park — skip the mutation
                    // rather than race an in-flight Present; the resize degrade
                    // path works (slightly slower) with vsync still on.
                    //
                    // External pacing 下跳过：Present 本就恒 sync-interval 0（关 vsync
                    // 零收益），而 vsyncEnabled_=false 会让 presentFlags 引入
                    // DXGI_PRESENT_ALLOW_TEARING——远程/虚拟显示器(IDD)环境实测
                    // tearing present 不被 DWM 合成，整个拖拽期间窗口停在旧帧、
                    // 新暴露区域全黑（"resize 空白不刷新"的根因）。
                    if (!window._swapPacingActive)
                    {
                        if (window.RequestRenderThreadIdle())
                        {
                            window.RenderTarget?.SetVSyncEnabled(false);
                        }
                        window.ResumeRenderThread();
                    }
                    return nint.Zero;

                case WM_EXITSIZEMOVE:
                    window._isSizing = false;
                    // Re-enable VSync after resize (honour the JALIUM_DISABLE_VSYNC
                    // opt-out so a benchmark session that disabled it stays disabled).
                    // FIX #2: drain the render thread around the swap-chain param change.
                    // FIX #5 consistency: honor a failed park (see WM_ENTERSIZEMOVE).
                    // External pacing 下对称跳过（ENTERSIZEMOVE 没改过 vsync）。
                    if (!window._swapPacingActive)
                    {
                        if (window.RequestRenderThreadIdle())
                        {
                            window.RenderTarget?.SetVSyncEnabled(!VSyncDisabledByEnv);
                        }
                        window.ResumeRenderThread();
                    }
                    // Do final resize to ensure correct buffer size (physical pixels)
                    int finalPhysW = (int)(window.Width * window._dpiScale);
                    int finalPhysH = (int)(window.Height * window._dpiScale);
                    window.TryResizeRenderTarget(finalPhysW, finalPhysH, "ExitSizeMoveResize");
                    // 强制重画一帧 _isSizing=false 的正常帧。拖拽期间每帧画的是 _isSizing=true 的
                    // backdrop 降级帧（折射型特效被简化/跳过），松手时 _isSizing 刚变 false，但
                    // 内容没变（无 dirty）、最后一帧已把 _fullInvalidation 清为 false、且本帧
                    // TryResizeRenderTarget 多半因尺寸已一致而 no-op（不重设 _fullInvalidation）——
                    // 若不在此显式 RequestFullInvalidation，下面 RedrawWindow 触发的 RenderFrame 会在
                    // "!_fullInvalidation && 无 dirty" 处 skip，屏幕停在 _isSizing=true 的降级坏帧
                    // （所有特效失效），直到鼠标移动经 InvalidateWindow 才恢复。这才是 resize 后
                    // "特效失效 + 需鼠标刷新" 的主因（与 TryBeginDrawOrScheduleRetry 的重试修复互补：
                    // 此处保证发起重画，那里保证重画帧 GPU 忙时也会重试到成功）。
                    window.RequestFullInvalidation();
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
                    if (IsShellReservedVirtualKey(wParam))
                    {
                        break;
                    }

                    bool keyDownHandled = window.OnNativeKeyDown(wParam, lParam);
                    if (keyDownHandled || hWnd == nint.Zero)
                    {
                        return nint.Zero;
                    }
                    break;

                case WM_KEYUP:
                    if (IsShellReservedVirtualKey(wParam))
                    {
                        break;
                    }

                    bool keyUpHandled = window.OnNativeKeyUp(wParam, lParam);
                    if (keyUpHandled || hWnd == nint.Zero)
                    {
                        return nint.Zero;
                    }
                    break;

                case WM_SYSKEYDOWN:
                    bool sysKeyDownHandled = window.OnNativeKeyDown(wParam, lParam);
                    if (sysKeyDownHandled || hWnd == nint.Zero)
                    {
                        return nint.Zero;
                    }
                    break;

                case WM_SYSKEYUP:
                    bool sysKeyUpHandled = window.OnNativeKeyUp(wParam, lParam);
                    if (sysKeyUpHandled || hWnd == nint.Zero)
                    {
                        return nint.Zero;
                    }
                    break;

                case WM_CHAR:
                    window.OnChar(wParam, lParam);
                    return nint.Zero;

                // IME input
                case WM_IME_STARTCOMPOSITION:
                    if (window.CanHandleImeMessages())
                    {
                        window.OnImeStartComposition();
                        return nint.Zero;
                    }

                    break;

                case WM_IME_ENDCOMPOSITION:
                    if (window.CanHandleImeMessages() || InputMethod.IsComposing)
                    {
                        window.OnImeEndComposition();
                        return nint.Zero;
                    }

                    break;

                case WM_IME_COMPOSITION:
                    if (window.CanHandleImeMessages() && window.OnImeComposition(lParam))
                    {
                        return nint.Zero;
                    }

                    break;

                case WM_IME_CHAR:
                    // IME character - let it fall through to default processing
                    // or handle specially if needed
                    break;

                case WM_IME_SETCONTEXT:
                    // When the focused element disallows IME (read-only text controls),
                    // swallow the activation so DefWindowProc does not show the IME
                    // composition / candidate window. The IME context itself is detached
                    // in UpdateInputMethodAssociation; intercepting this message stops the
                    // candidate list that Microsoft Pinyin (and other IMEs) would otherwise
                    // pop up despite the detached context. Editable targets fall through so
                    // the IME UI keeps working normally.
                    if (!window.CanHandleImeMessages())
                    {
                        return nint.Zero;
                    }

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
                    {
                        return nint.Zero;
                    }
                    window.OnMouseMove(wParam, lParam);
                    return nint.Zero;

                // NOTE: Do NOT filter promoted mouse messages here.
                // OnPointerMessage already returns early for Mouse-kind pointers,
                // so WM_xBUTTON* / WM_MOUSEWHEEL are the sole delivery path for
                // mouse clicks on systems with WM_POINTER support.
                case WM_LBUTTONDOWN:
                    window.OnMouseButtonDown(MouseButton.Left, wParam, lParam, clickCount: 1);
                    return nint.Zero;

                case WM_LBUTTONDBLCLK:
                    window.OnMouseButtonDown(MouseButton.Left, wParam, lParam, clickCount: 2);
                    return nint.Zero;

                case WM_LBUTTONUP:
                    window.OnMouseButtonUp(MouseButton.Left, wParam, lParam);
                    return nint.Zero;

                case WM_RBUTTONDOWN:
                    window.OnMouseButtonDown(MouseButton.Right, wParam, lParam, clickCount: 1);
                    return nint.Zero;

                case WM_RBUTTONDBLCLK:
                    window.OnMouseButtonDown(MouseButton.Right, wParam, lParam, clickCount: 2);
                    return nint.Zero;

                case WM_RBUTTONUP:
                    window.OnMouseButtonUp(MouseButton.Right, wParam, lParam);
                    return nint.Zero;

                case WM_MBUTTONDOWN:
                    window.OnMouseButtonDown(MouseButton.Middle, wParam, lParam, clickCount: 1);
                    return nint.Zero;

                case WM_MBUTTONDBLCLK:
                    window.OnMouseButtonDown(MouseButton.Middle, wParam, lParam, clickCount: 2);
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

                case WM_CAPTURECHANGED:
                    window._inputDispatcher.HandleNativeCaptureChanged();
                    return nint.Zero;

                case WM_CANCELMODE:
                    window.OnCancelMode();
                    return nint.Zero;

                case WM_ACTIVATE:
                    int activateState = (int)(wParam.ToInt64() & 0xFFFF);
                    window.OnActivateChanged(activateState, lParam);
                    break;

                case WM_ACTIVATEAPP:
                    // Unlike WM_ACTIVATE, this is process-wide. Every top-level HWND receives
                    // it, and Application de-duplicates those identical notifications.
                    Application.Current?.SetPlatformActivationState(wParam != nint.Zero);
                    break;

                case WM_SETFOCUS:
                    window.OnSetFocus();
                    // Notify UIA that this window received focus so Narrator can announce it.
                    // Routed through EventSink (instead of UiaAccessibilityBridge directly)
                    // so the UIAutomationCore.dll first-touch is gated on a real UIA client
                    // having attached via WM_GETOBJECT — otherwise the [DllImport] inside
                    // UiaClientsAreListening() forces UIAutomationCore + Oleacc to load on
                    // every process startup. Must be deferred either way: UIA COM calls
                    // during input-synchronous messages cause RPC_E_CANTCALLOUT.
                    if (OperatingSystem.IsWindows())
                    {
                        window.Dispatcher.BeginInvoke(() =>
                        {
                            var focusPeer = window.GetAutomationPeer();
                            if (focusPeer != null)
                                Jalium.UI.Automation.Peers.AutomationPeer.EventSink?.OnFocusChanged(focusPeer);
                        });
                    }
                    break;

                case WM_KILLFOCUS:
                    window.OnKillFocus(wParam);
                    break;

                case WM_SETTINGCHANGE:
                case WM_THEMECHANGED:
                    window.OnSystemSettingsChanged(EventArgs.Empty);
                    break;

                case WM_QUERYENDSESSION:
                {
                    // lParam bit 0 set = shutdown, else logoff
                    var reason = (lParam.ToInt64() & 1) != 0
                        ? ReasonSessionEnding.Shutdown
                        : ReasonSessionEnding.Logoff;
                    var args = new SessionEndingCancelEventArgs(reason);
                    // Window-level handler first, then application-level so either
                    // can cancel the end-session request.
                    window.OnSessionEnding(args);
                    Application.Current?.RaiseSessionEnding(args);
                    // Return 0 to prevent, non-zero to allow
                    return args.Cancel ? nint.Zero : 1;
                }

                case WM_ENDSESSION:
                    if (wParam != nint.Zero)
                    {
                        // Session is definitely ending — close gracefully
                        window.Close();
                    }
                    return nint.Zero;
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
        _inputDispatcher.HandleMouseLeave();
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

        var previousWidth = Width;
        var previousHeight = Height;

        // Convert physical pixels to DIPs for layout
        Width = physicalWidth / _dpiScale;
        Height = physicalHeight / _dpiScale;

        // Always use WM_SIZE client dimensions as the single source of truth.
        // This keeps layout and swapchain size stable during drag resize.
        TryResizeRenderTarget(physicalWidth, physicalHeight, "OnSizeChanged");

        // After swap chain resize, DXGI discards all buffer contents.
        // Must request full invalidation so RenderFrame takes the full-render path
        // instead of partial dirty-rect rendering (which would leave stale/black areas).
        RequestFullInvalidation();
        InvalidateMeasure();

        bool widthChanged = previousWidth != Width;
        bool heightChanged = previousHeight != Height;
        if (widthChanged || heightChanged)
        {
            OnSizeChanged(new SizeChangedEventArgs(
                new SizeChangedInfo(this, new Size(previousWidth, previousHeight), widthChanged, heightChanged)));
        }
    }

    // ── Swap-chain resize serialization (0xC000041D fix) ────────────────────
    // Root cause of the "resize too fast" crash: during a drag-resize the OS
    // delivers WM_SIZE / WM_WINDOWPOSCHANGED as *sent* messages that reenter
    // this WndProc through KiUserCallbackDispatcher — including while we are in
    // the middle of a RenderFrame (between BeginDraw and EndDraw). Resizing the
    // swap chain there makes the native backend AbortFrame()+ResizeBuffers() out
    // from under the still-running managed frame, which then keeps issuing draw
    // commands into freed back buffers → AccessViolation. An AVE is a corrupted-
    // state exception that NEITHER the inner nor the outer WndProc catch(Exception)
    // can intercept (the CLR does not deliver CSEs to managed handlers by
    // default), so it escapes the user callback and Windows raises
    // STATUS_FATAL_USER_CALLBACK_EXCEPTION (0xC000041D).
    //
    // Fix: never touch the swap chain while a frame is in flight. A resize that
    // arrives at an unsafe moment is stashed and applied at the next safe point
    // (top of RenderFrame, before BeginDraw — see FlushPendingRenderTargetResize).
    private bool _resizeInProgress;
    private bool _hasPendingResize;
    private int _pendingResizeWidth;
    private int _pendingResizeHeight;

    private void TryResizeRenderTarget(int physicalWidth, int physicalHeight, string stage)
    {
        if (physicalWidth <= 0 || physicalHeight <= 0)
            return;

        // Defer if it is unsafe to touch the swap chain right now:
        //   • inside RenderFrame (a command list may be open), or
        //   • the render target is mid BeginDraw..EndDraw, or
        //   • another resize's native call is already in flight (reentrancy).
        // Stash the LATEST requested size; a scheduled frame flushes it safely.
        var rt = RenderTarget;
        if (_resizeInProgress || HasRenderFlag(RenderFlag_Rendering) || (rt != null && rt.IsDrawing))
        {
            if (ResizeTraceEnabled) Console.Error.WriteLine($"[resize-trace] DEFER {physicalWidth}x{physicalHeight} stage={stage} inProg={_resizeInProgress} rendering={HasRenderFlag(RenderFlag_Rendering)} drawing={rt?.IsDrawing}");
            _pendingResizeWidth = physicalWidth;
            _pendingResizeHeight = physicalHeight;
            _hasPendingResize = true;
            // Ensure a frame is scheduled to apply the pending size and repaint
            // at the new dimensions even if the caller does not invalidate.
            RequestFullInvalidation();
            return;
        }

        if (ResizeTraceEnabled) Console.Error.WriteLine($"[resize-trace] APPLY-DIRECT {physicalWidth}x{physicalHeight} stage={stage}");
        ApplyRenderTargetResize(physicalWidth, physicalHeight, stage);

        // A resize may have been deferred while we held _resizeInProgress above
        // (native ResizeBuffers can pump a reentrant WM_SIZE). Drain the latest
        // one now that the swap chain is idle again.
        FlushPendingRenderTargetResize();
    }

    // Applies a resize that was deferred because it arrived mid-frame. MUST only
    // be called from a safe point — never between BeginDraw and EndDraw. Called
    // by RenderFrame just before it begins drawing.
    // 临时诊断（JALIUM_RESIZE_TRACE=1）：定位"拖拽中交换链不跟新尺寸"的断点。
    private static readonly bool ResizeTraceEnabled = IsEnvironmentSwitchEnabled("JALIUM_RESIZE_TRACE");

    // 临时诊断（JALIUM_PACING_TRACE=1）：定位"extPacing=1 但 Present 仍阻塞"
    // 的调度旁路——打印 pacing 激活分支与每次 BeginDraw 时的调度状态。
    private static readonly bool PacingTraceEnabled = IsEnvironmentSwitchEnabled("JALIUM_PACING_TRACE");
    private long _pacingTraceCount;

    private void FlushPendingRenderTargetResize()
    {
        if (!_hasPendingResize || _resizeInProgress)
            return;

        int w = _pendingResizeWidth;
        int h = _pendingResizeHeight;
        _hasPendingResize = false;
        if (ResizeTraceEnabled) Console.Error.WriteLine($"[resize-trace] FLUSH {w}x{h}");
        ApplyRenderTargetResize(w, h, "PendingResize");

        // ResizeBuffers discarded the back-buffer contents — force a full repaint
        // at the new size so no stale/black pixels survive.
        RequestFullInvalidation();
    }

    private void ApplyRenderTargetResize(int physicalWidth, int physicalHeight, string stage)
    {
        var renderTarget = RenderTarget;
        if (renderTarget == null || !renderTarget.IsValid)
        {
            // Render target not yet created (e.g., first RESIZE event on Android arrives
            // before EnsureRenderTarget succeeded). Try to create it now that we have
            // valid dimensions — Width/Height are already updated by the caller.
            EnsureRenderTarget();
            renderTarget = RenderTarget;
            if (renderTarget == null || !renderTarget.IsValid)
                return;
        }

        // Reentrancy gate for the actual native call. While this is set, both
        // TryResizeRenderTarget and RenderFrame defer rather than touch the swap
        // chain — see their guards.
        _resizeInProgress = true;

        // Park the render thread before ResizeBuffers — it must NOT be mid-
        // BeginDraw..EndDraw when DXGI discards/recreates the back buffers.
        // (No-op when the render thread is disabled, which is the default.)
        // FIX #5: if it didn't park in time (a hung present), do NOT ResizeBuffers
        // under an in-flight frame — skip this resize and let a later tick retry.
        if (!RequestRenderThreadIdle())
        {
            ResumeRenderThread();
            _resizeInProgress = false;
            // "Let a later tick retry" must actually arrange that retry: re-stash
            // the size (FlushPendingRenderTargetResize cleared _hasPendingResize
            // before calling us) and schedule a frame — otherwise the final
            // EXITSIZEMOVE resize can be silently dropped and the swap chain
            // stays at the stale size until the next external invalidation.
            _pendingResizeWidth = physicalWidth;
            _pendingResizeHeight = physicalHeight;
            _hasPendingResize = true;
            RequestFullInvalidation();
            return;
        }
        try
        {
            // Defensive: if a draw session is somehow still open, close it before
            // touching the swap chain. The guards above should prevent this, but a
            // half-open session here would corrupt the resize.
            if (renderTarget.IsDrawing)
            {
                try { _ = renderTarget.TryEndDraw(); } catch { /* best effort */ }
            }

            _debugHud.OnResize();
            if (ResizeTraceEnabled) Console.Error.WriteLine($"[resize-trace] NATIVE-RESIZE {physicalWidth}x{physicalHeight} stage={stage}");
            var resizeResult = renderTarget.Resize(physicalWidth, physicalHeight);
            if (resizeResult == JaliumResult.Busy)
            {
                // Native refused: a command list is still open and references the
                // back buffers (a cross-thread render in flight, or a frame left
                // open). Freeing them now would be the #921 use-after-free. Re-stash
                // and retry at the next safe point — FlushPendingRenderTargetResize
                // runs at the top of RenderFrame, before BeginDraw, when the list is
                // provably closed. Mirrors the FIX #5 park-failure re-stash. The
                // finally below still runs (ResumeRenderThread + _resizeInProgress
                // = false), so the swap chain is never left wedged, and no present
                // credit was consumed (no ResizeBuffers ran) so none is returned.
                if (ResizeTraceEnabled) Console.Error.WriteLine($"[resize-trace] NATIVE-RESIZE BUSY→deferred {physicalWidth}x{physicalHeight} stage={stage}");
                _pendingResizeWidth = physicalWidth;
                _pendingResizeHeight = physicalHeight;
                _hasPendingResize = true;
                RequestFullInvalidation();
                return;
            }
            if (ResizeTraceEnabled) Console.Error.WriteLine($"[resize-trace] NATIVE-RESIZE OK rt={renderTarget.Width}x{renderTarget.Height}");
            // ResizeBuffers leaves the frame-latency waitable's signal count in
            // an unspecified state (in-flight presents were discarded). Reset
            // the present credit optimistically: the resized swap chain has no
            // queued frames, so an immediate BeginDraw is always safe — worst
            // case Present briefly queues inside DXGI instead of deadlocking
            // the event-driven scheduler on a signal that never comes.
            ReturnSwapCreditAfterFailedPresent();
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
        finally
        {
            ResumeRenderThread();
            _resizeInProgress = false;
        }
    }

    private static bool IsRecoverableRenderPipelineException(RenderPipelineException exception)
        => exception.Result is JaliumResult.DeviceLost
            or JaliumResult.InvalidState
            or JaliumResult.ResourceCreationFailed
            || (exception.Result == JaliumResult.Unknown &&
                string.Equals(exception.Stage, "Create", StringComparison.OrdinalIgnoreCase));

    private static bool IsRecoverableRenderPipelineFailure(JaliumResult result, string stage)
        => result is JaliumResult.DeviceLost
            or JaliumResult.InvalidState
            or JaliumResult.ResourceCreationFailed
            || (result == JaliumResult.Unknown &&
                string.Equals(stage, "Create", StringComparison.OrdinalIgnoreCase));

    private bool TryRecoverFromRenderPipelineFailure(RenderPipelineException exception, string stage)
    {
        if (!IsRecoverableRenderPipelineException(exception) ||
            Handle == nint.Zero ||
            _isClosing ||
            _renderRecoveryInProgress)
        {
            return false;
        }

        _renderRecoveryInProgress = true;
        // A recovered target must start with a fresh render worker and drawing
        // context. Joining the old worker is stronger than merely parking it:
        // after this point it cannot resume and touch either the retired target
        // or a newly-created drawing context.
        if (!StopRenderThread())
        {
            _renderRecoveryInProgress = false;
            ScheduleRenderRecoveryRetry(escalateBackoff: false);
            return false;
        }

        bool recovered = false;
        try
        {
            // A capture recorded before the device-loss boundary can contain
            // retained-layer/effect commands whose native handles belong to the
            // failed context. The render thread is parked and this callback runs
            // on the publishing (UI) thread, so discard that queued capture before
            // retiring the old target. Recovery already requests a full repaint;
            // the next capture is rebuilt entirely against the replacement device.
            lock (_rtChannelLock) { _rtPendingFrame = null; }

            RenderBackend failedBackend = RenderTarget?.Backend ?? RenderBackend.Auto;
            if (failedBackend == RenderBackend.Auto &&
                Enum.TryParse<RenderBackend>(exception.Backend, ignoreCase: true, out var parsedBackend))
            {
                failedBackend = parsedBackend;
            }

            // Retained GPU layers bake textures on the failed device. Evict the
            // whole tree's handles and destroy them through the OLD render
            // target NOW, while its backend graveyard still exists (a removed
            // device's fence reads completed, so reclamation never blocks).
            // Skipping this would composite stale-device textures into the new
            // device's first frame (the original GPU-switch AV signature), and
            // draining later would destroy the handles on the NEW target whose
            // backend pointer they don't belong to.
            Visual.ReleaseRetainedLayersRecursive(this);
            ReleaseDrawingContextBeforeRenderTargetDisposal(clearAllCaches: true);

            // The registered swap wait targets the waitable HANDLE that
            // RenderTarget.Dispose is about to close — unregister first
            // (waiting on a closed handle is undefined behaviour).
            StopExternalPresentPacing();
            RenderTarget?.Dispose();
            RenderTarget = null;

            if (exception.Result == JaliumResult.DeviceLost &&
                _consecutiveRecoverableRenderFailures >= DeviceLostBackendFallbackThreshold &&
                failedBackend != RenderBackend.Auto)
            {
                if (!TryAdvanceRenderBackendFallback(failedBackend) &&
                    failedBackend == RenderBackend.D3D12)
                {
                    EnableD3D12WarpFallback();
                }
            }

            bool forceNewContext = exception.Result == JaliumResult.DeviceLost ||
                string.Equals(exception.Stage, "Create", StringComparison.OrdinalIgnoreCase);
            EnsureRenderTarget(forceNewContext: forceNewContext);
            if (RenderTarget == null || !RenderTarget.IsValid)
            {
                return false;
            }

            lock (_dirtyLock)
            {
                _dirtyElements.Clear();
                _dirtyFreeRects.Clear();
                MarkFullInvalidationLocked();
            }

            InvalidateMeasure();
            ScheduleRenderRecoveryRetry(escalateBackoff: false);
            recovered = true;
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
            if (recovered)
            {
                StartRenderThreadIfSupported();
            }
        }
    }

    private bool TryRecoverFromRenderPipelineFailure(JaliumResult result, string stage)
    {
        if (!IsRecoverableRenderPipelineFailure(result, stage) ||
            Handle == nint.Zero ||
            _isClosing ||
            _renderRecoveryInProgress)
        {
            return false;
        }

        _renderRecoveryInProgress = true;
        // Same stop-and-join ownership boundary as the exception overload.
        if (!StopRenderThread())
        {
            _renderRecoveryInProgress = false;
            ScheduleRenderRecoveryRetry(escalateBackoff: false);
            return false;
        }

        bool recovered = false;
        try
        {
            // See the exception overload above: no pre-recovery command stream
            // may be replayed after its creating device has been retired.
            lock (_rtChannelLock) { _rtPendingFrame = null; }

            RenderBackend failedBackend = RenderTarget?.Backend ?? RenderBackend.Auto;

            // Evict + drain retained layers on the OLD target before disposing
            // it (same rule as the exception-typed recovery overload above).
            Visual.ReleaseRetainedLayersRecursive(this);
            ReleaseDrawingContextBeforeRenderTargetDisposal(clearAllCaches: true);

            // Unregister the swap wait before Dispose closes its HANDLE
            // (same rule as the exception-typed recovery overload above).
            StopExternalPresentPacing();
            RenderTarget?.Dispose();
            RenderTarget = null;

            if (result == JaliumResult.DeviceLost &&
                _consecutiveRecoverableRenderFailures >= DeviceLostBackendFallbackThreshold &&
                failedBackend != RenderBackend.Auto)
            {
                if (!TryAdvanceRenderBackendFallback(failedBackend) &&
                    failedBackend == RenderBackend.D3D12)
                {
                    EnableD3D12WarpFallback();
                }
            }

            bool forceNewContext = result == JaliumResult.DeviceLost ||
                string.Equals(stage, "Create", StringComparison.OrdinalIgnoreCase);
            EnsureRenderTarget(forceNewContext: forceNewContext);
            if (RenderTarget == null || !RenderTarget.IsValid)
            {
                return false;
            }

            lock (_dirtyLock)
            {
                _dirtyElements.Clear();
                _dirtyFreeRects.Clear();
                MarkFullInvalidationLocked();
            }

            InvalidateMeasure();
            ScheduleRenderRecoveryRetry(escalateBackoff: false);
            recovered = true;
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
            if (recovered)
            {
                StartRenderThreadIfSupported();
            }
        }
    }

    private void ScheduleRenderAfterRecovery()
    {
        if (Handle == nint.Zero || _dispatcher == null)
        {
            return;
        }

        if (TrySetRenderFlag(RenderFlag_Scheduled))
        {
            _dispatcher.BeginInvokeCritical(ProcessRender);
        }
    }

    /// <summary>
    /// Replays a frame that the native backend intentionally abandoned while its
    /// device is still healthy. InvalidState at End means the command list/frame
    /// was already closed or aborted (for example by a serialized resize); it is
    /// not evidence that the render target or device must be rebuilt.
    /// </summary>
    private void ScheduleDroppedFrameRetry()
    {
        RequestFullInvalidation();
        ScheduleRenderAfterRecovery();
    }

    private void MarkRecoverableRenderFailure()
    {
        _consecutiveRecoverableRenderFailures = Math.Min(_consecutiveRecoverableRenderFailures + 1, 8);
        if (_consecutiveRecoverableRenderFailures <= 1)
        {
            _renderRecoveryRetryDelayMs = RenderRecoveryRetryInitialDelayMs;
            return;
        }

        _renderRecoveryRetryDelayMs = Math.Min(RenderRecoveryRetryMaxDelayMs, _renderRecoveryRetryDelayMs * 2);
    }

    private void ResetRenderRecoveryBackoff()
    {
        _consecutiveRecoverableRenderFailures = 0;
        _renderRecoveryRetryDelayMs = RenderRecoveryRetryInitialDelayMs;
        _renderRecoveryRetryTimer?.Stop();
    }

    private bool HandleRecoverableRenderPipelineFailure(RenderPipelineException exception, string stage)
    {
        if (!IsRecoverableRenderPipelineException(exception))
        {
            return false;
        }

        // The failed frame may have consumed a present credit at BeginDraw
        // without reaching a successful Present. This is the single funnel all
        // recoverable mid-frame failures pass through (the RenderFrame inner
        // catches return via here without touching the outer catch), so return
        // the credit here or the event-driven scheduler deadlocks when the
        // recovery is deferred (_renderRecoveryInProgress / park failure) and
        // no RT rebuild resets pacing. Over-return is harmless: the flag
        // saturates at 1 and a spurious credit degrades to one bounded DXGI
        // queue wait inside Present.
        ReturnSwapCreditAfterFailedPresent();

        // Preserve a full repaint request when a frame dies after we already
        // committed dirty state, so the eventual retry never resumes from a
        // partially-rendered dirty set.
        lock (_dirtyLock)
        {
            MarkFullInvalidationLocked();
        }

        MarkRecoverableRenderFailure();

        if (TryRecoverFromRenderPipelineFailure(exception, stage))
        {
            return true;
        }

        ScheduleRenderRecoveryRetry(escalateBackoff: false);
        return true;
    }

    // Last-frame snapshot of the unified native path-stats struct. We diff
    // each ulong field against the next frame to publish per-frame deltas
    // to DevTools. The whole struct is grabbed in one P/Invoke so producers
    // (D3D12 / Vulkan) and the reader (managed) see a single atomic point.
    private Interop.NativeMethods.JaliumPathStats _prevPathStats;

    private void PublishPathCacheStatsFromNative()
    {
        Interop.NativeMethods.JaliumPathStats cur = default;
        try
        {
            unsafe
            {
                Interop.NativeMethods.QueryPathStats(&cur);
            }
        }
        catch
        {
            // Backend not loaded / DLL missing — skip stats this frame.
            return;
        }
        var prev = _prevPathStats;
        _prevPathStats = cur;

        Jalium.UI.Diagnostics.RenderDiagnostics.PublishPathCacheStats(
            new Jalium.UI.Diagnostics.RenderDiagnostics.PathCacheFrameStats
            {
                Timestamp            = DateTime.Now,
                StrokeHits           = (long)(cur.StrokeHits           - prev.StrokeHits),
                StrokeMisses         = (long)(cur.StrokeMisses         - prev.StrokeMisses),
                FillHits             = (long)(cur.FillHits             - prev.FillHits),
                FillMisses           = (long)(cur.FillMisses           - prev.FillMisses),
                StrokeRectsTotal     = (long)(cur.StrokeRects          - prev.StrokeRects),
                FillRectsTotal       = (long)(cur.FillRects            - prev.FillRects),
                GeometryHits         = (long)(cur.GeometryHits         - prev.GeometryHits),
                GeometryMisses       = (long)(cur.GeometryMisses       - prev.GeometryMisses),
                FlattenNs            = (long)(cur.FlattenNs            - prev.FlattenNs),
                FlattenInputSegments = (long)(cur.FlattenInputSegments - prev.FlattenInputSegments),
                FlattenOutputVerts   = (long)(cur.FlattenOutputVerts   - prev.FlattenOutputVerts),
                TriangulateNs        = (long)(cur.TriangulateNs        - prev.TriangulateNs),
                TriangulateOk        = (long)(cur.TriangulateOk        - prev.TriangulateOk),
                TriangulateFail      = (long)(cur.TriangulateFail      - prev.TriangulateFail),
                CacheEvictions       = (long)(cur.CacheEvictions       - prev.CacheEvictions),
            });

        // Bitmap upload telemetry — same diff-from-last-frame pattern, now
        // through the unified core ABI (jalium_query_bitmap_stats). Single
        // struct grab so all 9 counters move in lockstep.
        Interop.NativeMethods.JaliumBitmapStats curBmp = default;
        try
        {
            unsafe
            {
                Interop.NativeMethods.QueryBitmapStats(&curBmp);
            }
        }
        catch
        {
            return;
        }
        var prevBmp = _prevBitmapStats;
        _prevBitmapStats = curBmp;

        // Managed-side downscale cache evictions ride alongside native cache
        // evictions in the unified CacheEvictions field — DevTools doesn't
        // need to distinguish the source.
        long managedDownscaleEvictions = Jalium.UI.Diagnostics.RenderDiagnostics.BitmapDownscaleEvictionsTotal;
        long deltaManagedEvictions = managedDownscaleEvictions - _prevBitmapDownscaleEvictions;
        _prevBitmapDownscaleEvictions = managedDownscaleEvictions;

        Jalium.UI.Diagnostics.RenderDiagnostics.PublishBitmapUploadStats(
            new Jalium.UI.Diagnostics.RenderDiagnostics.BitmapUploadFrameStats
            {
                Timestamp           = DateTime.Now,
                UploadCount         = (long)(curBmp.UploadCount         - prevBmp.UploadCount),
                UploadBytes         = (long)(curBmp.UploadBytes         - prevBmp.UploadBytes),
                FastPathHits        = (long)(curBmp.FastPathHits        - prevBmp.FastPathHits),
                DynamicReuses       = (long)(curBmp.DynamicReuses       - prevBmp.DynamicReuses),
                MemcmpShortCircuits = (long)(curBmp.MemcmpShortCircuits - prevBmp.MemcmpShortCircuits),
                GpuResidentBytes    = curBmp.GpuResidentBytes - prevBmp.GpuResidentBytes,
                AtlasHits           = (long)(curBmp.AtlasHits           - prevBmp.AtlasHits),
                CacheEvictions      = (long)(curBmp.CacheEvictions      - prevBmp.CacheEvictions)
                                       + deltaManagedEvictions,
            });
    }

    // Unified bitmap stats last-frame snapshot — single struct (the C ABI
    // returns the whole thing in one call so producers and reader observe
    // a consistent atomic point).
    private Interop.NativeMethods.JaliumBitmapStats _prevBitmapStats;
    private long _prevBitmapDownscaleEvictions;

    // Unified text stats last-frame snapshot.  Same pattern as path / bitmap:
    // diff against the next call to publish per-frame deltas.
    private Interop.NativeMethods.JaliumTextStats _prevTextStats;

    private void PublishTextCacheStatsFromNative()
    {
        Interop.NativeMethods.JaliumTextStats cur = default;
        try
        {
            unsafe
            {
                Interop.NativeMethods.QueryTextStats(&cur);
            }
        }
        catch
        {
            return;
        }
        var prev = _prevTextStats;
        _prevTextStats = cur;

        Jalium.UI.Diagnostics.RenderDiagnostics.PublishTextCacheStats(
            new Jalium.UI.Diagnostics.RenderDiagnostics.TextCacheFrameStats
            {
                Timestamp          = DateTime.Now,
                LayoutHits         = (long)(cur.LayoutHits         - prev.LayoutHits),
                LayoutMisses       = (long)(cur.LayoutMisses       - prev.LayoutMisses),
                LayoutEvictions    = (long)(cur.LayoutEvictions    - prev.LayoutEvictions),
                InstanceHits       = (long)(cur.InstanceHits       - prev.InstanceHits),
                InstanceMisses     = (long)(cur.InstanceMisses     - prev.InstanceMisses),
                InstanceEvictions  = (long)(cur.InstanceEvictions  - prev.InstanceEvictions),
                GlyphRasterHits    = (long)(cur.GlyphRasterHits    - prev.GlyphRasterHits),
                GlyphRasterMisses  = (long)(cur.GlyphRasterMisses  - prev.GlyphRasterMisses),
                AtlasResets        = (long)(cur.AtlasResets        - prev.AtlasResets),
                DrawTextCalls      = (long)(cur.DrawTextCalls      - prev.DrawTextCalls),
                EmittedGlyphs      = (long)(cur.EmittedGlyphs      - prev.EmittedGlyphs),
                EmittedDecorations = (long)(cur.EmittedDecorations - prev.EmittedDecorations),
            });
    }

    // Retained-cache last-frame snapshots — Visual.s_retainedCache* are global
    // counters incremented by every Visual that runs RenderDirect this frame.
    private long _prevRetainedRecords;
    private long _prevRetainedReplays;
    private long _prevRetainedBypasses;

    private void PublishRetainedCacheStatsFromManaged()
    {
        long records  = Jalium.UI.Visual.RetainedCacheRecordsTotal;
        long replays  = Jalium.UI.Visual.RetainedCacheReplaysTotal;
        long bypasses = Jalium.UI.Visual.RetainedCacheBypassesTotal;
        long deltaR   = records  - _prevRetainedRecords;
        long deltaRP  = replays  - _prevRetainedReplays;
        long deltaB   = bypasses - _prevRetainedBypasses;
        _prevRetainedRecords  = records;
        _prevRetainedReplays  = replays;
        _prevRetainedBypasses = bypasses;
        Jalium.UI.Diagnostics.RenderDiagnostics.PublishRetainedCacheStats(
            new Jalium.UI.Diagnostics.RenderDiagnostics.RetainedCacheFrameStats
            {
                Timestamp = DateTime.Now,
                Records  = deltaR,
                Replays  = deltaRP,
                Bypasses = deltaB,
            });
    }

    private bool CompleteEndDrawOrHandleFailure()
    {
        var renderTarget = RenderTarget;
        if (renderTarget == null || !renderTarget.IsValid)
        {
            return false;
        }

        JaliumResult endResult = renderTarget.TryEndDraw();
        if (endResult == JaliumResult.Ok)
        {
            _debugHud.OnEndDraw();
            // This frame's BeginDraw blocking wait (swap-chain frame-latency
            // waitable + GPU fence) in ns — peeled out of the "BeginDraw"
            // DRAW-API row below so a multi-ms wait doesn't masquerade as CPU
            // work. Filled inside the gpuStats block; stays 0 when unavailable.
            long beginBlockingWaitNs = 0;
            // This frame's Present blocking time in ns — peeled out of the
            // "EndDraw" DRAW-API row the same way. Under a slow compositor a
            // vsync-aligned Present stalls for the whole DWM buffer-retire
            // interval; without the split that stall reads as EndDraw CPU work.
            long presentBlockNs = 0;
            // GPU resource snapshot (glyph atlas, path cache, textures) for
            // the Perf tab. Best-effort — a backend that hasn't implemented
            // the query just leaves LatestGpuSnapshot unchanged.
            if (renderTarget.TryQueryGpuStats(out var gpuStats))
            {
                Jalium.UI.Diagnostics.RenderDiagnostics.PublishGpuSnapshot(
                    gpuStats.GlyphSlotsUsed, gpuStats.GlyphSlotsTotal, gpuStats.GlyphBytes,
                    gpuStats.PathEntries, gpuStats.PathBytes,
                    gpuStats.TextureCount, gpuStats.TextureBytes);
                // Frame-pacing: roll up the managed BeginDraw attempt counters
                // (incremented inside RenderTarget.TryBeginDraw) with the
                // native backend's fence-wait + GPU work timings so DevTools
                // shows one cohesive "Frame pacing" block per frame.
                Jalium.UI.Diagnostics.RenderDiagnostics.PublishFramePacing(
                    gpuStats.FrameGpuWaitNs,
                    gpuStats.SwapBufferCount,
                    gpuStats.LastFramePresentToReadyNs,
                    gpuStats.FrameWaitableWaitNs);
                // Cache the live back-buffer count so the FLIP_SEQUENTIAL follow-up
                // flush (HandlePresentedFrameFlush) covers every buffer even under a
                // JALIUM_SWAPCHAIN_BUFFERS override (default kDefaultSwapBufferCount=2).
                if (gpuStats.SwapBufferCount > 0) _lastSwapBufferCount = gpuStats.SwapBufferCount;
                // Same two blocking waits, used to split the BeginDraw API row.
                beginBlockingWaitNs = gpuStats.FrameWaitableWaitNs + gpuStats.FrameGpuWaitNs;
                presentBlockNs = gpuStats.PresentBlockNs;
            }
            // GPU breakdown via hardware timestamp queries — gated behind the
            // same ApiStatsEnabled flag the path/bitmap/retained queries use.
            // Backends without timestamp support return NOT_SUPPORTED here and
            // DevTools renders a "—" placeholder.
            // Diagnostic: a hover trace turns the stats path on so the GPU timing is
            // collected exactly like the DevTools Perf tab (proven safe). REMOVE with
            // the rest of the HoverTrace scaffolding after the investigation.
            if (Jalium.UI.Diagnostics.HoverTrace.Enabled)
                Jalium.UI.Diagnostics.RenderDiagnostics.ApiStatsEnabled = true;
            if (Jalium.UI.Diagnostics.RenderDiagnostics.ApiStatsEnabled &&
                renderTarget.TryQueryGpuTiming(out var gpuTiming))
            {
                Jalium.UI.Diagnostics.RenderDiagnostics.PublishGpuTiming(
                    gpuTiming.TimingValid,
                    gpuTiming.TotalGpuNs,
                    gpuTiming.SdfRectNs, gpuTiming.TextNs, gpuTiming.BitmapNs, gpuTiming.PathNs,
                    gpuTiming.BackdropNs, gpuTiming.LiquidGlassNs, gpuTiming.OtherNs,
                    gpuTiming.BatchCount);
                if (Jalium.UI.Diagnostics.HoverTrace.Enabled && gpuTiming.TimingValid)
                {
                    Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_GPU_TOTAL_US, gpuTiming.TotalGpuNs / 1000);
                    Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_GPU_OTHER_US, gpuTiming.OtherNs / 1000);
                    Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_GPU_PATH_US, gpuTiming.PathNs / 1000);
                    Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_GPU_SDF_US, gpuTiming.SdfRectNs / 1000);
                    Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_GPU_TEXT_US, gpuTiming.TextNs / 1000);
                    Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_GPU_BITMAP_US, gpuTiming.BitmapNs / 1000);
                }
            }
            // Per-frame draw-API call counters → DevTools Perf tab. Only
            // publishes when ApiStatsEnabled (set by the Perf tab while it's
            // visible) so production frames pay zero overhead.
            Jalium.UI.Diagnostics.RenderDiagnostics.PublishAndResetApiStats(beginBlockingWaitNs, presentBlockNs);
            // Native path-rasterization cache hit/miss counters (D3D12 Impeller).
            // Cumulative atomics in native — diff against the previous frame's
            // values to get per-frame deltas.
            if (Jalium.UI.Diagnostics.RenderDiagnostics.ApiStatsEnabled)
            {
                PublishPathCacheStatsFromNative();
                PublishTextCacheStatsFromNative();
                PublishRetainedCacheStatsFromManaged();
            }
            if (ResizeTraceEnabled) Console.Error.WriteLine($"[resize-trace] PRESENTED rt={renderTarget.Width}x{renderTarget.Height} win={Width:F0}x{Height:F0}");
            Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.PRESENT);
            _lastRenderTicks = Environment.TickCount64;
            ResetRenderRecoveryBackoff();
            // Pre-arm the swap wait right after a successful present: the DWM
            // retire signal then converts to a credit as soon as it lands, so a
            // continuous animation's next tick finds the credit already in hand
            // (inline fast path) instead of paying a miss → register → callback
            // → dispatch round-trip every frame. No-op when pacing is inactive;
            // a spare armed wait is harmless (the callback just banks the credit).
            if (_swapPacingActive) EnsureSwapWaitRegistered();
            return true;
        }

        // The frame consumed a present credit at BeginDraw but never reached a
        // successful Present — return the credit or the event-driven scheduler
        // waits forever for a DWM retire that will never come. (Recoverable and
        // throwing paths both end without a present; the RT-recreation path
        // also resets pacing state, making a double return harmless.)
        ReturnSwapCreditAfterFailedPresent();

        if (endResult == JaliumResult.PresentFailed)
        {
            // Transient Present failure (mode change, resize race) on a HEALTHY
            // device — native reports it (instead of swallowing it as Ok) only
            // under external pacing, precisely so the credit return above runs.
            // The device needs no recovery: repaint fully (the dropped frame
            // breaks the FLIP_SEQUENTIAL dirty-rect chain's assumption that the
            // previous frame reached the screen) and schedule a retry. No
            // RT rebuild, no backoff escalation, no exception — any of those
            // would turn a one-frame hiccup into a visible stall.
            lock (_dirtyLock)
            {
                MarkFullInvalidationLocked();
            }

            ScheduleRenderRecoveryRetry(escalateBackoff: false);
            return false;
        }

        if (endResult == JaliumResult.InvalidState)
        {
            // The native frame has already been aborted/closed. Preserve the
            // same render target and retry a full frame; recreating it here turns
            // a transient command-state collision into an observable recovery.
            ScheduleDroppedFrameRetry();
            return false;
        }

        if (IsRecoverableRenderPipelineFailure(endResult, "End"))
        {
            lock (_dirtyLock)
            {
                MarkFullInvalidationLocked();
            }

            MarkRecoverableRenderFailure();

            if (!TryRecoverFromRenderPipelineFailure(endResult, "End"))
            {
                ScheduleRenderRecoveryRetry(escalateBackoff: false);
            }

            return false;
        }

        throw new RenderPipelineException(
            stage: "End",
            result: endResult,
            resultCode: (int)endResult,
            hwnd: Handle,
            width: renderTarget.Width,
            height: renderTarget.Height,
            dpiX: (float)(_dpiScale * 96.0),
            dpiY: (float)(_dpiScale * 96.0),
            backend: renderTarget.Backend.ToString());
    }

    private void ScheduleRenderRecoveryRetry(bool escalateBackoff = true)
    {
        if (Handle == nint.Zero || _dispatcher == null || _isClosing)
        {
            return;
        }

        if (escalateBackoff)
        {
            MarkRecoverableRenderFailure();
        }

        _renderRecoveryRetryTimer ??= CreateRenderRecoveryRetryTimer();
        _renderRecoveryRetryTimer.Interval = TimeSpan.FromMilliseconds(_renderRecoveryRetryDelayMs);
        if (!_renderRecoveryRetryTimer.IsEnabled)
        {
            _renderRecoveryRetryTimer.Start();
        }
    }

    private DispatcherTimer CreateRenderRecoveryRetryTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_renderRecoveryRetryDelayMs)
        };
        timer.Tick += OnRenderRecoveryRetryTimerTick;
        return timer;
    }

    private void OnRenderRecoveryRetryTimerTick(object? sender, EventArgs e)
    {
        _renderRecoveryRetryTimer?.Stop();

        if (Handle == nint.Zero || _isClosing)
        {
            return;
        }

        if (TrySetRenderFlag(RenderFlag_Scheduled))
        {
            _dispatcher?.BeginInvokeCritical(ProcessRender);
        }
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

    private RenderTargetDrawingContext? _drawingContext;
    private UIElement? _lastMouseOverElement;
    // Per-frame hit-test memoize. `_hitMemoLayoutGeneration` snapshots
    // LayoutManager.Generation at the time of the cached hit; any later
    // measure/arrange (including ScrollViewer scrolling, animation, theme
    // changes, etc.) bumps Generation and invalidates the memo automatically.
    // Cache only hits for the exact same (point, generation) pair — same frame,
    // same layout, same coordinate — so it removes redundant traversals from
    // mouse-down/up/wheel at the same position without any manual invalidation.
    private Point _hitMemoPoint;
    private UIElement? _hitMemoElement;
    private long _hitMemoLayoutGeneration = -1;
    private readonly List<UIElement> _mousePressedChain = [];
    private readonly List<UIElement> _keyboardPressedChain = [];
    private nint _detachedImeContext;
    private bool _imeContextDetached;
    private bool _keyboardPressActive;
    private const int EscapeReactivateSuppressionMs = 250;

    /// <summary>
    /// WM_PAINT handler. Used for OS-initiated repaints (window uncovered, initial show, resize).
    /// Validates the update region via BeginPaint/EndPaint and delegates to RenderFrame.
    /// </summary>
    private void OnPaint()
    { _debugHud.OnPaint();
        PAINTSTRUCT ps = new();
        _ = BeginPaint(Handle, out ps);
        RenderFrame();
        EndPaint(Handle, ref ps);
    }

    /// <summary>
    /// Processes a scheduled render from the Dispatcher queue.
    /// This is the primary render path 鈥?called via Dispatcher.BeginInvokeCritical
    /// after InvalidateMeasure/InvalidateArrange/InvalidateVisual.
    ///
    /// WPF-style: rendering is a Dispatcher operation, not WM_PAINT.
    /// When DispatcherTimer ticks (animations) call BeginInvoke(RaiseTick),
    /// the tick handler invalidates elements which calls BeginInvokeCritical(ProcessRender).
    /// ProcessQueue drains all items in FIFO order, so ProcessRender runs
    /// immediately after all ticks in the same batch 鈥?no WM_PAINT starvation.
    /// </summary>
    private void ProcessRender()
    { _debugHud.OnProcessRender();
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.PR);
        ClearRenderFlag(RenderFlag_Scheduled);
        if (_isClosing || Handle == nint.Zero) return;

        // Dispose any pending throttle timer from a previous rate-limit cycle.
        var throttleTimer = _renderThrottleTimer;
        _renderThrottleTimer = null;
        throttleTimer?.Dispose();

        // Skip the whole render pipeline while minimized — DWM is not picking
        // up presents anyway, so layout / dirty-region / present cost is pure
        // waste. Pending dirty rects stay in place and a full invalidation
        // runs on restore.
        if (WindowState == WindowState.Minimized) return;

        // No rate-limiting — render as fast as possible.
        RenderFrame();
    }

    private void ScheduleDeferredRender(int delayMs)
    {
        if (_isClosing || Handle == nint.Zero || _dispatcher == null)
        {
            return;
        }

        // Timer-based retry — the proven path. Letting the UI thread return
        // to the dispatcher between BeginDraw attempts is the reason input
        // and animation stay responsive while waiting on a busy GPU. An
        // earlier attempt to route this through a waitable-driven pacer
        // thread (see project memory v5) backfired: the fence wait inside
        // BeginDraw still blocks the UI thread on iGPU for ~120 ms per
        // frame, so removing the retry just shifted that 120 ms from a
        // dispersed wait to a synchronous one — same wall clock, much
        // worse perceived responsiveness. The pacer code below is kept
        // behind an env-var gate for future experimentation.
        //
        // RenderFlag_Scheduled is claimed when the timer FIRES, not here: this
        // System.Threading.Timer runs at the ~15.6 ms OS resolution (the process
        // holds no timeBeginPeriod), so claiming the flag at arm time would block
        // every animation tick's InvalidateWindow for a random 0–15.6 ms slice
        // after each present — the direct cause of the visible present-cadence
        // stutter in continuous partial-present animations (DevTools tree
        // reveal). Claiming at fire time keeps real invalidations scheduling
        // immediately; if the fire loses the race (flag already set), a real
        // frame is already queued and this retry's purpose is served.
        var deferredTimer = new Timer(_ =>
        {
            if (_isClosing || Handle == nint.Zero)
            {
                return;
            }

            if (TrySetRenderFlag(RenderFlag_Scheduled))
            {
                _dispatcher?.BeginInvokeCritical(ProcessRender);
            }
        }, null, Math.Max(1, delayMs), Timeout.Infinite);

        var previousTimer = Interlocked.Exchange(ref _renderThrottleTimer, deferredTimer);
        previousTimer?.Dispose();
    }

    // ── Frame-latency pacer thread ──────────────────────────────────────
    // Blocks on the swap-chain frame-latency waitable HANDLE and pokes the
    // dispatcher to drive ProcessRender exactly when the swap chain can
    // accept another frame. This replaces the previous "fire-and-retry"
    // loop: when ScheduleDeferredRender used a 1 ms timer, every iteration
    // hit BeginDraw → waitable timeout → return INVALID_STATE → retry,
    // wasting ~100 ms per frame on iGPU. With the pacer driving the
    // schedule, BeginDraw runs exactly once per frame, on the right tick.
    //
    // Polling cadence: 16 ms (≈ 1 vsync) keeps the loop alive even when the
    // waitable never signals (window minimised etc.), so Stop() doesn't have
    // to set up a second wait handle.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObjectEx(nint hHandle, uint dwMilliseconds, [MarshalAs(UnmanagedType.Bool)] bool bAlertable);

    private const uint FRAME_PACER_POLL_MS = 16;
    private volatile int _framePacerStopFlag;

    // Pacer thread is opt-in via JALIUM_ENABLE_FRAME_PACER=1.  The default
    // path uses the timer-based retry in ScheduleDeferredRender, which keeps
    // input responsive on iGPU by letting the dispatcher run between
    // BeginDraw attempts. The pacer experiment is preserved so a future
    // change that also moves the fence wait off the UI thread can re-enable
    // it cleanly — see the v5 retrospective in the project memory.
    private static readonly bool EnableFramePacer = IsEnvironmentSwitchEnabled("JALIUM_ENABLE_FRAME_PACER");

    // Render-thread path (JALIUM_RENDER_THREAD=1), default OFF. Increment 1 (now):
    // record the whole visual tree into a self-contained Drawing on the UI thread,
    // then replay it into the live context on the SAME thread — proves the capture
    // is pixel-identical before Increment 2 moves the replay onto a render thread,
    // which is what finally takes the ~110ms iGPU present off the message pump.
    private static readonly bool EnableRenderThread = IsEnvironmentSwitchEnabled("JALIUM_RENDER_THREAD");

    /// <summary>
    /// Default path: directly walk the tree into the live context. Render-thread
    /// path (env-gated): record the whole frame into a self-contained Drawing then
    /// replay it into the live context (same thread for Increment 1). Falls back to
    /// direct render if the cache host doesn't support whole-frame capture.
    /// </summary>
    private void RenderTreeOrCapture(Jalium.UI.Interop.RenderTargetDrawingContext ctx)
    {
        if (!EnableRenderThread)
        {
            Render(ctx);
            return;
        }

        var host = Visual.RenderCacheHost;
        var recorder = host?.CreateFrameRecorder();
        if (host == null || recorder == null)
        {
            Render(ctx);
            return;
        }

        object drawing;
        try
        {
            Render(recorder);                          // walk tree → command list
        }
        finally
        {
            // Always exit the whole-frame recording scope, even if the tree walk
            // throws — otherwise s_wholeFrameRecording would leak true on this thread.
            drawing = host.FinishRecord(recorder);     // immutable Drawing
        }

        // schema-gap: if the capture hit content it can't represent (windowless
        // WebView punch, video surface, ink-layer blit, transition shader …),
        // discard it and direct-render so nothing is silently dropped.
        if (drawing is Jalium.UI.Media.Rendering.Drawing dr && !dr.IsFullyRecordable)
        {
            Render(ctx);
            return;
        }

        var savedOffset = ctx.Offset;
        ctx.Offset = Point.Zero;                       // whole-frame Drawing is absolute from 0
        host.Replay(drawing, ctx);                     // issue native draws
        ctx.Offset = savedOffset;
    }

    // ── Render thread (Increment 2) ─────────────────────────────────────
    // When EnableRenderThread is on AND the window is a non-composition HWND, the
    // GPU-blocking work (BeginDraw waitable+fence, Replay, EndDraw/Present) runs on
    // this dedicated thread instead of the UI/message-pump thread. The UI thread
    // records the whole frame into a Drawing and publishes it (latest-frame-wins
    // mailbox) then returns to GetMessage immediately — so the ~110ms iGPU present
    // no longer blocks input/animation. FULL render only in this first version
    // (no dirty-rect partial present): on iGPU the present cost is dominated by the
    // DWM flip release regardless of dirty area, so full-frame is the simpler, safe
    // path. DComp/composition windows fall back to the inline same-thread path.
    private sealed class FrameCapture
    {
        public object Drawing = null!;
        public RenderTarget Target = null!;
        public RenderContext? OwnerContext;
        public int ContextGeneration;
        public int LifecycleGeneration;
    }

    private Thread? _renderThread;
    private int _deferredRenderResourceReleaseScheduled;
    private volatile bool _renderThreadStop;
    private readonly object _rtChannelLock = new();
    private FrameCapture? _rtPendingFrame;
    private AutoResetEvent? _rtFrameAvailable;
    private volatile bool _rtPause;
    private ManualResetEventSlim? _rtIdle;
    private volatile bool _rtActive;
    private volatile bool _rtBusy;   // render thread is mid BeginDraw..EndDraw (back-pressure gate)
    private bool _renderThreadDisabledForSchemaGap;  // latched after un-recordable content; do not resurrect the render thread on RT recreate
    // Serializes every direct native draw session that can run outside the UI
    // render lifecycle. In particular, the pre-show background frame and the
    // dedicated render worker must never record into the same command list.
    private readonly object _renderTargetUseGate = new();

    private void StartRenderThreadIfSupported()
    {
        if (!EnableRenderThread || _renderThread != null) return;
        if (_renderThreadDisabledForSchemaGap) return;  // latched off after un-recordable content — don't flap back on RT recreate
        // The render thread is Windows/HWND-only by design (it owns the swap-chain
        // present). On non-Windows _platformWindow drives rendering, and
        // ShouldUseCompositionRenderTarget()'s user32 GetWindowLong P/Invoke would
        // throw DllNotFoundException there — bail BEFORE touching it.
        if (_platformWindow != null) return;
        if (RenderTarget == null || ShouldUseCompositionRenderTarget()) return;  // DComp → inline path
        if (Visual.RenderCacheHost == null) return;                              // no whole-frame capture
        _renderThreadStop = false;
        _rtFrameAvailable ??= new AutoResetEvent(false);
        _rtIdle ??= new ManualResetEventSlim(false);
        var t = new Thread(RenderThreadLoop) { IsBackground = true, Name = "Jalium.Render" };
        _renderThread = t;
        _rtActive = true;
        t.Start();
    }

    private bool StopRenderThread()
    {
        var t = _renderThread;
        if (t == null) return true;
        _rtActive = false;
        _renderThreadStop = true;
        _rtPause = false;
        _rtFrameAvailable?.Set();
        // FIX #2: join generously — a present can run long during a device-removed /
        // TDR stall (well beyond steady state). Cover the ~2s Windows TDR window so a
        // caller doesn't dispose the RenderTarget out from under an in-flight present.
        bool joined = t.Join(2000);
        if (!joined)
        {
            // Retain the Thread reference. Callers must defer every resource
            // owned by it rather than pretending the timed-out join succeeded.
            return false;
        }

        _ = Interlocked.CompareExchange(ref _renderThread, null, t);
        lock (_rtChannelLock) { _rtPendingFrame = null; }
        return true;
    }

    private bool TryCompleteRenderThreadSchemaFallback()
    {
        if (!StopRenderThread())
        {
            // The worker has been asked to exit but is still inside native code.
            // Keep the UI out of the inline path and do not arm another wait on
            // this target until a later retry can join the worker conclusively.
            RequestFullInvalidation();
            return false;
        }

        StartExternalPresentPacingIfSupported();
        return true;
    }

    /// <summary>
    /// Detaches the drawing context while its render target/backend are still
    /// alive. The ordering is intentional: pending retained handles are drained,
    /// the static GPU-eviction subscription is removed, and only then are cached
    /// native resources released. Callers may dispose the RenderTarget after this
    /// method returns.
    /// </summary>
    private void ReleaseDrawingContextBeforeRenderTargetDisposal(bool clearAllCaches)
    {
        var drawingContext = _drawingContext;
        _drawingContext = null;
        if (drawingContext == null)
        {
            return;
        }

        try
        {
            drawingContext.DrainPendingRetainedLayers();
        }
        finally
        {
            // Close is the ownership boundary, not a cache operation: it removes
            // ImageSource.GpuCacheEvictionRequested's static delegate, which would
            // otherwise root this context together with its retired RT/Context.
            drawingContext.Close();
            if (clearAllCaches)
            {
                drawingContext.ClearCache();
            }
            else
            {
                drawingContext.ClearBitmapCache();
            }
        }
    }

    private void ReleaseRenderResourcesAfterRenderThreadStopped()
    {
        // Release large image resources before dropping the drawing context.
        // Avoid full ClearCache during shutdown because text/brush ordering is
        // still sensitive.
        try { ReleaseDrawingContextBeforeRenderTargetDisposal(clearAllCaches: false); }
        catch { /* shutdown cleanup is best effort */ }

        // Both waiters borrow HANDLEs owned by the swap chain and must be gone
        // before RenderTarget.Dispose closes those handles.
        StopFramePacer();
        StopExternalPresentPacing();

        var renderTarget = RenderTarget;
        RenderTarget = null;
        try { renderTarget?.Dispose(); }
        catch { /* never surface deferred/native teardown failures */ }
    }

    private void ScheduleDeferredRenderResourceRelease(Thread stalledRenderThread)
    {
        if (Interlocked.Exchange(ref _deferredRenderResourceReleaseScheduled, 1) != 0)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                // No timeout here: if the native present never returns, leaking
                // the still-in-use target is safer than freeing it underneath
                // the live render thread.
                stalledRenderThread.Join();
                if (Interlocked.CompareExchange(
                        ref _renderThread, null, stalledRenderThread) == stalledRenderThread)
                {
                    lock (_rtChannelLock) { _rtPendingFrame = null; }
                    ReleaseRenderResourcesAfterRenderThreadStopped();
                }
            }
            catch
            {
                // Process shutdown / thread teardown: retain resources rather
                // than risk releasing them while ownership is uncertain.
            }
        });
    }

    // Park the render thread (provably out of BeginDraw..EndDraw) so the UI thread
    // can safely Resize / Dispose / recreate the RenderTarget the render thread owns.
    /// <returns>
    /// True if the render thread actually parked within the timeout (or there is
    /// no render thread). FIX #5: callers that then dispose/resize/recreate the
    /// RenderTarget MUST honor a false result — skip the teardown and reschedule
    /// rather than free the RT out from under an in-flight present.
    /// </returns>
    private bool RequestRenderThreadIdle()
    {
        var t = _renderThread;
        if (t == null || !t.IsAlive) return true;   // nothing to drain
        _rtIdle?.Reset();
        _rtPause = true;
        _rtFrameAvailable?.Set();
        return _rtIdle?.Wait(1000) ?? true;
    }

    private void ResumeRenderThread()
    {
        if (_renderThread == null) return;
        _rtPause = false;
        _rtFrameAvailable?.Set();
    }

    private void RenderThreadLoop()
    {
        var wake = _rtFrameAvailable;
        while (!_renderThreadStop)
        {
            wake?.WaitOne(250);   // short poll: keeps the loop alive even with no frames queued
            if (_renderThreadStop) break;
            if (_rtPause) { _rtIdle?.Set(); continue; }   // drain barrier — parked for lifecycle ops
            FrameCapture? cap;
            lock (_rtChannelLock) { cap = _rtPendingFrame; _rtPendingFrame = null; }
            if (cap == null) continue;
            try { PresentCaptureOnRenderThread(cap); }
            catch (RenderPipelineException ex) { MarshalRenderRecovery(ex); }
            catch
            {
                // The capture is lost and dirty was already cleared at publish
                // time — without re-invalidation a static scene would keep the
                // stale frame on screen until the next external invalidation.
                // Marshal a full invalidation back to the UI thread so the
                // scene re-records and re-publishes.
                try { _dispatcher?.BeginInvokeCritical(() => { RequestFullInvalidation(); InvalidateWindow(); }); }
                catch { /* shutting down */ }
            }
        }
    }

    private void PresentCaptureOnRenderThread(FrameCapture cap)
    {
        lock (_renderTargetUseGate)
        {
            PresentCaptureOnRenderThreadCore(cap);
        }
    }

    private void PresentCaptureOnRenderThreadCore(FrameCapture cap)
    {
        var rt = cap.Target;
        if (!IsRenderLifecycleCurrent(cap.LifecycleGeneration, rt) ||
            !ReferenceEquals(rt.OwnerContext, cap.OwnerContext) ||
            !rt.IsValid ||
            rt.OwnerContextGeneration != cap.ContextGeneration)
        {
            return;
        }
        var host = Visual.RenderCacheHost;
        if (host == null) return;

        // The render thread EXCLUSIVELY owns _drawingContext on this path (the UI
        // thread no longer touches it — see RenderFrame). Create + maintain it here
        // so the brush-cache trim and Replay run on a single thread.
        // A global Current lookup is not an ownership proof: another window can
        // replace it while this target remains pinned to its creating backend.
        // Every cache/resource created for replay must use the target's owner.
        var context = rt.OwnerContext;
        if (context == null || !context.IsValid) return;
        var dc = _drawingContext ??= new RenderTargetDrawingContext(rt, context);
        // backdrop/LiquidGlass（snapshot/背景折射型 effect）在拖拽中仍降级回扁平 overlay：
        // 其背景 snapshot 滞后于 in-flight 的 resize back buffer，真折射会采到错位的屏幕区域
        // → 玻璃内容"跑到面板外"。glow/shadow 是 element-capture 型、不采背景，故不受此 flag 影响。
        dc.SimplifyBackdropEffects = _isSizing;
        dc.DrainPendingRetainedLayers();

        rt.SetFullInvalidation();
        if (!rt.TryBeginDraw())
        {
            // GPU/swap-chain busy. Do NOT silently drop the capture: dirty was
            // already cleared when this frame was published, so on a static
            // scene nothing would ever re-publish and the last frame would
            // never reach the screen (same "needs a mouse-move to refresh"
            // failure mode the inline path fixed in TryBeginDrawOrScheduleRetry).
            // Put the capture back (UI thread's newer frame wins if one landed
            // meanwhile) and re-wake the loop — BeginDraw's internal 16 ms
            // waitable wait is the retry back-off, and it blocks only this
            // render thread, never the UI thread.
            lock (_rtChannelLock) { _rtPendingFrame ??= cap; }
            _rtFrameAvailable?.Set();
            return;
        }

        _rtBusy = true;
        bool ended = false;
        try
        {
            ClearBackground(rt);
            dc.Offset = Point.Zero;
            host.Replay(cap.Drawing, dc);
            if (!IsRenderLifecycleCurrent(cap.LifecycleGeneration, rt) ||
                !ReferenceEquals(rt.OwnerContext, cap.OwnerContext))
            {
                return;
            }
            OnRender(rt);
            if (!IsRenderLifecycleCurrent(cap.LifecycleGeneration, rt) ||
                !ReferenceEquals(rt.OwnerContext, cap.OwnerContext))
            {
                return;
            }
            JaliumResult endResult = rt.TryEndDraw();   // non-throwing; Ok or failure result
            ended = endResult == JaliumResult.Ok;
            if (ended && _consecutiveRecoverableRenderFailures != 0)
            {
                // Mirror the inline path's "successful present resets the
                // backoff" rule (CompleteEndDrawOrHandleFailure) — otherwise
                // failures accumulate across unrelated incidents for the
                // lifetime of the render thread and the SECOND incident, months
                // later, would trip the backend-fallback threshold. Racy read is
                // fine: a missed reset is retried on the next presented frame.
                _dispatcher?.BeginInvokeCritical(ResetRenderRecoveryBackoff);
            }
            if (!ended && endResult == JaliumResult.InvalidState)
            {
                // End/InvalidState is a dropped frame on a healthy device, not a
                // device-loss incident. Requeue a full capture on the SAME target;
                // do not touch the recovery latch/backoff or rebuild the RT.
                ScheduleDroppedFrameRetry();
            }
            else if (!ended && IsRecoverableRenderPipelineFailure(endResult, "End"))
            {
                // Surface device loss to the UI thread NOW instead of waiting
                // for the next frame's TryBeginDraw to throw — saves a frame of
                // latency and stops this loop from re-submitting against a
                // removed device meanwhile. External present pacing is never
                // active while the render thread runs, so there is no credit
                // to return on this path.
                MarshalRenderRecovery(new RenderPipelineException(
                    stage: "End",
                    result: endResult,
                    resultCode: (int)endResult,
                    hwnd: Handle,
                    width: rt.Width,
                    height: rt.Height,
                    dpiX: (float)(_dpiScale * 96.0),
                    dpiY: (float)(_dpiScale * 96.0),
                    backend: rt.Backend.ToString()));
            }
        }
        finally
        {
            if (!ended && rt.IsDrawing) { try { rt.TryEndDraw(); } catch { } }
            dc.TrimCacheIfNeeded();   // FIX: cache trim now on the owning (render) thread
            _rtBusy = false;
        }
    }

    // Nonzero while a render-thread-marshalled recovery is queued or running on
    // the UI thread. One GPU switch produces a burst of failures on the render
    // thread (EndDraw DEVICE_LOST, then next frame's BeginDraw throw, ...);
    // without this latch each one queues its own recovery, the second disposes
    // the RT the first just rebuilt, and two MarkRecoverableRenderFailure calls
    // from a single incident trip the backend-fallback threshold — permanently
    // downgrading the window to the software backend.
    private int _recoveryMarshalPending;

    private void MarshalRenderRecovery(RenderPipelineException ex)
    {
        int alreadyPending = Interlocked.CompareExchange(ref _recoveryMarshalPending, 1, 0);
        if (alreadyPending != 0)
        {
            return; // a recovery for this incident is already queued — coalesce
        }

        try
        {
            _dispatcher?.BeginInvokeCritical(() =>
            {
                try
                {
                    // FIX #5 contract: only touch the RT from this thread when the
                    // render thread actually parked. If it is wedged inside a
                    // native present (>1 s), a concurrent EndDraw here would be
                    // two threads in the same swap chain — the recovery path
                    // re-parks and safely defers instead.
                    bool parked = RequestRenderThreadIdle();
                    if (parked && RenderTarget?.IsDrawing == true) { try { RenderTarget.TryEndDraw(); } catch { } }
                    HandleRecoverableRenderPipelineFailure(ex, "RenderThread");
                    ResumeRenderThread();
                }
                finally
                {
                    Volatile.Write(ref _recoveryMarshalPending, 0);
                }
            });
        }
        catch
        {
            // shutting down — release the latch so a later marshal isn't blocked
            Volatile.Write(ref _recoveryMarshalPending, 0);
        }
    }

    // UI thread: record the whole tree into a Drawing and hand it to the render
    // thread (overwriting any un-consumed pending frame = latest-frame-wins), then
    // return immediately. Never blocks on the present.
    private void PublishFrameToRenderThread(int lifecycleGeneration, RenderTarget renderTarget)
    {
        if (!IsRenderLifecycleCurrent(lifecycleGeneration, renderTarget)) return;

        // Back-pressure (FIX): if the render thread is still presenting the
        // previous frame and a capture is already queued, skip recording this tick
        // — the expensive Render walk would only overwrite the unconsumed pending
        // frame. Reschedule so the latest state still reaches the screen once the
        // render thread drains. Checked before allocating a recorder.
        if (_rtBusy)
        {
            lock (_rtChannelLock)
            {
                if (_rtPendingFrame != null) { ScheduleDeferredRender(1); return; }
            }
        }

        var host = Visual.RenderCacheHost;
        var recorder = host?.CreateFrameRecorder();
        if (host == null || recorder == null)
        {
            // Host can't whole-frame capture: latch to inline for this window and
            // JOIN the render thread (FIX: don't leave it alive owning the RT while
            // the UI thread resumes ownership).
            _renderThreadDisabledForSchemaGap = true;
            _ = TryCompleteRenderThreadSchemaFallback();
            // The window now lives on the inline path permanently — hand present
            // pacing to the event-driven scheduler it would otherwise have missed
            // (StartRenderThreadIfSupported won't resurrect the thread: the latch
            // blocks it), so a slow compositor can't pin the UI thread here either.
            ScheduleDeferredRender(1);
            return;
        }

        object drawing;
        try
        {
            Render(recorder);
            try { DevToolsOverlay?.DrawOverlay(recorder); } catch { }
        }
        finally
        {
            // Always exit the whole-frame scope even if the tree walk throws.
            drawing = host.FinishRecord(recorder);
        }

        // Render(recorder) walks user-extensible visuals. A callback can close
        // or externally destroy the window; never publish that stale capture to
        // the render thread after its lifecycle token has been invalidated.
        if (!IsRenderLifecycleCurrent(lifecycleGeneration, renderTarget)) return;

        // schema-gap (render-thread path): the capture hit content it can't
        // represent (windowless WebView punch, video surface, ink-layer blit, …).
        // Don't publish a lossy frame — permanently fall back to the inline UI
        // path for this window (latch so RT recreation can't resurrect the lossy
        // path) and STOP+JOIN the render thread before the UI resumes RT ownership.
        // Dirty state is preserved so the rescheduled inline frame repaints.
        if (drawing is Jalium.UI.Media.Rendering.Drawing d && !d.IsFullyRecordable)
        {
            _renderThreadDisabledForSchemaGap = true;
            _ = TryCompleteRenderThreadSchemaFallback();
            // Same as the host-null latch above: the inline path this window
            // falls back to gets event-driven present pacing.
            ScheduleDeferredRender(1);   // repaint inline (dirty intact)
            return;
        }

        var cap = new FrameCapture
        {
            Drawing = drawing,
            Target = renderTarget,
            OwnerContext = renderTarget.OwnerContext,
            ContextGeneration = renderTarget.OwnerContextGeneration,
            LifecycleGeneration = lifecycleGeneration,
        };

        // Publish and consume dirty state as one lifecycle transaction. If
        // teardown, recovery, or schema fallback won the race after recording,
        // leave dirty intact and never expose this stale capture to the worker.
        lock (_renderLifecycleGate)
        {
            if (!IsRenderLifecycleCurrent(lifecycleGeneration, renderTarget) ||
                !_rtActive || _renderThread == null || _renderThreadStop ||
                !ReferenceEquals(renderTarget.OwnerContext, cap.OwnerContext))
            {
                return;
            }

            lock (_rtChannelLock)
            {
                _rtPendingFrame = cap;
                lock (_dirtyLock)
                {
                    _dirtyElements.Clear();
                    _dirtyFreeRects.Clear();
                    _fullInvalidation = false;
                }
            }
        }
        _rtFrameAvailable?.Set();
    }

    private void StartFramePacerIfSupported()
    {
        if (!EnableFramePacer) return;
        // FIX: the swap-chain frame-latency waitable is auto-reset / single-
        // consumer. The render thread (when enabled) is the consumer that moves
        // the BeginDraw wait off the UI thread; the legacy pacer must NOT also
        // wait on the same handle — that is the v5 two-waiter trap where each
        // signal is won by one thread and the loser stalls to the 16ms timeout.
        // Render thread supersedes the pacer.
        if (EnableRenderThread) return;
        if (_framePacerThread != null) return;  // already started
        var rt = RenderTarget;
        if (rt == null) return;
        nint waitable = rt.GetFrameLatencyWaitable();
        if (waitable == nint.Zero) return;  // backend doesn't expose a waitable — fall back to timer
        _frameLatencyWaitable = waitable;
        _framePacerStopFlag = 0;
        var thread = new Thread(FramePacerLoop)
        {
            IsBackground = true,
            Name = "Jalium.FramePacer",
        };
        _framePacerThread = thread;
        thread.Start();
    }

    private void StopFramePacer()
    {
        var thread = _framePacerThread;
        if (thread == null) return;
        _framePacerStopFlag = 1;
        // Up to one poll period (~16 ms) for the loop to observe the flag.
        // Don't block on Join indefinitely — if the wait API is hung the
        // background thread is harmless once the swap chain handle is gone.
        thread.Join(200);
        _framePacerThread = null;
        _frameLatencyWaitable = nint.Zero;
        Interlocked.Exchange(ref _framePending, 0);
    }

    private void FramePacerLoop()
    {
        nint waitable = _frameLatencyWaitable;
        while (_framePacerStopFlag == 0 && waitable != nint.Zero)
        {
            // Short poll: blocks at most one vsync. WAIT_OBJECT_0 means the
            // swap chain just released a back buffer; the pacer's only job
            // is to translate that into a dispatcher post when a frame is
            // pending. WAIT_TIMEOUT means no signal yet — loop, re-check stop.
            uint waitResult = WaitForSingleObjectEx(waitable, FRAME_PACER_POLL_MS, false);
            if (_framePacerStopFlag != 0) break;
            if (waitResult != 0 /* WAIT_OBJECT_0 */) continue;  // timeout or APC abandon — loop
            if (Interlocked.Exchange(ref _framePending, 0) != 1) continue;  // no pending render
            // Hand off to the UI dispatcher. BeginInvokeCritical schedules
            // ProcessRender in the high-priority queue so it runs before
            // ordinary input/dispatch work piles up.
            try { _dispatcher?.BeginInvokeCritical(ProcessRender); } catch { /* shutdown race */ }
        }
    }

    // ── External present pacing（事件驱动 present 调度）─────────────────────
    // 根治"慢合成环境（核显切换 / 遮挡节流 / 远程虚拟显示器）整窗卡死"：
    // 旧模型里 UI 线程要么阻塞在 Present(1)（vsync 对齐，实测 DWM buffer-retire
    // 130-460ms 全落 UI 线程），要么阻塞在 BeginDraw 的 16ms-waitable 超时 +
    // 1ms timer 重试忙等循环（每帧 ~30 圈，等待只是换了位置——见 D3D12RT
    // CreateSwapChain 内的 4 配置 A/B 注释）。新模型把 swap-chain frame-latency
    // waitable 的消费权上移到这里：线程池 RegisterWaitForSingleObject 把信号转成
    // 一个 present credit，UI 线程只在拿到 credit 时才进 BeginDraw（native 在
    // external-pacing 模式下不再等 waitable，Present 永远 sync-interval 0 不阻塞）。
    // DWM 慢只会降低上屏频率（dirty 自然累积、丢帧合并、动画时钟照常推进），
    // 永远不再阻塞输入 / 布局 / 动画。正常环境下 waitable 以 vblank 节奏 signal，
    // 帧节奏与旧 vsync 模型一致。
    //
    // credit 守恒：waitable 是 auto-reset、计数 = MaxFrameLatency(=1)，消费即
    // 拥有。一个 credit 只在两种情况下回到池里：① present 成功 → DWM retire →
    // waitable signal → 回调转 credit；② 本帧消费了 credit 但最终没 present
    // （BeginDraw fence 失败 / TryEndDraw 失败）→ 显式归还。漏掉任何一条都会
    // 死锁在"credit 永远不来"；多归还则退化为 Present 内 DXGI 自身排队（短暂
    // 阻塞，不丢正确性）。resize / RT 重建后信号计数语义不可依赖，统一重置。
    private volatile bool _swapPacingActive;
    private int _swapCredit;            // 可立即开帧的 present 名额（计数信号量，上限 _swapCreditCap）
    private int _swapCreditCap = 1;     // = swap chain 的 MaxFrameLatency（默认 1；JALIUM_MAX_FRAME_LATENCY 实验旋钮）
    private int _swapWaitRegistered;    // 1 = 线程池等待已挂（CAS 幂等）
    private volatile bool _renderPendingOnSwap;  // miss 时置位：credit 到位后需要调度渲染
    private RegisteredWaitHandle? _swapRegisteredWait;
    private ManualResetEvent? _swapWaitEvent;   // 包装 native HANDLE（不拥有）
    private long _swapWaitTimeoutCount;          // 诊断：500ms 兜底超时次数
    // 串行化 arm/disarm 与线程池超时回调的重注册：CAS 成功到 _swapRegisteredWait
    // 赋值之间若被 Stop 插入，新注册会逃过 Unregister、随后在已被 RT.Dispose 关闭
    // 的 HANDLE 上等待（UB）。低频路径，锁无热路径代价。
    private readonly object _swapPacingLock = new();

    private void StartExternalPresentPacingIfSupported()
    {
        void Trace(string reason)
        {
            if (PacingTraceEnabled) Console.Error.WriteLine($"[pacing-trace] Start: {reason} (win={Title})");
        }

        StopExternalPresentPacing();   // RT 重建路径：刷新句柄 + 重置状态
        // Cross-platform host (Android 等)：无 user32、无 DXGI waitable，且下面的
        // ShouldUseCompositionRenderTarget 会 P/Invoke user32（StartRenderThreadIfSupported
        // 规避过的同一个陷阱）。
        if (_platformWindow != null) { Trace("skip: platformWindow"); return; }
        // 渲染线程运行中不启用：present 阻塞落在渲染线程，本就不卡 UI；且
        // waitable 必须单消费者（v5 双等待者陷阱），渲染线程路径里 native
        // BeginDraw 是那个消费者。判定用"线程实际存活"而非 EnableRenderThread
        // 环境变量——schema-gap latch 会永久停掉渲染线程改走 inline 路径，
        // 那之后这个窗口理应获得 inline 路径的全部能力（latch 点会重新调用
        // 本方法接管 pacing）。
        if (_renderThread?.IsAlive == true) { Trace("skip: renderThread alive"); return; }
        // JALIUM_DISABLE_VSYNC 是 benchmark/动画人群的显式选择：非对齐 present、
        // 不限帧。健康合成器上旧 inline 路径的 waitable 等待即到即过，没有本
        // 调度器要解决的 16ms 卡顿；接管反而给每帧加一圈线程池+dispatcher 跳。
        if (VSyncDisabledByEnv) { Trace("skip: JALIUM_DISABLE_VSYNC"); return; }
        var rt = RenderTarget;
        if (rt == null || !rt.IsValid || ShouldUseCompositionRenderTarget()) { Trace("skip: rt/composition"); return; }
        nint waitable = rt.GetFrameLatencyWaitable();
        if (waitable == nint.Zero) { Trace("skip: no waitable"); return; }   // 非 D3D12 / 降级阶梯 Flags=0 → 旧路径
        // legacy frame-pacer 实验（JALIUM_ENABLE_FRAME_PACER）也常驻等待同一个
        // auto-reset waitable——本调度器接管前必须停掉它，否则两个消费者抢信号，
        // pacer 抢到的每个信号都被无声丢弃（其 _framePending 无人置位），credit
        // 永远不来。external pacing 是 pacer 思路的功能超集。
        StopFramePacer();
        lock (_swapPacingLock)
        {
            var ev = new ManualResetEvent(false);
            ev.SafeWaitHandle.Dispose();
            // ownsHandle:false——HANDLE 归 swap chain 所有，RenderTarget.Dispose 关闭它；
            // StopExternalPresentPacing 必须先于 RT Dispose 执行（见窗口关闭路径）。
            ev.SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(waitable, ownsHandle: false);
            _swapWaitEvent = ev;
            rt.SetExternalPresentPacing(true);
            // credit 播 0：swap chain 创建时 DXGI 已预置 MaxFrameLatency 个信号，
            // 由首次 miss 挂上的回调立即转换成 credit（信号 pending 时注册即刻满足）。
            // 播种非零会与预置信号叠加，让稳态管线静默加深。桶深 = native 的
            // 真实 MaxFrameLatency（默认 1，JALIUM_MAX_FRAME_LATENCY 可覆盖）——
            // 唯一真源是 native，经 GetPresentInfo 回读，绝不双轨配置。
            int cap = 1;
            var presentInfo = rt.GetPresentInfo();
            if (presentInfo is { MaxFrameLatency: > 0 } pi)
            {
                cap = Math.Min(pi.MaxFrameLatency, 8);
            }
            _swapCreditCap = cap;
            Interlocked.Exchange(ref _swapCredit, 0);
            Interlocked.Exchange(ref _swapWaitRegistered, 0);
            _renderPendingOnSwap = false;
            _swapPacingActive = true;
        }
        Trace($"ACTIVE cap={_swapCreditCap}");
    }

    private void StopExternalPresentPacing()
    {
        RegisteredWaitHandle? rw;
        ManualResetEvent? ev;
        lock (_swapPacingLock)
        {
            _swapPacingActive = false;
            _renderPendingOnSwap = false;
            rw = _swapRegisteredWait;
            _swapRegisteredWait = null;
            Interlocked.Exchange(ref _swapWaitRegistered, 0);
            ev = _swapWaitEvent;
            _swapWaitEvent = null;
        }
        if (rw != null)
        {
            // 阻塞式注销：调用方紧接着就会 RenderTarget.Dispose 关闭 waitable
            // HANDLE——必须确保在途回调与底层等待已完全离开该句柄（对注册等待中
            // 的句柄 CloseHandle 是未定义行为）。100ms 上限防线程池饥饿恶化成卡死。
            var done = new ManualResetEvent(false);
            try { if (rw.Unregister(done)) done.WaitOne(100); }
            catch { /* already gone */ }
            finally { done.Dispose(); }
        }
        try { ev?.Dispose(); } catch { /* non-owning wrapper */ }
        try
        {
            var rt = RenderTarget;
            if (rt != null && rt.IsValid) rt.SetExternalPresentPacing(false);
        }
        catch { /* RT mid-teardown */ }
    }

    private void EnsureSwapWaitRegistered()
    {
        if (!_swapPacingActive) return;
        if (Interlocked.CompareExchange(ref _swapWaitRegistered, 1, 0) != 0) return;
        lock (_swapPacingLock)
        {
            // 复查：CAS 与进锁之间 Stop 可能已经 disarm（并清掉了 registered 标志，
            // 但本线程的 CAS 又把它置回了 1）——这里必须回滚并放弃注册。
            if (!_swapPacingActive || _swapWaitEvent == null)
            {
                Interlocked.Exchange(ref _swapWaitRegistered, 0);
                return;
            }
            try
            {
                // 500ms 超时只是防"信号丢失"的心跳兜底：超时回调不伪造 credit（伪造
                // 会让 Present 落回 DXGI 排队阻塞），只在 credit 仍未到时继续追等。
                _swapRegisteredWait = ThreadPool.RegisterWaitForSingleObject(
                    _swapWaitEvent, OnSwapWaitableSignaled, null, 500, executeOnlyOnce: true);
            }
            catch
            {
                Interlocked.Exchange(ref _swapWaitRegistered, 0);
                ScheduleDeferredRender(GpuBusyRetryDelayMs);   // 兜底退回 timer 重试
            }
        }
    }

    private void OnSwapWaitableSignaled(object? state, bool timedOut)
    {
        Interlocked.Exchange(ref _swapWaitRegistered, 0);
        if (timedOut)
        {
            Interlocked.Increment(ref _swapWaitTimeoutCount);
            // 不伪造 credit；present 仍在 DWM 手里（慢合成下 retire 可达 460ms+，
            // 多个 500ms 周期是预期内的），credit 没回来就继续追下一个信号。
            if (_swapPacingActive && Volatile.Read(ref _swapCredit) < _swapCreditCap)
            {
                EnsureSwapWaitRegistered();
            }
            return;
        }
        // credit 的唯一节拍源就是 waitable 信号本身——它是"上一帧已 retire、
        // back buffer 可写、下一次 Present 不会阻塞"的权威信号。
        //
        // 【撤回记录 2026-06-10】曾在此叠加 DwmFlush"合成节拍门"（密集期等一拍
        // 全局合成再发 credit，防连续动画 40fps 白渲染占线）。两个致命缺陷：
        // ① DWM 合成按需——静止桌面不合成，无条件门让低频应用陷入
        //   渲染等credit→credit等合成→合成等屏幕变化 的循环依赖（帧间隔 1.8s+）；
        // ② 改成"密集期才过门"后仍错：DwmFlush 是【全局】合成节拍，而 retire 是
        //   【per-window】的——大窗口（如 1344x852 Gallery）在远程 IDD 上 retire
        //   ~164ms 慢于全局节拍 ~110ms，门过早放行 credit → 下一帧 Present 撞
        //   MFL=1 的未-retire 墙 → presentBlock 164ms 落回 UI 线程（恰是本调度器
        //   要消灭的形态）。小窗探针（retire 25ms）测不出此失配。
        // 防占线的正确手段是"只在有渲染意图时调度"（下方 pending 检查），而重帧
        // 应用的 retire 本来就慢，渲染节奏天然被 waitable 配平。
        AddSwapCredit();
        if (!_swapPacingActive) return;
        // MFL>1 时一次只消费得到一个信号；若还差名额（credit 未满且仍有
        // pending in-flight），立刻续挂等待去收下一个。
        if (Volatile.Read(ref _swapCredit) < _swapCreditCap)
        {
            EnsureSwapWaitRegistered();
        }
        // 只在确有挂起的渲染意图时调度——无条件调度会在连续动画下叠加每秒
        // 几十次 dispatcher 空转，进一步挤压输入处理。丢失唤醒由
        // TryBeginDrawOrScheduleRetry 的 double-check 兜底（pending 置位后
        // 它会再取一次 credit）。
        if (_renderPendingOnSwap)
        {
            _renderPendingOnSwap = false;
            try { _dispatcher?.BeginInvokeCritical(ProcessRender); } catch { /* shutting down */ }
        }
    }

    /// <summary>
    /// 本帧消费了 present credit 但最终没有 present 成功（BeginDraw 的 fence /
    /// 设备失败、TryEndDraw 失败）——归还 credit，否则 DWM 永远不会 retire 出
    /// 新信号，调度死锁在"credit 永远不来"。
    /// </summary>
    private void ReturnSwapCreditAfterFailedPresent()
    {
        if (_swapPacingActive) AddSwapCredit();
    }

    /// <summary>计数信号量入账，clamp 到 MaxFrameLatency 上限（多还塌缩，无害）。</summary>
    private void AddSwapCredit()
    {
        if (Interlocked.Increment(ref _swapCredit) > _swapCreditCap)
        {
            Interlocked.Decrement(ref _swapCredit);
        }
    }

    /// <summary>尝试消费一个 present 名额；失败时余额不变。</summary>
    private bool TryTakeSwapCredit()
    {
        if (Interlocked.Decrement(ref _swapCredit) >= 0) return true;
        Interlocked.Increment(ref _swapCredit);
        return false;
    }

    private bool TryBeginDrawOrScheduleRetry()
    {
        if (PacingTraceEnabled && _pacingTraceCount < 200)
        {
            _pacingTraceCount++;
            Console.Error.WriteLine($"[pacing-trace] TryBegin: active={_swapPacingActive} sizing={_isSizing} credit={Volatile.Read(ref _swapCredit)} cap={_swapCreditCap} pending={_renderPendingOnSwap}");
        }
        Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_CREDIT, Volatile.Read(ref _swapCredit));
        Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_PENDING, _renderPendingOnSwap ? 1 : 0);
        Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_WAITREG, Volatile.Read(ref _swapWaitRegistered));
        if (_swapPacingActive)
        {
            // 拖拽 resize（modal sizing loop）期间旁路 credit 门：每个 WM_SIZE
            // 都必须即时重画，否则新暴露的区域要等合成节拍（慢合成下 ~9fps）
            // 才被填充，体感是"空白慢慢填、松手才正常"。直接操纵反馈优先于
            // 限流：Present(0) 不阻塞、深桶+DXGI 队列吸收突发；期间账本只进
            // 不出（present 的 ack 照常经回调入账并被 clamp 塌缩），无泄漏。
            if (_isSizing)
            {
                if (RenderTarget?.TryBeginDraw() == true) return true;
                if (ResizeTraceEnabled) Console.Error.WriteLine("[resize-trace] BEGIN-FAIL (sizing bypass)");
                _debugHud.OnBeginFail();
            Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.BEGIN_FAIL);
                ScheduleDeferredRender(GpuBusyRetryDelayMs);
                return false;
            }
            if (!TryTakeSwapCredit())
            {
                // 先声明渲染意图再挂等待：回调只在 pending 置位时才调度
                // ProcessRender（防连续动画下每信号一次的 dispatcher 空转）。
                _renderPendingOnSwap = true;
                EnsureSwapWaitRegistered();
                // double-check：信号可能在上面消费尝试与 Register 之间被一个
                // 在途回调转成 credit——丢失唤醒会让本次渲染请求等满一整个
                // DWM 周期（慢合成下 460ms+），这里补救性再取一次。
                if (!TryTakeSwapCredit())
                {
                    Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.CREDIT_MISS);
                    // Not a "begin fail" for the HUD: a credit miss is the
                    // event-driven scheduler's normal deferral, not a GPU stall.
                    if (DebugRender) System.Diagnostics.Debug.WriteLine("[TryBeginDraw] SKIP: no present credit (event-driven wait armed)");
                    // dirty 保留；OnSwapWaitableSignaled 到点调度 ProcessRender。
                    // 不再走 1ms timer 重试——那个循环每圈都在 native waitable 上
                    // 阻塞 16ms，慢合成下 UI 线程 94% 时间被钉死，正是本次重构
                    // 要根治的形态。
                    return false;
                }
                // double-check 拿到了 credit——渲染意图已兑现，撤销 pending
                // 防止下一个回调多调度一轮（无害但白占 dispatcher）。
                _renderPendingOnSwap = false;
            }
            if (RenderTarget?.TryBeginDraw() == true)
            {
                return true;
            }
            // external pacing 下 BeginDraw 不等 waitable，失败=fence/设备问题。
            // 本帧没花掉 present 名额 → 归还 credit，沿用 timer 重试兜底。
            ReturnSwapCreditAfterFailedPresent();
            _debugHud.OnBeginFail();
            Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.BEGIN_FAIL);
            if (DebugRender) System.Diagnostics.Debug.WriteLine("[TryBeginDraw] FAIL: fence/device busy (credit returned)");
            ScheduleDeferredRender(GpuBusyRetryDelayMs);
            return false;
        }

        if (RenderTarget?.TryBeginDraw() == true)
        {
            return true;
        }

        _debugHud.OnBeginFail();
        if (DebugRender) System.Diagnostics.Debug.WriteLine("[TryBeginDraw] FAIL: GPU busy");

        // 总是显式调度一次重试——绝不能依赖 CompositionTarget.IsActive 下"下一个动画 tick 会
        // 顺带重绘"。IsActive 只表示存在长寿命订阅者（状态栏时钟、未结束的滚动惯性等），并不
        // 保证 tick 真的会及时到来；漏掉调度时，BeginDraw 失败的那一帧（典型：resize 刚结束、
        // 内容仍处 backdrop 降级 + 尺寸过渡态）会一直停在屏幕上，直到鼠标移动经
        // InvalidateWindow 才被调度——正是"resize 后所有特效失效 / 内容向右下偏移 / 需鼠标
        // 经过才刷新"的根因。InvalidateWindow 早先已对同一个 IsActive 陷阱做过同款修复（不再
        // 依赖 RenderFlag_DirtyBetween，总是调度 ProcessRender），这里与之对齐。
        // ScheduleDeferredRender 经 TrySetRenderFlag(Scheduled) 幂等：动画进行中与动画 tick
        // 并存也不会重复渲染（先到者渲染并清 dirty，后到者 RenderFlag_Rendering 早退）。
        ScheduleDeferredRender(GpuBusyRetryDelayMs);

        return false;
    }

    /// <summary>
    /// Core rendering logic shared by both Dispatcher-based and WM_PAINT paths.
    /// Performs layout, submits dirty rects, and renders the visual tree.
    ///
    /// Retained mode rendering:
    /// - When nothing is dirty, skip the frame entirely (GPU idle).
    /// - When dirty elements exist, push an ALIASED D2D clip to the dirty region
    ///   and render only that area. ALIASED mode creates hard pixel boundaries
    ///   with no semi-transparent edge artifacts (unlike PER_PRIMITIVE mode).
    /// - Present1 dirty rects tell DWM which areas changed; FLIP_SEQUENTIAL
    ///   copies non-dirty areas from the previously presented buffer automatically.
    /// - Falls back to full render on first frame, resize, theme change, etc.
    /// - ProcessRender rate-limits to display refresh rate when no animation is active,
    ///   preventing GPU saturation from rapid input events (scrolling, mouse drag).
    /// </summary>
    private int _renderFrameLogCount;
    // ── Off-thread animation probe (Increment 1 — architecture hard gate) ──
    // Env-gated (JALIUM_DCOMP_ANIM_PROBE). On the first frame of a COMPOSITION
    // window (AllowsTransparency=true → WS_EX_NOREDIRECTIONBITMAP), creates ONE
    // self-driving DComp visual, then lets the app go idle. The whole off-thread
    // architecture rests on one unverified assumption: that DWM drives an
    // IDCompositionAnimation autonomously at vblank with NO app present. This probe
    // measures exactly that, cheaply. Put it on a composition window with NO other
    // animation (so the app genuinely idles), then confirm with PresentMon that the
    // app present rate is ≈0 while the amber block keeps sliding. If the block
    // freezes when the app idles, the direction is void — abandon for the cost of
    // one native function.
    private static readonly bool AnimProbeEnabled = IsEnvironmentSwitchEnabled("JALIUM_DCOMP_ANIM_PROBE");
    private bool _animProbeDone;
    private nint _animProbeVisual;

    private void MaybeFireAnimProbe()
    {
        if (!AnimProbeEnabled || _animProbeDone) return;
        var rt = RenderTarget;
        if (rt == null || !rt.IsValid) return;   // not ready yet — retry next frame
        if (!ShouldUseCompositionRenderTarget())
        {
            _animProbeDone = true;
            Console.Error.WriteLine("[AnimProbe] SKIP: window is NOT a composition window. Set AllowsTransparency=true on the test window to evaluate the gate.");
            return;
        }
        _animProbeDone = true;

        int w = Math.Clamp(rt.Width / 3, 80, 320);
        int h = Math.Max(12, (int)(16 * _dpiScale));
        int x = 40;
        int y = Math.Max(0, rt.Height / 2 - h / 2);
        float travel = Math.Max(1, rt.Width - w - 80);
        const float periodSec = 1.0f / 1.2f;     // mirror ProgressBar IndeterminateSpeedPerSecond (1.2 track/sec)
        const uint amber = 0xFFFFC107u;           // accent color (matches the screenshot)

        if (rt.TryCreateAnimProbe(x, y, w, h, travel, periodSec, amber, vertical: false, out _animProbeVisual))
            Console.Error.WriteLine($"[AnimProbe] OK — self-driving DComp visual created (bounds=({x},{y},{w},{h}), travel={travel}px). The app may now idle; the amber block MUST keep sliding. Measure app present rate with PresentMon (expect ≈0).");
        else
            Console.Error.WriteLine("[AnimProbe] FAILED — CreateAnimProbe returned NOT_SUPPORTED/error. Gate cannot be evaluated on this backend/window.");
    }

    private void RenderFrame()
    { _debugHud.OnRenderFrame();
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.RF);
        int frameLifecycleGeneration;
        lock (_renderLifecycleGate)
        {
            // A zero HWND does not by itself mean the managed render lifecycle
            // has ended. ForceRenderFrame supports an explicitly supplied render
            // target for offline/test rendering before a native window is shown.
            // Close/WM_DESTROY both invalidate the lifecycle through
            // _managedTeardownStarted, so that is the authoritative teardown gate.
            if (_managedTeardownStarted || _isClosing) return;
            if (HasRenderFlag(RenderFlag_Rendering)) return;
            // A swap-chain resize is mid-flight (native ResizeBuffers can pump a
            // reentrant WM_PAINT through the kernel callback). Beginning a frame now
            // would draw into buffers that are being recreated → AccessViolation.
            if (_resizeInProgress) return;
            if (WindowState == WindowState.Minimized) return;
            SetRenderFlag(RenderFlag_Rendering);
            ClearRenderFlag(RenderFlag_Requested);
            frameLifecycleGeneration = _renderLifecycleGeneration;
        }

        // RC4-a 帧事务状态：swapped=true 表示本帧已把活动脏集捕获为帧独占集且尚未
        // 收尾（Discard/RetainOrDrop 会把它翻回 false）；finally 兜底对仍未收尾的
        // 捕获集 MergeBack——单一出口审计，任何 return/throw 都不可能弄丢失效。
        bool frameDirtySwapped = false;
        long frameFullSeq = 0;
        RenderTarget? frameRenderTarget = null;
        // This token records ownership acquired by THIS UI-thread RenderFrame.
        // RenderTarget.IsDrawing is shared with the dedicated render worker and
        // therefore cannot prove who opened the native command list: after the
        // publish branch returns, the worker may BeginDraw before this method's
        // finally runs. Ending merely because IsDrawing became true would close
        // the worker's command list while it is still replaying.
        bool ownsNativeDrawSession = false;

        bool TryBeginOwnedNativeDrawSession()
        {
            if (!TryBeginDrawOrScheduleRetry())
            {
                return false;
            }

            ownsNativeDrawSession = true;
            return true;
        }

        bool CompleteOwnedNativeDrawSession()
        {
            if (!ownsNativeDrawSession)
            {
                return false;
            }

            try
            {
                return CompleteEndDrawOrHandleFailure();
            }
            finally
            {
                // RenderTarget.TryEndDraw clears its drawing state on every
                // result, including recoverable failures.
                ownsNativeDrawSession = false;
            }
        }

        void EndOwnedNativeDrawSessionBestEffort()
        {
            if (!ownsNativeDrawSession)
            {
                return;
            }

            // Clear first so re-entrant recovery/teardown cannot end twice.
            ownsNativeDrawSession = false;
            try { _ = frameRenderTarget?.TryEndDraw(); }
            catch { }
        }

        try
        {
            if (RenderTarget == null || !RenderTarget.IsValid)
            {
                EnsureRenderTarget();
            }

            if (RenderTarget == null || !RenderTarget.IsValid)
            {
                if (_renderFrameLogCount++ < 3)
                    Console.Error.WriteLine($"[RenderFrame] SKIP: RT still null/invalid after ensure");
                return;
            }

            // A schema-gap fallback may have timed out while joining a worker
            // stuck in native Present. StopRenderThread already lowered
            // _rtActive, so without this explicit pending-owner gate the code
            // below would immediately enter inline rendering on the same target.
            if (_renderThreadDisabledForSchemaGap && _renderThread != null &&
                !TryCompleteRenderThreadSchemaFallback())
            {
                ScheduleDeferredRender(1);
                return;
            }

            // Apply any resize that was deferred because it arrived mid-frame.
            // Safe here: RenderFlag_Rendering is set (so a reentrant resize defers
            // instead of racing) and we have not begun drawing yet. Recovery inside
            // the flush may recreate the render target, so re-validate afterwards.
            FlushPendingRenderTargetResize();
            if (RenderTarget == null || !RenderTarget.IsValid)
                return;

            frameRenderTarget = RenderTarget;
            if (frameRenderTarget == null ||
                !IsRenderLifecycleCurrent(frameLifecycleGeneration, frameRenderTarget))
                return;

            // Increment-1 architecture gate (env-gated, one-shot). Safe here: no
            // frame command list is open yet, so the probe's one-shot clear queues
            // cleanly on the shared command queue before this frame's BeginDraw.
            MaybeFireAnimProbe();
            if (!IsRenderLifecycleCurrent(frameLifecycleGeneration, frameRenderTarget))
                return;

            // Perform layout before rendering (queue-based: only dirty elements).
            // UpdateLayout may trigger further invalidations via AddDirtyElement.
            UpdateLayout();
            if (!IsRenderLifecycleCurrent(frameLifecycleGeneration, frameRenderTarget))
                return;
            _debugHud.MarkLayout();

            // ── Compute dirty region from accumulated dirty elements ──
            // Check dirty AFTER UpdateLayout so layout-triggered invalidations are included.
            bool fullInvalidation;
            bool isFlushFrame = false;
            bool explicitFullInvalidation = false;
            DirtyRegionAggregator? aggregator = null;
            // D3D12 now uses retained-mode dirty rects by default.
            // If a specific driver shows stale-buffer artifacts, the old behavior can be
            // restored with JALIUM_D3D12_FORCE_FULL_REPLAY=1.
            bool requiresFullReplay = frameRenderTarget.Backend == RenderBackend.D3D12 && ForceFullReplayForD3D12;
            // _rtActive 只在 UI 线程翻转（Start/StopRenderThread），帧首读入局部对本帧
            // 稳定；swap 条件与后面的分支必须用同一个值，防止中途翻转把捕获集晾在
            // render-thread 路径上。
            bool rtActive = _rtActive;
            int dirtyCountForHud = 0;
            lock (_dirtyLock)
            {
                // Idle frame skip: if nothing is dirty and no explicit full invalidation,
                // skip the frame entirely regardless of backend.  requiresFullReplay means
                // "when we DO render, repaint everything" — it must NOT prevent skipping
                // frames where there is genuinely nothing to render.
                if (!_fullInvalidation && _dirtyElements.Count == 0 && _dirtyFreeRects.Count == 0)
                {
                    // FLIP_SEQUENTIAL follow-up flush: nothing changed this frame, but a
                    // prior present painted the just-changed region onto the CURRENT back
                    // buffer only. Nothing else schedules a frame, so the alternate
                    // buffer(s) keep stale/blank pixels until a resize. Instead of
                    // skipping, re-present the dirty-history union onto the next buffer.
                    // _partialPresentsToFlush is armed after a real present (below) and a
                    // flush frame NEVER re-arms it, so it strictly decreases to 0.
                    if (_partialPresentsToFlush <= 0)
                    {
                        _debugHud.OnSkipped();
                        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.SKIP_NODIRTY);
                        if (DebugRender) System.Diagnostics.Debug.WriteLine("[RenderFrame] SKIP: no dirty, no fullInvalidation");
                        return;
                    }
                    isFlushFrame = true;
                }

                if (isFlushFrame)
                {
                    // A flush is always a partial-style present rebuilt from the history
                    // ring (see BuildFlushAggregator below) — never a full replay, which
                    // would reseed history mid-drain.
                    fullInvalidation = false;
                }
                else
                {
                    explicitFullInvalidation = _fullInvalidation;
                    fullInvalidation = _fullInvalidation || requiresFullReplay;
                    dirtyCountForHud = _dirtyElements.Count;
                    if (!rtActive)
                    {
                        // RC4-a 事务化消费：把累计脏状态换进帧捕获容器。此后帧内新到的
                        // AddDirtyElement（渲染回调、后台 timer）落入换上来的空活动集，
                        // 不再被旧代码"BeginDraw 成功即 Clear"吞掉；捕获集在 present
                        // 成功时 Discard、空 aggregator 时 RetainOrDrop、其余未 present
                        // 的退出经 finally MergeBack 归还。
                        SwapDirtyForFrame();
                        frameDirtySwapped = true;
                        frameFullSeq = _fullInvalidationSeq;
                    }
                    // render-thread 路径不 swap：PublishFrameToRenderThread 是全帧捕获，
                    // 自己在发布成功后清活动集（语义保持现状）。
                }
            }

            if (!isFlushFrame)
            {
                if (fullInvalidation) Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.FULL_FRAME);
                if (DebugRender)
                {
                    var reason = explicitFullInvalidation ? "_fullInvalidation" : requiresFullReplay ? "forceFullReplay" : "dirty";
                    System.Diagnostics.Debug.WriteLine($"[RenderFrame] path={( fullInvalidation ? "FULL" : "PARTIAL")} reason={reason} dirtyCount={dirtyCountForHud} fullInv={explicitFullInvalidation}");
                    if (frameDirtySwapped && _frameDirtyElements.Count > 0 && _frameDirtyElements.Count <= 10)
                    {
                        foreach (var (el, entry) in _frameDirtyElements)
                            System.Diagnostics.Debug.WriteLine($"  dirty: {el.GetType().Name} bounds={entry.PreLayoutBounds}");
                    }
                }

                if (!fullInvalidation && frameDirtySwapped)
                {
                    // 捕获集自 swap 起由 UI 线程独占——无锁读。render-thread 路径
                    // （未 swap）根本不需要 aggregator（全帧发布），直接跳过计算。
                    aggregator = ComputeDirtyRegions(_frameDirtyElements, _frameDirtyFreeRects);
                }
                _debugHud.SetDirtyInfo(dirtyCountForHud, aggregator?.GetBoundingBox() ?? Rect.Empty);
            }

            _debugHud.SetBackend(frameRenderTarget.Backend.ToString());
            _debugHud.SetEngine(frameRenderTarget.RenderingEngine.ToString());
            _debugHud.SetWindowSize(frameRenderTarget.Width, frameRenderTarget.Height);
            _debugHud.SetDpiScale((float)_dpiScale);

            // VSync 状态由 EnsureRenderTarget（JALIUM_DISABLE_VSYNC-gated，默认 ON）与
            // WM_ENTERSIZEMOVE/EXITSIZEMOVE 的 resize 开关统一拥有——不再每帧重设。
            // 历史教训：之前这里无条件 SetVSyncEnabled(false) 强制每帧 Present(0)，在核显
            // + 窗口 DWM 合成下让 swap-chain frame-latency waitable 错过 16ms 预算，
            // BeginDraw 的 16ms-超时重试循环每帧累积 ~100ms 卡顿（见 project memory frame-pacing）。

            var windowBounds = new Rect(0, 0, ActualWidth, ActualHeight);

            // Render-thread path (Increment 2): record the whole frame on this (UI)
            // thread and hand it to the render thread, which does BeginDraw/Replay/
            // EndDraw — so the ~110ms iGPU present never blocks the message pump.
            // Full-render only; DComp windows keep _rtActive=false (inline path).
            // FIX (CRASH): on the render-thread path the render thread EXCLUSIVELY
            // owns _drawingContext — creation,
            // DrainPendingRetainedLayers, draw, and TrimCacheIfNeeded all happen in
            // PresentCaptureOnRenderThread. The UI thread must NOT touch it; it
            // previously created / mutated / trimmed _drawingContext while the
            // render thread was mid-Replay into the same context → native brush
            // use-after-free + Dictionary corruption. So publish and return BEFORE
            // creating _drawingContext below.
            if (rtActive)
            {
                // The render-thread path is full-present-every-frame (it calls
                // SetFullInvalidation before each BeginDraw), so it has no partial
                // stale-buffer tail and never arms the flush counter. A flush frame
                // must not be published there — drop it defensively.
                if (isFlushFrame) { _partialPresentsToFlush = 0; return; }
                PublishFrameToRenderThread(frameLifecycleGeneration, frameRenderTarget);
                return;
            }

            // Inline (default) path: only reached when the render thread is off, so
            // the UI thread solely owns _drawingContext from here on.
            var context = ResolveRenderTargetContext(frameRenderTarget);
            _drawingContext ??= new RenderTargetDrawingContext(frameRenderTarget, context);
            // backdrop/LiquidGlass（snapshot/背景折射型）拖拽中仍降级：snapshot 滞后于 in-flight
            // resize buffer，真折射采到错位屏幕区域（玻璃"跑到面板外"）。glow/shadow 不受影响。
            _drawingContext.SimplifyBackdropEffects = _isSizing;
            // Destroy retained GPU layers orphaned by idle-eviction / detach since
            // the last frame (fence-gated native release) before drawing.
            _drawingContext.DrainPendingRetainedLayers();

            if (isFlushFrame)
            {
                // Rebuild the dirty region from the history ring (read-only — a flush
                // must NOT advance/overwrite history). This is the set of recently
                // presented regions the alternate FLIP buffer has not received yet.
                aggregator = BuildFlushAggregator(windowBounds);
                if (aggregator == null || aggregator.IsEmpty)
                {
                    _partialPresentsToFlush = 0;   // nothing to propagate — stop the chain
                    return;
                }
            }

            if (fullInvalidation)
            {
                // ── Full render path (structural: first frame, resize, theme change,
                //    device recovery, explicit RequestFullInvalidation, force-replay) ──
                // Full render refreshes the CURRENT buffer only. The other N-1
                // swap chain buffers still have stale content. Seed the dirty
                // history with the full window rect so the next N-1 partial frames
                // repaint everything on those buffers. 结构性帧是唯一的 seed 点
                // （全槽 + 重置写指针）：这类帧之后其余 back buffer 内容不可信
                // （ResizeBuffers 后 undefined）。present 前 seed 属过量声明、安全——
                // 失败时脏集经 finally 归还且 _fullInvalidation 未被 Discard 清掉，
                // 重试帧仍走 full。
                SeedDirtyHistoryFullWindow(windowBounds);
                frameRenderTarget.SetFullInvalidation();

                // TryBeginDraw: non-blocking.  If the GPU hasn't finished the
                // previous frame for this swap chain buffer, skip this frame
                // and let the UI thread process input messages instead.
                // Dirty state stays in the frame capture and is merged back by the
                // finally net, so the next attempt renders it.
                if (!TryBeginOwnedNativeDrawSession())
                {
                    return;
                }

                try
                {
                    _debugHud.OnFull();
                    ClearBackground(frameRenderTarget);
                    _drawingContext.Offset = Point.Zero;
                    RenderTreeOrCapture(_drawingContext);
                    if (AbortRenderFrameIfLifecycleChanged(frameLifecycleGeneration, frameRenderTarget)) return;
                    _debugHud.MarkRender();
                    DevToolsOverlay?.DrawOverlay(_drawingContext);
                    if (AbortRenderFrameIfLifecycleChanged(frameLifecycleGeneration, frameRenderTarget)) return;
                    OnRender(frameRenderTarget);
                    if (AbortRenderFrameIfLifecycleChanged(frameLifecycleGeneration, frameRenderTarget)) return;
                    // EndDraw/present 失败不再丢脏：直接 return，捕获集由 finally 归还。
                    if (!CompleteOwnedNativeDrawSession()) { return; }
                    // Present 成功——捕获的脏状态已经上屏，此刻才真正消费掉。
                    // RC4-c：结构性 full 跳过了 ComputeDirtyRegions（LastDirtyBounds 的
                    // 唯一常规写点），Discard 前必须补回写，否则首帧后缓存恒为陈旧值，
                    // 一次性 RenderTransform/RenderOffset 跳变的旧位置永无通道提交。
                    if (frameDirtySwapped)
                    {
                        UpdateLastDirtyBoundsAfterFullPresent();
                        DiscardFrameDirty(frameFullSeq);
                        frameDirtySwapped = false;
                    }
                    // A full present refreshes only the CURRENT back buffer; arm the
                    // follow-up flush so the alternate FLIP buffer(s) converge before the
                    // idle-skip stops further frames. (isFlushFrame is always false on the
                    // full path — flush frames force a partial present.)
                    HandlePresentedFrameFlush(isFlushFrame);
                    _debugHud.UpdateOverlay(_debugHudOverlay);
                }
                catch (RenderPipelineException ex)
                {
                    EndOwnedNativeDrawSessionBestEffort();
                    if (HandleRecoverableRenderPipelineFailure(ex, "RenderFrame"))
                    {
                        return;
                    }

                    throw;
                }
                catch
                {
                    EndOwnedNativeDrawSessionBestEffort();
                    throw;
                }
            }
            else if (aggregator == null || aggregator.IsEmpty)
            {
                // Dirty elements exist but their visible bounds are outside the window
                // (e.g., ProgressBar animating off-screen). Nothing to render — GPU idle.
                // RC4-a：不再整锅倒掉——layout 仍无效且仍挂本窗口的条目（新实化子树的
                // 首次失效常在 bounds 就绪前注册；MaxLayoutIterations 截断同型）保留到
                // 下帧限次重试，其余（layout 有效但真越界/零尺寸/脱树）照旧丢弃。
                if (frameDirtySwapped) { RetainOrDropFrameDirty(); frameDirtySwapped = false; }
                // This real frame presented nothing, but an earlier present may have armed a
                // follow-up flush whose alternate buffer still needs converging — keep it alive.
                RescheduleFlushIfArmed();
                return;
            }
            else
            {
                // ── Retained mode partial render ──
                Rect[]? rawSnapshot = null;
                double rawArea = 0;
                if (!isFlushFrame)
                {
                    // Capture this frame's raw rects BEFORE folding in history — we
                    // store the raw snapshot (what actually changed this frame) in
                    // the ring buffer; history folding is applied to the working
                    // aggregator only, so we don't compound history indefinitely.
                    rawSnapshot = aggregator.Rects.ToArray();
                    // RC2：promote 判定面积在 fold 之前取（本帧真实变更量）。旧代码在
                    // fold 之后测量——首帧 full 把 ring 种成 [W,W] 后，任何脏帧 fold
                    // 进全窗矩形 → 面积恒 >50% → promote → 再 seed 全 ring：吸收态
                    // 回路，partial 路径事实不可达，动画期间每帧整窗重绘。
                    rawArea = aggregator.ComputeRealArea();

                    // Fold in dirty regions from the last N-1 frames so that every
                    // FLIP_SEQUENTIAL buffer has its stale pixels repainted. Because
                    // aggregator absorbs redundant rects the fold is idempotent for
                    // regions that haven't changed.
                    for (int h = 0; h < DirtyHistoryCount; h++)
                    {
                        var history = _dirtyHistory[h];
                        if (history == null) continue;
                        foreach (var r in history) aggregator.Add(r);
                    }
                    if (LegacyPromoteBehavior)
                    {
                        // 逃生门：旧管线在 fold 时（present 前）推进 ring。BeginDraw 连败
                        // 两次会把仍被 N=3 收敛需要的 raw(K-1) 驱逐——正是新管线把提交挪
                        // 到 present 成功后（CommitDirtyHistory）要修的缺陷。
                        _dirtyHistory[_dirtyHistoryIndex] = rawSnapshot;
                        _dirtyHistoryIndex = (_dirtyHistoryIndex + 1) % DirtyHistoryCount;
                    }

                    // DPI-aware margin. Anti-aliased edges on high-density displays
                    // can exceed 2 device pixels; scale the DIP margin accordingly
                    // to avoid subpixel leaks outside the clip.
                    double margin = Math.Max(2.0, 2.0 * Math.Max(1.0, _dpiScale));
                    aggregator.Inflate(margin, windowBounds);

                    if (aggregator.IsEmpty)
                    {
                        // RC4-a：同上方空 aggregator 分支——选择性保留而非清空。
                        if (frameDirtySwapped) { RetainOrDropFrameDirty(); frameDirtySwapped = false; }
                        RescheduleFlushIfArmed();
                        return;
                    }
                }
                // NOTE: a flush frame arrives with an already-built, already-inflated
                // aggregator from BuildFlushAggregator and must NOT touch the history ring.

                // ── 50 % area check, measured against the TRUE covered pixel area
                //    of THIS FRAME's raw dirty set (pre-fold, see rawArea above), not
                //    the bounding box. A caret at (10,10) + a progress bar at (600,400)
                //    used to balloon the bounding box to the whole window; with
                //    union-area it measures only the two small regions. Promotion
                //    fires only when this frame's changes alone would touch > half
                //    the pixels of a full frame.
                double windowArea = ActualWidth * ActualHeight;
                // flush 帧不判 promote，其面积仅进 HUD（保持旧读数口径）；legacy 门
                // 恢复 fold 后判定。
                double judgedArea = (isFlushFrame || LegacyPromoteBehavior)
                    ? aggregator.ComputeRealArea()
                    : rawArea;
                // A flush frame must never promote to full: it is a targeted re-present
                // of already-known history regions and must not call
                // SeedDirtyHistoryFullWindow (which would reseed the ring mid-drain).
                bool promoteToFull = !isFlushFrame && windowArea > 0 && judgedArea > windowArea * 0.5;
                // 口径见 SetDirtyRegionStats 注释：rect 数 = fold 后、面积比 = 判定口径。
                _debugHud.SetDirtyRegionStats(
                    aggregator.Count,
                    windowArea > 0 ? judgedArea / windowArea : 0);

                if (promoteToFull)
                {
                    if (DebugRender)
                        System.Diagnostics.Debug.WriteLine(
                            $"[RenderFrame] PROMOTE raw={judgedArea:F0}px² win={windowArea:F0}px² ratio={judgedArea / windowArea:P0} rawRects={rawSnapshot!.Length}");
                    if (LegacyPromoteBehavior)
                    {
                        // 逃生门：旧管线 promote 前整 ring 重播为全窗（并重置写指针）——
                        // 第 K-1 帧刚写入的 raw 随即被覆盖，回路的第二半。
                        SeedDirtyHistoryFullWindow(windowBounds);
                    }
                    // RC2：新管线 promote 不再 seed ring（判定已不看 fold 后面积，ring
                    // 也不再被整体污染）；present 成功后 CommitDirtyHistory 提交单槽全窗
                    // 条目，在 DirtyHistoryCount 帧内让每个备用 FLIP buffer 各收到一次
                    // 全量重涂后自然老化。
                    frameRenderTarget.SetFullInvalidation();
                    if (!TryBeginOwnedNativeDrawSession()) { return; }
                    try
                    {
                        _debugHud.OnPromoted();
                        ClearBackground(frameRenderTarget);
                        _drawingContext.Offset = Point.Zero;
                        RenderTreeOrCapture(_drawingContext);
                        if (AbortRenderFrameIfLifecycleChanged(frameLifecycleGeneration, frameRenderTarget)) return;
                        _debugHud.MarkRender();
                        DevToolsOverlay?.DrawOverlay(_drawingContext);
                        if (AbortRenderFrameIfLifecycleChanged(frameLifecycleGeneration, frameRenderTarget)) return;
                        _debugHud.UpdateOverlay(_debugHudOverlay);
                        OnRender(frameRenderTarget);
                        if (AbortRenderFrameIfLifecycleChanged(frameLifecycleGeneration, frameRenderTarget)) return;
                        if (!CompleteOwnedNativeDrawSession()) { return; }
                        if (!LegacyPromoteBehavior)
                        {
                            // painted(=全窗) ⊇ changed：以上界入 ring，不依赖"全树重放
                            // 逐像素确定"的假设（backdrop/LiquidGlass 采样实况屏幕）。
                            CommitDirtyHistory(new[] { windowBounds });
                        }
                        if (frameDirtySwapped) { DiscardFrameDirty(frameFullSeq); frameDirtySwapped = false; }
                        // Promoted full present refreshes only the current buffer; arm the
                        // follow-up flush (isFlushFrame is always false — flush never promotes).
                        HandlePresentedFrameFlush(isFlushFrame);
                    }
                    catch (RenderPipelineException ex)
                    {
                        EndOwnedNativeDrawSessionBestEffort();
                        if (HandleRecoverableRenderPipelineFailure(ex, "RenderFrame"))
                        {
                            return;
                        }

                        throw;
                    }
                    catch
                    {
                        EndOwnedNativeDrawSessionBestEffort();
                        throw;
                    }
                }
                else
                {
                    // Submit every rect to the native RT — D3D12 uses them for
                    // Present1 DirtyRects (DWM copies the rest from the previous
                    // buffer), and the bounding box is still used as the D2D
                    // scissor clip because D2D clip stack takes a single rect.
                    foreach (var r in aggregator.EnumerateRects())
                    {
                        frameRenderTarget.AddDirtyRect(
                            (float)r.X, (float)r.Y,
                            (float)r.Width, (float)r.Height);
                    }

                    var clipRegion = Rect.Intersect(aggregator.GetBoundingBox(), windowBounds);
                    if (clipRegion.IsEmpty)
                    {
                        // RC4-a：同空 aggregator 分支——选择性保留（flush 帧未 swap，此处为 no-op）。
                        if (frameDirtySwapped) { RetainOrDropFrameDirty(); frameDirtySwapped = false; }
                        // Don't strand an armed follow-up flush on a degenerate clip — keep the
                        // FLIP convergence chain alive so the alternate buffer still converges.
                        RescheduleFlushIfArmed();
                        return;
                    }

                    if (!TryBeginOwnedNativeDrawSession()) { return; }
                    try
                    {
                        _debugHud.OnPartial();
                        _drawingContext.Offset = Point.Zero;
                        _drawingContext.PushDirtyRegionClip(clipRegion);

                        ClearBackground(frameRenderTarget, clipRegion);
                        RenderTreeOrCapture(_drawingContext);
                        if (AbortRenderFrameIfLifecycleChanged(frameLifecycleGeneration, frameRenderTarget))
                        {
                            try { _drawingContext.PopDirtyRegionClip(); } catch { }
                            return;
                        }
                        _debugHud.MarkRender();

                        _drawingContext.PopDirtyRegionClip();
                        DevToolsOverlay?.DrawOverlay(_drawingContext);
                        if (AbortRenderFrameIfLifecycleChanged(frameLifecycleGeneration, frameRenderTarget)) return;
                        _debugHud.UpdateOverlay(_debugHudOverlay);
                        OnRender(frameRenderTarget);
                        if (AbortRenderFrameIfLifecycleChanged(frameLifecycleGeneration, frameRenderTarget)) return;
                        // EndDraw/present 失败不再丢脏（旧代码 BeginDraw 后即清，EndDraw
                        // 失败只能靠 recovery 的整窗兜底）——return 交 finally 归还。
                        if (!CompleteOwnedNativeDrawSession()) { return; }
                        if (!isFlushFrame && !LegacyPromoteBehavior)
                        {
                            // RC2：ring 语义 = "最近 DirtyHistoryCount 个已成功 present 帧
                            // 的变更集上界"。提交移到 present 成功之后，BeginDraw/EndDraw
                            // 失败不再推进 ring——修掉连续两次 BeginDraw 失败驱逐仍被
                            // FLIP 收敛需要的 raw(K-1) 的现存漏洞。flush 帧只读 ring。
                            CommitDirtyHistory(rawSnapshot!);
                        }
                        if (frameDirtySwapped) { DiscardFrameDirty(frameFullSeq); frameDirtySwapped = false; }
                        // Real partial present painted only the current FLIP buffer → arm
                        // the follow-up flush. On a flush frame this instead drains one and
                        // chains the next (never re-arms → terminates).
                        HandlePresentedFrameFlush(isFlushFrame);
                    }
                    catch (RenderPipelineException ex)
                    {
                        EndOwnedNativeDrawSessionBestEffort();
                        if (HandleRecoverableRenderPipelineFailure(ex, "RenderFrame"))
                        {
                            return;
                        }

                        throw;
                    }
                    catch
                    {
                        EndOwnedNativeDrawSessionBestEffort();
                        throw;
                    }
                }
            }

            _drawingContext?.TrimCacheIfNeeded();

        }
        catch (RenderPipelineException ex)
        {
            // The frame may have consumed a present credit at BeginDraw without
            // reaching a successful Present — return it or the event-driven
            // scheduler deadlocks on a DWM retire that never comes. Over-return
            // is harmless (recovery's StartExternalPresentPacingIfSupported
            // resets to one credit anyway; a spurious extra degrades to a brief
            // DXGI queue wait inside Present).
            ReturnSwapCreditAfterFailedPresent();
            if (HandleRecoverableRenderPipelineFailure(ex, "RenderFrame"))
            {
                return;
            }

            LogRenderFailure(ex, "RenderFrame");
            throw;
        }
        catch (Exception ex)
        {
            // Same credit-conservation rule as the pipeline-exception path above:
            // a mid-frame failure after BeginDraw must not strand the credit.
            ReturnSwapCreditAfterFailedPresent();
            // Restore full invalidation so dirty elements aren't permanently lost.
            // Without this, the stale error frame stays on screen because the dirty
            // tracking was already cleared before rendering began.
            // （捕获集本身由下方 finally 归还——这里只需保证下一帧走 full。）
            lock (_dirtyLock)
            {
                MarkFullInvalidationLocked();
            }
            ScheduleRenderAfterRecovery();
            LogRenderFailure(ex, "RenderFrame");
            throw;
        }
        finally
        {
            EndOwnedNativeDrawSessionBestEffort();

            // RC4-a 单一出口兜底：任何未走到"present 成功 → Discard"或"空 aggregator
            // → RetainOrDrop"的退出（BeginDraw credit/fence 失败、EndDraw/present 失败、
            // 可恢复异常、rethrow）都在这里把帧捕获的脏状态并回活动集——失效不可能
            // 因失败而丢失，帧间"捕获容器恒空"不变量恒成立。
            if (frameDirtySwapped) MergeBackFrameDirty();
            lock (_renderLifecycleGate)
            {
                ClearRenderFlag(RenderFlag_Rendering);
            }
            CompletePendingManagedTeardown();
        }

        // If something requested a render during our rendering
        // (e.g., UpdateLayout triggered further invalidation, or rapid mouse-move
        // events arrived mid-frame), schedule another render cycle immediately.
        //
        // 不再因 CompositionTarget.IsActive 跳过。理由同 InvalidateWindow 的注释：
        // IsActive 仅表示有任意长寿命订阅者，不代表下一帧一定到。如果跳过，
        // 中键拖拽/快速 hover 等密集输入产生的 Requested 会被丢弃，表现为
        // 规律性卡顿——必须等下一次独立输入事件才能 InvalidateWindow。
        // InvalidateWindow 本身幂等（TrySetRenderFlag），重复调用不会重复渲染。
        if (HasRenderFlag(RenderFlag_Requested))
        {
            ClearRenderFlag(RenderFlag_Requested);
            InvalidateWindow();
        }
    }

    /// <summary>
    /// Builds a <see cref="DirtyRegionAggregator"/> containing every dirty region for
    /// this frame. Combines per-element pre-layout and post-layout bounds (so
    /// elements that moved during UpdateLayout repaint both old and new positions),
    /// the element's last actually-presented bounds (RC4-c erasure channel),
    /// precise sub-rects from <see cref="AddDirtyElement(UIElement, Rect)"/>,
    /// and free-floating rects from <see cref="AddDirtyRect"/>. Clamped to the
    /// window client area.
    /// 读的是 RC4-a 的帧捕获集——自 SwapDirtyForFrame 起到帧收尾为止由 UI 线程
    /// 独占，无需持 <see cref="_dirtyLock"/>。
    /// </summary>
    private DirtyRegionAggregator ComputeDirtyRegions(
        Dictionary<UIElement, DirtyElementEntry> elements, List<Rect> freeRects)
    {
        var agg = new DirtyRegionAggregator(capacity: 32);
        var windowBounds = new Rect(0, 0, ActualWidth, ActualHeight);

        foreach (var (element, entry) in elements)
        {
            if (entry.PreciseLocalRects is { Count: > 0 } preciseLocal)
            {
                // Map each local sub-rect into screen space through the element's FULL
                // local→screen matrix (ancestor + own RenderTransform), NOT a plain translate
                // by the screen origin: under scale/rotate the origin is the AABB corner, so a
                // translate would mis-place and un-scale the rect → under-coverage smear. With
                // no transform on the chain MapLocalRectToScreen is a pure translation, so this
                // is byte-identical to the old path in the common case. The pre-layout bounds
                // are still submitted below so vacated pixels get repainted.
                foreach (var local in preciseLocal)
                {
                    var screenRect = Rect.Intersect(element.MapLocalRectToScreen(local), windowBounds);
                    if (!screenRect.IsEmpty) agg.Add(screenRect);
                }
                // 内容级失效不动 bounds——LastDirtyBounds 缓存保持上次全框（恒为
                // 超集，过擦除无害），不在 precise 分支更新。
            }
            else
            {
                // Post-layout bounds: where the element IS now — transform- AND effect-aware
                // (RC4-c) so the dirty region follows an animating RenderTransform and covers
                // DropShadow/OuterGlow ink outside RenderSize instead of the static layout box.
                var newBounds = element.GetDirtyRenderBounds();
                var postLayoutBounds = Rect.Intersect(newBounds, windowBounds);
                if (!postLayoutBounds.IsEmpty) agg.Add(postLayoutBounds);
                // RC4-c：回写未 clip 的新 AABB 作"上次被计入 present 的位置"。present
                // 失败时 merge-back 保留条目侧 PrevPaintedBounds 快照，重试帧仍会提交
                // 旧位置——链条闭合，缓存的一帧偏差自愈。
                element.LastDirtyBounds = newBounds;
            }

            // Pre-layout bounds: where the element WAS before UpdateLayout.
            // Always submitted (even for precise-rect callers) so that elements
            // which moved or resized leave no stale pixels behind.
            if (!entry.PreLayoutBounds.IsEmpty)
            {
                var clipped = Rect.Intersect(entry.PreLayoutBounds, windowBounds);
                if (!clipped.IsEmpty) agg.Add(clipped);
            }

            // RC4-c：上次真实 present 覆盖过的位置。关闭"先改 RenderTransform/
            // RenderOffset 再失效 + 长空闲后不连续跳变 + 新 AABB 不含旧 AABB"的
            // 残影缺口（连续动画的上一帧位置本由 dirty-history fold 覆盖，此通道
            // 只兜不连续跳变）。
            if (!entry.PrevPaintedBounds.IsEmpty)
            {
                var clipped = Rect.Intersect(entry.PrevPaintedBounds, windowBounds);
                if (!clipped.IsEmpty) agg.Add(clipped);
            }
        }

        foreach (var free in freeRects)
        {
            var clipped = Rect.Intersect(free, windowBounds);
            if (!clipped.IsEmpty) agg.Add(clipped);
        }

        return agg;
    }

    /// <summary>
    /// Seeds every dirty-history slot with the full window rect, then resets
    /// the write index. RC2 之后只保留给结构性全量帧（首帧/resize/theme/设备恢复/
    /// 显式 RequestFullInvalidation/强制全重放）——这类帧之后其余 back buffer 内容
    /// 不可信，必须让接下来 N-1 个 FLIP_SEQUENTIAL buffer 全量重涂。promote 帧不再
    /// 调用（改为 present 成功后 CommitDirtyHistory 单槽全窗条目）。
    /// </summary>
    private void SeedDirtyHistoryFullWindow(Rect windowBounds)
    {
        var seed = new[] { windowBounds };
        for (int h = 0; h < DirtyHistoryCount; h++) _dirtyHistory[h] = seed;
        _dirtyHistoryIndex = 0;
    }

    /// <summary>
    /// RC2：向 dirty-history ring 提交一帧"已成功 present 的变更集上界"并推进写
    /// 指针。只在 present 成功之后调用（partial 帧提交 fold 前的 raw 快照；promote
    /// 帧提交单槽全窗条目），失败帧绝不推进——ring 语义由"最近种下的东西"修正为
    /// "最近 DirtyHistoryCount 个已成功 present 帧各自的变更集上界"。UI 线程独占。
    /// </summary>
    private void CommitDirtyHistory(Rect[] snapshot)
    {
        _dirtyHistory[_dirtyHistoryIndex] = snapshot;
        _dirtyHistoryIndex = (_dirtyHistoryIndex + 1) % DirtyHistoryCount;
    }

    /// <summary>
    /// RC4-a：把活动脏容器与帧捕获容器引用互换（O(1)，无拷贝）。必须在
    /// <see cref="_dirtyLock"/> 内调用；调用前捕获容器必须为空（帧间不变量，
    /// 由 Discard/RetainOrDrop/MergeBack 三个收尾路径共同维护）。
    /// </summary>
    private void SwapDirtyForFrame()
    {
        System.Diagnostics.Debug.Assert(
            _frameDirtyElements.Count == 0 && _frameDirtyFreeRects.Count == 0,
            "Frame-captured dirty state leaked from a previous frame — a RenderFrame exit path missed its resolve step.");
        (_dirtyElements, _frameDirtyElements) = (_frameDirtyElements, _dirtyElements);
        (_dirtyFreeRects, _frameDirtyFreeRects) = (_frameDirtyFreeRects, _dirtyFreeRects);
    }

    /// <summary>
    /// RC4-c：结构性 full present 不经过 ComputeDirtyRegions（那里是 LastDirtyBounds
    /// 的唯一常规写点），但捕获集里的元素同样被这次全帧 present 画上了屏。不回写会让
    /// "上次真实上屏位置"停留在陈旧值——首帧（必 full、全树入捕获集）后恒为 Empty，
    /// 此后对从未再动过的元素做一次性 RenderTransform/RenderOffset 跳变时，旧位置矩形
    /// 没有任何通道提交（ring 中的全窗条目两个 partial 后即老化），残影永不自愈。
    /// 与 ComputeDirtyRegions 的写点语义一致：precise 条目刻意不写（缓存保持上次全框
    /// 超集）；promote 路径已经跑过 ComputeDirtyRegions，无需调用本方法。
    /// </summary>
    private void UpdateLastDirtyBoundsAfterFullPresent()
    {
        foreach (var (element, entry) in _frameDirtyElements)
        {
            if (entry.PreciseLocalRects == null)
                element.LastDirtyBounds = element.GetDirtyRenderBounds();
        }
    }

    /// <summary>
    /// RC4-a：present 成功后的事务提交——清空捕获容器（保容量，稳态零分配），并在
    /// 帧中没有新的 full-invalidation 请求（序列号未变）时才清 _fullInvalidation。
    /// </summary>
    private void DiscardFrameDirty(long capturedFullSeq)
    {
        lock (_dirtyLock)
        {
            _frameDirtyElements.Clear();
            _frameDirtyFreeRects.Clear();
            if (_fullInvalidationSeq == capturedFullSeq)
                _fullInvalidation = false;
        }
    }

    /// <summary>
    /// RC4-a：未成功 present 的事务回滚——把捕获集并回活动集，与帧中新到的注册合并
    /// 后等下一次尝试。_fullInvalidation 不动（swap 不曾清它；失败路径若需要整窗兜底
    /// 由各自的 MarkFullInvalidationLocked 处理）。
    /// </summary>
    private void MergeBackFrameDirty()
    {
        lock (_dirtyLock)
        {
            if (_frameDirtyElements.Count > 0)
            {
                foreach (var (element, captured) in _frameDirtyElements)
                    MergeFrameEntryIntoActiveLocked(element, captured);
                _frameDirtyElements.Clear();
            }
            if (_frameDirtyFreeRects.Count > 0)
            {
                _dirtyFreeRects.AddRange(_frameDirtyFreeRects);
                _frameDirtyFreeRects.Clear();
            }
        }
    }

    /// <summary>
    /// 把一个帧捕获条目并回活动集。键冲突（帧中同一元素又注册了一次）时：
    /// PreLayoutBounds/PrevPaintedBounds 保留捕获侧——它更旧，是上次真实绘制的位置，
    /// 丢了会留残影；PreciseLocalRects 任一侧为 null（全元素脏）则 null，否则合并；
    /// RetryCount 取 max。必须在 <see cref="_dirtyLock"/> 内调用。
    /// </summary>
    private void MergeFrameEntryIntoActiveLocked(UIElement element, DirtyElementEntry captured)
    {
        if (_dirtyElements.TryGetValue(element, out var live))
        {
            live.PreLayoutBounds = captured.PreLayoutBounds;
            live.PrevPaintedBounds = captured.PrevPaintedBounds;
            if (live.PreciseLocalRects == null || captured.PreciseLocalRects == null)
                live.PreciseLocalRects = null;
            else
                live.PreciseLocalRects.AddRange(captured.PreciseLocalRects);
            if (captured.RetryCount > live.RetryCount)
                live.RetryCount = captured.RetryCount;
        }
        else
        {
            _dirtyElements[element] = captured;
        }
    }

    // RC4-a：空 bounds 保留重试的上限。保留只发生在 layout-invalid（LayoutManager
    // 队列必含其祖先链，下帧 UpdateLayout 有实质推进），RetryCount 单调递增、
    // 超限即弃——不可能死循环。
    private const int DirtyRetryLimit = 3;

    /// <summary>
    /// RC4-a：替代旧"aggregator 为空即整锅清脏"的三个吞失效点。谓词 = 元素 layout
    /// 仍无效 且 仍挂在本窗口 且 重试次数未超限 → 并回活动集并调度一次延迟渲染
    /// （幂等，RenderFlag_Scheduled 门）；其余条目（layout 有效但真越界/零尺寸/
    /// collapsed、已脱树、超限）直接丢弃——脱屏动画元素每帧注册-丢弃与旧行为一致，
    /// 不产生自发帧（perf 评审指名排除）。free rects 无 layout 状态可等，照旧丢弃。
    /// </summary>
    private void RetainOrDropFrameDirty()
    {
        bool retainedAny = false;
        lock (_dirtyLock)
        {
            if (_frameDirtyElements.Count > 0)
            {
                foreach (var (element, entry) in _frameDirtyElements)
                {
                    bool keep = (!element.IsMeasureValid || !element.IsArrangeValid)
                        && ReferenceEquals(element.GetWindowHostOrNull(), this)
                        && ++entry.RetryCount <= DirtyRetryLimit;
                    if (keep)
                    {
                        MergeFrameEntryIntoActiveLocked(element, entry);
                        retainedAny = true;
                    }
                    else if (DebugRender && entry.RetryCount > DirtyRetryLimit)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[RenderFrame] dirty element dropped after {DirtyRetryLimit} empty-bounds retries: {element.GetType().Name}");
                    }
                }
                _frameDirtyElements.Clear();
            }
            _frameDirtyFreeRects.Clear();
        }
        if (retainedAny) ScheduleDeferredRender(1);
    }

    // ── 测试访问器（DirtyPromotePipelineTests / DirtyRetentionTests /
    //    ArrangeDirtyRegistrationTests 的 headless 断言钩子）──
    internal Rect[]?[] DirtyHistoryForTests => _dirtyHistory;
    internal int DirtyHistoryIndexForTests => _dirtyHistoryIndex;
    internal (int full, int partial, int promoted, int skipped) RenderPathCountersForTests => _debugHud.CurrentCounters;
    internal Rect[] DirtyFreeRectsForTests
    {
        get { lock (_dirtyLock) { return _dirtyFreeRects.ToArray(); } }
    }

    /// <summary>
    /// Builds the dirty region for a FLIP_SEQUENTIAL "flush" frame: the union of
    /// every snapshot still in the dirty-history ring, inflated and clamped to the
    /// window. This is the set of regions recently presented to the current back
    /// buffer that the alternate buffer(s) have not yet received. Read-only over the
    /// ring — a flush must NOT advance or overwrite history. Returns <c>null</c> when
    /// the ring holds nothing to propagate.
    /// </summary>
    private DirtyRegionAggregator? BuildFlushAggregator(Rect windowBounds)
    {
        DirtyRegionAggregator? agg = null;
        for (int h = 0; h < DirtyHistoryCount; h++)
        {
            var history = _dirtyHistory[h];
            if (history == null) continue;
            foreach (var r in history)
            {
                var clipped = Rect.Intersect(r, windowBounds);
                if (clipped.IsEmpty) continue;
                agg ??= new DirtyRegionAggregator(capacity: 32);
                agg.Add(clipped);
            }
        }
        if (agg == null) return null;
        // Same DPI-aware margin as the normal partial path so the flush covers the
        // same anti-aliased fringe the original present did.
        double margin = Math.Max(2.0, 2.0 * Math.Max(1.0, _dpiScale));
        agg.Inflate(margin, windowBounds);
        return agg;
    }

    /// <summary>
    /// Follow-up-flush bookkeeping, called after a SUCCESSFUL present. On a REAL
    /// frame (<paramref name="isFlushFrame"/> = false) it arms (swapBufferCount - 1)
    /// flush frames so every other FLIP_SEQUENTIAL back buffer receives the
    /// just-presented content before the idle-skip halts rendering. On a FLUSH frame
    /// it consumes one and schedules the next if buffers remain. A flush frame NEVER
    /// re-arms, so the counter strictly decreases to 0 — no render loop. UI-thread
    /// only (called from RenderFrame).
    /// </summary>
    private void HandlePresentedFrameFlush(bool isFlushFrame)
    {
        if (isFlushFrame)
        {
            if (_partialPresentsToFlush > 0) _partialPresentsToFlush--;
            if (_partialPresentsToFlush > 0) ScheduleDeferredRender(1);
            return;
        }
        // Cover every OTHER back buffer. With kDefaultSwapBufferCount=2 this is a
        // single follow-up flush; a JALIUM_SWAPCHAIN_BUFFERS=3 override yields 2.
        // INVARIANT: the dirty-history ring must be deep enough to rebuild the
        // regions for every alternate buffer, i.e. DirtyHistoryCount >= swapBufferCount-1.
        // Native clamps swapBufferCount to [2, FrameCount=3] (d3d12_render_target.h),
        // so DirtyHistoryCount(=2) covers the deepest reachable config. Assert it so
        // raising the native buffer cap without growing the ring fails loudly in Debug
        // instead of silently re-introducing the stale-buffer bug on the deepest buffer.
        System.Diagnostics.Debug.Assert(
            DirtyHistoryCount >= _lastSwapBufferCount - 1,
            $"Dirty-history ring too shallow (DirtyHistoryCount={DirtyHistoryCount}) for " +
            $"swapBufferCount={_lastSwapBufferCount}: the deepest FLIP buffer would stay stale.");
        // Arm one flush per OTHER back buffer, but NEVER more than the dirty-history ring can
        // rebuild (BuildFlushAggregator unions exactly DirtyHistoryCount frames). In the
        // reachable config (native clamps N to [2, FrameCount=3]) N-1 ∈ {1,2} == DirtyHistoryCount,
        // so this clamp is a no-op today; it future-proofs a native FrameCount bump (the assert
        // above only fires in Debug) against re-introducing the stale-deepest-buffer bug in Release.
        _partialPresentsToFlush = Math.Clamp(_lastSwapBufferCount - 1, 1, DirtyHistoryCount);
        ScheduleDeferredRender(1);
    }

    /// <summary>
    /// Called from a RenderFrame early-return that did NOT present (empty aggregator / empty
    /// clip). If a FLIP_SEQUENTIAL follow-up flush is still armed, the alternate back buffer
    /// has not converged — post a deferred render so the armed counter is drained on a later
    /// frame. Without this, an armed flush whose drain frame hits an empty-region guard is
    /// stranded (nothing else schedules a frame in the default swap-pacing-off inline path) and
    /// the alternate buffer keeps stale pixels. Idempotent via the RenderFlag_Scheduled gate in
    /// ScheduleDeferredRender; a flush frame never re-arms, so the chain strictly terminates.
    /// UI-thread only.
    /// </summary>
    private void RescheduleFlushIfArmed()
    {
        if (_partialPresentsToFlush > 0)
            ScheduleDeferredRender(1);
    }

    /// <summary>
    /// Clears the render target with the window background color.
    /// When a D2D clip is active (retained mode), only the clipped area is cleared.
    /// </summary>
    private void ClearBackground(RenderTarget renderTarget)
    {
        ClearBackground(renderTarget, clipRegion: null);
    }

    /// <summary>
    /// Resolves the context that owns resources created for a render target.
    /// Production targets retain their creating context; the global-current
    /// fallback exists only for legacy/synthetic targets that predate explicit
    /// owner pinning.
    /// </summary>
    private RenderContext ResolveRenderTargetContext(RenderTarget renderTarget)
    {
        return renderTarget.OwnerContext ??
            RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
    }

    /// <summary>
    /// Clears the window background.  When <paramref name="clipRegion"/> is non-null
    /// a clip-aware fill is used instead of <c>D2D1::Clear</c>, which ignores D2D clips
    /// and would destroy transparent punch-through areas (e.g. WebView composition holes)
    /// outside the dirty region.
    /// </summary>
    private void ClearBackground(RenderTarget renderTarget, Rect? clipRegion)
    {
        if (clipRegion == null)
        {
            // Full render — D2D Clear is safe because the entire surface is redrawn.
            if (Background is SolidColorBrush solidFull)
            {
                var c = solidFull.Color;
                // Composition swap chain 用 DXGI_ALPHA_MODE_PREMULTIPLIED：app 必须提供已预乘
                // alpha 的 RGB（即 RGB *= A）。否则 Colors.Transparent = (R=255, G=255, B=255, A=0)
                // 这种 WPF 历史习惯的"透明白"会被 DWM 渲染为不透明白色（A=0 被弱化、RGB=255 直接显示）。
                // 在这里预乘后：(255, 255, 255, 0) → (0, 0, 0, 0)，PREMULTIPLIED 合法值，真透明。
                // 半透明色如 (255, 0, 0, 128) → (128, 0, 0, 128)，与 swap chain 期望一致。
                var a = c.A / 255f;
                renderTarget.Clear(c.R / 255f * a, c.G / 255f * a, c.B / 255f * a, a);
            }
            else if (SystemBackdrop != WindowBackdropType.None || AllowsTransparency)
            {
                renderTarget.Clear(0.0f, 0.0f, 0.0f, 0.0f);
            }
            else
            {
                renderTarget.Clear(0.0f, 0.0f, 0.0f, 1.0f);
            }
            return;
        }

        // Partial render — use clip-aware operations to avoid destroying content
        // outside the dirty region (D2D Clear ignores all clips).
        var r = clipRegion.Value;
        if (Background is SolidColorBrush solidPartial)
        {
            // For opaque backgrounds, FillRectangle is clip-aware and equivalent
            // to Clear within the clip region.  For semi-transparent backgrounds
            // PunchTransparentRect (D2D1_PRIMITIVE_BLEND_COPY) is needed to
            // overwrite rather than blend.
            if (solidPartial.Color.A == 255)
            {
                // 不透明色不需要预乘（A=1 时 RGB*A = RGB）
                var context = ResolveRenderTargetContext(renderTarget);
                using var brush = context.CreateSolidBrush(
                    solidPartial.Color.R / 255f,
                    solidPartial.Color.G / 255f,
                    solidPartial.Color.B / 255f,
                    solidPartial.Color.A / 255f);
                renderTarget.FillRectangle(
                    (float)r.X, (float)r.Y,
                    (float)r.Width, (float)r.Height,
                    brush);
            }
            else
            {
                renderTarget.PunchTransparentRect(
                    (float)r.X, (float)r.Y,
                    (float)r.Width, (float)r.Height);
                if (solidPartial.Color.A > 0)
                {
                    // 半透明色：RGB 必须预乘 alpha 才符合 PREMULTIPLIED swap chain 期望——
                    // 否则 Colors.Transparent = (255,255,255,0) 这种 WPF 历史"透明白"被 DWM
                    // 渲染成不透明白色。同 ClearBackground 全屏路径，跟着预乘。
                    var a = solidPartial.Color.A / 255f;
                    var context = ResolveRenderTargetContext(renderTarget);
                    using var brush = context.CreateSolidBrush(
                        solidPartial.Color.R / 255f * a,
                        solidPartial.Color.G / 255f * a,
                        solidPartial.Color.B / 255f * a,
                        a);
                    renderTarget.FillRectangle(
                        (float)r.X, (float)r.Y,
                        (float)r.Width, (float)r.Height,
                        brush);
                }
            }
        }
        else if (SystemBackdrop != WindowBackdropType.None || AllowsTransparency)
        {
            renderTarget.PunchTransparentRect(
                (float)r.X, (float)r.Y,
                (float)r.Width, (float)r.Height);
        }
        else
        {
            var context = ResolveRenderTargetContext(renderTarget);
            using var brush = context.CreateSolidBrush(0.0f, 0.0f, 0.0f, 1.0f);
            renderTarget.FillRectangle(
                (float)r.X, (float)r.Y,
                (float)r.Width, (float)r.Height,
                brush);
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
    private new void UpdateLayout()
    {
        Size availableSize = new(Width, Height);

        if (_isFirstLayout)
        {
            _isFirstLayout = false;
            // First-layout fast path bypasses the LayoutManager queue and times
            // measure / arrange directly so DevTools sees the same breakdown
            // shape on the first frame as on every subsequent one.
            long fStart = System.Diagnostics.Stopwatch.GetTimestamp();
            long fMeasureStart = fStart;
            Measure(availableSize);
            long fMeasureEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            Arrange(new Rect(0, 0, availableSize.Width, availableSize.Height));
            long fEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            PublishLayoutPassStatsFromTicks(
                totalTicks:   fEnd - fStart,
                measureTicks: fMeasureEnd - fMeasureStart,
                arrangeTicks: fEnd - fMeasureEnd,
                measureCount: 1, arrangeCount: 1, iterations: 1);
            return;
        }

        var result = _layoutManager.UpdateLayout(this, availableSize);
        PublishLayoutPassStatsFromTicks(
            result.TotalTicks, result.MeasureTicks, result.ArrangeTicks,
            result.MeasureCount, result.ArrangeCount, result.Iterations);
    }

    private static void PublishLayoutPassStatsFromTicks(
        long totalTicks, long measureTicks, long arrangeTicks,
        int measureCount, int arrangeCount, int iterations)
    {
        if (!Jalium.UI.Diagnostics.RenderDiagnostics.ApiStatsEnabled) return;
        // Stopwatch ticks → ns: tick_ns = 1e9 / Frequency. Multiplying first
        // keeps a 1 ns floor without overflow at Frequency = 10 MHz (typical
        // Windows QPC) for sub-second deltas.
        long freq = System.Diagnostics.Stopwatch.Frequency;
        long totalNs   = totalTicks   * 1_000_000_000L / freq;
        long measureNs = measureTicks * 1_000_000_000L / freq;
        long arrangeNs = arrangeTicks * 1_000_000_000L / freq;
        Jalium.UI.Diagnostics.RenderDiagnostics.PublishLayoutPassStats(
            new Jalium.UI.Diagnostics.RenderDiagnostics.LayoutPassFrameStats
            {
                Timestamp     = DateTime.Now,
                TotalNs       = totalNs,
                MeasureNs     = measureNs,
                ArrangeNs     = arrangeNs,
                MeasureCount  = measureCount,
                ArrangeCount  = arrangeCount,
                Iterations    = iterations,
            });
    }

    /// <summary>
    /// Detects the refresh rate of the monitor displaying this window.
    /// </summary>
    /// <returns>The refresh rate in Hz (e.g., 60, 120, 144), or 60 as fallback.</returns>
    private int DetectMonitorRefreshRate()
    {
        if (Handle == nint.Zero) return 60;

        // Cross-platform path
        if (_platformWindow != null)
            return _platformWindow.GetMonitorRefreshRate();

        // Win32 path
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
        if (_isClosing || Handle == nint.Zero) return;

        // While the window is minimized there is no surface to present to —
        // any dirty work just has to be replayed when the window is restored
        // (RequestFullInvalidation runs on resize anyway). Holding the dirty
        // flag avoids dropping invalidations that arrived during minimize.
        if (WindowState == WindowState.Minimized) return;

        if (HasRenderFlag(RenderFlag_DirtyBetween))
        {
            ClearRenderFlag(RenderFlag_DirtyBetween);
            if (TrySetRenderFlag(RenderFlag_Scheduled))
            {
                _dispatcher?.BeginInvokeCritical(ProcessRender);
            }
        }
    }

    /// <summary>
    /// Schedules a render via Dispatcher.BeginInvokeCritical (WPF-style).
    /// Implements IWindowHost.InvalidateWindow.
    ///
    /// Unlike InvalidateRect 鈫?WM_PAINT (which is low-priority and gets starved
    /// by posted messages from DispatcherTimer), this enqueues a render directly
    /// in the Dispatcher queue. ProcessQueue drains all items, so the render
    /// runs right after animation ticks in the same batch.
    ///
    /// iGPU optimization: when CompositionTarget is active (animations running),
    /// only allow renders triggered during the Rendering event phase.
    /// Mouse/interaction-triggered renders between frames are suppressed 鈥?
    /// dirty elements are batched into the next CompositionTarget frame.
    /// This prevents render storms on slow GPUs (200ms render + immediate
    /// mouse render + immediate timer render = frozen UI).
    /// </summary>
    public void InvalidateWindow()
    {
        if (Handle == nint.Zero) return;

        // During rendering, don't schedule — just flag for re-render after current frame
        if (HasRenderFlag(RenderFlag_Rendering))
        {
            SetRenderFlag(RenderFlag_Requested);
            return;
        }

        // Self-driven render loop (rewrite): bank the dirty for the next frame and ask
        // the central CompositionTarget loop to run. That loop ticks at the display
        // refresh rate whether or not the mouse moves; OnFrameStarting turns
        // RenderFlag_DirtyBetween into a RenderFrame on each tick, and the loop parks
        // (~0 CPU) once everything is clean and no animation remains.
        //
        // This replaces the old input-coupled path — a refresh-rate cap timer plus
        // BeginInvokeCritical(ProcessRender) — under which a static state change
        // (hover Background / IsSelected, a popup fade sample, a property update) with
        // no active animation sat unpainted until the next mouse move happened to pump
        // the dispatcher ("UI only renders while the mouse moves"). The refresh-rate
        // cap is now implicit: RenderFrame runs at most once per self-clocked tick, so
        // a 125 Hz hover burst still coalesces to one render per refresh interval.
        SetRenderFlag(RenderFlag_DirtyBetween);
        Jalium.UI.CompositionTarget.RequestFrame();
    }

    /// <summary>
    /// Adds a dirty element for partial rendering via native dirty rects.
    /// The element's full screen bounds are used.
    /// </summary>
    public void AddDirtyElement(UIElement element)
    {
        // Thread-safe: background threads (System.Threading.Timer callbacks from
        // ProgressBar, Storyboard, caret timers) call InvalidateVisual → AddDirtyElement.
        lock (_dirtyLock)
        {
            // Only capture pre-layout bounds on first registration per frame.
            // This preserves the true "old" position before UpdateLayout moves things.
            if (_dirtyElements.TryGetValue(element, out var entry))
            {
                // Already registered. A caller now wants full-element dirty, which
                // supersedes any precise sub-rect list we had.
                entry.PreciseLocalRects = null;
                return;
            }
            _dirtyElements[element] = new DirtyElementEntry
            {
                // Transform- AND effect-aware (RC4-c): capture where the element's ink
                // actually rasterizes — incl. any RenderTransform in effect plus the
                // Effect (DropShadow/Glow) padding overflow — so a moved/scaled/rotated
                // element leaves no stale pixels or shadow tails. No-transform,
                // no-effect chains fall back to GetScreenBounds (identical).
                PreLayoutBounds = element.GetDirtyRenderBounds(),
                PreciseLocalRects = null,
                // RC4-c：上一次真实被 present 覆盖的 AABB（不连续跳变擦除通道）。
                PrevPaintedBounds = element.LastDirtyBounds,
            };
        }
    }

    /// <summary>
    /// Adds a dirty element with a precise sub-rectangle in the element's local
    /// coordinate space. Multiple calls accumulate (each rect is stored), so
    /// several independent local regions can be marked dirty without promoting
    /// to the full element bounds.
    /// </summary>
    public void AddDirtyElement(UIElement element, Rect localDirtyRect)
    {
        if (localDirtyRect.IsEmpty || localDirtyRect.Width <= 0 || localDirtyRect.Height <= 0)
        {
            AddDirtyElement(element);
            return;
        }

        lock (_dirtyLock)
        {
            if (!_dirtyElements.TryGetValue(element, out var entry))
            {
                entry = new DirtyElementEntry
                {
                    // Transform- and effect-aware (see AddDirtyElement(element) overload).
                    PreLayoutBounds = element.GetDirtyRenderBounds(),
                    PreciseLocalRects = new List<Rect>(2),
                    PrevPaintedBounds = element.LastDirtyBounds,
                };
                _dirtyElements[element] = entry;
            }

            // If AddDirtyElement(element) was previously called in this frame we
            // are already tracking the full element — no point adding a sub-rect.
            if (entry.PreciseLocalRects == null) return;

            entry.PreciseLocalRects.Add(localDirtyRect);
        }
    }

    /// <summary>
    /// Adds a free-floating dirty rectangle in window (screen) coordinates.
    /// Used by animation / compositor systems that know what pixels changed
    /// but don't own a single <see cref="UIElement"/>.
    /// </summary>
    public void AddDirtyRect(Rect screenRect)
    {
        if (screenRect.IsEmpty || screenRect.Width <= 0 || screenRect.Height <= 0) return;
        lock (_dirtyLock) { _dirtyFreeRects.Add(screenRect); }
    }

    /// <summary>
    /// Requests a full invalidation of the window (e.g., after layout changes).
    /// </summary>
    public void RequestFullInvalidation()
    {
        // 与帧事务串行化：序列号自增让帧中到达的请求在 DiscardFrameDirty 的
        // 成功提交里存活（提交只在快照序列未变时清标志）。公开行为不变。
        lock (_dirtyLock)
        {
            MarkFullInvalidationLocked();
        }
    }

    /// <summary>
    /// 置 full-invalidation 标志并自增序列号。必须在 <see cref="_dirtyLock"/> 内
    /// 调用——所有直接置位点统一走这里，防止帧中置位被本帧 present 成功后的
    /// DiscardFrameDirty 顺手清掉（见 _fullInvalidationSeq）。
    /// </summary>
    private void MarkFullInvalidationLocked()
    {
        _fullInvalidation = true;
        _fullInvalidationSeq++;
    }

    /// <summary>
    /// Calls Win32 SetCapture so the window receives mouse messages even when the cursor is outside.
    /// </summary>
    public void SetNativeCapture()
    {
        if (Handle == nint.Zero) return;

        if (_platformWindow != null)
        {
            // Cross-platform: mouse capture managed at the framework level.
            // Native capture is a Win32 concept; on Linux/Android, pointer
            // events continue delivery to the focused window automatically.
            return;
        }

        SetCapture(Handle);
    }

    /// <summary>
    /// Calls Win32 ReleaseCapture to stop capturing mouse messages outside the window.
    /// </summary>
    public void ReleaseNativeCapture()
    {
        if (_platformWindow != null)
            return; // Cross-platform: no native capture to release

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

    private static bool IsShellReservedVirtualKey(nint wParam)
    {
        int virtualKey = (int)wParam;
        return virtualKey is VK_LWIN or VK_RWIN;
    }

    private bool OnNativeKeyDown(nint wParam, nint lParam)
    {
        Key key = KeyInterop.KeyFromVirtualKey((int)wParam);
        var modifiers = GetModifierKeys();
        bool isRepeat = ((lParam.ToInt64() >> 30) & 1) != 0;
        return _inputDispatcher.HandleKeyDown(key, modifiers, isRepeat, Environment.TickCount);
    }

    private UIElement GetKeyboardEventTarget()
    {
        var focusedElement = Keyboard.FocusedElement as UIElement;
        var dialogRoot = ActiveContentDialog;

        // Keep keyboard routing inside the active modal dialog whenever focus escaped it.
        return dialogRoot != null && (focusedElement == null || !IsDescendantOf(focusedElement, dialogRoot))
            ? dialogRoot
            : focusedElement ?? this;
    }

    private ContentDialog? FindContainingInPlaceDialog()
    {
        var focused = Keyboard.FocusedElement as Visual;
        for (var current = focused; current != null; current = current.VisualParent)
        {
            if (current is ContentDialog dialog && ActiveInPlaceDialogs.Contains(dialog))
            {
                return dialog;
            }
        }

        return null;
    }

    private UIElement? GetTextInputTarget()
    {
        var focusedElement = Keyboard.FocusedElement as UIElement;
        var dialogRoot = ActiveContentDialog;

        if (dialogRoot != null)
        {
            return focusedElement != null && IsDescendantOf(focusedElement, dialogRoot)
                ? focusedElement
                : dialogRoot;
        }

        return focusedElement;
    }

    private static bool IsDescendantOf(UIElement descendant, UIElement ancestor)
    {
        int depthGuard = 0;
        for (Visual? current = descendant; current != null && depthGuard++ < 4096; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// Toggles the DevTools window for this window.
    /// Press F12 to open/close DevTools in DEBUG builds.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevToolsWindow includes a REPL and inspector that reflect on user types.")]
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "DevTools is an opt-in developer surface. Production AOT applications do not call OpenDevTools; consumers that do must keep DevTools reflection roots alive themselves.")]
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

    private bool OnNativeKeyUp(nint wParam, nint lParam)
    {
        Key key = KeyInterop.KeyFromVirtualKey((int)wParam);
        var modifiers = GetModifierKeys();
        return _inputDispatcher.HandleKeyUp(key, modifiers, Environment.TickCount);
    }

    private void OnActivateChanged(int activateState, nint newForegroundWindow)
    {
        if (activateState == WA_INACTIVE)
        {
            if (IsActive)
            {
                SetIsActive(false);
                OnDeactivated(EventArgs.Empty);
            }
            // Match WPF semantics: a window losing activation must NOT drop the logical
            // keyboard focus. The focused element stays focused so that re-activation
            // restores input naturally, and so that transient activation flickers
            // (briefly-shown tooltips, IME windows, system popups stealing focus for a
            // single message pump cycle) don't visibly tear down the focus visual or
            // strand the user mid-Tab. Clearing focus here was the root cause of focus
            // rings disappearing one frame after each Tab landed on a NavigationViewItem.
            _inputDispatcher.HandleWindowDeactivated(newForegroundWindow, clearKeyboardFocus: false);
            return;
        }

        if (!IsActive)
        {
            SetIsActive(true);
            OnActivated(EventArgs.Empty);
        }
        _inputDispatcher.ArmEscapeSuppressionIfNeeded();
        WakeRenderPipeline();
    }

    private void OnCancelMode()
    {
        _inputDispatcher.HandleCancelMode();
    }

    private void OnKillFocus(nint newFocusWindow)
    {
        // Same rationale as OnActivateChanged: WM_KILLFOCUS arrives on activation
        // transitions and on transient focus thefts (popup windows, IME). Preserve
        // the logical keyboard focus so the next WM_SETFOCUS resumes seamlessly.
        _inputDispatcher.HandleWindowDeactivated(newFocusWindow, clearKeyboardFocus: true);
    }

    private void OnSetFocus()
    {
        _inputDispatcher.HandleSetFocus();
    }

    private void HandleWindowDeactivated(nint newForegroundWindow, bool clearKeyboardFocus)
    {
        CloseLightDismissPopupsOnDeactivate(newForegroundWindow);
        ResetTransientInputStateOnDeactivate();

        if (clearKeyboardFocus)
        {
            Keyboard.ClearFocus();
        }

        UpdateInputMethodAssociation();
        WakeRenderPipeline();
    }

    private void CloseLightDismissPopupsOnDeactivate(nint newForegroundWindow)
    {
        if (PopupWindow.IsPopupWindow(newForegroundWindow))
        {
            return;
        }

        _ = OverlayLayer.CloseLightDismissPopups();

        if (ActiveExternalPopups.Count == 0)
        {
            return;
        }

        var popupsToClose = ActiveExternalPopups
            .Where(p => !p.StaysOpen)
            .ToList();
        foreach (var popup in popupsToClose)
        {
            popup.IsOpen = false;
        }
    }

    private void ResetTransientInputStateOnDeactivate()
    {
        UIElement.ForceReleaseMouseCapture();
        ClearPressedChains();

        if (TitleBarStyle == WindowTitleBarStyle.Custom)
        {
            ClearTitleBarInteractionState();
        }
    }

    private void ArmEscapeSuppressionIfNeeded()
    {
        _suppressEscapeUntilTick = IsVirtualKeyDown(VK_ESCAPE)
            ? Environment.TickCount64 + EscapeReactivateSuppressionMs
            : 0;
    }

    private void WakeRenderPipeline()
    {
        RequestFullInvalidation();

        if (Handle == nint.Zero || _dispatcher == null || _isClosing)
        {
            return;
        }

        if (HasRenderFlag(RenderFlag_Rendering))
        {
            SetRenderFlag(RenderFlag_Requested);
            return;
        }

        if (TrySetRenderFlag(RenderFlag_Scheduled))
        {
            _dispatcher.BeginInvokeCritical(ProcessRender);
        }
    }

    private bool ShouldSuppressReactivatedEscape(Key key, bool isKeyDown)
    {
        if (key != Key.Escape)
        {
            return false;
        }

        long suppressUntilTick = _suppressEscapeUntilTick;
        if (suppressUntilTick == 0)
        {
            return false;
        }

        if (Environment.TickCount64 > suppressUntilTick)
        {
            _suppressEscapeUntilTick = 0;
            return false;
        }

        if (!isKeyDown)
        {
            _suppressEscapeUntilTick = 0;
        }

        return true;
    }

    private void OnChar(nint wParam, nint lParam)
    {
        char c = (char)(int)wParam;
        if (char.IsControl(c) && c != '\r' && c != '\t')
            return;

        _inputDispatcher.HandleCharInput(c.ToString(), Environment.TickCount);
    }

    #region IME Handling

    private void OnImeStartComposition()
    {
        if (!TryGetImeTarget(out _, out _))
        {
            return;
        }

        InputMethod.StartComposition();

        // Position the IME composition window near the caret
        UpdateImeCompositionWindow();
    }

    private void OnImeEndComposition()
    {
        InputMethod.EndComposition();
        UpdateInputMethodAssociation();
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
                    var target = GetTextInputTarget();
                    if (target != null)
                    {
                        TextCompositionEventArgs args = new(TextInputEvent, resultStr, Environment.TickCount);
                        target.RaiseEvent(args);
                    }
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

    private void OnWindowKeyboardFocusChanged(object? sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateInputMethodAssociation();

        // Forward focus through the active platform accessibility sink. On Windows the
        // sink is armed lazily by WM_GETOBJECT; on Linux it is armed only after the
        // application has joined the AT-SPI2 bus. Keep the callback deferred because
        // Windows UIA cannot call out during an input-synchronous focus message.
        if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) &&
            e.NewFocus is UIElement focused)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var peer = focused.GetAutomationPeer();
                if (peer != null)
                    Jalium.UI.Automation.Peers.AutomationPeer.EventSink?.OnFocusChanged(peer);
            });
        }
    }

    private bool CanHandleImeMessages()
        => TryGetImeTarget(out _, out _);

    private bool TryGetImeTarget(out UIElement? target, out IImeSupport? imeSupport)
    {
        target = Keyboard.FocusedElement as UIElement;
        if (target is not IImeSupport support)
        {
            imeSupport = null;
            return false;
        }

        if (!InputMethod.GetIsInputMethodEnabled(target))
        {
            imeSupport = null;
            return false;
        }

        // Honour IsImeAllowed=false (e.g. IsReadOnly text controls) — the
        // IME context is detached below in UpdateInputMethodAssociation so
        // the candidate window never opens for input that will be ignored.
        if (!support.IsImeAllowed)
        {
            imeSupport = null;
            return false;
        }

        imeSupport = support;
        return true;
    }

    /// <summary>
    /// Re-evaluates whether the focused element wants IME input and attaches
    /// or detaches the window's IMM context accordingly. Call this after
    /// changing properties that influence <see cref="IImeSupport.IsImeAllowed"/>
    /// (most commonly <c>IsReadOnly</c>) so the IME candidate window appears
    /// or disappears immediately rather than only on the next focus change.
    /// </summary>
    public void RefreshInputMethodAssociation()
    {
        UpdateInputMethodAssociation();
    }

    private void UpdateInputMethodAssociation()
    {
        // IMM32 is the Windows IME transport. Linux input-method association is
        // handled by its platform backend and must never enter these P/Invokes.
        if (!OperatingSystem.IsWindows() || Handle == nint.Zero)
        {
            return;
        }

        bool shouldEnableIme = CanHandleImeMessages();
        if (!shouldEnableIme && InputMethod.IsComposing)
        {
            InputMethod.CancelComposition();
        }

        if (shouldEnableIme)
        {
            if (!_imeContextDetached)
            {
                return;
            }

            if (_detachedImeContext != nint.Zero)
            {
                _ = ImmNativeMethods.ImmAssociateContext(Handle, _detachedImeContext);
            }
            else
            {
                _ = ImmNativeMethods.ImmAssociateContextEx(Handle, nint.Zero, IACE_DEFAULT);
            }

            _detachedImeContext = nint.Zero;
            _imeContextDetached = false;
            return;
        }

        if (_imeContextDetached)
        {
            return;
        }

        _detachedImeContext = ImmNativeMethods.ImmAssociateContext(Handle, nint.Zero);
        _imeContextDetached = true;
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
        var buttons = new MouseButtonStates
        {
            Left = left, Middle = middle, Right = right,
            XButton1 = xButton1, XButton2 = xButton2
        };
        _inputDispatcher.HandleMouseMove(position, buttons, modifiers, Environment.TickCount);
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
                MouseEventArgs args = new(MouseLeaveEvent) { Source = uiElement };
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
            MouseEventArgs args = new(MouseEnterEvent) { Source = uiElement };
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
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        var buttons = new MouseButtonStates
        {
            Left = left, Middle = middle, Right = right,
            XButton1 = xButton1, XButton2 = xButton2
        };
        _inputDispatcher.HandleMouseDown(button, position, buttons, modifiers, clickCount, Environment.TickCount);
    }

    private void OnMouseButtonUp(MouseButton button, nint wParam, nint lParam)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        var buttons = new MouseButtonStates
        {
            Left = left, Middle = middle, Right = right,
            XButton1 = xButton1, XButton2 = xButton2
        };
        _inputDispatcher.HandleMouseUp(button, position, buttons, modifiers, Environment.TickCount);
    }

    private void UpdateMouseOverState(UIElement? newMouseOverElement, int timestamp)
    {
        if (newMouseOverElement == _lastMouseOverElement)
        {
            return;
        }

        if (_lastMouseOverElement != null)
        {
            RaiseMouseLeaveChain(_lastMouseOverElement, newMouseOverElement, timestamp);
        }

        if (newMouseOverElement != null)
        {
            RaiseMouseEnterChain(newMouseOverElement, _lastMouseOverElement, timestamp);
        }

        _lastMouseOverElement = newMouseOverElement;
    }

    private MenuItem? HitTopLevelMenuItemBehindOverlay(Point windowPosition)
    {
        var hitElement = HitIgnoringOverlay(windowPosition)?.VisualHit as UIElement;
        return FindTopLevelMenuItemAncestor(hitElement);
    }

    private HitTestResult? HitIgnoringOverlay(Point windowPosition)
    {
        // Window.GetVisualChild order is topmost-last, so iterate reverse to preserve hit-test priority.
        for (int i = VisualChildrenCount - 1; i >= 0; i--)
        {
            if (GetVisualChild(i) is not FrameworkElement fe || fe == OverlayLayer)
            {
                continue;
            }

            var hit = fe.HitTest(windowPosition);
            if (hit != null)
            {
                return hit;
            }
        }

        return null;
    }

    private UIElement? HitTestElement(Point windowPosition, string source = "hit-test")
    {
        // Bring layout up to date before reading any element's _visualBounds /
        // _cachedScreenOffset. Mirrors WPF's MouseDevice.Synchronize →
        // ContextLayoutManager.UpdateLayout pattern: an input event that arrives
        // between an InvalidateArrange call and the next render frame would
        // otherwise hit-test against stale geometry (the bug that forced the
        // previous TryHitTestCachedSubtree fast path to be disabled).
        EnsureLayoutValidForInput();

        long generation = _layoutManager.Generation;
        // Popup/modal roots can be attached to OverlayLayer between two input messages without
        // changing the owner's normal content-layout generation.  Never reuse an underlying-tree
        // hit while an overlay is active; the overlay must receive first hit-test priority.
        bool overlayRequiresFreshHit = OverlayLayer.HasPopupRoots ||
                                       OverlayLayer.HasModalRoots ||
                                       ActiveInPlaceDialogs.Count != 0;
        if (!overlayRequiresFreshHit &&
            generation == _hitMemoLayoutGeneration &&
            _hitMemoPoint == windowPosition &&
            (_hitMemoElement == null || IsElementAttachedToThisWindow(_hitMemoElement)))
        {
            return _hitMemoElement;
        }

        var hitElement = HitTest(windowPosition)?.VisualHit as UIElement;

        _hitMemoLayoutGeneration = generation;
        _hitMemoPoint = windowPosition;
        _hitMemoElement = hitElement;

        return hitElement;
    }

    private bool IsElementAttachedToThisWindow(UIElement element)
    {
        for (Visual? current = element; current != null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, this))
                return true;
        }

        return false;
    }

    // Flush any queued measure/arrange so input handlers see the same layout
    // the next render will. No-op when the window is closing or the queue is
    // empty — the common steady-state path stays free of layout work.
    internal void EnsureLayoutValidForInput()
    {
        if (_isClosing || Handle == nint.Zero)
            return;

        if (!_layoutManager.HasPendingLayout && !_isFirstLayout)
            return;

        UpdateLayout();
    }

    private static MenuItem? FindTopLevelMenuItemAncestor(UIElement? element)
    {
        var current = element;
        while (current != null)
        {
            if (current is MenuItem menuItem
                && menuItem.VisualParent is Panel panel
                && panel.VisualParent is Menu)
            {
                return menuItem;
            }

            current = current.VisualParent as UIElement;
        }

        return null;
    }

    // Light dismiss is now handled by OverlayLayer.TryHandleLightDismiss()

    private void OnMouseWheel(nint wParam, nint lParam)
    {
        // WM_MOUSEWHEEL lParam contains SCREEN coordinates (physical pixels).
        int screenX = (short)(lParam.ToInt64() & 0xFFFF);
        int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = screenX, Y = screenY };
        _ = ScreenToClient(Handle, ref pt);
        Point position = new(pt.X / _dpiScale, pt.Y / _dpiScale);

        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        var buttons = new MouseButtonStates
        {
            Left = left, Middle = middle, Right = right,
            XButton1 = xButton1, XButton2 = xButton2
        };
        _inputDispatcher.HandleMouseWheel(position, delta, buttons, modifiers, Environment.TickCount);
    }

    private void OnPointerMessage(uint msg, nint wParam, nint lParam)
    {
        if (!Win32PointerInterop.TryGetPointerData(Handle, wParam, _dpiScale, out var pointerData))
            return;
        if (pointerData.Kind == PointerInputKind.Mouse)
            return;

        bool isDown = msg == Win32PointerInterop.WM_POINTERDOWN;
        bool isUp = msg == Win32PointerInterop.WM_POINTERUP;
        _inputDispatcher.HandlePointerInput(pointerData, isDown, isUp, Environment.TickCount);
    }

    private void OnPointerWheel(nint wParam, nint lParam)
    {
        if (!Win32PointerInterop.TryGetPointerData(Handle, wParam, _dpiScale, out var pointerData))
            return;
        if (pointerData.Kind == PointerInputKind.Mouse)
            return;
        _inputDispatcher.HandlePointerWheel(pointerData, Environment.TickCount);
    }

    private void OnPointerCaptureChanged(nint wParam)
    {
        uint pointerId = Win32PointerInterop.GetPointerId(wParam);
        _inputDispatcher.HandlePointerCaptureChanged(pointerId, Environment.TickCount);
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

        // --- Touch → Stylus promotion ---
        // Feed touch input through the RealTimeStylus / StylusPlugIn pipeline so that
        // controls like InkCanvas (which rely on StylusPlugIns) work with touch.
        PromoteTouchToStylus(target, pointerData, isDown, isUp, timestamp);

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

            // Clean up the promoted stylus device.
            _activeStylusDevices.Remove(pointerData.PointerId);
        }
    }

    /// <summary>
    /// Promotes a touch pointer into the Stylus pipeline so that StylusPlugIns
    /// (DynamicRenderer, InkCollectionStylusPlugIn, etc.) receive the input.
    /// </summary>
    private void PromoteTouchToStylus(
        UIElement target,
        PointerInputData pointerData,
        bool isDown,
        bool isUp,
        int timestamp)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new StylusDevice((int)pointerData.PointerId, $"Touch{pointerData.PointerId}");
            _activeStylusDevices[pointerData.PointerId] = stylusDevice;
        }

        var properties = pointerData.Point.Properties;
        float pressure = properties.Pressure;

        stylusDevice.UpdateState(
            pointerData.Position,
            pointerData.StylusPoints,
            inAir: !pointerData.Point.IsInContact,
            inverted: false,
            inRange: pointerData.IsInRange,
            barrelPressed: false,
            eraserPressed: false,
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
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: pointerData.IsCanceled);

        // Update stylus device with any modifications made by plugins.
        stylusDevice.UpdateState(
            pointerData.Position,
            processResult.RawStylusInput.GetStylusPoints(),
            inAir: !pointerData.Point.IsInContact,
            inverted: false,
            inRange: pointerData.IsInRange,
            barrelPressed: false,
            eraserPressed: false,
            directlyOver: target);

        // Raise Stylus RoutedEvents (Preview + Bubble) so handlers see touch as stylus.
        RoutedEvent previewEvent = isDown ? PreviewStylusDownEvent : (isUp ? PreviewStylusUpEvent : PreviewStylusMoveEvent);
        RoutedEvent bubbleEvent = isDown ? StylusDownEvent : (isUp ? StylusUpEvent : StylusMoveEvent);

        StylusEventArgs previewArgs = CreateStylusEventArgs(stylusDevice, timestamp, previewEvent, isDown);
        target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled && !processResult.Canceled)
        {
            StylusEventArgs bubbleArgs = CreateStylusEventArgs(stylusDevice, timestamp, bubbleEvent, isDown);
            target.RaiseEvent(bubbleArgs);
        }

        _realTimeStylus.QueueProcessedCallbacks(processResult);
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
        if (IsVirtualKeyDown(VK_SHIFT))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsVirtualKeyDown(VK_CONTROL))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsVirtualKeyDown(VK_MENU))
        {
            modifiers |= ModifierKeys.Alt;
        }

        return modifiers;
    }

    internal static void SetKeyStateProviderForTesting(Func<int, short>? provider)
    {
        s_getKeyStateProvider = provider ?? GetKeyState;
    }

    private static bool IsVirtualKeyDown(int nVirtKey)
    {
        return (s_getKeyStateProvider(nVirtKey) & 0x8000) != 0;
    }

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);

    #endregion

    #region Win32 Interop
    private static Func<int, short> s_getKeyStateProvider = GetKeyState;
    #endregion

    #region IInputDispatcherHost

    Window IInputDispatcherHost.Self => this;
    nint IInputDispatcherHost.Handle => Handle;
    double IInputDispatcherHost.DpiScale => _dpiScale;

    UIElement? IInputDispatcherHost.HitTestElement(Point windowPosition, string tag) => HitTestElement(windowPosition, tag);
    HitTestResult? IInputDispatcherHost.HitIgnoringOverlay(Point windowPosition) => HitIgnoringOverlay(windowPosition);

    OverlayLayer IInputDispatcherHost.OverlayLayer => OverlayLayer;
    IReadOnlyList<Popup> IInputDispatcherHost.ActiveExternalPopups => ActiveExternalPopups;
    ContentDialog? IInputDispatcherHost.ActiveContentDialog => ActiveContentDialog;
    IReadOnlyList<ContentDialog> IInputDispatcherHost.ActiveInPlaceDialogs => ActiveInPlaceDialogs;

    bool IInputDispatcherHost.IsTitleBarVisible() => IsTitleBarVisible();
    TitleBarButton? IInputDispatcherHost.GetTitleBarButtonAtPoint(Point point, double windowWidth) => GetTitleBarButtonAtPoint(point, windowWidth);
    WindowTitleBarStyle IInputDispatcherHost.TitleBarStyle => TitleBarStyle;
    TitleBar? IInputDispatcherHost.TitleBar => TitleBar;

    UIElement IInputDispatcherHost.GetKeyboardEventTarget() => GetKeyboardEventTarget();
    UIElement? IInputDispatcherHost.GetTextInputTarget() => GetTextInputTarget();
    ContentDialog? IInputDispatcherHost.FindContainingInPlaceDialog() => FindContainingInPlaceDialog();
    Button? IInputDispatcherHost.FindButton(UIElement root, Func<Button, bool> predicate) => FindButton(root, predicate);

    bool IInputDispatcherHost.CanOpenDevTools => CanOpenDevTools;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "DevTools opt-in; only reached via explicit F12 / manual host plumbing.")]
    void IInputDispatcherHost.ToggleDevTools() => ToggleDevTools();

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "DevTools opt-in; production AOT consumers do not invoke this path.")]
    void IInputDispatcherHost.OpenDevTools() => OpenDevTools();

    void IInputDispatcherHost.ActivateDevToolsPicker() => _devToolsWindow?.ActivatePicker();

    bool IInputDispatcherHost.CanToggleDebugHud
        => Jalium.UI.Hosting.DeveloperToolsResolver.IsDebugHudEnabled;

    bool IInputDispatcherHost.DebugHudEnabled
    {
        get => _debugHud.Enabled;
        set => _debugHud.Enabled = value;
    }

    Visibility IInputDispatcherHost.DebugHudOverlayVisibility
    {
        set => _debugHudOverlay.Visibility = value;
    }

    /// <summary>
    /// Gets the per-window frame history ring buffer used by DevTools for trend plots.
    /// Populated on every completed frame by the render HUD.
    /// </summary>
    public Jalium.UI.Diagnostics.FrameHistory FrameHistory => _debugHud.FrameHistory;

    /// <summary>
    /// Hot-switches the rendering engine (Vello/Impeller/Auto) for this window's render target.
    /// Falls through silently if the render target is not yet created.
    /// </summary>
    public void SetRenderingEngineOverride(Jalium.UI.Interop.RenderingEngine engine)
    {
        RenderTarget?.SetRenderingEngine(engine);
        RequestFullInvalidation();
        InvalidateWindow();
    }

    /// <summary>
    /// Returns the active rendering engine for this window, or Auto if no target is bound.
    /// </summary>
    public Jalium.UI.Interop.RenderingEngine CurrentRenderingEngine
        => RenderTarget?.RenderingEngine ?? Jalium.UI.Interop.RenderingEngine.Auto;

    /// <summary>
    /// Returns the active graphics backend (D3D12/Vulkan/Metal/Software).
    /// </summary>
    public Jalium.UI.Interop.RenderBackend CurrentRenderBackend
        => RenderTarget?.Backend ?? Jalium.UI.Interop.RenderBackend.Auto;

    bool IInputDispatcherHost.OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat) => OnPreviewWindowKeyDown(key, modifiers, isRepeat);
    bool IInputDispatcherHost.OnPreviewWindowKeyUp(Key key, ModifierKeys modifiers) => OnPreviewWindowKeyUp(key, modifiers);
    bool IInputDispatcherHost.OnPreviewWindowMouseDown(MouseButton button, Point position, int clickCount) => OnPreviewWindowMouseDown(button, position, clickCount);
    bool IInputDispatcherHost.OnPreviewWindowMouseUp(MouseButton button, Point position) => OnPreviewWindowMouseUp(button, position);
    bool IInputDispatcherHost.OnPreviewWindowMouseMove(Point position) => OnPreviewWindowMouseMove(position);
    bool IInputDispatcherHost.OnPreviewWindowMouseWheel(int delta, Point position) => OnPreviewWindowMouseWheel(delta, position);

    void IInputDispatcherHost.InvalidateWindow() => InvalidateWindow();
    void IInputDispatcherHost.RequestFullInvalidation() => RequestFullInvalidation();

    void IInputDispatcherHost.RequestTrackMouseLeave()
    {
        if (PlatformFactory.IsWindows && Handle != nint.Zero)
        {
            TRACKMOUSEEVENT tme = new()
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE,
                hwndTrack = Handle,
                dwHoverTime = 0
            };
            _ = TrackMouseEvent(ref tme);
        }
    }

    void IInputDispatcherHost.SetPlatformCursor(int cursorType)
    {
        _platformWindow?.SetCursor(cursorType);
    }

    void IInputDispatcherHost.UpdateInputMethodAssociation() => UpdateInputMethodAssociation();

    bool IInputDispatcherHost.IsPopupWindow(nint hwnd) => Primitives.PopupWindow.IsPopupWindow(hwnd);
    bool IInputDispatcherHost.IsVirtualKeyDown(int nVirtKey) => IsVirtualKeyDown(nVirtKey);
    void IInputDispatcherHost.WakeRenderPipeline() => WakeRenderPipeline();

    Jalium.UI.Input.StylusPlugIns.RealTimeStylus IInputDispatcherHost.RealTimeStylus => _realTimeStylus;

    #endregion
}

/// <summary>
/// Specifies the state of a window.
/// </summary>
public enum WindowState
{
    Normal,
    Minimized,
    Maximized,
    /// <summary>
    /// The window occupies the entire screen with no borders or title bar.
    /// </summary>
    FullScreen
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
    /// Only the client area is visible 鈥?the title bar and border are not shown.
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

/// <summary>
/// Specifies the position that a Window will be shown in when it is first opened.
/// </summary>
public enum WindowStartupLocation
{
    /// <summary>
    /// The startup location of a Window is set from code, or defers to the default Windows position.
    /// </summary>
    Manual,

    /// <summary>
    /// The startup location of a Window is the center of the screen.
    /// </summary>
    CenterScreen,

    /// <summary>
    /// The startup location of a Window is the center of the Window that owns it.
    /// </summary>
    CenterOwner
}
