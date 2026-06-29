using Jalium.UI.Media;
using Jalium.UI.Rendering;

namespace Jalium.UI;

/// <summary>
/// Represents a node in the visual tree.
/// This is the base class for all renderable objects.
/// </summary>
public abstract class Visual : DependencyObject
{
    private Visual? _parent;
    private readonly List<Visual> _children = new();
    private bool _isRenderDirty;
    private bool _isSubtreeDirty;

    // Composition-only dirtiness, propagated UP the ancestor chain SEPARATELY from
    // the content flag _isSubtreeDirty. Set by MarkSubtreeDirtyForComposition (an
    // Opacity / RenderTransform / RenderTransformOrigin change) which deliberately
    // does NOT touch content. The distinction lets the damage-driven walk tell
    // "a descendant changed only how it composites (re-composite a cached layer)"
    // from "a descendant changed its CONTENT (must re-record / re-realize)". A
    // subtree is layer-eligible (Phase 4 GPU retained layer) precisely when it is
    // content-clean (!_isRenderDirty && !_isSubtreeDirty) even if composition-dirty.
    private bool _isSubtreeCompositionDirty;

    // Retained-mode drawing cache. When RenderCacheHost is installed, the
    // render loop records OnRender output into an opaque Drawing handle on
    // the first dirty frame and replays it from this slot on subsequent
    // clean frames. SetRenderDirty() implicitly invalidates the cache because
    // RenderDirect re-records whenever _isRenderDirty is true. Kept as object
    // so Core doesn't leak the Media.Rendering.Drawing concrete type.
    private object? _cachedDrawing;

    // Retained GPU layer handle (opaque native pointer; 0 = none). Holds this
    // visual's subtree CONTENT rasterized once into a persistent offscreen
    // texture for the damage-driven composited-animation fast path: while the
    // subtree is content-clean and animating Opacity/RenderTransform, the parent
    // composites this layer as a transformed quad instead of re-walking the
    // subtree (see RenderChildVisualInline / TryCompositeChildLayer).
    private nint _cachedLayer;

    // True when the layer's baked content can no longer be trusted (this visual
    // or a descendant changed CONTENT) and must be re-realized before compositing.
    private bool _layerContentDirty = true;

    // Layers whose owning visual was evicted / detached / GC'd WITHOUT a live
    // render context (no render target handle to call destroy on). Drained and
    // destroyed (fence-gated, native side) once per frame by the drawing context.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<nint> s_pendingLayerDestroy = new();

    /// <summary>Dequeues a retained-layer handle pending native destruction.
    /// Called by the drawing context once per frame (it owns the render target).</summary>
    internal static bool TryDequeuePendingLayerDestroy(out nint handle) =>
        s_pendingLayerDestroy.TryDequeue(out handle);

    // Wall-clock tick (Environment.TickCount64) of the last time this visual
    // entered RenderDirect with Visibility.Visible. The idle-resource reclaimer
    // uses this together with VisualRenderedObserver to find visuals that have
    // been hidden / clipped out of the viewport / detached from a painted window
    // for long enough that their cached resources can be released.
    // 0 means "never rendered" (still being constructed, or never attached).
    private long _lastRenderedTickMs;

    /// <summary>
    /// Static hook raised at the entry of <see cref="RenderDirect"/> after the
    /// visibility check passes. Stays <see langword="null"/> in default builds
    /// (no overhead beyond a single field-load + null branch on the render hot
    /// path); the idle-resource reclaimer installs a handler when
    /// <c>app.UseIdleResourceReclamation()</c> is called so it can track which
    /// visuals are still being painted each frame.
    /// </summary>
    /// <remarks>
    /// Handlers run on the UI thread, synchronously, on the render hot path.
    /// They MUST be allocation-free and MUST NOT throw, mutate the visual tree,
    /// or call back into rendering.
    /// </remarks>
    internal static Action<Visual>? VisualRenderedObserver;

    /// <summary>
    /// Wall-clock tick (<see cref="Environment.TickCount64"/>) of the last time
    /// this visual rendered with <see cref="Visibility.Visible"/>. Returns 0 if
    /// the visual has never been rendered. Read by the idle-resource reclaimer
    /// to compute how long the visual has been idle.
    /// </summary>
    internal long LastRenderedTickMs => _lastRenderedTickMs;

    /// <summary>
    /// Set to <see langword="true"/> the first time the idle-resource reclaimer
    /// records this visual into its tracked set, so the per-frame
    /// <see cref="VisualRenderedObserver"/> callback can early-return on the
    /// next thousand-plus frames without re-touching the tracking table.
    /// Reset to <see langword="false"/> if the reclaimer is shut down (so a
    /// subsequent <c>UseIdleResourceReclamation</c> call would re-track).
    /// </summary>
    internal bool IsTrackedByIdleReclaimer;

    /// <summary>
    /// Marks this visual as the root of a <c>ControlTemplate</c> instance owned
    /// by its parent <see cref="Visual"/> (typically a <c>Control</c>). When
    /// <see langword="true"/>, <see cref="RenderDirect"/> skips this visual in
    /// the regular children-render loop because the owning control renders it
    /// explicitly through <see cref="RenderTemplatedBackground"/> BEFORE its
    /// own <see cref="OnRender"/>. The visual remains a real visual child for
    /// layout, hit-testing, and visual-tree walks — only the render pass
    /// re-orders so that a self-drawing control's <c>OnRender</c> output is
    /// not painted over by an opaque template background.
    /// </summary>
    internal bool IsTemplatedRoot { get; set; }

