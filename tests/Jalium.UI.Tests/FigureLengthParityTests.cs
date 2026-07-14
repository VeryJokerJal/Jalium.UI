using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace Jalium.UI.Tests;

public sealed class FigureLengthParityTests
{
    [Fact]
    public void SingleArgumentConstructorCreatesPixelLength()
    {
        var length = new FigureLength(12d);

        Assert.True(length.IsAbsolute);
        Assert.Equal(FigureUnitType.Pixel, length.FigureUnitType);
        Assert.Equal(12d, length.Value);
    }

    [Fact]
    public void AutoIgnoresConstructorValueAndReportsWpfLogicalValue()
    {
        var length = new FigureLength(42d, FigureUnitType.Auto);

        Assert.True(length.IsAuto);
        Assert.Equal(1d, length.Value);
        Assert.Equal("Auto", length.ToString());
    }

    [Theory]
    [InlineData(double.NaN, FigureUnitType.Pixel)]
    [InlineData(double.PositiveInfinity, FigureUnitType.Pixel)]
    [InlineData(-1d, FigureUnitType.Pixel)]
    [InlineData(1.1d, FigureUnitType.Content)]
    [InlineData(1.1d, FigureUnitType.Page)]
    [InlineData(1001d, FigureUnitType.Column)]
    [InlineData(1000001d, FigureUnitType.Pixel)]
    public void ConstructorRejectsValuesOutsideWpfRange(double value, FigureUnitType unit)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FigureLength(value, unit));
    }

    [Fact]
    public void ConverterParsesAndFormatsWpfSyntax()
    {
        var converter = new FigureLengthConverter();
        var french = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(new FigureLength(20d), converter.ConvertFromInvariantString("20"));
        Assert.Equal(
            new FigureLength(0.5d, FigureUnitType.Column),
            converter.ConvertFrom(null, CultureInfo.InvariantCulture, "0.5 Column"));
        Assert.Equal(
            new FigureLength(0.5d, FigureUnitType.Content),
            converter.ConvertFrom(null, french, "0,5 Content"));
        Assert.Equal("0,5 Content", converter.ConvertTo(null, french,
            new FigureLength(0.5d, FigureUnitType.Content), typeof(string)));
    }

    [Fact]
    public void FigureLengthExposesConverterAndInstanceDescriptor()
    {
        var converter = TypeDescriptor.GetConverter(typeof(FigureLength));
        Assert.IsType<FigureLengthConverter>(converter);

        var value = new FigureLength(0.75d, FigureUnitType.Page);
        var descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(value, typeof(InstanceDescriptor)));
        Assert.Equal(value, descriptor.Invoke());
    }
}
