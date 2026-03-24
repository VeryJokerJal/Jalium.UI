using Jalium.UI;

namespace Jalium.UI.Media;

/// <summary>
/// Specifies the system backdrop type for a window.
/// These are DWM system backdrops that blur content behind the window (desktop, other apps).
/// </summary>
public enum WindowBackdropType
{
    /// <summary>
    /// No system backdrop. Default behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// Let the Desktop Window Manager (DWM) automatically decide the system-drawn backdrop material.
    /// </summary>
    Auto = 1,

    /// <summary>
    /// Mica effect - samples the desktop wallpaper with blur and tint.
    /// Available on Windows 11 22000+.
    /// </summary>
    Mica = 2,

    /// <summary>
    /// Acrylic effect - blurs content behind the window with tint.
    /// Available on Windows 11 22H2+.
    /// </summary>
    Acrylic = 3,

    /// <summary>
    /// Mica Alt effect - similar to Mica but with a different appearance.
    /// Available on Windows 11 22H2+.
    /// </summary>
    MicaAlt = 4
}

/// <summary>
/// Base class for backdrop effects providing common functionality.
/// </summary>
public abstract class BackdropEffect : IBackdropEffect
{
    /// <inheritdoc />
    public virtual float BlurRadius { get; set; }

    /// <inheritdoc />
    public virtual float BlurSigma { get; set; }

    /// <inheritdoc />
    public virtual BackdropBlurType BlurType { get; set; } = BackdropBlurType.Gaussian;

    /// <inheritdoc />
    public virtual float NoiseIntensity { get; set; }

    /// <inheritdoc />
    public virtual float Brightness { get; set; } = 1.0f;

    /// <inheritdoc />
    public virtual float Contrast { get; set; } = 1.0f;

    /// <inheritdoc />
    public virtual float Saturation { get; set; } = 1.0f;

    /// <inheritdoc />
    public virtual float HueRotation { get; set; }

    /// <inheritdoc />
    public virtual float Grayscale { get; set; }

    /// <inheritdoc />
    public virtual float Sepia { get; set; }

    /// <inheritdoc />
    public virtual float Invert { get; set; }

    /// <inheritdoc />
    public virtual float Opacity { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the tint color.
    /// </summary>
    public virtual Color TintColor { get; set; } = Color.Transparent;

    /// <inheritdoc />
    public uint TintColorArgb => TintColor.ToArgb();

    /// <inheritdoc />
    public virtual float TintOpacity { get; set; }

    /// <inheritdoc />
    public virtual float Luminosity { get; set; } = 1.0f;

    /// <inheritdoc />
    public virtual bool HasEffect =>
        BlurRadius > 0 ||
        Math.Abs(Brightness - 1.0f) > 0.001f ||
        Math.Abs(Contrast - 1.0f) > 0.001f ||
        Math.Abs(Saturation - 1.0f) > 0.001f ||
        Math.Abs(HueRotation) > 0.001f ||
        Grayscale > 0 ||
        Sepia > 0 ||
        Invert > 0 ||
        Math.Abs(Opacity - 1.0f) > 0.001f ||
        TintOpacity > 0;
}

/// <summary>
/// A simple blur effect.
/// </summary>
public sealed class BlurEffect : BackdropEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BlurEffect"/> class.
    /// </summary>
    public BlurEffect()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BlurEffect"/> class.
    /// </summary>
    /// <param name="radius">The blur radius in pixels.</param>
    /// <param name="blurType">The type of blur to apply.</param>
    public BlurEffect(float radius, BackdropBlurType blurType = BackdropBlurType.Gaussian)
    {
        BlurRadius = radius;
        BlurType = blurType;
        BlurSigma = radius / 3.0f;
    }
}

/// <summary>
/// Windows Acrylic material effect.
/// </summary>
public sealed class AcrylicEffect : BackdropEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AcrylicEffect"/> class with default settings.
    /// </summary>
    public AcrylicEffect()
    {
        BlurRadius = 30f;
        BlurSigma = 10f;
        BlurType = BackdropBlurType.Gaussian;
        NoiseIntensity = 0.02f;
        TintOpacity = 0.6f;
        Luminosity = 1.0f;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AcrylicEffect"/> class.
    /// </summary>
    /// <param name="tintColor">The tint color.</param>
    /// <param name="tintOpacity">The tint opacity (0.0 - 1.0).</param>
    /// <param name="blurRadius">The blur radius.</param>
    public AcrylicEffect(Color tintColor, float tintOpacity = 0.6f, float blurRadius = 30f)
        : this()
    {
        TintColor = tintColor;
        TintOpacity = tintOpacity;
        BlurRadius = blurRadius;
        BlurSigma = blurRadius / 3.0f;
    }
}