    /// <summary>
    /// Releases the retained-mode drawing cache slot, if any, and forces the
    /// next render pass to re-record from <see cref="OnRender"/>. Called by
    /// the idle-resource reclaimer when the visual has been idle long enough
    /// that holding its baked command list is no longer worth the memory.
    /// </summary>
    /// <remarks>
    /// Safe to call from the UI thread at any time outside the render pass.
    /// The cache will be re-populated on the next dirty frame; correctness of
    /// the rendered output is unaffected.
    /// </remarks>
    internal void EvictRetainedDrawingCache()
    {
        // Also release any retained GPU layer — its texture is much larger than
        // the command list and there is no render context here, so defer the
        // native destroy to the next frame via the pending-destroy queue.
        if (_cachedLayer != 0)
        {
            s_pendingLayerDestroy.Enqueue(_cachedLayer);
            _cachedLayer = 0;
            _layerContentDirty = true;
        }

        if (_cachedDrawing == null) return;
        _cachedDrawing = null;
        // Force RenderDirect's record-or-replay branch to re-record next time.
        _isRenderDirty = true;
    }

    /// <summary>
    /// Installs the process-wide retained-mode drawing cache. When non-null,
    /// every visual's <c>OnRender</c> is recorded into an immutable Drawing
    /// on its first dirty frame and replayed verbatim on subsequent clean
    /// frames. A null value preserves the legacy immediate-mode behaviour
    /// where <c>OnRender</c> is invoked each frame.
    /// </summary>
    /// <remarks>
    /// Typically set once at startup by
    /// <c>Jalium.UI.Media.Rendering.MediaRenderCacheHost.Bootstrap()</c>,
    /// which is invoked from <c>RenderTargetDrawingContext</c>'s type
    /// initializer. Users can opt out via the
    /// <c>JALIUM_DISABLE_RENDER_CACHE=1</c> environment variable checked by
    /// that bootstrap.
    /// </remarks>
    public static IRenderCacheHost? RenderCacheHost { get; set; }

    // Retained-mode cache telemetry — cumulative since process start.
    // DevTools subtracts last-frame snapshot to expose per-frame deltas via
    // RenderDiagnostics.RetainedCacheFrameStats. record = had to re-run
    // OnRender (dirty / first time); replay = served from _cachedDrawing
    // (the win); bypass = OnRender ran without caching at all (no host /
    // opted out / non-cacheable DC). When `record` dominates, the visual
    // tree is being marked dirty every frame — the win is in finding the
    // invalidation source, not in the cache itself.
    private static long s_retainedCacheRecords;
    private static long s_retainedCacheReplays;
    private static long s_retainedCacheBypasses;
    public static long RetainedCacheRecordsTotal  => System.Threading.Volatile.Read(ref s_retainedCacheRecords);
    public static long RetainedCacheReplaysTotal  => System.Threading.Volatile.Read(ref s_retainedCacheReplays);
    public static long RetainedCacheBypassesTotal => System.Threading.Volatile.Read(ref s_retainedCacheBypasses);

    /// <summary>
    /// Whether this Visual participates in the retained-mode drawing cache.
    /// </summary>
    /// <remarks>
    /// Default is <c>true</c> — <c>OnRender</c> is recorded on first dirty
    /// frame and replayed on subsequent clean frames.
    /// <para/>
    /// Override to <c>false</c> when <c>OnRender</c> is a pure delegator to
    /// external state (e.g. <c>TextBoxContentHost</c> whose <c>OnRender</c>
    /// forwards to its owner's <c>RenderTextContent</c>). Such visuals own
    /// no local rendering state, so <c>_isRenderDirty</c> cannot correctly
    /// track dirtiness: the owner's state can change without this visual's
    /// own cache ever being invalidated, and the cache would replay stale
    /// commands forever. Opting out forces immediate-mode per frame, which
    /// is correct for pure proxies.
    /// </remarks>
    protected virtual bool ParticipatesInRenderCache => true;
    // When true, this visual + all descendants are hidden from Diagnostics
    // (Layout / Binding / RoutedEvent recording). Set once for DevTools roots;
    // inherited by children through AddVisualChild so we don't need to walk
    // the VisualParent chain on every notification — a critical perf win and
    // also avoids false-negatives when the parent chain is momentarily broken
    // (e.g. VSP recycling, mid-attach Measure calls).
    //
    // Field initializer reads the thread-local creation scope so a Visual
    // constructed inside DiagnosticsScope.BeginIgnoredCreation() is flagged
    // immediately — this closes the constructor-time invalidation hole
    // (DP defaults / Header / Foreground sets fire InvalidateMeasure before
    // AddVisualChild ever runs).
    private bool _isDiagnosticsIgnored = Diagnostics.DiagnosticsScope.IsInIgnoredCreationScope;
    [ThreadStatic]
    private static HashSet<Visual>? _renderPath;
    [ThreadStatic]
    private static int _renderDepth;
    private const int MaxRenderDepth = 1024;

    /// <summary>
    /// Gets the parent visual.
    /// </summary>
    public Visual? VisualParent => _parent;

    /// <summary>
    /// Gets the number of child visuals.
    /// </summary>
    public virtual int VisualChildrenCount => _children.Count;

    /// <summary>
    /// Gets a child visual by index.
    /// </summary>
    /// <param name="index">The index of the child.</param>
    /// <returns>The child visual.</returns>
    public virtual Visual? GetVisualChild(int index)
    {
        if (index < 0 || index >= _children.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _children[index];
    }

    /// <summary>
    /// Adds a child visual.
    /// </summary>
    /// <param name="child">The child to add.</param>
    protected void AddVisualChild(Visual child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (ReferenceEquals(child, this))
        {
            throw new InvalidOperationException($"Visual cannot be added as its own child. visual={GetType().Name}");
        }

        for (var ancestor = this; ancestor != null; ancestor = ancestor._parent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new InvalidOperationException(
                    $"Adding child would create a visual cycle. child={child.GetType().Name}, parent={GetType().Name}");
            }
        }

        if (ReferenceEquals(child._parent, this))
        {
            // Idempotent fast-path: re-adding the same child to the same parent
            // should be a no-op. Happens during own-container realization when
            // multiple pipelines (ItemsControl populate + VSP realize) converge
            // on the same container within one layout pass.
            if (!_children.Contains(child)) _children.Add(child);
            return;
        }

        if (child._parent != null)
        {
            throw new InvalidOperationException($"Visual already has a parent. child={child.GetType().Name}, parent={child._parent.GetType().Name}, attempted new parent={this.GetType().Name}");
        }

        var oldParent = child._parent;
        child._parent = this;
        _children.Add(child);

        // Propagate diagnostics-ignored flag down. Doing this at attach time
        // (not at ShouldIgnore query time) means the check is O(1) later,
        // and can't race with mid-attach Measure calls.
        if (_isDiagnosticsIgnored && !child._isDiagnosticsIgnored)
            child.MarkDiagnosticsIgnoredSubtree();

        OnVisualChildrenChanged(child, null);
        child.OnVisualParentChanged(oldParent);
    }

