using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Effects;

/// <summary>Base class for effects applied to rendered element content.</summary>
public abstract class Effect : Animatable, IEffect
{
    private static readonly Brush s_implicitInput = new ImplicitEffectInputBrush();
    private static readonly GeneralTransform s_identityMapping = new IdentityEffectTransform();

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
