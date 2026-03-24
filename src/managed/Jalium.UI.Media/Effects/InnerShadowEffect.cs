using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// A bitmap effect that renders a shadow inside the bounds of the target element.
/// Similar to CSS box-shadow with the inset keyword.
/// </summary>
public sealed class InnerShadowEffect : Effect
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the BlurRadius dependency property.
    /// </summary>
    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(nameof(BlurRadius), typeof(double), typeof(InnerShadowEffect),
            new PropertyMetadata(5.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the Color dependency property.
    /// </summary>
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(Color), typeof(InnerShadowEffect),
            new PropertyMetadata(Color.FromArgb(128, 0, 0, 0), OnPropertyChanged));

    /// <summary>
    /// Identifies the Direction dependency property.
    /// </summary>
    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(nameof(Direction), typeof(double), typeof(InnerShadowEffect),
            new PropertyMetadata(315.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the Opacity dependency property.
    /// </summary>
    public static readonly DependencyProperty OpacityProperty =
        DependencyProperty.Register(nameof(Opacity), typeof(double), typeof(InnerShadowEffect),
            new PropertyMetadata(1.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the ShadowDepth dependency property.
    /// </summary>
    public static readonly DependencyProperty ShadowDepthProperty =
        DependencyProperty.Register(nameof(ShadowDepth), typeof(double), typeof(InnerShadowEffect),
            new PropertyMetadata(3.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the SpreadRadius dependency property.
    /// </summary>
    public static readonly DependencyProperty SpreadRadiusProperty =
        DependencyProperty.Register(nameof(SpreadRadius), typeof(double), typeof(InnerShadowEffect),
            new PropertyMetadata(0.0, OnPropertyChanged));

    /// <summary>
    /// Identifies the CornerRadius dependency property.
    /// </summary>
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(InnerShadowEffect),
            new PropertyMetadata(0.0, OnPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the blur radius of the inner shadow.
    /// A larger value produces a softer shadow. Default value is 5.
    /// </summary>
    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty)!;
        set => SetValue(BlurRadiusProperty, Math.Clamp(value, 0, 100));
    }

    /// <summary>
    /// Gets or sets the color of the inner shadow.
    /// Default value is semi-transparent black (128, 0, 0, 0).
    /// </summary>
    public Color Color
    {
        get => (Color)GetValue(ColorProperty)!;
        set => SetValue(ColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the direction of the inner shadow, in degrees.
    /// 0 is to the right, 90 is up, 180 is to the left, and 270 is down.
    /// Default value is 315 (light from upper-left, shadow at bottom-right).
    /// </summary>
    public double Direction
    {
        get => (double)GetValue(DirectionProperty)!;
        set => SetValue(DirectionProperty, value % 360);
    }

    /// <summary>
    /// Gets or sets the opacity of the inner shadow.
    /// Default value is 1.0.
    /// </summary>
    public double Opacity
    {
        get => (double)GetValue(OpacityProperty)!;
        set => SetValue(OpacityProperty, Math.Clamp(value, 0, 1));
    }

    /// <summary>
    /// Gets or sets the depth of the inner shadow offset.
    /// Default value is 3.
    /// </summary>
    public double ShadowDepth
    {
        get => (double)GetValue(ShadowDepthProperty)!;
        set => SetValue(ShadowDepthProperty, Math.Max(0, value));
    }

    /// <summary>
    /// Gets or sets the spread radius of the inner shadow.
    /// A positive value expands the shadow inward, negative contracts it.
    /// Default value is 0.
    /// </summary>
    public double SpreadRadius
    {
        get => (double)GetValue(SpreadRadiusProperty)!;
        set => SetValue(SpreadRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius for the inner shadow shape.
    /// Should match the element's corner radius for proper rendering.
    /// Default value is 0.
    /// </summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, Math.Max(0, value));
    }

    #endregion

    #region Computed Properties

    /// <inheritdoc />
    public override bool HasEffect => Opacity > 0 && (BlurRadius > 0 || ShadowDepth > 0 || SpreadRadius != 0);

    /// <inheritdoc />
    public override EffectType EffectType => EffectType.InnerShadow;

    /// <summary>
    /// Gets the X offset of the inner shadow based on Direction and ShadowDepth.
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
    /// Gets the Y offset of the inner shadow based on Direction and ShadowDepth.
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

    /// <inheritdoc />
    public override Thickness EffectPadding
    {
        get
        {
            // Inner shadow is rendered inside the element bounds,
            // so it does not add external padding.
            return Thickness.Zero;
        }
    }

    #endregion

    #region Property Changed

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InnerShadowEffect effect)
        {
            effect.OnEffectChanged();
        }
    }

    #endregion
}