    /// <summary>
    /// True when this visual (or a DevTools-style ancestor) has been marked
    /// with <see cref="MarkDiagnosticsIgnoredSubtree"/>. Diagnostics layers
    /// use this for an O(1) "is DevTools" check.
    /// </summary>
    public bool IsDiagnosticsIgnored => _isDiagnosticsIgnored;

    /// <summary>
    /// Flag this visual + all current descendants so that Diagnostics hooks
    /// skip them. New descendants added later inherit the flag via
    /// <see cref="AddVisualChild"/>.
    /// </summary>
    public void MarkDiagnosticsIgnoredSubtree()
    {
        if (_isDiagnosticsIgnored) return;
        _isDiagnosticsIgnored = true;
        for (int i = 0; i < _children.Count; i++)
            _children[i].MarkDiagnosticsIgnoredSubtree();
    }

    /// <summary>
    /// Detaches this visual from its current parent, if any.
    /// Used by Popup to move a child into the PopupRoot overlay tree.
    /// </summary>
    internal void DetachFromVisualParent()
    {
        if (_parent != null)
        {
            _parent.RemoveVisualChild(this);
        }
    }

    /// <summary>
    /// Removes a child visual.
    /// </summary>
    /// <param name="child">The child to remove.</param>
    protected void RemoveVisualChild(Visual child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (child._parent != this)
        {
            return;
        }

        var oldParent = child._parent;
        child._parent = null;
        _children.Remove(child);

        OnVisualChildrenChanged(null, child);
        child.OnVisualParentChanged(oldParent);
    }

    /// <summary>
    /// Called when the visual parent changes.
    /// </summary>
    /// <param name="oldParent">The previous parent visual, or null.</param>
    protected virtual void OnVisualParentChanged(Visual? oldParent)
    {
    }

    /// <summary>
    /// Internal method for VisualCollection to add a child.
    /// Calls AddVisualChild which is protected.
    /// </summary>
    internal void InternalAddVisualChild(Visual child) => AddVisualChild(child);

    /// <summary>
    /// Internal method for VisualCollection to remove a child.
    /// Calls RemoveVisualChild which is protected.
    /// </summary>
    internal void InternalRemoveVisualChild(Visual child) => RemoveVisualChild(child);

    /// <summary>
    /// Called when visual children change.
    /// </summary>
    /// <param name="visualAdded">The child that was added, if any.</param>
    /// <param name="visualRemoved">The child that was removed, if any.</param>
    protected virtual void OnVisualChildrenChanged(Visual? visualAdded, Visual? visualRemoved)
    {
    }

    /// <summary>
    /// Gets whether this element needs re-rendering.
    /// </summary>
    internal bool IsRenderDirty => _isRenderDirty;

    /// <summary>
    /// Gets whether this element or any descendant needs re-rendering (CONTENT
    /// dirtiness — set via <see cref="SetRenderDirty"/>). Composition-only changes
    /// (Opacity / RenderTransform) do NOT set this; see
    /// <see cref="IsSubtreeCompositionDirty"/>.
    /// </summary>
    internal bool IsSubtreeDirty => _isSubtreeDirty;

    /// <summary>
    /// Gets whether this element or any descendant has a pending composition-only
    /// change (Opacity / RenderTransform / RenderTransformOrigin) with no content
    /// change. Propagated by <see cref="MarkSubtreeDirtyForComposition"/>.
    /// </summary>
    internal bool IsSubtreeCompositionDirty => _isSubtreeCompositionDirty;

    /// <summary>
    /// Marks this element as needing re-rendering and propagates subtree dirty flag up.
    /// </summary>
    internal void SetRenderDirty()
    {
        _isRenderDirty = true;
        // Content changed → any cached GPU layer for this visual is stale and must
        // be re-realized (the texture is reused; only its pixels are re-rendered).
        _layerContentDirty = true;
        MarkSubtreeDirtyUp();
    }

    /// <summary>
    /// Propagates the subtree-dirty flag up without invalidating this visual's own
    /// cached drawing. Used by composition-only invalidations (Opacity, RenderTransform,
    /// RenderTransformOrigin animations) where the parent re-traverses the child loop
    /// every frame and reads the live property values via PushOpacity / PushTransform,
    /// so the child's recorded command list remains correct. Skipping the render-dirty
    /// flip avoids needlessly re-recording OnRender for the animated element.
    /// </summary>
    internal void MarkSubtreeDirtyForComposition()
    {
        MarkSubtreeCompositionDirtyUp();
    }

    /// <summary>
    /// Propagates the CONTENT subtree dirty flag to all ancestors.
    /// </summary>
    private void MarkSubtreeDirtyUp()
    {
        var current = this;
        while (current != null)
        {
            if (current._isSubtreeDirty)
            {
                break; // Already marked, ancestors also marked
            }
            current._isSubtreeDirty = true;
            current = current._parent;
        }
    }

    /// <summary>
    /// Propagates the COMPOSITION-ONLY subtree dirty flag to all ancestors without
    /// touching the content flag, so a clean-content subtree under a composited
    /// animation stays layer-eligible (its cached GPU layer is re-composited at the
    /// new transform/opacity rather than re-recorded). Short-circuits once an
    /// ancestor is already marked, exactly like <see cref="MarkSubtreeDirtyUp"/>.
    /// </summary>
    private void MarkSubtreeCompositionDirtyUp()
    {
        var current = this;
        while (current != null)
        {
            if (current._isSubtreeCompositionDirty)
            {
                break;
            }
            current._isSubtreeCompositionDirty = true;
            current = current._parent;
        }
    }

