using System.Diagnostics;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Interop;

/// <summary>
/// Snapshot of a render target's GPU resource usage, returned by
/// <see cref="RenderTarget.TryQueryGpuStats(out GpuResourceStats)"/>.
/// Field semantics mirror native <c>JaliumGpuStats</c>.
///
/// Frame-pacing fields decompose UI-thread BeginDraw cost three ways:
///  • FrameGpuWaitNs — UI-thread time blocked on the GPU fence (≈ 0 when
///    the swap-chain waitable does the synchronisation instead).
///  • FrameWaitableWaitNs — UI-thread time blocked on the swap-chain
///    frame-latency waitable. This is where DWM / DXGI queue back-pressure
///    actually shows up on a healthy modern D3D12 setup.
///  • LastFramePresentToReadyNs — wall clock from EndFrame's queue Signal
///    to the next BeginFrame observing the fence as completed. **Not pure
///    GPU work** — includes DWM composition and DXGI queue latency. Use
///    the hardware-timestamp GPU breakdown (<see cref="GpuTimingStats"/>)
///    for the canonical "what did the GPU actually do" number.
/// Unimplemented backends report 0 for these fields.
/// </summary>
public readonly record struct GpuResourceStats(
    int GlyphSlotsUsed,
    int GlyphSlotsTotal,
    long GlyphBytes,
    int PathEntries,
    long PathBytes,
    int TextureCount,
    long TextureBytes,
    long FrameGpuWaitNs,
    int SwapBufferCount,
    long LastFramePresentToReadyNs,
    long FrameWaitableWaitNs,
    long PresentBlockNs);

/// <summary>
/// Per-frame GPU work breakdown by draw-call category, sourced from
/// hardware timestamp queries on the graphics queue. Reports the previously
/// completed frame's numbers (read back after fence sync).
///
/// TimingValid: false when no frame has been decoded yet (first frame after
/// init, or backend lacks timestamp-query support). Categories sum to
/// roughly TotalGpuNs minus driver overhead; OtherNs captures everything
/// outside the classified categories (barriers, MSAA resolves, idle gaps).
/// </summary>
public readonly record struct GpuTimingStats(
    long TotalGpuNs,
    long SdfRectNs,
    long TextNs,
    long BitmapNs,
    long PathNs,
    long BackdropNs,
    long LiquidGlassNs,
    long OtherNs,
    int BatchCount,
    bool TimingValid);

/// <summary>
/// Represents a native render target for drawing.
/// </summary>
public sealed class RenderTarget : IDisposable
{
    [ThreadStatic]
    private static int _drawTextDepth;

    private readonly IRenderTargetNative _native;
    private readonly RenderContext? _ownerContext;
    private readonly RenderBackend _backend;
    private readonly NativeSurfaceDescriptor _surface;
    private readonly nint _hwnd;
    private nint _handle;
    private bool _disposed;
    // volatile: read/written by both the UI thread (resize/recovery defensive
    // TryEndDraw) and the render thread (TryBeginDraw/TryEndDraw). Accesses are
    // serialized by the render-thread idle drain, but the field still needs an
    // acquire/release fence across that barrier (FIX #4).
    private volatile bool _isDrawing;
    private int _ownerContextReleased;
    private float _dpiX = 96.0f;
    private float _dpiY = 96.0f;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public nint Handle => _handle;

    /// <summary>
    /// Gets whether the render target is valid.
    /// </summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>
    /// Gets whether a drawing session is active.
    /// </summary>
    public bool IsDrawing => _isDrawing;

    /// <summary>
    /// Gets the backend associated with this render target.
    /// </summary>
    public RenderBackend Backend => _backend;

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Gets whether the native backend preserves back-buffer contents across presents,
    /// allowing partial redraw + dirty-rect presentation.
    /// </summary>
    public bool SupportsPartialPresentation { get; }

    /// <summary>
    /// Gets the active rendering engine for this render target.
    /// </summary>
    public RenderingEngine RenderingEngine =>
        _handle != nint.Zero ? _native.GetEngine(_handle) : RenderingEngine.Auto;

    /// <summary>
    /// Queries the swap chain present configuration (SwapEffect / tearing / waitable /
    /// max latency / composition). Returns null when backend doesn't expose it
    /// (older versions of jalium.native.core, or non-D3D12 backends).
    /// </summary>
    public PresentInfo? GetPresentInfo()
    {
        if (_handle == nint.Zero || _disposed) return null;
        if (NativeGpuMethods.RenderTargetGetPresentInfo(_handle, out var info) == 0)
            return info;
        return null;
    }

    /// <summary>
    /// Sets the rendering engine (hot-switch). Takes effect at the next BeginDraw().
    /// </summary>
    public void SetRenderingEngine(RenderingEngine engine)
    {
        if (_handle != nint.Zero)
        {
            NativeMethods.RenderTargetSetEngine(_handle, engine);
        }
    }

    /// <summary>
    /// Asks the active backend to drop any reusable GPU / CPU caches it has
    /// accumulated (path tessellation, rasterized text bitmaps, glyph atlas
    /// pages, etc). The backend rebuilds them lazily on the next frame that
    /// needs them. Backends that have nothing to reclaim treat the call as a
    /// no-op. Safe to invoke between frames; must NOT be called while a
    /// drawing session is active.
    /// </summary>
    /// <remarks>
    /// Used by <c>JaliumAppExtensions.UseIdleResourceReclamation</c>; can also
    /// be called directly under memory pressure. No-op when the render target
    /// has been disposed or never had a native handle.
    /// </remarks>
    public void ReclaimIdleResources()
    {
        if (_disposed || _handle == nint.Zero || _isDrawing)
        {
            return;
        }
        _ = NativeMethods.RenderTargetReclaimIdleResources(_handle);
    }

    internal RenderTarget(RenderContext context, NativeSurfaceDescriptor surface, int width, int height, bool useComposition = false)
        : this(
            context.Backend,
            context.Handle,
            surface,
            width,
            height,
            useComposition,
            native: null,
            ownerContext: context)
    {
    }

    internal RenderTarget(
        RenderBackend backend,
        nint contextHandle,
        NativeSurfaceDescriptor surface,
        int width,
        int height,
        bool useComposition,
        IRenderTargetNative? native = null,
        RenderContext? ownerContext = null)
    {
        _native = native ?? DefaultRenderTargetNative.Instance;
        _ownerContext = ownerContext;
        _backend = backend;
        _surface = surface;
        _hwnd = surface.Platform == NativePlatform.Windows ? surface.Handle0 : nint.Zero;
        Width = width;
        Height = height;

        _ownerContext?.RegisterRenderTarget();
        try
        {
            _handle = useComposition
                ? _native.CreateForCompositionSurface(contextHandle, surface, width, height)
                : _native.CreateForSurface(contextHandle, surface, width, height);
            if (_handle == nint.Zero)
            {
                int resultCode = _native.GetContextLastError(contextHandle);
                ThrowRenderPipelineException("Create", resultCode);
            }

            SupportsPartialPresentation = _native.SupportsPartialPresentation(_handle);
        }
        catch
        {
            ReleaseOwnerContextReference();
            throw;
        }
    }

    /// <summary>
    /// Resizes the render target.
    /// </summary>
    /// <param name="width">The new width.</param>
    /// <param name="height">The new height.</param>
    public JaliumResult Resize(int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0) return JaliumResult.Ok;

