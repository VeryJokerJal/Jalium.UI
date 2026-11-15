using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontVariantTests
{
    [Theory]
    [InlineData("normal")] [InlineData("none")] [InlineData("small-caps")] [InlineData("text")]
    [InlineData("common-ligatures small-caps discretionary-ligatures")]
    [InlineData("ordinal jis78 slashed-zero")]
    [InlineData("stylistic(a) small-caps styleset(b)")]
    [InlineData("common-ligatures no-discretionary-ligatures historical-ligatures no-contextual all-small-caps historical-forms stylistic(foo) styleset(a,b) character-variant(c) swash(d) ornaments(e) annotation(f) oldstyle-nums tabular-nums diagonal-fractions ordinal slashed-zero jis78 full-width ruby super unicode")]
    [InlineData("e\\6d oji/**/s\\75 per/**/all-small-caps")]
    public void ValidGrammarExpandsEveryLonghand(string value)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-variant", value));
        var descriptor = CssPropertyRegistry.LookupForCompile("font-variant", out _)!;
        var reader = new CssTokenReader(value); var output = new List<CssCompiledDeclaration>();
        Assert.True(descriptor.Expand!(ref reader, new(), output)); Assert.True(reader.AtEnd);
        Assert.Equal(CssFontVariant.Longhands, output.Select(declaration => declaration.Name));
    }

    [Theory]
    [InlineData("")] [InlineData("normal none")] [InlineData("normal emoji")] [InlineData("none sub")]
    [InlineData("sub super")] [InlineData("small-caps unicase")] [InlineData("emoji text")]
    [InlineData("common-ligatures no-common-ligatures")] [InlineData("ordinal jis78 ordinal")]
    [InlineData("ruby sub ruby")] [InlineData("stylistic(a) emoji stylistic(b)")]
    [InlineData("styleset()")] [InlineData("swash(a,b)")] [InlineData("normal()")] [InlineData("emoji,sub")]
    public void InvalidGrammarIsTransactional(string value)
    {
        Assert.False(CssAuthoring.IsValueSupported("font-variant", value));
        var descriptor = CssPropertyRegistry.LookupForCompile("font-variant", out _)!;
        var reader = new CssTokenReader(value); var output = new List<CssCompiledDeclaration>();
        Assert.False(descriptor.Expand!(ref reader, new(), output)); Assert.Empty(output);
        var text = new TextBlock(); Style(text, "font-variant:small-caps emoji;font-variant:" + value);
        Assert.Equal(CssEmojiVariant.Emoji, text.GetValue(CssFontEmoji.VariantProperty));
        Assert.Equal(CssCapsVariant.SmallCaps, text.GetValue(CssFontCaps.VariantProperty));
    }

    [Theory] [InlineData("normal", 0)] [InlineData("none", 256)] [InlineData("super", 0)]
    public void UnspecifiedComponentsResetWithoutTouchingOtherFontControls(string value, int ligatures)
    {
        var text = new TextBlock();
        Style(text, "font-variant:small-caps oldstyle-nums ruby historical-forms emoji;font-kerning:none;font-synthesis-small-caps:none;font-synthesis-position:none;font-feature-settings:'kern' on;font-variant:" + value);
        Assert.Equal(CssCapsVariant.Normal, text.GetValue(CssFontCaps.VariantProperty));
        Assert.Equal(CssNumericVariant.Normal, text.GetValue(CssFontNumeric.VariantProperty));
        Assert.Equal(CssEastAsianVariant.Normal, text.GetValue(CssFontEastAsian.VariantProperty));
        Assert.Equal(CssFontAlternates.Normal, text.GetValue(CssFontAlternates.VariantProperty));
        Assert.Equal(CssEmojiVariant.Normal, text.GetValue(CssFontEmoji.VariantProperty));
        Assert.Equal((CssLigatureVariant)ligatures, text.GetValue(CssFontLigatures.VariantProperty));
        Assert.Equal(CssFontKerning.None, text.GetValue(CssFontLigatures.KerningProperty));
        Assert.False((bool)text.GetValue(CssFontCaps.SynthesisProperty)!);
        Assert.False((bool)text.GetValue(CssFontPosition.SynthesisProperty)!);
        Assert.Equal(1u, Assert.Single(CssFontFeatureSettings.ForRendering(text), feature => feature.Tag == FontFeature.TagOf("kern")).Value);
    }

    [Fact]
    public void VariablesImportanceAndComputedInvalidityApplyToAllSevenLonghands()
    {
        var text = new TextBlock(); var root = new Border { Child = text };
        Style(root, "font-variant:all-small-caps sub emoji;--variant:small-caps super text");
        Style(text, "font-variant:var(--variant)!important;font-variant-emoji:normal"); Evaluate(root);
        Assert.Equal(CssEmojiVariant.Text, text.GetValue(CssFontEmoji.VariantProperty));
        Assert.Equal(CssPositionVariant.Super, text.GetValue(CssFontPosition.VariantProperty));
        Style(text, "font-variant:var(--missing)");
        Assert.Equal(CssEmojiVariant.Emoji, text.GetValue(CssFontEmoji.VariantProperty));
        Assert.Equal(CssCapsVariant.AllSmallCaps, text.GetValue(CssFontCaps.VariantProperty));
        Assert.Equal(CssPositionVariant.Sub, text.GetValue(CssFontPosition.VariantProperty));
        Assert.True(CssOrdinaryStyleQuery.Matches("font-variant", "all-small-caps sub emoji", CssNode.Get(text), default));
        Style(text, "font-variant:initial");
        Assert.True(CssOrdinaryStyleQuery.Matches("font-variant", "normal", CssNode.Get(text), default));
    }

    private static void Style(DependencyObject owner, string css) { Css.SetStyle(owner, css); CssEngine.EvaluateElement(CssNode.Get(owner)); }
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
}
