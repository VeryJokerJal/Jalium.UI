namespace Jalium.UI.Controls.Ink;

/// <summary>
/// Destination blend mode for a brush's pixel-shader output.
/// The framework configures the D3D12 pipeline state accordingly; most
/// brushes should stay on <see cref="SourceOver"/> (the default).
/// </summary>
public enum BrushBlendMode
{
    /// <summary>
    /// Premultiplied source-over. Output.rgb assumed premultiplied.
    /// Used by pen / marker / round / calligraphy — anything that paints
    /// an opaque-ish trail.
    /// </summary>
    SourceOver,

    /// <summary>
    /// Additive with saturation clamp. Repeated passes accumulate brightness.
    /// Used by airbrush / watercolor where re-tracing deepens the color.
    /// </summary>
    Additive,

    /// <summary>
    /// Multiply-by-inverse-alpha: erases instead of paints. Used by the
    /// built-in eraser shader so it routes through the same dispatch path.
    /// </summary>
    Erase,
}

/// <summary>
/// A pixel-shader-based brush. The shader runs once per committed stroke
/// and paints directly into the InkCanvas's offscreen bitmap — the stroke
/// itself stops being a per-frame cost after commit.
/// <para/>
/// Built-in brushes subclass this and ship their HLSL as an embedded
/// resource. Application code can subclass too: provide an HLSL body that
/// implements <c>float4 BrushMain(float2 px)</c>, and the framework wraps
/// it with the shared preamble (stroke points SRV + cbuffer + helpers).
/// </summary>
public abstract class BrushShader
{
    /// <summary>
    /// HLSL source for this brush. Must define
    /// <c>float4 BrushMain(float2 px)</c>. The shared preamble providing
    /// the cbuffer, stroke-point SRV, and SDF helpers is prepended at
    /// compile time — do not redeclare them.
    /// </summary>
    public abstract string BrushMainHlsl { get; }

    /// <summary>
    /// Stable identifier used as the shader cache key. Two BrushShader
    /// instances that produce identical HLSL and identical blend mode
    /// should return the same ShaderKey so the native pipeline-state
    /// object is reused instead of recompiled.
    /// </summary>
    public abstract string ShaderKey { get; }

    /// <summary>
    /// Blend mode configured on the D3D12 pipeline state.
    /// Default: <see cref="BrushBlendMode.SourceOver"/>.
    /// </summary>
    public virtual BrushBlendMode BlendMode => BrushBlendMode.SourceOver;

    /// <summary>
    /// Optional extra parameters uploaded as a secondary cbuffer (b1).
    /// Keys must match HLSL field names; values are packed into a float4
    /// array in declaration order. Leave empty for brushes that only use
    /// the standard BrushConstants (b0).
    /// </summary>
    public virtual IReadOnlyList<BrushShaderParameter> ExtraParameters
        => System.Array.Empty<BrushShaderParameter>();
}

/// <summary>
/// A scalar or vector parameter in the brush's secondary cbuffer. The
/// framework packs parameters into a float4 array in the declaration
/// order returned by <see cref="BrushShader.ExtraParameters"/>.
/// </summary>
public readonly record struct BrushShaderParameter(string Name, float X, float Y, float Z, float W)
{
    public static BrushShaderParameter Float(string name, float value)
        => new(name, value, 0, 0, 0);

    public static BrushShaderParameter Float4(string name, float x, float y, float z, float w)
        => new(name, x, y, z, w);
}
