using Jalium.UI;

namespace Jalium.UI.Media;

/// <summary>
/// Aggregate parameter object for <see cref="DrawingContext.DrawLiquidGlass"/>.
/// Carried as a reference type because <see cref="DrawingContext"/> is also the
/// API surface observed by recording contexts (retained-mode cache): a value
/// type would box into the command payload on every draw, whereas a single
/// shared class instance is stored by reference without allocation beyond the
/// one initial construction per frame.
/// </summary>
/// <remarks>
/// <para>
/// The rectangle is in local drawing coordinates, mirroring every other
/// <c>Draw*</c> API on <see cref="DrawingContext"/>. Implementations that
/// render to a real render target are expected to add <c>IOffsetDrawingContext.Offset</c>
/// before issuing the underlying native call. Mouse / light coordinates in
/// contrast are provided in screen space by the caller because the shader
/// treats them as a global position, independent of the element's own offset.
/// </para>
/// <para>
/// <see cref="NeighborData"/> is a tightly packed array of <c>(x, y, w, h, cornerRadius)</c>
/// tuples describing up to <see cref="NeighborCount"/> sibling glass panels
/// that the caller wants the shader to fuse with. Each tuple occupies five
/// consecutive floats; the array may be <c>null</c> when <see cref="NeighborCount"/>
/// is zero.
/// </para>
/// </remarks>
public sealed class LiquidGlassParameters
{
    /// <summary>
    /// Gets or sets the rectangle in local drawing coordinates.
    /// </summary>
    public Rect Rectangle { get; set; }

    /// <summary>
    /// Gets or sets the (average) corner radius of the glass shape.
    /// </summary>
    public float CornerRadius { get; set; }

    /// <summary>
    /// Gets or sets the Gaussian blur radius applied to the captured
    /// background before refraction.
    /// </summary>
    public float BlurRadius { get; set; } = 8f;

    /// <summary>
    /// Gets or sets the refraction strength.
    /// </summary>
    public float RefractionAmount { get; set; } = 60f;

    /// <summary>
    /// Gets or sets the chromatic aberration amount (0 - 1).
    /// </summary>
    public float ChromaticAberration { get; set; }

    /// <summary>
    /// Gets or sets the red component of the glass tint (0 - 1, linear).
    /// </summary>
    public float TintR { get; set; } = 0.08f;

    /// <summary>
    /// Gets or sets the green component of the glass tint (0 - 1, linear).
    /// </summary>
    public float TintG { get; set; } = 0.08f;

    /// <summary>
    /// Gets or sets the blue component of the glass tint (0 - 1, linear).
    /// </summary>
    public float TintB { get; set; } = 0.08f;

    /// <summary>
    /// Gets or sets the opacity of the glass tint (0 - 1).
    /// </summary>
    public float TintOpacity { get; set; } = 0.3f;

    /// <summary>
    /// Gets or sets the screen-space X position of the mouse-following light.
    /// Use -1 when no light is active.
    /// </summary>
    public float LightX { get; set; } = -1f;

    /// <summary>
    /// Gets or sets the screen-space Y position of the mouse-following light.
    /// Use -1 when no light is active.
    /// </summary>
    public float LightY { get; set; } = -1f;

    /// <summary>
    /// Gets or sets an extra highlight boost (0 - 1) layered on top of the
    /// mouse-following light — used by the press-interaction animation.
    /// </summary>
    public float HighlightBoost { get; set; }

    /// <summary>
    /// Gets or sets the glass shape: 0 = rounded rectangle, 1 = super-ellipse.
    /// </summary>
    public int ShapeType { get; set; }

    /// <summary>
    /// Gets or sets the super-ellipse exponent used when <see cref="ShapeType"/>
    /// is 1. Defaults to 4 (iOS-style squircle).
    /// </summary>
    public float ShapeExponent { get; set; } = 4f;

    /// <summary>
    /// Gets or sets the number of neighboring glass panels to fuse with (0 - 4).
    /// </summary>
    public int NeighborCount { get; set; }

    /// <summary>
    /// Gets or sets the smooth-min radius controlling how far apart two
    /// sibling glass panels can be and still fuse.
    /// </summary>
    public float FusionRadius { get; set; } = 30f;

    /// <summary>
    /// Gets or sets the flat array of neighbor descriptors. Five floats per
    /// neighbor: <c>(x, y, width, height, cornerRadius)</c> in screen space,
    /// laid out consecutively for <see cref="NeighborCount"/> neighbors.
    /// May be <c>null</c> when <see cref="NeighborCount"/> is zero.
    /// </summary>
    public float[]? NeighborData { get; set; }
}
