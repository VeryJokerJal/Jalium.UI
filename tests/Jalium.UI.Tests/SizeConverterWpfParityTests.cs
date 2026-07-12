using System.ComponentModel;
using System.Globalization;
using MarkupSizeConverter = Jalium.UI.Markup.SizeConverter;

namespace Jalium.UI.Tests;

public sealed class SizeConverterWpfParityTests
{
    [Fact]
    public void Size_AdvertisesTheStandardComponentModelConverter()
    {
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(Size));

        Assert.IsType<SizeConverter>(converter);
        Assert.Equal(typeof(TypeConverter), typeof(SizeConverter).BaseType);
        Assert.True(typeof(SizeConverter).IsSealed);
        Assert.True(typeof(IFormattable).IsAssignableFrom(typeof(Size)));
        Assert.Contains(
            typeof(Size).GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(SerializableAttribute));
    }

    [Fact]
    public void Converter_ReportsItsSupportedStringConversions()
    {
        var converter = new SizeConverter();

        Assert.True(converter.CanConvertFrom(null, typeof(string)));
        Assert.True(converter.CanConvertTo(null, typeof(string)));
        Assert.False(converter.CanConvertFrom(null, typeof(DateTime)));
    }

    [Fact]
    public void ConvertFrom_AlwaysUsesInvariantSizeParsing()
    {
        var converter = new SizeConverter();
        var french = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(
            new Size(1.5, 2.25),
            Assert.IsType<Size>(converter.ConvertFrom(null, french, "1.5,2.25")));
        Assert.Equal(
            Size.Empty,
            Assert.IsType<Size>(converter.ConvertFrom(null, french, "Empty")));
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(null, french, null!));
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(null, french, 42));
    }

    [Fact]
    public void ConvertTo_UsesTheRequestedCulture()
    {
        var converter = new SizeConverter();
        var french = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(
            "1,5;2,25",
            converter.ConvertTo(null, french, new Size(1.5, 2.25), typeof(string)));
        Assert.Equal(
            "Empty",
            converter.ConvertTo(null, french, Size.Empty, typeof(string)));
        Assert.Throws<NotSupportedException>(
            () => converter.ConvertTo(null, french, new Size(1, 2), typeof(DateTime)));
    }

    [Fact]
    public void XamlRegistryAdapter_DelegatesToTheSameSizeParser()
    {
        var converter = new MarkupSizeConverter();

        Assert.Equal(new Size(7, 8), Assert.IsType<Size>(converter.ConvertFrom("7,8")));
        Assert.Equal(Size.Empty, Assert.IsType<Size>(converter.ConvertFrom("Empty")));
        Assert.Throws<ArgumentException>(() => converter.ConvertFrom("-1,2"));
        Assert.Throws<InvalidOperationException>(() => converter.ConvertFrom("1,2,3"));
    }
}
