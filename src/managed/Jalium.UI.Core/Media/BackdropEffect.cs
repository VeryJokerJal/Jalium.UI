using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI;
using Jalium.UI.Markup;

namespace Jalium.UI.Media;

/// <summary>
/// Specifies the system backdrop type for a window.
/// Uses DWM backdrops on Windows and corresponding AppKit materials on macOS.
/// An opaque Window.Background covers the native material.
/// </summary>
public enum WindowBackdropType
{
    /// <summary>
    /// No system backdrop. Default behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// Let DWM select the material on Windows; uses the window background material on macOS.
    /// </summary>
    Auto = 1,

    /// <summary>
    /// Mica effect - samples the desktop wallpaper with blur and tint.
    /// Available on Windows 11 22000+.
    /// Uses the AppKit window background material on macOS.
    /// </summary>
    Mica = 2,

    /// <summary>
    /// Acrylic effect - blurs content behind the window with tint.
    /// Available on Windows 11 22H2+.
    /// Uses the AppKit popover material on macOS.
    /// </summary>
    Acrylic = 3,

    /// <summary>
    /// Mica Alt effect - similar to Mica but with a different appearance.
    /// Available on Windows 11 22H2+.
    /// Uses the AppKit under-window background material on macOS.
    /// </summary>
    MicaAlt = 4
}

/// <summary>
/// Base class for backdrop effects providing common functionality.
/// </summary>
public abstract class BackdropEffect : IBackdropEffect
{
    private float _blurRadius;
    private float _blurSigma;
    private BackdropBlurType _blurType = BackdropBlurType.Gaussian;
    private float _noiseIntensity;
    private float _brightness = 1.0f;
    private float _contrast = 1.0f;
    private float _saturation = 1.0f;
    private float _hueRotation;
    private float _grayscale;
    private float _sepia;
    private float _invert;
    private float _opacity = 1.0f;
    private Color _tintColor = Color.Transparent;
    private float _tintOpacity;
    private float _luminosity = 1.0f;
    private int _deferChangeDepth;
    private bool _hasDeferredChange;

    /// <summary>Raised whenever a render-affecting backdrop parameter changes.</summary>
    public event EventHandler? EffectChanged;

    /// <inheritdoc />
    public virtual float BlurRadius
    {
        get => _blurRadius;
        set => SetField(ref _blurRadius, value);
    }

    /// <inheritdoc />
    public virtual float BlurSigma
    {
        get => _blurSigma;
        set => SetField(ref _blurSigma, value);
    }

    /// <inheritdoc />
    public virtual BackdropBlurType BlurType
    {
        get => _blurType;
        set => SetField(ref _blurType, value);
    }

    /// <inheritdoc />
    public virtual float NoiseIntensity
    {
        get => _noiseIntensity;
        set => SetField(ref _noiseIntensity, value);
    }

    /// <inheritdoc />
    public virtual float Brightness
    {
        get => _brightness;
        set => SetField(ref _brightness, value);
    }

    /// <inheritdoc />
    public virtual float Contrast
    {
        get => _contrast;
        set => SetField(ref _contrast, value);
    }

    /// <inheritdoc />
    public virtual float Saturation
    {
        get => _saturation;
        set => SetField(ref _saturation, value);
    }

    /// <inheritdoc />
    public virtual float HueRotation
    {
        get => _hueRotation;
        set => SetField(ref _hueRotation, value);
    }

    /// <inheritdoc />
    public virtual float Grayscale
    {
        get => _grayscale;
        set => SetField(ref _grayscale, value);
    }

    /// <inheritdoc />
    public virtual float Sepia
    {
        get => _sepia;
        set => SetField(ref _sepia, value);
    }

    /// <inheritdoc />
    public virtual float Invert
    {
        get => _invert;
        set => SetField(ref _invert, value);
    }

    /// <inheritdoc />
    public virtual float Opacity
    {
        get => _opacity;
        set => SetField(ref _opacity, value);
    }

    /// <summary>
    /// Gets or sets the tint color.
    /// </summary>
    public virtual Color TintColor
    {
        get => _tintColor;
        set => SetField(ref _tintColor, value);
    }

    /// <inheritdoc />
    public uint TintColorArgb => TintColor.ToArgb();

    /// <inheritdoc />
    public virtual float TintOpacity
    {
        get => _tintOpacity;
        set => SetField(ref _tintOpacity, value);
    }

    /// <inheritdoc />
    public virtual float Luminosity
    {
        get => _luminosity;
        set => SetField(ref _luminosity, value);
    }

    /// <inheritdoc />
    public virtual bool HasEffect =>
        BlurRadius > 0 ||
        NoiseIntensity > 0 ||
        Math.Abs(Brightness - 1.0f) > 0.001f ||
        Math.Abs(Contrast - 1.0f) > 0.001f ||
        Math.Abs(Saturation - 1.0f) > 0.001f ||
        Math.Abs(HueRotation) > 0.001f ||
        Grayscale > 0 ||
        Sepia > 0 ||
        Invert > 0 ||
        Math.Abs(Opacity - 1.0f) > 0.001f ||
        TintOpacity > 0 ||
        Math.Abs(Luminosity - 1.0f) > 0.001f;

