using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssLengthTests
{
    [Theory]
    [InlineData(10, (int)CssUnit.Px, 10.0)]
    [InlineData(10, (int)CssUnit.None, 10.0)]
    [InlineData(72, (int)CssUnit.Pt, 96.0)]
    [InlineData(1, (int)CssUnit.In, 96.0)]
    [InlineData(2.54, (int)CssUnit.Cm, 96.0)]
    [InlineData(25.4, (int)CssUnit.Mm, 96.0)]
    [InlineData(101.6, (int)CssUnit.Q, 96.0)]
    [InlineData(1, (int)CssUnit.Pc, 16.0)]
    public void AbsoluteUnits_ConvertToPx(double value, int unit, double expectedPx)
    {
        var length = new CssLength(value, (CssUnit)unit);
        Assert.True(length.IsAbsolute);
        Assert.Equal(expectedPx, length.ToPxAbsolute(), precision: 9);
    }

    [Fact]
    public void Em_ResolvesAgainstElementFontSize()
    {
        var context = new CssLengthContext(elementFontSize: 20, inheritedFontSize: 20, rootFontSize: 16, viewportWidth: 0, viewportHeight: 0);
        var length = new CssLength(1.5, CssUnit.Em);
        Assert.True(length.TryResolve(context, CssPercentBasis.NotSupported, out var px));
        Assert.Equal(30.0, px);
    }

    [Fact]
    public void Rem_ResolvesAgainstRootFontSize()
    {
        var context = new CssLengthContext(elementFontSize: 20, inheritedFontSize: 20, rootFontSize: 16, viewportWidth: 0, viewportHeight: 0);
        var length = new CssLength(2, CssUnit.Rem);
        Assert.True(length.TryResolve(context, CssPercentBasis.NotSupported, out var px));
        Assert.Equal(32.0, px);
    }

    [Fact]
    public void Percent_FontSizeBasis()
    {
        var context = new CssLengthContext(elementFontSize: 20, inheritedFontSize: 20, rootFontSize: 16, viewportWidth: 0, viewportHeight: 0);
        var length = new CssLength(150, CssUnit.Percent);
        Assert.True(length.TryResolve(context, CssPercentBasis.ElementFontSize, out var px));
        Assert.Equal(30.0, px);
    }

    [Fact]
    public void Percent_FractionBasis()
    {
        var length = new CssLength(50, CssUnit.Percent);
        Assert.True(length.TryResolve(CssLengthContext.Default, CssPercentBasis.Fraction, out var fraction));
        Assert.Equal(0.5, fraction);
    }

    [Fact]
    public void Percent_NotSupportedBasisFails()
    {
        var length = new CssLength(50, CssUnit.Percent);
        Assert.False(length.TryResolve(CssLengthContext.Default, CssPercentBasis.NotSupported, out _));
    }

    [Fact]
    public void ViewportUnits_FailWithoutViewport()
    {
        var length = new CssLength(10, CssUnit.Vw);
        Assert.False(length.TryResolve(CssLengthContext.Default, CssPercentBasis.NotSupported, out _));

        var context = new CssLengthContext(14, 14, 14, viewportWidth: 800, viewportHeight: 600);
        Assert.True(length.TryResolve(context, CssPercentBasis.NotSupported, out var px));
        Assert.Equal(80.0, px);
    }

    [Fact]
    public void InvalidContextValues_FallBackToDefaultFontSize()
    {
        var context = new CssLengthContext(double.NaN, 0, 0, 0, 0);
        Assert.Equal(CssLengthContext.DefaultFontSize, context.ElementFontSize);
        Assert.Equal(CssLengthContext.DefaultFontSize, context.RootFontSize);
    }

    [Theory]
    [InlineData(180, (int)CssUnit.Deg, 180.0)]
    [InlineData(Math.PI, (int)CssUnit.Rad, 180.0)]
    [InlineData(200, (int)CssUnit.Grad, 180.0)]
    [InlineData(0.5, (int)CssUnit.Turn, 180.0)]
    [InlineData(45, (int)CssUnit.None, 45.0)]
    public void AngleConversions(double value, int unit, double expectedDegrees)
    {
        Assert.True(CssUnitConversion.TryToDegrees(value, (CssUnit)unit, out var degrees));
        Assert.Equal(expectedDegrees, degrees, precision: 9);
    }

    [Fact]
    public void AngleConversion_RejectsLengthUnits()
        => Assert.False(CssUnitConversion.TryToDegrees(1, CssUnit.Px, out _));

    [Theory]
    [InlineData(0.2, (int)CssUnit.S, 200.0)]
    [InlineData(150, (int)CssUnit.Ms, 150.0)]
    public void TimeConversions(double value, int unit, double expectedMs)
    {
        Assert.True(CssUnitConversion.TryToMilliseconds(value, (CssUnit)unit, out var ms));
        Assert.Equal(expectedMs, ms);
    }

    [Fact]
    public void TimeConversion_RejectsUnitlessNumbers()
        => Assert.False(CssUnitConversion.TryToMilliseconds(1, CssUnit.None, out _));
}