    /// <summary>
    /// Clears dirty flags after rendering.
    /// </summary>
    internal void ClearRenderDirty()
    {
        _isRenderDirty = false;
        _isSubtreeDirty = false;
        _isSubtreeCompositionDirty = false;
    }

    /// <summary>
    /// Performs hit testing at the specified point.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <returns>The hit test result, or null if nothing was hit.</returns>
    protected virtual HitTestResult? HitTestCore(Point point)
    {
        return null;
    }

    /// <summary>
    /// Performs rendering using the specified drawing context.
    /// Renders this element and all visible children.
    /// </summary>
    /// <param name="drawingContext">The drawing context.</param>
    public void Render(DrawingContext drawingContext)
    {
        _renderPath ??= new HashSet<Visual>();

        if (_renderDepth > MaxRenderDepth)
        {
            return;
        }

        if (!_renderPath.Add(this))
        {
            return;
        }

        _renderDepth++;
        try
        {
            RenderDirect(drawingContext);
        }
        finally
        {
            _renderDepth--;
            _renderPath.Remove(this);
            if (_renderDepth == 0 && _renderPath.Count > 0)
            {
                _renderPath.Clear();
            }
        }
    }

    private void RenderDirect(DrawingContext drawingContext)
    {
        // Respect UIElement visibility at render entry.
        // Hidden and Collapsed should not render.
        if (this is UIElement thisElementVisibility &&
            thisElementVisibility.Visibility != Visibility.Visible)
        {
            _isRenderDirty = false;
            _isSubtreeDirty = false;
            _isSubtreeCompositionDirty = false;
            return;
        }

        // Stamp the "last rendered" tick so the idle-resource reclaimer can
        // tell how long this visual has been off-screen. Updated AFTER the
        // visibility gate so Hidden/Collapsed visuals correctly look idle, and
        // BEFORE we recurse into children so the parent counts as rendered the
        // moment its own subtree starts. Viewport-clipped children naturally
        // never reach this line because ShouldRenderChild short-circuits the
        // child.Render() call entirely.
        _lastRenderedTickMs = Environment.TickCount64;
        var renderedObserver = VisualRenderedObserver;
        if (renderedObserver != null)
        {
            renderedObserver(this);
        }

        // Check for element effect (BlurEffect, DropShadowEffect, etc.)
        // If present, capture all rendering to an offscreen bitmap so the effect can be applied.
        IEffect? activeEffect = null;
        IEffectDrawingContext? effectDc = null;
        float captureX = 0, captureY = 0, captureW = 0, captureH = 0;

        if (this is UIElement effectElement &&
            effectElement.Effect is IEffect eff &&
            eff.HasEffect &&
            drawingContext is IEffectDrawingContext edc &&
            drawingContext is IOffsetDrawingContext offsetDc)
        {
            activeEffect = eff;
            effectDc = edc;

            var padding = eff.EffectPadding;
            var size = effectElement.RenderSize;
            var left = offsetDc.Offset.X - padding.Left;
            var top = offsetDc.Offset.Y - padding.Top;
            var right = offsetDc.Offset.X + size.Width + padding.Right;
            var bottom = offsetDc.Offset.Y + size.Height + padding.Bottom;

            // Pixel-snap the offscreen capture bounds to avoid sub-pixel resampling jitter
            // when effect parameters (e.g. Blur radius / Shadow depth) change continuously.
            var snappedLeft = Math.Floor(left);
            var snappedTop = Math.Floor(top);
            var snappedRight = Math.Ceiling(right);
            var snappedBottom = Math.Ceiling(bottom);
            captureX = (float)snappedLeft;
            captureY = (float)snappedTop;
            captureW = (float)Math.Max(0.0, snappedRight - snappedLeft);
            captureH = (float)Math.Max(0.0, snappedBottom - snappedTop);

            effectDc.BeginEffectCapture(captureX, captureY, captureW, captureH);
        }

        // Push clip BEFORE OnRender so the element's own drawing is also clipped
        // (matches WPF semantics: ClipToBounds clips the element itself + children).
        Geometry? clipGeometry = null;
        if (this is UIElement thisElement)
        {
            clipGeometry = thisElement.GetLayoutClip();
        }

        bool pushedClip = false;
        if (clipGeometry != null && drawingContext is IClipDrawingContext clipContext)
        {
            clipContext.PushClip(clipGeometry);
            pushedClip = true;
        }

        // Templated-background layer. Painted BEFORE OnRender so that a
        // self-drawing control (e.g. ChartBase, custom visualisations) whose
        // ControlTemplate has an opaque Background is not obscured by its
        // own template root. The template root is rendered explicitly here
        // and skipped in the regular children loop below (gated on
        // child.IsTemplatedRoot). For visuals without a templated root this
        // is a no-op virtual call. The live drawing context is used directly
        // — the template's own cacheable visuals recurse through Render()
        // and pick up the retained-mode cache themselves.
        RenderTemplatedBackground(drawingContext);

        // Retained-mode cache path. When a render cache host is installed
        // AND the live drawing context opts in via ICacheableDrawingContext,
        // OnRender is captured into an immutable command list the first time
        // the visual becomes dirty and replayed on subsequent clean frames.
        // The cached handle survives across frames; SetRenderDirty simply
        // flips _isRenderDirty, and RenderDirect re-records when it notices.
        //
        // The marker gate exists because OnRender accepts any DrawingContext
        // subclass — user code (typically tests) may pattern-match the
        // argument for context-specific probing. Substituting a recorder for
        // such a context would break the match. Only contexts that advertise
        // themselves as cache-safe participate in caching.
        //
        // Invariants preserved against the legacy path:
        //  - OnRender still sees a DrawingContext that honours IOffsetDrawingContext
        //    and IClipBoundsDrawingContext for ambient-state reads.
        //  - Commands arrive at drawingContext in the same order and with the
        //    same arguments as the legacy direct-dispatch path.
        //  - Any push (transform / clip / opacity / effect) recorded during
        //    OnRender has its matching pop recorded too, so drawingContext's
        //    state stacks remain balanced post-replay.
        var cacheHost = RenderCacheHost;
        if (cacheHost != null && ParticipatesInRenderCache && drawingContext is ICacheableDrawingContext)
        {
            if (_isRenderDirty || _cachedDrawing == null)
            {
                var recorder = cacheHost.CreateRecorder(drawingContext);
                OnRender(recorder);
                _cachedDrawing = cacheHost.FinishRecord(recorder);
                System.Threading.Interlocked.Increment(ref s_retainedCacheRecords);
            }
            else
            {
                System.Threading.Interlocked.Increment(ref s_retainedCacheReplays);
            }
            cacheHost.Replay(_cachedDrawing!, drawingContext);
        }
        else
        {
            OnRender(drawingContext);
            System.Threading.Interlocked.Increment(ref s_retainedCacheBypasses);
        }

        var childCount = VisualChildrenCount;
        for (int i = 0; i < childCount && i < VisualChildrenCount; i++)
        {
            var child = GetVisualChild(i);
            if (child == null) continue;

            // Skip the ControlTemplate root — it was already painted as the
            // background layer by RenderTemplatedBackground above. Rendering
            // it again here would double-paint, reintroducing the very
            // overlay-obscures-OnRender bug this split is meant to fix.
            if (child.IsTemplatedRoot)
            {
                continue;
            }

            RenderChildVisualInline(drawingContext, child);
        }

        // Pop child clip BEFORE OnPostRender so OnPostRender executes outside the
        // layout clip. This matters for Border: its stroke is rendered in
        // OnPostRender on the *outer* rounded-rect (centred on the stroke ring),
        // but the layout clip from GetLayoutClip uses the *inner* rounded-rect
        // shape (matching the element's visible Background, so that child
        // content is clipped to the visible area instead of bleeding past the
        // stroke). If the stroke draw ran inside that inner clip, the outer
        // half of the stroke pen would be sliced off and the stroke would visibly
        // shrink. OnPostRender after the pop sidesteps that — children still
        // get the inner clip; the stroke renders unconstrained on the outer
        // shape; the result is a clean visible boundary where nothing leaks
        // past the stroke into the BorderThickness ring.
        if (pushedClip && drawingContext is IClipDrawingContext clipContext2)
        {
            clipContext2.Pop();
            pushedClip = false;
        }

        OnPostRender(drawingContext);

        if (activeEffect != null && effectDc != null)
        {
            effectDc.EndEffectCapture();
            var elemOffset = (drawingContext is IOffsetDrawingContext odc2) ? odc2.Offset : new Point(captureX, captureY);
            var elemSize = (this is UIElement ue) ? ue.RenderSize : new Size(captureW, captureH);

            // Corner radii for the element content clip (shadows render outside, unclipped).
            var cr = (this is UIElement cornerElem) ? GetCornerRadius(cornerElem) : new CornerRadius(0);
            float maxR = (float)Math.Max(Math.Max(cr.TopLeft, cr.TopRight),
                                         Math.Max(cr.BottomRight, cr.BottomLeft));

            effectDc.ApplyElementEffect(activeEffect,
                (float)elemOffset.X, (float)elemOffset.Y,
                (float)elemSize.Width, (float)elemSize.Height,
                captureX, captureY,
                (float)cr.TopLeft, (float)cr.TopRight,
                (float)cr.BottomRight, (float)cr.BottomLeft);
        }
        // Clearing this visual's own dirty flags here is correct for the
        // damage-driven gate (Phase 4). The child loop above is UNCONDITIONAL
        // with respect to dirty state: every non-template-root child is handed
        // to RenderChildVisualInline — content-dirty / subtree-dirty does NOT
        // gate the walk (there is no IsSubtreeDirty-gated skip anywhere in it).
        // RenderChildVisualInline skips a child only for the reasons below,
        // none of which loses pending work when this visual resets its OWN
        // aggregate flags:
        //   - Visibility != Visible: an invisible child legitimately renders
        //     nothing this frame.
        //   - clip-bounds culling (ShouldRenderChild): the child's bounds miss
        //     the context's CurrentClipBounds (viewport / damage region). This
        //     is ORTHOGONAL to dirty state — a content-dirty child can be
        //     culled — but a culled child is never walked, so its OWN flags are
        //     left intact and its pending work rides on the child itself, not
        //     on this visual's aggregate flags that we clear here.
        //   - the retained-layer composite fast path (TryCompositeChildLayer),
        //     which is taken only for a content-clean child (its
        //     !_isRenderDirty && !_isSubtreeDirty gate) — so a composited child
        //     genuinely has nothing pending either.
        // _isSubtreeDirty is consumed only by that composite gate, and any
        // later content change re-runs SetRenderDirty -> MarkSubtreeDirtyUp,
        // re-propagating it (and _layerContentDirty) back up through this
        // visual; so resetting the baseline here cannot strand future work. A
        // composition-dirty child is likewise re-marked every frame the
        // animation actually moves (Part A gates invalidation on real value
        // change), so clearing the composition flag here cannot strand a
        // pending composite.
        _isRenderDirty = false;
        _isSubtreeDirty = false;
        _isSubtreeCompositionDirty = false;
    }

