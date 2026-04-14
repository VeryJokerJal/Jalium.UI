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

        _handle = NativeMethods.ContextCreate(backend);

        // If Auto failed, explicitly retry with Software as last-resort fallback.
        if (_handle == nint.Zero && backend != RenderBackend.Software)
        {
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
    /// Optionally forces a replacement to recover from device-lost scenarios.
    /// </summary>
    public static RenderContext GetOrCreateCurrent(RenderBackend backend = RenderBackend.Auto, GpuPreference gpuPreference = GpuPreference.Auto, bool forceReplace = false)
    {
        backend = NormalizeRequestedBackend(backend);
        gpuPreference = NormalizeGpuPreference(gpuPreference);

        var current = _current;
        if (!forceReplace && current != null && current.IsValid)
        {
            return current;
        }

        RenderContext? previous;
        RenderContext context;
        bool clearTextMeasurementCache = false;
        lock (s_sync)
        {
            current = _current;
            if (!forceReplace && current != null && current.IsValid)
            {
                return current;
            }

            previous = current;
            context = new RenderContext(backend, gpuPreference);
            _current = context;
            clearTextMeasurementCache = previous != null && !ReferenceEquals(previous, context);

            if (previous != null &&
                previous.IsValid &&
                !ReferenceEquals(previous, context) &&
                forceReplace)
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

    internal void RegisterRenderTarget()
    {
        _ = Interlocked.Increment(ref _activeRenderTargetCount);
    }

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
    /// Creates a bitmap from raw BGRA8 pixel data.
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
