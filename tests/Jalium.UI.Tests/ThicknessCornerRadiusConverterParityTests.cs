using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class ThicknessCornerRadiusConverterParityTests
{
    private static readonly Type[] SupportedSourceTypes =
    {
        typeof(string), typeof(decimal), typeof(float), typeof(double),
        typeof(short), typeof(int), typeof(long),
        typeof(ushort), typeof(uint), typeof(ulong),
    };

    [Fact]
    public void RootTypesExposeStandardComponentModelConverters()
    {
        Assert.Equal("Jalium.UI", typeof(ThicknessConverter).Namespace);
        Assert.Equal("Jalium.UI", typeof(CornerRadiusConverter).Namespace);
        Assert.Equal(typeof(TypeConverter), typeof(ThicknessConverter).BaseType);
        Assert.Equal(typeof(TypeConverter), typeof(CornerRadiusConverter).BaseType);
        Assert.False(typeof(ThicknessConverter).IsSealed);
        Assert.False(typeof(CornerRadiusConverter).IsSealed);

        Assert.IsType<ThicknessConverter>(TypeDescriptor.GetConverter(typeof(Thickness)));
        Assert.IsType<CornerRadiusConverter>(TypeDescriptor.GetConverter(typeof(CornerRadius)));
    }

    [Fact]
    public void ConversionCapabilitiesMatchWpf()
    {
        TypeConverter[] converters = { new ThicknessConverter(), new CornerRadiusConverter() };
        foreach (TypeConverter converter in converters)
        {
            foreach (Type sourceType in SupportedSourceTypes)
            {
                Assert.True(converter.CanConvertFrom(sourceType));
            }

            Assert.False(converter.CanConvertFrom(typeof(byte)));
            Assert.False(converter.CanConvertFrom(typeof(sbyte)));
            Assert.False(converter.CanConvertFrom(typeof(bool)));
            Assert.False(converter.CanConvertFrom(typeof(object)));

            Assert.True(converter.CanConvertTo(typeof(string)));
            Assert.True(converter.CanConvertTo(typeof(InstanceDescriptor)));
            Assert.False(converter.CanConvertTo(typeof(double)));
            Assert.False(converter.CanConvertTo(typeof(object)));
        }
    }

    [Fact]
    public void ThicknessConverterSupportsWpfShorthandUnitsAndNumbers()
    {
        var converter = new ThicknessConverter();
        CultureInfo french = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(new Thickness(2.5), converter.ConvertFrom(null, french, 2.5m));
        Assert.Equal(
            new Thickness(1.5, 2.5, 1.5, 2.5),
            converter.ConvertFrom(null, french, "1,5;2,5"));
        Assert.Equal(
            new Thickness(96, 96, 96, 4),
            converter.ConvertFrom(null, CultureInfo.InvariantCulture, "1in,2.54cm,72pt,4px"));

        var automatic = (Thickness)converter.ConvertFromInvariantString("Auto")!;
        Assert.True(double.IsNaN(automatic.Left));
        Assert.True(double.IsNaN(automatic.Top));
        Assert.True(double.IsNaN(automatic.Right));
        Assert.True(double.IsNaN(automatic.Bottom));

        Assert.Throws<FormatException>(() => converter.ConvertFromInvariantString("1,2,3"));
    }

    [Fact]
    public void CornerRadiusConverterSupportsOnlyUniformOrFourValueSyntax()
    {
        var converter = new CornerRadiusConverter();
        CultureInfo french = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(new CornerRadius(2.5), converter.ConvertFrom(null, french, 2.5m));
        Assert.Equal(
            new CornerRadius(1.5, 2.5, 3.5, 4.5),
            converter.ConvertFrom(null, french, "1,5;2,5;3,5;4,5"));

        Assert.Throws<FormatException>(() => converter.ConvertFromInvariantString("1,2"));
        Assert.Throws<FormatException>(() => converter.ConvertFromInvariantString("1in"));
    }

    [Fact]
    public void ConvertToStringUsesCultureAndThicknessAutoSyntax()
    {
        CultureInfo french = CultureInfo.GetCultureInfo("fr-FR");
        var thicknessConverter = new ThicknessConverter();
        var cornerRadiusConverter = new CornerRadiusConverter();

        string positiveInfinity = double.PositiveInfinity.ToString(french);
        string negativeInfinity = double.NegativeInfinity.ToString(french);
        string notANumber = double.NaN.ToString(french);

        Assert.Equal(
            $"1,5;Auto;{positiveInfinity};{negativeInfinity}",
            thicknessConverter.ConvertTo(null, french,
                new Thickness(1.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity),
                typeof(string)));
        Assert.Equal(
            $"1,5;{notANumber};{positiveInfinity};{negativeInfinity}",
            cornerRadiusConverter.ConvertTo(null, french,
                new CornerRadius(1.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity),
                typeof(string)));
    }

    [Fact]
    public void ConvertToInstanceDescriptorUsesFourArgumentConstructors()
    {
        AssertDescriptor(
            new ThicknessConverter(),
            new Thickness(1, 2, 3, 4),
            typeof(Thickness),
            new object[] { 1d, 2d, 3d, 4d });
        AssertDescriptor(
            new CornerRadiusConverter(),
            new CornerRadius(5, 6, 7, 8),
            typeof(CornerRadius),
            new object[] { 5d, 6d, 7d, 8d });
    }

    [Fact]
    public void ConvertToRejectsWrongValuesAndDestinations()
    {
        var converter = new ThicknessConverter();

        Assert.Throws<ArgumentNullException>(() =>
            converter.ConvertTo(null, CultureInfo.InvariantCulture, null, typeof(string)));
        Assert.Throws<ArgumentException>(() =>
            converter.ConvertTo(null, CultureInfo.InvariantCulture, new CornerRadius(1), typeof(string)));
        Assert.Throws<ArgumentException>(() =>
            converter.ConvertTo(null, CultureInfo.InvariantCulture, new Thickness(1), typeof(double)));
    }

    private static void AssertDescriptor(
        TypeConverter converter,
        object value,
        Type expectedType,
        object[] expectedArguments)
    {
        var descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(value, typeof(InstanceDescriptor)));
        var constructor = Assert.IsType<ConstructorInfo>(descriptor.MemberInfo, exactMatch: false);

        Assert.Equal(expectedType, constructor.DeclaringType);
        Assert.Equal(4, constructor.GetParameters().Length);
        Assert.Equal(expectedArguments, descriptor.Arguments.Cast<object>().ToArray());
        Assert.Equal(value, descriptor.Invoke());
        Assert.True(descriptor.IsComplete);
    }
}
