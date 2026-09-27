using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Threading;
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
/// <see cref="Visual.Render"/>. Active-frame scans and the idle fallback timer
/// both dispatch to the UI thread. That means the tracking list and state can stay
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
    private long _lastScanTickMs;
    private DispatcherTimer? _idleTimer;
    private bool _started;
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
    /// subscribes to <see cref="CompositionTarget.Rendering"/> and starts the
    /// idle timer. Idempotent — safe to call after a previous
    /// <see cref="Stop"/>.
    /// </summary>
    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ResourceReclaimer));
        if (_started) return;
        _started = true;

        _options.Enabled = true;

        // Multicast — composes with anyone else who installs an observer
        // (DevTools, profiling overlays, etc.).
        Visual.VisualRenderedObserver += _onVisualRenderedDelegate;
        CompositionTarget.Rendering += _onRenderingDelegate;
        // Rendering is demand-driven and parks when the last animation ends.
        // A frame-only reclaimer would never visit a genuinely idle window.
        // One low-frequency UI timer covers that case without requesting a draw
        // or keeping the high-resolution animation loop awake.
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += OnIdleTick;
        _idleTimer.Start();
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
        if (_idleTimer != null)
        {
            _idleTimer.Stop();
            _idleTimer.Tick -= OnIdleTick;
            _idleTimer = null;
        }
        _started = false;

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
        _lastScanTickMs = 0;
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

    private void OnIdleTick(object? sender, EventArgs e)
    {
        if (!_options.Enabled || Environment.TickCount64 - _lastScanTickMs < 1000) return;
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

        // Publish the same window to the image sources themselves. This scan decides per element,
        // but the pixels and GPU uploads it releases belong to sources that elements SHARE (and
        // that image brushes and drawings, which own no element at all, also consume). The source
        // is therefore the last word on whether it may be reclaimed, and it answers with the very
        // window used here: drawn within it means something is still painting it.
        Jalium.UI.Media.ImageSource.ReclaimGraceMs = idleTimeoutMs;

        var nowMs = Environment.TickCount64;
        _lastScanTickMs = nowMs;
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

                if (invokeReclaimable && IsProvenOffScreen(visual))
                {
                    try
                    {
                        if (visual is Jalium.UI.Controls.Image image)
                            image.ReclaimIdleResources();
                        else if (visual is IReclaimableResource reclaimable)
                            reclaimable.ReclaimIdleResources();
                    }
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

    /// <summary>
    /// Whether a visual that has not painted for the idle window is genuinely off screen, as
    /// opposed to on screen and simply unchanged.
    /// </summary>
    /// <remarks>
    /// <para>"Has not painted recently" is not the same question as "is not on screen", and under
    /// partial presents the two answers routinely differ. A visible element that nothing has
    /// changed falls outside every dirty rect, so it paints on no frame at all while the user
    /// leaves it alone — a banner image, a title-bar logo, a preview pane. Reclaiming from that
    /// signal alone frees the decoded pixels and GPU upload of a bitmap the window is displaying,
    /// and the consequence is visible: the next frame that does repaint the element finds no pixels
    /// and re-requests a full decode, so the image blanks and returns, over and over, for as long
    /// as anything keeps producing frames (scrolling elsewhere in the window is enough).</para>
    /// <para>The host window's last whole-window frame is the missing half of the test. A frame
    /// that began AFTER this visual last painted, painted the entire window, and still did not
    /// paint this visual, proves it was not on screen — collapsed, hidden, detached, or scrolled
    /// out of the viewport, all of which the reclaimer should act on. Partial frames are excluded
    /// by construction; they only ever paint part of the window and prove nothing about the rest.
    /// A visual with no host window is unreachable from any presentation surface and needs no
    /// witness at all.</para>
    /// <para>Deliberately gates only <see cref="IReclaimableResource"/>. The retained drawing-cache
    /// eviction above stays on the plain idle test: rebuilding a command list costs one render pass
    /// and cannot make anything disappear.</para>
    /// </remarks>
    private static bool IsProvenOffScreen(Visual visual)
    {
        // Not reachable from any presentation surface: nothing can be displaying it, so no witness
        // is needed. This is the common case the reclaimer was written for — a view that has been
        // navigated away from and detached.
        var host = Window.GetWindow(visual);
        if (host is null)
            return true;

        // Collapsed or Hidden, here or at any ancestor. Also immediate: the element cannot be on
        // screen no matter what the last frame did, and waiting for a whole-window frame to say so
        // would strand the resources of every hidden tab until one happened to occur.
        if (visual is UIElement { IsVisible: false })
            return true;

        // Attached and visible, so only a whole-window frame can settle it — see the remarks above.
        // This is the scrolled-out-of-the-viewport case, and the one an on-screen static element is
        // saved by.
        return host.LastFullFramePaintStartTickMs > visual.LastRenderedTickMs;
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
                windows[i].ReclaimIdleBackendResources();
            }
            catch
            {
                // Same rationale as the per-element try/catch above — never
                // let a backend cleanup poison the render-loop callback.
            }
        }
    }
}
