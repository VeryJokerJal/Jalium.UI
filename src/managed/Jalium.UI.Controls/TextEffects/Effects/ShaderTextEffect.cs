using System;
using Jalium.UI;

namespace Jalium.UI.Controls.TextEffects.Effects;

/// <summary>
/// Base class for text effects that route the entire <see cref="TextEffectPresenter"/>
/// surface through a GPU <see cref="IEffect"/> — typically a subclass of
/// <see cref="Jalium.UI.Media.Effects.ShaderEffect"/> holding user-supplied
/// DXBC bytecode, but any concrete <see cref="IEffect"/> (BlurEffect,
/// DropShadowEffect, OuterGlowEffect, ColorMatrixEffect, etc.) is accepted.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists alongside the ordinary <see cref="UIElement.Effect"/>:
/// <see cref="UIElement.Effect"/> is a dependency property — once assigned,
/// its parameters are static until someone explicitly writes to them. A
/// <see cref="ShaderTextEffect"/> instead exposes <see cref="UpdateForFrame"/>,
/// which the presenter calls on every render tick. Subclasses use it to
/// refresh shader constants / effect properties from the animation clock
/// (pulsing, scrolling noise, time-varying distortion, etc.) without any
/// per-frame plumbing in user code.
/// </para>
/// <para>
/// Interaction with the presenter's built-in blur pass:
/// when a <see cref="ShaderTextEffect"/> is active, the presenter renders
/// <b>all</b> cells inside one <c>PushEffect</c> scope wrapping
/// <see cref="CurrentEffect"/>, and the <c>BlurRadius</c> field on
/// <see cref="TextCellRenderPayload"/> is ignored — the shader is expected
/// to be the final visual pass. Nested offscreen capture is not supported
/// by the native D3D12 renderer, so combining built-in blur and a
/// <see cref="ShaderTextEffect"/> is not allowed.
/// </para>
/// </remarks>
public abstract class ShaderTextEffect : TextEffectBase
{
    /// <summary>
    /// Called by the presenter once per frame, immediately before rendering,
    /// with the current surface size and the animation clock in ms. Subclasses
    /// override to refresh shader uniforms / effect properties.
    /// </summary>
    protected internal abstract void UpdateForFrame(Size presenterSize, double totalElapsedMs);

    /// <summary>
    /// The effect to apply to the whole presenter surface this frame. Called
    /// after <see cref="UpdateForFrame"/>. Returning <c>null</c> (or an
    /// <see cref="IEffect"/> whose <see cref="IEffect.HasEffect"/> is false)
    /// makes the presenter skip the shader pass entirely for the frame and
    /// render cells normally — useful when the effect has a natural "off"
    /// state (e.g. pulse amplitude dips through zero).
    /// </summary>
    public abstract IEffect? CurrentEffect { get; }

    /// <summary>
    /// Default per-cell apply is a no-op: the shader pass is the visual, and
    /// mixing per-cell transforms with a full-presenter capture would produce
    /// weird double-animation artifacts. Subclasses may override if they want
    /// to compose per-cell positioning <i>inside</i> the shader pass.
    /// </summary>
    public override void Apply(in TextEffectFrameContext context, ref TextCellRenderPayload payload)
    {
        // Intentionally empty.
    }

    /// <inheritdoc />
    public override double EnterDurationMs => 500;

    /// <inheritdoc />
    public override double ShiftDurationMs => 300;

    /// <inheritdoc />
    public override double ExitDurationMs => 400;
}
