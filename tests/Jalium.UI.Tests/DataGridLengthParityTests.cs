using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class DataGridLengthParityTests
{
    [Fact]
    public void PublicSurface_MatchesWpfContract()
    {
        Assert.Equal(
            new[]
            {
                DataGridLengthUnitType.Auto,
                DataGridLengthUnitType.Pixel,
                DataGridLengthUnitType.SizeToCells,
                DataGridLengthUnitType.SizeToHeader,
                DataGridLengthUnitType.Star,
            },
            Enum.GetValues<DataGridLengthUnitType>());
        Assert.Equal(
            new[] { 0, 1, 2, 3, 4 },
            Enum.GetValues<DataGridLengthUnitType>().Select(static value => (int)value));

        Type type = typeof(DataGridLength);
        Assert.True(type.IsValueType);
        Assert.Contains(typeof(IEquatable<DataGridLength>), type.GetInterfaces());
        Assert.Equal(3, type.GetConstructors().Length);
        Assert.NotNull(type.GetConstructor([typeof(double)]));
        Assert.NotNull(type.GetConstructor([typeof(double), typeof(DataGridLengthUnitType)]));
        Assert.NotNull(type.GetConstructor(
            [typeof(double), typeof(DataGridLengthUnitType), typeof(double), typeof(double)]));

        string[] expectedProperties =
        [
            nameof(DataGridLength.Auto),
            nameof(DataGridLength.DesiredValue),
            nameof(DataGridLength.DisplayValue),
            nameof(DataGridLength.IsAbsolute),
            nameof(DataGridLength.IsAuto),
            nameof(DataGridLength.IsSizeToCells),
            nameof(DataGridLength.IsSizeToHeader),
            nameof(DataGridLength.IsStar),
            nameof(DataGridLength.SizeToCells),
            nameof(DataGridLength.SizeToHeader),
            nameof(DataGridLength.UnitType),
            nameof(DataGridLength.Value),
        ];
        Assert.Equal(
            expectedProperties.OrderBy(static name => name),
            type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Select(static property => property.Name)
                .OrderBy(static name => name));

        foreach (PropertyInfo property in type.GetProperties())
        {
            Assert.NotNull(property.GetMethod);
            Assert.Null(property.SetMethod);
        }

        Assert.Equal(typeof(DataGridLength), type.GetProperty(nameof(DataGridLength.Auto))!.PropertyType);
        Assert.Equal(typeof(double), type.GetProperty(nameof(DataGridLength.DesiredValue))!.PropertyType);
        Assert.Equal(typeof(double), type.GetProperty(nameof(DataGridLength.DisplayValue))!.PropertyType);
        Assert.Equal(typeof(bool), type.GetProperty(nameof(DataGridLength.IsAbsolute))!.PropertyType);
        Assert.Equal(typeof(bool), type.GetProperty(nameof(DataGridLength.IsAuto))!.PropertyType);
        Assert.Equal(typeof(bool), type.GetProperty(nameof(DataGridLength.IsSizeToCells))!.PropertyType);
        Assert.Equal(typeof(bool), type.GetProperty(nameof(DataGridLength.IsSizeToHeader))!.PropertyType);
        Assert.Equal(typeof(bool), type.GetProperty(nameof(DataGridLength.IsStar))!.PropertyType);
        Assert.Equal(typeof(DataGridLength), type.GetProperty(nameof(DataGridLength.SizeToCells))!.PropertyType);
        Assert.Equal(typeof(DataGridLength), type.GetProperty(nameof(DataGridLength.SizeToHeader))!.PropertyType);
        Assert.Equal(
            typeof(DataGridLengthUnitType),
            type.GetProperty(nameof(DataGridLength.UnitType))!.PropertyType);
        Assert.Equal(typeof(double), type.GetProperty(nameof(DataGridLength.Value))!.PropertyType);

        Assert.Equal(typeof(bool), GetDeclaredMethod(type, nameof(DataGridLength.Equals), typeof(object)).ReturnType);
        Assert.Equal(
            typeof(bool),
            GetDeclaredMethod(type, nameof(DataGridLength.Equals), typeof(DataGridLength)).ReturnType);
        Assert.Equal(typeof(int), GetDeclaredMethod(type, nameof(DataGridLength.GetHashCode)).ReturnType);
        Assert.Equal(typeof(string), GetDeclaredMethod(type, nameof(DataGridLength.ToString)).ReturnType);
        Assert.Equal(
            typeof(bool),
            GetDeclaredMethod(type, "op_Equality", typeof(DataGridLength), typeof(DataGridLength)).ReturnType);
        Assert.Equal(
            typeof(bool),
            GetDeclaredMethod(type, "op_Inequality", typeof(DataGridLength), typeof(DataGridLength)).ReturnType);
        Assert.Equal(
            typeof(DataGridLength),
            GetDeclaredMethod(type, "op_Implicit", typeof(double)).ReturnType);

        Assert.Equal(typeof(DataGridLengthConverter), TypeDescriptor.GetConverter(type).GetType());
        Type converterType = typeof(DataGridLengthConverter);
        Assert.Equal(typeof(TypeConverter), converterType.BaseType);
        Assert.NotNull(converterType.GetConstructor(Type.EmptyTypes));
        Assert.NotNull(converterType.GetMethod(
            nameof(TypeConverter.CanConvertFrom),
            [typeof(ITypeDescriptorContext), typeof(Type)]));
        Assert.NotNull(converterType.GetMethod(
            nameof(TypeConverter.CanConvertTo),
            [typeof(ITypeDescriptorContext), typeof(Type)]));
        Assert.NotNull(converterType.GetMethod(
            nameof(TypeConverter.ConvertFrom),
            [typeof(ITypeDescriptorContext), typeof(CultureInfo), typeof(object)]));
        Assert.NotNull(converterType.GetMethod(
            nameof(TypeConverter.ConvertTo),
            [typeof(ITypeDescriptorContext), typeof(CultureInfo), typeof(object), typeof(Type)]));
    }

    [Fact]
    public void ConstructorsAndStaticValues_PreserveWpfValueSemantics()
    {
        var pixels = new DataGridLength(12.5);
        Assert.True(pixels.IsAbsolute);
        Assert.Equal(DataGridLengthUnitType.Pixel, pixels.UnitType);
        Assert.Equal(12.5, pixels.Value);
        Assert.Equal(12.5, pixels.DesiredValue);
        Assert.Equal(12.5, pixels.DisplayValue);

        var star = new DataGridLength(3, DataGridLengthUnitType.Star);
        Assert.True(star.IsStar);
        Assert.Equal(3, star.Value);
        Assert.True(double.IsNaN(star.DesiredValue));
        Assert.True(double.IsNaN(star.DisplayValue));

        var full = new DataGridLength(4, DataGridLengthUnitType.SizeToCells, 5, 6);
        Assert.True(full.IsSizeToCells);
        Assert.Equal(4, full.Value);
        Assert.Equal(5, full.DesiredValue);
        Assert.Equal(6, full.DisplayValue);

        var autoWithIgnoredValue = new DataGridLength(42, DataGridLengthUnitType.Auto, 7, 8);
        Assert.True(autoWithIgnoredValue.IsAuto);
        Assert.Equal(1, autoWithIgnoredValue.Value);
        Assert.Equal(7, autoWithIgnoredValue.DesiredValue);
        Assert.Equal(8, autoWithIgnoredValue.DisplayValue);

        Assert.Equal(default, DataGridLength.Auto);
        Assert.True(DataGridLength.Auto.IsAuto);
        Assert.Equal((1d, 0d, 0d),
            (DataGridLength.Auto.Value, DataGridLength.Auto.DesiredValue, DataGridLength.Auto.DisplayValue));
        Assert.True(DataGridLength.SizeToCells.IsSizeToCells);
        Assert.True(DataGridLength.SizeToHeader.IsSizeToHeader);
        Assert.Equal(1, DataGridLength.SizeToCells.Value);
        Assert.Equal(1, DataGridLength.SizeToHeader.Value);
    }

    [Fact]
    public void Constructors_ValidateOnlyTheWpfInvalidBoundaries()
    {
        Assert.Equal(-1, new DataGridLength(-1).Value);
        var nanMeasurements = new DataGridLength(1, DataGridLengthUnitType.Star, double.NaN, double.NaN);
        Assert.True(double.IsNaN(nanMeasurements.DesiredValue));
        Assert.True(double.IsNaN(nanMeasurements.DisplayValue));

        Assert.Equal("value", Assert.Throws<ArgumentException>(
            () => new DataGridLength(double.NaN)).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentException>(
            () => new DataGridLength(double.PositiveInfinity)).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentException>(
            () => new DataGridLength(double.NegativeInfinity)).ParamName);
        Assert.Equal("type", Assert.Throws<ArgumentException>(
            () => new DataGridLength(1, (DataGridLengthUnitType)99)).ParamName);
        Assert.Equal("desiredValue", Assert.Throws<ArgumentException>(
            () => new DataGridLength(1, DataGridLengthUnitType.Pixel, double.PositiveInfinity, 1)).ParamName);
        Assert.Equal("displayValue", Assert.Throws<ArgumentException>(
            () => new DataGridLength(1, DataGridLengthUnitType.Pixel, 1, double.NegativeInfinity)).ParamName);
    }

    [Fact]
    public void Equality_TreatsPairedNaNMeasurementsAsEqual()
    {
        var left = new DataGridLength(2, DataGridLengthUnitType.Star, double.NaN, double.NaN);
        var right = new DataGridLength(2, DataGridLengthUnitType.Star, double.NaN, double.NaN);
        var differentDesired = new DataGridLength(2, DataGridLengthUnitType.Star, 0, double.NaN);

        Assert.True(left == right);
        Assert.False(left != right);
        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.False(left == differentDesired);
        Assert.True(left != differentDesired);
        Assert.False(left.Equals("2*"));
    }

    [Fact]
    public void ImplicitConversionAndToString_MatchWpfFormatting()
    {
        DataGridLength implicitPixels = 2.5;
        Assert.Equal(new DataGridLength(2.5), implicitPixels);
        Assert.Equal("2.5", implicitPixels.ToString());
        Assert.Equal("Auto", DataGridLength.Auto.ToString());
        Assert.Equal("SizeToCells", DataGridLength.SizeToCells.ToString());
        Assert.Equal("SizeToHeader", DataGridLength.SizeToHeader.ToString());
        Assert.Equal("*", new DataGridLength(1, DataGridLengthUnitType.Star).ToString());
        Assert.Equal("*", new DataGridLength(1.000000000000001, DataGridLengthUnitType.Star).ToString());
        Assert.Equal(
            "1.00000000000001*",
            new DataGridLength(1.00000000000001, DataGridLengthUnitType.Star).ToString());
        Assert.Equal("2*", new DataGridLength(2, DataGridLengthUnitType.Star).ToString());
    }

    [Fact]
    public void Converter_AdvertisesTheWpfConversionSet()
    {
        var converter = new DataGridLengthConverter();

        foreach (Type sourceType in new[]
                 {
                     typeof(string), typeof(decimal), typeof(float), typeof(double),
                     typeof(short), typeof(int), typeof(long), typeof(ushort), typeof(uint),
                     typeof(ulong), typeof(byte),
                 })
        {
            Assert.True(converter.CanConvertFrom(sourceType));
        }

        Assert.False(converter.CanConvertFrom(typeof(sbyte)));
        Assert.False(converter.CanConvertFrom(typeof(bool)));
        Assert.False(converter.CanConvertFrom(typeof(object)));
        Assert.True(converter.CanConvertTo(typeof(string)));
        Assert.True(converter.CanConvertTo(typeof(InstanceDescriptor)));
        Assert.False(converter.CanConvertTo(typeof(double)));
    }

    [Fact]
    public void Converter_ParsesWpfMarkupUnits()
    {
        var converter = new DataGridLengthConverter();
        var invariant = CultureInfo.InvariantCulture;

        AssertLength(converter.ConvertFrom(null, invariant, " Auto "), DataGridLengthUnitType.Auto, 1);
        AssertLength(converter.ConvertFrom(null, invariant, "SizeToCells"), DataGridLengthUnitType.SizeToCells, 1);

        // WPF treats SizeToHeader as a suffix rather than a standalone descriptive unit,
        // so its value is zero when no numeric prefix is present.
        AssertLength(converter.ConvertFrom(null, invariant, "SizeToHeader"), DataGridLengthUnitType.SizeToHeader, 0);
        AssertLength(converter.ConvertFrom(null, invariant, "*"), DataGridLengthUnitType.Star, 1);
        AssertLength(converter.ConvertFrom(null, invariant, "2.5*"), DataGridLengthUnitType.Star, 2.5);
        AssertLength(converter.ConvertFrom(null, invariant, "px"), DataGridLengthUnitType.Pixel, 1);
        AssertLength(converter.ConvertFrom(null, invariant, "12.5"), DataGridLengthUnitType.Pixel, 12.5);
        AssertLength(converter.ConvertFrom(null, invariant, "1in"), DataGridLengthUnitType.Pixel, 96);
        AssertLength(converter.ConvertFrom(null, invariant, "2.54cm"), DataGridLengthUnitType.Pixel, 96);
        AssertLength(converter.ConvertFrom(null, invariant, "72pt"), DataGridLengthUnitType.Pixel, 96);
        AssertLength(converter.ConvertFrom(null, invariant, ""), DataGridLengthUnitType.Pixel, 0);
        Assert.Throws<FormatException>(() => converter.ConvertFrom(null, invariant, "2px"));
    }

    [Fact]
    public void Converter_UsesCultureForParsingAndFormatting()
    {
        var converter = new DataGridLengthConverter();
        CultureInfo culture = CultureInfo.GetCultureInfo("fr-FR");

        var star = Assert.IsType<DataGridLength>(converter.ConvertFrom(null, culture, "1,5*"));
        Assert.Equal(1.5, star.Value);
        Assert.Equal("1,5*", converter.ConvertTo(null, culture, star, typeof(string)));

        var centimeter = Assert.IsType<DataGridLength>(converter.ConvertFrom(null, culture, "2,54cm"));
        Assert.Equal(96, centimeter.Value, 10);
        Assert.Equal("1,5", converter.ConvertTo(
            null,
            culture,
            new DataGridLength(1.5),
            typeof(string)));
    }

    [Fact]
    public void Converter_MapsNumericNaNToAutoAndRejectsInfinity()
    {
        var converter = new DataGridLengthConverter();

        var auto = Assert.IsType<DataGridLength>(converter.ConvertFrom(double.NaN));
        Assert.True(auto.IsAuto);
        Assert.Equal(1, auto.Value);
        Assert.True(double.IsNaN(auto.DesiredValue));
        Assert.True(double.IsNaN(auto.DisplayValue));

        var pixels = Assert.IsType<DataGridLength>(converter.ConvertFrom(12.5m));
        Assert.Equal(new DataGridLength(12.5), pixels);
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(double.PositiveInfinity));
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(null!));
    }

    [Fact]
    public void Converter_CreatesWpfTwoArgumentInstanceDescriptor()
    {
        var converter = new DataGridLengthConverter();
        var original = new DataGridLength(3, DataGridLengthUnitType.Star, 10, 9);

        var descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(original, typeof(InstanceDescriptor)));
        var reconstructed = Assert.IsType<DataGridLength>(descriptor.Invoke());

        Assert.Equal(3, reconstructed.Value);
        Assert.Equal(DataGridLengthUnitType.Star, reconstructed.UnitType);
        Assert.True(double.IsNaN(reconstructed.DesiredValue));
        Assert.True(double.IsNaN(reconstructed.DisplayValue));
        Assert.Throws<NotSupportedException>(() => converter.ConvertTo(original, typeof(double)));
        Assert.Throws<ArgumentNullException>(() => converter.ConvertTo(null, null, original, null!));
    }

    private static void AssertLength(object value, DataGridLengthUnitType expectedUnit, double expectedValue)
    {
        var length = Assert.IsType<DataGridLength>(value);
        Assert.Equal(expectedUnit, length.UnitType);
        Assert.Equal(expectedValue, length.Value, 10);
    }

    private static MethodInfo GetDeclaredMethod(Type type, string name, params Type[] parameterTypes)
    {
        return type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null)!;
    }
}