    /// <summary>
    /// Extracts the CornerRadius from an element by looking for the CLR property.
    /// Returns zero radii if the element doesn't have one.
    /// </summary>
    private static CornerRadius GetCornerRadius(UIElement element)
    {
        // AOT-safe DependencyProperty lookup via the registry (no reflection).
        var dp = DependencyProperty.FromName(element.GetType(), "CornerRadius");
        if (dp != null && element.GetValue(dp) is CornerRadius cr)
            return cr;
        return new CornerRadius(0);
    }

    private static bool ShouldRenderChild(DrawingContext drawingContext, UIElement child, Point childOffset)
    {
        if (drawingContext is not IClipBoundsDrawingContext { CurrentClipBounds: Rect clipBounds })
        {
            return true;
        }

        // CurrentClipBounds is expressed in the final drawing surface space. Use the
        // child's render-space AABB as well: testing the static layout box here culls a
        // translated/rotated/scaled child as soon as an animation moves it away from its
        // original slot. The dirty region then gets cleared but the Path subtree is never
        // submitted, which makes vector content blink between swap-chain buffers.
        Rect childBounds;
        if (child.Effect is IEffect effect && effect.HasEffect)
        {
            var padding = effect.EffectPadding;
            var size = child.RenderSize;
            childBounds = child.MapLocalRectToScreen(new Rect(
                -padding.Left,
                -padding.Top,
                size.Width + padding.Left + padding.Right,
                size.Height + padding.Top + padding.Bottom));
        }
        else
        {
            childBounds = child.GetRenderBounds();
        }

        // Render() is also used by detached/offscreen callers that seed a non-zero
        // DrawingContext.Offset. GetRenderBounds is rooted in the visual tree, so retain
        // the caller's ambient offset by translating from the tree-space layout origin to
        // the live childOffset. In the normal Window path this delta is exactly zero.
        var treeSpaceOrigin = child.GetScreenBounds();
        double offsetDeltaX = childOffset.X - treeSpaceOrigin.X;
        double offsetDeltaY = childOffset.Y - treeSpaceOrigin.Y;
        if (offsetDeltaX != 0 || offsetDeltaY != 0)
        {
            childBounds = new Rect(
                childBounds.X + offsetDeltaX,
                childBounds.Y + offsetDeltaY,
                childBounds.Width,
                childBounds.Height);
        }

        return clipBounds.IntersectsWith(childBounds);
    }

