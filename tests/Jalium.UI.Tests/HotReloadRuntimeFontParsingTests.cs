using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

/// FontWeight/FontStyle/FontStretch 是 struct（非 enum），TypeConverterRegistry 的通用 enum
/// 分支接不住；注册表补上前，运行时 XamlReader.Parse（设计器预览、热重载）对这三个属性
/// 一律静默丢弃——编译路径正常渲染、同一份标记热重载后字重纹丝不动。
public class HotReloadRuntimeFontParsingTests
{
    private const string J = "http://schemas.jalium.ui/2024";

    [Fact]
    public void ParsedFontWeightStyleStretch_LandInLocalLayer()
    {
        var tb = (TextBlock)XamlReader.Parse(
            $"""<TextBlock xmlns="{J}" Text="a" FontWeight="Bold" FontStyle="Italic" FontStretch="Condensed" />""");

        Assert.Equal(FontWeights.Bold, tb.FontWeight);
        Assert.Equal(FontStyles.Italic, tb.FontStyle);
        Assert.Equal(FontStretches.Condensed, tb.FontStretch);

        Assert.Equal(FontWeights.Bold, tb.ReadLocalValue(TextBlock.FontWeightProperty));
        Assert.Equal(FontStyles.Italic, tb.ReadLocalValue(TextBlock.FontStyleProperty));
        Assert.Equal(FontStretches.Condensed, tb.ReadLocalValue(TextBlock.FontStretchProperty));
    }

    [Fact]
    public void ParsedNumericFontWeight_Works()
    {
        var tb = (TextBlock)XamlReader.Parse(
            $"""<TextBlock xmlns="{J}" Text="a" FontWeight="700" />""");

        Assert.Equal(FontWeights.Bold, tb.FontWeight);
    }
}
