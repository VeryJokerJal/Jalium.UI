using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// A bitmap effect that renders a glow around the outer edges of the target element.
/// This is the modern replacement for the deprecated OuterGlowBitmapEffect.
/// </summary>
public sealed class OuterGlowEffect : Effect
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the GlowSize dependency property.
    /// </summary>
    public static readonly DependencyProperty GlowSizeProperty =
        DependencyProperty.Register(nameof(GlowSize), typeof(double), typeof(OuterGlowEffect),
            new PropertyMetadata(5.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the GlowColor dependency property.
    /// </summary>
    public static readonly DependencyProperty GlowColorProperty =
        DependencyProperty.Register(nameof(GlowColor), typeof(Color), typeof(OuterGlowEffect),
            new PropertyMetadata(Color.FromArgb(255, 255, 215, 0), OnPropertyChanged)); // Gold

    /// <summary>
    /// Identifies the Opacity dependency property.
    /// </summary>
    public static readonly DependencyProperty OpacityProperty =
        DependencyProperty.Register(nameof(Opacity), typeof(double), typeof(OuterGlowEffect),
            new PropertyMetadata(1.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the BlurRadius dependency property.
    /// </summary>
    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(nameof(BlurRadius), typeof(double), typeof(OuterGlowEffect),
            new PropertyMetadata(0.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the Intensity dependency property.
    /// </summary>
    public static readonly DependencyProperty IntensityProperty =
        DependencyProperty.Register(nameof(Intensity), typeof(double), typeof(OuterGlowEffect),
            new PropertyMetadata(1.0, OnPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the size of the glow around the element.
    /// Default value is 5. The value range is [0, 100].
    /// </summary>
    public double GlowSize
    {
        get => (double)GetValue(GlowSizeProperty)!;
        set => SetValue(GlowSizeProperty, Math.Clamp(value, 0, 100));
    }

    /// <summary>
    /// Gets or sets the color of the glow.
    /// Default value is Gold (255, 215, 0).
    /// </summary>
    public Color GlowColor
    {
        get => (Color)GetValue(GlowColorProperty)!;
        set => SetValue(GlowColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the opacity of the glow effect.
    /// Default value is 1.0. The value range is [0, 1].
    /// </summary>
    public double Opacity
    {
        get => (double)GetValue(OpacityProperty)!;
        set => SetValue(OpacityProperty, Math.Clamp(value, 0, 1));
    }

    /// <summary>
    /// Gets or sets the blur radius applied to the glow.
    /// A larger value produces a softer glow. Default value is 0 (uses GlowSize as blur).
    /// When 0, the blur radius is automatically derived from GlowSize.
    /// </summary>
    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty)!;
        set => SetValue(BlurRadiusProperty, Math.Max(0, value));
    }

    /// <summary>
    /// Gets or sets the intensity multiplier for the glow.
    /// Values greater than 1.0 produce a brighter glow. Default value is 1.0.
    /// The value range is [0, 5].
    /// </summary>
    public double Intensity
    {
        get => (double)GetValue(IntensityProperty)!;
        set => SetValue(IntensityProperty, Math.Clamp(value, 0, 5));
    }

    #endregion

    #region Computed Properties

    /// <inheritdoc />
    public override bool HasEffect => Opacity > 0 && GlowSize > 0;

    /// <inheritdoc />
    public override EffectType EffectType => EffectType.OuterGlow;

    /// <summary>
    /// Gets the effective blur radius. If BlurRadius is 0, uses GlowSize as the blur.
    /// </summary>
    public double EffectiveBlurRadius => BlurRadius > 0 ? BlurRadius : GlowSize;

    /// <inheritdoc />
    public override Thickness EffectPadding
    {
        get
        {
            // Glow expands equally in all directions by the glow size plus any additional blur
            var padding = GlowSize + EffectiveBlurRadius;
            return new Thickness(padding, padding, padding, padding);
        }
    }

    #endregion

    #region Property Changed

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OuterGlowEffect effect)
        {
            effect.OnEffectChanged();
        }
    }

    #endregion
}
