using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// A bitmap effect that creates a raised or sunken appearance by simulating light
/// hitting the surface at a specified angle. This is the modern replacement for
/// the deprecated EmbossBitmapEffect.
/// </summary>
public sealed class EmbossEffect : Effect
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Amount dependency property.
    /// </summary>
    public static readonly DependencyProperty AmountProperty =
        DependencyProperty.Register(nameof(Amount), typeof(double), typeof(EmbossEffect),
            new PropertyMetadata(1.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the LightAngle dependency property.
    /// </summary>
    public static readonly DependencyProperty LightAngleProperty =
        DependencyProperty.Register(nameof(LightAngle), typeof(double), typeof(EmbossEffect),
            new PropertyMetadata(45.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the Relief dependency property.
    /// </summary>
    public static readonly DependencyProperty ReliefProperty =
        DependencyProperty.Register(nameof(Relief), typeof(double), typeof(EmbossEffect),
            new PropertyMetadata(0.44, OnPropertyChanged));

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(double), typeof(EmbossEffect),
            new PropertyMetadata(1.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the RenderingBias dependency property.
    /// </summary>
    public static readonly DependencyProperty RenderingBiasProperty =
        DependencyProperty.Register(nameof(RenderingBias), typeof(RenderingBias), typeof(EmbossEffect),
            new PropertyMetadata(RenderingBias.Performance, OnPropertyChanged));

    /// <summary>
    /// Identifies the LightColor dependency property.
    /// </summary>
    public static readonly DependencyProperty LightColorProperty =
        DependencyProperty.Register(nameof(LightColor), typeof(Color), typeof(EmbossEffect),
            new PropertyMetadata(Color.FromArgb(255, 255, 255, 255), OnPropertyChanged));

    /// <summary>
    /// Identifies the DarkColor dependency property.
    /// </summary>
    public static readonly DependencyProperty DarkColorProperty =
        DependencyProperty.Register(nameof(DarkColor), typeof(Color), typeof(EmbossEffect),
            new PropertyMetadata(Color.FromArgb(255, 0, 0, 0), OnPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the amount of the emboss effect.
    /// Higher values produce a stronger emboss. Default value is 1.0.
    /// The value range is [0, 10].
    /// </summary>
    public double Amount
    {
        get => (double)GetValue(AmountProperty)!;
        set => SetValue(AmountProperty, Math.Clamp(value, 0, 10));
    }

    /// <summary>
    /// Gets or sets the light angle in degrees.
    /// 0 is from the right, 90 is from the top, 180 is from the left, 270 is from the bottom.
    /// Default value is 45 (upper-right).
    /// </summary>
    public double LightAngle
    {
        get => (double)GetValue(LightAngleProperty)!;
        set => SetValue(LightAngleProperty, value % 360);
    }

    /// <summary>
    /// Gets or sets the relief (depth) of the emboss effect.
    /// Higher values create more pronounced depth. Default value is 0.44.
    /// The value range is [0, 1].
    /// </summary>
    public double Relief
    {
        get => (double)GetValue(ReliefProperty)!;
        set => SetValue(ReliefProperty, Math.Clamp(value, 0, 1));
    }

    /// <summary>
    /// Gets or sets the width of the emboss edges in pixels.
    /// Default value is 1.0. The value range is [0.5, 10].
    /// </summary>
    public double Width
    {
        get => (double)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, Math.Clamp(value, 0.5, 10));
    }

    /// <summary>
    /// Gets or sets the rendering bias for quality vs performance tradeoff.
    /// Default value is Performance.
    /// </summary>
    public RenderingBias RenderingBias
    {
        get => (RenderingBias)(GetValue(RenderingBiasProperty) ?? RenderingBias.Performance);
        set => SetValue(RenderingBiasProperty, value);
    }

    /// <summary>
    /// Gets or sets the color used for the lit (highlighted) edges.
    /// Default value is White.
    /// </summary>
    public Color LightColor
    {
        get => (Color)GetValue(LightColorProperty)!;
        set => SetValue(LightColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the color used for the shadowed edges.
    /// Default value is Black.
    /// </summary>
    public Color DarkColor
    {
        get => (Color)GetValue(DarkColorProperty)!;
        set => SetValue(DarkColorProperty, value);
    }

    #endregion

    #region Computed Properties

    /// <inheritdoc />
    public override bool HasEffect => Amount > 0;

    /// <inheritdoc />
    public override EffectType EffectType => EffectType.Emboss;

    /// <summary>
    /// Gets the light direction X component based on LightAngle.
    /// </summary>
    public double LightDirectionX
    {
        get
        {
            var radians = LightAngle * Math.PI / 180.0;
            return Math.Cos(radians);
        }
    }

    /// <summary>
    /// Gets the light direction Y component based on LightAngle.
    /// </summary>
    public double LightDirectionY
    {
        get
        {
            var radians = LightAngle * Math.PI / 180.0;
            return -Math.Sin(radians);
        }
    }

    /// <inheritdoc />
    public override Thickness EffectPadding
    {
        get
        {
            // Emboss effect may bleed slightly beyond the original bounds
            var padding = Width;
            return new Thickness(padding, padding, padding, padding);
        }
    }

    #endregion

    #region Property Changed

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmbossEffect effect)
        {
            effect.OnEffectChanged();
        }
    }

    #endregion
}
