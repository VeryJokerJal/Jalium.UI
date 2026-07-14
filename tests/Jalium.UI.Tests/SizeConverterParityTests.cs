using System.ComponentModel;
using System.Globalization;
using TypeConverterRegistry = Jalium.UI.Markup.TypeConverterRegistry;

namespace Jalium.UI.Tests;

public sealed class SizeConverterParityTests
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
    public void XamlRegistry_DelegatesToTheSameSizeParser()
    {
        Assert.Equal(new Size(7, 8), Assert.IsType<Size>(TypeConverterRegistry.ConvertValue("7,8", typeof(Size))));
        Assert.Equal(Size.Empty, Assert.IsType<Size>(TypeConverterRegistry.ConvertValue("Empty", typeof(Size))));
        Assert.Throws<ArgumentException>(() => TypeConverterRegistry.ConvertValue("-1,2", typeof(Size)));
        Assert.Throws<InvalidOperationException>(() => TypeConverterRegistry.ConvertValue("1,2,3", typeof(Size)));
    }
}