        int resultCode = _native.Resize(_handle, width, height);
        var result = JaliumResultMapper.FromCode(resultCode);
        // Busy = the native backend refused this resize because a command list is
        // still open and references the back buffers it would free (cross-thread
        // render in flight, or a frame left open). NOT a failure: do not throw and
        // do not update Width/Height — the caller re-stashes and retries at the next
        // safe point (see Window.ApplyRenderTargetResize). Avoids the #921
        // OBJECT_DELETED_WHILE_STILL_IN_USE use-after-free.
        if (result == JaliumResult.Busy)
            return result;

        ThrowIfNativeFailure("Resize", resultCode);

        Width = width;
        Height = height;
        return JaliumResult.Ok;
    }

    /// <summary>
    /// Begins a drawing session.
    /// </summary>
    public void BeginDraw()
    {
        ThrowIfDisposed();
        if (_isDrawing) return;

        long t0 = ApiStart();
        int resultCode = _native.BeginDraw(_handle);
        ApiEnd("BeginDraw", t0);
        // Frame-pacing: BeginDraw always succeeds-or-throws, so the attempt
        // outcome is fully determined by the native result code; no separate
        // "managed-side fail" branch like TryBeginDraw.
        RenderDiagnostics.OnBeginDrawAttempt(success: resultCode == (int)JaliumResult.Ok);
        ThrowIfNativeFailure("Begin", resultCode);
        _isDrawing = true;
    }

    /// <summary>
    /// Attempts to begin a drawing session.  Returns false if the GPU is still
    /// processing the previous frame for this buffer, allowing the caller to
    /// skip the frame without blocking the UI thread.
    /// </summary>
    public bool TryBeginDraw()
    {
        ThrowIfDisposed();
        if (_isDrawing) return true;

        long t0 = ApiStart();
        int resultCode = _native.BeginDraw(_handle);
        ApiEnd("BeginDraw", t0);
        bool success = resultCode == (int)JaliumResult.Ok;
        // Frame-pacing: every TryBeginDraw call counts as one attempt,
        // success or failure. Counted here (not in Window) so any callsite
        // — not just the main render loop — feeds the pacing snapshot.
        RenderDiagnostics.OnBeginDrawAttempt(success);
        if (success)
        {
            _isDrawing = true;
            return true;
        }

        // D3D12 uses InvalidState here when the GPU is still presenting the
        // previous back buffer. Callers can skip the frame and retry later.
        if (resultCode == (int)JaliumResult.InvalidState)
        {
            return false;
        }

        ThrowIfNativeFailure("Begin", resultCode);
        return false;
    }

    /// <summary>
    /// Ends a drawing session and presents the content.
    /// </summary>
    public void EndDraw()
    {
        ThrowIfDisposed();
        if (!_isDrawing) return;

        long t0 = ApiStart();
        int resultCode;
        try
        {
            resultCode = _native.EndDraw(_handle);
        }
        finally
        {
            _isDrawing = false;
            ApiEnd("EndDraw", t0);
        }

        ThrowIfNativeFailure("End", resultCode);
    }

    /// <summary>
    /// Ends a drawing session without throwing on recoverable errors.
    /// Returns <see cref="JaliumResult.Ok"/> on success, or the failure result.
    /// </summary>
    public JaliumResult TryEndDraw()
    {
        if (_disposed || !_isDrawing) return JaliumResult.Ok;

        long t0 = ApiStart();
        int resultCode;
        try
        {
            resultCode = _native.EndDraw(_handle);
        }
        finally
        {
            _isDrawing = false;
            ApiEnd("EndDraw", t0);
        }

        return JaliumResultMapper.FromCode(resultCode);
    }

    /// <summary>
    /// Snapshots the backend's GPU resource usage (glyph atlas, path cache,
    /// texture totals). Returns true when the backend filled the struct, false
    /// when either the handle is invalid or the backend hasn't implemented the
    /// query yet — DevTools treats that case as "no snapshot published".
    /// </summary>
    public bool TryQueryGpuStats(out GpuResourceStats stats)
    {
        stats = default;
        if (_disposed || _handle == nint.Zero) return false;

        int resultCode = NativeMethods.RenderTargetQueryGpuStats(_handle, out var raw);
        if (resultCode != 0) return false;

        stats = new GpuResourceStats(
            raw.GlyphSlotsUsed, raw.GlyphSlotsTotal, raw.GlyphBytes,
            raw.PathEntries, raw.PathBytes,
            raw.TextureCount, raw.TextureBytes,
            raw.FrameGpuWaitNs, raw.SwapBufferCount,
            raw.LastFramePresentToReadyNs, raw.FrameWaitableWaitNs,
            raw.PresentBlockNs);
        return true;
    }

    /// <summary>
    /// Returns the OS HANDLE the backend uses as its swap-chain frame-latency
    /// waitable, or <see cref="nint.Zero"/> when no such object exists
    /// (non-D3D12 backends, older runtimes, or swap chain created without the
    /// FRAME_LATENCY_WAITABLE_OBJECT flag). Callers wait on the handle from a
    /// background thread to drive vsync-aligned scheduling; ownership stays
    /// with the render target — DO NOT close it.
    /// </summary>
    public nint GetFrameLatencyWaitable()
    {
        if (_disposed || _handle == nint.Zero) return nint.Zero;
        return NativeMethods.RenderTargetGetFrameLatencyWaitable(_handle);
    }

    /// <summary>
    /// Snapshots per-category GPU timing for the previous completed frame.
    /// Returns true when the backend filled the struct (TimingValid may still
    /// be false if no frame has been decoded yet); false when the handle is
    /// invalid or the backend doesn't implement timestamp queries at all.
    /// </summary>
    public bool TryQueryGpuTiming(out GpuTimingStats timing)
    {
        timing = default;
        if (_disposed || _handle == nint.Zero) return false;

        int resultCode = NativeMethods.RenderTargetQueryGpuTiming(_handle, out var raw);
        if (resultCode != 0) return false;

        timing = new GpuTimingStats(
            raw.TotalGpuNs,
            raw.SdfRectNs, raw.TextNs, raw.BitmapNs, raw.PathNs,
            raw.BackdropNs, raw.LiquidGlassNs, raw.OtherNs,
            raw.BatchCount,
            raw.TimingValid != 0);
        return true;
    }

    /// <summary>
    /// Clears the render target with the specified color.
    /// </summary>
    /// <param name="r">Red component (0-1).</param>
    /// <param name="g">Green component (0-1).</param>
    /// <param name="b">Blue component (0-1).</param>
    /// <param name="a">Alpha component (0-1).</param>
    public void Clear(float r, float g, float b, float a = 1.0f)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.RenderTargetClear(_handle, r, g, b, a);
        ApiEnd("Clear", t0);
    }

    /// <summary>
    /// Draws a filled rectangle.
    /// </summary>
    // ─── DevTools draw-API instrumentation ──────────────────────────────
    // ApiStart/ApiEnd are no-cost when RenderDiagnostics.ApiStatsEnabled is
    // false (which is the default outside DevTools). When enabled they record
    // per-frame call counts + native-side wall-clock time per native draw API
    // so the Perf tab can show which paths dominate the frame budget.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ApiStart()
        => RenderDiagnostics.ApiStatsEnabled ? Stopwatch.GetTimestamp() : 0L;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApiEnd(string name, long t0)
    {
        if (!RenderDiagnostics.ApiStatsEnabled) return;
        RenderDiagnostics.RecordApi(name, Stopwatch.GetTimestamp() - t0);
    }

    public void FillRectangle(float x, float y, float width, float height, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawFillRectangle(_handle, x, y, width, height, brush.Handle);
        ApiEnd("FillRectangle", t0);
    }

    /// <summary>
    /// Draws a rectangle outline.
    /// </summary>
    public void DrawRectangle(float x, float y, float width, float height, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawRectangle(_handle, x, y, width, height, brush.Handle, strokeWidth);
        ApiEnd("DrawRectangle", t0);
    }

    /// <summary>
    /// Draws a filled rounded rectangle.
    /// </summary>
    public void FillRoundedRectangle(float x, float y, float width, float height, float radiusX, float radiusY, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawFillRoundedRectangle(_handle, x, y, width, height, radiusX, radiusY, brush.Handle);
        ApiEnd("FillRoundedRectangle", t0);
    }

    /// <summary>
    /// Draws a rounded rectangle outline.
    /// </summary>
    public void DrawRoundedRectangle(float x, float y, float width, float height, float radiusX, float radiusY, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawRoundedRectangle(_handle, x, y, width, height, radiusX, radiusY, brush.Handle, strokeWidth);
        ApiEnd("DrawRoundedRectangle", t0);
    }

    /// <summary>
    /// Draws a filled rounded rectangle with per-corner radii.
    /// </summary>
    public void FillPerCornerRoundedRectangle(float x, float y, float width, float height, float tl, float tr, float br, float bl, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.FillPerCornerRoundedRectangle(_handle, x, y, width, height, tl, tr, br, bl, brush.Handle);
        ApiEnd("FillPerCornerRoundedRectangle", t0);
    }

    /// <summary>
    /// Draws a rounded rectangle outline with per-corner radii.
    /// </summary>
    public void DrawPerCornerRoundedRectangle(float x, float y, float width, float height, float tl, float tr, float br, float bl, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawPerCornerRoundedRectangle(_handle, x, y, width, height, tl, tr, br, bl, brush.Handle, strokeWidth);
        ApiEnd("DrawPerCornerRoundedRectangle", t0);
    }

    /// <summary>
    /// Draws a filled ellipse.
    /// </summary>
    public void FillEllipse(float centerX, float centerY, float radiusX, float radiusY, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawFillEllipse(_handle, centerX, centerY, radiusX, radiusY, brush.Handle);
        ApiEnd("FillEllipse", t0);
    }

    /// <summary>
    /// Draws a batch of filled ellipses with per-ellipse color.
    /// data layout: [cx, cy, rx, ry, packedRGBA] × count (5 floats per ellipse).
    /// Single P/Invoke call eliminates per-ellipse marshaling overhead.
    /// </summary>
    public void FillEllipseBatch(float[] data, uint count)
    {
        ThrowIfDisposed();
        if (data == null || count == 0) return;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = data)
            {
                NativeMethods.FillEllipseBatch(_handle, p, count);
            }
        }
        ApiEnd("FillEllipseBatch", t0);
    }

    /// <summary>
    /// Draws an ellipse outline.
    /// </summary>
    public void DrawEllipse(float centerX, float centerY, float radiusX, float radiusY, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawEllipse(_handle, centerX, centerY, radiusX, radiusY, brush.Handle, strokeWidth);
        ApiEnd("DrawEllipse", t0);
    }

    /// <summary>
    /// Draws a line.
    /// </summary>
    public void DrawLine(float x1, float y1, float x2, float y2, NativeBrush brush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawLine(_handle, x1, y1, x2, y2, brush.Handle, strokeWidth);
        ApiEnd("DrawLine", t0);
    }

    /// <summary>
    /// Fills a polygon defined by an array of points.
    /// </summary>
    /// <param name="points">Array of point coordinates (x0, y0, x1, y1, ...).</param>
    /// <param name="brush">Brush to fill with.</param>
    /// <param name="fillRule">Fill rule: 0 = EvenOdd, 1 = NonZero.</param>
    public void FillPolygon(float[] points, NativeBrush brush, int fillRule = 0)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || points == null || points.Length < 6) return;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = points)
            {
                NativeMethods.FillPolygon(_handle, p, points.Length / 2, brush.Handle, fillRule);
            }
        }
        ApiEnd("FillPolygon", t0);
    }

    /// <summary>
    /// Draws a polygon outline.
    /// </summary>
    /// <param name="points">Array of point coordinates (x0, y0, x1, y1, ...).</param>
    /// <param name="brush">Brush for stroke.</param>
    /// <param name="strokeWidth">Width of stroke.</param>
    /// <param name="closed">Whether to close the polygon.</param>
    public void DrawPolygon(float[] points, NativeBrush brush, float strokeWidth = 1.0f, bool closed = true, int lineJoin = 0, float miterLimit = 10.0f)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || points == null || points.Length < 4) return;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = points)
            {
                NativeMethods.DrawPolygon(_handle, p, points.Length / 2, brush.Handle, strokeWidth, closed ? 1 : 0, lineJoin, miterLimit);
            }
        }
        ApiEnd("DrawPolygon", t0);
    }

    /// <summary>
    /// Fills a path with native bezier curve support.
    /// </summary>
    /// <param name="edgeMode">-1 = inherit / backend default, 1 = Aliased, 2 = Antialiased.</param>
    public void FillPath(float startX, float startY, float[] commands, NativeBrush brush, int fillRule = 0, int edgeMode = -1)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || commands == null || commands.Length == 0) return;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = commands)
            {
                NativeMethods.FillPath(_handle, startX, startY, p, commands.Length, brush.Handle, fillRule, edgeMode);
            }
        }
        ApiEnd("FillPath", t0);
    }

    /// <summary>
    /// Fills a path using only the first <paramref name="commandLength"/> floats of
    /// <paramref name="commands"/>. Used by callers that pool / reuse the buffer
    /// across calls — the array's <c>Length</c> is the pool capacity, not the
    /// active command count.
    /// </summary>
    /// <param name="edgeMode">-1 = inherit / backend default, 1 = Aliased, 2 = Antialiased.</param>
    public void FillPath(float startX, float startY, float[] commands, int commandLength, NativeBrush brush, int fillRule = 0, int edgeMode = -1)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || commands == null || commandLength <= 0) return;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = commands)
            {
                NativeMethods.FillPath(_handle, startX, startY, p, commandLength, brush.Handle, fillRule, edgeMode);
            }
        }
        ApiEnd("FillPath", t0);
    }

    /// <summary>
    /// Strokes a path with native bezier curve support.
    /// lineCap: 0 = Butt, 1 = Square, 2 = Round.
    /// </summary>
    /// <param name="edgeMode">-1 = inherit / backend default, 1 = Aliased, 2 = Antialiased.</param>
    public void StrokePath(float startX, float startY, float[] commands, NativeBrush brush, float strokeWidth = 1.0f, bool closed = true, int lineJoin = 0, float miterLimit = 10.0f, int lineCap = 0,
        float[]? dashPattern = null, float dashOffset = 0.0f, int edgeMode = -1)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || commands == null || commands.Length == 0) return;
        int dashCount = dashPattern?.Length ?? 0;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = commands)
            fixed (float* dp = dashPattern)
            {
                NativeMethods.StrokePath(_handle, startX, startY, p, commands.Length, brush.Handle, strokeWidth, closed ? 1 : 0, lineJoin, miterLimit, lineCap,
                    dp, dashCount, dashOffset, edgeMode);
            }
        }
        ApiEnd("StrokePath", t0);
    }

    /// <summary>
    /// Strokes a path using only the first <paramref name="commandLength"/> floats of
    /// <paramref name="commands"/>. Used by callers that pool / reuse the buffer
    /// across calls.
    /// </summary>
    /// <param name="edgeMode">-1 = inherit / backend default, 1 = Aliased, 2 = Antialiased.</param>
    public void StrokePath(float startX, float startY, float[] commands, int commandLength, NativeBrush brush, float strokeWidth = 1.0f, bool closed = true, int lineJoin = 0, float miterLimit = 10.0f, int lineCap = 0,
        float[]? dashPattern = null, float dashOffset = 0.0f, int edgeMode = -1)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || commands == null || commandLength <= 0) return;
        int dashCount = dashPattern?.Length ?? 0;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = commands)
            fixed (float* dp = dashPattern)
            {
                NativeMethods.StrokePath(_handle, startX, startY, p, commandLength, brush.Handle, strokeWidth, closed ? 1 : 0, lineJoin, miterLimit, lineCap,
                    dp, dashCount, dashOffset, edgeMode);
            }
        }
        ApiEnd("StrokePath", t0);
    }

    /// <summary>
    /// Fills a path with an additional translation (offsetX, offsetY) applied on top
    /// of the current transform stack for this single call. Single-P/Invoke replacement
    /// for the push_transform + fill_path + pop_transform sequence — saves two GC
    /// frame transitions per draw, which adds up to a meaningful chunk of managed
    /// overhead when many visuals each emit a few paths per frame.
    /// </summary>
    public void FillPathAtOffset(float offsetX, float offsetY, float startX, float startY,
        float[] commands, int commandLength, NativeBrush brush, int fillRule = 0, int edgeMode = -1)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || commands == null || commandLength <= 0) return;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = commands)
            {
                NativeMethods.FillPathAt(_handle, offsetX, offsetY, startX, startY, p, commandLength, brush.Handle, fillRule, edgeMode);
            }
        }
        ApiEnd("FillPathAtOffset", t0);
    }

    /// <summary>
    /// Strokes a path with an additional translation applied on top of the current
    /// transform stack. Single-P/Invoke counterpart to FillPathAtOffset.
    /// </summary>
    public void StrokePathAtOffset(float offsetX, float offsetY, float startX, float startY,
        float[] commands, int commandLength, NativeBrush brush,
        float strokeWidth = 1.0f, bool closed = true, int lineJoin = 0, float miterLimit = 10.0f, int lineCap = 0,
        float[]? dashPattern = null, float dashOffset = 0.0f, int edgeMode = -1)
    {
        ThrowIfDisposed();
        if (brush == null || !brush.IsValid || commands == null || commandLength <= 0) return;
        int dashCount = dashPattern?.Length ?? 0;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = commands)
            fixed (float* dp = dashPattern)
            {
                NativeMethods.StrokePathAt(_handle, offsetX, offsetY, startX, startY, p, commandLength, brush.Handle,
                    strokeWidth, closed ? 1 : 0, lineJoin, miterLimit, lineCap,
                    dp, dashCount, dashOffset, edgeMode);
            }
        }
        ApiEnd("StrokePathAtOffset", t0);
    }


    /// <summary>
    /// Draws a content area border: fills rect with bottom-only rounded corners,
    /// strokes U-shape (left + bottom + right, no top) with native D2D arcs.
    /// </summary>
    public void DrawContentBorder(float x, float y, float width, float height,
        float blRadius, float brRadius,
        NativeBrush? fillBrush, NativeBrush? strokeBrush, float strokeWidth = 1.0f)
    {
        ThrowIfDisposed();
        var fillHandle = (fillBrush != null && fillBrush.IsValid) ? fillBrush.Handle : 0;
        var strokeHandle = (strokeBrush != null && strokeBrush.IsValid) ? strokeBrush.Handle : 0;
        if (fillHandle == 0 && strokeHandle == 0) return;
        long t0 = ApiStart();
        NativeMethods.DrawContentBorder(_handle, x, y, width, height, blRadius, brRadius, fillHandle, strokeHandle, strokeWidth);
        ApiEnd("DrawContentBorder", t0);
    }

    /// <summary>
    /// Draws text.
    /// </summary>
    public void DrawText(string text, NativeTextFormat format, float x, float y, float width, float height, NativeBrush brush)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(text) || format == null || !format.IsValid || brush == null || !brush.IsValid) return;

        // Hard guard against unexpected re-entrant draw recursion from native/interop paths.
        // Normal rendering should never exceed a very small depth on a single thread.
        if (_drawTextDepth > 8)
        {
            return;
        }

        _drawTextDepth++;
        long t0 = ApiStart();
        try
        {
            unsafe
            {
                fixed (char* textPtr = text)
                {
                    NativeMethods.DrawTextRaw(_handle, textPtr, text.Length, format.Handle, x, y, width, height, brush.Handle);
                }
            }
        }
        finally
        {
            _drawTextDepth--;
            ApiEnd("DrawText", t0);
        }
    }

    /// <summary>
    /// Draws text while temporarily applying <paramref name="inverseMatrix"/> as
    /// a transient transform that is popped automatically. Bundles the
    /// PushTransform + DrawText + PopTransform sequence into a single P/Invoke;
    /// used by managed DrawText to cancel the active native matrix when
    /// glyphs are pre-rasterized at screen resolution.
    /// </summary>
    public void DrawTextWithInverseTransform(string text, NativeTextFormat format,
        float x, float y, float width, float height, NativeBrush brush,
        ReadOnlySpan<float> inverseMatrix)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(text) || format == null || !format.IsValid ||
            brush == null || !brush.IsValid || inverseMatrix.Length < 6)
        {
            return;
        }

        if (_drawTextDepth > 8) return;

        _drawTextDepth++;
        long t0 = ApiStart();
        try
        {
            unsafe
            {
                fixed (char* textPtr = text)
                fixed (float* matrixPtr = inverseMatrix)
                {
                    NativeMethods.DrawTextWithInverseRaw(_handle, textPtr, text.Length,
                        format.Handle, x, y, width, height, brush.Handle, matrixPtr);
                }
            }
        }
        finally
        {
            _drawTextDepth--;
            ApiEnd("DrawText", t0);
        }
    }

    /// <summary>
    /// Pushes a transform matrix.
    /// </summary>
    public void PushTransform(float[] matrix)
    {
        ThrowIfDisposed();
        if (matrix == null || matrix.Length < 6) return;
        long t0 = ApiStart();
        unsafe
        {
            fixed (float* p = matrix)
            {
                NativeMethods.PushTransform(_handle, p);
            }
        }
        ApiEnd("PushTransform", t0);
    }

    /// <summary>
    /// Pushes a pure translation matrix (1, 0, 0, 1, dx, dy) onto the transform
    /// stack — zero-allocation stackalloc fast path for the common case of
    /// applying a per-Visual offset around a single Draw call. Equivalent to
    /// translating every command coordinate by (dx, dy) but lets the native
    /// path cache treat (dx, dy)-only-different draws as the same path.
    /// </summary>
    public unsafe void PushTransformTranslation(float dx, float dy)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        float* mat = stackalloc float[6];
        mat[0] = 1f; mat[1] = 0f;
        mat[2] = 0f; mat[3] = 1f;
        mat[4] = dx; mat[5] = dy;
        NativeMethods.PushTransform(_handle, mat);
        ApiEnd("PushTransformTranslation", t0);
    }

    /// <summary>
    /// Pops the current transform.
    /// </summary>
    public void PopTransform()
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.PopTransform(_handle);
        ApiEnd("PopTransform", t0);
    }

    /// <summary>
    /// Pushes a clip rectangle (PER_PRIMITIVE anti-aliasing).
    /// </summary>
    public void PushClip(float x, float y, float width, float height)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.PushClip(_handle, x, y, width, height);
        ApiEnd("PushClip", t0);
    }

    /// <summary>
    /// Pushes a clip rectangle with ALIASED anti-aliasing (hard pixel boundary).
    /// Used for dirty region clips where semi-transparent edges cause artifacts.
    /// </summary>
    public void PushClipAliased(float x, float y, float width, float height)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.PushClipAliased(_handle, x, y, width, height);
        ApiEnd("PushClipAliased", t0);
    }

    /// <summary>
    /// Pushes a rounded rectangle clip using a geometry mask layer.
    /// </summary>
    public void PushRoundedRectClip(float x, float y, float width, float height, float rx, float ry)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.PushRoundedRectClip(_handle, x, y, width, height, rx, ry);
        ApiEnd("PushRoundedRectClip", t0);
    }

    /// <summary>
    /// Pushes a per-corner rounded-rect clip with independent radii for each corner.
    /// </summary>
    public void PushPerCornerRoundedRectClip(float x, float y, float width, float height,
        float tl, float tr, float br, float bl)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.PushPerCornerRoundedRectClip(_handle, x, y, width, height, tl, tr, br, bl);
        ApiEnd("PushPerCornerRoundedRectClip", t0);
    }

    /// <summary>
    /// Pops the current clip.
    /// </summary>
    public void PopClip()
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.PopClip(_handle);
        ApiEnd("PopClip", t0);
    }

    /// <summary>
    /// Punches a transparent rectangular hole in the current render target.
    /// </summary>
    public void PunchTransparentRect(float x, float y, float width, float height)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.PunchTransparentRect(_handle, x, y, width, height);
        ApiEnd("PunchTransparentRect", t0);
    }

    /// <summary>
    /// Pushes an opacity value.
    /// </summary>
    public void PushOpacity(float opacity)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.PushOpacity(_handle, opacity);
        ApiEnd("PushOpacity", t0);
    }

    /// <summary>
    /// Pops the current opacity.
    /// </summary>
    public void PopOpacity()
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.PopOpacity(_handle);
        ApiEnd("PopOpacity", t0);
    }

    /// <summary>
    /// Sets the current shape type for SDF rect rendering.
    /// </summary>
    /// <param name="type">0 = RoundedRect, 1 = SuperEllipse.</param>
    /// <param name="n">SuperEllipse exponent (e.g. 4.0 for squircle).</param>
    public void SetShapeType(int type, float n)
    {
        ThrowIfDisposed();
        NativeMethods.SetShapeType(_handle, type, n);
    }

    /// <summary>
    /// Sets whether VSync is enabled.
    /// When disabled, Present returns immediately for faster frame updates during resize.
    /// </summary>
    /// <param name="enabled">True to enable VSync, false to disable.</param>
    /// <summary>
    /// Hands present pacing to the caller. While enabled, BeginDraw no longer
    /// waits on the swap-chain frame-latency waitable — the caller must own
    /// that HANDLE (see <see cref="GetFrameLatencyWaitable"/>), consume its
    /// signals (e.g. via a thread-pool registered wait) and only begin a frame
    /// once a signal arrived — and Present uses sync interval 0 so it never
    /// blocks on the compositor. Restores internal pacing when disabled.
    /// No-op on backends without a frame-latency waitable.
    /// </summary>
    public void SetExternalPresentPacing(bool enabled)
    {
        ThrowIfDisposed();
        _native.SetExternalPresentPacing(_handle, enabled);
    }

    public void SetVSyncEnabled(bool enabled)
    {
        ThrowIfDisposed();
        _native.SetVSyncEnabled(_handle, enabled);
    }

    /// <summary>
    /// Sets the path stencil-then-cover MSAA sample count (clamped to 1/2/4/8).
    /// Applied at the next frame boundary. Lower counts trade path edge
    /// anti-aliasing quality for GPU time; 8 is the highest-quality default.
    /// Backends without an MSAA path renderer ignore this.
    /// </summary>
    /// <param name="sampleCount">Desired MSAA sample count (1, 2, 4, or 8).</param>
    public void SetPathMsaaSampleCount(uint sampleCount)
    {
        ThrowIfDisposed();
        _native.SetPathMsaaSampleCount(_handle, sampleCount);
    }

    /// <summary>
    /// Sets the DPI for the render target.
    /// D2D will use this to map DIP coordinates to physical pixels.
    /// </summary>
    /// <param name="dpiX">Horizontal DPI (96 = 100% scaling).</param>
    /// <param name="dpiY">Vertical DPI (96 = 100% scaling).</param>
    public void SetDpi(float dpiX, float dpiY)
    {
        ThrowIfDisposed();
        _dpiX = dpiX;
        _dpiY = dpiY;
        NativeMethods.RenderTargetSetDpi(_handle, dpiX, dpiY);
    }

    /// <summary>
    /// Adds a dirty rectangle for partial rendering optimization.
    /// The native layer uses this to clip D2D drawing and for Present1 dirty rects.
    /// </summary>
    public void AddDirtyRect(float x, float y, float width, float height)
    {
        ThrowIfDisposed();
        NativeMethods.RenderTargetAddDirtyRect(_handle, x, y, width, height);
    }

    /// <summary>
    /// Marks the entire render target as needing full redraw.
    /// </summary>
    public void SetFullInvalidation()
    {
        ThrowIfDisposed();
        _native.SetFullInvalidation(_handle);
    }

    /// <summary>
    /// Destroys a retained GPU layer previously realized for this target.
    /// Routed through <see cref="IRenderTargetNative"/> (rather than a direct
    /// P/Invoke) so the drain path stays on the injectable seam — a test double
    /// can no-op it instead of dereferencing a non-owned native handle. No-op
    /// when the target has been disposed or never had a native handle.
    /// </summary>
    internal void DestroyRetainedLayer(nint layer)
    {
        if (_disposed || _handle == nint.Zero || layer == nint.Zero)
        {
            return;
        }
        _native.DestroyRetainedLayer(_handle, layer);
    }

    /// <summary>
    /// Attempts to create a composition visual node suitable for WebView composition hosting.
    /// </summary>
    /// <param name="visualTarget">Returns the native visual pointer (IUnknown* on Windows).</param>
    /// <returns>True when a visual was created; false when unsupported or unavailable.</returns>
    public bool TryCreateWebViewCompositionVisual(out nint visualTarget)
    {
        ThrowIfDisposed();
        visualTarget = nint.Zero;

        var resultCode = NativeMethods.RenderTargetCreateWebViewVisual(_handle, out var target);
        if (resultCode == (int)JaliumResult.Ok && target != nint.Zero)
        {
            visualTarget = target;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Destroys a composition visual previously created by <see cref="TryCreateWebViewCompositionVisual"/>.
    /// </summary>
    /// <param name="visualTarget">The native visual pointer.</param>
    public void DestroyWebViewCompositionVisual(nint visualTarget)
    {
        ThrowIfDisposed();
        if (visualTarget == nint.Zero)
        {
            return;
        }

        var resultCode = NativeMethods.RenderTargetDestroyWebViewVisual(_handle, visualTarget);
        ThrowIfNativeFailure("DestroyWebViewVisual", resultCode);
    }

    /// <summary>
    /// Updates the placement and visible clip of a composition visual created for WebView hosting.
    /// </summary>
    public void SetWebViewCompositionVisualPlacement(nint visualTarget, PixelRect bounds, PixelPoint contentOffset)
    {
        ThrowIfDisposed();
        if (visualTarget == nint.Zero)
        {
            return;
        }

        var resultCode = NativeMethods.RenderTargetSetWebViewVisualPlacement(
            _handle,
            visualTarget,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            contentOffset.X,
            contentOffset.Y);
        ThrowIfNativeFailure("SetWebViewVisualPlacement", resultCode);
    }

    /// <summary>
    /// Off-thread animation probe (Increment 1 — architecture hard gate). Creates a
    /// self-driving DirectComposition child visual (one-time solid-color content plus
    /// an autonomous offset animation that DWM drives at vblank with no app-side
    /// present). Coordinates and sizes are in physical pixels. Returns <c>true</c> and
    /// the native visual handle on success; <c>false</c> when unsupported (non-composition
    /// window, non-D3D12 backend) — the caller then keeps the per-frame present path.
    /// </summary>
    public bool TryCreateAnimProbe(int x, int y, int width, int height,
        float travelPx, float periodSec, uint colorArgb, bool vertical, out nint visualTarget)
    {
        ThrowIfDisposed();
        visualTarget = nint.Zero;

        var resultCode = NativeMethods.RenderTargetCreateAnimProbe(
            _handle, x, y, width, height, travelPx, periodSec, colorArgb, vertical ? 1 : 0, out var target);
        if (resultCode == (int)JaliumResult.Ok && target != nint.Zero)
        {
            visualTarget = target;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Destroys a probe visual previously created by <see cref="TryCreateAnimProbe"/>.
    /// </summary>
    public void DestroyAnimProbe(nint visualTarget)
    {
        ThrowIfDisposed();
        if (visualTarget == nint.Zero)
        {
            return;
        }

        NativeMethods.RenderTargetDestroyAnimProbe(_handle, visualTarget);
    }

    /// <summary>
    /// Draws a bitmap.
    /// </summary>
    /// <param name="bitmap">The bitmap to draw.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="opacity">The opacity (0-1).</param>
    public void DrawBitmap(NativeBitmap bitmap, float x, float y, float width, float height, float opacity = 1.0f)
    {
        ThrowIfDisposed();
        if (bitmap == null || !bitmap.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawBitmap(_handle, bitmap.Handle, x, y, width, height, opacity);
        ApiEnd("DrawBitmap", t0);
    }

    /// <summary>
    /// Draws a bitmap with the specified scaling mode.
    /// </summary>
    /// <param name="bitmap">The bitmap to draw.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="opacity">The opacity (0-1).</param>
    /// <param name="scalingMode">The bitmap scaling algorithm to use.</param>
    public void DrawBitmap(NativeBitmap bitmap, float x, float y, float width, float height, float opacity, Jalium.UI.Media.BitmapScalingMode scalingMode)
    {
        ThrowIfDisposed();
        if (bitmap == null || !bitmap.IsValid) return;
        long t0 = ApiStart();
        NativeMethods.DrawBitmapEx(_handle, bitmap.Handle, x, y, width, height, opacity, (int)scalingMode);
        ApiEnd("DrawBitmap", t0);
    }

    /// <summary>
    /// Draws a <see cref="Jalium.UI.Media.NativeVideoSurface"/> at the given rectangle.
    /// Used by the video render path in <see cref="RenderTargetDrawingContext.DrawImage(Jalium.UI.Media.ImageSource, Jalium.UI.Rect, Jalium.UI.Media.BitmapScalingMode)"/>
    /// when the source is a <see cref="Jalium.UI.Media.D3DImage"/> backed by a
    /// <c>NativeVideoSurface</c>.
    /// </summary>
    public void DrawVideoSurface(nint videoSurfaceHandle, float x, float y, float width, float height, float opacity, Jalium.UI.Media.BitmapScalingMode scalingMode)
    {
        ThrowIfDisposed();
        if (videoSurfaceHandle == nint.Zero) return;
        long t0 = ApiStart();
        NativeMethods.DrawVideoSurface(_handle, videoSurfaceHandle, x, y, width, height, opacity, (int)scalingMode);
        ApiEnd("DrawVideoSurface", t0);
    }

    /// <summary>
    /// Draws a backdrop filter effect.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="width">The width of the filter area.</param>
    /// <param name="height">The height of the filter area.</param>
    /// <param name="backdropFilter">The CSS-style backdrop filter string.</param>
    /// <param name="material">The material type string.</param>
    /// <param name="materialTint">The material tint color string.</param>
    /// <param name="materialTintOpacity">The material tint opacity.</param>
    /// <param name="materialBlurRadius">The material blur radius.</param>
    /// <param name="cornerRadiusTL">Top-left corner radius.</param>
    /// <param name="cornerRadiusTR">Top-right corner radius.</param>
    /// <param name="cornerRadiusBR">Bottom-right corner radius.</param>
    /// <param name="cornerRadiusBL">Bottom-left corner radius.</param>
    public void DrawBackdropFilter(
        float x, float y, float width, float height,
        string? backdropFilter,
        string? material,
        string? materialTint,
        float materialTintOpacity,
        float materialBlurRadius,
        float cornerRadiusTL,
        float cornerRadiusTR,
        float cornerRadiusBR,
        float cornerRadiusBL)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawBackdropFilter(
            _handle,
            x, y, width, height,
            backdropFilter ?? string.Empty,
            material ?? string.Empty,
            materialTint ?? string.Empty,
            materialTintOpacity,
            materialBlurRadius,
            cornerRadiusTL,
            cornerRadiusTR,
            cornerRadiusBR,
            cornerRadiusBL);
        ApiEnd("DrawBackdropFilter", t0);
    }

    /// <summary>
    /// Draws a glowing border highlight effect for DevTools element inspection.
    /// </summary>
    /// <param name="x">The x coordinate of the element.</param>
    /// <param name="y">The y coordinate of the element.</param>
    /// <param name="width">The width of the element.</param>
    /// <param name="height">The height of the element.</param>
    /// <param name="animationPhase">Animation phase (0.0 - 1.0).</param>
    /// <param name="glowColorR">Glow color red component (0-1).</param>
    /// <param name="glowColorG">Glow color green component (0-1).</param>
    /// <param name="glowColorB">Glow color blue component (0-1).</param>
    /// <param name="strokeWidth">Width of the glowing stroke.</param>
    /// <param name="trailLength">Length of the trailing glow (0.0 - 1.0 of perimeter).</param>
    /// <param name="dimOpacity">Opacity of the dimmed area outside (0-1).</param>
    /// <param name="screenWidth">Total screen/window width for dimming.</param>
    /// <param name="screenHeight">Total screen/window height for dimming.</param>
    public void DrawGlowingBorderHighlight(
        float x, float y, float width, float height,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawGlowingBorderHighlight(
            _handle,
            x, y, width, height,
            animationPhase,
            glowColorR, glowColorG, glowColorB,
            strokeWidth,
            trailLength,
            dimOpacity,
            screenWidth, screenHeight);
        ApiEnd("DrawGlowingBorderHighlight", t0);
    }

    /// <summary>
    /// Draws a glowing border transition effect between two elements.
    /// </summary>
    public void DrawGlowingBorderTransition(
        float fromX, float fromY, float fromWidth, float fromHeight,
        float toX, float toY, float toWidth, float toHeight,
        float headProgress, float tailProgress,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawGlowingBorderTransition(
            _handle,
            fromX, fromY, fromWidth, fromHeight,
            toX, toY, toWidth, toHeight,
            headProgress, tailProgress,
            animationPhase,
            glowColorR, glowColorG, glowColorB,
            strokeWidth,
            trailLength,
            dimOpacity,
            screenWidth, screenHeight);
        ApiEnd("DrawGlowingBorderTransition", t0);
    }

    /// <summary>
    /// Draws a ripple effect expanding from element border.
    /// Used after transition animation completes, before rotation starts.
    /// </summary>
    /// <param name="x">The x coordinate of the element.</param>
    /// <param name="y">The y coordinate of the element.</param>
    /// <param name="width">The width of the element.</param>
    /// <param name="height">The height of the element.</param>
    /// <param name="rippleProgress">Ripple expansion progress (0.0 - 1.0).</param>
    /// <param name="glowColorR">Glow color red component (0-1).</param>
    /// <param name="glowColorG">Glow color green component (0-1).</param>
    /// <param name="glowColorB">Glow color blue component (0-1).</param>
    /// <param name="strokeWidth">Base stroke width.</param>
    /// <param name="dimOpacity">Opacity of the dimmed area outside (0-1).</param>
    /// <param name="screenWidth">Total screen/window width for dimming.</param>
    /// <param name="screenHeight">Total screen/window height for dimming.</param>
    public void DrawRippleEffect(
        float x, float y, float width, float height,
        float rippleProgress,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float dimOpacity,
        float screenWidth, float screenHeight)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawRippleEffect(
            _handle,
            x, y, width, height,
            rippleProgress,
            glowColorR, glowColorG, glowColorB,
            strokeWidth,
            dimOpacity,
            screenWidth, screenHeight);
        ApiEnd("DrawRippleEffect", t0);
    }

    /// <summary>
    /// Captures the desktop area at the specified screen coordinates.
    /// The captured content is cached internally for use by DrawDesktopBackdrop.
    /// </summary>
    /// <param name="screenX">Screen X coordinate.</param>
    /// <param name="screenY">Screen Y coordinate.</param>
    /// <param name="width">Width to capture.</param>
    /// <param name="height">Height to capture.</param>
    public void CaptureDesktopArea(int screenX, int screenY, int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0) return;
        long t0 = ApiStart();
        NativeMethods.CaptureDesktopArea(_handle, screenX, screenY, width, height);
        ApiEnd("CaptureDesktopArea", t0);
    }

    /// <summary>
    /// Draws the cached desktop capture with Gaussian blur and tint overlay.
    /// Must call CaptureDesktopArea first.
    /// </summary>
    public void DrawDesktopBackdrop(
        float x, float y, float width, float height,
        float blurRadius,
        float tintR, float tintG, float tintB, float tintOpacity,
        float noiseIntensity = 0f, float saturation = 1f)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawDesktopBackdrop(
            _handle, x, y, width, height,
            blurRadius, tintR, tintG, tintB, tintOpacity,
            noiseIntensity, saturation);
        ApiEnd("DrawDesktopBackdrop", t0);
    }

    /// <summary>
    /// Begins capturing content into an offscreen bitmap for transition shader effects.
    /// </summary>
    /// <param name="slot">0 = old content, 1 = new content.</param>
    /// <param name="x">X position (in DIPs).</param>
    /// <param name="y">Y position (in DIPs).</param>
    /// <param name="w">Width (in DIPs).</param>
    /// <param name="h">Height (in DIPs).</param>
    public void BeginTransitionCapture(int slot, float x, float y, float w, float h)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.TransitionBeginCapture(_handle, slot, x, y, w, h);
        ApiEnd("BeginTransitionCapture", t0);
    }

    /// <summary>
    /// Ends capturing content for a transition slot and restores the main render target.
    /// </summary>
    /// <param name="slot">0 = old content, 1 = new content.</param>
    public void EndTransitionCapture(int slot)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.TransitionEndCapture(_handle, slot);
        ApiEnd("EndTransitionCapture", t0);
    }

    /// <summary>
    /// Draws the transition shader effect blending old and new content bitmaps.
    /// </summary>
    /// <param name="x">X position (in DIPs).</param>
    /// <param name="y">Y position (in DIPs).</param>
    /// <param name="w">Width (in DIPs).</param>
    /// <param name="h">Height (in DIPs).</param>
    /// <param name="progress">Transition progress (0.0 - 1.0).</param>
    /// <param name="mode">Shader mode index (0-9).</param>
    public void DrawTransitionShader(float x, float y, float w, float h, float progress, int mode, float cornerRadius = 0f)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawTransitionShader(_handle, x, y, w, h, progress, mode, cornerRadius);
        ApiEnd("DrawTransitionShader", t0);
    }

    /// <summary>
    /// Draws a previously captured transition bitmap to the current render target.
    /// </summary>
    public void DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawCapturedTransition(_handle, slot, x, y, w, h, opacity);
        ApiEnd("DrawCapturedTransition", t0);
    }

    // ========================================================================
    // Element Effect Capture & Rendering
    // ========================================================================

    /// <summary>
    /// Begins capturing element content into an offscreen bitmap for effect processing.
    /// </summary>
    public void BeginEffectCapture(float x, float y, float w, float h)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.EffectBeginCapture(_handle, x, y, w, h);
        ApiEnd("BeginEffectCapture", t0);
    }

    /// <summary>
    /// Ends capturing element content and restores the main render target.
    /// </summary>
    public void EndEffectCapture()
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.EffectEndCapture(_handle);
        ApiEnd("EndEffectCapture", t0);
    }

    /// <summary>
    /// Applies a Gaussian blur effect to the captured element content and draws it.
    /// </summary>
    public void DrawBlurEffect(float x, float y, float w, float h, float radius,
        float uvOffsetX = 0, float uvOffsetY = 0)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawBlurEffect(_handle, x, y, w, h, radius, uvOffsetX, uvOffsetY);
        ApiEnd("DrawBlurEffect", t0);
    }

    /// <summary>
    /// Applies a drop shadow effect to the captured element content and draws it.
    /// </summary>
    public void DrawDropShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawDropShadowEffect(_handle, x, y, w, h,
            blurRadius, offsetX, offsetY, r, g, b, a,
            uvOffsetX, uvOffsetY,
            cornerTL, cornerTR, cornerBR, cornerBL);
        ApiEnd("DrawDropShadowEffect", t0);
    }

    public void DrawOuterGlowEffect(float x, float y, float w, float h,
        float glowSize, float r, float g, float b, float a, float intensity,
        float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawOuterGlowEffect(_handle, x, y, w, h,
            glowSize, r, g, b, a, intensity, uvOffsetX, uvOffsetY,
            cornerTL, cornerTR, cornerBR, cornerBL);
        ApiEnd("DrawOuterGlowEffect", t0);
    }

    public void DrawInnerShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawInnerShadowEffect(_handle, x, y, w, h,
            blurRadius, offsetX, offsetY, r, g, b, a, uvOffsetX, uvOffsetY,
            cornerTL, cornerTR, cornerBR, cornerBL);
        ApiEnd("DrawInnerShadowEffect", t0);
    }

    public void DrawColorMatrixEffect(float x, float y, float w, float h,
        ReadOnlySpan<float> matrix)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawColorMatrixEffect(_handle, x, y, w, h, matrix);
        ApiEnd("DrawColorMatrixEffect", t0);
    }

    public void DrawEmbossEffect(float x, float y, float w, float h,
        float amount, float lightDirX, float lightDirY, float relief)
    {
        ThrowIfDisposed();
        long t0 = ApiStart();
        NativeMethods.DrawEmbossEffect(_handle, x, y, w, h,
            amount, lightDirX, lightDirY, relief);
        ApiEnd("DrawEmbossEffect", t0);
    }

    /// <summary>
    /// Applies a custom pixel shader effect to the captured element content and draws it.
    /// </summary>
    public void DrawShaderEffect(float x, float y, float w, float h,
        byte[] shaderBytecode, float[] constants)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(shaderBytecode);
        ArgumentNullException.ThrowIfNull(constants);

        long t0 = ApiStart();
        NativeMethods.DrawShaderEffect(_handle, x, y, w, h,
            shaderBytecode, (uint)shaderBytecode.Length,
            constants, (uint)constants.Length);
        ApiEnd("DrawShaderEffect", t0);
    }

    /// <summary>
    /// Draws a custom pixel-shader effect from SM6 HLSL <b>source</b>, compiled at
    /// runtime by the backend (D3D12: D3DCompile, Vulkan: DXC→SPIR-V). This is the
    /// cross-backend custom-shader path — unlike the DXBC <see cref="DrawShaderEffect"/>
    /// overload, it works on the Vulkan backend too.
    /// </summary>
    public void DrawShaderEffectFromSource(float x, float y, float w, float h,
        string hlslSource, float[] constants)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(hlslSource);
        ArgumentNullException.ThrowIfNull(constants);

        long t0 = ApiStart();
        NativeMethods.DrawShaderEffectFromSource(_handle, x, y, w, h,
            hlslSource, constants, (uint)constants.Length);
        ApiEnd("DrawShaderEffectFromSource", t0);
    }

    /// <summary>
    /// Draws a liquid glass effect with SDF-based refraction, highlight, and inner shadow.
    /// </summary>
    public unsafe void DrawLiquidGlass(
        float x, float y, float width, float height,
        float cornerRadius,
        float blurRadius = 8f,
        float refractionAmount = 60f,
        float chromaticAberration = 0f,
        float tintR = 0.08f, float tintG = 0.08f, float tintB = 0.08f,
        float tintOpacity = 0.3f,
        float lightX = -1f, float lightY = -1f,
        float highlightBoost = 0f,
        int shapeType = 0,
        float shapeExponent = 4f,
        int neighborCount = 0,
        float fusionRadius = 30f,
        ReadOnlySpan<float> neighborData = default)
    {
        ThrowIfDisposed();
        if (neighborCount > 0 && neighborData.Length < neighborCount * 5)
            throw new ArgumentException("neighborData too small for neighborCount");
        long t0 = ApiStart();
        fixed (float* pNeighbor = neighborData)
        {
            NativeMethods.DrawLiquidGlass(
                _handle, x, y, width, height,
                cornerRadius, blurRadius,
                refractionAmount, chromaticAberration,
                tintR, tintG, tintB, tintOpacity,
                lightX, lightY, highlightBoost,
                shapeType, shapeExponent,
                neighborCount, fusionRadius, (nint)pNeighbor);
        }
        ApiEnd("DrawLiquidGlass", t0);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfNativeFailure(string stage, int resultCode)
    {
        if (resultCode == (int)JaliumResult.Ok)
        {
            return;
        }

        ThrowRenderPipelineException(stage, resultCode);
    }

    private void ThrowRenderPipelineException(string stage, int resultCode)
    {
        // RenderTarget creation can fail with a null handle while context last-error is still OK.
        // Treat this as a transient resource-creation failure so upper layers can recover/retry.
        bool nullHandleWithOkError = stage == "Create" && resultCode == (int)JaliumResult.Ok;
        int normalizedCode = nullHandleWithOkError
            ? (int)JaliumResult.ResourceCreationFailed
            : resultCode;

        JaliumResult mapped = JaliumResultMapper.FromCode(normalizedCode);
        throw new RenderPipelineException(
            stage: stage,
            result: mapped,
            resultCode: normalizedCode,
            hwnd: _hwnd,
            width: Width,
            height: Height,
            dpiX: _dpiX,
            dpiY: _dpiY,
            backend: _backend.ToString(),
            details: nullHandleWithOkError
                ? "RenderTarget creation returned null handle while context last-error was OK."
                : null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isDrawing)
        {
            try { _ = _native.EndDraw(_handle); } catch { }
            _isDrawing = false;
        }

        if (_handle != nint.Zero)
        {
            _native.Destroy(_handle);
            _handle = nint.Zero;
        }

        ReleaseOwnerContextReference();
        GC.SuppressFinalize(this);
    }

    ~RenderTarget()
    {
        if (_handle != nint.Zero)
        {
            NativeMethods.RenderTargetDestroy(_handle);
        }
        _isDrawing = false;
        _disposed = true;
        _handle = nint.Zero;
    }

    private void ReleaseOwnerContextReference()
    {
        if (Interlocked.Exchange(ref _ownerContextReleased, 1) != 0)
        {
            return;
        }

        _ownerContext?.UnregisterRenderTarget();
    }
}

