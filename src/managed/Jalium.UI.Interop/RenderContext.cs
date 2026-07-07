namespace Jalium.UI.Interop;

/// <summary>
/// Event data for device-lost recovery events.
/// </summary>
public sealed class DeviceLostEventArgs : EventArgs
{
    /// <summary>
    /// The previous context that was lost (may already be disposed).
    /// </summary>
    public RenderContext? PreviousContext { get; }

    /// <summary>
    /// The newly created replacement context.
    /// </summary>
    public RenderContext NewContext { get; }

    public DeviceLostEventArgs(RenderContext? previousContext, RenderContext newContext)
    {
        PreviousContext = previousContext;
        NewContext = newContext;
    }
}

/// <summary>
/// Represents a native rendering context.
/// </summary>
public sealed class RenderContext : IDisposable
{
    private static readonly object s_sync = new();
    private static readonly HashSet<RenderContext> _retiredContexts = [];
    private static int _generationCounter;
    private static RenderContext? _current;
    private nint _handle;
    private int _disposed; // 0 = not disposed, 1 = disposed (Interlocked for thread-safety)
    private bool _retireRequested;
    // Number of live resources whose native lifetime is bound to this context's
    // backend: render targets AND backend-owned dependent handles
    // (InkLayerBitmap, BrushShaderHandle, …). A retired context must not destroy
    // its backend while this is non-zero — those handles route their native
    // destroy back through the creating backend, so destroying the backend first
    // would be a use-after-free. See RegisterRenderTarget / UnregisterRenderTarget.
    private int _activeRenderTargetCount;

    /// <summary>
    /// Gets the current render context.
    /// </summary>
    public static RenderContext? Current => _current;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public nint Handle => _handle;

    /// <summary>
    /// Gets the active backend type.
    /// </summary>
    public RenderBackend Backend { get; }

    /// <summary>
    /// Gets the unique generation identifier for this render context instance.
    /// </summary>
    public int Generation { get; }

    /// <summary>
    /// Gets whether the context is valid.
    /// </summary>
    public bool IsValid => _handle != nint.Zero && Volatile.Read(ref _disposed) == 0;

    /// <summary>
    /// Gets the GPU adapter preference used to create this context.
    /// </summary>
    public GpuPreference GpuPreference { get; }

    /// <summary>
    /// Gets or sets the default rendering engine for new render targets.
    /// </summary>
    public RenderingEngine DefaultRenderingEngine
    {
        get => _handle != nint.Zero ? NativeMethods.ContextGetDefaultEngine(_handle) : RenderingEngine.Auto;
        set
        {
            if (_handle != nint.Zero)
            {
                NativeMethods.ContextSetDefaultEngine(_handle, value);
            }
        }
    }

    /// <summary>
    /// Raised when the rendering device is lost and a new context has been created.
    /// Subscribers should release cached GPU resources and recreate them.
    /// </summary>
    public static event EventHandler<DeviceLostEventArgs>? DeviceLost;

    /// <summary>
    /// Creates a new render context with the specified backend.
    /// </summary>
    /// <param name="backend">The rendering backend to use.</param>
    /// <param name="gpuPreference">GPU adapter preference for multi-GPU systems.</param>
    /// <param name="renderingEngine">The rendering engine to use (Auto selects the best for the platform).</param>
    public RenderContext(
        RenderBackend backend = RenderBackend.Auto,
        GpuPreference gpuPreference = GpuPreference.Auto,
        RenderingEngine renderingEngine = RenderingEngine.Auto)
    {
        backend = NormalizeRequestedBackend(backend);

        // Lazy load the chosen backend's native DLL right before we ask the
        // native registry to materialize a context. This is the single point
        // where a non-default backend (e.g. Vulkan on Windows) gets brought
        // into the process; if no caller ever requests it, jalium.native.vulkan
        // and vulkan-1.dll remain unloaded for the lifetime of the process.
        NativeMethods.EnsureBackendInitialized(backend);

        _handle = NativeMethods.ContextCreate(backend);

        // If Auto failed, explicitly retry with Software as last-resort fallback.
        if (_handle == nint.Zero && backend != RenderBackend.Software)
        {
            NativeMethods.EnsureBackendInitialized(RenderBackend.Software);
            _handle = NativeMethods.ContextCreate(RenderBackend.Software);
        }

        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException($"Failed to create render context with backend {backend}. No rendering backends are available.");
        }

