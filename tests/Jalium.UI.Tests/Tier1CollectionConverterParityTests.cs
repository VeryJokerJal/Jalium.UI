using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using Jalium.UI.Ink;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class Tier1CollectionConverterParityTests
{
    [Fact]
    public void ConverterTypes_MatchWpfNamespaceAndSealing()
    {
        Assert.Equal("Jalium.UI", typeof(Int32RectConverter).Namespace);
        Assert.Equal("Jalium.UI", typeof(TextDecorationCollectionConverter).Namespace);
        Assert.Equal("Jalium.UI", typeof(StrokeCollectionConverter).Namespace);
        Assert.True(typeof(Int32RectConverter).IsSealed);
        Assert.True(typeof(TextDecorationCollectionConverter).IsSealed);
        Assert.False(typeof(StrokeCollectionConverter).IsSealed);
    }

    [Fact]
    public void Int32RectConverter_IsRegisteredAndUsesWpfCultureRules()
    {
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(Int32Rect));

        Assert.IsType<Int32RectConverter>(converter);
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.True(converter.CanConvertTo(typeof(string)));
        Assert.False(converter.CanConvertFrom(typeof(int)));
        Assert.False(converter.CanConvertTo(typeof(int)));

        Assert.Equal(
            new Int32Rect(1, -2, 3, 4),
            converter.ConvertFrom(null, CultureInfo.GetCultureInfo("fr-FR"), "1,-2,3,4"));
        Assert.Equal(
            "1;2;3;4",
            converter.ConvertTo(
                null,
                CultureInfo.GetCultureInfo("fr-FR"),
                new Int32Rect(1, 2, 3, 4),
                typeof(string)));
        Assert.Equal("Empty", converter.ConvertToInvariantString(Int32Rect.Empty));
    }

    [Fact]
    public void Int32RectConverter_RejectsUnsupportedValues()
    {
        var converter = new Int32RectConverter();

        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(null!));
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(42));
        Assert.Equal("42", converter.ConvertTo(42, typeof(string)));
        Assert.Throws<NotSupportedException>(
            () => converter.ConvertTo(new Int32Rect(1, 2, 3, 4), typeof(int)));
    }

    [Fact]
    public void TextDecorationConverter_IsRegisteredWithWpfConversionMatrix()
    {
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(TextDecorationCollection));

        Assert.IsType<TextDecorationCollectionConverter>(converter);
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.False(converter.CanConvertFrom(typeof(int)));
        Assert.True(converter.CanConvertTo(typeof(InstanceDescriptor)));
        Assert.False(converter.CanConvertTo(typeof(string)));
    }

    [Fact]
    public void TextDecorationConverter_ParsesPredefinedNamesInSourceOrder()
    {
        TextDecorationCollection result = Assert.IsType<TextDecorationCollection>(
            TextDecorationCollectionConverter.ConvertFromString(
                " underline, OVERLINE, Baseline, strikethrough "));

        Assert.Collection(
            result,
            item => Assert.Same(TextDecorations.Underline[0], item),
            item => Assert.Same(TextDecorations.OverLine[0], item),
            item => Assert.Same(TextDecorations.Baseline[0], item),
            item => Assert.Same(TextDecorations.Strikethrough[0], item));
    }

    [Fact]
    public void TextDecorationConverter_HandlesEmptyAndNone()
    {
        Assert.Null(TextDecorationCollectionConverter.ConvertFromString(null));
        Assert.Empty(TextDecorationCollectionConverter.ConvertFromString(string.Empty)!);
        Assert.Empty(TextDecorationCollectionConverter.ConvertFromString("  None  ")!);

        var converter = new TextDecorationCollectionConverter();
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(null!));
        ArgumentException badType = Assert.Throws<ArgumentException>(() => converter.ConvertFrom(42));
        Assert.Equal("input", badType.ParamName);
    }

    [Theory]
    [InlineData("Underline,Underline")]
    [InlineData("None,Underline")]
    [InlineData(",Underline")]
    [InlineData("Underline,")]
    [InlineData("Unknown")]
    public void TextDecorationConverter_RejectsInvalidOrDuplicateNames(string text)
    {
        Assert.Throws<ArgumentException>(
            () => TextDecorationCollectionConverter.ConvertFromString(text));
    }

    [Fact]
    public void TextDecorationConverter_CreatesExecutableInstanceDescriptor()
    {
        var source = new TextDecorationCollection(
            [TextDecorations.Underline[0], TextDecorations.OverLine[0]]);
        var converter = new TextDecorationCollectionConverter();

        InstanceDescriptor descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(source, typeof(InstanceDescriptor)));
        TextDecorationCollection reconstructed = Assert.IsType<TextDecorationCollection>(
            descriptor.Invoke());

        Assert.NotSame(source, reconstructed);
        Assert.Equal(source.ToArray(), reconstructed.ToArray());
    }

    [Fact]
    public void StrokeCollectionConverter_IsRegisteredWithWpfConversionMatrix()
    {
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(StrokeCollection));

        Assert.IsType<StrokeCollectionConverter>(converter);
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.True(converter.CanConvertTo(typeof(string)));
        Assert.True(converter.CanConvertTo(typeof(InstanceDescriptor)));
        Assert.False(converter.CanConvertFrom(typeof(int)));
        Assert.False(converter.CanConvertTo(typeof(int)));
        Assert.False(converter.GetStandardValuesSupported());
    }

    [Fact]
    public void StrokeCollectionConverter_RoundTripsCanonicalEmptyWpfIsf()
    {
        const string emptyWpfIsf = "AAYCAA8AHwA=";
        var converter = new StrokeCollectionConverter();

        Assert.Empty(Assert.IsType<StrokeCollection>(converter.ConvertFromInvariantString(string.Empty)));
        Assert.Empty(Assert.IsType<StrokeCollection>(converter.ConvertFromInvariantString("   ")));
        Assert.Empty(Assert.IsType<StrokeCollection>(converter.ConvertFromInvariantString(emptyWpfIsf)));
        Assert.Equal(emptyWpfIsf, converter.ConvertToInvariantString(new StrokeCollection()));
    }

    [Fact]
    public void StrokeCollectionConverter_RoundTripsNonEmptyInkWithoutLoss()
    {
        var stroke = new Stroke(
            new StylusPointCollection([new StylusPoint(1, 2, 0.75f)]));
        var source = new StrokeCollection([stroke]);
        var converter = new StrokeCollectionConverter();

        string encoded = Assert.IsType<string>(converter.ConvertToInvariantString(source));
        StrokeCollection restored = Assert.IsType<StrokeCollection>(
            converter.ConvertFromInvariantString(encoded));

        Stroke restoredStroke = Assert.Single(restored);
        Assert.Single(restoredStroke.StylusPoints);
        Assert.Equal(1, restoredStroke.StylusPoints[0].X);
        Assert.Equal(2, restoredStroke.StylusPoints[0].Y);
        Assert.Equal(0.75f, restoredStroke.StylusPoints[0].PressureFactor);
        Assert.Throws<ArgumentException>(() => converter.ConvertFromInvariantString("AA=="));
    }

    [Fact]
    public void StrokeCollectionConverter_CreatesExecutableConstructorDescriptor()
    {
        var stroke = new Stroke(
            new StylusPointCollection([new StylusPoint(1, 2, 0.75f)]));
        var source = new StrokeCollection([stroke]);
        var converter = new StrokeCollectionConverter();

        InstanceDescriptor descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(source, typeof(InstanceDescriptor)));
        StrokeCollection reconstructed = Assert.IsType<StrokeCollection>(descriptor.Invoke());

        Assert.NotSame(source, reconstructed);
        Assert.Single(reconstructed);
        Assert.Same(stroke, reconstructed[0]);
    }
}
