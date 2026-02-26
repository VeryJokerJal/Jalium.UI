using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// A bitmap effect that blurs the element content itself.
/// This is different from BackdropEffect's BlurEffect which blurs content behind the element.
/// </summary>
public sealed class ElementBlurEffect : Effect
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Radius dependency property.
    /// </summary>
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.Register(nameof(Radius), typeof(double), typeof(ElementBlurEffect),
            new PropertyMetadata(5.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the KernelType dependency property.
    /// </summary>
    public static readonly DependencyProperty KernelTypeProperty =
        DependencyProperty.Register(nameof(KernelType), typeof(KernelType), typeof(ElementBlurEffect),
            new PropertyMetadata(KernelType.Gaussian, OnPropertyChanged));

    /// <summary>
    /// Identifies the RenderingBias dependency property.
    /// </summary>
    public static readonly DependencyProperty RenderingBiasProperty =
        DependencyProperty.Register(nameof(RenderingBias), typeof(RenderingBias), typeof(ElementBlurEffect),
            new PropertyMetadata(RenderingBias.Performance, OnPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the blur radius.
    /// A larger radius produces more blur. Default value is 5.
    /// </summary>
    public double Radius
    {
        get => (double)GetValue(RadiusProperty)!;
        set => SetValue(RadiusProperty, Math.Clamp(value, 0, 100));
    }

    /// <summary>
    /// Gets or sets the kernel type used for blur calculation.
    /// Default value is Gaussian.
    /// </summary>
    public KernelType KernelType
    {
        get => (KernelType)(GetValue(KernelTypeProperty) ?? KernelType.Gaussian);
        set => SetValue(KernelTypeProperty, value);
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

    #endregion

    #region Computed Properties

    /// <inheritdoc />
    public override bool HasEffect => Radius > 0;

    /// <inheritdoc />
    public override EffectType EffectType => EffectType.Blur;

    /// <inheritdoc />
    public override Thickness EffectPadding
    {
        get
        {
            // Blur expands equally in all directions
            var padding = Radius;
            return new Thickness(padding, padding, padding, padding);
        }
    }

    #endregion

    #region Property Changed

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ElementBlurEffect effect)
        {
            effect.OnEffectChanged();
        }
    }

    #endregion
}

/// <summary>
/// Specifies the type of kernel used for blur effect calculation.
/// </summary>
public enum KernelType
{
    /// <summary>
    /// Gaussian blur kernel. Produces a smooth, natural-looking blur.
    /// </summary>
    Gaussian,

    /// <summary>
    /// Box blur kernel. Faster but produces a less smooth blur.
    /// </summary>
    Box
}
