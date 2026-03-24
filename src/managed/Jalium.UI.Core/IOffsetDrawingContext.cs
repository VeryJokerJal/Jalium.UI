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
    /// <param name="clipGeometry">The clipping geometry (Media.Geometry).</param>
    void PushClip(object clipGeometry);

    /// <summary>
    /// Pushes a rounded-rect clip using element bounds and corner radius.
    /// </summary>
    void PushRoundedRectClip(Rect bounds, CornerRadius cornerRadius) =>
        PushClip(bounds); // default: fall back to rectangular clip

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
    /// <param name="transform">The transform to push (Media.Transform).</param>
    /// <param name="originX">The X origin in pixels for the transform center.</param>
    /// <param name="originY">The Y origin in pixels for the transform center.</param>
    void PushTransform(object transform, double originX, double originY);

    /// <summary>
    /// Pops the most recent transform from the transform stack.
    /// </summary>
    void PopTransform();
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
