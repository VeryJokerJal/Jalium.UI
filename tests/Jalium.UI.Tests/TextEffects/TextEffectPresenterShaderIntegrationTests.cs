using Jalium.UI;
using Jalium.UI.Controls.TextEffects;
using Jalium.UI.Controls.TextEffects.Effects;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Tests.TextEffects;

/// <summary>
/// Covers the split between <see cref="TextEffectPresenter.TextEffect"/> (CPU
/// per-cell animation) and <see cref="UIElement.Effect"/> (whole-element GPU
/// pass: BlurEffect, ShaderEffect, etc.). The two properties must be independent
/// — setting one must never clobber the other.
/// </summary>
public class TextEffectPresenterShaderIntegrationTests
{
    [Fact]
    public void TextEffect_And_UIElementEffect_AreIndependent()
    {
        var p = new TextEffectPresenter();
        var originalTextEffect = p.TextEffect;
        Assert.NotNull(originalTextEffect);

        var blur = new BlurEffect(8);
        p.Effect = blur;

        // Setting the GPU effect must not touch the per-cell animation driver.
        Assert.Same(originalTextEffect, p.TextEffect);
        Assert.Same(blur, p.Effect);
    }

    [Fact]
    public void UIElementEffect_AcceptsFrameworkEffects()
    {
        // Regression: PR1 accidentally shadowed UIElement.Effect with an
        // ITextEffect-typed property, which made `presenter.Effect = blur`
        // silently fail. This test locks that behaviour down.
        var p = new TextEffectPresenter();

        p.Effect = new BlurEffect(4);
        Assert.IsType<BlurEffect>(p.Effect);
    }

    [Fact]
    public void UIElementEffect_AcceptsShaderEffect()
    {
        // ShaderEffect itself is abstract (users subclass to bind their own HLSL),
        // but any concrete subclass should plug directly into UIElement.Effect.
        var p = new TextEffectPresenter();
        var shader = new TestShaderEffect();

        p.Effect = shader;

        Assert.Same(shader, p.Effect);
    }

    [Fact]
    public void ClearingUIElementEffect_LeavesTextEffectIntact()
    {
        var p = new TextEffectPresenter();
        p.Effect = new BlurEffect(4);

        p.Effect = null;

        Assert.Null(p.Effect);
        Assert.IsType<RiseSettleEffect>(p.TextEffect);
    }

    [Fact]
    public void ClearingTextEffect_LeavesUIElementEffectIntact()
    {
        var p = new TextEffectPresenter();
        var blur = new BlurEffect(4);
        p.Effect = blur;

        p.TextEffect = null;

        Assert.Same(blur, p.Effect);
        Assert.Null(p.TextEffect);
    }

    [Fact]
    public void TextEffectPropertyIdentity_IsDistinctFromUIElementEffectProperty()
    {
        // The two DPs must be separate — otherwise a value written to one would
        // be visible via the other's property system slot.
        Assert.NotSame(TextEffectPresenter.TextEffectProperty, UIElement.EffectProperty);
    }

    private sealed class TestShaderEffect : ShaderEffect
    {
        // Empty test fixture. Real users subclass ShaderEffect and provide a
        // PixelShader (DXBC bytecode via PixelShader.SetStreamSource) plus
        // DependencyProperties wired to shader constant registers via
        // ShaderEffect.PixelShaderConstantCallback.
    }
}
