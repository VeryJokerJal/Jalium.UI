using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// Retains a CSS filter's ordered color matrices so every stage can clamp its
/// output before the next stage reads it.
/// </summary>
internal sealed class CssColorMatrixChainEffect : Effect
{
    internal static readonly DependencyProperty MatricesProperty =
        DependencyProperty.Register(nameof(Matrices), typeof(ColorMatrix[]),
            typeof(CssColorMatrixChainEffect),
            new PropertyMetadata(Array.Empty<ColorMatrix>(), static (d, _) =>
                ((CssColorMatrixChainEffect)d).OnEffectChanged()));

    internal CssColorMatrixChainEffect() { }

    internal CssColorMatrixChainEffect(IReadOnlyList<ColorMatrix> matrices)
        => Matrices = matrices.ToArray();

    internal ColorMatrix[] Matrices
    {
        get => (ColorMatrix[])GetValue(MatricesProperty)!;
        private set => SetValue(MatricesProperty, value);
    }

    public override bool HasEffect => Matrices.Any(static matrix => !matrix.IsIdentity);

    public override EffectType EffectType => EffectType.ColorMatrix;

    protected override Freezable CreateInstanceCore() => new CssColorMatrixChainEffect();
}