        Backend = NativeMethods.ContextGetBackend(_handle);
        GpuPreference = gpuPreference;
        Generation = Interlocked.Increment(ref _generationCounter);
        _current ??= this;

        // Apply rendering engine: explicit parameter takes priority, then env var, then Auto
        var engine = NormalizeRenderingEngine(renderingEngine);
        NativeMethods.ContextSetDefaultEngine(_handle, engine);
    }

    /// <summary>
    /// Resolves the rendering engine: if Auto, checks env var override, otherwise keeps Auto
    /// (native layer resolves Auto → concrete engine based on backend).
    /// </summary>
    private static RenderingEngine NormalizeRenderingEngine(RenderingEngine requested)
    {
        // If explicitly set (not Auto), use it directly
        if (requested != RenderingEngine.Auto)
        {
            return requested;
        }

        // Check environment variable override
        return RenderBackendSelector.GetPreferredRenderingEngine();
    }

    /// <summary>
    /// Gets the current context or creates a new one when unavailable.
    /// </summary>
    /// <param name="backend">
    /// The desired backend. <see cref="RenderBackend.Auto"/> accepts whatever
    /// context already exists (or creates the platform default when there is
    /// none). An explicit backend (e.g. <see cref="RenderBackend.Vulkan"/>) is a
    /// hard requirement: if the current context runs a <em>different</em> backend
    /// and the requested one is available, the current context is retired and
    /// replaced with one running the requested backend. (If the requested backend
    /// is unavailable the current context is left untouched rather than downgraded
    /// to the software rasterizer.) This is what makes a single
    /// <c>GetOrCreateCurrent(RenderBackend.Vulkan)</c>
    /// reliably switch even after the background GPU prewarm
    /// (<see cref="RenderBackend.Auto"/> → platform default, D3D12 on Windows)
    /// has already populated <see cref="Current"/>. Call it before the first
    /// window builds its render target: a context still pinned by a live render
    /// target (see <see cref="RegisterRenderTarget"/>) is retired but cannot be
    /// torn down until that target is released, so an already-rendering window
    /// keeps its original backend until its render target is recreated.
    /// </param>
    /// <param name="gpuPreference">GPU adapter preference for multi-GPU systems.</param>
    /// <param name="forceReplace">
    /// Forces a brand-new context even when the current one already satisfies the
    /// request (used to recover from device-lost scenarios).
    /// </param>
    public static RenderContext GetOrCreateCurrent(RenderBackend backend = RenderBackend.Auto, GpuPreference gpuPreference = GpuPreference.Auto, bool forceReplace = false)
    {
        // Capture whether the caller named a concrete backend BEFORE Auto is
        // resolved to the platform default. Only an explicitly requested backend
        // is enforced against the existing context; an Auto request is satisfied
        // by whatever context is already current. That asymmetry is deliberate:
        // it lets the prewarm's Auto call and the window's Auto call happily reuse
        // a Vulkan context an explicit caller installed, instead of clobbering it
        // back to the platform default.
        bool explicitBackend = backend != RenderBackend.Auto;
        backend = NormalizeRequestedBackend(backend);
        gpuPreference = NormalizeGpuPreference(gpuPreference);

        // Only enforce (i.e. retire + replace a different-backend current context)
        // when the requested backend is genuinely available. If it is not, the
        // constructor would fall back to Software (see ContextCreate → Software),
        // which would needlessly downgrade a working GPU context to the software
        // rasterizer — and, because the created context's Backend could then never
        // equal the request, every subsequent explicit call would churn another
        // fallback context. Probing availability here also performs the same lazy
        // backend-DLL load the constructor relies on. Short-circuits for Auto so
        // the common path pays no native query.
        bool enforceBackend = explicitBackend &&
            NativeMethods.IsBackendAvailable(backend) != 0;

        var current = _current;
        if (!forceReplace && current != null && current.IsValid &&
            (!enforceBackend || current.Backend == backend))
        {
            return current;
        }

        RenderContext? previous;
        RenderContext context;
        bool clearTextMeasurementCache = false;
        lock (s_sync)
        {
            current = _current;
            if (!forceReplace && current != null && current.IsValid &&
                (!enforceBackend || current.Backend == backend))
            {
                return current;
            }

            previous = current;
            context = new RenderContext(backend, gpuPreference);
            _current = context;
            clearTextMeasurementCache = previous != null && !ReferenceEquals(previous, context);

            // Retire the displaced context when we forced a replacement, or when an
            // available explicit backend request swapped out a context running a
            // different backend. Retirement defers native teardown until the
            // context's last render target / dependent handle is released, so this
            // stays safe even if something still holds the old context.
            if (previous != null &&
                previous.IsValid &&
                !ReferenceEquals(previous, context) &&
                (forceReplace || (enforceBackend && previous.Backend != backend)))
            {
                previous._retireRequested = true;
                _retiredContexts.Add(previous);
            }
        }

        if (clearTextMeasurementCache)
        {
            TextMeasurement.ClearCache();
        }

        TryDisposeRetiredContexts();
        return context;
    }

    /// <summary>
    /// Pins this context as having one more live backend-bound resource (a
    /// render target or a backend-owned dependent handle such as
    /// <see cref="InkLayerBitmap"/> / <see cref="BrushShaderHandle"/>). While the
    /// count is non-zero a retired context will not destroy its native backend,
    /// keeping any handle that routes its destroy through that backend safe.
    /// Every call must be balanced by exactly one <see cref="UnregisterRenderTarget"/>.
    /// </summary>
    internal void RegisterRenderTarget()
    {
        _ = Interlocked.Increment(ref _activeRenderTargetCount);
    }

    /// <summary>
    /// Releases a pin taken by <see cref="RegisterRenderTarget"/>. When the last
    /// pin is released this drives the deferred disposal of any retired context
    /// whose resources have now fully drained (the only safe point at which a
    /// retired backend may be destroyed).
    /// </summary>
    internal void UnregisterRenderTarget()
    {
        var remaining = Interlocked.Decrement(ref _activeRenderTargetCount);
        if (remaining <= 0)
        {
            if (remaining < 0)
            {
                _ = Interlocked.Exchange(ref _activeRenderTargetCount, 0);
            }

            TryDisposeRetiredContexts();
        }
    }

    private bool CanDisposeRetiredUnsafe()
        => _retireRequested &&
           Volatile.Read(ref _disposed) == 0 &&
           _handle != nint.Zero &&
           Volatile.Read(ref _activeRenderTargetCount) == 0 &&
           !ReferenceEquals(_current, this);

    private static void TryDisposeRetiredContexts()
    {
        List<RenderContext>? toDispose = null;
        lock (s_sync)
        {
            foreach (var candidate in _retiredContexts)
            {
                if (!candidate.CanDisposeRetiredUnsafe())
                {
                    continue;
                }

                toDispose ??= [];
                toDispose.Add(candidate);
            }

            if (toDispose == null)
            {
                return;
            }

            foreach (var candidate in toDispose)
            {
                _retiredContexts.Remove(candidate);
            }
        }

        foreach (var candidate in toDispose)
        {
            candidate.Dispose();
        }
    }

    /// <summary>
    /// Creates a render target for a window handle.
    /// </summary>
    public RenderTarget CreateRenderTarget(nint hwnd, int width, int height)
    {
        ThrowIfDisposed();
        return CreateRenderTarget(NativeSurfaceDescriptor.ForWindowsHwnd(hwnd), width, height);
    }

    /// <summary>
    /// Creates a render target with composition swap chain for per-pixel alpha transparency.
    /// </summary>
    public RenderTarget CreateRenderTargetForComposition(nint hwnd, int width, int height)
    {
        ThrowIfDisposed();
        return CreateRenderTargetForComposition(NativeSurfaceDescriptor.ForWindowsHwnd(hwnd, composition: true), width, height);
    }

    internal RenderTarget CreateRenderTarget(NativeSurfaceDescriptor surface, int width, int height)
    {
        ThrowIfDisposed();
        return new RenderTarget(this, surface, width, height);
    }

    internal RenderTarget CreateRenderTargetForComposition(NativeSurfaceDescriptor surface, int width, int height)
    {
        ThrowIfDisposed();
        return new RenderTarget(this, surface, width, height, useComposition: true);
    }

    /// <summary>
    /// Creates a solid color brush.
    /// </summary>
    public NativeBrush CreateSolidBrush(float r, float g, float b, float a = 1.0f)
    {
        ThrowIfDisposed();
        return new NativeBrush(this, r, g, b, a);
    }

    /// <summary>
    /// Creates a linear gradient brush.
    /// </summary>
    public NativeBrush CreateLinearGradientBrush(
        float startX, float startY, float endX, float endY,
        float[] stops, uint stopCount, uint extendMode = 0)
    {
        ThrowIfDisposed();
        return new NativeBrush(this, startX, startY, endX, endY, stops, stopCount, extendMode);
    }

    /// <summary>
    /// Creates a radial gradient brush.
    /// </summary>
    public NativeBrush CreateRadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        float[] stops, uint stopCount, uint extendMode = 0)
    {
        ThrowIfDisposed();
        return new NativeBrush(this, centerX, centerY, radiusX, radiusY,
            originX, originY, stops, stopCount, extendMode);
    }

    /// <summary>
    /// Creates a text format.
    /// </summary>
    public NativeTextFormat CreateTextFormat(string fontFamily, float fontSize, int fontWeight = 400, int fontStyle = 0)
    {
        ThrowIfDisposed();
        return new NativeTextFormat(this, fontFamily, fontSize, fontWeight, fontStyle);
    }

    /// <summary>
    /// Creates a bitmap from encoded image data (PNG, JPEG, etc.).
    /// </summary>
    public NativeBitmap CreateBitmap(byte[] imageData)
    {
        ThrowIfDisposed();
        return new NativeBitmap(this, imageData);
    }

    /// <summary>
    /// Creates a bitmap from raw BGRA8 pixel data in STRAIGHT (non-premultiplied)
    /// alpha. The caller stays backend-agnostic: each native backend premultiplies
    /// internally where its blend requires it, so the same straight pixels are
    /// correct on D3D12, Vulkan and the software rasterizer alike.
    /// </summary>
    public NativeBitmap CreateBitmapFromPixels(byte[] pixelData, int width, int height, int stride = 0)
    {
        ThrowIfDisposed();
        return new NativeBitmap(this, pixelData, width, height, stride);
    }

    /// <summary>
    /// Attempts to recover from a device-lost scenario by creating a new render context.
    /// Returns the new context if recovery succeeds, or null if it fails.
    /// </summary>
    public static RenderContext? TryRecoverFromDeviceLost(RenderBackend backend = RenderBackend.Auto, GpuPreference gpuPreference = GpuPreference.Auto)
    {
        try
        {
            var previous = _current;
            var newContext = GetOrCreateCurrent(backend, gpuPreference, forceReplace: true);

            if (newContext.IsValid)
            {
                DeviceLost?.Invoke(null, new DeviceLostEventArgs(previous, newContext));
                return newContext;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks whether the current context's device is still operational.
    /// </summary>
    public bool CheckDeviceStatus()
    {
        if (Volatile.Read(ref _disposed) != 0 || _handle == nint.Zero)
            return false;

        var status = NativeMethods.ContextCheckDeviceStatus(_handle);
        if (status == 0) // device OK
            return true;

        // Device lost — attempt recovery with same GPU preference
        var recovered = TryRecoverFromDeviceLost(Backend, GpuPreference);
        return recovered != null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    /// <summary>
    /// Gets information about the GPU adapter selected by this context.
    /// Returns null if adapter info is not available (e.g. software backend).
    /// </summary>
    public AdapterInfo? GetAdapterInfo()
    {
        if (Volatile.Read(ref _disposed) != 0 || _handle == nint.Zero)
            return null;

        if (NativeGpuMethods.ContextGetAdapterInfo(_handle, out var info) == 0)
            return info;

        return null;
    }

    private static RenderBackend NormalizeRequestedBackend(RenderBackend backend)
        => backend == RenderBackend.Auto
            ? RenderBackendSelector.GetPreferredBackend()
            : backend;

    private static GpuPreference NormalizeGpuPreference(GpuPreference preference)
        => preference == GpuPreference.Auto
            ? RenderBackendSelector.GetPreferredGpuPreference()
            : preference;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        var handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero)
        {
            NativeMethods.ContextDestroy(handle);
        }

        lock (s_sync)
        {
            _retiredContexts.Remove(this);
            if (_current == this)
            {
                _current = null;
            }
        }

        GC.SuppressFinalize(this);
    }

    ~RenderContext()
    {
        Volatile.Write(ref _disposed, 1);
        Volatile.Write(ref _handle, nint.Zero);
    }
}
