using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// [Deprecated] Base class for bitmap effects.
/// </summary>
[Obsolete("BitmapEffect is deprecated. Use Effect-derived classes instead.")]
public abstract class BitmapEffect : DependencyObject
{
    protected BitmapEffect() { }

    /// <summary>
    /// Creates a modifiable clone of the current value of this BitmapEffect.
    /// </summary>
    public BitmapEffect? CloneCurrentValue() => (BitmapEffect?)MemberwiseClone();
}

/// <summary>
/// [Deprecated] Applies a blur effect to a bitmap.
/// </summary>
[Obsolete("Use BlurEffect instead.")]
public sealed class BlurBitmapEffect : BitmapEffect
{
    /// <summary>
    /// Gets or sets the blur radius.
    /// </summary>
    public double Radius { get; set; } = 5.0;

    /// <summary>
    /// Gets or sets the kernel type for the blur.
    /// </summary>
    public KernelType KernelType { get; set; } = KernelType.Gaussian;
}

/// <summary>
/// [Deprecated] Applies a drop shadow effect to a bitmap.
/// </summary>
[Obsolete("Use DropShadowEffect instead.")]
public sealed class DropShadowBitmapEffect : BitmapEffect
{
    /// <summary>
    /// Gets or sets the depth of the shadow.
    /// </summary>
    public double ShadowDepth { get; set; } = 5.0;

    /// <summary>
    /// Gets or sets the shadow color.
    /// </summary>
    public Color Color { get; set; } = Colors.Black;

    /// <summary>
    /// Gets or sets the softness of the shadow.
    /// </summary>
    public double Softness { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the direction of the shadow in degrees.
    /// </summary>
    public double Direction { get; set; } = 315.0;

    /// <summary>
    /// Gets or sets the opacity of the shadow.
    /// </summary>
    public double Opacity { get; set; } = 1.0;
}

/// <summary>
/// [Deprecated] Applies a bevel effect to a bitmap.
/// </summary>
[Obsolete("BitmapEffect is deprecated.")]
public sealed class BevelBitmapEffect : BitmapEffect
{
    /// <summary>
    /// Gets or sets the width of the bevel.
    /// </summary>
    public double BevelWidth { get; set; } = 5.0;

    /// <summary>
    /// Gets or sets the relief amount.
    /// </summary>
    public double Relief { get; set; } = 0.3;

    /// <summary>
    /// Gets or sets the light angle in degrees.
    /// </summary>
    public double LightAngle { get; set; } = 135.0;

    /// <summary>
    /// Gets or sets the smoothness of the bevel.
    /// </summary>
    public double Smoothness { get; set; } = 0.2;

    /// <summary>
    /// Gets or sets the edge profile of the bevel.
    /// </summary>
    public EdgeProfile EdgeProfile { get; set; } = EdgeProfile.Linear;
}

/// <summary>
/// [Deprecated] Applies an emboss effect to a bitmap.
/// </summary>
[Obsolete("BitmapEffect is deprecated.")]
public sealed class EmbossBitmapEffect : BitmapEffect
{
    /// <summary>
    /// Gets or sets the emboss amount.
    /// </summary>
    public double Amount { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the light angle in degrees.
    /// </summary>
    public double LightAngle { get; set; } = 45.0;
}

/// <summary>
/// [Deprecated] Applies an outer glow effect to a bitmap.
/// </summary>
[Obsolete("BitmapEffect is deprecated.")]
public sealed class OuterGlowBitmapEffect : BitmapEffect
{
    /// <summary>
    /// Gets or sets the glow size.
    /// </summary>
    public double GlowSize { get; set; } = 5.0;

    /// <summary>
    /// Gets or sets the glow color.
    /// </summary>
    public Color GlowColor { get; set; } = Colors.Gold;

    /// <summary>
    /// Gets or sets the opacity of the glow.
    /// </summary>
    public double Opacity { get; set; } = 1.0;
}

/// <summary>
/// [Deprecated] Groups multiple BitmapEffect objects into a single composite effect.
/// </summary>
[Obsolete("Use Effect-derived classes instead.")]
#pragma warning disable CS0618 // Type or member is obsolete
public sealed class BitmapEffectGroup : BitmapEffect
{
    /// <summary>
    /// Gets the children of this effect group.
    /// </summary>
    public List<BitmapEffect> Children { get; } = new();
}
#pragma warning restore CS0618

/// <summary>
/// [Deprecated] Defines the input for a bitmap effect.
/// </summary>
[Obsolete("BitmapEffect is deprecated.")]
public sealed class BitmapEffectInput : DependencyObject
{
    /// <summary>
    /// Gets or sets the input bitmap source.
    /// </summary>
    public BitmapSource? Input { get; set; }

    /// <summary>
    /// Gets or sets the area to apply the effect.
    /// </summary>
    public Rect AreaToApplyEffect { get; set; } = Rect.Empty;

    /// <summary>
    /// Gets or sets whether the area is specified in absolute or relative units.
    /// </summary>
    public bool AreaToApplyEffectUnits { get; set; }
}

/// <summary>
/// Specifies the edge profile for a BevelBitmapEffect.
/// </summary>
public enum EdgeProfile
{
    /// <summary>
    /// A linear edge.
    /// </summary>
    Linear,

    /// <summary>
    /// A curved-in edge.
    /// </summary>
    CurvedIn,

    /// <summary>
    /// A curved-out edge.
    /// </summary>
    CurvedOut,

    /// <summary>
    /// A bulged-up edge.
    /// </summary>
    BulgedUp
}

/// <summary>
/// Represents an implicit input brush used in shader effects.
/// This brush takes its input from the element being rendered.
/// </summary>
public sealed class ImplicitInputBrush : Brush
{
}
