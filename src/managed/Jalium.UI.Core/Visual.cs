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

        if (child._parent != null)
        {
            throw new InvalidOperationException($"Visual already has a parent. child={child.GetType().Name}, parent={child._parent.GetType().Name}, attempted new parent={this.GetType().Name}");
        }

        var oldParent = child._parent;
        child._parent = this;
        _children.Add(child);

        OnVisualChildrenChanged(child, null);
        child.OnVisualParentChanged(oldParent);
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
                break; // Already marked, ancestors also marked
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
        // Respect UIElement visibility at render entry.
        // Hidden and Collapsed should not render.
        if (this is UIElement thisElementVisibility &&
            thisElementVisibility.Visibility != Visibility.Visible)
        {
            _isRenderDirty = false;
            _isSubtreeDirty = false;
            return;
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
            captureX = (float)(offsetDc.Offset.X - padding.Left);
            captureY = (float)(offsetDc.Offset.Y - padding.Top);
            captureW = (float)(size.Width + padding.Left + padding.Right);
            captureH = (float)(size.Height + padding.Top + padding.Bottom);

            effectDc.BeginEffectCapture(captureX, captureY, captureW, captureH);
        }

        OnRender(drawingContext);

        // Check if clipping should be applied before rendering children
        object? clipGeometry = null;
        if (this is UIElement thisElement)
        {
            clipGeometry = thisElement.GetLayoutClip();
        }

        // Push clip if available (clipGeometry should be Media.Geometry)
        bool pushedClip = false;
        if (clipGeometry != null && drawingContext is IClipDrawingContext clipContext)
        {
            clipContext.PushClip(clipGeometry);
            pushedClip = true;
        }

        var childCount = VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = GetVisualChild(i);
            if (child == null) continue;

            // Skip non-visible children
            if (child is UIElement uiElement && uiElement.Visibility != Visibility.Visible)
                continue;

            // Get child's visual bounds and update drawing context offset
            if (child is UIElement uiChild && drawingContext is IOffsetDrawingContext offsetContext)
            {
                var bounds = uiChild.VisualBounds;
                var savedOffset = offsetContext.Offset;

                // Apply RenderOffset (visual-only translation, does not affect layout)
                var ro = uiChild.RenderOffset;
                offsetContext.Offset = new Point(
                    savedOffset.X + bounds.X + ro.X,
                    savedOffset.Y + bounds.Y + ro.Y);

                // Handle opacity for child elements
                var childOpacity = uiChild.Opacity;
                var pushedOpacity = false;
                if (childOpacity < 1.0 && drawingContext is IOpacityDrawingContext opacityContext)
                {
                    opacityContext.PushOpacity(childOpacity);
                    pushedOpacity = true;
                }

                child.Render(drawingContext);

                // Pop opacity if it was pushed
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

        // Pop clip if it was pushed
        if (pushedClip && drawingContext is IClipDrawingContext clipContext2)
        {
            clipContext2.Pop();
        }

        // Call post-render for overlays (like scrollbars)
        OnPostRender(drawingContext);

        // End effect capture and apply the effect to the captured content
        if (activeEffect != null && effectDc != null)
        {
            effectDc.EndEffectCapture();
            effectDc.ApplyElementEffect(activeEffect, captureX, captureY, captureW, captureH);
        }

        // Clear dirty flags after this element has rendered
        _isRenderDirty = false;
        _isSubtreeDirty = false;
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
