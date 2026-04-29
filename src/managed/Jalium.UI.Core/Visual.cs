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

    // Retained-mode drawing cache. When RenderCacheHost is installed, the
    // render loop records OnRender output into an opaque Drawing handle on
    // the first dirty frame and replays it from this slot on subsequent
    // clean frames. SetRenderDirty() implicitly invalidates the cache because
    // RenderDirect re-records whenever _isRenderDirty is true. Kept as
    // object so Core doesn't leak the Media.Rendering.Drawing type.
    private object? _cachedDrawing;

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
    /// Gets whether this element or any descendant needs re-rendering.
    /// </summary>
    internal bool IsSubtreeDirty => _isSubtreeDirty;

    /// <summary>
    /// Marks this element as needing re-rendering and propagates subtree dirty flag up.
    /// </summary>
    internal void SetRenderDirty()
    {
        _isRenderDirty = true;
        MarkSubtreeDirtyUp();
    }

    /// <summary>
    /// Propagates the subtree dirty flag to all ancestors.
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
    /// Clears dirty flags after rendering.
    /// </summary>
    internal void ClearRenderDirty()
    {
        _isRenderDirty = false;
        _isSubtreeDirty = false;
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
    /// <param name="drawingContext">The drawing context (should be cast to DrawingContext from Media).</param>
    public void Render(object drawingContext)
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

    private void RenderDirect(object drawingContext)
    {
        // Respect UIElement visibility at render entry.
        // Hidden and Collapsed should not render.
        if (this is UIElement thisElementVisibility &&
            thisElementVisibility.Visibility != Visibility.Visible)
        {
            _isRenderDirty = false;
            _isSubtreeDirty = false;
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
        object? clipGeometry = null;
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

        // Retained-mode cache path. When a render cache host is installed
        // AND the live drawing context opts in via ICacheableDrawingContext,
        // OnRender is captured into an immutable command list the first time
        // the visual becomes dirty and replayed on subsequent clean frames.
        // The cached handle survives across frames; SetRenderDirty simply
        // flips _isRenderDirty, and RenderDirect re-records when it notices.
        //
        // The marker gate exists because OnRender(object) accepts any
        // context type — user code (typically tests) may pattern-match the
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
            }
            cacheHost.Replay(_cachedDrawing!, drawingContext);
        }
        else
        {
            OnRender(drawingContext);
        }

        var childCount = VisualChildrenCount;
        for (int i = 0; i < childCount && i < VisualChildrenCount; i++)
        {
            var child = GetVisualChild(i);
            if (child == null) continue;

            if (child is UIElement uiElement && uiElement.Visibility != Visibility.Visible)
            {
                continue;
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
                    continue;
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
            }
            else
            {
                child.Render(drawingContext);
            }
        }

        OnPostRender(drawingContext);

        if (pushedClip && drawingContext is IClipDrawingContext clipContext2)
        {
            clipContext2.Pop();
        }

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
        _isRenderDirty = false;
        _isSubtreeDirty = false;
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

    private static bool ShouldRenderChild(object drawingContext, UIElement child, Point childOffset)
    {
        if (drawingContext is not IClipBoundsDrawingContext { CurrentClipBounds: Rect clipBounds })
        {
            return true;
        }

        var childBounds = new Rect(childOffset.X, childOffset.Y, child.VisualBounds.Width, child.VisualBounds.Height);
        if (child.Effect is IEffect effect && effect.HasEffect)
        {
            var padding = effect.EffectPadding;
            childBounds = new Rect(
                childBounds.X - padding.Left,
                childBounds.Y - padding.Top,
                childBounds.Width + padding.Left + padding.Right,
                childBounds.Height + padding.Top + padding.Bottom);
        }

        return clipBounds.IntersectsWith(childBounds);
    }

    /// <summary>
    /// Override to provide custom rendering.
    /// </summary>
    /// <param name="drawingContext">The drawing context (should be cast to DrawingContext from Media).</param>
    protected virtual void OnRender(object drawingContext)
    {
    }

    /// <summary>
    /// Override to render content after children (useful for overlays like scrollbars).
    /// </summary>
    /// <param name="drawingContext">The drawing context.</param>
    protected virtual void OnPostRender(object drawingContext)
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