/// <summary>
/// Windows 11 Mica material effect.
/// </summary>
public sealed class MicaEffect : BackdropEffect
{
    /// <summary>
    /// Gets or sets whether to use the alternate Mica style.
    /// </summary>
    public bool UseAlt { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MicaEffect"/> class.
    /// </summary>
    public MicaEffect()
    {
        BlurRadius = 60f;
        BlurSigma = 20f;
        BlurType = BackdropBlurType.Gaussian;
        Saturation = 1.25f;
        Luminosity = 1.03f;
        TintOpacity = 0.8f;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MicaEffect"/> class.
    /// </summary>
    /// <param name="useAlt">Whether to use the alternate Mica style.</param>
    public MicaEffect(bool useAlt)
        : this()
    {
        UseAlt = useAlt;
        if (useAlt)
        {
            Saturation = 1.0f;
            Luminosity = 1.0f;
            TintOpacity = 0.5f;
        }
    }
}

/// <summary>
/// Frosted glass effect.
/// </summary>
public sealed class FrostedGlassEffect : BackdropEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FrostedGlassEffect"/> class.
    /// </summary>
    public FrostedGlassEffect()
    {
        BlurRadius = 20f;
        BlurSigma = 6.67f;
        BlurType = BackdropBlurType.Frosted;
        NoiseIntensity = 0.03f;
        TintOpacity = 0.4f;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FrostedGlassEffect"/> class.
    /// </summary>
    /// <param name="blurRadius">The blur radius.</param>
    /// <param name="noiseIntensity">The noise intensity.</param>
    /// <param name="tintColor">The tint color.</param>
    /// <param name="tintOpacity">The tint opacity.</param>
    public FrostedGlassEffect(float blurRadius, float noiseIntensity = 0.03f, Color? tintColor = null, float tintOpacity = 0.4f)
        : this()
    {
        BlurRadius = blurRadius;
        BlurSigma = blurRadius / 3.0f;
        NoiseIntensity = noiseIntensity;
        TintColor = tintColor ?? Color.White;
        TintOpacity = tintOpacity;
    }
}

/// <summary>
/// A color adjustment effect for backdrop.
/// </summary>
public sealed class ColorAdjustmentEffect : BackdropEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ColorAdjustmentEffect"/> class.
    /// </summary>
    public ColorAdjustmentEffect()
    {
    }

    /// <summary>
    /// Creates a brightness adjustment effect.
    /// </summary>
    public static ColorAdjustmentEffect CreateBrightness(float factor) =>
        new() { Brightness = factor };

    /// <summary>
    /// Creates a contrast adjustment effect.
    /// </summary>
    public static ColorAdjustmentEffect CreateContrast(float factor) =>
        new() { Contrast = factor };

    /// <summary>
    /// Creates a saturation adjustment effect.
    /// </summary>
    public static ColorAdjustmentEffect CreateSaturation(float factor) =>
        new() { Saturation = factor };

    /// <summary>
    /// Creates a grayscale effect.
    /// </summary>
    public static ColorAdjustmentEffect CreateGrayscale(float amount = 1.0f) =>
        new() { Grayscale = amount };

    /// <summary>
    /// Creates a sepia effect.
    /// </summary>
    public static ColorAdjustmentEffect CreateSepia(float amount = 1.0f) =>
        new() { Sepia = amount };

    /// <summary>
    /// Creates an invert effect.
    /// </summary>
    public static ColorAdjustmentEffect CreateInvert(float amount = 1.0f) =>
        new() { Invert = amount };

    /// <summary>
    /// Creates a hue rotation effect.
    /// </summary>
    public static ColorAdjustmentEffect CreateHueRotate(float degrees) =>
        new() { HueRotation = degrees * MathF.PI / 180f };
}

/// <summary>
/// A composite effect that combines multiple backdrop effects.
/// </summary>
public sealed class CompositeBackdropEffect : BackdropEffect
{
    private readonly List<IBackdropEffect> _effects = new();

    /// <summary>
    /// Gets the list of effects to combine.
    /// </summary>
    public IReadOnlyList<IBackdropEffect> Effects => _effects;

    /// <summary>
    /// Adds an effect to the composite.
    /// </summary>
    public CompositeBackdropEffect Add(IBackdropEffect effect)
    {
        _effects.Add(effect);
        UpdateCombinedValues();
        return this;
    }

    /// <summary>
    /// Removes an effect from the composite.
    /// </summary>
    public CompositeBackdropEffect Remove(IBackdropEffect effect)
    {
        _effects.Remove(effect);
        UpdateCombinedValues();
        return this;
    }

    private void UpdateCombinedValues()
    {
        // Reset to defaults
        BlurRadius = 0;
        BlurSigma = 0;
        BlurType = BackdropBlurType.Gaussian;
        NoiseIntensity = 0;
        Brightness = 1.0f;
        Contrast = 1.0f;
        Saturation = 1.0f;
        HueRotation = 0;
        Grayscale = 0;
        Sepia = 0;
        Invert = 0;
        Opacity = 1.0f;
        TintColor = Color.Transparent;
        TintOpacity = 0;
        Luminosity = 1.0f;

        // Combine effects
        foreach (var effect in _effects)
        {
            // For blur, use the maximum
            if (effect.BlurRadius > BlurRadius)
            {
                BlurRadius = effect.BlurRadius;
                BlurSigma = effect.BlurSigma;
                BlurType = effect.BlurType;
            }

            // For noise, use the maximum
            NoiseIntensity = Math.Max(NoiseIntensity, effect.NoiseIntensity);

            // For color adjustments, multiply
            Brightness *= effect.Brightness;
            Contrast *= effect.Contrast;
            Saturation *= effect.Saturation;

            // For hue rotation, add
            HueRotation += effect.HueRotation;
            HueRotation = HueRotation % (2.0f * MathF.PI);

            // For grayscale/sepia/invert, use maximum
            Grayscale = Math.Max(Grayscale, effect.Grayscale);
            Sepia = Math.Max(Sepia, effect.Sepia);
            Invert = Math.Max(Invert, effect.Invert);

            // For opacity, multiply
            Opacity *= effect.Opacity;

            // For tint, use the last non-transparent one
            if (effect.TintOpacity > 0)
            {
                TintColor = Color.FromArgb(effect.TintColorArgb);
                TintOpacity = effect.TintOpacity;
            }

            // For luminosity, multiply
            Luminosity *= effect.Luminosity;
        }
    }
}
