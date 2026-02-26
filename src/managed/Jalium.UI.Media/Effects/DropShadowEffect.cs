using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// A bitmap effect that paints a drop shadow around the target element.
/// </summary>
public sealed class DropShadowEffect : Effect
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the BlurRadius dependency property.
    /// </summary>
    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(nameof(BlurRadius), typeof(double), typeof(DropShadowEffect),
            new PropertyMetadata(5.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the Color dependency property.
    /// </summary>
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(Color), typeof(DropShadowEffect),
            new PropertyMetadata(Color.FromArgb(255, 0, 0, 0), OnPropertyChanged));

    /// <summary>
    /// Identifies the Direction dependency property.
    /// </summary>
    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(nameof(Direction), typeof(double), typeof(DropShadowEffect),
            new PropertyMetadata(315.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the Opacity dependency property.
    /// </summary>
    public static readonly DependencyProperty OpacityProperty =
        DependencyProperty.Register(nameof(Opacity), typeof(double), typeof(DropShadowEffect),
            new PropertyMetadata(1.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the ShadowDepth dependency property.
    /// </summary>
    public static readonly DependencyProperty ShadowDepthProperty =
        DependencyProperty.Register(nameof(ShadowDepth), typeof(double), typeof(DropShadowEffect),
            new PropertyMetadata(5.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the RenderingBias dependency property.
    /// </summary>
    public static readonly DependencyProperty RenderingBiasProperty =
        DependencyProperty.Register(nameof(RenderingBias), typeof(RenderingBias), typeof(DropShadowEffect),
            new PropertyMetadata(RenderingBias.Performance, OnPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value that indicates the radius of the shadow's blur effect.
    /// Default value is 5.
    /// </summary>
    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty)!;
        set => SetValue(BlurRadiusProperty, Math.Clamp(value, 0, 100));
    }

    /// <summary>
    /// Gets or sets the color of the shadow.
    /// Default value is Black.
    /// </summary>
    public Color Color
    {
        get => (Color)GetValue(ColorProperty)!;
        set => SetValue(ColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the direction of the shadow, in degrees.
    /// 0 is to the right, 90 is up, 180 is to the left, and 270 is down.
    /// Default value is 315 (down and to the right).
    /// </summary>
    public double Direction
    {
        get => (double)GetValue(DirectionProperty)!;
        set => SetValue(DirectionProperty, value % 360);
    }

    /// <summary>
    /// Gets or sets the opacity of the shadow.
    /// Default value is 1.0.
    /// </summary>
    public double Opacity
    {
        get => (double)GetValue(OpacityProperty)!;
        set => SetValue(OpacityProperty, Math.Clamp(value, 0, 1));
    }

    /// <summary>
    /// Gets or sets the distance between the object and the shadow that it casts.
    /// Default value is 5.
    /// </summary>
    public double ShadowDepth
    {
        get => (double)GetValue(ShadowDepthProperty)!;
        set => SetValue(ShadowDepthProperty, Math.Max(0, value));
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
    public override bool HasEffect => Opacity > 0 && (BlurRadius > 0 || ShadowDepth > 0);

    /// <inheritdoc />
    public override EffectType EffectType => EffectType.DropShadow;

    /// <inheritdoc />
    public override Thickness EffectPadding
    {
        get
        {
            // Calculate the padding needed to accommodate the shadow
            var padding = BlurRadius + ShadowDepth;
            var radians = Direction * Math.PI / 180.0;
            var offsetX = Math.Cos(radians) * ShadowDepth;
            var offsetY = -Math.Sin(radians) * ShadowDepth; // Negative because Y is inverted

            // Expand in all directions to ensure shadow is fully visible
            var left = padding + Math.Max(0, -offsetX);
            var top = padding + Math.Max(0, -offsetY);
            var right = padding + Math.Max(0, offsetX);
            var bottom = padding + Math.Max(0, offsetY);

            return new Thickness(left, top, right, bottom);
        }
    }

    /// <summary>
    /// Gets the X offset of the shadow based on Direction and ShadowDepth.
    /// </summary>
    public double OffsetX
    {
        get
        {
            var radians = Direction * Math.PI / 180.0;
            return Math.Cos(radians) * ShadowDepth;
        }
    }

    /// <summary>
    /// Gets the Y offset of the shadow based on Direction and ShadowDepth.
    /// Positive Y is down in screen coordinates.
    /// </summary>
    public double OffsetY
    {
        get
        {
            var radians = Direction * Math.PI / 180.0;
            return -Math.Sin(radians) * ShadowDepth;
        }
    }

    #endregion

    #region Property Changed

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DropShadowEffect effect)
        {
            effect.OnEffectChanged();
        }
    }

    #endregion
}

/// <summary>
/// Specifies the rendering quality preference for effects.
/// </summary>
public enum RenderingBias
{
    /// <summary>
    /// Rendering emphasizes performance over quality.
    /// </summary>
    Performance,

    /// <summary>
    /// Rendering emphasizes quality over performance.
    /// </summary>
    Quality
}
