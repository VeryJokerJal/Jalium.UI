using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Interface for drawing contexts that support offset-based positioning.
/// </summary>
public interface IOffsetDrawingContext
{
    /// <summary>
    /// Gets or sets the current drawing offset.
    /// </summary>
    Point Offset { get; set; }
}

/// <summary>
/// Interface for drawing contexts that support clipping.
/// </summary>
public interface IClipDrawingContext
{
    /// <summary>
    /// Pushes a clip region onto the clip stack.
    /// </summary>
    /// <param name="clipGeometry">The clipping geometry.</param>
    void PushClip(Geometry clipGeometry);

    /// <summary>
    /// Pushes a rounded-rect clip using element bounds and corner radius.
    /// </summary>
    void PushRoundedRectClip(Rect bounds, CornerRadius cornerRadius) =>
        PushClip(new RectangleGeometry(bounds)); // default: fall back to rectangular clip

    /// <summary>
    /// Pushes a per-corner rounded-rect clip. Default forwards to
    /// <see cref="PushRoundedRectClip(Rect, CornerRadius)"/> so contexts that
    /// don't natively support asymmetric corners stay correct (the underlying
    /// implementation may collapse to max-radius / rectangular fall-back).
    /// </summary>
    void PushPerCornerRoundedRectClip(Rect bounds, CornerRadius cornerRadius) =>
        PushRoundedRectClip(bounds, cornerRadius);

    /// <summary>
    /// Pops the most recent clip from the clip stack.
    /// </summary>
    void Pop();
}

/// <summary>
/// Interface for drawing contexts that can expose the effective clip bounds
/// of the current render pass in absolute drawing coordinates.
/// </summary>
public interface IClipBoundsDrawingContext
{
    /// <summary>
    /// Gets the current effective clip bounds, or null when unclipped.
    /// </summary>
    Rect? CurrentClipBounds { get; }
}

/// <summary>
/// Interface for drawing contexts that support opacity.
/// </summary>
public interface IOpacityDrawingContext
{
    /// <summary>
    /// Pushes an opacity value onto the opacity stack.
    /// </summary>
    /// <param name="opacity">The opacity (0.0 - 1.0).</param>
    void PushOpacity(double opacity);

    /// <summary>
    /// Pops the most recent opacity from the opacity stack.
    /// </summary>
    void PopOpacity();
}

/// <summary>
/// Interface for drawing contexts that support push/pop transforms.
/// </summary>
public interface ITransformDrawingContext
{
    /// <summary>
    /// Pushes a transform onto the transform stack, applying it around the specified origin.
    /// </summary>
    /// <param name="transform">The transform to push.</param>
    /// <param name="originX">The X origin in pixels for the transform center.</param>
    /// <param name="originY">The Y origin in pixels for the transform center.</param>
    void PushTransform(Transform transform, double originX, double originY);

    /// <summary>
    /// Pops the most recent transform from the transform stack.
    /// </summary>
    void PopTransform();
}

/// <summary>
/// Interface for drawing contexts that can realize a visual subtree into a
/// persistent GPU texture once and composite that texture as a transformed /
/// opacity-scaled quad on subsequent frames. This lets a composited animation
/// (Opacity / RenderTransform — <c>AffectsCompositionOnly</c>) on a content-clean
/// subtree skip re-emitting the whole subtree's draw commands every frame:
/// instead the parent draws one cached-layer quad with the live transform/opacity.
/// </summary>
/// <remarks>
/// The live render-target context implements this; recording / probing contexts
/// (the retained-mode recorder, the whole-frame recorder) do NOT, so a caller that
/// only sees a recorder transparently falls back to full re-emission. A layer is a
/// content snapshot at identity transform/opacity: it is rebuilt only when the
/// subtree's CONTENT changes (render-dirty / size / DPI), never when the animated
/// transform/opacity changes (those are applied at composite time).
/// </remarks>
public interface ILayerCompositingDrawingContext
{
    /// <summary>
    /// Whether this context can realize and composite retained GPU layers right now
    /// (false on backends without offscreen-RT capture, e.g. so callers fall back to
    /// full re-emission with no behavior change).
    /// </summary>
    bool SupportsRetainedLayers { get; }

    /// <summary>
    /// Opens a capture scope that redirects subsequent draws into a persistent layer
    /// texture covering <paramref name="worldBounds"/> (in current offset/screen
    /// space). Reuses/resizes <paramref name="existingLayer"/> when non-zero. The
    /// caller renders the subtree's CONTENT (no animated transform/opacity) between
    /// this call and <see cref="EndLayerCapture"/>. Returns the layer handle, or
    /// <c>0</c> if a layer could not be realized (caller must fall back to direct
    /// rendering and must not call <see cref="EndLayerCapture"/>).
    /// </summary>
    nint BeginLayerCapture(nint existingLayer, Rect worldBounds);

    /// <summary>
    /// Closes the capture scope opened by <see cref="BeginLayerCapture"/> and
    /// finalizes the layer texture for sampling. Restores the previous render target
    /// / viewport / clip state.
    /// </summary>
    void EndLayerCapture(nint layer);

    /// <summary>
    /// Composites a realized layer as a single quad at <paramref name="worldBounds"/>
    /// with the live <paramref name="opacity"/> and <paramref name="transform"/>
    /// (applied around the given origin), honoring the current clip/scissor and
    /// z-order. Cheap and O(1) regardless of how many draw commands the subtree had.
    /// </summary>
    void CompositeLayer(nint layer, Rect worldBounds, double opacity,
        Transform? transform, double originX, double originY);
}

/// <summary>
/// Interface for drawing contexts that support element effect capture and rendering.
/// </summary>
public interface IEffectDrawingContext
{
    /// <summary>
    /// Begins capturing element content into an offscreen bitmap for effect processing.
    /// </summary>
    void BeginEffectCapture(float x, float y, float w, float h);

    /// <summary>
    /// Ends capturing element content and restores the main render target.
    /// </summary>
    void EndEffectCapture();

    /// <summary>
    /// Applies the given element effect to the captured content and draws the result.
    /// The implementation dispatches to the appropriate native rendering method
    /// based on the concrete effect type.
    /// </summary>
    void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
        float captureOriginX = 0, float captureOriginY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0);
}
