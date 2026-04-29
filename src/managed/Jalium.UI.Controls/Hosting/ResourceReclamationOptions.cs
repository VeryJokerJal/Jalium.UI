namespace Jalium.UI.Hosting;

/// <summary>
/// Tunables for the idle-resource reclaimer activated by
/// <see cref="JaliumAppExtensions.UseIdleResourceReclamation"/>. The
/// reclaimer watches every rendered <see cref="Visual"/>, and once a visual
/// has stayed off-screen (collapsed, hidden, scrolled out of the viewport,
/// or in a window that is no longer being painted) for
/// <see cref="IdleTimeoutMs"/> milliseconds, it evicts that visual's retained
/// drawing-cache slot and invokes <see cref="IReclaimableResource.ReclaimIdleResources"/>
/// on any element that opted in.
/// </summary>
/// <remarks>
/// Registered as a singleton on the DI container by
/// <see cref="JaliumAppExtensions.UseIdleResourceReclamation"/>. Defaults are
/// chosen so most applications can opt in with a single call and never touch
/// the options object.
/// </remarks>
public sealed class ResourceReclamationOptions
{
    /// <summary>
    /// Master switch. <see cref="JaliumAppExtensions.UseIdleResourceReclamation"/>
    /// flips this to <see langword="true"/> and starts the scan loop. Setting it
    /// back to <see langword="false"/> at runtime stops further reclamation but
    /// does not restore previously released resources (they are re-acquired
    /// lazily on the next render).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How long a visual must stay unrendered before its resources are reclaimed,
    /// in milliseconds. Default: <c>2000</c> (2 seconds) — long enough to ride
    /// through a fast scroll-then-scroll-back without thrashing decode caches,
    /// short enough that a hidden tab releases its bitmaps within a couple of
    /// seconds. Minimum: <c>250</c>; values below the floor are clamped.
    /// </summary>
    public int IdleTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// How often the reclaimer walks its tracked-visuals table looking for idle
    /// entries, expressed as a number of <see cref="CompositionTarget.Rendering"/>
    /// frames. Default: <c>60</c> — once per second at 60 Hz, low enough that
    /// the scan never shows up on a flame graph. Minimum: <c>1</c>.
    /// </summary>
    public int ScanFrameInterval { get; set; } = 60;

    /// <summary>
    /// When <see langword="true"/> (default), the reclaimer evicts the
    /// retained-mode drawing-cache slot on every idle visual — a zero-risk
    /// release because the cache is rebuilt from <see cref="Visual.OnRender"/>
    /// the next time the element draws. Set to <see langword="false"/> if you
    /// want only <see cref="IReclaimableResource.ReclaimIdleResources"/> to fire
    /// (i.e. preserve the baked command list across visibility flips).
    /// </summary>
    public bool EvictDrawingCache { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (default), the reclaimer invokes
    /// <see cref="IReclaimableResource.ReclaimIdleResources"/> on every idle
    /// visual that implements the interface, letting controls release their
    /// own decoded pixels, GPU uploads, or media frames. Set to
    /// <see langword="false"/> to opt out at the application level without
    /// changing per-control code.
    /// </summary>
    public bool InvokeReclaimableResources { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (default), the reclaimer also asks every
    /// active <see cref="Window"/>'s native render target to drop its
    /// backend-side caches — Vulkan path-geometry tessellation, rasterized
    /// text bitmaps, glyph atlas pages, gradient stops, etc. The call is
    /// throttled by <see cref="IdleTimeoutMs"/> (i.e. fires at most once per
    /// idle window) so it never piles up native work.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="false"/> for applications that prioritize the
    /// next-frame latency cost of rebuilding those caches over the memory
    /// they hold (e.g. a real-time animation that scrolls through hundreds
    /// of unique paths every second). The per-element <see cref="IReclaimableResource"/>
    /// path is unaffected.
    /// </remarks>
    public bool ReclaimBackendCaches { get; set; } = true;
}
