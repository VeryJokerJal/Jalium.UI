using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// Provides a base class for all bitmap effect types.
/// Effects are applied to UIElement's rendered content to create visual effects like shadows and blurs.
/// This is different from BackdropEffect which blurs content behind an element.
/// </summary>
public abstract class Effect : DependencyObject, IEffect
{
    /// <summary>
    /// Gets a value indicating whether the effect produces any visual change.
    /// </summary>
    public abstract bool HasEffect { get; }

    /// <summary>
    /// Gets the effect type for native rendering dispatch.
    /// </summary>
    public abstract EffectType EffectType { get; }

    /// <summary>
    /// Gets the effect type identifier for native rendering dispatch.
    /// </summary>
    int IEffect.EffectTypeId => (int)EffectType;

    /// <summary>
    /// Gets the padding that this effect adds around the element bounds.
    /// This is used to expand the render area to accommodate the effect.
    /// </summary>
    public virtual Thickness EffectPadding => Thickness.Zero;

    /// <summary>
    /// Occurs when a property changes that affects the visual output of the effect.
    /// </summary>
    public event EventHandler? EffectChanged;

    /// <summary>
    /// Raises the EffectChanged event.
    /// </summary>
    protected void OnEffectChanged()
    {
        EffectChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Specifies the type of effect for native rendering.
/// </summary>
public enum EffectType
{
    /// <summary>
    /// No effect.
    /// </summary>
    None,

    /// <summary>
    /// Drop shadow effect.
    /// </summary>
    DropShadow,

    /// <summary>
    /// Blur effect applied to the element itself.
    /// </summary>
    Blur,

    /// <summary>
    /// Custom shader effect.
    /// </summary>
    Shader,

    /// <summary>
    /// Outer glow effect around the element.
    /// </summary>
    OuterGlow,

    /// <summary>
    /// Inner shadow effect inside the element bounds.
    /// </summary>
    InnerShadow,

    /// <summary>
    /// Emboss effect that creates a raised or sunken appearance.
    /// </summary>
    Emboss,

    /// <summary>
    /// Color matrix transformation effect.
    /// </summary>
    ColorMatrix,

    /// <summary>
    /// Group of multiple effects composed together.
    /// </summary>
    EffectGroup
}
