using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Effects;

/// <summary>Base class for effects applied to rendered element content.</summary>
public abstract class Effect : Animatable, IEffect
{
    private static readonly Brush s_implicitInput = new ImplicitEffectInputBrush();
    private static readonly GeneralTransform s_identityMapping = new IdentityEffectTransform();

    // CSS shadow values specify either a blur radius (box-shadow) or a Gaussian
    // standard deviation (filter drop-shadow). Retain the latter through clones
    // so the drawing context can adapt to each native backend's blur parameter.
    internal static readonly DependencyProperty CssGaussianSigmaProperty =
        DependencyProperty.Register(nameof(CssGaussianSigma), typeof(double), typeof(Effect),
            new PropertyMetadata(double.NaN, static (d, _) => ((Effect)d).OnEffectChanged()));

    internal double CssGaussianSigma
    {
        get => (double)GetValue(CssGaussianSigmaProperty)!;
        set => SetValue(CssGaussianSigmaProperty, value);
    }

    // A standalone CSS box-shadow paints between an element's background and
    // descendants. Keep the source marker through Freezable clones so the
    // visual can complete its background capture before rendering children.
    internal static readonly DependencyProperty CssBoxShadowLayerProperty =
        DependencyProperty.Register(nameof(CssBoxShadowLayer), typeof(bool), typeof(Effect),
            new PropertyMetadata(false, static (d, _) => ((Effect)d).OnEffectChanged()));

    internal bool CssBoxShadowLayer
    {
        get => (bool)GetValue(CssBoxShadowLayerProperty)!;
        set => SetValue(CssBoxShadowLayerProperty, value);
    }

    // CSS filter covers the complete painted group, including its outline.
    // Keep this source marker through clones without changing native Effect
    // behavior for XAML callers.
    internal static readonly DependencyProperty CssFilterLayerProperty =
        DependencyProperty.Register(nameof(CssFilterLayer), typeof(bool), typeof(Effect),
            new PropertyMetadata(false, static (d, _) => ((Effect)d).OnEffectChanged()));

    internal bool CssFilterLayer
    {
        get => (bool)GetValue(CssFilterLayerProperty)!;
        set => SetValue(CssFilterLayerProperty, value);
    }

    /// <summary>
    /// Gets the special brush that samples the element to which the effect is applied.
    /// </summary>
    [Browsable(false)]
    public static Brush ImplicitInput => s_implicitInput;

    /// <summary>
    /// Gets the mapping from effect output space to input space. The default mapping is identity.
    /// </summary>
    protected internal virtual GeneralTransform EffectMapping => s_identityMapping;

    /// <summary>Creates a modifiable clone of this effect.</summary>
    public new Effect Clone() => (Effect)base.Clone();

    /// <summary>Creates a modifiable clone using the current values of this effect.</summary>
    public new Effect CloneCurrentValue() => (Effect)base.CloneCurrentValue();

    public abstract bool HasEffect { get; }

    public abstract EffectType EffectType { get; }

    int IEffect.EffectTypeId => (int)EffectType;

    public virtual Thickness EffectPadding => Thickness.Zero;

    public event EventHandler? EffectChanged;

    /// <summary>Signals that render state derived from this effect must be refreshed.</summary>
    protected void OnEffectChanged() => WritePostscript();

    /// <inheritdoc />
    protected override void OnChanged()
    {
        base.OnChanged();
        EffectChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates the concrete effect instance. Effects conventionally expose a parameterless
    /// constructor, including protected constructors on custom shader-effect types.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Concrete Effect implementations preserve a parameterless constructor as part of the Freezable cloning contract.")]
    protected override Freezable CreateInstanceCore()
    {
        return (Freezable)(Activator.CreateInstance(GetType(), nonPublic: true) ??
            throw new InvalidOperationException($"Effect type '{GetType().FullName}' must have a parameterless constructor."));
    }

    private sealed class ImplicitEffectInputBrush : Brush
    {
        protected override Freezable CreateInstanceCore() => new ImplicitEffectInputBrush();
    }

    private sealed class IdentityEffectTransform : GeneralTransform
    {
        public override GeneralTransform Inverse => this;

        public override Point Transform(Point inPoint) => inPoint;

        public override bool TryTransform(Point inPoint, out Point result)
        {
            result = inPoint;
            return true;
        }

        public override Rect TransformBounds(Rect rect) => rect;
    }
}

public enum EffectType
{
    None,
    DropShadow,
    Blur,
    Shader,
    OuterGlow,
    InnerShadow,
    Emboss,
    ColorMatrix,
    EffectGroup,
}
