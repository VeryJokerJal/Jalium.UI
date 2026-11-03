using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

// Original WPT shorthand/longhand pairs, preserving their border-box dimensions.
// Sources and adaptations are recorded in docs/css-wpt-cases.md.
public class CssBorderRadiusWptTests
{
    [Fact]
    [Trait("WPT", "css/css-backgrounds/border-radius-005.xht")]
    public void BorderRadius005_ShorthandMatchesAllFourEllipticalLonghands()
    {
        Compare("50px 15px 40px / 20px 25px",
            "border-top-left-radius:50px 20px; border-top-right-radius:15px 25px; " +
            "border-bottom-right-radius:40px 20px; border-bottom-left-radius:15px 25px");
    }

    [Fact]
    [Trait("WPT", "css/css-backgrounds/border-radius-007.xht")]
    public void BorderRadius007_MixedUnitsPercentagesAndOverlapMatchLonghands()
    {
        var border = Compare("50px 10mm 6pc 15% / 0.5in 25px 30% 70pt",
            "border-top-left-radius:50px 0.5in; border-top-right-radius:10mm 25px; " +
            "border-bottom-right-radius:6pc 30%; border-bottom-left-radius:15% 70pt");
        var radii = CssBorderRadiusTests.Used(border);
        var factor = 104d / (48 + 70 * 96d / 72);
        Assert.Equal(50 * factor, radii.TopLeft.Width, 6);
        Assert.Equal(204 * .15 * factor, radii.BottomLeft.Width, 6);
        Assert.Equal(104 * .30 * factor, radii.BottomRight.Height, 6);
    }

    private static Border Compare(string shorthand, string longhands)
    {
        const string common = "box-sizing:content-box; width:200px; height:100px; border:2px solid #a1a1a1; background-color:#dddddd; ";
        var actual = CssBorderRadiusTests.Box(common + "border-radius:" + shorthand, 204, 104);
        var reference = CssBorderRadiusTests.Box(common + longhands, 204, 104);
        Assert.Equal(new Size(204, 104), actual.RenderSize);
        Assert.Equal(CssBorderRadiusTests.Used(reference), CssBorderRadiusTests.Used(actual));
        Assert.Equal(CssBorderRadiusTests.Pixels(reference, 204, 104), CssBorderRadiusTests.Pixels(actual, 204, 104));
        return actual;
    }
}