    /// <summary>
    /// Override to render content after children (useful for overlays like scrollbars).
    /// </summary>
    /// <param name="drawingContext">The drawing context.</param>
    protected virtual void OnPostRender(DrawingContext drawingContext)
    {
    }

    /// <summary>
    /// Returns true if <paramref name="transform"/> carries rotation or skew (off-
    /// diagonal terms). The first-cut layer composite (via the bitmap quad path)
    /// bakes only scale+translate, so rotated/skewed subtrees fall back.
    /// </summary>
    private static bool TransformHasRotationOrSkew(Transform transform)
    {
        var m = transform.Value;
        return Math.Abs(m.M12) > 1e-6 || Math.Abs(m.M21) > 1e-6;
    }

    /// <summary>
    /// Returns true when compositing a retained layer would enlarge its source
    /// texels. Retained layers are realized at the element's untransformed
    /// <see cref="UIElement.RenderSize"/>; upscaling that texture turns vector
    /// paths and text into a blurry bitmap. Such subtrees must be rendered
    /// through the live transform so vector geometry is rasterized directly at
    /// the final surface resolution.
    /// </summary>
    internal static bool TransformWouldUpscaleRetainedLayer(Transform? transform)
    {
        if (transform == null)
            return false;

        var m = transform.Value;
        var scaleX = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
        var scaleY = Math.Sqrt(m.M21 * m.M21 + m.M22 * m.M22);
        const double epsilon = 1e-6;
        return scaleX > 1.0 + epsilon || scaleY > 1.0 + epsilon;
    }

    /// <summary>Queues this visual's retained layer (if any) for fence-gated
    /// destruction and marks it for re-realization.</summary>
    private static void ReleaseLayerIfAny(Visual v)
    {
        if (v._cachedLayer != 0)
        {
            s_pendingLayerDestroy.Enqueue(v._cachedLayer);
            v._cachedLayer = 0;
            v._layerContentDirty = true;
        }
    }

    /// <summary>
    /// Releases the retained GPU layers of <paramref name="root"/>'s entire
    /// subtree into the pending-destroy queue. Called by the window's
    /// device-lost recovery BEFORE the failed render target is disposed: layer
    /// textures live on the failed device, so every cached handle must be
    /// destroyed through the OLD target (whose native graveyard is still
    /// alive) and re-realized from scratch on the new device. Leaving them
    /// cached would composite stale-device textures into the new device's
    /// first frame — the same driver AV the recovery is escaping from.
    /// </summary>
    internal static void ReleaseRetainedLayersRecursive(Visual root)
    {
        ReleaseLayerIfAny(root);
        int count = root.VisualChildrenCount;
        for (int i = 0; i < count; i++)
        {
            if (root.GetVisualChild(i) is Visual child)
                ReleaseRetainedLayersRecursive(child);
        }
    }

