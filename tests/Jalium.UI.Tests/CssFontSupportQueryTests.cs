using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssFontSupportQueryTests
{
    // CSS-supports-L5.html uses CSS.supports(conditionText). The native C#
    // equivalent evaluates the same condition used by @supports and @import.
    [Theory]
    [InlineData("font-format(opentype)", true)]
    [InlineData("font-format(xyzzy)", false)]
    [InlineData("font-format(opentype, truetype)", false)]
    [InlineData("font-format('opentype')", false)]
    [InlineData("font-tech(features-opentype)", true)]
    [InlineData("font-tech(feature-opentype)", false)]
    [InlineData("font-tech(foobar)", false)]
    [InlineData("font-tech(features-opentype, color-COLRv0)", false)]
    [InlineData("font-tech('features-opentype')", false)]
    public void WptCssSupportsLevel5_FontConditions(string condition, bool expected)
        => Assert.Equal(expected, Css.Supports(condition));

    [Theory]
    [InlineData("font-format(truetype)", true)]
    [InlineData("font-format(WOFF)", true)]
    [InlineData("font-format(woff2)", true)]
    [InlineData("font-format(collection)", true)]
    [InlineData("font-format(svg)", false)]
    [InlineData("font-format(embedded-opentype)", false)]
    [InlineData("font-format(woff2-variations)", false)]
    [InlineData("font-format()", false)]
    [InlineData("font-format(woff2 extra)", false)]
    [InlineData("font-tech(FEATURES-OPENTYPE)", true)]
    [InlineData("font-tech(variations)", false)]
    [InlineData("font-tech(color-COLRv0)", false)]
    [InlineData("font-tech(color-COLRv1)", false)]
    [InlineData("font-tech(palettes)", false)]
    [InlineData("font-tech(incremental)", false)]
    public void Queries_ReportOnlyUsableNativeFontCapabilities(string condition, bool expected)
        => Assert.Equal(expected, Css.Supports(condition));

    [Theory]
    [InlineData("font-format(woff2) and font-tech(features-opentype)", true)]
    [InlineData("font-format(woff2) and font-tech(variations)", false)]
    [InlineData("font-format(woff2) or font-tech(variations)", true)]
    [InlineData("not font-format(svg)", true)]
    [InlineData("(font-tech(features-opentype))", true)]
    public void FontConditions_ComposeWithSupportsBooleanGrammar(string condition, bool expected)
        => Assert.Equal(expected, Css.Supports(condition));

    [Theory]
    [InlineData("display:grid", true)]
    [InlineData("(display:grid)", true)]
    [InlineData("not (made-up:yes)", true)]
    [InlineData("font-format(woff2) and", false)]
    [InlineData("", false)]
    public void ConditionApi_HandlesDeclarationsAndInvalidGrammar(string condition, bool expected)
        => Assert.Equal(expected, Css.Supports(condition));
}
