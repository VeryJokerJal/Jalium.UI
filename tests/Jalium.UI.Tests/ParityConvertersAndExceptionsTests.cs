using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;

#pragma warning disable WPF0001 // ThemeMode and its converter intentionally mirror the experimental WPF API.

namespace Jalium.UI.Tests;

public class ParityConvertersAndExceptionsTests
{
    [Fact]
    public void NullableBoolConverter_ProvidesExclusiveStandardValues()
    {
        var converter = new NullableBoolConverter();

        Assert.True(converter.GetStandardValuesSupported());
        Assert.True(converter.GetStandardValuesExclusive());
        Assert.Equal(
            new bool?[] { true, false, null },
            converter.GetStandardValues()!.Cast<bool?>());
        Assert.Same(converter.GetStandardValues(), converter.GetStandardValues());
    }

    [Fact]
    public void RectConverter_RoundTripsInvariantStringAndSupportsEmpty()
    {
        var converter = new RectConverter();
        var value = new Rect(1.25, -2.5, 30.75, 40);

        string text = Assert.IsType<string>(
            converter.ConvertTo(null, CultureInfo.InvariantCulture, value, typeof(string)));

        Assert.Equal("1.25,-2.5,30.75,40", text);
        Assert.Equal(value, Assert.IsType<Rect>(
            converter.ConvertFrom(null, CultureInfo.InvariantCulture, text)));
        Assert.Equal(Rect.Empty, Assert.IsType<Rect>(converter.ConvertFrom("Empty")));
        Assert.Equal("Empty", converter.ConvertTo(Rect.Empty, typeof(string)));
        Assert.Throws<ArgumentException>(() => converter.ConvertFrom("1,2,-3,4"));
        Assert.Throws<InvalidOperationException>(() => converter.ConvertFrom("1,2,3"));
        Assert.Throws<InvalidOperationException>(() => converter.ConvertFrom("1,2,3,4,"));
    }

    [Fact]
    public void RectConverter_UsesCultureAwareNumericSeparatorWhenFormatting()
    {
        var converter = new RectConverter();
        var culture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(
            "1,5;2,5;3,5;4,5",
            converter.ConvertTo(null, culture, new Rect(1.5, 2.5, 3.5, 4.5), typeof(string)));
    }

