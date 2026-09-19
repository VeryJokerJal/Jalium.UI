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
    // Shared Software context used only by Windows Auto windows that have no
    // content yet. It is deliberately separate from Current once any caller
    // asks for the normal platform backend, so one empty window can never
    // downgrade the rest of the process to Software.
    private static RenderContext? _emptyWindowSoftwareContext;
    // True only while the first empty-window Software context temporarily also
    // occupies Current. TextMeasurement reads Current directly, so this
    // provisional publication preserves exact title-bar metrics until a real
    // rendering demand promotes Current to the platform backend.
    private static bool _currentIsEmptyWindowProvisional;
    // Backend an explicit (non-Auto) request last installed successfully. Every
    // later Auto resolution prefers it, so an application-level backend choice
    // survives other Auto-created surfaces and device-lost
    // recovery — all of which request RenderBackend.Auto. Written under s_sync,
    // read unlocked as a hint; cleared when the current context is disposed.
    private static RenderBackend _pinnedBackend = RenderBackend.Auto;
    // Backend the caller named when _current was installed (Auto when implicit).
    private static RenderBackend _currentRequestedBackend = RenderBackend.Auto;
    // True when Current was installed or subsequently affirmed by an explicit
    // backend, GPU-preference, or rendering-engine request. Empty-window Auto
    // optimization must never route around such an application decision.
    private static bool _currentHasExplicitConfiguration;
    private nint _handle;
    // 0 = usable, 1 = disposal requested.  A requested context can retain its
    // native handle while backend-bound resources are still pinned; the final
    // pin release performs the physical native teardown.
    private int _disposed;
    private bool _retireRequested;
    // Number of Window.EmptyRendering leases that currently use this context.
    // Each lease also takes one backend-resource pin, covering the gap before a
    // RenderTarget is created and any deferred teardown after it is detached.
    private int _emptyWindowLeaseCount;
    // Number of ordinary Windows Auto windows whose current render target uses
    // this automatically-selected GPU context.  The lease is separate from the
    // target pin: it spans the gap while a target is being rebuilt and, when the
    // final automatic window releases it, allows Current to stop rooting an idle
    // GPU backend. Explicit backend/adapter/engine selections are never detached
    // by this mechanism.
    private int _automaticWindowConsumerCount;
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
    public static RenderContext? Current => Volatile.Read(ref _current);

    /// <summary>
    /// A lifetime lease for the shared Software context used by automatic empty
    /// windows. The lease is a class (rather than a copyable struct) so exactly
    /// one backend-resource pin is released.
    /// </summary>
    internal sealed class EmptyWindowSoftwareContextLease : IDisposable
    {
        private int _disposed;

        internal EmptyWindowSoftwareContextLease(RenderContext context)
        {
            Context = context;
        }

        internal RenderContext Context { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ReleaseEmptyWindowSoftwareContextLease(Context);
            }
        }
    }

    /// <summary>
    /// Keeps an automatically-selected GPU context alive for one Window across
    /// render-target replacement. The final lease release retires an otherwise
    /// implicit Current, while independent backend-resource pins keep any
    /// externally-held target/brush/bitmap safe until its own native destroy.
    /// </summary>
    internal sealed class AutomaticWindowContextLease : IDisposable
    {
        private int _disposed;

        internal AutomaticWindowContextLease(RenderContext context)
        {
            Context = context;
        }

        internal RenderContext Context { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ReleaseAutomaticWindowContextLease(Context);
            }
        }
    }

    /// <summary>
    /// Pins the native backend for one independently-held resource. The owning
    /// wrapper must destroy its native handle before disposing this lease; several
    /// backend resource destructors route cleanup through their creating backend.
    /// </summary>
    internal sealed class BackendResourceLease : IDisposable
    {
        private readonly RenderContext _context;
        private int _disposed;

        internal BackendResourceLease(RenderContext context)
        {
            _context = context;
            context.RegisterRenderTarget();
        }

        internal RenderContext Context => _context;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _context.UnregisterRenderTarget();
            }
        }
    }

    /// <summary>
    /// Tries to lease the process-shared Software context used by a Windows
    /// <see cref="RenderBackend.Auto"/> window before it has content. The lease
    /// never overrides an explicit backend choice. When no normal current
    /// context exists, the Software context is published provisionally so the
    /// global text measurement service can use the same native font factory as
    /// the empty window's custom title bar.
    /// </summary>
    internal static EmptyWindowSoftwareContextLease? TryAcquireEmptyWindowSoftwareContext()
    {
        if (!OperatingSystem.IsWindows() || HasExplicitRenderingEnvironmentOverride())
        {
            return null;
        }

        RenderContext? rejectedContext = null;
        RenderContext? acquiredContext = null;
        EmptyWindowSoftwareContextLease? lease = null;

        try
        {
            lock (s_sync)
            {
                // Re-check the environment under the same gate as the process
                // selection metadata. Environment variables are mutable, and an
                // explicit choice made between the outer check and this lock must
                // still win.
                if (HasExplicitRenderingEnvironmentOverride() ||
                    _pinnedBackend != RenderBackend.Auto ||
                    _currentHasExplicitConfiguration ||
                    (_current != null &&
                     !Volatile.Read(ref _currentIsEmptyWindowProvisional) &&
                     _currentRequestedBackend != RenderBackend.Auto))
                {
                    return null;
                }

                var context = _emptyWindowSoftwareContext;
                if (context != null &&
                    (!context.IsValid || context._retireRequested ||
                     context.Backend != RenderBackend.Software))
                {
                    _emptyWindowSoftwareContext = null;
                    context = null;
                }

                // A normal automatic Software current (for example a machine
                // with no usable GPU backend) is already the cheapest shared
                // context. Reuse it without changing its normal-Current status.
                if (context == null &&
                    _current is { } current &&
                    current.IsValid &&
                    current.Backend == RenderBackend.Software &&
                    _currentRequestedBackend == RenderBackend.Auto &&
                    _pinnedBackend == RenderBackend.Auto)
                {
                    context = current;
                }

                if (context == null)
                {
                    context = new RenderContext(
                        RenderBackend.Software,
                        GpuPreference.Auto,
                        RenderingEngine.Auto,
                        publishIfAbsent: false);

                    // The native layer also honors JALIUM_RENDER_BACKEND. If it
                    // changed concurrently, or a host supplied another override,
                    // do not mistake the resulting GPU context for the private
                    // empty-window Software pool.
                    if (context.Backend != RenderBackend.Software)
                    {
                        rejectedContext = context;
                        context = null;
                    }
                }

                if (context != null)
                {
                    _emptyWindowSoftwareContext = context;

                    if (_current == null)
                    {
                        _current = context;
                        _currentRequestedBackend = RenderBackend.Auto;
                        _currentHasExplicitConfiguration = false;
                        Volatile.Write(ref _currentIsEmptyWindowProvisional, true);
                    }

                    ObjectDisposedException.ThrowIf(
                        Volatile.Read(ref context._disposed) != 0 ||
                        context._handle == nint.Zero,
                        context);

                    // The lease itself pins the backend. A Window can acquire the
                    // context before its RenderTarget exists, and deferred render
                    // teardown can outlive the managed Window close path.
                    context._emptyWindowLeaseCount++;
                    context._activeRenderTargetCount++;
                    acquiredContext = context;
                    lease = new EmptyWindowSoftwareContextLease(context);
                    acquiredContext = null;
                }
            }
        }
        catch (Exception ex)
        {
            if (acquiredContext != null)
            {
                ReleaseEmptyWindowSoftwareContextLease(acquiredContext);
            }
            rejectedContext?.Dispose();

            if (ex is OutOfMemoryException)
            {
                throw;
            }

            // The optimization is optional. The caller will use the normal Auto
            // path, which retains the existing backend fallback diagnostics.
            System.Diagnostics.Debug.WriteLine(
                $"[RenderContext] Empty-window Software context was unavailable; using normal Auto rendering: {ex.Message}");
            return null;
        }

        rejectedContext?.Dispose();
        return lease;
    }

    private static bool HasExplicitRenderingEnvironmentOverride()
    {
        var backendConfigured = Environment.GetEnvironmentVariable(
            RenderBackendSelector.BackendOverrideEnvironmentVariable);
        if (RenderBackendSelector.TryParseBackend(backendConfigured, out var backend) &&
            backend != RenderBackend.Auto)
        {
            return true;
        }

        // A non-empty GPU preference is an intentional adapter policy. Even an
        // explicit "auto" differs from the Windows D3D12 default (high performance),
        // so preserve it by taking the ordinary rendering path from the start.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                RenderBackendSelector.GpuPreferenceEnvironmentVariable)))
        {
            return true;
        }

        var engineConfigured = Environment.GetEnvironmentVariable(
            RenderBackendSelector.EngineOverrideEnvironmentVariable);
        return RenderBackendSelector.TryParseRenderingEngine(
                   engineConfigured,
                   out var engine) &&
               engine != RenderingEngine.Auto;
    }

    private static void ReleaseEmptyWindowSoftwareContextLease(RenderContext context)
    {
        bool detachProvisionalCurrent = false;
        bool disposeContext = false;
        bool resourcesDrained;

        lock (s_sync)
        {
            if (context._emptyWindowLeaseCount <= 0)
            {
                return;
            }

            context._emptyWindowLeaseCount--;
            if (context._activeRenderTargetCount > 0)
            {
                context._activeRenderTargetCount--;
            }

            if (context._emptyWindowLeaseCount == 0)
            {
                if (ReferenceEquals(_emptyWindowSoftwareContext, context))
                {
                    _emptyWindowSoftwareContext = null;
                }

                if (ReferenceEquals(_current, context) &&
                    Volatile.Read(ref _currentIsEmptyWindowProvisional))
                {
                    _current = null;
                    _currentRequestedBackend = RenderBackend.Auto;
                    _pinnedBackend = RenderBackend.Auto;
                    _currentHasExplicitConfiguration = false;
                    Volatile.Write(ref _currentIsEmptyWindowProvisional, false);
                    detachProvisionalCurrent = true;
                    disposeContext = true;
                }
                else if (!ReferenceEquals(_current, context))
                {
                    // An auxiliary pool is useful only while at least one empty
                    // window owns it. Reclaim its Software worker threads as soon
                    // as the last window has released both target and lease.
                    disposeContext = true;
                }
            }

            resourcesDrained = context._activeRenderTargetCount == 0;
        }

        // Native text formats do not retain their creating context. Dispose the
        // global cache before destroying a provisional current's font factory.
        if (detachProvisionalCurrent)
        {
            TextMeasurement.ClearCache();
        }

        if (disposeContext)
        {
            context.Dispose();
        }
        else if (resourcesDrained)
        {
            TryDisposeRetiredContexts();
        }
    }

    /// <summary>
    /// Acquires Window ownership of the current automatically-selected GPU
    /// context. Software contexts use <see cref="EmptyWindowSoftwareContextLease"/>
    /// instead, and any explicit rendering choice deliberately rejects this path.
    /// </summary>
    internal static AutomaticWindowContextLease? TryAcquireAutomaticWindowContext(
        RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Allocate before publishing either count. If allocation itself fails,
        // no invisible consumer/pin is left behind in the context.
        var lease = new AutomaticWindowContextLease(context);

        lock (s_sync)
        {
            if (!ReferenceEquals(_current, context) ||
                !context.IsValid ||
                context._retireRequested ||
                context.Backend == RenderBackend.Software ||
                Volatile.Read(ref _currentIsEmptyWindowProvisional) ||
                _currentRequestedBackend != RenderBackend.Auto ||
                _pinnedBackend != RenderBackend.Auto ||
                _currentHasExplicitConfiguration)
            {
                return null;
            }

            context._automaticWindowConsumerCount++;
            context._activeRenderTargetCount++;
            return lease;
        }
    }

    /// <summary>
    /// Acquires a native-backend lifetime pin for an external resource wrapper.
    /// Construction and the zero-pin disposal decision share the same context
    /// gate through <see cref="RegisterRenderTarget"/>.
    /// </summary>
    internal BackendResourceLease AcquireBackendResourceLease()
        => new(this);

    private static void ReleaseAutomaticWindowContextLease(RenderContext context)
    {
        bool currentChanged = false;
        bool resourcesDrained;

        lock (s_sync)
        {
            if (context._automaticWindowConsumerCount <= 0)
            {
                return;
            }

            context._automaticWindowConsumerCount--;
            if (context._activeRenderTargetCount > 0)
            {
                context._activeRenderTargetCount--;
            }

            // Only an implicit Auto Current is framework-owned. An application
            // can affirm the same context explicitly after a Window acquired its
            // lease; that late decision must make the context sticky instead of
            // being undone when the Window later becomes empty or closes.
            if (context._automaticWindowConsumerCount == 0 &&
                ReferenceEquals(_current, context) &&
                !Volatile.Read(ref _currentIsEmptyWindowProvisional) &&
                _currentRequestedBackend == RenderBackend.Auto &&
                _pinnedBackend == RenderBackend.Auto &&
                !_currentHasExplicitConfiguration)
            {
                RenderContext? replacement = _emptyWindowSoftwareContext;
                if (replacement != null &&
                    (!replacement.IsValid ||
                     replacement._retireRequested ||
                     replacement.Backend != RenderBackend.Software ||
                     replacement._emptyWindowLeaseCount <= 0))
                {
                    if (ReferenceEquals(_emptyWindowSoftwareContext, replacement))
                    {
                        _emptyWindowSoftwareContext = null;
                    }
                    replacement = null;
                }

                _current = replacement;
                _currentRequestedBackend = RenderBackend.Auto;
                _currentHasExplicitConfiguration = false;
                Volatile.Write(
                    ref _currentIsEmptyWindowProvisional,
                    replacement != null);

                context._retireRequested = true;
                if (context._handle != nint.Zero)
                {
                    _retiredContexts.Add(context);
                }
                currentChanged = true;
            }

            resourcesDrained = context._activeRenderTargetCount == 0;
        }

        // Text-format cache entries are generation-keyed, but their native
        // factories belong to the previous Current. Destroy them while that
        // backend is still pinned by this method's just-released lease/resources.
        if (currentChanged)
        {
            TextMeasurement.ClearCache();
        }

        if (resourcesDrained || currentChanged)
        {
            TryDisposeRetiredContexts();
        }
    }

    internal static bool IsEmptyWindowProvisionalCurrent(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (s_sync)
        {
            return ReferenceEquals(_current, context) &&
                   Volatile.Read(ref _currentIsEmptyWindowProvisional);
        }
    }

    internal static bool IsEmptyWindowSoftwareContext(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (s_sync)
        {
            return ReferenceEquals(_emptyWindowSoftwareContext, context) ||
                   context._emptyWindowLeaseCount > 0;
        }
    }

    /// <summary>
    /// Revalidates that no explicit rendering choice appeared after an empty
    /// window acquired its lease but before it created the first target. Existing
    /// live targets retain their established owner context, matching the normal
    /// explicit-backend switching contract; this gate covers only the uncommitted
    /// lease-to-target interval.
    /// </summary>
    internal static bool IsAutomaticEmptyWindowSoftwareAllowed()
    {
        if (!OperatingSystem.IsWindows() || HasExplicitRenderingEnvironmentOverride())
        {
            return false;
        }

        lock (s_sync)
        {
            return _pinnedBackend == RenderBackend.Auto &&
                   !_currentHasExplicitConfiguration &&
                   (_current == null ||
                    Volatile.Read(ref _currentIsEmptyWindowProvisional) ||
                    _currentRequestedBackend == RenderBackend.Auto);
        }
    }

    /// <summary>
    /// Returns whether <paramref name="context"/> was installed by an unpinned
    /// <see cref="RenderBackend.Auto"/> request. Window presentation policy uses
    /// this distinction to apply compatibility fallbacks without overriding an
    /// application that explicitly required D3D12 or Vulkan.
    /// </summary>
    internal static bool IsBackendSelectionAutomatic(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (s_sync)
        {
            return ReferenceEquals(_current, context) &&
                   _currentRequestedBackend == RenderBackend.Auto &&
                   _pinnedBackend == RenderBackend.Auto &&
                   !_currentHasExplicitConfiguration;
        }
    }

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
    /// Gets whether the native backend is still physically alive.  This can
    /// remain true after <see cref="Dispose"/> has made the context unusable,
    /// while a backend-bound resource is pinned and still needs to destroy its
    /// native handle through that backend.
    /// </summary>
    internal bool IsNativeBackendAlive => _handle != nint.Zero;

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
                lock (s_sync)
                {
                    if (ReferenceEquals(_current, this))
                    {
                        _currentHasExplicitConfiguration = true;
                    }
                }
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
        : this(backend, gpuPreference, renderingEngine, publishIfAbsent: true)
    {
    }

    private RenderContext(
        RenderBackend backend,
        GpuPreference gpuPreference,
        RenderingEngine renderingEngine,
        bool publishIfAbsent)
    {
        var requestedBackend = backend;
        bool hasExplicitConfiguration =
            backend != RenderBackend.Auto ||
            gpuPreference != GpuPreference.Auto ||
            renderingEngine != RenderingEngine.Auto;
        backend = NormalizeRequestedBackend(backend);
        gpuPreference = NormalizeGpuPreference(gpuPreference, backend);

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
        int gpuPreferenceResult =
            NativeGpuMethods.ContextSetGpuPreference(_handle, gpuPreference);
        if (Backend == RenderBackend.D3D12 && gpuPreferenceResult != 0)
        {
            nint failedHandle = _handle;
            _handle = nint.Zero;
            NativeMethods.ContextDestroy(failedHandle);
            throw new InvalidOperationException(
                $"The D3D12 backend rejected GPU preference {gpuPreference} " +
                $"before device creation (result {gpuPreferenceResult}).");
        }

        GpuPreference = gpuPreference;
        Generation = Interlocked.Increment(ref _generationCounter);

        // Apply rendering engine: explicit parameter takes priority, then env var, then Auto
        var engine = NormalizeRenderingEngine(renderingEngine);
        NativeMethods.ContextSetDefaultEngine(_handle, engine);

        if (publishIfAbsent)
        {
            lock (s_sync)
            {
                if (_current == null)
                {
                    _current = this;
                    _currentRequestedBackend = requestedBackend;
                    _currentHasExplicitConfiguration = hasExplicitConfiguration;
                    Volatile.Write(ref _currentIsEmptyWindowProvisional, false);

                    // A directly-created explicit context is just as intentional
                    // as an explicit GetOrCreateCurrent request. Record the
                    // selection so later Auto callers cannot silently replace it.
                    if (requestedBackend != RenderBackend.Auto && Backend == backend)
                    {
                        _pinnedBackend = backend;
                    }
                }
            }
        }
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
    /// reliably switch even after another Auto caller
    /// (<see cref="RenderBackend.Auto"/> → platform default, D3D12 on Windows)
    /// has already populated <see cref="Current"/>. An honored explicit request
    /// also becomes sticky: every later <see cref="RenderBackend.Auto"/>
    /// resolution in the process picks that backend while it stays available, so
    /// popup / dock-indicator surfaces and device-lost recovery
    /// (all of which request Auto) cannot fall back to the platform default
    /// behind the application's back. Call it before the first
    /// window builds its render target: a context still pinned by a live render
    /// target (see <see cref="RegisterRenderTarget"/>) is retired but cannot be
    /// torn down until that target is released, so an already-rendering window
    /// keeps its original backend until its render target is recreated.
    /// </param>
    /// <param name="gpuPreference">
    /// GPU adapter preference for multi-GPU systems.
    /// <see cref="GpuPreference.Auto"/> means "no requirement": it never retires a
    /// current context whose preference differs, it only supplies the default for
    /// a context this call has to construct. Only an explicit preference is
    /// enforced against the current context.
    /// </param>
    /// <param name="forceReplace">
    /// Forces a brand-new context even when the current one already satisfies the
    /// request (used to recover from device-lost scenarios).
    /// </param>
    public static RenderContext GetOrCreateCurrent(RenderBackend backend = RenderBackend.Auto, GpuPreference gpuPreference = GpuPreference.Auto, bool forceReplace = false)
    {
        // Capture what the caller actually named BEFORE normalization resolves
        // Auto to a concrete value. Only an explicit request is enforced against
        // the existing context; an Auto request is satisfied by whatever context
        // is already current. That asymmetry is what lets every framework Auto
        // call reuse a Vulkan context an explicit caller
        // installed instead of clobbering it back to the platform default.
        //
        // The same rule must hold for the GPU preference. NormalizeGpuPreference
        // turns Auto into HighPerformance for D3D12 on Windows, so an Auto call
        // (backend Auto -> D3D12 -> HighPerformance) used to compare
        // HighPerformance against the Vulkan context's Auto preference, miss, and
        // replace the explicitly installed Vulkan context with a fresh D3D12 one.
        // A normalized default is a value for CONSTRUCTION, never a reuse
        // requirement.
        var requestedBackend = backend;
        bool explicitBackend = backend != RenderBackend.Auto;
        bool explicitGpuPreference = gpuPreference != GpuPreference.Auto;
        backend = NormalizeRequestedBackend(backend);
        gpuPreference = NormalizeGpuPreference(gpuPreference, backend);

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

        var current = Volatile.Read(ref _current);
        if (!forceReplace &&
            !explicitBackend &&
            !explicitGpuPreference &&
            !IsProvisionalCurrentFast(current) &&
            CanSatisfyRequest(current, requestedBackend, backend, gpuPreference, enforceBackend, explicitGpuPreference))
        {
            return current!;
        }

        RenderContext? previous = null;
        RenderContext? contextToDispose = null;
        RenderContext context;
        bool clearTextMeasurementCache = false;
        lock (s_sync)
        {
            current = _current;
            bool provisionalCurrent = current != null &&
                ReferenceEquals(current, _current) &&
                Volatile.Read(ref _currentIsEmptyWindowProvisional);

            if (!forceReplace && current is { IsValid: true })
            {
                if (provisionalCurrent)
                {
                    // An unavailable explicit request preserves the working
                    // provisional context, matching the normal contract that an
                    // unavailable backend never downgrades or churns Current.
                    if (explicitBackend && !enforceBackend)
                    {
                        return current;
                    }

                    // If Auto genuinely resolves to Software (or the caller
                    // explicitly selected Software), the provisional context is
                    // already the desired process backend. Promote its metadata
                    // in place rather than starting a second Software worker pool.
                    if (current.Backend == backend &&
                        CanSatisfyRequest(
                            current,
                            requestedBackend,
                            backend,
                            gpuPreference,
                            enforceBackend,
                            explicitGpuPreference))
                    {
                        Volatile.Write(ref _currentIsEmptyWindowProvisional, false);
                        _currentRequestedBackend = requestedBackend;
                        _currentHasExplicitConfiguration =
                            explicitBackend || explicitGpuPreference;
                        if (explicitBackend && current.Backend == backend)
                        {
                            _pinnedBackend = backend;
                        }
                        return current;
                    }
                }
                else if (CanSatisfyRequest(
                             current,
                             requestedBackend,
                             backend,
                             gpuPreference,
                             enforceBackend,
                             explicitGpuPreference))
                {
                    // Reusing an automatically-created context for a matching
                    // explicit request must still make the selection sticky.
                    if (explicitBackend && enforceBackend && current.Backend == backend)
                    {
                        _currentRequestedBackend = requestedBackend;
                        _pinnedBackend = backend;
                    }
                    if (explicitBackend || explicitGpuPreference)
                    {
                        _currentHasExplicitConfiguration = true;
                    }
                    return current;
                }
            }

            previous = current;
            context = new RenderContext(
                backend,
                gpuPreference,
                RenderingEngine.Auto,
                publishIfAbsent: false);

            // A requested GPU backend can still fail between availability probe
            // and context construction. The constructor's last-resort Software
            // fallback must not leave two Software backends (and two worker pools)
            // alive when a provisional Software context already exists.
            if (!forceReplace &&
                provisionalCurrent &&
                requestedBackend == RenderBackend.Auto &&
                previous!.Backend == RenderBackend.Software &&
                context.Backend == RenderBackend.Software)
            {
                Volatile.Write(ref _currentIsEmptyWindowProvisional, false);
                _currentRequestedBackend = RenderBackend.Auto;
                _currentHasExplicitConfiguration = false;
                contextToDispose = context;
                context = previous;
            }
            else
            {
                _current = context;
                _currentRequestedBackend = requestedBackend;
                _currentHasExplicitConfiguration =
                    explicitBackend || explicitGpuPreference;
                Volatile.Write(ref _currentIsEmptyWindowProvisional, false);

                // Sticky selection: once an explicit request is honored, every later
                // Auto resolution in this process resolves to that backend — including
                // the device-lost paths that pass RenderBackend.Auto with forceReplace.
                // Only pin an honored request: a request that silently degraded (e.g.
                // the constructor's Software last resort) must not become the process
                // default.
                if (explicitBackend && context.Backend == backend)
                {
                    _pinnedBackend = backend;
                }

                clearTextMeasurementCache = previous != null && !ReferenceEquals(previous, context);

                // A displaced empty-window context remains an auxiliary Software
                // pool while its Window leases are alive. RenderTarget.OwnerContext
                // keeps all resources bound to it; the final lease reclaims it.
                // Other displaced contexts retain the existing retirement rules.
                bool previousHasEmptyWindowLeases =
                    previous?._emptyWindowLeaseCount > 0;
                if (previous != null &&
                    previous.IsValid &&
                    !ReferenceEquals(previous, context) &&
                    (!previousHasEmptyWindowLeases || forceReplace) &&
                    (forceReplace ||
                     (enforceBackend && previous.Backend != backend) ||
                     (explicitGpuPreference && previous.GpuPreference != gpuPreference)))
                {
                    previous._retireRequested = true;
                    _retiredContexts.Add(previous);
                }
            }
        }

        contextToDispose?.Dispose();

        if (clearTextMeasurementCache)
        {
            TextMeasurement.ClearCache();
        }

        TryDisposeRetiredContexts();
        return context;
    }

    private static bool IsProvisionalCurrentFast(RenderContext? context)
        => context != null &&
           ReferenceEquals(context, Volatile.Read(ref _current)) &&
           Volatile.Read(ref _currentIsEmptyWindowProvisional);

    /// <summary>
    /// Decides whether <paramref name="current"/> already satisfies the request.
    /// Backend and GPU preference are only compared when the caller named them:
    /// a value produced by normalizing Auto is a construction default, not a
    /// reuse requirement.
    /// </summary>
    private static bool CanSatisfyRequest(
        RenderContext? current,
        RenderBackend requestedBackend,
        RenderBackend resolvedBackend,
        GpuPreference resolvedGpuPreference,
        bool enforceBackend,
        bool enforceGpuPreference)
    {
        if (current is null || !current.IsValid)
        {
            return false;
        }

        // A context whose Backend differs from the request is still accepted when
        // it was installed for that very request: jalium_context_create honors
        // JALIUM_RENDER_BACKEND after the fact, so an overridden request can never
        // produce a context whose Backend equals it, and without this the caller
        // would churn a brand-new context on every call.
        if (enforceBackend &&
            current.Backend != resolvedBackend &&
            _currentRequestedBackend != requestedBackend)
        {
            return false;
        }

        return !enforceGpuPreference || current.GpuPreference == resolvedGpuPreference;
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
        // Registration and the zero-pin disposal decision must be one atomic
        // transition.  Otherwise Dispose can observe zero, destroy the native
        // backend, and a racing resource constructor can pin/use the stale
        // handle immediately afterwards.
        lock (s_sync)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0 || _handle == nint.Zero,
                this);

            _activeRenderTargetCount++;
        }
    }

    /// <summary>
    /// Releases a pin taken by <see cref="RegisterRenderTarget"/>. When the last
    /// pin is released this drives the deferred disposal of any retired context
    /// whose resources have now fully drained (the only safe point at which a
    /// retired backend may be destroyed).
    /// </summary>
    internal void UnregisterRenderTarget()
    {
        bool resourcesDrained;
        lock (s_sync)
        {
            if (_activeRenderTargetCount > 0)
            {
                _activeRenderTargetCount--;
            }

            resourcesDrained = _activeRenderTargetCount == 0;
        }

        if (resourcesDrained)
        {
            TryDisposeRetiredContexts();
        }
    }

    private bool CanFinalizeNativeDisposeUnsafe()
        => (_retireRequested || Volatile.Read(ref _disposed) != 0) &&
           _handle != nint.Zero &&
           _activeRenderTargetCount == 0 &&
           !ReferenceEquals(_current, this);

    private static void TryDisposeRetiredContexts()
    {
        List<nint>? handlesToDestroy = null;
        lock (s_sync)
        {
            foreach (var candidate in _retiredContexts.ToArray())
            {
                if (!candidate.CanFinalizeNativeDisposeUnsafe())
                {
                    continue;
                }

                // Mark unusable and detach the native handle while holding the
                // same gate used by RegisterRenderTarget.  No new resource can
                // pin this context after the zero-pin decision.
                Volatile.Write(ref candidate._disposed, 1);
                var handle = candidate._handle;
                candidate._handle = nint.Zero;
                _retiredContexts.Remove(candidate);
                if (handle != nint.Zero)
                {
                    handlesToDestroy ??= [];
                    handlesToDestroy.Add(handle);
                }
            }
        }

        if (handlesToDestroy == null)
        {
            return;
        }

        foreach (var handle in handlesToDestroy)
        {
            NativeMethods.ContextDestroy(handle);
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
    /// Creates a bitmap from raw 32-bpp pixel data in STRAIGHT (non-premultiplied)
    /// alpha. The caller stays backend-agnostic: each native backend premultiplies
    /// internally where its blend requires it, so the same straight pixels are
    /// correct on D3D12, Vulkan and the software rasterizer alike.
    /// </summary>
    /// <param name="pixelData">The source buffer. Never written to.</param>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    /// <param name="stride">Bytes per row, or 0 to infer <c>width * 4</c>.</param>
    /// <param name="format">
    /// Channel order of <paramref name="pixelData"/>. The bitmap-upload ABI
    /// (<c>jalium_bitmap_create_from_pixels</c>) takes BGRA8 only, so any other order is
    /// normalized here rather than being assumed away by the caller — an Android/Mali decoder
    /// legitimately hands back <see cref="Jalium.UI.Media.Imaging.NativePixelFormat.Rgba8"/>, and uploading that as BGRA8
    /// swaps red and blue in every image with nothing reporting it.
    /// </param>
    public NativeBitmap CreateBitmapFromPixels(
        byte[] pixelData,
        int width,
        int height,
        int stride = 0,
        Jalium.UI.Media.Imaging.NativePixelFormat format = Jalium.UI.Media.Imaging.NativePixelFormat.Bgra8)
    {
        ThrowIfDisposed();

        if (format != Jalium.UI.Media.Imaging.NativePixelFormat.Bgra8)
        {
            pixelData = ConvertToBgra8(pixelData, width, height, stride, format);
        }

        return new NativeBitmap(this, pixelData, width, height, stride);
    }

    /// <summary>
    /// Rewrites a non-BGRA8 32-bpp buffer into a freshly allocated BGRA8 one.
    /// </summary>
    /// <remarks>
    /// Deliberately allocates instead of swizzling in place. The buffer handed in belongs to an
    /// immutable <c>BitmapPixelSnapshot</c> that the owning source, the downscale cache and every
    /// other render target share by reference; swizzling it in place would corrupt all of them and
    /// would double-swap on the second upload. The copy is paid once per GPU cache miss, not per
    /// frame.
    /// </remarks>
    private static byte[] ConvertToBgra8(
        byte[] pixelData,
        int width,
        int height,
        int stride,
        Jalium.UI.Media.Imaging.NativePixelFormat format)
    {
        if (format != Jalium.UI.Media.Imaging.NativePixelFormat.Rgba8)
        {
            // An order this method does not know how to rewrite. Uploading it unconverted is the
            // pre-existing behaviour and is visibly wrong rather than silently wrong, so let the
            // pixels through instead of throwing away the image entirely.
            return pixelData;
        }

        if (width <= 0 || height <= 0)
        {
            return pixelData;
        }

        var rowBytes = width * 4;
        if (stride <= 0) stride = rowBytes;
        if ((long)stride * height > pixelData.Length)
        {
            // Geometry the caller could not honour; leave the bytes alone rather than reading out
            // of range. BitmapPixelSnapshot.Create rejects this shape, so only a legacy caller can
            // reach it.
            return pixelData;
        }

        var converted = new byte[pixelData.Length];
        Array.Copy(pixelData, converted, pixelData.Length);

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * stride;
            for (var x = 0; x < rowBytes; x += 4)
            {
                var offset = rowStart + x;
                // RGBA -> BGRA: swap channels 0 and 2, leave green and alpha in place.
                converted[offset] = pixelData[offset + 2];
                converted[offset + 2] = pixelData[offset];
            }
        }

        return converted;
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

    /// <summary>
    /// Resolves an Auto backend request. A backend an explicit caller already
    /// installed wins over the platform default for as long as it stays
    /// available, so the Auto call sites (popup / dock-indicator
    /// surfaces, device-lost recovery) rebuild on the application's choice.
    /// </summary>
    private static RenderBackend NormalizeRequestedBackend(RenderBackend backend)
    {
        if (backend != RenderBackend.Auto)
        {
            return backend;
        }

        var pinned = _pinnedBackend;
        if (pinned != RenderBackend.Auto &&
            NativeMethods.IsBackendAvailable(pinned) != 0)
        {
            return pinned;
        }

        return RenderBackendSelector.GetPreferredBackend();
    }

    private static GpuPreference NormalizeGpuPreference(
        GpuPreference preference,
        RenderBackend backend)
        => preference == GpuPreference.Auto
            ? RenderBackendSelector.GetPreferredGpuPreference(backend)
            : preference;

    /// <inheritdoc />
    public void Dispose()
    {
        nint handleToDestroy = nint.Zero;
        bool clearTextMeasurementCache = false;
        lock (s_sync)
        {
            Volatile.Write(ref _disposed, 1);
            _retireRequested = true;

            if (ReferenceEquals(_emptyWindowSoftwareContext, this))
            {
                _emptyWindowSoftwareContext = null;
            }

            if (_current == this)
            {
                _current = null;
                _currentRequestedBackend = RenderBackend.Auto;
                _currentHasExplicitConfiguration = false;
                Volatile.Write(ref _currentIsEmptyWindowProvisional, false);
                // The sticky selection lives exactly as long as the context an
                // explicit request installed; a later Auto request resolves the
                // platform default again.
                _pinnedBackend = RenderBackend.Auto;
                clearTextMeasurementCache = true;
            }

            if (_handle != nint.Zero && _activeRenderTargetCount == 0)
            {
                handleToDestroy = _handle;
                _handle = nint.Zero;
                _retiredContexts.Remove(this);
            }
            else if (_handle != nint.Zero)
            {
                // Keep the backend alive until every target/dependent handle
                // has destroyed itself through that backend and released its
                // pin.  UnregisterRenderTarget drives the final teardown.
                _retiredContexts.Add(this);
            }
            else
            {
                _retiredContexts.Remove(this);
            }
        }

        // NativeTextFormat does not retain its RenderContext. Clear every global
        // format while this context's native font factory is still alive.
        if (clearTextMeasurementCache)
        {
            TextMeasurement.ClearCache();
        }

        if (handleToDestroy != nint.Zero)
        {
            NativeMethods.ContextDestroy(handleToDestroy);
        }

        GC.SuppressFinalize(this);
    }

    ~RenderContext()
    {
        // Leak-safe fallback: do not destroy the native backend from the
        // finalizer. NativeBitmap and NativeBrush now take explicit backend pins,
        // but NativeTextFormat still does not retain its creating RenderContext;
        // one can remain reachable after this managed context becomes collectible.
        // Destroying the backend here would leave that live format pointing at a
        // released font factory. Explicit Dispose remains deterministic and uses
        // the two-phase pin-aware teardown above.
        Volatile.Write(ref _disposed, 1);
        _ = Interlocked.Exchange(ref _handle, nint.Zero);
    }
}