internal interface IRenderTargetNative
{
    nint CreateForSurface(nint context, NativeSurfaceDescriptor surface, int width, int height);
    nint CreateForCompositionSurface(nint context, NativeSurfaceDescriptor surface, int width, int height);
    int GetContextLastError(nint context);
    int Resize(nint renderTarget, int width, int height);
    int BeginDraw(nint renderTarget);
    int EndDraw(nint renderTarget);
    RenderingEngine GetEngine(nint renderTarget);
    void SetVSyncEnabled(nint renderTarget, bool enabled);
    void SetExternalPresentPacing(nint renderTarget, bool enabled);
    void SetPathMsaaSampleCount(nint renderTarget, uint sampleCount);
    void SetFullInvalidation(nint renderTarget);
    bool SupportsPartialPresentation(nint renderTarget);
    void DestroyRetainedLayer(nint renderTarget, nint layer);
    void Destroy(nint renderTarget);
}

internal sealed class DefaultRenderTargetNative : IRenderTargetNative
{
    internal static readonly DefaultRenderTargetNative Instance = new();

    private DefaultRenderTargetNative()
    {
    }

    public nint CreateForSurface(nint context, NativeSurfaceDescriptor surface, int width, int height)
        => NativeMethods.RenderTargetCreateForSurface(context, in surface, width, height);

