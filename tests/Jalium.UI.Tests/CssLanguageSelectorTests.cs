using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssLanguageSelectorTests
{
    static CssLanguageSelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":lang(en)", true)]
    [InlineData(":lang(fr, \"*-CH\")", true)]
    [InlineData(":lang(\\*)", true)]
    [InlineData(":lang(\"\")", true)]
    [InlineData(":lang(/* comment */ en /**/, fr)", true)]
    [InlineData(":lang()", false)]
    [InlineData(":lang(en,)", false)]
    [InlineData(":lang(, en)", false)]
    [InlineData(":lang(*)", false)]
    [InlineData(":lang(en fr)", false)]
    [InlineData(":lang(en, 3)", false)]
    public void SelectorSupport_ValidatesLanguageRangeTokens(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Theory]
    [InlineData(":lang(de-DE)", "de-DE", true)]
    [InlineData(":lang(DE-de)", "de-DE", true)]
    [InlineData(":lang(de-DE)", "de-Latn-DE", true)]
    [InlineData(":lang(de-DE)", "de-Latn-DE-1996", true)]
    [InlineData(":lang(de-DE)", "de-x-DE", false)]
    [InlineData(":lang(de-DE)", "de-Deva", false)]
    [InlineData(":lang(\"*-CH\")", "fr-CH", true)]
    [InlineData(":lang(\"*-CH\")", "de-Latn-CH", true)]
    [InlineData(":lang(\"*-CH\")", "de-x-CH", false)]
    [InlineData(":lang(fr, \"de-CH\")", "de-Latn-CH", true)]
    [InlineData(":lang(fr, \"de-CH\")", "es-CH", false)]
    [InlineData(":lang(\"*\")", "und", true)]
    [InlineData(":lang(\"*\")", "", false)]
    [InlineData(":lang(\"\")", "", true)]
    [InlineData(":lang(\"\")", "und", false)]
    [InlineData(":lang(he)", "iw-IL", true)]
    [InlineData(":lang(iw)", "he-IL", true)]
    [InlineData(":lang(en-BU)", "en-MM", true)]
    [InlineData(":lang(\"*-BU\")", "en-MM", true)]
    [InlineData(":lang(ja-Latn-heploc)", "ja-Latn-alalc97", true)]
    [InlineData(":lang(i-klingon)", "tlh", true)]
    [InlineData(":lang(nb)", "no-bok", true)]
    [InlineData(":lang(zh-hakka)", "zh-hak", true)]
    [InlineData(":lang(yue)", "zh-yue-Hant", true)]
    [InlineData(":lang(zh)", "yue-Hant", true)]
    [InlineData(":lang(zh-yue)", "yue-Hant", true)]
    [InlineData(":lang(en-b-ccc-a-bbb)", "en-a-bbb-b-ccc", true)]
    [InlineData(":lang(en-x-heploc)", "en-x-alalc97", false)]
    [InlineData(":lang(he)", "x-iw", false)]
    public void LanguageRanges_UseRfc4647ExtendedFiltering(string selectorText, string tag, bool expected)
    {
        var parsed = CssSelectorParser.ParseGroup(selectorText, out var error);
        Assert.NotNull(parsed);
        var selector = Assert.Single(parsed);
        Assert.Null(error);
        var border = new Border { Language = XmlLanguage.GetLanguage(tag) };
        Assert.Equal(expected, CssMatcher.Matches(border, selector, evaluateStates: true));
    }

    [Fact]
    public void InheritedLanguage_ChangesInvalidateChildSelectorAndRespectLocalOverride()
    {
        var child = new Border();
        var root = new StackPanel();
        root.Children.Add(child);
        Css.SetStyleSheet(root, "Border:lang(fr) { opacity:.4 } Border:lang(en) { width:11px }");
        Flush(root);
        Assert.Equal(1, child.Opacity);
        Assert.Equal(11, child.Width);

        root.Language = XmlLanguage.GetLanguage("fr-CA");
        Flush(root);
        Assert.Equal(.4, child.Opacity);
        Assert.True(double.IsNaN(child.Width));

        child.Language = XmlLanguage.GetLanguage("en-GB");
        Flush(root);
        Assert.Equal(1, child.Opacity);
        Assert.Equal(11, child.Width);

        root.Language = XmlLanguage.GetLanguage("de-DE");
        Flush(root);
        Assert.Equal(11, child.Width);

        child.ClearValue(FrameworkElement.LanguageProperty);
        Flush(root);
        Assert.True(double.IsNaN(child.Width));
        Assert.Equal(1, child.Opacity);
    }

    [Fact]
    public void LangContributesOneClassOfSpecificity()
    {
        var border = new Border { Language = XmlLanguage.GetLanguage("fr") };
        Css.SetStyleSheet(border, ":lang(fr) { opacity:.4 } Border { opacity:.8 }");
        Flush(border);
        Assert.Equal(.4, border.Opacity);
    }

    [Fact]
    public void InlineDocumentLanguage_UsesInheritedLanguageAndLiveRuleMatching()
    {
        var run = new Run("bonjour");
        var block = new TextBlock(run) { FontSize = 14, Language = XmlLanguage.GetLanguage("fr") };
        Css.SetStyleSheet(block, "Run:lang(fr) { font-size:24px }");
        Flush(block);
        Assert.Equal(24, run.FontSize);

        block.Language = XmlLanguage.GetLanguage("en-US");
        Flush(block);
        Assert.Equal(14, run.FontSize);
    }

    [Fact]
    public void RegisteredLanguageAlias_UpdatesAppliedStylesWhenAncestorChanges()
    {
        var child = new Border();
        var root = new StackPanel { Language = XmlLanguage.GetLanguage("iw-IL") };
        root.Children.Add(child);
        Css.SetStyleSheet(root, "Border:lang(he) { opacity:.3 }");
        Flush(root);
        Assert.Equal(.3, child.Opacity);

        root.Language = XmlLanguage.GetLanguage("fr");
        Flush(root);
        Assert.Equal(1, child.Opacity);
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}
