using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// A bitmap effect that blurs the target texture.
/// This is the WPF-compatible BlurEffect class.
/// </summary>
public sealed class BlurEffect : Effect
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Radius dependency property.
    /// </summary>
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.Register(nameof(Radius), typeof(double), typeof(BlurEffect),
            new PropertyMetadata(5.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the KernelType dependency property.
    /// </summary>
    public static readonly DependencyProperty KernelTypeProperty =
        DependencyProperty.Register(nameof(KernelType), typeof(KernelType), typeof(BlurEffect),
            new PropertyMetadata(KernelType.Gaussian, OnPropertyChanged));

    /// <summary>
    /// Identifies the RenderingBias dependency property.
    /// </summary>
    public static readonly DependencyProperty RenderingBiasProperty =
        DependencyProperty.Register(nameof(RenderingBias), typeof(RenderingBias), typeof(BlurEffect),
            new PropertyMetadata(RenderingBias.Performance, OnPropertyChanged));

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="BlurEffect"/> class.
    /// </summary>
    public BlurEffect()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BlurEffect"/> class with the specified radius.
    /// </summary>
    /// <param name="radius">The blur radius.</param>
    public BlurEffect(double radius)
    {
        Radius = radius;
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value that indicates the radius of the blur effect.
    /// The radius is the half-width of the Gaussian bell curve used to blur the image.
    /// Default value is 5. The value range is [0, infinity).
    /// </summary>
    public double Radius
    {
        get => (double)GetValue(RadiusProperty)!;
        set => SetValue(RadiusProperty, Math.Max(0, value));
    }

    /// <summary>
    /// Gets or sets the type of kernel used in the blur calculation.
    /// Default value is Gaussian.
    /// </summary>
    public KernelType KernelType
    {
        get => (KernelType)(GetValue(KernelTypeProperty) ?? KernelType.Gaussian);
        set => SetValue(KernelTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating the bias between quality and performance.
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
            // Blur expands equally in all directions by the radius amount
            var padding = Radius;
            return new Thickness(padding, padding, padding, padding);
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the render bounds after applying the blur effect.
    /// </summary>
    /// <param name="contentBounds">The original content bounds.</param>
    /// <returns>The expanded bounds to accommodate the blur effect.</returns>
    public Rect GetRenderBounds(Rect contentBounds)
    {
        var radius = Radius;
        return new Rect(
            contentBounds.X - radius,
            contentBounds.Y - radius,
            contentBounds.Width + radius * 2,
            contentBounds.Height + radius * 2);
    }

    #endregion

    #region Property Changed

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BlurEffect effect)
        {
            effect.OnEffectChanged();
        }
    }

    #endregion
}
