using Jalium.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Jalium.UI.Hosting;

/// <summary>
/// UI-thread service that watches every rendered <see cref="Visual"/> via
/// <see cref="Visual.VisualRenderedObserver"/>, periodically scans its tracked
/// set, and releases retained resources for visuals that have stayed
/// off-screen longer than <see cref="ResourceReclamationOptions.IdleTimeoutMs"/>.
/// Activated by <see cref="JaliumAppExtensions.UseIdleResourceReclamation"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every interaction happens on the UI thread:
/// <see cref="Visual.VisualRenderedObserver"/> fires synchronously inside
/// <see cref="Visual.Render"/>, and the periodic scan piggybacks on
/// <see cref="CompositionTarget.Rendering"/> which dispatches to the UI
/// thread. That means the tracking list and the iterated state can stay
/// lock-free.
/// </para>
/// <para>
/// Visuals are held by <see cref="WeakReference{T}"/> so the reclaimer never
/// extends an element's lifetime — dead entries are pruned during the periodic
/// scan. The first time a visual is observed it is added to the list and
/// <see cref="Visual.IsTrackedByIdleReclaimer"/> is set so all subsequent
/// renders take an early-return fast path.
/// </para>
/// </remarks>
internal sealed class ResourceReclaimer : IDisposable
{
    private readonly ResourceReclamationOptions _options;
    private readonly List<WeakReference<Visual>> _tracked = new(capacity: 256);
    private readonly Action<Visual> _onVisualRenderedDelegate;
    private readonly EventHandler _onRenderingDelegate;
    private int _frameCounter;
    private long _lastBackendReclaimTickMs;
    private bool _disposed;

    public ResourceReclaimer(ResourceReclamationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _onVisualRenderedDelegate = OnVisualRendered;
        _onRenderingDelegate = OnRendering;
    }

    /// <summary>
    /// Installs the <see cref="Visual.VisualRenderedObserver"/> hook and
    /// subscribes to <see cref="CompositionTarget.Rendering"/> so the periodic
    /// scan starts running. Idempotent — safe to call after a previous
    /// <see cref="Stop"/>.
    /// </summary>
    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ResourceReclaimer));

        _options.Enabled = true;

        // Multicast — composes with anyone else who installs an observer
        // (DevTools, profiling overlays, etc.).
        Visual.VisualRenderedObserver += _onVisualRenderedDelegate;
        CompositionTarget.Rendering += _onRenderingDelegate;
    }

    /// <summary>
    /// Removes the render-loop hooks and clears the tracked set. Called by
    /// <see cref="Dispose"/>; can also be invoked at runtime to suspend
    /// reclamation without tearing down the service.
    /// </summary>
    public void Stop()
    {
        Visual.VisualRenderedObserver -= _onVisualRenderedDelegate;
        CompositionTarget.Rendering -= _onRenderingDelegate;

        _options.Enabled = false;

        // Clear the "tracked" flag on live entries so a future Start() can
        // re-track them. Walk the list once; dead entries are dropped silently.
        for (int i = 0; i < _tracked.Count; i++)
        {
            if (_tracked[i].TryGetTarget(out var v))
            {
                v.IsTrackedByIdleReclaimer = false;
            }
        }
        _tracked.Clear();
        _frameCounter = 0;
        _lastBackendReclaimTickMs = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── render-hot-path callback ────────────────────────────────────────────

    private void OnVisualRendered(Visual visual)
    {
        // Fast path: already tracked. This branch hits on the vast majority
        // of frames — only the very first render of a visual takes the slow
        // path that touches the list.
        if (visual.IsTrackedByIdleReclaimer) return;

        visual.IsTrackedByIdleReclaimer = true;
        _tracked.Add(new WeakReference<Visual>(visual));
    }

    // ── periodic scan (UI thread, throttled by ScanFrameInterval) ──────────

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_options.Enabled) return;

        var interval = _options.ScanFrameInterval;
        if (interval < 1) interval = 1;

        if (++_frameCounter < interval) return;
        _frameCounter = 0;

        ScanAndReclaim();
    }

    /// <summary>
    /// Walk the tracked-visuals list and reclaim resources for any visual that
    /// has stayed unrendered past <see cref="ResourceReclamationOptions.IdleTimeoutMs"/>.
    /// Exposed at <c>internal</c> for the test suite — production code reaches
    /// it only through <see cref="OnRendering"/>.
    /// </summary>
    internal void ScanAndReclaim()
    {
        var idleTimeoutMs = _options.IdleTimeoutMs;
        if (idleTimeoutMs < 250) idleTimeoutMs = 250;

        var nowMs = Environment.TickCount64;
        var threshold = nowMs - idleTimeoutMs;

        var evictCache = _options.EvictDrawingCache;
        var invokeReclaimable = _options.InvokeReclaimableResources;

        // Walk-and-compact: process live entries, drop dead ones in place.
        // Iterating from the back lets us swap-remove without disturbing
        // the indices we have not yet visited.
        var write = 0;
        for (int read = 0; read < _tracked.Count; read++)
        {
            var weak = _tracked[read];
            if (!weak.TryGetTarget(out var visual))
            {
                // Dead — drop by skipping the write.
                continue;
            }

            // The visual.LastRenderedTickMs == 0 case ("never rendered") cannot
            // occur here because OnVisualRendered only adds visuals that have
            // just rendered; their tick is non-zero by construction.
            if (visual.LastRenderedTickMs <= threshold)
            {
                // Idle — release reclaimable resources. Wrapped so a misbehaving
                // implementation cannot take down the render-loop callback.
                if (evictCache)
                {
                    try { visual.EvictRetainedDrawingCache(); }
                    catch { /* swallow — never let cleanup poison the loop */ }
                }

                if (invokeReclaimable && visual is IReclaimableResource reclaimable)
                {
                    try { reclaimable.ReclaimIdleResources(); }
                    catch { /* swallow — same rationale */ }
                }
            }

            if (write != read) _tracked[write] = weak;
            write++;
        }

        if (write < _tracked.Count)
        {
            _tracked.RemoveRange(write, _tracked.Count - write);
        }

        // Backend-cache reclamation runs at the same tick cadence as the
        // managed scan but is throttled to one call per IdleTimeoutMs window —
        // there is no value in clearing the path/text caches more often than
        // the idle window an element has to cross. ScanFrameInterval already
        // gates how often we get here; the timestamp guard just prevents
        // wasted native traffic when a user has set a very tight scan
        // interval. Throttle is applied per process, not per window — every
        // window's render target is reclaimed in the same tick.
        if (_options.ReclaimBackendCaches &&
            (_lastBackendReclaimTickMs == 0 ||
             nowMs - _lastBackendReclaimTickMs >= idleTimeoutMs))
        {
            _lastBackendReclaimTickMs = nowMs;
            ReclaimBackendCaches();
        }
    }

    private static void ReclaimBackendCaches()
    {
        // Snapshot first so a window closing inside ReclaimIdleResources()
        // (e.g. a popup auto-dismiss) cannot invalidate iteration. Each call
        // is wrapped because a backend that throws here would prevent later
        // windows from being reclaimed.
        var windows = Window.SnapshotOpenWindows();
        for (int i = 0; i < windows.Length; i++)
        {
            try
            {
                windows[i].RenderTarget?.ReclaimIdleResources();
            }
            catch
            {
                // Same rationale as the per-element try/catch above — never
                // let a backend cleanup poison the render-loop callback.
            }
        }
    }
}