    /// <summary>Defers change notifications while several related values are updated.</summary>
    protected void BeginEffectUpdate() => _deferChangeDepth++;

    /// <summary>Ends a deferred update and emits one consolidated notification.</summary>
    protected void EndEffectUpdate()
    {
        if (_deferChangeDepth <= 0)
            return;

        _deferChangeDepth--;
        if (_deferChangeDepth == 0 && _hasDeferredChange)
        {
            _hasDeferredChange = false;
            EffectChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetField<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        if (_deferChangeDepth > 0)
        {
            _hasDeferredChange = true;
        }
        else
        {
            EffectChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>
/// A simple blur effect.
/// </summary>
public sealed class BackdropBlurEffect : BackdropEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropBlurEffect"/> class.
    /// </summary>
    public BackdropBlurEffect()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropBlurEffect"/> class.
    /// </summary>
    /// <param name="radius">The blur radius in pixels.</param>
    /// <param name="blurType">The type of blur to apply.</param>
    public BackdropBlurEffect(float radius, BackdropBlurType blurType = BackdropBlurType.Gaussian)
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
[ContentProperty(nameof(Effects))]
public sealed class CompositeBackdropEffect : BackdropEffect
{
    private readonly ObservableCollection<IBackdropEffect> _effects = new();
    private readonly HashSet<BackdropEffect> _subscribedEffects = new();
    private bool _isUpdatingCombinedValues;

    /// <summary>Initializes an empty composite backdrop effect.</summary>
    public CompositeBackdropEffect()
    {
        _effects.CollectionChanged += OnEffectsCollectionChanged;
    }

    /// <summary>
    /// Gets the list of effects to combine.
    /// </summary>
    public ObservableCollection<IBackdropEffect> Effects => _effects;

    /// <summary>
    /// Adds an effect to the composite.
    /// </summary>
    public CompositeBackdropEffect Add(IBackdropEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effects.Add(effect);
        return this;
    }

    /// <summary>
    /// Removes an effect from the composite.
    /// </summary>
    public CompositeBackdropEffect Remove(IBackdropEffect effect)
    {
        _effects.Remove(effect);
        return this;
    }

    private void OnEffectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshChildSubscriptions();
        UpdateCombinedValues();
    }

    private void OnChildEffectChanged(object? sender, EventArgs e) => UpdateCombinedValues();

    private void RefreshChildSubscriptions()
    {
        var current = new HashSet<BackdropEffect>();
        for (int i = 0; i < _effects.Count; i++)
        {
            if (_effects[i] is BackdropEffect effect && !ReferenceEquals(effect, this))
                current.Add(effect);
        }

        foreach (var oldEffect in _subscribedEffects)
        {
            if (!current.Contains(oldEffect))
                oldEffect.EffectChanged -= OnChildEffectChanged;
        }

        foreach (var newEffect in current)
        {
            if (!_subscribedEffects.Contains(newEffect))
                newEffect.EffectChanged += OnChildEffectChanged;
        }

        _subscribedEffects.Clear();
        foreach (var effect in current)
            _subscribedEffects.Add(effect);
    }

    private void UpdateCombinedValues()
    {
        if (_isUpdatingCombinedValues)
            return;

        _isUpdatingCombinedValues = true;
        BeginEffectUpdate();
        try
        {
            // Reset to defaults.
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

            // Combine effects.
            foreach (var effect in _effects)
            {
                if (ReferenceEquals(effect, this))
                    continue;

                // For blur, use the maximum.
                if (effect.BlurRadius > BlurRadius)
                {
                    BlurRadius = effect.BlurRadius;
                    BlurSigma = effect.BlurSigma;
                    BlurType = effect.BlurType;
                }

                NoiseIntensity = Math.Max(NoiseIntensity, effect.NoiseIntensity);

                Brightness *= effect.Brightness;
                Contrast *= effect.Contrast;
                Saturation *= effect.Saturation;

                HueRotation += effect.HueRotation;
                HueRotation %= 2.0f * MathF.PI;

                Grayscale = Math.Max(Grayscale, effect.Grayscale);
                Sepia = Math.Max(Sepia, effect.Sepia);
                Invert = Math.Max(Invert, effect.Invert);

                Opacity *= effect.Opacity;

                if (effect.TintOpacity > 0)
                {
                    TintColor = Color.FromArgb(effect.TintColorArgb);
                    TintOpacity = effect.TintOpacity;
                }

                Luminosity *= effect.Luminosity;
            }
        }
        finally
        {
            try
            {
                EndEffectUpdate();
            }
            finally
            {
                _isUpdatingCombinedValues = false;
            }
        }
    }
}