    public nint CreateForCompositionSurface(nint context, NativeSurfaceDescriptor surface, int width, int height)
        => NativeMethods.RenderTargetCreateForCompositionSurface(context, in surface, width, height);

    public int GetContextLastError(nint context)
        => NativeMethods.ContextGetLastError(context);

    public int Resize(nint renderTarget, int width, int height)
        => NativeMethods.RenderTargetResize(renderTarget, width, height);

    public int BeginDraw(nint renderTarget)
        => NativeMethods.RenderTargetBeginDraw(renderTarget);

    public int EndDraw(nint renderTarget)
        => NativeMethods.RenderTargetEndDraw(renderTarget);

    public RenderingEngine GetEngine(nint renderTarget)
        => NativeMethods.RenderTargetGetEngine(renderTarget);

    public void SetVSyncEnabled(nint renderTarget, bool enabled)
        => NativeMethods.RenderTargetSetVSync(renderTarget, enabled ? 1 : 0);

    public void SetExternalPresentPacing(nint renderTarget, bool enabled)
        => NativeMethods.RenderTargetSetExternalPresentPacing(renderTarget, enabled ? 1 : 0);

    public void SetPathMsaaSampleCount(nint renderTarget, uint sampleCount)
        => NativeMethods.RenderTargetSetPathMsaa(renderTarget, sampleCount);

    public void SetFullInvalidation(nint renderTarget)
        => NativeMethods.RenderTargetSetFullInvalidation(renderTarget);

    public bool SupportsPartialPresentation(nint renderTarget)
        => NativeMethods.RenderTargetSupportsPartialPresentation(renderTarget) != 0;

    public void DestroyRetainedLayer(nint renderTarget, nint layer)
        => NativeMethods.RenderTargetDestroyRetainedLayer(renderTarget, layer);

    public void Destroy(nint renderTarget)
        => NativeMethods.RenderTargetDestroy(renderTarget);
}