    /// <summary>
    /// Attempts the retained-GPU-layer fast path for <paramref name="child"/>:
    /// realize its content-clean subtree into a persistent texture once, then
    /// composite that texture with the live opacity/transform instead of
    /// re-walking + re-emitting the subtree. Returns true if the child was
    /// composited via a layer (caller must NOT render it normally); false to fall
    /// back to the regular push+recurse path.
    /// </summary>
    private bool TryCompositeChildLayer(DrawingContext drawingContext, UIElement child, Point childOffset)
    {
        if (drawingContext is not ILayerCompositingDrawingContext layerCtx || !layerCtx.SupportsRetainedLayers)
            return false;
        if (drawingContext is not IOffsetDrawingContext offsetContext)
            return false;

        // Effects use the offscreen-capture machinery the layer path also uses, and
        // non-cacheable visuals opt out of retained-mode entirely.
        if (!child.ParticipatesInRenderCache || (child.Effect is IEffect ce && ce.HasEffect))
        {
            ReleaseLayerIfAny(child);
            return false;
        }

        // CONTENT dirty (this visual or any descendant) → must re-record this
        // frame; keep the layer but mark it for re-realization once clean.
        if (child._isRenderDirty || child._isSubtreeDirty)
        {
            child._layerContentDirty = true;
            return false;
        }

        var transform = child.RenderTransform;
        double opacity = child.Opacity;

        // Only worth a layer when there is a composited animation to apply cheaply
        // (a live transform or sub-1 opacity). First cut excludes rotation/skew
        // (the quad path bakes only scale+translate) and trivial / leaf subtrees.
        bool hasComposite = transform != null || opacity < 1.0;
        bool rotation = transform != null && TransformHasRotationOrSkew(transform);
        bool upscalesLayer = TransformWouldUpscaleRetainedLayer(transform);
        var size = child.RenderSize;
        bool eligible = hasComposite && !rotation && !upscalesLayer
            && size.Width >= 1.0 && size.Height >= 1.0
            && child.VisualChildrenCount > 0;

        if (!eligible)
        {
            ReleaseLayerIfAny(child);
            return false;
        }

        // 子树（任意后代）含 effect → 必须退出 retained 模式。:786 已对 child 自身的 Effect
        // 做了同样的排除，但漏了后代：retained-layer capture 会渲染整个子树，子树里 effect
        // 元素的 BeginEffectCapture→BeginOffscreenCapture 会因 inRetainedCapture_=true 被
        // guard 拒绝（offscreen capture 不能嵌套 retained capture，EndOffscreenCapture 会把
        // RT 恢复成 swap-chain 而非 layer），导致 glow/阴影/backdrop 等 effect 静默消失。
        // resize 把带动画的容器翻上 layer 路径正是触发点（独显才有 retained-layer 优化）。
        if (SubtreeHasEffect(child))
        {
            ReleaseLayerIfAny(child);
            return false;
        }

        var worldBounds = new Rect(childOffset.X, childOffset.Y, size.Width, size.Height);

        nint layer = child._cachedLayer;
        if (layer == 0 || child._layerContentDirty)
        {
            nint realized = layerCtx.BeginLayerCapture(layer, worldBounds);
            if (realized == 0)
            {
                // A refused handle must not stay cached. After device-lost
                // recovery, a stale-device layer that escaped the recovery
                // sweep (subtree detached at recovery time, reattached later)
                // is refused by the native generation guard FOREVER — keeping
                // it would leak the native wrapper and pin the removed device
                // in memory. Transient refusals (nested capture, ancestor
                // state) only lose a texture that re-realizes next frame.
                if (layer != 0)
                    ReleaseLayerIfAny(child);
                return false; // ancestor transform/opacity, nested capture, or unsupported → fall back
            }

            var savedOffset = offsetContext.Offset;
            offsetContext.Offset = childOffset;
            child.Render(drawingContext); // CONTENT only — no transform/opacity pushed
            offsetContext.Offset = savedOffset;

            layerCtx.EndLayerCapture(realized);
            child._cachedLayer = realized;
            child._layerContentDirty = false;
            layer = realized;
        }

        // Composite the cached layer with the live transform + opacity at the
        // child's exact z-order slot. Offset = childOffset so the transform is
        // composed around (childOffset + origin) in screen space, matching the
        // normal child path.
        var saved = offsetContext.Offset;
        offsetContext.Offset = childOffset;
        double originX = 0, originY = 0;
        if (transform != null)
        {
            var origin = child.RenderTransformOrigin;
            originX = origin.X * size.Width;
            originY = origin.Y * size.Height;
        }
        layerCtx.CompositeLayer(layer, worldBounds, opacity, transform, originX, originY);
        offsetContext.Offset = saved;

        // The composite path skips child.Render, so stamp idle-tracking directly
        // (and notify the reclaimer observer) — otherwise a child composited every
        // frame would look "idle" and get its layer/cache evicted mid-animation.
        child._lastRenderedTickMs = Environment.TickCount64;
        VisualRenderedObserver?.Invoke(child);
        return true;
    }

