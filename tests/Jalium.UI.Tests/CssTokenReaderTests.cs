using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssTokenReaderTests
{
    [Theory]
    [InlineData("12px", 12.0, (int)CssUnit.Px)]
    [InlineData(".5", 0.5, (int)CssUnit.None)]
    [InlineData("-3e2deg", -300.0, (int)CssUnit.Deg)]
    [InlineData("50%", 50.0, (int)CssUnit.Percent)]
    [InlineData("+2.25rem", 2.25, (int)CssUnit.Rem)]
    [InlineData("0.2s", 0.2, (int)CssUnit.S)]
    [InlineData("150ms", 150.0, (int)CssUnit.Ms)]
    [InlineData("1.5turn", 1.5, (int)CssUnit.Turn)]
    public void ReadNumber_ParsesValueAndUnit(string text, double expected, int expectedUnit)
    {
        var reader = new CssTokenReader(text);
        Assert.True(reader.TryReadNumber(out var value, out var unit));
        Assert.Equal(expected, value, precision: 10);
        Assert.Equal((CssUnit)expectedUnit, unit);
        Assert.True(reader.AtEnd);
    }

    [Theory]
    [InlineData("10vmin")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-")]
    public void ReadNumber_RejectsUnknownUnitsAndNonNumbers(string text)
    {
        var reader = new CssTokenReader(text);
        Assert.False(reader.TryReadNumber(out _, out _));
    }

    [Fact]
    public void ReadNumber_FailureDoesNotAdvance()
    {
        var reader = new CssTokenReader("bold");
        Assert.False(reader.TryReadNumber(out _, out _));
        Assert.True(reader.TryReadIdent(out var ident));
        Assert.Equal("bold", ident.ToString());
    }

    [Fact]
    public void ReadIdent_MinusFollowedByDigitIsNotIdent()
    {
        var reader = new CssTokenReader("-3px");
        Assert.False(reader.TryReadIdent(out _));
        Assert.True(reader.TryReadNumber(out var value, out var unit));
        Assert.Equal(-3.0, value);
        Assert.Equal(CssUnit.Px, unit);
    }

    [Fact]
    public void ReadFunction_HandlesNestedParenthesesAndStrings()
    {
        var reader = new CssTokenReader("linear-gradient(90deg, rgba(0, 0, 0, .5), red) more");
        Assert.True(reader.TryReadFunction(out var name, out var args));
        Assert.Equal("linear-gradient", name.ToString());
        Assert.Equal("90deg, rgba(0, 0, 0, .5), red", args.Remaining.ToString());
        Assert.True(reader.TryReadIdent(out var trailing));
        Assert.Equal("more", trailing.ToString());
    }

    [Fact]
    public void ReadFunction_UnbalancedFails()
    {
        var reader = new CssTokenReader("blur(5px");
        Assert.False(reader.TryReadFunction(out _, out _));
    }

    [Fact]
    public void ReadIdent_DoesNotConsumeFunctionToken()
    {
        var reader = new CssTokenReader("blur(5px)");
        Assert.False(reader.TryReadIdent(out _));
        Assert.True(reader.TryReadFunction(out var name, out _));
        Assert.Equal("blur", name.ToString());
    }

    [Fact]
    public void ReadHash_ReturnsDigits()
    {
        var reader = new CssTokenReader("  #A1b2C3 ");
        Assert.True(reader.TryReadHash(out var digits));
        Assert.Equal("A1b2C3", digits.ToString());
        Assert.True(reader.AtEnd);
    }

    [Theory]
    [InlineData("\"hello world\"", "hello world")]
    [InlineData("'single'", "single")]
    [InlineData("\"esc\\\"aped\"", "esc\"aped")]
    public void ReadString_HandlesQuotesAndEscapes(string text, string expected)
    {
        var reader = new CssTokenReader(text);
        Assert.True(reader.TryReadString(out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void ReadString_UnterminatedFails()
    {
        var reader = new CssTokenReader("\"open");
        Assert.False(reader.TryReadString(out _));
    }

    [Fact]
    public void ReadUntilTopLevelComma_HonorsNesting()
    {
        var reader = new CssTokenReader("0 2px 4px rgba(0,0,0,.3), inset 0 0 2px red");
        Assert.True(reader.TryReadUntilTopLevelComma(out var first));
        Assert.Equal("0 2px 4px rgba(0,0,0,.3)", first.ToString());
        Assert.True(reader.TryReadComma());
        Assert.True(reader.TryReadUntilTopLevelComma(out var second));
        Assert.Equal("inset 0 0 2px red", second.ToString());
        Assert.True(reader.AtEnd);
    }

    [Fact]
    public void MalformedInput_NeverThrows()
    {
        foreach (var text in new[] { "((((", "#", "'", "1e", "..", "-,", ")" })
        {
            var reader = new CssTokenReader(text);
            reader.TryReadNumber(out _, out _);
            reader.TryReadIdent(out _);
            reader.TryReadFunction(out _, out _);
            reader.TryReadHash(out _);
            reader.TryReadString(out _);
            reader.TryReadComma();
            reader.TryReadSlash();
        }
    }
}
