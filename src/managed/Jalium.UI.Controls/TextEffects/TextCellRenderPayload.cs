using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.TextEffects;

/// <summary>
/// Per-frame render state that a <see cref="ITextEffect"/> fills in for a single
/// <see cref="TextEffectCell"/>. The presenter then applies transforms, opacity,
/// and blur sampling on top of the cell's laid-out position.
/// </summary>
/// <remarks>
/// This is a mutable struct by design — effects receive it by ref and write
/// values directly, avoiding per-frame allocation. All transforms are composed
/// around <see cref="TransformOrigin"/>, expressed in cell-local coordinates
/// (0,0 = cell top-left).
/// </remarks>
public struct TextCellRenderPayload
{
    /// <summary>
    /// Horizontal translation applied on top of the cell's laid-out X, in pixels.
    /// Positive = right.
    /// </summary>
    public double TranslateX;

    /// <summary>
    /// Vertical translation applied on top of the cell's laid-out Y, in pixels.
    /// Positive = down.
    /// </summary>
    public double TranslateY;

    /// <summary>
    /// Horizontal scale around <see cref="TransformOrigin"/>. 1.0 = no scale.
    /// </summary>
    public double ScaleX;

    /// <summary>
    /// Vertical scale around <see cref="TransformOrigin"/>. 1.0 = no scale.
    /// </summary>
    public double ScaleY;

    /// <summary>
    /// Rotation around <see cref="TransformOrigin"/>, in degrees. 0 = no rotation.
    /// </summary>
    public double Rotation;

    /// <summary>
    /// Opacity multiplier in [0, 1]. Composes with the cell's and presenter's opacity.
    /// </summary>
    public double Opacity;

    /// <summary>
    /// Blur radius in pixels. The presenter samples the glyph <c>N</c> times around
    /// this radius to approximate a gaussian blur in the CPU render path. Set to 0
    /// to disable blur for the cell.
    /// </summary>
    public double BlurRadius;

    /// <summary>
    /// When non-null, replaces the presenter's <c>Foreground</c> brush for this cell
    /// on this frame only. Use for color tints that are part of an effect (e.g. flash).
    /// </summary>
    public Brush? ForegroundOverride;

    /// <summary>
    /// Origin of scale and rotation, in cell-local coordinates (0..cell.Width, 0..cell.Height).
    /// </summary>
    public Point TransformOrigin;

    /// <summary>
    /// When non-null, the presenter wraps this cell's draw call in
    /// <c>DrawingContext.PushEffect</c>/<c>PopEffect</c> so the glyph renders
    /// through a per-cell GPU effect pipeline (e.g. <c>BlurEffect</c>,
    /// <c>DropShadowEffect</c>, <c>ShaderEffect</c> with custom DXBC bytecode).
    /// </summary>
    /// <remarks>
    /// <para>Every cell with a non-null <see cref="PerCellEffect"/> triggers
    /// its own offscreen capture — one draw call becomes one render target
    /// allocation, one shader dispatch, and one compose-back. Use it for
    /// highlighted glyphs or accent characters, not on every cell of a long
    /// paragraph.</para>
    /// <para>Effects may be mutated between cells (e.g. updating a uniform
    /// DependencyProperty on a shared <c>ShaderEffect</c> instance) to achieve
    /// per-cell uniform differentiation without allocating a new effect per cell.</para>
    /// </remarks>
    public IEffect? PerCellEffect;

    /// <summary>
    /// Returns an identity payload (no transform, fully opaque, no blur).
    /// Effects typically start from this and mutate fields they care about.
    /// </summary>
    public static TextCellRenderPayload Identity => new()
    {
        TranslateX = 0,
        TranslateY = 0,
        ScaleX = 1,
        ScaleY = 1,
        Rotation = 0,
        Opacity = 1,
        BlurRadius = 0,
        ForegroundOverride = null,
        TransformOrigin = default,
        PerCellEffect = null,
    };
}