    // 元素或任意后代是否带活动 effect。effect 通过 offscreen capture 渲染，而 offscreen
    // capture 不能嵌套进 retained-layer capture（见 TryCompositeChildLayer 的说明），故含
    // effect 的子树不可作为 retained layer 合成，否则 effect 会静默失效。只在已判定 eligible
    // 的动画容器上调用，递归开销可控。
    private static bool SubtreeHasEffect(Visual visual)
    {
        if (visual is UIElement ue && ue.Effect is IEffect e && e.HasEffect)
            return true;
        int n = visual.VisualChildrenCount;
        for (int i = 0; i < n; i++)
        {
            var c = visual.GetVisualChild(i);
            if (c != null && SubtreeHasEffect(c))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Renders a single child visual inline, applying the same offset, clip,
    /// opacity and render-transform handling that <see cref="RenderDirect"/>
    /// uses for the normal children loop.
    /// </summary>
    /// <remarks>
    /// Extracted so that <see cref="RenderTemplatedBackground"/> overrides
    /// (most importantly <c>Control.RenderTemplatedBackground</c>) can paint
    /// the template root with identical ambient state — same offset push,
    /// same viewport-cull, same opacity / transform stack — as the regular
    /// child loop. Without that, a template root painted as a background
    /// would skip viewport culling or render-transform application and drift
    /// from how a normal child of the same control would behave.
    /// </remarks>
    /// <param name="drawingContext">The live drawing context.</param>
    /// <param name="child">The child visual to render.</param>
    /// <returns>
    /// <see langword="true"/> if the child was rendered; <see langword="false"/>
    /// if it was culled (invisible, viewport-clipped, etc.).
    /// </returns>
    internal bool RenderChildVisualInline(DrawingContext drawingContext, Visual child)
    {
        if (child is UIElement uiVisibility && uiVisibility.Visibility != Visibility.Visible)
        {
            return false;
        }

        if (child is UIElement uiChild && drawingContext is IOffsetDrawingContext offsetContext)
        {
            var bounds = uiChild.VisualBounds;
            var savedOffset = offsetContext.Offset;
            var ro = uiChild.RenderOffset;
            var childOffset = new Point(
                savedOffset.X + bounds.X + ro.X,
                savedOffset.Y + bounds.Y + ro.Y);

            if (!ShouldRenderChild(drawingContext, uiChild, childOffset))
            {
                return false;
            }

            // Damage-driven composited-animation fast path: if this child's CONTENT
            // is clean and it is animating Opacity/RenderTransform, composite its
            // cached GPU layer (one transformed quad) instead of re-walking and
            // re-emitting the whole subtree. Transparently falls back when not
            // eligible or unsupported by the backend.
            if (TryCompositeChildLayer(drawingContext, uiChild, childOffset))
            {
                return true;
            }

            offsetContext.Offset = new Point(
                childOffset.X,
                childOffset.Y);

            var childOpacity = uiChild.Opacity;
            var pushedOpacity = false;
            if (childOpacity < 1.0 && drawingContext is IOpacityDrawingContext opacityContext)
            {
                opacityContext.PushOpacity(childOpacity);
                pushedOpacity = true;
            }

            // Apply the child's RenderTransform around its RenderTransformOrigin so that
            // transforms declared on elements (e.g. ScaleTransform for zoom, RotateTransform
            // for animations) actually affect the rendered subtree. Without this the
            // RenderTransform DP would be a no-op during live drawing, matching the
            // behavior already implemented in RenderTargetBitmap for offscreen capture.
            var pushedTransform = false;
            var childRenderTransform = uiChild.RenderTransform;
            if (childRenderTransform != null && drawingContext is ITransformDrawingContext transformContext)
            {
                var origin = uiChild.RenderTransformOrigin;
                var size = uiChild.RenderSize;
                var originX = origin.X * size.Width;
                var originY = origin.Y * size.Height;
                transformContext.PushTransform(childRenderTransform, originX, originY);
                pushedTransform = true;
            }

            child.Render(drawingContext);

            if (pushedTransform && drawingContext is ITransformDrawingContext transformContextPop)
            {
                transformContextPop.PopTransform();
            }

            if (pushedOpacity && drawingContext is IOpacityDrawingContext opacityContext2)
            {
                opacityContext2.PopOpacity();
            }

            offsetContext.Offset = savedOffset;
            return true;
        }

        child.Render(drawingContext);
        return true;
    }

    /// <summary>
    /// Hook for rendering a control template's root visual as a BACKGROUND
    /// layer, BEFORE this visual's own <see cref="OnRender"/> runs.
    /// </summary>
    /// <remarks>
    /// Templated controls render in two layers:
    /// <list type="number">
    /// <item>
    /// The <c>ControlTemplate</c> root (background / border / corner-radius
    /// decoration) is painted first by this method.
    /// </item>
    /// <item>
    /// The control's own <see cref="OnRender"/> then paints on top of that
    /// background — this matters for self-drawing controls (charts, diagrams,
    /// custom visualisations) that have an opaque template background. With
    /// the default WPF-style order (OnRender first, then children), the
    /// template root would be painted on top of OnRender and obscure its
    /// output entirely. This hook inverts that ordering for template roots
    /// specifically without affecting normal visual children.
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="drawingContext">The drawing context.</param>
    protected virtual void RenderTemplatedBackground(DrawingContext drawingContext)
    {
    }

    /// <summary>
    /// Override to provide custom rendering.
    /// </summary>
    /// <param name="drawingContext">The drawing context.</param>
    protected virtual void OnRender(DrawingContext drawingContext)
    {
    }

    /// <summary>
    /// Returns a transform that can be used to transform coordinates from this Visual to the specified Visual.
    /// </summary>
    /// <param name="visual">The Visual to transform coordinates to.</param>
    /// <returns>A GeneralTransform that can be used to transform coordinates.</returns>
    public GeneralTransform? TransformToVisual(Visual? visual)
    {
        if (visual == null)
        {
            // Transform to root coordinates
            return GetTransformToRoot();
        }

        // Get transforms from both visuals to the root
        var thisToRoot = GetTransformToRoot();
        var targetToRoot = visual.GetTransformToRoot();

        if (thisToRoot == null || targetToRoot == null)
            return null;

        // Combine: this -> root -> target (using inverse of target -> root)
        var targetInverse = targetToRoot.Inverse;
        if (targetInverse == null)
            return thisToRoot;

        var group = new GeneralTransformGroup();
        group.Children.Add(thisToRoot);
        group.Children.Add(targetInverse);
        return group;
    }

    /// <summary>
    /// Gets the transform from this visual to the root of the visual tree.
    /// </summary>
    private GeneralTransform? GetTransformToRoot()
    {
        var offset = Point.Zero;
        Visual? current = this;

        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                var bounds = uiElement.VisualBounds;
                offset = new Point(offset.X + bounds.X, offset.Y + bounds.Y);
            }
            current = current.VisualParent;
        }

        return new TranslateTransform2D(offset.X, offset.Y);
    }
}

/// <summary>
/// Result of a hit test operation.
/// </summary>
public class HitTestResult
{
    // Reusable instance to avoid allocations on every mouse move
    [ThreadStatic]
    private static HitTestResult? _reusable;

    /// <summary>
    /// Gets the visual that was hit.
    /// </summary>
    public Visual VisualHit { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HitTestResult"/> class.
    /// </summary>
    /// <param name="visualHit">The visual that was hit.</param>
    public HitTestResult(Visual visualHit)
    {
        VisualHit = visualHit;
    }

    /// <summary>
    /// Gets a reusable HitTestResult instance to avoid allocations.
    /// </summary>
    /// <param name="visualHit">The visual that was hit.</param>
    /// <returns>A HitTestResult instance.</returns>
    internal static HitTestResult GetReusable(Visual visualHit)
    {
        _reusable ??= new HitTestResult(visualHit);
        _reusable.VisualHit = visualHit;
        return _reusable;
    }
}
