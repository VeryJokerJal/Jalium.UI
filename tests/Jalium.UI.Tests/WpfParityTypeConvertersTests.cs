using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class WpfParityTypeConvertersTests
{
    [Theory]
    [InlineData("12", 12d)]
    [InlineData("12px", 12d)]
    [InlineData("1in", 96d)]
    [InlineData("2.54cm", 96d)]
    [InlineData("72pt", 96d)]
    public void LengthConverter_ParsesWpfUnits(string text, double expected)
    {
        var converter = new LengthConverter();

        double actual = Assert.IsType<double>(
            converter.ConvertFrom(null, CultureInfo.InvariantCulture, text));

        Assert.Equal(expected, actual, precision: 10);
    }

    [Fact]
    public void LengthConverter_HandlesAutoAndCulture()
    {
        var converter = new LengthConverter();
        var french = CultureInfo.GetCultureInfo("fr-FR");

        Assert.True(double.IsNaN((double)converter.ConvertFromInvariantString("Auto")!));
        Assert.Equal(1.5d, (double)converter.ConvertFrom(null, french, "1,5px"), precision: 10);
        Assert.Equal("Auto", converter.ConvertToInvariantString(double.NaN));
    }

    [Fact]
    public void FontSizeConverter_HandlesStringsAndNumericInputs()
    {
        var converter = new FontSizeConverter();

        Assert.Equal(16d, converter.ConvertFromInvariantString("12pt"));
        Assert.Equal(18d, converter.ConvertFrom(18));
        Assert.Equal(18d, converter.ConvertFrom(18f));
        Assert.Equal(18f, converter.ConvertTo(18d, typeof(float)));
    }

    [Fact]
    public void FontConverters_ParseNamesNumbersAndCreateDescriptors()
    {
        var weightConverter = new FontWeightConverter();
        var styleConverter = new FontStyleConverter();
        var stretchConverter = new FontStretchConverter();

        Assert.Equal(FontWeights.SemiBold, weightConverter.ConvertFromInvariantString("DemiBold"));
        Assert.Equal(FontWeights.ExtraBlack, weightConverter.ConvertFromInvariantString("950"));
        Assert.Equal(FontStyles.Italic, styleConverter.ConvertFromInvariantString("italic"));
        Assert.Equal(FontStretches.Normal, stretchConverter.ConvertFromInvariantString("Medium"));
        Assert.Equal(FontStretches.Expanded, stretchConverter.ConvertFromInvariantString("7"));

        var descriptor = Assert.IsType<InstanceDescriptor>(
            weightConverter.ConvertTo(FontWeights.Bold, typeof(InstanceDescriptor)));
        Assert.Equal(FontWeights.Bold, descriptor.Invoke());
    }

    [Fact]
    public void FontStructsExposeTheirWpfConverters()
    {
        Assert.IsType<FontWeightConverter>(TypeDescriptor.GetConverter(typeof(FontWeight)));
        Assert.IsType<FontStyleConverter>(TypeDescriptor.GetConverter(typeof(FontStyle)));
        Assert.IsType<FontStretchConverter>(TypeDescriptor.GetConverter(typeof(FontStretch)));
    }

    [Fact]
    public void DefaultFontStructsMatchWpfNormalValuesAndValidateOpenTypeRanges()
    {
        Assert.Equal(FontWeights.Normal, default(FontWeight));
        Assert.Equal(400, default(FontWeight).ToOpenTypeWeight());
        Assert.Equal("Normal", default(FontWeight).ToString());

        Assert.Equal(FontStretches.Normal, default(FontStretch));
        Assert.Equal(5, default(FontStretch).ToOpenTypeStretch());
        Assert.Equal("Normal", default(FontStretch).ToString());

        Assert.Throws<ArgumentOutOfRangeException>(() => FontWeight.FromOpenTypeWeight(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontWeight.FromOpenTypeWeight(1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontStretch.FromOpenTypeStretch(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontStretch.FromOpenTypeStretch(10));
    }

    [Fact]
    public void DialogResultConverterRejectsMarkupConversion()
    {
        var converter = new DialogResultConverter();

        Assert.False(converter.CanConvertFrom(typeof(string)));
        Assert.False(converter.CanConvertTo(typeof(string)));
        Assert.Throws<InvalidOperationException>(() => converter.ConvertFromInvariantString("true"));
        Assert.Throws<InvalidOperationException>(() => converter.ConvertToInvariantString(true));
    }
}
