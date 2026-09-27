using System.Runtime.InteropServices;
using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// Blur kernel family of a backdrop material, mirroring the native
/// <c>JaliumBackdropBlurType</c>.
/// </summary>
public enum BackdropMaterialBlurType
{
    /// <summary>Separable Gaussian (sigma = <see cref="BackdropMaterialDesc.BlurSigma"/> or radius / 3).</summary>
    Gaussian = 0,

    /// <summary>Box-equivalent Gaussian (sigma = radius / sqrt(3)).</summary>
    Box = 1,

    /// <summary>Gaussian plus per-pixel sample jitter for frosted-glass grain.</summary>
    Frosted = 2,
}

/// <summary>
/// Full parameter set of an in-app backdrop material. Layout MUST stay
/// byte-identical to <c>JaliumBackdropMaterialDesc</c> in jalium_types.h; the
/// native side rejects a call whose <see cref="StructSize"/> does not match.
/// </summary>
/// <remarks>
/// Colour pipeline order shared by every backend (a single CSS backdrop-filter
/// function maps to one stage; ordered CSS lists need a separate pipeline):
/// blur → brightness → contrast → saturation → hue rotation → grayscale → sepia
/// → invert → tint → luminosity → noise. All lengths are DIPs.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct BackdropMaterialDesc
{
    /// <summary>sizeof(JaliumBackdropMaterialDesc) — 26 × 4 bytes.</summary>
    public const uint NativeStructSize = 26 * 4;

    /// <summary>Versioning guard; always <see cref="NativeStructSize"/>.</summary>
    public uint StructSize;

    /// <summary><see cref="BackdropMaterialBlurType"/> as its native integer.</summary>
    public uint BlurType;

    /// <summary>Panel rect in DIPs.</summary>
    public float X;
    /// <summary>Panel rect in DIPs.</summary>
    public float Y;
    /// <summary>Panel rect in DIPs.</summary>
    public float Width;
    /// <summary>Panel rect in DIPs.</summary>
    public float Height;

    /// <summary>Kernel extent in DIPs (radius ≈ 3σ convention).</summary>
    public float BlurRadius;
    /// <summary>Gaussian sigma in DIPs; 0 = <see cref="BlurRadius"/> / 3.</summary>
    public float BlurSigma;
    /// <summary>Full-range grain mixed at this amplitude (0 = none).</summary>
    public float NoiseIntensity;

    /// <summary>Tint colour, straight (0..1).</summary>
    public float TintR;
    /// <summary>Tint colour, straight (0..1).</summary>
    public float TintG;
    /// <summary>Tint colour, straight (0..1).</summary>
    public float TintB;
    /// <summary>Effective tint opacity (colour alpha already folded in).</summary>
    public float TintA;

    /// <summary>1 = unchanged.</summary>
    public float Saturation;
    /// <summary>Multiplier applied after the tint; 1 = unchanged.</summary>
    public float Luminosity;
    /// <summary>1 = unchanged.</summary>
    public float Brightness;
    /// <summary>1 = unchanged.</summary>
    public float Contrast;
    /// <summary>Radians.</summary>
    public float HueRotation;
    /// <summary>0..1.</summary>
    public float Grayscale;
    /// <summary>0..1.</summary>
    public float Sepia;
    /// <summary>0..1.</summary>
    public float Invert;
    /// <summary>Overall effect opacity (0 = backdrop untouched).</summary>
    public float Opacity;

    /// <summary>Per-corner rounding in DIPs, already normalised to the rect.</summary>
    public float CornerRadiusTL;
    /// <summary>Per-corner rounding in DIPs, already normalised to the rect.</summary>
    public float CornerRadiusTR;
    /// <summary>Per-corner rounding in DIPs, already normalised to the rect.</summary>
    public float CornerRadiusBR;
    /// <summary>Per-corner rounding in DIPs, already normalised to the rect.</summary>
    public float CornerRadiusBL;

    /// <summary>
    /// Builds a description from an <see cref="IBackdropEffect"/>.
    /// </summary>
    /// <remarks>
    /// Tint semantics follow WinUI's AcrylicBrush: the tint colour's own alpha
    /// multiplies <see cref="IBackdropEffect.TintOpacity"/>. A fully transparent
    /// tint colour (the <see cref="Jalium.UI.Media.BackdropEffect"/> default) means
    /// "no colour set" and resolves to white at <c>TintOpacity</c>, which keeps the
    /// historical look of the parameterless presets.
    /// <see cref="BackdropBlurType.Directional"/>, <see cref="BackdropBlurType.Radial"/>
    /// and <see cref="BackdropBlurType.Zoom"/> have no native kernel and render as
    /// Gaussian.
    /// </remarks>
    public static BackdropMaterialDesc FromEffect(
        IBackdropEffect effect,
        float x, float y, float width, float height,
        in CornerRadius normalizedCornerRadius)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var tint = Color.FromArgb(effect.TintColorArgb);
        float tintR, tintG, tintB, tintA;
        if (tint.A == 0)
        {
            tintR = tintG = tintB = 1f;
            tintA = effect.TintOpacity;
        }
        else
        {
            tintR = tint.R / 255f;
            tintG = tint.G / 255f;
            tintB = tint.B / 255f;
            tintA = (tint.A / 255f) * effect.TintOpacity;
        }

        return new BackdropMaterialDesc
        {
            StructSize = NativeStructSize,
            BlurType = (uint)MapBlurType(effect.BlurType),
            X = x,
            Y = y,
            Width = width,
            Height = height,
            BlurRadius = Math.Max(0f, effect.BlurRadius),
            BlurSigma = effect.BlurSigma > 0f ? effect.BlurSigma : 0f,
            NoiseIntensity = Math.Clamp(effect.NoiseIntensity, 0f, 1f),
            TintR = tintR,
            TintG = tintG,
            TintB = tintB,
            TintA = Math.Clamp(tintA, 0f, 1f),
            Saturation = Math.Max(0f, effect.Saturation),
            Luminosity = Math.Max(0f, effect.Luminosity),
            Brightness = Math.Max(0f, effect.Brightness),
            Contrast = Math.Max(0f, effect.Contrast),
            HueRotation = effect.HueRotation,
            Grayscale = Math.Clamp(effect.Grayscale, 0f, 1f),
            Sepia = Math.Clamp(effect.Sepia, 0f, 1f),
            Invert = Math.Clamp(effect.Invert, 0f, 1f),
            Opacity = Math.Clamp(effect.Opacity, 0f, 1f),
            CornerRadiusTL = (float)normalizedCornerRadius.TopLeft,
            CornerRadiusTR = (float)normalizedCornerRadius.TopRight,
            CornerRadiusBR = (float)normalizedCornerRadius.BottomRight,
            CornerRadiusBL = (float)normalizedCornerRadius.BottomLeft,
        };
    }

    /// <summary>Maps the public blur type onto the native kernel family.</summary>
    public static BackdropMaterialBlurType MapBlurType(BackdropBlurType blurType) => blurType switch
    {
        BackdropBlurType.Box => BackdropMaterialBlurType.Box,
        BackdropBlurType.Frosted => BackdropMaterialBlurType.Frosted,
        _ => BackdropMaterialBlurType.Gaussian,
    };
}
