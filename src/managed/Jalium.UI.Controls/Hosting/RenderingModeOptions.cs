using Jalium.UI.Interop;

namespace Jalium.UI.Hosting;

/// <summary>
/// How the framework turns a frame's damage into GPU work. Selected via
/// <c>app.UseRenderingMode(...)</c>; defaults to <see cref="Adaptive"/>.
/// </summary>
public enum RenderingMode
{
    /// <summary>
    /// Pick per GPU adapter at runtime: integrated / software adapters route to
    /// <see cref="Performance"/> (once damage-scoped rendering is available),
    /// discrete adapters route to <see cref="FullFrame"/>. This is the default.
    /// </summary>
    Adaptive = 0,

    /// <summary>
    /// Damage-scoped: each present does GPU work proportional to what actually
    /// changed, not the whole window — lowest GPU / power, best for weak
    /// integrated GPUs. Runs on the inline present path (never the render thread,
    /// which issues a full invalidation every frame and would defeat scoping).
    /// </summary>
    Performance = 1,

    /// <summary>
    /// Full frames: every present rasterizes the whole window. Smoothest / highest
    /// fidelity, highest GPU — best for discrete GPUs or ample GPU headroom. This
    /// is the framework's historical behavior.
    /// </summary>
    FullFrame = 2,
}

/// <summary>
/// Process-wide rendering-mode selection. Activated by
/// <c>app.UseRenderingMode(RenderingMode)</c>; defaults to <see cref="RenderingMode.Adaptive"/>.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the <c>JALIUM_DAMAGE_SCOPED</c> environment switch from the damage-scoped
/// present design (<c>docs/design/phase-b-damage-scoped-present.md</c>) with a first-class
/// API. <see cref="RenderingMode.Performance"/>'s GPU savings are delivered by the native
/// damage-scoped present path; until that lands (<see cref="DamageScopedAvailable"/> is
/// <see langword="false"/>) Performance runs the inline full-frame path and
/// <see cref="RenderingMode.Adaptive"/> keeps weak adapters on
/// <see cref="RenderingMode.FullFrame"/> so nothing regresses.
/// </para>
/// <para>
/// The instance is a process-wide singleton (<see cref="Current"/>). Setting
/// <see cref="Mode"/> or <see cref="DamageScopedAvailable"/> re-resolves the cached
/// <see cref="RenderingMode.Adaptive"/> decision.
/// </para>
/// </remarks>
public sealed class RenderingModeOptions
{
    /// <summary>The single process-wide instance.</summary>
    public static RenderingModeOptions Current { get; } = new();

    private readonly object _sync = new();
    private RenderingMode _mode = RenderingMode.Adaptive;
    private bool _damageScopedAvailable;
    private RenderingMode? _resolvedAdaptive;

    /// <summary>
    /// The mode the app selected. Setting it invalidates the cached
    /// <see cref="RenderingMode.Adaptive"/> resolution.
    /// </summary>
    public RenderingMode Mode
    {
        get { lock (_sync) return _mode; }
        set { lock (_sync) { _mode = value; _resolvedAdaptive = null; } }
    }

    /// <summary>
    /// Whether the native damage-scoped present path exists this session. The
    /// renderer flips this on at startup when the active backend reports the
    /// capability. Until then, <see cref="RenderingMode.Performance"/> has no
    /// GPU-saving effect and <see cref="RenderingMode.Adaptive"/> does not route
    /// weak adapters to Performance (avoids regressing them onto a laggy inline
    /// full-frame path). Setting it invalidates the cached Adaptive resolution.
    /// </summary>
    public bool DamageScopedAvailable
    {
        get { lock (_sync) return _damageScopedAvailable; }
        set { lock (_sync) { _damageScopedAvailable = value; _resolvedAdaptive = null; } }
    }

    /// <summary>
    /// The concrete mode after resolving <see cref="RenderingMode.Adaptive"/> against
    /// the active GPU adapter. Resolves to <see cref="RenderingMode.FullFrame"/> until
    /// an adapter is known (safe default); the result is cached once it is.
    /// </summary>
    public RenderingMode EffectiveMode
    {
        get
        {
            lock (_sync)
            {
                if (_mode != RenderingMode.Adaptive)
                    return _mode;
                if (_resolvedAdaptive is { } cached)
                    return cached;
                var resolved = ResolveAdaptiveLocked();
                if (resolved is { } r)
                {
                    _resolvedAdaptive = r; // adapter known → cache
                    return r;
                }
                return RenderingMode.FullFrame; // adapter not known yet → retry next call
            }
        }
    }

    /// <summary>
    /// Whether the damage-scoped present path should be active this session:
    /// the effective mode is <see cref="RenderingMode.Performance"/> AND the native
    /// capability exists. The native present path gates its scoped clear / batch
    /// culling / persistent-scene-surface on this.
    /// </summary>
    public bool DamageScopedEnabled
    {
        get { lock (_sync) return _damageScopedAvailable && EffectiveModeLocked() == RenderingMode.Performance; }
    }

    /// <summary>
    /// Whether the render thread may run under the active mode.
    /// <see cref="RenderingMode.Performance"/> requires the inline path (the render
    /// thread issues a full invalidation every frame — see R-A in the design doc),
    /// so it is disallowed there. Other modes leave the render thread to its own
    /// opt-in.
    /// </summary>
    public bool AllowRenderThread
    {
        get { lock (_sync) return EffectiveModeLocked() != RenderingMode.Performance; }
    }

    // Same as EffectiveMode but assumes the caller already holds _sync.
    private RenderingMode EffectiveModeLocked()
    {
        if (_mode != RenderingMode.Adaptive)
            return _mode;
        if (_resolvedAdaptive is { } cached)
            return cached;
        var resolved = ResolveAdaptiveLocked();
        if (resolved is { } r) { _resolvedAdaptive = r; return r; }
        return RenderingMode.FullFrame;
    }

    private RenderingMode? ResolveAdaptiveLocked()
    {
        AdapterInfo? info;
        try
        {
            info = RenderContext.Current?.GetAdapterInfo();
        }
        catch
        {
            info = null;
        }

        if (info is not { } a)
            return null; // adapter not known yet — don't cache

        // Integrated / software adapters are the weak, thermally-constrained targets
        // that damage-scoped rendering exists for. Route them to Performance only when
        // it can actually help; otherwise keep them on FullFrame so a weak adapter is
        // never regressed onto the laggy inline full-frame path.
        bool weak = a.AdapterType is GpuAdapterType.Integrated or GpuAdapterType.Software;
        if (weak && _damageScopedAvailable)
            return RenderingMode.Performance;
        return RenderingMode.FullFrame;
    }
}
