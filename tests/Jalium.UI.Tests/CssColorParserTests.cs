using Jalium.UI.Styling;
using Jalium.UI.Media;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssColorParserTests
{
    private static Color Parse(string text)
    {
        var reader = new CssTokenReader(text);
        Assert.True(CssColorParser.TryParse(ref reader, out var color, out var isCurrentColor));
        Assert.False(isCurrentColor);
        Assert.True(reader.AtEnd);
        return color;
    }

    private static void AssertColor(Color color, byte a, byte r, byte g, byte b)
    {
        Assert.Equal(a, color.A);
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    [Fact]
    public void Hex_EightDigits_TrailingAlphaIsCssOrderNotXamlOrder()
    {
        // CSS: #RRGGBBAA — alpha is the LAST byte (XAML would read #AARRGGBB).
        AssertColor(Parse("#11223344"), 0x44, 0x11, 0x22, 0x33);
    }

    [Theory]
    [InlineData("#fff", 0xFF, 0xFF, 0xFF, 0xFF)]
    [InlineData("#f00", 0xFF, 0xFF, 0x00, 0x00)]
    [InlineData("#f008", 0x88, 0xFF, 0x00, 0x00)]
    [InlineData("#1a2b3c", 0xFF, 0x1A, 0x2B, 0x3C)]
    public void Hex_AllLengths(string text, byte a, byte r, byte g, byte b)
        => AssertColor(Parse(text), a, r, g, b);

    [Theory]
    [InlineData("#12345")]
    [InlineData("#gg0000")]
    [InlineData("#1234567")]
    public void Hex_InvalidLengthsOrDigitsFail(string text)
    {
        var reader = new CssTokenReader(text);
        Assert.False(CssColorParser.TryParse(ref reader, out _, out _));
    }

    [Theory]
    [InlineData("rgb(255, 0, 0)", 0xFF, 255, 0, 0)]
    [InlineData("rgba(255, 0, 0, 0.5)", 0x80, 255, 0, 0)]
    [InlineData("rgb(255 0 0)", 0xFF, 255, 0, 0)]
    [InlineData("rgb(255 0 0 / 50%)", 0x80, 255, 0, 0)]
    [InlineData("rgb(100%, 0%, 50%)", 0xFF, 255, 0, 128)]
    [InlineData("rgba(0, 0, 0, 25%)", 0x40, 0, 0, 0)]
    [InlineData("rgb(300, -20, 12)", 0xFF, 255, 0, 12)]
    public void Rgb_LegacyAndModernSyntax(string text, byte a, byte r, byte g, byte b)
        => AssertColor(Parse(text), a, r, g, b);

    [Theory]
    [InlineData("hsl(120, 100%, 25%)", 0xFF, 0x00, 0x80, 0x00)]
    [InlineData("hsl(120 100% 25%)", 0xFF, 0x00, 0x80, 0x00)]
    [InlineData("hsl(0, 100%, 50%)", 0xFF, 0xFF, 0x00, 0x00)]
    [InlineData("hsl(240, 100%, 50%)", 0xFF, 0x00, 0x00, 0xFF)]
    [InlineData("hsla(0, 0%, 100%, 0.5)", 0x80, 0xFF, 0xFF, 0xFF)]
    [InlineData("hsl(480, 100%, 50%)", 0xFF, 0x00, 0xFF, 0x00)]
    [InlineData("hsl(-120, 100%, 50%)", 0xFF, 0x00, 0x00, 0xFF)]
    [InlineData("hsl(0.5turn, 100%, 25%)", 0xFF, 0x00, 0x80, 0x80)]
    public void Hsl_StandardAlgorithm(string text, byte a, byte r, byte g, byte b)
        => AssertColor(Parse(text), a, r, g, b);

    [Theory]
    [InlineData("red", 0xFF, 0xFF, 0x00, 0x00)]
    [InlineData("REBECCAPURPLE", 0xFF, 0x66, 0x33, 0x99)]
    [InlineData("LightGoldenrodYellow", 0xFF, 0xFA, 0xFA, 0xD2)]
    public void NamedColors_CaseInsensitive(string text, byte a, byte r, byte g, byte b)
        => AssertColor(Parse(text), a, r, g, b);

    [Fact]
    public void Transparent_IsFullyTransparentBlack()
        => AssertColor(Parse("transparent"), 0, 0, 0, 0);

    [Fact]
    public void CurrentColor_ReportsFlag()
    {
        var reader = new CssTokenReader("currentColor");
        Assert.True(CssColorParser.TryParse(ref reader, out _, out var isCurrentColor));
        Assert.True(isCurrentColor);
    }

    [Theory]
    [InlineData("notacolor")]
    [InlineData("rgb(1, 2)")]
    [InlineData("rgb(1, 2, 3, 4, 5)")]
    [InlineData("hsl(0, 50, 50)")]
    [InlineData("rgb(10px, 0, 0)")]
    public void InvalidColors_Fail(string text)
    {
        var reader = new CssTokenReader(text);
        Assert.False(CssColorParser.TryParse(ref reader, out _, out _));
    }
}