    [Fact]
    public void ThemeModeConverter_RoundTripsStringAndCreatesInvokableDescriptor()
    {
        var converter = new ThemeModeConverter();
        var value = new ThemeMode("HighContrast");

        string text = Assert.IsType<string>(converter.ConvertTo(value, typeof(string)));
        Assert.Equal("HighContrast", text);
        Assert.Equal(value, Assert.IsType<ThemeMode>(converter.ConvertFrom(text)));

        var descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(value, typeof(InstanceDescriptor)));
        Assert.Equal(value, Assert.IsType<ThemeMode>(descriptor.Invoke()));
    }

    [Fact]
    public void ResourceReferenceKeyNotFoundException_PreservesKeyAndSerializedState()
    {
        var key = new object();
        var original = new ResourceReferenceKeyNotFoundException("Missing resource.", key);

        Assert.Same(key, original.Key);

#pragma warning disable SYSLIB0050 // SerializationInfo requires the legacy formatter converter.
        var info = new SerializationInfo(
            typeof(ResourceReferenceKeyNotFoundException),
            new FormatterConverter());
#pragma warning restore SYSLIB0050
#pragma warning disable SYSLIB0051 // Exercise the WPF-compatible formatter serialization contract.
        original.GetObjectData(info, default);
        var restored = new DeserializationProbeException(info, default);
#pragma warning restore SYSLIB0051

        Assert.Equal(original.Message, restored.Message);
        Assert.Same(key, restored.Key);
    }

    [Fact]
    public void PublicApiSignatures_MatchWpfParityAppendix()
    {
        Assert.Equal(typeof(NullableConverter), typeof(NullableBoolConverter).BaseType);
        Assert.NotNull(typeof(NullableBoolConverter).GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            typeof(TypeConverter.StandardValuesCollection),
            GetDeclaredMethod(
                typeof(NullableBoolConverter),
                nameof(NullableBoolConverter.GetStandardValues),
                typeof(ITypeDescriptorContext)).ReturnType);
        Assert.Equal(
            typeof(bool),
            GetDeclaredMethod(
                typeof(NullableBoolConverter),
                nameof(NullableBoolConverter.GetStandardValuesExclusive),
                typeof(ITypeDescriptorContext)).ReturnType);
        Assert.Equal(
            typeof(bool),
            GetDeclaredMethod(
                typeof(NullableBoolConverter),
                nameof(NullableBoolConverter.GetStandardValuesSupported),
                typeof(ITypeDescriptorContext)).ReturnType);

        Assert.Equal(typeof(TypeConverter), typeof(RectConverter).BaseType);
        Assert.True(typeof(RectConverter).IsSealed);
        Assert.NotNull(typeof(RectConverter).GetConstructor(Type.EmptyTypes));
        AssertConverterMethodSignatures(typeof(RectConverter));

        Assert.Equal(typeof(TypeConverter), typeof(ThemeModeConverter).BaseType);
        Assert.NotNull(typeof(ThemeModeConverter).GetConstructor(Type.EmptyTypes));
        AssertConverterMethodSignatures(typeof(ThemeModeConverter));

        Type exceptionType = typeof(ResourceReferenceKeyNotFoundException);
        Assert.Equal(typeof(InvalidOperationException), exceptionType.BaseType);
        Assert.Contains(
            exceptionType.GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(SerializableAttribute));
        Assert.NotNull(exceptionType.GetConstructor(Type.EmptyTypes));
        Assert.NotNull(exceptionType.GetConstructor([typeof(string), typeof(object)]));
        Assert.NotNull(exceptionType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(SerializationInfo), typeof(StreamingContext)],
            modifiers: null));
        PropertyInfo keyProperty = exceptionType.GetProperty(
            nameof(ResourceReferenceKeyNotFoundException.Key))!;
        Assert.Equal(typeof(object), keyProperty.PropertyType);
        Assert.Null(keyProperty.SetMethod);
        Assert.Equal(
            typeof(void),
            GetDeclaredMethod(
                exceptionType,
                nameof(ResourceReferenceKeyNotFoundException.GetObjectData),
                typeof(SerializationInfo),
                typeof(StreamingContext)).ReturnType);
    }

    private static void AssertConverterMethodSignatures(Type converterType)
    {
        Assert.Equal(
            typeof(bool),
            GetDeclaredMethod(
                converterType,
                nameof(TypeConverter.CanConvertFrom),
                typeof(ITypeDescriptorContext),
                typeof(Type)).ReturnType);
        Assert.Equal(
            typeof(bool),
            GetDeclaredMethod(
                converterType,
                nameof(TypeConverter.CanConvertTo),
                typeof(ITypeDescriptorContext),
                typeof(Type)).ReturnType);
        Assert.Equal(
            typeof(object),
            GetDeclaredMethod(
                converterType,
                nameof(TypeConverter.ConvertFrom),
                typeof(ITypeDescriptorContext),
                typeof(CultureInfo),
                typeof(object)).ReturnType);
        Assert.Equal(
            typeof(object),
            GetDeclaredMethod(
                converterType,
                nameof(TypeConverter.ConvertTo),
                typeof(ITypeDescriptorContext),
                typeof(CultureInfo),
                typeof(object),
                typeof(Type)).ReturnType);
    }

    private static MethodInfo GetDeclaredMethod(
        Type declaringType,
        string name,
        params Type[] parameterTypes)
    {
        return declaringType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null)!;
    }

    private sealed class DeserializationProbeException : ResourceReferenceKeyNotFoundException
    {
#pragma warning disable SYSLIB0051 // Exposes the compatibility constructor for round-trip verification.
        public DeserializationProbeException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#pragma warning restore SYSLIB0051
    }
}

#pragma warning restore WPF0001
